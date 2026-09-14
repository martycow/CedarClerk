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
        $"""<img src="/favicon.png" width="{size}" height="{size}" alt="" style="display:block;flex:none;object-fit:contain">""";

    // A plain (non-interpolated) raw string: CSS is mostly braces, and in an interpolated raw
    // string every one of them would have to be doubled. Same Replace-a-placeholder shape as
    // BlogEndpoints' shell, for the same reason.
    private const string Css = """
            :root { color-scheme: light dark; {{LIGHT_TOKENS}} }
            @media (prefers-color-scheme: dark) { :root { {{DARK_TOKENS}} } }
            {{FONT_FACES}}

            * { box-sizing: border-box; }

            /* The wall, lit from the corner — the same ground the app stands on, so the door and the
               room behind it are made of one thing (ADR-177).

               The ink here is --wood-ink and NOT --text (ADR-141). At night the wall goes dark while
               paper stays light, so the two grounds need two inks: --text is ink on paper and is
               invisible on wood. Everything below that sits on the wall names --wood-ink; everything
               that sits on a sheet re-declares --text, because a ground's colour is inherited. */
            body {
                margin: 0;
                background-color: var(--canvas);
                background-image: var(--lamp), var(--surface-page);
                background-attachment: fixed;
                color: var(--wood-ink);
                font-family: var(--font-sans);
                line-height: 1.6;
            }
            a { color: var(--wood-ink); }
            .wrap { max-width: 1160px; margin: 0 auto; padding: 0 24px; }
            .spacer { flex: 1; }

            /* ---- the rail ---------------------------------------------------------------- */
            .rail {
                position: sticky; top: 0; z-index: 20;
                display: flex; align-items: center; gap: 12px;
                height: var(--bench-rail-h); padding: 0 24px;
                background: var(--paper-bright);
                border-bottom: 1px solid var(--rule-ink-soft);
                box-shadow: var(--shadow-paper-sm);
            }
            .rail-name {
                font-family: var(--font-display); font-size: 18px; font-weight: 700;
                letter-spacing: .02em; color: var(--text);
            }
            .rail-chip { font-family: var(--font-mono); font-size: 11px; color: var(--t3); }
            .rail-nav { display: flex; gap: 14px; }
            .rail-nav a { font-size: 12.5px; font-weight: 600; color: var(--t2); text-decoration: none; }
            .rail-nav a:hover { color: var(--accent); }

            .lang {
                display: flex; gap: 4px; padding: 3px;
                border: var(--border-paper); border-radius: var(--radius-stamp);
                background: var(--sheet);
            }
            .lang a {
                display: inline-flex; align-items: center; justify-content: center;
                min-width: 34px; height: 24px; border: 1px solid transparent; border-radius: 3px;
                font-family: var(--font-mono); font-size: 11px; font-weight: 700; letter-spacing: .06em;
                color: var(--t2); text-decoration: none;
            }
            .lang a[aria-current] {
                border-color: var(--abord); background: var(--asoft); color: var(--accent);
            }

            /* ---- controls ---------------------------------------------------------------- */
            .btn {
                display: inline-flex; align-items: center; justify-content: center; gap: 8px;
                padding: 9px 18px; border-radius: var(--radius-plaque);
                font-family: var(--font-sans); font-size: 14px; font-weight: 700; line-height: 1.2;
                text-decoration: none; cursor: pointer;
            }
            .btn-sm { padding: 5px 13px; font-size: 13px; }
            .btn-pine {
                border: 1px solid var(--pine-deep); background: var(--grad-pine);
                color: var(--text-on-pine); box-shadow: var(--shadow-pine-btn);
                text-shadow: 0 1px 1px rgba(18, 26, 20, .45);
            }
            .btn-pine:hover { filter: brightness(1.07); }
            .btn-pine:active { transform: translateY(2px); }
            .btn-paper {
                border: var(--border-paper); background: var(--sheet); color: var(--t2);
                font-weight: 600; box-shadow: var(--shadow-paper-sm);
            }
            .btn-paper:hover { background: var(--surface); color: var(--text); }

            /* ---- paper ------------------------------------------------------------------- */
            /* Every sheet on this page is one recipe with a tilt: warm stock, paper noise, a warm
               edge and a real drop shadow, turned a degree or two so a stack looks handled. */
            .paper {
                position: relative;
                padding: 20px 22px;
                color: var(--text);
                background-color: var(--sheet); background-image: var(--tex-paper);
                border: var(--border-paper); border-radius: var(--radius-paper);
                box-shadow: var(--shadow-paper);
                transform: rotate(var(--tilt, -.5deg));
            }
            .paper.bright { background-color: var(--paper-bright); }
            .paper.pinned::before {
                content: ""; position: absolute; top: -8px; left: 50%; translate: -50%;
                width: 15px; height: 15px; border-radius: 50%;
                background: var(--grad-brass); border: 1px solid var(--brass-edge);
                box-shadow: inset 0 1px 0 var(--brass-hi), 0 2px 3px rgba(30, 18, 6, .45);
            }
            .paper.tight { padding: 10px; }
            .paper img { display: block; width: 100%; height: auto; border: 1px solid var(--paper-edge); }
            .cap { margin-top: 8px; font-family: var(--font-mono); font-size: 10.5px; color: var(--t3); }
            .paper a { color: var(--accent); }

            .stamp {
                display: inline-flex; align-items: center; padding: 2px 8px;
                border: 1px solid currentColor; border-radius: var(--radius-stamp);
                font-family: var(--font-display); font-size: 11px; font-weight: 700;
                letter-spacing: .13em; text-transform: uppercase; opacity: .92;
                color: var(--accent); background: var(--asoft); transform: rotate(-2deg);
            }
            .stamp.brass { color: var(--brass-ink); background: var(--brass-soft); }
            .stamp.ink { color: var(--t2); background: transparent; }

            /* The one place the hand-written face is allowed, and it lies on the wall — so it takes
               the wall's ink, like .margin-note does in the app. */
            .note {
                font-family: var(--font-note); font-size: 19px; line-height: 1.35;
                color: var(--wood-ink-soft); transform: rotate(-1deg);
            }

            /* ---- hero -------------------------------------------------------------------- */
            .hero { display: grid; grid-template-columns: minmax(0, 1fr) 470px; gap: 44px; align-items: start; padding: 56px 0 8px; }
            .kicker-row { display: flex; align-items: center; gap: 10px; margin-bottom: 18px; }
            .kicker-meta { font-family: var(--font-mono); font-size: 11px; color: var(--wood-ink-soft); }
            .hero h1 {
                margin: 0; font-family: var(--font-display); font-size: clamp(32px, 4.4vw, 44px);
                line-height: 1.12; font-weight: 700; letter-spacing: -.01em; text-wrap: pretty;
                color: var(--wood-ink);
            }
            .hero-sub {
                margin: 18px 0 0; max-width: 52ch;
                font-family: var(--font-serif); font-size: var(--fs-read); line-height: var(--lh-read);
                text-wrap: pretty; color: var(--wood-ink);
            }
            .wait-wrap { margin-top: 28px; max-width: 540px; scroll-margin-top: 76px; }
            .wait-label {
                margin-bottom: 8px; font-size: 11px; font-weight: 700; letter-spacing: .07em;
                text-transform: uppercase; color: var(--t2);
            }
            .waitlist { display: flex; gap: 10px; align-items: flex-start; }
            .waitlist input[type="email"] {
                flex: 1; min-width: 0; padding: 9px 13px; font: inherit; font-size: 14px;
                color: var(--text); background: var(--paper-bright);
                border: var(--border-paper); border-radius: var(--radius-field);
                box-shadow: var(--shadow-field-inset);
            }
            .waitlist input[type="email"]:focus-visible { outline: 2px solid var(--focus-halo); outline-offset: 1px; }
            .waitlist button { border: 1px solid var(--pine-deep); font-family: inherit; }
            /* The honeypot: invisible to people, present to bots. display:none would be too obvious. */
            .waitlist .hp { position: absolute; left: -9999px; width: 1px; height: 1px; opacity: 0; }

            /* A bead of resin: forming while nothing has been sent, set once it has. */
            .drop { display: flex; align-items: center; gap: 10px; margin-top: 12px; min-height: 22px;
                    font-size: 13px; color: var(--t2); }
            .drop::before {
                content: ""; flex: none; width: 10px; height: 10px; rotate: 45deg;
                border-radius: 50% 50% 50% 2px;
                background: radial-gradient(circle at 34% 28%, var(--resin-hi), var(--resin));
                box-shadow: 0 1px 2px rgba(58, 38, 16, .35);
            }
            .waitlist-done { color: var(--accent); font-weight: 600; }
            .proof { margin-top: 6px; font-family: var(--font-mono); font-size: 11px; color: var(--t3); }

            /* ---- wood plaque ------------------------------------------------------------- */
            .plaque {
                margin-top: 22px; padding: 14px 16px; border: 1px solid var(--wood-edge); border-radius: 4px;
                background-color: var(--wood); background-image: var(--tex-wood), var(--surface-rail);
                background-size: 420px, auto; box-shadow: var(--shadow-rail);
            }
            .plaque-title {
                font-family: var(--font-display); font-size: 11.5px; font-weight: 700;
                letter-spacing: .11em; text-transform: uppercase; color: var(--rail-ink);
                text-shadow: 0 1px 1px var(--rail-edge);
            }
            .flow {
                display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin-top: 10px;
                font-family: var(--font-mono); font-size: 11.5px; color: var(--rail-ink-soft);
            }
            .flow .arrow { color: var(--brass); }

            /* ---- sections ---------------------------------------------------------------- */
            section { padding: 64px 0 0; scroll-margin-top: 76px; }
            /* The pencil rule under a heading is drawn on the wall, so it is the wall's rule ink and
               not the sheet's — --rule-ink already flips with the theme for exactly this. */
            .rule {
                display: flex; align-items: baseline; gap: 12px; flex-wrap: wrap;
                padding-bottom: 10px; margin-bottom: 26px; border-bottom: 1px dashed var(--rule-ink);
            }
            .rule h2 {
                margin: 0; font-family: var(--font-display); font-size: 26px; font-weight: 700;
                color: var(--wood-ink);
            }
            .rule .meta { font-family: var(--font-mono); font-size: 11px; color: var(--wood-ink-soft); }

            .gallery { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 26px; }
            .gallery img { height: 210px; object-fit: cover; object-position: top; }

            .features { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; }
            .feature {
                display: flex; flex-direction: column; gap: 6px; padding: 16px 18px 18px;
                color: var(--text);
                background-color: var(--sheet); background-image: var(--tex-paper);
                border: var(--border-paper); border-radius: var(--radius-paper);
                box-shadow: var(--shadow-paper-sm);
            }
            .feature-head { display: flex; align-items: center; gap: 9px; color: var(--accent); }
            .feature-head b { font-family: var(--font-display); font-size: 16px; font-weight: 700; color: var(--text); }
            .feature p { margin: 0; font-size: 14px; line-height: 1.5; color: var(--t2); text-wrap: pretty; }

            .plans { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 24px; align-items: start; }
            .plan { padding: 26px 26px 24px; }
            .plan-head { display: flex; align-items: center; gap: 9px; margin-bottom: 14px; }
            .plan-head b { font-family: var(--font-display); font-size: 16px; font-weight: 700; letter-spacing: .02em; }
            .plan-price { display: flex; align-items: baseline; gap: 6px; }
            .plan-price .n { font-family: var(--font-mono); font-size: 34px; line-height: 1; letter-spacing: -.02em; }
            .plan-price .per { font-family: var(--font-mono); font-size: 12px; color: var(--t3); }
            .plan-for { margin-top: 6px; font-size: 14px; color: var(--t2); }
            .plan ul {
                display: flex; flex-direction: column; gap: 9px; margin: 18px 0 0; padding: 14px 0 0;
                list-style: none; border-top: 1px solid var(--rule-ink-soft);
            }
            .plan li { display: flex; align-items: flex-start; gap: 8px; font-size: 15px; line-height: 1.4; }
            .plan li svg { flex: none; margin-top: 2px; color: var(--accent); }
            .plan-foot { margin-top: 16px; font-family: var(--font-mono); font-size: 11px; color: var(--wood-ink-soft); }

            /* ---- shelf board (roadmap) --------------------------------------------------- */
            .shelves { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; align-items: start; }
            .shelf {
                padding: 3px; border: 1px solid var(--wood-edge); border-radius: 5px;
                background: var(--shelf-frame); box-shadow: var(--shadow-shelf);
            }
            .shelf-head {
                display: flex; align-items: baseline; gap: 8px;
                min-height: var(--bench-panel-hd); padding: 0 8px 0 10px;
                border-bottom: 1px solid var(--wood-edge);
                background-color: var(--sign-tile-hi);
                background-image: var(--tex-wood), var(--grad-sign-tile);
                background-size: 420px, auto;
            }
            .shelf-head b {
                font-family: var(--font-display); font-size: 11px; font-weight: 700;
                letter-spacing: .11em; text-transform: uppercase; color: var(--rail-ink);
                text-shadow: 0 1px 1px var(--rail-edge);
            }
            .shelf-head .n { font-family: var(--font-readout); font-size: 11px; color: var(--rail-ink); }
            .shelf-sheet {
                padding: 14px; background-color: var(--sheet); background-image: var(--tex-paper);
                color: var(--text);
            }
            .shelf-sheet ul { margin: 0; padding: 0; list-style: none; display: flex; flex-direction: column; gap: 10px; }
            .shelf-sheet li {
                display: flex; align-items: flex-start; gap: 8px; padding-bottom: 9px;
                border-bottom: 1px solid var(--rule-ink-soft); font-size: 15px; line-height: 1.45;
            }
            .shelf-sheet li:last-child { border-bottom: 0; padding-bottom: 0; }
            .shelf-sheet li svg { flex: none; margin-top: 2px; }
            .mark-done { color: var(--accent); }
            .mark-doing { color: var(--resin); }
            .mark-next { color: var(--t3); }

            /* ---- story ------------------------------------------------------------------- */
            .story { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 40px; align-items: start; }
            .timeline { position: relative; padding-left: 26px; border-left: 2px solid var(--rule-ink); }
            .step { position: relative; margin-bottom: 26px; }
            .step::before {
                content: ""; position: absolute; left: -35px; top: 7px; width: 11px; height: 11px;
                border-radius: 50%; background: var(--wood);
                box-shadow: inset 0 1px 2px rgba(30, 16, 4, .6), 0 0 0 2.5px var(--brass), 0 0 0 3.5px var(--brass-edge);
            }
            .step .when {
                font-family: var(--font-mono); font-size: 11px; letter-spacing: .06em;
                text-transform: uppercase; color: var(--wood-ink-soft);
            }
            .step b {
                display: block; margin-top: 3px; font-family: var(--font-display); font-size: 16px;
                color: var(--wood-ink);
            }
            .step p {
                margin: 6px 0 0; max-width: 62ch; font-family: var(--font-serif);
                font-size: 15.5px; line-height: 1.7; text-wrap: pretty; color: var(--wood-ink);
            }

            /* ---- download ---------------------------------------------------------------- */
            .download-card { display: flex; align-items: center; gap: 26px; flex-wrap: wrap; padding: 26px 30px; }
            .download-card .lead { flex: 1; min-width: 240px; }
            .download-card b { display: block; font-family: var(--font-display); font-size: 18px; font-weight: 700; }
            .download-card p { margin: 8px 0 0; font-size: 14px; line-height: 1.55; color: var(--t2); text-wrap: pretty; }
            .download-meta { margin-top: 10px; font-family: var(--font-mono); font-size: 11px; color: var(--t3); }

            /* ---- close ------------------------------------------------------------------- */
            .band {
                display: flex; align-items: center; gap: 26px; margin: 64px 0 56px; padding: 30px 34px;
                border: 1px solid var(--wood-edge); border-radius: 5px;
                background-color: var(--wood); background-image: var(--tex-wood), var(--surface-rail);
                background-size: 420px, auto; box-shadow: var(--shadow-rail);
            }
            .band b {
                display: block; font-family: var(--font-display); font-size: 20px; font-weight: 700;
                color: var(--rail-ink); text-shadow: 0 1px 1px var(--rail-edge);
            }
            .band span.sub { display: block; margin-top: 5px; font-size: 13px; color: var(--rail-ink-soft); }

            /* ADR-243 — a live sample of the public commons, not a screenshot of one. */
            .discover-preview {
                display: grid; grid-template-columns: minmax(260px, .8fr) minmax(0, 1.5fr);
                gap: 34px; margin-top: 62px; padding: 34px;
                color: var(--text-on-pine); background: var(--pine-deep);
                border: 1px solid var(--pine-deep); border-radius: var(--radius-paper);
                box-shadow: var(--shadow-paper);
            }
            .discover-copy h2 { margin: 10px 0 8px; font: 700 30px/1.08 var(--font-display); }
            .discover-copy p { margin: 0 0 20px; color: color-mix(in srgb, var(--text-on-pine) 76%, transparent); }
            .discover-copy .stamp { color: var(--brass); background: transparent; }
            .discover-copy .btn { width: fit-content; border-color: var(--brass-edge); }
            .discover-minis { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
            .discover-mini {
                display: grid; grid-template-columns: 98px minmax(0, 1fr); gap: 12px;
                min-height: 92px; padding: 8px; color: var(--text-on-pine); text-decoration: none;
                background: rgb(255 255 255 / .055); border: 1px solid rgb(255 255 255 / .13);
                border-radius: var(--radius-paper);
            }
            .discover-mini img { width: 98px; height: 76px; object-fit: cover; border-radius: 2px; }
            .discover-mini span { align-self: center; min-width: 0; }
            .discover-mini small, .discover-mini em, .discover-mini b { display: block; }
            .discover-mini small { color: var(--brass); font: 9px var(--font-mono); text-transform: uppercase; }
            .discover-mini b { margin: 4px 0; font: 700 14px/1.15 var(--font-display); }
            .discover-mini em { color: color-mix(in srgb, var(--text-on-pine) 62%, transparent); font: 10px var(--font-mono); }
            .discover-empty { grid-template-columns: 1fr auto; align-items: center; }

            .ruler {
                display: flex; align-items: center; gap: 16px; min-height: 30px; padding: 0 24px;
                border-top: 1px solid var(--rail-edge);
                background-color: var(--rail-lo);
                background-image: var(--tex-wood), var(--surface-rail);
                background-size: 420px, auto;
                font-family: var(--font-readout); font-size: 11px; color: var(--rail-ink);
                text-shadow: 0 1px 1px var(--rail-edge); overflow: hidden; white-space: nowrap;
            }
            .ruler .label {
                font-family: var(--font-sans); font-weight: 700; letter-spacing: .1em; text-transform: uppercase;
            }
            .ruler a { color: var(--rail-ink-soft); }

            /* T-153 — the consent gate. Fixed and low-left so it never covers the waitlist form,
               which is the one thing on this page a visitor came to use. */
            .consent {
                position: fixed; z-index: 60; left: 16px; bottom: 16px;
                width: min(28rem, calc(100vw - 32px)); padding: 16px;
                border: 1px solid var(--paper-edge); border-radius: 3px;
                background-color: var(--sheet); background-image: var(--tex-paper);
                box-shadow: 0 6px 18px rgb(0 0 0 / .22);
                font-size: 14px; line-height: 1.6;
            }
            .consent[hidden] { display: none; }
            .consent h2 { margin: 0 0 8px; font-family: var(--font-display); font-size: 18px; }
            .consent p { margin: 0 0 8px; }
            .consent-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 12px; }

            /* ---- narrow ------------------------------------------------------------------ */
            @media (max-width: 1000px) {
                .hero, .story { grid-template-columns: minmax(0, 1fr); gap: 32px; }
                .gallery, .features, .plans, .shelves { grid-template-columns: repeat(2, minmax(0, 1fr)); }
                .discover-preview { grid-template-columns: 1fr; }
            }
            @media (max-width: 720px) {
                .rail { height: auto; min-height: var(--bench-rail-h); flex-wrap: wrap; gap: 8px; padding: 8px 12px; }
                .rail-name { white-space: nowrap; }
                .rail-chip { margin-left: auto; }
                .rail > .spacer { flex: 0 0 100%; }
                .rail > .lang, .rail > .btn { flex-shrink: 0; }
                .rail > .btn { min-height: 34px; }
                .rail-nav { display: none; }
                .hero { padding-top: 36px; }
                section { padding-top: 44px; }
                .gallery, .features, .plans, .shelves { grid-template-columns: minmax(0, 1fr); }
                .band { flex-direction: column; align-items: flex-start; gap: 16px; }
                .discover-minis { grid-template-columns: 1fr; }
                .discover-preview { padding: 24px; }
                /* Off the tilt at phone width: a rotated sheet in a single column reads as a bug,
                   not as a hand — there is no stack for it to be part of. */
                .paper { transform: none; }
            }
            @media (prefers-reduced-motion: reduce) { .paper { transform: none; } }
        """;

    // Plain raw string with placeholders, like Css above: JS is as brace-heavy as CSS, and inside
    // an interpolated raw string every one of those braces would need escaping.
    private const string WaitlistJs = """
        document.getElementById('waitlist-form').addEventListener('submit', async e => {
            e.preventDefault();
            const form = e.target, note = document.getElementById('waitlist-note');
            const body = { email: form.email.value, website: form.website.value, language: '%%LANG%%' };
            try {
                const res = await fetch('/api/waitlist', { method: 'POST',
                    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
                if (res.ok) {
                    form.replaceWith(Object.assign(document.createElement('p'),
                        { className: 'waitlist-done', textContent: '%%DONE%%' }));
                    note.remove();
                } else {
                    note.textContent = (await res.json()).error || note.textContent;
                }
            } catch {
                note.textContent = '%%FAIL%%';
            }
        });
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

        var nav = new List<string>();
        nav.Add($"""<a href="/discovery">Discovery</a>""");
        if (discovery.Settings.Enabled) nav.Add($"""<a href="#discover">{LandingTexts.Community(ru)}</a>""");
        if (c.ShowShots) nav.Add($"""<a href="#shots">{LandingTexts.Screenshots(ru)}</a>""");
        if (c.ShowFeatures) nav.Add($"""<a href="#features">{LandingTexts.WhatItDoes(ru)}</a>""");
        if (c.ShowPricing) nav.Add($"""<a href="#pricing">{LandingTexts.Pricing(ru)}</a>""");
        if (c.ShowRoadmap && c.Roadmap.Count > 0) nav.Add("""<a href="#roadmap">Roadmap</a>""");
        if (c.ShowStory && c.Story.Count > 0) nav.Add($"""<a href="#story">{LandingTexts.Story(ru)}</a>""");
        if (c.ShowDownload) nav.Add($"""<a href="#download">{LandingTexts.Download(ru)}</a>""");

        var check = Icons.Svg("check", 15);

        var featureCards = string.Join("", features.Select(f => $"""
            <article class="feature">
                <span class="feature-head">{Icons.Svg(f.Icon)}<b>{E(f.Title)}</b></span>
                <p>{E(f.Body)}</p>
            </article>
            """));

        var planCards = string.Join("", plans.Select((p, i) => $"""
            <article class="paper plan{(p.Featured ? " bright pinned" : "")}" style="--tilt:{(i - 1) * 0.5:0.#}deg">
                <div class="plan-head">
                    <b>{E(p.Name)}</b><span class="spacer"></span>
                    <span class="stamp {p.Tone}">{E(p.Badge)}</span>
                </div>
                <div class="plan-price"><span class="n">{E(p.Price)}</span><span class="per">{E(p.Per)}</span></div>
                <div class="plan-for">{E(p.For)}</div>
                <ul>{string.Join("", p.Lines.Select(l => $"<li>{check}<span>{E(l)}</span></li>"))}</ul>
            </article>
            """));

        var gallery = !c.ShowShots || c.Gallery.Count == 0 ? "" : $"""
            <section id="shots">
                <div class="rule">
                    <h2>{LandingTexts.WhatItLooksLike(ru)}</h2>
                    <span class="meta">{LandingTexts.RealScreenshotsNotMockups(ru)}</span>
                </div>
                <div class="gallery">
                    {string.Join("", c.Gallery.Select((s, i) => $"""
                    <figure class="paper tight" style="--tilt:{(i % 3 - 1) * 0.55:0.#}deg;margin:0">
                        <img src="{E(LandingContent.ShotUrl(s.File))}" alt="{E(s.Caption.Pick(ru))}" loading="lazy">
                        {(s.Caption.IsEmpty ? "" : $"""<figcaption class="cap">{E(s.Caption.Pick(ru))}</figcaption>""")}
                    </figure>
                    """))}
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
                    <figure class="paper tight pinned" style="--tilt:1.2deg;margin:0">
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
                <div class="paper bright download-card" style="--tilt:.4deg">
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
                {Mark(24)}
                <span class="rail-name">Cedar Clerk</span>
                <span class="rail-chip">{LandingTexts.InviteOnlyBeta(ru)}</span>
                <span class="spacer"></span>
                <nav class="rail-nav">{string.Join("", nav)}</nav>
                <!--Two links rather than a script: the page is server-rendered, and a language is a
                different document, not a different state of this one.-->
                <div class="lang">
                    <a href="?lang=ru"{(ru ? """ aria-current="true" """ : "")}>RU</a>
                    <a href="?lang=en"{(ru ? "" : """ aria-current="true" """)}>EN</a>
                </div>
                <a class="btn btn-paper btn-sm" href="/login">{LandingTexts.LogIn(ru)}</a>
                <a class="btn btn-pine btn-sm" href="#waitlist">{LandingTexts.JoinTheWaitlist(ru)}</a>
            </header>

            <main class="wrap">
                <section class="hero" style="padding-top:56px">
                    <div>
                        <div class="kicker-row">
                            <span class="stamp">{E(c.Kicker.Pick(ru))}</span>
                            <span class="kicker-meta">{LandingTexts.LanguageSummary(ru, languageCount)}</span>
                        </div>
                        <h1>{c.HeroTitle.Pick(ru)}</h1>
                        <p class="hero-sub">{E(description)}</p>

                        <div id="waitlist" class="wait-wrap">
                            <div class="paper bright pinned" style="--tilt:-.4deg">
                                <div class="wait-label">{LandingTexts.EmailForAnInvite(ru)}</div>
                                <!--ADR-135 — the primary CTA is the waitlist: while registration is
                                invite-only, "Create an account" leads to a wall, and a wall converts nobody.-->
                                <form class="waitlist" id="waitlist-form" autocomplete="off">
                                    <input type="email" name="email" required maxlength="254"
                                           placeholder="you@studio.dev" aria-label="{LandingTexts.Email(ru)}">
                                    <input type="text" name="website" class="hp" tabindex="-1" autocomplete="off" aria-hidden="true">
                                    <button class="btn btn-pine" type="submit">{LandingTexts.SaveMySeat(ru)}</button>
                                </form>
                                <p class="drop" id="waitlist-note">{LandingTexts.WaitlistHint(ru)}</p>
                                {(c.Proof.IsEmpty ? "" : $"""<div class="proof">{E(c.Proof.Pick(ru))}</div>""")}
                            </div>
                            {(c.Note.IsEmpty ? "" : $"""<div class="note" style="margin-top:14px">{E(c.Note.Pick(ru))}</div>""")}
                        </div>
                    </div>

                    <div>
                        <figure class="paper tight" style="--tilt:1deg;margin:0">
                            <img src="{E(LandingContent.ShotUrl(c.Hero.File))}"
                                 alt="{E(LandingTexts.PostOnCedarClerkBlog(ru))}">
                            {(c.Hero.Caption.IsEmpty ? "" : $"""<figcaption class="cap" style="text-align:center">{E(c.Hero.Caption.Pick(ru))}</figcaption>""")}
                        </figure>
                        <div class="plaque">
                            <div class="plaque-title">{LandingTexts.OnePostEveryAddress(ru)}</div>
                            <div class="flow">
                                <span>{LandingTexts.Draft(ru)}</span><span class="arrow">&rarr;</span>
                                <span>Telegram · X · Bluesky · LinkedIn · Discord</span><span class="arrow">&rarr;</span>
                                <span>{LandingTexts.Blog(ru)}</span><span class="arrow">&rarr;</span><span>RSS</span>
                            </div>
                        </div>
                    </div>
                </section>

                {DiscoveryEndpoints.RenderLandingPreview(ru, discovery)}

                {gallery}

                {(c.ShowFeatures ? $"""
                <section id="features">
                    <div class="rule">
                        <h2>{LandingTexts.WhatIsAlreadyOnTheBench(ru)}</h2>
                        <span class="meta">{features.Length} {LandingTexts.Tools(ru)}</span>
                    </div>
                    <div class="features">{featureCards}</div>
                </section>
                """ : "")}

                {(c.ShowPricing ? $"""
                <section id="pricing">
                    <div class="rule">
                        <h2>{LandingTexts.WhatItCosts(ru)}</h2>
                        <span class="meta">{LandingTexts.PricingHint(ru)}</span>
                    </div>
                    <div class="plans">{planCards}</div>
                    <div class="plan-foot">{LandingTexts.TrialPrice(ru, Consts.Plans.TrialPrice)}</div>
                </section>
                """ : "")}

                {roadmap}
                {story}
                {download}

                <div class="band">
                    {Mark(56)}
                    <div style="flex:1;min-width:0">
                        <b>{LandingTexts.TheDoorsOpenByList(ru)}</b>
                        <span class="sub">{LandingTexts.InviteHint(ru)}</span>
                    </div>
                    <a class="btn btn-pine" href="#waitlist">{LandingTexts.SaveMySeat(ru)}</a>
                </div>
            </main>

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
