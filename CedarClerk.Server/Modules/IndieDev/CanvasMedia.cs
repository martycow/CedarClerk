using System.Collections.Concurrent;
using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

/// <summary>
/// Whether a file may be read because a shared board draws it.
///
/// <para>A picture on a board is an ordinary library upload: no post claims it, so
/// <c>MediaVisibilityIndex</c> puts it in neither the public set nor the gated one, and the door in
/// front of <c>/media</c> falls through to "are you the owner". On a board that is exactly the
/// wrong question — the whole point of the feature is that somebody else is looking — so an
/// owner-uploaded image was a broken picture for every collaborator.</para>
///
/// <para>A signed grant is not the answer: <see cref="MediaGrant"/> lives fifteen minutes and a
/// board stays open far longer, so the images would blank out mid-session. The board is asked
/// instead, and the ordinary project access decides.</para>
/// </summary>
public sealed class CanvasMediaIndex(IServiceScopeFactory scopes, TimeProvider? time = null)
{
    /// <summary>Same span as the visibility index, for the same reason: a board opens twenty
    /// pictures at once and the answer is the same for all of them.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, Cached> byAsset = new();

    private sealed record Cached(Guid[] Projects, long ExpiresAt);

    /// <summary>
    /// True when this caller can reach one of the projects whose boards display the file. Asked
    /// only after every cheaper answer has already said no, so an anonymous reader never gets here
    /// with anything to gain.
    /// </summary>
    public async Task<bool> MayReadAsync(string ownerId, Guid assetId, string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        var projects = await ProjectsAsync(ownerId, assetId, ct);
        if (projects.Length == 0) return false;

        using var scope = scopes.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        foreach (var projectId in projects)
            if (await ProjectAccessResolver.ResolveAsync(db, projectId, userId, ct) is not null)
                return true;

        return false;
    }

    private async ValueTask<Guid[]> ProjectsAsync(string ownerId, Guid assetId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcTicks;
        if (byAsset.TryGetValue(assetId, out var known) && known.ExpiresAt > now) return known.Projects;

        using var scope = scopes.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var projects = await ProjectsOfAssetAsync(db, ownerId, assetId, ct);
        byAsset[assetId] = new Cached(projects, clock.GetUtcNow().UtcTicks + Lifetime.Ticks);
        return projects;
    }

    /// <summary>
    /// The projects whose boards draw this file. The payload stores the name as written, so the id
    /// is enough to find the items — narrowed to the owner's own rows, which is the only place the
    /// name can appear, against a context that is already unfiltered.
    /// </summary>
    public static Task<Guid[]> ProjectsOfAssetAsync(
        CedarDbContext platformDb, string ownerId, Guid assetId, CancellationToken ct = default)
    {
        var name = "asset_" + assetId.ToString("D");
        return platformDb.CanvasItems.AsNoTracking()
            .Where(i => i.OwnerId == ownerId && i.Kind == CanvasItemKinds.Image && i.Payload.Contains(name))
            .Select(i => i.ProjectId)
            .Distinct()
            .ToArrayAsync(ct);
    }
}
