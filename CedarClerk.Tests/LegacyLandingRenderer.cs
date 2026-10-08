using System.Net;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;

namespace CedarClerk.Tests;

// ADR-323 §5 — the renderer the landing had before it became sections of blocks, kept verbatim as
// the reference the equivalence test compares the block renderer against. It leaves with the
// vestigial LandingSettings columns.
internal static class LegacyLandingRenderer
{
    private static string Gb(PlanTiers tier) =>
        (PlanLimitations.StorageLimitBytes(tier) / (1024.0 * 1024 * 1024)) is var gb && gb >= 1
            ? $"{gb:0.#} GB"
            : $"{PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024)} MB";

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static string Mark(int size) =>
        $"""<img src="/assets/brand/cedar-clerk-mark.svg" width="{size}" height="{size}" alt="" style="display:block;flex:none;object-fit:contain">""";

    public static string Render(bool ru, LandingContent c, DiscoveryEndpoints.Snapshot discovery,
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
        nav.Add($"""<a class="nav-discovery" href="/discovery">Discovery</a>""");

        var check = Icons.Svg("check", 15);

        string[] screens = ["project", "glossary", "canvas", "presets", "editor", "publishing", "publishing", "publishing", "editor", "posts", "tasks", "library", "builds", "calendar", "appearance", "publishing"];
        // ADR-321 / T-413 — a grid of tool buttons on the left and one detail pane on the right;
        // hovering, focusing or clicking a button shows its tool. Without a script the first shows.
        var toolButtons = string.Join("", features.Select((f, index) => $"""
            <button type="button" class="tool-btn" role="tab" id="tool-tab-{index}" aria-controls="tool-panel-{index}" aria-selected="{(index == 0 ? "true" : "false")}" tabindex="{(index == 0 ? "0" : "-1")}">{Icons.Svg(f.Icon, 24)}<span>{E(f.Title)}</span></button>
            """));
        var toolPanels = string.Join("", features.Select((f, index) => $"""
            <div class="tool-panel" role="tabpanel" id="tool-panel-{index}" aria-labelledby="tool-tab-{index}"{(index == 0 ? "" : " hidden")}>
                <h3>{E(f.Title)}</h3><p>{E(f.Body)}</p>
                <a href="/assets/review/{screens[index]}.png">
                    <img src="/assets/review/{screens[index]}.png" alt="{E(LandingTexts.ViewScreen.Pick(ru))}" loading="lazy" width="1057" height="891">
                </a>
            </div>
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
            {PublicControls.HeadScript}
            <style>{LandingRenderer.Stylesheet()}</style>
            </head>
            <body>

            <header class="rail">
                <a class="brand" href="/welcome">{Mark(30)}<span class="rail-name">Cedar Clerk</span></a>
                <span class="rail-chip">{LandingTexts.InviteOnlyBeta(ru)}</span>
                <span class="spacer"></span>
                <nav class="rail-nav" aria-label="{LandingTexts.OnThisPage.Pick(ru)}">{string.Join("", nav)}</nav>
                <div class="pc-controls">{PublicControls.LanguageSwitch(ru, "?lang=ru", "?lang=en")}{PublicControls.MenuHtml(ru ? "ru" : "en")}</div>
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
                <section class="tools" aria-labelledby="tools-title">
                    <h3 id="tools-title">{LandingTexts.AllTools.Pick(ru)} · {features.Length}</h3>
                    <div class="tools-layout"><div class="tool-grid" role="tablist" aria-orientation="vertical" aria-labelledby="tools-title">{toolButtons}</div><div class="tool-detail">{toolPanels}</div></div>
                </section>
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
            <script>{LandingRenderer.WaitlistScript(ru ? "ru" : "en")}</script>
            {PublicControls.Script}
            {(analyticsKey is null ? "" : LandingRenderer.ConsentBlock(ru, analyticsKey, analyticsHost))}
            </body>
            </html>
            """;
    }

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
