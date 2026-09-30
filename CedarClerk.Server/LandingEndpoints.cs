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
/// story — is <see cref="LandingContent"/>, edited in the admin panel.
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

            var ru = ChooseRussian(ctx);
            // ADR-243 — the preview is the same consent-filtered cross-account pool as Discovery.
            ctx.RequestServices.GetRequiredService<TenantProvider>().UsePlatform();
            var db = ctx.RequestServices.GetRequiredService<CedarDbContext>();
            var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var content = await LandingContent.LoadAsync(db, cfg);
            var discovery = await DiscoveryEndpoints.LoadAsync(db, cfg);

            ctx.Response.ContentType = "text/html; charset=utf-8";
            // A marketing page is worth caching at the edge, but not for long: it carries prices.
            ctx.Response.Headers.CacheControl = "public, max-age=300";
            // Without this, one visitor's language is served to the next from the edge cache. The
            // ?lang= override needs no Vary — it is part of the URL, which the cache keys on.
            ctx.Response.Headers.Vary = "Accept-Language";

            var analyticsKey = cfg.GetValue(Consts.Analytics.EnabledCfg, false)
                ? cfg[Consts.Analytics.ProjectKeyCfg]
                : null;
            await ctx.Response.WriteAsync(Render(ru, content, discovery,
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
    /// The switch in the rail wins, then the browser. English is the floor (ADR-135) — the market
    /// this page sells to is English-speaking, and the old Russian-first rule dated from when the
    /// author was the audience.
    /// </summary>
    private static bool ChooseRussian(HttpContext ctx) => ctx.Request.Query["lang"].ToString() switch
    {
        "ru" => true,
        "en" => false,
        _ => PrefersRussian(ctx.Request.Headers.AcceptLanguage.ToString()),
    };

    private static bool PrefersRussian(string acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return false;
        foreach (var entry in acceptLanguage.Split(','))
        {
            var code = entry.Split(';')[0].Trim().ToLowerInvariant();
            if (code.StartsWith("ru")) return true;
            if (code.Length >= 2) return false;
        }
        return false;
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

    private static string Gb(PlanTiers tier) =>
        (PlanLimitations.StorageLimitBytes(tier) / (1024.0 * 1024 * 1024)) is var gb && gb >= 1
            ? $"{gb:0.#} GB"
            : $"{PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024)} MB";

    private static string E(string value) => WebUtility.HtmlEncode(value);

    /// <summary>The wordmark's conifer, the one drawing on this page that is not a Phosphor glyph.</summary>
    private static string Mark(int size) =>
        $"""<img src="/assets/brand/cedar-clerk-mark.svg" width="{size}" height="{size}" alt="" style="display:block;flex:none;object-fit:contain">""";

    // A plain (non-interpolated) raw string: CSS is mostly braces, and in an interpolated raw
    // string every one of them would have to be doubled. Same Replace-a-placeholder shape as
    // BlogEndpoints' shell, for the same reason.
    private const string Css = """
            :root { color-scheme: light; {{LIGHT_TOKENS}} }
            @media (prefers-color-scheme: dark) { :root { {{DARK_TOKENS}} } }
            {{FONT_FACES}}
            * { box-sizing: border-box; }
            html { scroll-behavior: smooth; scroll-padding-top: 100px; }
            body { margin: 0; background: var(--sheet); color: var(--text); font: var(--fs-19)/1.5 var(--font-sans); }
            h1, h2, h3, p, figure { margin: 0; }
            h1, h2, h3 { font-family: var(--font-display); line-height: 1.12; font-weight: 600; }
            h2 { font-size: clamp(var(--fs-27), 3vw, calc(var(--fs-21) * 2)); letter-spacing: -.02em; }
            h3 { font-size: var(--fs-27); }
            a { color: var(--accent); }
            img { display: block; max-width: 100%; height: auto; }
            button, input { font: inherit; }
            button, summary { cursor: pointer; }
            :is(a, button, input, summary):focus-visible { outline: 3px solid var(--accent); outline-offset: 4px; }
            .wrap { max-width: 1280px; margin: auto; padding: 0 var(--space-5); }
            .spacer { flex: 1; }
            .rail { display: flex; align-items: center; gap: var(--space-4); min-height: 78px; padding: var(--space-3) var(--space-6); border-bottom: 1px solid var(--border); background: var(--sheet); }
            .brand { display: inline-flex; align-items: center; gap: var(--space-2); color: var(--text); text-decoration: none; flex-shrink: 0; }
            .rail-name { font: 600 var(--fs-27) var(--font-display); white-space: nowrap; }
            .rail-chip { font-size: var(--fs-13); color: var(--t2); }
            .rail-nav { display: flex; gap: var(--space-5); }
            .rail-nav a { font-size: var(--fs-16); text-decoration: none; color: var(--text); }
            .rail-nav a:hover { text-decoration: underline; }
            .lang { display: flex; gap: var(--space-1); }
            .lang a { padding: var(--space-2); font-size: var(--fs-13); text-decoration: none; color: var(--text); border-radius: var(--radius-sm); }
            .lang [aria-current] { background: var(--asoft); font-weight: 700; }
            .btn { display: inline-flex; justify-content: center; align-items: center; gap: var(--space-2); min-height: 48px; padding: var(--space-3) var(--space-5); border: 1px solid var(--accent); border-radius: var(--radius-md); font: 600 var(--fs-17)/1.2 var(--font-sans); text-decoration: none; cursor: pointer; }
            .btn-sm { min-height: 44px; padding: var(--space-2) var(--space-4); font-size: var(--fs-15); }
            .btn-pine { background: var(--accent); color: var(--text-on-pine); }
            .btn-pine:hover { background: var(--pine-deep); }
            .btn-paper { color: var(--text); background: var(--sheet); border-color: var(--border-strong); }
            .btn[disabled] { opacity: .6; cursor: wait; }
            .mobile-menu { display: none; }
            .hero { padding: 70px 0 var(--space-8); text-align: center; }
            .kicker-row { margin-bottom: var(--space-4); }
            .stamp { font: 600 var(--fs-13) var(--font-sans); letter-spacing: .08em; text-transform: uppercase; color: var(--accent); }
            h1 { font-size: clamp(var(--fs-34), 5.3vw, calc(var(--fs-34) * 2.2)); letter-spacing: -.045em; line-height: 1.04; max-width: 1100px; margin: auto; overflow-wrap: anywhere; }
            .hero-sub { max-width: 760px; margin: var(--space-5) auto var(--space-6); font-size: var(--fs-27); line-height: 1.4; color: var(--t2); }
            .wait-wrap { max-width: 660px; margin: auto; }
            .waitlist { display: flex; gap: var(--space-3); }
            .waitlist input[type=email] { width: 100%; min-width: 0; flex: 1; padding: var(--space-3) var(--space-4); min-height: 54px; background: var(--paper-bright); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); }
            .waitlist input::placeholder { color: var(--t2); }
            .waitlist .btn { flex-shrink: 0; }
            .hp { position: absolute; left: -10000px; width: 1px; height: 1px; }
            .drop { margin-top: var(--space-3); font-size: var(--fs-15); color: var(--t2); }
            .proof, .note { margin-top: var(--space-3); font: var(--fs-16)/1.5 var(--font-sans); color: var(--t2); }
            .waitlist-done { padding: var(--space-4); background: var(--asoft); color: var(--text); border-radius: var(--radius-md); }
            .hero-shot { max-width: 1120px; margin: var(--space-8) auto 0; background: var(--paper-bright); border: 1px solid var(--border); border-radius: var(--radius-md); overflow: hidden; box-shadow: var(--shadow-paper); }
            .hero-shot img { width: 100%; }
            .cap { padding: var(--space-3) var(--space-4); font-size: var(--fs-15); color: var(--t2); }
            section { padding: var(--space-8) 0; }
            .section-heading { text-align: center; margin-bottom: var(--space-6); }
            .section-heading p { margin-top: var(--space-3); color: var(--t2); font-size: var(--fs-21); }
            .workflow { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--space-8); list-style: none; margin: 0; padding: 0; }
            .workflow li { display: flex; gap: var(--space-4); }
            .step-number { flex-shrink: 0; display: grid; place-items: center; width: 48px; height: 48px; border-radius: 50%; background: var(--asoft); color: var(--accent); font: 600 var(--fs-27) var(--font-display); }
            .workflow h3 { font-size: var(--fs-27); margin-bottom: var(--space-2); }
            .workflow p { color: var(--t2); font-size: var(--fs-19); }
            .network-strip { display: flex; justify-content: center; align-items: center; flex-wrap: wrap; gap: var(--space-4) var(--space-5); padding: var(--space-5) 0; margin-top: var(--space-6); border-top: 1px solid var(--border); font-size: var(--fs-16); color: var(--t2); }
            .network-strip b { color: var(--text); }
            .examples { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); align-items: center; gap: var(--space-8); padding: var(--space-8); background: var(--alt); border-radius: var(--radius-md); }
            .example-copy p { margin: var(--space-5) 0; color: var(--t2); font-size: var(--fs-21); }
            .example-copy a { font-weight: 600; text-underline-offset: 4px; }
            .gallery { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-4); }
            .gallery figure { background: var(--paper-bright); border: 1px solid var(--border); border-radius: var(--radius-md); overflow: hidden; }
            .gallery figure:only-child { grid-column: 1/-1; }
            .gallery img { width: 100%; }
            .gallery a { display: block; }
            .benefits { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--space-6); background: var(--asoft); padding: var(--space-6); margin-top: var(--space-8); border-radius: var(--radius-md); }
            .benefit { display: flex; align-items: flex-start; gap: var(--space-4); }
            .benefit svg { flex-shrink: 0; color: var(--accent); }
            .benefit h3 { font-size: var(--fs-21); }
            .benefit p { margin-top: var(--space-2); font-size: var(--fs-16); color: var(--t2); }
            .tools { margin-top: var(--space-6); border-bottom: 1px solid var(--border); }
            .tools>summary, .comparison-toggle>summary { font-weight: 600; padding: var(--space-4) 0; }
            .features { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-4); margin: var(--space-4) 0 var(--space-6); align-items: start; }
            .feature { padding: var(--space-4); border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--paper-bright); }
            .feature-head { display: flex; align-items: center; gap: var(--space-3); min-height: 44px; list-style: none; }
            .feature-head b { flex: 1; }
            .feature-head svg { flex-shrink: 0; color: var(--accent); }
            .feature-detail { padding-top: var(--space-4); font-size: var(--fs-17); }
            .feature-detail img { margin-top: var(--space-4); width: 100%; }
            .plans { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--space-5); align-items: stretch; }
            .plan { padding: var(--space-6); background: var(--paper-bright); border: 1px solid var(--border); border-radius: var(--radius-md); }
            .plan-head b { font: 600 var(--fs-27) var(--font-display); }
            .plan-price { display: flex; align-items: baseline; gap: var(--space-3); margin: var(--space-4) 0; }
            .plan-price .n { font: 600 calc(var(--fs-27) * 2)/1 var(--font-display); }
            .plan-price .per, .plan-for { color: var(--t2); font-size: var(--fs-17); }
            .plan ul { padding: 0; margin: var(--space-5) 0 0; list-style: none; }
            .plan li { display: flex; gap: var(--space-3); margin-top: var(--space-3); font-size: var(--fs-17); }
            .plan li svg { color: var(--accent); flex-shrink: 0; margin-top: var(--space-1); }
            .plan-foot { text-align: center; margin-top: var(--space-5); font-size: var(--fs-16); color: var(--t2); }
            .plan-comparison { overflow-x: auto; color: var(--text); }
            .plan-comparison table { width: 100%; border-collapse: collapse; background: var(--paper-bright); }
            .plan-comparison caption { text-align: left; padding: var(--space-3) 0; }
            .plan-comparison th, .plan-comparison td { text-align: left; padding: var(--space-4); border-bottom: 1px solid var(--border); color: var(--text); font-size: var(--fs-16); }
            .faq h2 { margin-bottom: var(--space-5); }
            .faq details { border-bottom: 1px solid var(--border); padding: var(--space-4) 0; }
            .faq summary { font-weight: 600; }
            .faq p { max-width: 75ch; color: var(--t2); margin-top: var(--space-3); }
            .rule { display: flex; align-items: baseline; gap: var(--space-4); flex-wrap: wrap; margin-bottom: var(--space-5); }
            .meta, .when, .download-meta { color: var(--t2); font-size: var(--fs-15); }
            .shelves { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--space-5); }
            .shelf { background: var(--paper-bright); padding: var(--space-5); border: 1px solid var(--border); border-radius: var(--radius-md); }
            .shelf-head { display: flex; gap: var(--space-3); font-weight: 600; margin-bottom: var(--space-4); }
            .shelf-sheet ul { padding: 0; list-style: none; }
            .shelf-sheet li { display: flex; gap: var(--space-2); margin-top: var(--space-3); }
            .mark-done, .mark-doing, .mark-next { color: var(--accent); }
            .story { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: var(--space-8); }
            .step { border-left: 2px solid var(--border); padding-left: var(--space-5); margin-bottom: var(--space-5); }
            .step b { display: block; margin: var(--space-2) 0; font: 600 var(--fs-21) var(--font-display); }
            .download-card { display: flex; flex-wrap: wrap; gap: var(--space-5); align-items: center; padding: var(--space-6); background: var(--alt); border-radius: var(--radius-md); }
            .download-card .lead { flex: 1; min-width: min(280px, 100%); }
            .download-card p { margin: var(--space-3) 0; }
            .band { text-align: center; background: var(--pine-deep); color: var(--text-on-pine); margin-top: var(--space-8); padding: var(--space-8) var(--space-5); }
            .band p { margin: var(--space-4) auto var(--space-5); max-width: 65ch; font-size: var(--fs-21); }
            .band .btn { background: var(--paper-bright); color: var(--text); border-color: var(--paper-bright); }
            .ruler { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-5); padding: var(--space-5) var(--space-6); font-size: var(--fs-15); }
            .ruler .label { font: 600 var(--fs-21) var(--font-display); }
            .discover-preview { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: var(--space-8); margin-top: var(--space-8); padding: var(--space-6); background: var(--alt); border-radius: var(--radius-md); }
            .discover-copy h2 { margin: var(--space-3) 0; }
            .discover-copy p { margin-bottom: var(--space-5); color: var(--t2); }
            .discover-minis { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-4); }
            .discover-mini { display: block; border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--paper-bright); padding: var(--space-3); text-decoration: none; color: var(--text); }
            .discover-mini img { width: 100%; aspect-ratio: 1.6; object-fit: cover; border-radius: var(--radius-sm); }
            .discover-mini :is(small, b, em) { display: block; margin-top: var(--space-2); }
            .discover-mini b { font: 600 var(--fs-21) var(--font-display); }
            .discover-mini :is(small, em) { font: var(--fs-13) var(--font-sans); color: var(--t2); }
            .discover-empty { grid-template-columns: 1fr auto; align-items: center; }
            .consent { position: fixed; z-index: 60; left: var(--space-4); bottom: var(--space-4); width: min(28rem, calc(100vw - 32px)); padding: var(--space-5); color: var(--text); background: var(--paper-bright); border: 1px solid var(--border); border-radius: var(--radius-md); box-shadow: var(--shadow-paper); font-size: var(--fs-16); }
            .consent[hidden] { display: none; }
            .consent h2 { font-size: var(--fs-21); margin-bottom: var(--space-3); }
            .consent-actions { display: flex; justify-content: flex-end; gap: var(--space-3); margin-top: var(--space-4); }
            #waitlist-dialog { width: min(540px, calc(100% - 32px)); padding: var(--space-6); color: var(--text); background: var(--sheet); border: 1px solid var(--border); border-radius: var(--radius-md); }
            #waitlist-dialog h2 { font-size: var(--fs-27); margin: var(--space-3) 0 var(--space-5); }
            #waitlist-dialog::backdrop { background: color-mix(in srgb, var(--text) 55%, transparent); }
            .dialog-close { float: right; min-width: 44px; min-height: 44px; border: 0; background: transparent; color: inherit; cursor: pointer; }
            @media (max-width: 1100px) { .rail-chip { display: none; } .rail-nav { gap: var(--space-3); } .rail { gap: var(--space-3); padding-inline: var(--space-4); } .rail-name { font-size: var(--fs-21); } .examples, .discover-preview { gap: var(--space-5); } .workflow { gap: var(--space-5); } }
            @media (max-width: 800px) {
                .rail { flex-wrap: wrap; } .rail-nav { display: none; } .rail>.btn[data-waitlist] { display: none; }
                .mobile-menu { display: block; position: relative; } .mobile-menu summary { padding: var(--space-2); font-size: var(--fs-16); }
                .mobile-menu nav { position: absolute; z-index: 25; right: 0; width: 220px; padding: var(--space-4); background: var(--paper-bright); border: 1px solid var(--border); border-radius: var(--radius-md); box-shadow: var(--shadow-paper); }
                .mobile-menu a { display: block; padding: var(--space-3); color: var(--text); }
                .hero { padding-top: var(--space-8); } .hero-sub { font-size: var(--fs-21); }
                .workflow { grid-template-columns: 1fr; } .examples, .story, .discover-preview { grid-template-columns: 1fr; }
                .benefits { grid-template-columns: 1fr; } .plans { gap: var(--space-3); } .plan { padding: var(--space-4); }
                .plan-price .n { font-size: calc(var(--fs-21) * 2); } .plan-head b { font-size: var(--fs-21); }
            }
            @media (max-width: 540px) {
                .wrap { padding-inline: var(--space-4); } .rail { gap: var(--space-2); } .login-link { display: none; }
                .rail-name { font-size: var(--fs-21); } .lang a { min-height: 44px; display: grid; place-items: center; }
                h1 { font-size: calc(var(--fs-21) * 2); } .hero-sub { font-size: var(--fs-21); }
                .waitlist { flex-direction: column; } .hero-shot { margin-top: var(--space-6); }
                .examples { padding: var(--space-5); } .gallery, .plans, .shelves, .features { grid-template-columns: 1fr; }
                .benefits { padding: var(--space-5); } .plan { padding: var(--space-5); }
                .discover-minis { grid-template-columns: 1fr; } .discover-empty { grid-template-columns: 1fr; }
                .ruler { gap: var(--space-4); padding: var(--space-5) var(--space-4); }
                .plan-comparison th, .plan-comparison td { padding: var(--space-2); }
            }
            @media (prefers-reduced-motion: reduce) { html { scroll-behavior: auto; } }
        """;

    // Plain raw string with placeholders, like Css above: JS is as brace-heavy as CSS, and inside
    // an interpolated raw string every one of those braces would need escaping.
    private const string WaitlistJs = """
        document.querySelectorAll('.mobile-menu a').forEach(link => link.addEventListener('click', () => link.closest('details').open = false));
        for (const card of document.querySelectorAll('.feature')) {
            card.addEventListener('pointerenter', event => {
                if (event.pointerType === 'mouse' && !card.open) { card.open = true; card.dataset.hover = 'true'; }
            });
            card.addEventListener('pointerleave', () => {
                if (card.dataset.hover && !card.contains(document.activeElement)) card.open = false;
                delete card.dataset.hover;
            });
            card.querySelector('summary').addEventListener('click', event => {
                if (card.dataset.hover) { event.preventDefault(); delete card.dataset.hover; }
            });
        }
        const dialog = document.getElementById('waitlist-dialog');
        const original = document.getElementById('waitlist-form');
        if (dialog && original) {
            const copy = original.cloneNode(true);
            copy.id = 'waitlist-modal-form';
            copy.querySelector('input[type=email]').setAttribute('aria-describedby', 'waitlist-modal-note');
            const note = document.getElementById('waitlist-note').cloneNode(true);
            note.id = 'waitlist-modal-note';
            note.setAttribute('role', 'status');
            document.getElementById('waitlist-dialog-content').append(copy, note);
            document.querySelectorAll('[data-waitlist]').forEach(link => link.addEventListener('click', event => {
                event.preventDefault(); dialog.showModal(); copy.querySelector('input[type=email]')?.focus();
            }));
        }
        document.querySelectorAll('form.waitlist').forEach(form => form.addEventListener('submit', async e => {
            e.preventDefault();
            const form = e.target, note = form.nextElementSibling;
            const button = form.querySelector('button[type=submit]');
            if (button.disabled) return;
            button.disabled = true;
            const body = { email: form.email.value, website: form.website.value, language: '%%LANG%%' };
            try {
                const res = await fetch('/api/waitlist', { method: 'POST',
                    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
                if (res.ok) {
                    form.replaceWith(Object.assign(document.createElement('p'),
                        { className: 'waitlist-done', role: 'status', textContent: '%%DONE%%' }));
                    note.remove();
                } else {
                    note.textContent = (await res.json()).error || note.textContent;
                }
            } catch {
                note.textContent = '%%FAIL%%';
            } finally { button.disabled = false; }
        }));
        """;

    private static string WaitlistScript(bool ru) => WaitlistJs
        .Replace("%%LANG%%", ru ? "ru" : "en")
        .Replace("%%DONE%%", LandingTexts.WaitlistSuccess(ru))
        .Replace("%%FAIL%%", LandingTexts.WaitlistFailure(ru));

    /// <summary>
    /// The consent gate and the provider loader, as one block (T-153, ADR-236).
    ///
    /// The decision is made in the browser rather than here, and that is not a preference: this page
    /// is served with <c>Cache-Control: public, max-age=300</c>, so a server-rendered answer would
    /// hand one visitor's choice to the next from the edge cache. Reading the cookie in the page
    /// keeps the document identical for everyone and still correct for each of them.
    ///
    /// The banner ships in the markup and starts hidden, so nothing is injected after paint; the
    /// script only decides whether to show it. Nothing from the provider is fetched until the answer
    /// is yes — the loader is what an accepted consent buys, not something opted out of afterwards.
    /// </summary>
    private static string ConsentBlock(bool ru, string key, string host)
    {

        var title = E(LandingTexts.ConsentTitle(ru));
        var body = E(LandingTexts.ConsentDescription(ru));
        var accept = E(LandingTexts.Accept(ru));
        var decline = E(LandingTexts.Decline(ru));
        var privacy = E(LandingTexts.PrivacyPolicy(ru));

        return $$"""
            <div class="consent" id="consent" hidden>
              <h2>{{title}}</h2>
              <p>{{body}}</p>
              <p><a href="/privacy">{{privacy}}</a></p>
              <div class="consent-actions">
                <button class="btn btn-paper btn-sm" id="consent-no">{{decline}}</button>
                <button class="btn btn-pine btn-sm" id="consent-yes">{{accept}}</button>
              </div>
            </div>
            <script>
            (function () {
              var COOKIE = '{{Consts.General.ConsentCookie}}';
              var KEY = '{{key}}', HOST = '{{host}}';
              function answer() {
                var m = document.cookie.match(new RegExp('(?:^|; )' + COOKIE + '=([^;]*)'));
                return m ? m[1] : '';
              }
              function remember(value) {
                var secure = location.protocol === 'https:' ? '; Secure' : '';
                document.cookie = COOKIE + '=' + value + '; path=/; max-age=31536000; SameSite=Lax' + secure;
              }
              function load() {
                var s = document.createElement('script');
                s.src = HOST + '/static/array.js';
                s.onload = function () {
                  window.posthog.init(KEY, { api_host: HOST, defaults: '2025-05-24' });
                };
                document.head.appendChild(s);
              }
              var current = answer();
              if (current === '{{Consts.General.ConsentGranted}}') { load(); return; }
              if (current === '{{Consts.General.ConsentDenied}}') return;

              var banner = document.getElementById('consent');
              banner.hidden = false;
              document.getElementById('consent-yes').addEventListener('click', function () {
                remember('{{Consts.General.ConsentGranted}}');
                banner.hidden = true;
                load();
              });
              document.getElementById('consent-no').addEventListener('click', function () {
                remember('{{Consts.General.ConsentDenied}}');
                banner.hidden = true;
              });
            })();
            </script>
            """;
    }

    private static string Render(bool ru, LandingContent c, DiscoveryEndpoints.Snapshot discovery,
        string? analyticsKey, string analyticsHost)
    {

        var languageCount = Languages.ContentLanguages.Count;
        var networkCount = PublishNetworks.All.Count;

        var title = LandingTexts.PageTitle(ru);
        var description = c.HeroSub.Pick(ru);

        // Stated as what the product does, not as adjectives about it, and in the devlog-first
        // order the positioning sells (ADR-135): write → reach → keep → plan.
        var features = new (string Icon, string Title, string Body)[]
        {
            ("folder-open", LandingTexts.ProjectsTool.Pick(ru), LandingTexts.ProjectsToolBody.Pick(ru)),
            ("book-open", LandingTexts.GlossaryTool.Pick(ru), LandingTexts.GlossaryToolBody.Pick(ru)),
            ("images", LandingTexts.CanvasTool.Pick(ru), LandingTexts.CanvasToolBody.Pick(ru)),
            ("layout", LandingTexts.PresetsTool.Pick(ru), LandingTexts.PresetsToolBody.Pick(ru)),
            ("pencil-simple", LandingTexts.BlockEditor(ru),
                LandingTexts.EditorDescription(ru)),
            ("paper-plane-tilt", LandingTexts.PublishingToTelegram(ru),
                LandingTexts.TelegramDescription(ru)),
            ("tree-structure", LandingTexts.NetworksTitle(ru, networkCount),
                LandingTexts.NetworksDescription(ru)),
            ("newspaper", LandingTexts.BlogOnYourOwnSubdomain(ru),
                LandingTexts.BlogDescription(ru)),
            ("translate", LandingTexts.LanguagesTitle(ru, languageCount),
                LandingTexts.TranslationDescription(ru)),
            ("chat-teardrop-dots", LandingTexts.CommentsAndReactions(ru),
                LandingTexts.DiscussionDescription(ru)),
            ("kanban", LandingTexts.TasksAndSprints(ru),
                LandingTexts.TasksDescription(ru)),
            ("images", LandingTexts.AnAssetIndex(ru),
                LandingTexts.AssetsDescription(ru)),
            ("cube", LandingTexts.Builds(ru),
                LandingTexts.BuildsDescription(ru)),
            ("timer", LandingTexts.Scheduler(ru),
                LandingTexts.SchedulerDescription(ru)),
            ("eye", LandingTexts.DayAndNight(ru),
                LandingTexts.AppearanceDescription(ru)),
            ("download-simple", LandingTexts.TheTextsStayYours(ru),
                LandingTexts.ExportDescription(ru)),
        };

        var plans = new (string Name, string Price, string Per, string For, string Badge, string Tone, bool Featured, string[] Lines)[]
        {
            (LandingTexts.Free(ru), "$0", LandingTexts.Forever(ru),
                LandingTexts.OneChannelAndYourOwnBlog(ru),
                LandingTexts.Start(ru), "ink", false,
                [
                    LandingTexts.SingleChannelLimit(ru, PlanLimitations.MaxChannels(PlanTiers.Free)),
                    LandingTexts.MediaLimit(ru, Gb(PlanTiers.Free)),
                    LandingTexts.BlogCommentsReactionsRSS(ru),
                    LandingTexts.FreeSignature(ru),
                ]),
            ("Pro", $"${Consts.Plans.ProPrice}", LandingTexts.PerMonth(ru),
                LandingTexts.SeveralChannelsAndYourOwnVoice(ru),
                LandingTexts.Popular(ru), "pine", true,
                [
                    LandingTexts.ProChannelLimit(ru, PlanLimitations.MaxChannels(PlanTiers.Pro)),
                    LandingTexts.MediaLimit(ru, Gb(PlanTiers.Pro)),
                    LandingTexts.YourOwnSignatureWithLink(ru),
                    LandingTexts.HeaderSlotLimit(ru, PlanLimitations.MaxHeaderSlots(PlanTiers.Pro)),
                ]),
            ("Pro+", $"${Consts.Plans.ProPlusPrice}", LandingTexts.PerMonth(ru),
                LandingTexts.WithAITranslationAndEditing(ru),
                "ai", "brass", false,
                [
                    LandingTexts.ProPlusChannelLimit(ru, PlanLimitations.MaxChannels(PlanTiers.ProPlus)),
                    LandingTexts.MediaLimit(ru, Gb(PlanTiers.ProPlus)),
                    LandingTexts.DailyAiLimit(ru, PlanLimitations.AiDailyLimit),
                    LandingTexts.EverythingInPro(ru),
                ]),
        };

        string Copy(string key) => E(c.Copy(key, ru));
        var nav = new List<string>();
        if (c.ShowFeatures) nav.Add($"""<a href="#features">{LandingTexts.HowItWorks.Pick(ru)}</a>""");
        if (c.ShowShots || c.ShowcaseBlog is not null) nav.Add($"""<a href="#examples">{LandingTexts.Examples.Pick(ru)}</a>""");
        if (c.ShowPricing) nav.Add($"""<a href="#pricing">{LandingTexts.Pricing(ru)}</a>""");
        nav.Add($"""<a href="/discovery">Discovery</a>""");

        var check = Icons.Svg("check", 15);

        string[] screens = ["project", "glossary", "canvas", "presets", "editor", "publishing", "publishing", "publishing", "editor", "posts", "tasks", "library", "builds", "calendar", "appearance", "publishing"];
        var featureCards = string.Join("", features.Select((f, index) => $"""
            <details class="feature">
                <summary class="feature-head">{Icons.Svg(f.Icon, 32)}<b>{E(f.Title)}</b><span aria-hidden="true">+</span></summary>
                <div class="feature-detail"><p>{E(f.Body)}</p>
                    <a href="/assets/review/{screens[index]}.png">
                        <img src="/assets/review/{screens[index]}.png" alt="{E(LandingTexts.ViewScreen.Pick(ru))}" loading="lazy" width="1057" height="891">
                    </a>
                </div>
            </details>
            """));
        var comparison = $"""
            <div class="plan-comparison"><table>
                <caption>{LandingTexts.ComparePlans.Pick(ru)}</caption>
                <thead><tr><th scope="col">{LandingTexts.ComparePlans.Pick(ru)}</th>{string.Join("", plans.Select(p => $"<th scope=\"col\">{E(p.Name)}</th>"))}</tr></thead>
                <tbody>
                    <tr><th scope="row">{LandingTexts.ChannelsRow.Pick(ru)}</th><td>{PlanLimitations.MaxChannels(PlanTiers.Free)}</td><td>{PlanLimitations.MaxChannels(PlanTiers.Pro)}</td><td>{PlanLimitations.MaxChannels(PlanTiers.ProPlus)}</td></tr>
                    <tr><th scope="row">{LandingTexts.StorageRow.Pick(ru)}</th><td>{Gb(PlanTiers.Free)}</td><td>{Gb(PlanTiers.Pro)}</td><td>{Gb(PlanTiers.ProPlus)}</td></tr>
                    <tr><th scope="row">{LandingTexts.BlogCommentsReactionsRSS(ru)}</th><td>✓</td><td>✓</td><td>✓</td></tr>
                    <tr><th scope="row">{LandingTexts.YourOwnSignatureWithLink(ru)}</th><td>—</td><td>✓</td><td>✓</td></tr>
                </tbody>
            </table></div>
            """;

        var planCards = string.Join("", plans.Select(p => $"""
            <article class="plan">
                <div class="plan-head"><b>{E(p.Name)}</b></div>
                <div class="plan-price"><span class="n">{E(p.Price)}</span><span class="per">{E(p.Per)}</span></div>
                <div class="plan-for">{E(p.For)}</div>
                <ul>{string.Join("", p.Lines.Select(l => $"<li>{check}<span>{E(l)}</span></li>"))}</ul>
            </article>
            """));

        var gallery = !c.ShowShots && c.ShowcaseBlog is null ? "" : $"""
            <section id="examples" class="examples">
                <div class="example-copy">
                    <h2>{Copy("examplesTitle")}</h2><p>{Copy("examplesBody")}</p>
                    {(c.ShowcaseBlog is null ? "" : $"""<a href="https://{E(c.ShowcaseBlog)}">{Copy("examplesLink")} &rarr;</a>""")}
                </div>
                <div id="shots" class="gallery">
                    {(c.ShowShots ? string.Join("", (c.Gallery.Count > 0 ? c.Gallery : new[] { new LandingShot("/landing-blog.png", LandingTexts.ViewScreen) }).Select(s => $"""
                    <figure>
                        <a href="{E(LandingContent.ShotUrl(s.File))}"><img src="{E(LandingContent.ShotUrl(s.File))}" alt="{E(s.Caption.IsEmpty ? LandingTexts.ViewScreen.Pick(ru) : s.Caption.Pick(ru))}" loading="lazy"></a>
                        {(s.Caption.IsEmpty ? "" : $"""<figcaption class="cap">{E(s.Caption.Pick(ru))}</figcaption>""")}
                    </figure>
                    """)) : "")}
                </div>
            </section>
            """;

        var roadmap = !c.ShowRoadmap || c.Roadmap.Count == 0 ? "" : $"""
            <section id="roadmap">
                <div class="rule">
                    <h2>Roadmap</h2>
                    <span class="meta">{LandingTexts.DoneInProgressNext(ru)}</span>
                </div>
                <div class="shelves">
                    {string.Join("", c.Roadmap.Select(col => $"""
                    <div class="shelf">
                        <div class="shelf-head"><b>{E(col.Title.Pick(ru))}</b><span class="spacer"></span><span class="n">{col.Items.Count}</span></div>
                        <div class="shelf-sheet"><ul>
                            {string.Join("", col.Items.Select(it => $"""
                            <li><span class="mark-{MarkClass(col.Mark)}">{Icons.Svg(MarkIcon(col.Mark), 15)}</span><span>{E(it.Pick(ru))}</span></li>
                            """))}
                        </ul></div>
                    </div>
                    """))}
                </div>
            </section>
            """;

        var story = !c.ShowStory || c.Story.Count == 0 ? "" : $"""
            <section id="story">
                <div class="rule">
                    <h2>{LandingTexts.WhyThisExists(ru)}</h2>
                    <span class="meta">{LandingTexts.BrieflyByMilestones(ru)}</span>
                </div>
                <div class="story">
                    <div class="timeline">
                        {string.Join("", c.Story.Select(s => $"""
                        <div class="step">
                            <div class="when">{E(s.When.Pick(ru))}</div>
                            <b>{E(s.Title.Pick(ru))}</b>
                            <p>{E(s.Text.Pick(ru))}</p>
                        </div>
                        """))}
                    </div>
                    <figure class="paper tight pinned" >
                        <img src="{E(LandingContent.ShotUrl(c.Hero.File))}" alt="" loading="lazy">
                    </figure>
                </div>
            </section>
            """;

        var download = !c.ShowDownload ? "" : $"""
            <section id="download">
                <div class="rule">
                    <h2>{LandingTexts.TheDesktopApp(ru)}</h2>
                    <span class="meta">Windows</span>
                </div>
                <div class="paper bright download-card" >
                    <div class="lead">
                        <b>{LandingTexts.TheSameBenchInItsOwnWindow(ru)}</b>
                        <p>{LandingTexts.DesktopDescription(ru)}</p>
                        <div class="download-meta">{LandingTexts.KeepsItselfUpdatedWithEveryRelease(ru)}</div>
                    </div>
                    <a class="btn btn-pine" href="/downloads/latest">{Icons.Svg("download-simple")}{LandingTexts.DownloadForWindows(ru)}</a>
                </div>
            </section>
            """;

        var lightTokens = DesignTokens.Declarations(DesignTokens.Light, DesignTokens.MaterialsLight);
        var darkTokens = DesignTokens.Declarations(DesignTokens.Dark, DesignTokens.MaterialsDark);

        return $"""
            <!doctype html>
            <html lang="{(ru ? "ru" : "en")}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(title)}</title>
            <meta name="description" content="{E(description)}">
            <meta property="og:title" content="{E(title)}">
            <meta property="og:description" content="{E(description)}">
            <meta property="og:type" content="website">
            <meta property="og:image" content="{Consts.URLs.MainHost}/og-default.png">
            <link rel="canonical" href="{Consts.URLs.MainHost}/">
            <link rel="alternate" hreflang="en" href="{Consts.URLs.MainHost}/?lang=en">
            <link rel="alternate" hreflang="ru" href="{Consts.URLs.MainHost}/?lang=ru">
            <style>{Css
                .Replace("{{LIGHT_TOKENS}}", lightTokens)
                .Replace("{{DARK_TOKENS}}", darkTokens)
                .Replace("{{FONT_FACES}}", DesignTokens.FontFaces)}</style>
            </head>
            <body>

            <header class="rail">
                <a class="brand" href="/welcome">{Mark(30)}<span class="rail-name">Cedar Clerk</span></a>
                <span class="rail-chip">{LandingTexts.InviteOnlyBeta(ru)}</span>
                <span class="spacer"></span>
                <nav class="rail-nav" aria-label="{LandingTexts.OnThisPage.Pick(ru)}">{string.Join("", nav)}</nav>
                <!--Two links rather than a script: the page is server-rendered, and a language is a
                different document, not a different state of this one.-->
                <div class="lang">
                    <a href="?lang=ru"{(ru ? """ aria-current="true" """ : "")}>RU</a>
                    <a href="?lang=en"{(ru ? "" : """ aria-current="true" """)}>EN</a>
                </div>
                <a class="btn btn-paper btn-sm login-link" href="/login">{LandingTexts.LogIn(ru)}</a>
                <a class="btn btn-pine btn-sm" href="#waitlist" data-waitlist>{LandingTexts.JoinTheWaitlist(ru)}</a>
                <details class="mobile-menu"><summary>{LandingTexts.Menu.Pick(ru)}</summary><nav aria-label="{LandingTexts.OnThisPage.Pick(ru)}">{string.Join("", nav)}<a href="#waitlist" data-waitlist>{LandingTexts.JoinTheWaitlist(ru)}</a></nav></details>
            </header>

            <dialog id="waitlist-dialog" aria-labelledby="waitlist-dialog-title">
                <form method="dialog"><button class="dialog-close" aria-label="{LandingTexts.CloseDialog.Pick(ru)}">×</button></form>
                <h2 id="waitlist-dialog-title">{LandingTexts.JoinTheWaitlist(ru)}</h2>
                <div id="waitlist-dialog-content"></div>
            </dialog>
            <main class="wrap">
                <section class="hero">
                    <div class="kicker-row"><span class="stamp">{E(c.Kicker.Pick(ru))}</span></div>
                    <h1>{E(c.HeroTitle.Pick(ru)).Replace("&lt;br&gt;", "<br>").Replace("&lt;br/&gt;", "<br>").Replace("&lt;br /&gt;", "<br>")}</h1>
                    <p class="hero-sub">{E(description)}</p>
                    <div id="waitlist" class="wait-wrap">
                        <form class="waitlist" id="waitlist-form">
                            <input type="email" name="email" required maxlength="254" autocomplete="email" placeholder="you@studio.dev" aria-label="{LandingTexts.Email(ru)}" aria-describedby="waitlist-note">
                            <input type="text" name="website" class="hp" tabindex="-1" autocomplete="off" aria-hidden="true">
                            <button class="btn btn-pine" type="submit">{LandingTexts.JoinTheWaitlist(ru)}</button>
                        </form>
                        <p class="drop" id="waitlist-note" role="status">{LandingTexts.WaitlistHint(ru)}</p>
                        {(c.Proof.IsEmpty ? "" : $"""<div class="proof">{E(c.Proof.Pick(ru))}</div>""")}
                        {(c.Note.IsEmpty ? "" : $"""<div class="note">{E(c.Note.Pick(ru))}</div>""")}
                    </div>
                    {(c.ShowShots ? $"""
                    <figure class="hero-shot">
                        <img src="{E(LandingContent.ShotUrl(c.Hero.File))}" alt="{E(c.Hero.Caption.IsEmpty ? LandingTexts.ViewScreen.Pick(ru) : c.Hero.Caption.Pick(ru))}" fetchpriority="high">
                        {(c.Hero.Caption.IsEmpty ? "" : $"""<figcaption class="cap">{E(c.Hero.Caption.Pick(ru))}</figcaption>""")}
                    </figure>
                    """ : "")}
                </section>

                {(c.ShowFeatures ? $"""
                <section id="features" aria-label="{Copy("workflowTitle")}">
                    <ol class="workflow">
                        {string.Join("", new[] { "write", "channels", "publish" }.Select((key, i) => $"""
                        <li><span class="step-number" aria-hidden="true">{i + 1}</span><div><h3>{Copy(key + "Title")}</h3><p>{Copy(key + "Body")}</p></div></li>
                        """))}
                    </ol>
                    <div class="network-strip"><b>{LandingTexts.OnePostEveryAddress(ru)}</b><span>{LandingTexts.Blog(ru)}</span>{string.Join("", PublishNetworks.All.Select(n => $"<span>{E(n)}</span>"))}<span>RSS</span></div>
                </section>
                """ : "")}
                {gallery}
                {DiscoveryEndpoints.RenderLandingPreview(ru, discovery)}
                {(c.ShowFeatures ? $"""
                <div class="benefits">
                    <div class="benefit">{Icons.Svg("translate", 32)}<div><h3>{LandingTexts.LanguagesTitle(ru, languageCount)}</h3><p>{LandingTexts.TranslationDescription(ru)}</p></div></div>
                    <div class="benefit">{Icons.Svg("timer", 32)}<div><h3>{LandingTexts.Scheduler(ru)}</h3><p>{LandingTexts.SchedulerDescription(ru)}</p></div></div>
                    <div class="benefit">{Icons.Svg("download-simple", 32)}<div><h3>{LandingTexts.TheTextsStayYours(ru)}</h3><p>{LandingTexts.ExportDescription(ru)}</p></div></div>
                </div>
                <details class="tools"><summary>{LandingTexts.AllTools.Pick(ru)} · {features.Length}</summary><div class="features">{featureCards}</div></details>
                """ : "")}
                {(c.ShowPricing ? $"""
                <section id="pricing">
                    <div class="section-heading"><h2>{Copy("pricingTitle")}</h2><p>{Copy("pricingBody")}</p></div>
                    <div class="plans">{planCards}</div>
                    <p class="plan-foot">{LandingTexts.SeparateCredits.Pick(ru)}<br>{LandingTexts.TrialPrice(ru, Consts.Plans.TrialPrice)}</p>
                    <details class="comparison-toggle"><summary>{LandingTexts.ComparePlans.Pick(ru)}</summary>{comparison}</details>
                </section>
                """ : "")}
                <section class="faq" id="faq"><h2>{Copy("faqTitle")}</h2>
                    {string.Join("", Enumerable.Range(1, 3).Select(i => $"""<details><summary>{Copy($"faq{i}Question")}</summary><p>{Copy($"faq{i}Answer")}</p></details>"""))}
                </section>

                {roadmap}
                {story}
                {download}

            </main>
            <section class="band">
                <h2>{Copy("closingTitle")}</h2><p>{Copy("closingBody")}</p>
                <a class="btn btn-paper" href="#waitlist" data-waitlist>{LandingTexts.SaveMySeat(ru)}</a>
            </section>

            <footer class="ruler">
                <span class="label">Cedar Clerk</span>
                <span>&copy; {DateTime.UtcNow.Year}</span>
                <span class="spacer"></span>
                <a href="/terms">{LandingTexts.Terms(ru)}</a>
                <a href="/privacy">{LandingTexts.Privacy(ru)}</a>
                {(c.ShowcaseBlog is null ? "" : $"""<a href="https://{E(c.ShowcaseBlog)}">{LandingTexts.LiveBlog(ru)}</a>""")}
                <a href="/login">{LandingTexts.LogIn(ru)}</a>
            </footer>
            <script>{WaitlistScript(ru)}</script>
            {(analyticsKey is null ? "" : ConsentBlock(ru, analyticsKey, analyticsHost))}
            </body>
            </html>
            """;
    }

    /// <summary>done / doing / next, and anything else read as next rather than refused.</summary>
    private static string MarkClass(string mark) => mark switch
    {
        "done" => "done",
        "doing" => "doing",
        _ => "next",
    };

    private static string MarkIcon(string mark) => mark switch
    {
        "done" => "check",
        "doing" => "clock",
        _ => "plus",
    };
}
