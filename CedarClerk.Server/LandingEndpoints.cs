using System.Net;
using CedarClerk.Core;
using CedarClerk.Localization;

namespace CedarClerk.Server;

/// <summary>
/// The page a stranger sees first (T-009).
///
/// **Server-rendered, not an Angular route**, for the same reason the blog is: this is the one page
/// whose job is to be found and to be read by someone who has never heard of the product, and
/// making them download a 531 kB application bundle to read a headline is the wrong trade. It is
/// also the only page that needs to be indexable.
///
/// **Shown at `/` only to visitors who are not signed in.** An author who types the address wants
/// their drafts, not a sales page — so a request carrying an Identity cookie falls through to the
/// SPA exactly as before.
///
/// **Every number comes from the code that enforces it** (<see cref="PlanLimitations"/>,
/// <see cref="Consts.Plans"/>). A hand-written price table is wrong the first time a limit moves,
/// and a pricing page that lies is worse than none.
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
    /// <param name="blogHost">
    /// Excluded by name. The blog serves its own index at "/" on its own host, and the first
    /// version of this middleware matched the path alone — quietly replacing the blog's homepage
    /// with a marketing page. Caught by the smoke suite, which is what it is for.
    /// </param>
    public static void UseLanding(this WebApplication app, string blogHost)
    {
        app.Use(async (ctx, next) =>
        {
            var isRoot = ctx.Request.Path == "/" || ctx.Request.Path == "";
            var isBlog = string.Equals(ctx.Request.Host.Host, blogHost, StringComparison.OrdinalIgnoreCase);
            if (!isRoot || isBlog || ctx.Request.Method != HttpMethods.Get || ctx.Request.Cookies.ContainsKey(AuthCookie))
            {
                await next();
                return;
            }

            var ru = PrefersRussian(ctx.Request.Headers.AcceptLanguage.ToString());
            ctx.Response.ContentType = "text/html; charset=utf-8";
            // A marketing page is worth caching at the edge, but not for long: it carries prices.
            ctx.Response.Headers.CacheControl = "public, max-age=300";
            await ctx.Response.WriteAsync(Render(ru));
        });
    }

    /// <summary>
    /// Russian unless the browser asks for something else first. The product's own default is
    /// Russian and its author writes in it; an English-first browser gets English.
    /// </summary>
    private static bool PrefersRussian(string acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return true;
        foreach (var entry in acceptLanguage.Split(','))
        {
            var code = entry.Split(';')[0].Trim().ToLowerInvariant();
            if (code.StartsWith("ru")) return true;
            if (code.Length >= 2) return false;
        }
        return true;
    }

    private static string Gb(PlanTiers tier) =>
        (PlanLimitations.StorageLimitBytes(tier) / (1024.0 * 1024 * 1024)) is var gb && gb >= 1
            ? $"{gb:0.#} GB"
            : $"{PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024)} MB";

    /// <summary>
    /// Four marks from the product's own icon set (Phosphor, ADR-072), inlined rather than linked:
    /// a landing page that waits on a request to draw its own bullet points is a page that
    /// flickers. Copied from `icon-data.generated.ts` — the same paths the app draws.
    /// </summary>
    private static readonly string[] CardIcons =
    [
        """<path d="M227.31,73.37,182.63,28.68a16,16,0,0,0-22.63,0L36.69,152A15.86,15.86,0,0,0,32,163.31V208a16,16,0,0,0,16,16H92.69A15.86,15.86,0,0,0,104,219.31L227.31,96a16,16,0,0,0,0-22.63ZM92.69,208H48V163.31l88-88L180.69,120ZM192,108.68,147.31,64l24-24L216,84.68Z"/>""",
        """<path d="M216,40H40A16,16,0,0,0,24,56V200a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V56A16,16,0,0,0,216,40Zm0,16V96H40V56ZM40,112H96v88H40Zm176,88H112V112H216v88Z"/>""",
        """<path d="M247.15,212.42l-56-112a8,8,0,0,0-14.31,0l-21.71,43.43A88,88,0,0,1,108,126.93,103.65,103.65,0,0,0,135.69,64H160a8,8,0,0,0,0-16H104V32a8,8,0,0,0-16,0V48H32a8,8,0,0,0,0,16h87.63A87.76,87.76,0,0,1,96,116.35a87.74,87.74,0,0,1-19-31,8,8,0,1,0-15.08,5.34A103.63,103.63,0,0,0,84,127a87.55,87.55,0,0,1-52,17,8,8,0,0,0,0,16,103.46,103.46,0,0,0,64-22.08,104.18,104.18,0,0,0,51.44,21.31l-26.6,53.19a8,8,0,0,0,14.31,7.16L148.94,192h70.11l13.79,27.58A8,8,0,0,0,240,224a8,8,0,0,0,7.15-11.58ZM156.94,176,184,121.89,211.05,176Z"/>""",
        """<path d="M208,24H72A32,32,0,0,0,40,56V224a8,8,0,0,0,8,8H192a8,8,0,0,0,0-16H56a16,16,0,0,1,16-16H208a8,8,0,0,0,8-8V32A8,8,0,0,0,208,24ZM120,40h48v72L148.79,97.6a8,8,0,0,0-9.6,0L120,112Zm80,144H72a31.82,31.82,0,0,0-16,4.29V56A16,16,0,0,1,72,40h32v88a8,8,0,0,0,12.8,6.4L144,114l27.21,20.4A8,8,0,0,0,176,136a8,8,0,0,0,8-8V40h16Z"/>""",
    ];

    // A plain (non-interpolated) raw string: CSS is mostly braces, and in an interpolated raw
    // string every one of them would have to be doubled. Same Replace-a-placeholder shape as
    // BlogEndpoints' shell, for the same reason.
    private const string Css = """
            :root { color-scheme: light dark; {{LIGHT_TOKENS}} }
            @media (prefers-color-scheme: dark) { :root { {{DARK_TOKENS}} } }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--canvas); color: var(--text); font-family: var(--font-sans); line-height: 1.6; }
            a { color: var(--accent); }
            .wrap { max-width: 960px; margin: 0 auto; padding: 0 20px; }

            header.top { border-bottom: 1px solid var(--border); background: var(--surface); }
            .top-inner { display: flex; align-items: center; gap: 10px; padding: 14px 0; }
            .brand { font-weight: 700; letter-spacing: -.01em; }
            .spacer { flex: 1; }
            .btn { display: inline-block; border-radius: var(--radius-md); padding: 9px 18px; font-weight: 600; text-decoration: none; }
            .btn-accent { background: var(--accent); color: var(--sheet); }
            .btn-ghost { border: 1px solid var(--border); color: var(--t2); }

            .hero { padding: 64px 0 40px; text-align: center; }
            .hero h1 { font-size: clamp(30px, 5vw, 46px); line-height: 1.15; margin: 0 0 16px; letter-spacing: -.02em; }
            .hero p { font-size: var(--fs-read); color: var(--t2); max-width: 62ch; margin: 0 auto 28px; }
            .hero-actions { display: flex; gap: 10px; justify-content: center; flex-wrap: wrap; }
            .hero-note { margin-top: 14px; font-size: var(--fs-caption); color: var(--t2); }

            section { padding: 40px 0; }
            h2 { font-size: 26px; margin: 0 0 8px; letter-spacing: -.01em; }
            .section-sub { color: var(--t2); margin: 0 0 24px; }

            /* Two by two, not three-and-one: four cards in an auto-fit grid leave the fourth
               alone on its own row, which reads as an afterthought rather than a fourth thing. */
            .cards { display: grid; grid-template-columns: repeat(2, 1fr); gap: 16px; }
            @media (max-width: 700px) { .cards { grid-template-columns: 1fr; } }
            .card { border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--surface); padding: 20px; }
            .card-mark { display: inline-flex; align-items: center; justify-content: center; width: 34px; height: 34px;
                          border-radius: var(--radius-md); background: var(--asoft); color: var(--accent); font-size: 18px; }
            .card h3 { font-size: var(--fs-body); margin: 12px 0 6px; }
            .card p { margin: 0; color: var(--t2); font-size: var(--fs-ui); }

            .plans { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; align-items: start; }
            .plan { border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--surface); padding: 22px; }
            .plan-featured { border-color: var(--abord); background: var(--asoft); }
            .plan h3 { margin: 0; font-size: var(--fs-body); }
            .plan-price { font-size: 30px; font-weight: 700; margin: 6px 0 14px; }
            .plan-price span { font-size: var(--fs-ui); font-weight: 400; color: var(--t2); margin-left: 4px; }
            .plan ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
            .plan li { color: var(--t2); font-size: var(--fs-ui); padding-left: 18px; position: relative; }
            .plan li::before { content: "·"; position: absolute; left: 6px; color: var(--accent); font-weight: 700; }

            /* The screenshot is the product, so it gets the product's own frame rather than a
               drop shadow pretending to be a browser window. */
            .shot { border: 1px solid var(--border); border-radius: var(--radius-lg); overflow: hidden; background: var(--surface); }
            .shot img { display: block; width: 100%; height: auto; }
            .shot-caption { padding: 12px 16px; border-top: 1px solid var(--border); color: var(--t2); font-size: var(--fs-ui); }

            .about { border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--sheet); padding: 24px; }
            .about p { margin: 0 0 12px; color: var(--t2); }
            .about p:last-child { margin: 0; }

            footer.bottom { border-top: 1px solid var(--border); background: var(--surface); margin-top: 48px; }
            .bottom-inner { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; padding: 20px 0; font-size: var(--fs-caption); color: var(--t2); }
            .bottom-inner a { color: var(--t2); }

            @media (max-width: 640px) {
                .hero { padding: 48px 0 36px; }
                section { padding: 32px 0; }
            }
        """;

    private static string Render(bool ru)
    {
        string T(string russian, string english) => ru ? russian : english;

        var title = T("Cedar Clerk — пишите здесь, публикуйте там",
                      "Cedar Clerk — write here, publish there");
        var description = T(
            "Редактор, из которого один и тот же пост уходит в Telegram-канал и на собственный блог. Самостоятельный хостинг, никакой аналитики за спиной.",
            "One editor whose post goes out to a Telegram channel and to your own blog. Self-hosted, with nobody watching over your shoulder.");

        // Features are stated as what the product does, not as adjectives about it.
        var features = new (string Icon, string Title, string Body)[]
        {
            (CardIcons[0], T("Один документ — много адресатов", "One document, many destinations"),
                 T("Пост пишется один раз. Telegram, блог и файл-экспорт — это три рендерера одного и того же текста, а не три копии, которые надо держать в согласии.",
                   "A post is written once. Telegram, the blog and the file exports are three renderers of the same text, not three copies to keep in agreement.")),
            (CardIcons[1], T("Живой редактор, а не форма", "A real editor, not a form"),
                 T("Таблицы, формулы, галереи, сноски, опросы, оглавление. Автосохранение, история версий и защита от случайного стирания текста.",
                   "Tables, formulas, galleries, footnotes, polls, a table of contents. Autosave, version history, and a guard against wiping your own text.")),
            (CardIcons[2], T("Шесть языков контента", "Six content languages"),
                 T("Пост и его переводы живут рядом. Авто-перевод дописывает только то, что изменилось, и не трогает правки, сделанные руками.",
                   "A post and its translations live side by side. Auto-translate rewrites only what changed and leaves your own corrections alone.")),
            (CardIcons[3], T("Блог, который принадлежит вам", "A blog that is yours"),
                 T("Своя страница с комментариями, реакциями, RSS и глоссарием терминов. Приватные посты открываются по форме или по личной ссылке.",
                   "Your own page with comments, reactions, RSS and a glossary. Private posts open behind a form or a personal link.")),
        };

        var plans = new (string Name, string Price, string[] Lines, bool Featured)[]
        {
            (T("Бесплатно", "Free"), "$0",
                [
                    T($"{PlanLimitations.MaxChannels(PlanTiers.Free)} канал", $"{PlanLimitations.MaxChannels(PlanTiers.Free)} channel"),
                    T($"{Gb(PlanTiers.Free)} на медиа", $"{Gb(PlanTiers.Free)} of media"),
                    T("Блог, комментарии, RSS", "Blog, comments, RSS"),
                    T("Подпись Cedar Clerk под постом", "A Cedar Clerk line under each post"),
                ], false),
            ("Pro", $"${Consts.Plans.ProPrice}",
                [
                    T($"{PlanLimitations.MaxChannels(PlanTiers.Pro)} канала", $"{PlanLimitations.MaxChannels(PlanTiers.Pro)} channels"),
                    T($"{Gb(PlanTiers.Pro)} на медиа", $"{Gb(PlanTiers.Pro)} of media"),
                    T("Своя подпись со ссылкой", "Your own signature, with a link"),
                    T("Третий слот в шапке поста", "A third slot in the post header"),
                ], true),
            ("Pro Plus", $"${Consts.Plans.ProPlusPrice}",
                [
                    T($"{PlanLimitations.MaxChannels(PlanTiers.ProPlus)} каналов", $"{PlanLimitations.MaxChannels(PlanTiers.ProPlus)} channels"),
                    T($"{Gb(PlanTiers.ProPlus)} на медиа", $"{Gb(PlanTiers.ProPlus)} of media"),
                    T($"Авто-перевод и правка через ИИ — {PlanLimitations.AiDailyLimit} операций в день",
                      $"Auto-translate and AI editing — {PlanLimitations.AiDailyLimit} operations a day"),
                    T("Всё из Pro", "Everything in Pro"),
                ], false),
        };

        var featureCards = string.Join("", features.Select(f => $"""
            <article class="card">
                <span class="card-mark" aria-hidden="true"><svg viewBox="0 0 256 256" fill="currentColor" width="18" height="18">{f.Icon}</svg></span>
                <h3>{WebUtility.HtmlEncode(f.Title)}</h3>
                <p>{WebUtility.HtmlEncode(f.Body)}</p>
            </article>
            """));

        var planCards = string.Join("", plans.Select(p => $"""
            <article class="plan{(p.Featured ? " plan-featured" : "")}">
                <h3>{WebUtility.HtmlEncode(p.Name)}</h3>
                <div class="plan-price">{WebUtility.HtmlEncode(p.Price)}<span>{T("/ мес", "/ mo")}</span></div>
                <ul>{string.Join("", p.Lines.Select(l => $"<li>{WebUtility.HtmlEncode(l)}</li>"))}</ul>
            </article>
            """));

        // The tokens come from the app's own stylesheet (T-101), so the landing cannot drift from
        // the product it is advertising.
        var lightTokens = DesignTokens.Declarations(DesignTokens.Light);
        var darkTokens = DesignTokens.Declarations(DesignTokens.Dark);

        return $"""
            <!doctype html>
            <html lang="{(ru ? "ru" : "en")}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(title)}</title>
            <meta name="description" content="{WebUtility.HtmlEncode(description)}">
            <meta property="og:title" content="{WebUtility.HtmlEncode(title)}">
            <meta property="og:description" content="{WebUtility.HtmlEncode(description)}">
            <meta property="og:type" content="website">
            <link rel="canonical" href="{Consts.URLs.MainHost}/">
            <style>{Css.Replace("{{LIGHT_TOKENS}}", lightTokens).Replace("{{DARK_TOKENS}}", darkTokens)}</style>
            </head>
            <body>
            <header class="top"><div class="wrap top-inner">
                <span class="brand">Cedar Clerk</span>
                <span class="spacer"></span>
                <a class="btn btn-ghost" href="/login">{T("Войти", "Log in")}</a>
            </div></header>

            <main>
            <div class="wrap">
                <section class="hero">
                    <h1>{T("Пишите здесь. Публикуйте там.", "Write here. Publish there.")}</h1>
                    <p>{WebUtility.HtmlEncode(description)}</p>
                    <div class="hero-actions">
                        <a class="btn btn-accent" href="/register">{T("Создать аккаунт", "Create an account")}</a>
                        <a class="btn btn-ghost" href="https://{Consts.URLs.BlogHost}">{T("Посмотреть блог вживую", "See a live blog")}</a>
                    </div>
                    <p class="hero-note">{T("Сейчас регистрация по инвайт-коду.", "Registration is invite-only for now.")}</p>
                </section>

                <section>
                    <h2>{T("Что он делает", "What it does")}</h2>
                    <p class="section-sub">{T("Четыре вещи, ради которых он существует.", "The four things it exists for.")}</p>
                    <div class="cards">{featureCards}</div>
                </section>

                <section>
                    <h2>{T("Как это выглядит", "What it looks like")}</h2>
                    <p class="section-sub">{T("Пост на собственном блоге — с реакциями, комментариями и подписью.", "A post on its own blog — with reactions, comments and a signature.")}</p>
                    <figure class="shot" style="margin:0">
                        <img src="/landing-blog.png" width="1120" height="760" loading="lazy"
                             alt="{T("Страница поста на блоге Cedar Clerk", "A post page on a Cedar Clerk blog")}">
                        <figcaption class="shot-caption">{T("Тот же текст уходит и в Telegram-канал — из того же редактора, одним нажатием.", "The same text goes to a Telegram channel too — from the same editor, in one press.")}</figcaption>
                    </figure>
                </section>

                <section>
                    <h2>{T("Сколько стоит", "What it costs")}</h2>
                    <p class="section-sub">{T("Цены и лимиты берутся из того же кода, который их применяет.", "These numbers come from the code that enforces them.")}</p>
                    <div class="plans">{planCards}</div>
                </section>

                <section>
                    <h2>{T("Кто это делает", "Who makes it")}</h2>
                    <div class="about">
                        <p>{T("Cedar Clerk пишет Марти — разработчик игр, который устал вести канал и блог как две отдельные работы.",
                              "Cedar Clerk is built by Marty, a game developer who got tired of running a channel and a blog as two separate jobs.")}</p>
                        <p>{T("Это инструмент, сделанный сначала для себя: он крутится на одном маленьком сервере, хранит тексты в открытом формате и умеет отдать их обратно файлом в любой момент.",
                              "It is a tool built for its own author first: it runs on one small server, keeps posts in an open format, and will hand them back as a file whenever you ask.")}</p>
                    </div>
                </section>
            </div>
            </main>

            <footer class="bottom"><div class="wrap bottom-inner">
                <span>© {DateTime.UtcNow.Year} Cedar Clerk</span>
                <span class="spacer"></span>
                <a href="/terms">{T("Условия", "Terms")}</a>
                <a href="/privacy">{T("Приватность", "Privacy")}</a>
                <a href="https://{Consts.URLs.BlogHost}">{T("Блог", "Blog")}</a>
            </div></footer>
            </body>
            </html>
            """;
    }
}
