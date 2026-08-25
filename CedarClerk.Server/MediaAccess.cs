using System.Collections.Concurrent;
using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace CedarClerk.Server;

public enum MediaRefKind { AssetOriginal, AssetDerivative, ChannelAvatar }

public readonly record struct MediaRef(MediaRefKind Kind, Guid Id);

/// <summary>
/// The three shapes a file under the media directory can have, parsed without touching the
/// database. Anything else is a 404 before a query is ever opened; a well-formed name nobody owns
/// costs one query per <see cref="TenantOwnerCache.MissLifetime"/>, not one per request, so neither
/// half of a crawler's guesswork turns <c>/media</c> into a round-trip per guess.
/// </summary>
public static class MediaFileNames
{
    private const string AssetPrefix = "asset_";
    private const string DerivativeSuffix = "_tg";
    private const string ChannelFolder = Bot.ChannelAvatar.Folder + "/";

    public static bool TryParse(string relativePath, out MediaRef reference)
    {
        reference = default;
        if (string.IsNullOrEmpty(relativePath)) return false;
        if (relativePath.Contains("..", StringComparison.Ordinal)) return false;
        if (relativePath.Contains('\\') || relativePath[0] == '/') return false;

        var slash = relativePath.IndexOf('/');
        if (slash >= 0)
        {
            if (relativePath.IndexOf('/', slash + 1) >= 0) return false;
            if (!relativePath.StartsWith(ChannelFolder, StringComparison.Ordinal)) return false;

            var file = relativePath[ChannelFolder.Length..];
            if (!file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Guid.TryParseExact(file[..^".jpg".Length], "D", out var channelId)) return false;

            reference = new MediaRef(MediaRefKind.ChannelAvatar, channelId);
            return true;
        }

        if (!relativePath.StartsWith(AssetPrefix, StringComparison.Ordinal)) return false;

        var dot = relativePath.LastIndexOf('.');
        if (dot <= AssetPrefix.Length) return false;

        var stem = relativePath[AssetPrefix.Length..dot];
        var derivative = stem.EndsWith(DerivativeSuffix, StringComparison.Ordinal);
        if (derivative) stem = stem[..^DerivativeSuffix.Length];
        if (!Guid.TryParseExact(stem, "D", out var assetId)) return false;

        reference = new MediaRef(derivative ? MediaRefKind.AssetDerivative : MediaRefKind.AssetOriginal, assetId);
        return true;
    }
}

/// <summary>
/// Who owns a file. The same cache the Host → owner lookup uses, for the same reason and against
/// the same caller: a page pulls every image on it through here, and a name nobody owns used to
/// cost a fresh context and a fresh SQLite connection on every repeat of the same guess.
/// </summary>
public sealed class MediaOwnerIndex(IServiceScopeFactory scopes, TenantOwnerCache? cache = null)
{
    private readonly TenantOwnerCache owners = cache ?? new TenantOwnerCache();

    public ValueTask<string?> OwnerOfAsync(MediaRef reference, CancellationToken ct = default) =>
        owners.GetAsync(Key(reference), token => LoadAsync(reference, token), ct);

    private static string Key(MediaRef reference) =>
        (reference.Kind == MediaRefKind.ChannelAvatar ? "channel:" : "asset:") + reference.Id.ToString("N");

    private async Task<string?> LoadAsync(MediaRef reference, CancellationToken ct)
    {
        // A platform scope, not the request's own context: under the tenant filter every foreign
        // file would resolve to "no owner", which fails closed and would therefore never be noticed.
        using var scope = scopes.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var ownerId = reference.Kind == MediaRefKind.ChannelAvatar
            ? await db.Channels.Where(c => c.Id == reference.Id).Select(c => c.OwnerId).FirstOrDefaultAsync(ct)
            : await db.Assets.Where(a => a.Id == reference.Id).Select(a => a.OwnerId).FirstOrDefaultAsync(ct);

        return string.IsNullOrEmpty(ownerId) ? null : ownerId;
    }
}

/// <summary>
/// Which of an owner's files a stranger may read, derived from the posts that reference them: a
/// file inherits the audience of the documents it is published in. Referenced by a public post it
/// is public; referenced only by private ones it is theirs, and opens to whoever may open one of
/// them. A file no published post claims — a library upload, a project cover, a document still
/// being written — is not gated, which is what the editor, the showcase pages and a first send to
/// Telegram all read.
///
/// Computed for the whole account in one pass rather than per file: a blog page asks about every
/// image on it at once, and a per-file question would be a scan of the account's documents each
/// time. Held for <see cref="Lifetime"/>, which is also how long publishing a gated post's images
/// takes to open and un-publishing takes to close.
/// </summary>
public sealed class MediaVisibilityIndex(IServiceScopeFactory scopes, TimeProvider? time = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyDictionary<Guid, Guid[]> Nothing = new Dictionary<Guid, Guid[]>();

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Snapshot> byOwner = new(StringComparer.Ordinal);

    private sealed record Snapshot(IReadOnlyDictionary<Guid, Guid[]> Gated, long ExpiresAt);

    /// <summary>The private posts this file belongs to, or null when nothing gates it.</summary>
    public async ValueTask<Guid[]?> GateOfAsync(string ownerId, Guid fileId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcTicks;
        byOwner.TryGetValue(ownerId, out var known);

        if (known is not null && known.ExpiresAt > now)
            return known.Gated.TryGetValue(fileId, out var current) ? current : null;

        // The previous answer keeps being handed out while one caller refreshes it. A page whose
        // images all expire together would otherwise start that pass once per image.
        if (known is not null)
            byOwner[ownerId] = known with { ExpiresAt = now + Lifetime.Ticks };

        var loaded = new Snapshot(await LoadAsync(ownerId, ct), clock.GetUtcNow().UtcTicks + Lifetime.Ticks);
        byOwner[ownerId] = loaded;
        return loaded.Gated.TryGetValue(fileId, out var gate) ? gate : null;
    }

    private async Task<IReadOnlyDictionary<Guid, Guid[]>> LoadAsync(string ownerId, CancellationToken ct)
    {
        using var scope = scopes.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        // Private posts count whether or not they are published. Deriving the gate from published
        // posts alone made un-publishing the act that opened a post's pictures: the post left the
        // index and its files fell through to "nothing claims this".
        var posts = await db.Drafts.AsNoTracking()
            .Where(d => d.OwnerId == ownerId && (d.IsBlogPublished || d.IsPrivate))
            .Select(d => new { d.Id, d.IsPrivate, d.LastTelegramChatId, d.CedarJson })
            .ToListAsync(ct);
        if (posts.Count == 0) return Nothing;

        // A post already sent to a channel counts as public whatever its blog page says: channel
        // history cannot be edited, so its media is out regardless of who may open the article.
        var gates = posts.ToDictionary(p => p.Id, p => p.IsPrivate && p.LastTelegramChatId is null);

        var open = new HashSet<Guid>();
        var gated = new Dictionary<Guid, HashSet<Guid>>();

        void Fold(Guid draftId, string cedarJson)
        {
            foreach (var name in CedarPackage.FindReferencedMediaPathsSafe(cedarJson))
            {
                if (!MediaFileNames.TryParse(name, out var reference)) continue;

                // Keyed by the asset, not by the file: a Telegram-safe derivative is the same
                // picture and must not be the way around its post's gate.
                if (!gates[draftId]) open.Add(reference.Id);
                else if (gated.TryGetValue(reference.Id, out var drafts)) drafts.Add(draftId);
                else gated[reference.Id] = [draftId];
            }
        }

        foreach (var post in posts)
            Fold(post.Id, post.CedarJson);

        // A translated page is the same post for a different reader and may carry media the
        // primary language does not.
        var ids = posts.Select(p => p.Id).ToList();
        var translations = await db.DraftTranslations.AsNoTracking()
            .Where(t => ids.Contains(t.DraftId))
            .Select(t => new { t.DraftId, t.CedarJson })
            .ToListAsync(ct);
        foreach (var translation in translations)
            if (gates.ContainsKey(translation.DraftId))
                Fold(translation.DraftId, translation.CedarJson);

        foreach (var id in open) gated.Remove(id);
        return gated.Count == 0 ? Nothing : gated.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}

/// <summary>
/// The door in front of the media directory. Files are not laid out per owner — moving 489 of them
/// would mean rewriting every stored reference, including the revision bodies the stale-publish
/// guard fingerprints and channel history that cannot be edited — so the check is made here.
///
/// Two questions, and both are asked on every host:
/// <list type="bullet">
/// <item>a host that renders a blog serves that blog's owner's files and nothing else. The
/// application host belongs to no blog and is the origin the server advertises to Telegram's
/// fetcher and to OG scrapers, so it is not narrowed to one account;</item>
/// <item>a file gated by a private post needs the same signed grant its page needs, or the owner's
/// own session. This one holds everywhere, including the application host — otherwise the gate is
/// a property of which name a reader typed.</item>
/// </list>
/// </summary>
public sealed class MediaOwnershipMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TenantProvider blog, MediaOwnerIndex owners,
        MediaVisibilityIndex visibility, PrivateAccess access)
    {
        if (!ctx.Request.Path.StartsWithSegments(MediaAccessExtensions.Prefix, out var remainder))
        {
            await next(ctx);
            return;
        }

        // 404 rather than 403 throughout: a 403 confirms the file exists for somebody else.
        if (!MediaFileNames.TryParse(remainder.Value?.TrimStart('/') ?? "", out var reference))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var ownerId = await owners.OwnerOfAsync(reference, ctx.RequestAborted);
        if (ownerId is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Set by TenantScopeMiddleware for a subdomain and for the legacy blog host alike, which is
        // why this needs no host string of its own and no second lookup.
        if (blog.TenantId is { } blogOwner && blogOwner != ownerId)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var gate = await visibility.GateOfAsync(ownerId, reference.Id, ctx.RequestAborted);
        if (gate is not null)
        {
            if (!HasGrant(ctx, access, gate) && !await IsOwnerAsync(ctx, ownerId))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // The edge caches by URL, and this one is answered differently for two readers.
            ctx.Response.Headers.CacheControl = "private, no-store";
        }

        await next(ctx);
    }

    private static bool HasGrant(HttpContext ctx, PrivateAccess access, Guid[] posts) =>
        posts.Any(id => access.IsValid(ctx.Request.Cookies[PrivateAccess.CookieName(id)], id, out _));

    /// <summary>
    /// The editor asks for these files from the application host, where authentication runs after
    /// this middleware — so the cookie is read here, and only for a file something gates.
    /// </summary>
    private static async Task<bool> IsOwnerAsync(HttpContext ctx, string ownerId)
    {
        var result = await ctx.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return result.Succeeded
               && result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) == ownerId;
    }
}

public static class MediaAccessExtensions
{
    public const string Prefix = "/media";

    /// <summary>
    /// The access check in front of the ordinary static-file middleware, which keeps range
    /// requests, ETags and conditional GETs exactly as they were.
    /// </summary>
    public static IApplicationBuilder UseTenantMedia(this IApplicationBuilder app, string mediaDir) =>
        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments(Prefix), media =>
        {
            media.UseMiddleware<MediaOwnershipMiddleware>();
            media.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(mediaDir),
                RequestPath = Prefix
            });
        });
}
