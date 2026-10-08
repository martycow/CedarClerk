using CedarClerk.Localization;

namespace CedarClerk.Server;

// ADR-321 — the reader controls every public page shares: the landing, Discovery and the personal
// blog. Theme (Light / Dark / System), text size and typeface go on <html> as attributes and into
// localStorage; HeadScript applies them before the first paint so nothing flashes.
public static class PublicControls
{
    public const string HeadScript = """
        <script>
        (function () {
            /* Both settings, before the first paint: a theme applied after it flashes, and a text
               size applied after it reflows the article under the reader's eyes. */
            var el = document.documentElement;
            var theme = localStorage.getItem('cedar-blog-theme');
            if (theme) el.setAttribute('data-theme', theme);
            var size = localStorage.getItem('cedar-blog-text-size');
            if (size) el.setAttribute('data-read', size);
            var face = localStorage.getItem('cedar-blog-face');
            if (face) el.setAttribute('data-face', face);
        })();
        </script>
        """;

    public static string MenuHtml(string lang)
    {
        var t = BlogTexts.ReadingLabels.TryGetValue(lang, out var found) ? found : BlogTexts.ReadingLabels["en"];
        string Esc(string v) => System.Net.WebUtility.HtmlEncode(v);

        return $"""
            <div class="reading-anchor">
            <button type="button" class="reading-btn" id="readingBtn" aria-haspopup="true" aria-expanded="false"
                    aria-controls="readingMenu" title="{Esc(t.Menu)}" aria-label="{Esc(t.Menu)}">Aa</button>
            <div class="reading-menu" id="readingMenu" role="group" aria-label="{Esc(t.Menu)}" hidden>
            <div class="reading-title">{Esc(t.Menu)}</div>
            <div class="reading-group">
            <div class="reading-label" id="readingThemeLabel">{Esc(t.Theme)}</div>
            <div class="seg" role="group" aria-labelledby="readingThemeLabel" data-seg="theme">
            <button type="button" data-value="light" aria-pressed="false">{Esc(t.Day)}</button>
            <button type="button" data-value="dark" aria-pressed="false">{Esc(t.Night)}</button>
            <button type="button" data-value="" aria-pressed="false">{Esc(t.System)}</button>
            </div>
            </div>
            <div class="reading-group">
            <div class="reading-label" id="readingSizeLabel">{Esc(t.Size)}</div>
            <!-- The three A's are the control: each is drawn at the size it sets, which says what
                 the step does without a word that would need translating. -->
            <div class="seg seg-size" role="group" aria-labelledby="readingSizeLabel" data-seg="read">
            <button type="button" data-value="s" aria-pressed="false"><span style="font-size:13px">A</span></button>
            <button type="button" data-value="m" aria-pressed="false"><span style="font-size:15px">A</span></button>
            <button type="button" data-value="l" aria-pressed="false"><span style="font-size:18px">A</span></button>
            </div>
            </div>
            <div class="reading-group">
            <div class="reading-label" id="readingFaceLabel">{Esc(t.Face)}</div>
            <!-- ADR-192 — the two names are the faces' own, unlocalized, same as the app's own picker. -->
            <div class="seg seg-face" role="group" aria-labelledby="readingFaceLabel" data-seg="face">
            <button type="button" data-value="" aria-pressed="false" style="font-family:var(--font-serif)">Literata</button>
            <button type="button" data-value="sans" aria-pressed="false" style="font-family:var(--font-sans)">Source Sans 3</button>
            </div>
            </div>
            </div>
            </div>
            """;
    }

    /// <summary>RU / EN as two links: a language is a different document, not a state of this one.</summary>
    public static string LanguageSwitch(bool ru, string ruHref, string enHref) =>
        $"""<div class="pc-lang" role="group" aria-label="{(ru ? "Язык" : "Language")}"><a href="{System.Net.WebUtility.HtmlEncode(ruHref)}"{(ru ? " aria-current=\"true\"" : "")}>RU</a><a href="{System.Net.WebUtility.HtmlEncode(enHref)}"{(ru ? "" : " aria-current=\"true\"")}>EN</a></div>""";

    /// <summary>The same control for a page that carries more than the two languages.</summary>
    public static string LanguageSwitch(string current, IReadOnlyList<string> languages, Func<string, string> href) =>
        $"""<div class="pc-lang" role="group" aria-label="{(current == "ru" ? "Язык" : "Language")}">{string.Join("", languages.Select(l =>
            $"""<a href="{System.Net.WebUtility.HtmlEncode(href(l))}"{(l == current ? " aria-current=\"true\"" : "")}>{l.ToUpperInvariant()}</a>"""))}</div>""";

    /// <summary>Size and face for pages that are not a blog post: the blog maps them onto its own reading column.</summary>
    public const string PageCss = """
        :root[data-read="s"] body { zoom: .94; }
        :root[data-read="l"] body { zoom: 1.1; }
        :root[data-face="sans"] { --font-display: var(--font-sans); --font-serif: var(--font-sans); }
        """;

    public const string Css = """
        /* ── The reading menu (ADR-181) ──────────────────────────────────────────────────────────
           One control for the two things a reader may change. ADR-175 settled its face: a tinted
           button on the rail needs wood under it to read as anything, and at the chrome box the
           honest answer is paper. */
        .reading-anchor { position: relative; flex: none; }
        .reading-btn { display: flex; align-items: center; justify-content: center; width: 30px; height: 30px; border: 1px solid var(--paper-edge); background: var(--sheet); box-shadow: var(--shadow-paper-sm); border-radius: var(--radius-plaque); color: var(--t2); cursor: pointer; padding: 0; font-family: var(--font-display); font-size: 13px; font-weight: 700; line-height: 1; }
        .reading-btn:hover, .reading-btn[aria-expanded="true"] { background: var(--alt); color: var(--text); }
        /* It hangs off the rail and lands on paper, so the rail's cream ink stops at the board. */
        .reading-menu { position: absolute; top: calc(100% + 8px); right: 0; z-index: 20; width: 248px; padding: 14px 16px 16px; background-color: var(--sheet); background-image: var(--tex-paper); border: 1px solid var(--paper-edge); border-radius: var(--radius-paper); box-shadow: var(--shadow-sheet); color: var(--text); text-align: left; }
        .reading-title { font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--t2); margin: 0 0 12px; }
        .reading-group + .reading-group { margin-top: 14px; }
        .reading-label { font-size: 14px; font-weight: 600; color: var(--t2); margin: 0 0 6px; }
        /* A segmented control on paper: one plaque per state, the chosen one in pine. Not a toggle —
           the theme has three states and a toggle can only ever express two, which is why the old
           control could never hand the page back to the system setting. */
        /* Wraps, and the buttons size to their own labels: "Система" is longer than its third of the
           row and "Сістэмная" is longer still, so an equal split clips the very state it names. A
           long label takes its own line rather than being cut. */
        .seg { display: flex; flex-wrap: wrap; gap: 4px; }
        .seg button { flex: 1 1 auto; min-width: 0; min-height: 34px; padding: 0 10px; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--paper-bright); color: var(--t2); font-family: var(--font-sans); font-size: 13px; font-weight: 600; white-space: nowrap; cursor: pointer; }
        .seg button:hover { background: var(--alt); color: var(--text); }
        .seg button[aria-pressed="true"] { border-color: var(--pine-deep); background: var(--grad-pine); color: var(--text-on-pine); box-shadow: var(--shadow-pine-btn); text-shadow: 0 1px 1px rgba(18, 26, 20, .45); }
        .seg-size button { font-family: var(--font-serif); }
        .seg-size button span { display: inline-block; line-height: 1; }
        .seg-face button { font-size: 12.5px; }
        /* On a phone the popover would hang off the right edge of a 390px viewport. */
        @media (max-width: 420px) {
            .reading-menu { right: -8px; width: calc(100vw - 32px); max-width: 260px; }
        }
        .pc-lang { display: flex; gap: 2px; }
        .pc-lang a { display: inline-flex; align-items: center; min-height: 30px; padding: 0 8px; border-radius: var(--radius-plaque); color: inherit; font-size: 13px; font-weight: 600; text-decoration: none; }
        .pc-lang a:hover { background: var(--alt); }
        .pc-lang a[aria-current] { background: var(--asoft); color: var(--accent); }
        .pc-controls { display: flex; align-items: center; gap: 6px; }
        """;

    public const string Script = """
        <script>
        /* ADR-181 — the reading menu. Two segmented controls over the same mechanism: a value goes
           on <html> as an attribute and into localStorage under its key, and the empty value means
           "no attribute", which is how the theme reaches its third state — back to the system. */
        (function () {
            var btn = document.getElementById('readingBtn');
            var menu = document.getElementById('readingMenu');
            if (!btn || !menu) return;

            var SETTINGS = {
                theme: { attr: 'data-theme', key: 'cedar-blog-theme' },
                read: { attr: 'data-read', key: 'cedar-blog-text-size' },
                face: { attr: 'data-face', key: 'cedar-blog-face' }
            };
            var el = document.documentElement;

            function apply(name, value) {
                var s = SETTINGS[name];
                if (value) {
                    el.setAttribute(s.attr, value);
                    localStorage.setItem(s.key, value);
                } else {
                    el.removeAttribute(s.attr);
                    localStorage.removeItem(s.key);
                }
                mark(name);
            }

            /* The size control has no "no attribute" state to show — m is the default and declares
               nothing — so an absent value reads as m there and as the system on the theme row. */
            function mark(name) {
                var s = SETTINGS[name];
                var current = el.getAttribute(s.attr) || (name === 'read' ? 'm' : '');
                var group = menu.querySelector('[data-seg="' + name + '"]');
                if (!group) return;
                Array.prototype.forEach.call(group.querySelectorAll('button'), function (b) {
                    b.setAttribute('aria-pressed', b.getAttribute('data-value') === current ? 'true' : 'false');
                });
            }

            Object.keys(SETTINGS).forEach(function (name) {
                mark(name);
                var group = menu.querySelector('[data-seg="' + name + '"]');
                if (!group) return;
                group.addEventListener('click', function (e) {
                    var b = e.target.closest ? e.target.closest('button[data-value]') : null;
                    if (b) apply(name, b.getAttribute('data-value'));
                });
            });

            function open(state) {
                menu.hidden = !state;
                btn.setAttribute('aria-expanded', state ? 'true' : 'false');
            }

            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                open(menu.hidden);
            });
            /* A click inside must not close it: the two rows are meant to be tried against the page. */
            menu.addEventListener('click', function (e) { e.stopPropagation(); });
            document.addEventListener('click', function () { open(false); });
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && !menu.hidden) { open(false); btn.focus(); }
            });
        })();
        </script>
        """;
}
