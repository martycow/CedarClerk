using System.Net;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// The page a stranger sees first (T-009), drawn on the bench (ADR-215).
///
/// **Server-rendered, not an Angular route**, for the same reason the blog is: this is the one page
/// whose job is to be found and to be read by someone who has never heard of the product, and
/// making them download the application bundle to read a headline is the wrong trade. It is also
/// the only page that needs to be indexable.
///
/// **Shown at `/` only to visitors who are not signed in.** An author who types the address wants
/// their drafts, not a sales page — so a request carrying an Identity cookie falls through to the
/// SPA exactly as before. `/welcome` is the same page for everyone, cookie or not: the stable way
/// back to the front door once the root has been claimed by the app.
///
/// **Every number comes from the code that enforces it** (<see cref="PlanLimitations"/>,
/// <see cref="Consts.Plans"/>, <see cref="Languages.ContentLanguages"/>). A hand-written price table
/// is wrong the first time a limit moves, and a pricing page that lies is worse than none. What is
/// genuinely the maintainer's to write — the headline, the note, the screenshots, the roadmap, the
/// story — is the <see cref="LandingDocument"/> (ADR-323), edited in the admin panel and drawn by
/// <see cref="LandingRenderer"/>.
/// </summary>
public static class LandingEndpoints
{
    /// <summary>Identity's cookie name — the cheapest honest signal that somebody is signed in.</summary>
    private const string AuthCookie = ".AspNetCore.Identity.Application";

    /// <summary>
    /// Middleware rather than a mapped endpoint, so a signed-in request simply carries on into the
    /// static-file pipeline that already serves the app. An endpoint would have to know where
    /// index.html lives and hand it back itself — which is the SPA host's job, not this page's.
    /// </summary>
    public static void UseLanding(this IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            var isRoot = ctx.Request.Path == "/" || ctx.Request.Path == "";
            // /welcome is the landing regardless of the cookie: a signed-in author sometimes wants
            // to see the front door too, and "/" is already spoken for by their drafts.
            var isWelcome = ctx.Request.Path == "/welcome";
            if (!(isRoot || isWelcome) || TenantRouting.IsTenantRequest(ctx)
                || ctx.Request.Method != HttpMethods.Get
                || (isRoot && ctx.Request.Cookies.ContainsKey(AuthCookie)))
            {
                await next();
                return;
            }

            // ADR-243 — the preview is the same consent-filtered cross-account pool as Discovery.
            ctx.RequestServices.GetRequiredService<TenantProvider>().UsePlatform();
            var db = ctx.RequestServices.GetRequiredService<CedarDbContext>();
            var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var document = await LandingDocument.LoadAsync(db, cfg);
            var discovery = await DiscoveryEndpoints.LoadAsync(db, cfg);
            var language = LandingRenderer.ChooseLanguage(
                ctx.Request.Query["lang"].ToString(), ctx.Request.Headers.AcceptLanguage.ToString(), document.Languages);

            ctx.Response.ContentType = "text/html; charset=utf-8";
            // A marketing page is worth caching at the edge, but not for long: it carries prices.
            ctx.Response.Headers.CacheControl = "public, max-age=300";
            // Without this, one visitor's language is served to the next from the edge cache. The
            // ?lang= override needs no Vary — it is part of the URL, which the cache keys on.
            ctx.Response.Headers.Vary = "Accept-Language";

            var analyticsKey = cfg.GetValue(Consts.Analytics.EnabledCfg, false)
                ? cfg[Consts.Analytics.ProjectKeyCfg]
                : null;
            await ctx.Response.WriteAsync(LandingRenderer.Render(language, document, discovery,
                cfg[Consts.General.ShowcaseBlogCfg],
                string.IsNullOrWhiteSpace(analyticsKey) ? null : analyticsKey,
                cfg[Consts.Analytics.HostCfg] ?? Consts.Analytics.DefaultHost));
        });
    }

    /// <summary>
    /// The screenshots the admin uploaded, at <see cref="LandingContent.ShotUrlPrefix"/>.
    ///
    /// Public and unauthenticated, like everything else on this page — and deliberately not part of
    /// <c>/media/*</c>, which is owner-scoped and asks who is looking on every request. These files
    /// belong to the product rather than to an account, and there is nobody to scope them to.
    /// </summary>
    public static IApplicationBuilder UseLandingMedia(this IApplicationBuilder app, string dir)
    {
        // The provider refuses to construct over a directory that is not there, and on a fresh
        // install nothing has been uploaded yet.
        Directory.CreateDirectory(dir);
        return app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments(LandingContent.ShotUrlPrefix.TrimEnd('/'))
                   && !TenantRouting.IsTenantRequest(ctx),
            landing => landing.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dir),
                RequestPath = LandingContent.ShotUrlPrefix.TrimEnd('/'),
                OnPrepareResponse = ctx =>
                    // The name carries a GUID, so the bytes behind one never change: a long cache
                    // is safe, and replacing a screenshot means a new name, not a stale copy.
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=604800, immutable",
            }));
    }

    /// <summary>
    /// T-154 (ADR-135) — the waitlist behind the landing's primary CTA. Anonymous by design; the
    /// honeypot field stands in for a captcha, and a repeat signup answers OK rather than an error:
    /// the visitor's goal is to be on the list, and they are.
    /// </summary>
    public static void MapWaitlistEndpoint(this WebApplication app)
    {
        app.MapPost("/api/waitlist", async (WaitlistRequest req, CedarDbContext db) =>
        {
            if (!string.IsNullOrEmpty(req.Website)) return Results.Ok(); // a bot filled the invisible field
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            if (email.Length is < 5 or > 254 || email.Count(c => c == '@') != 1
                || !email.Contains('.', StringComparison.Ordinal) || email.EndsWith('@'))
                return Results.BadRequest(new { error = ErrorMessages.WaitlistEmailInvalid });

            if (!await db.WaitlistEntries.AnyAsync(w => w.Email == email))
            {
                db.WaitlistEntries.Add(new WaitlistEntry
                {
                    Email = email,
                    Language = req.Language == "ru" ? "ru" : "en",
                });
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });
    }

    public record WaitlistRequest(string? Email, string? Language, string? Website);

}
