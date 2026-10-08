using System.Net;
using CedarClerk.Core;
using CedarClerk.Localization;
using TextMap = System.Collections.Generic.Dictionary<string, string>;

namespace CedarClerk.Server;

/// <summary>
/// Draws the landing from its <see cref="LandingDocument"/> (ADR-323). The only renderer of that
/// document: the public page and the admin preview both come from here.
/// </summary>
public static class LandingRenderer
{
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
            :root { color-scheme: light dark; {{LIGHT_TOKENS}} }
            @media (prefers-color-scheme: dark) { :root { {{DARK_TOKENS}} } }
            :root[data-theme="light"] { {{LIGHT_TOKENS}} }
            :root[data-theme="dark"] { {{DARK_TOKENS}} }
            {{FONT_FACES}}
            {{PC_CSS}}
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
            .comparison-toggle>summary { font-weight: 600; padding: var(--space-4) 0; }
            .tools { margin-top: var(--space-6); padding-bottom: var(--space-6); border-bottom: 1px solid var(--border); }
            .tools>h3 { margin-bottom: var(--space-4); }
            .tools-layout { display: grid; grid-template-columns: minmax(0, 5fr) minmax(0, 7fr); gap: var(--space-5); align-items: start; }
            .tool-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-2); }
            .tool-btn { display: flex; align-items: center; gap: var(--space-2); min-height: 56px; padding: var(--space-2) var(--space-3); border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--paper-bright); color: var(--text); font: 600 var(--fs-16)/1.25 var(--font-sans); text-align: left; }
            .tool-btn svg { flex-shrink: 0; color: var(--accent); }
            .tool-btn:hover { border-color: var(--accent); }
            .tool-btn[aria-selected="true"] { border-color: var(--accent); background: var(--asoft); color: var(--accent); }
            .tool-detail { position: sticky; top: 100px; padding: var(--space-5); border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--paper-bright); }
            .tool-panel p { margin-top: var(--space-2); font-size: var(--fs-17); }
            .tool-panel img { margin-top: var(--space-4); width: 100%; }
            .rail-nav a.nav-discovery { padding: var(--space-1) var(--space-3); border: 1px solid var(--accent); border-radius: var(--radius-md); background: var(--asoft); color: var(--accent); font-weight: 700; }
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
                .rail-name { font-size: var(--fs-21); } .pc-lang a { min-height: 44px; }
                h1 { font-size: calc(var(--fs-21) * 2); } .hero-sub { font-size: var(--fs-21); }
                .waitlist { flex-direction: column; } .hero-shot { margin-top: var(--space-6); }
                .examples { padding: var(--space-5); } .gallery, .plans, .shelves, .tools-layout { grid-template-columns: 1fr; } .tool-detail { position: static; }
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
        const toolTabs = [...document.querySelectorAll('.tool-btn')];
        const showTool = tab => toolTabs.forEach(t => {
            const on = t === tab;
            t.setAttribute('aria-selected', on ? 'true' : 'false');
            t.tabIndex = on ? 0 : -1;
            document.getElementById(t.getAttribute('aria-controls')).hidden = !on;
        });
        toolTabs.forEach((tab, i) => {
            tab.addEventListener('click', () => showTool(tab));
            tab.addEventListener('focus', () => showTool(tab));
            tab.addEventListener('pointerenter', event => { if (event.pointerType === 'mouse') showTool(tab); });
            tab.addEventListener('keydown', event => {
                const step = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 }[event.key];
                if (!step) return;
                event.preventDefault();
                toolTabs[(i + step + toolTabs.length) % toolTabs.length].focus();
            });
        });
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

    public static string WaitlistScript(string lang) => WaitlistJs
        .Replace("%%LANG%%", Languages.IsUiLanguage(lang) ? lang : Languages.English)
        .Replace("%%DONE%%", LandingTexts.WaitlistSuccess(lang == Languages.Russian))
        .Replace("%%FAIL%%", LandingTexts.WaitlistFailure(lang == Languages.Russian));

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
    public static string ConsentBlock(bool ru, string key, string host)
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

    private sealed record Page(
        string Lang, bool Ru, LandingDocument Doc, DiscoveryEndpoints.Snapshot Discovery)
    {
        public bool FormDrawn { get; set; }
        public bool GalleryDrawn { get; set; }
        public bool ToolsDrawn { get; set; }

        public string T(TextMap? map) => LandingDocument.Pick(map, Lang)
            .Replace(LandingDocument.LanguagesToken, Languages.ContentLanguages.Count.ToString())
            .Replace(LandingDocument.NetworksToken, PublishNetworks.All.Count.ToString());

        public string Field(LandingBlock block, string key) => T(block.Text.GetValueOrDefault(key));
        public string Field(LandingItem item, string key) => T(item.Text.GetValueOrDefault(key));
    }

    /// <summary>
    /// The visitor's language among the ones the document carries: the switch in the rail wins,
    /// then the first language the browser names. English is the floor (ADR-135).
    /// </summary>
    public static string ChooseLanguage(string? query, string? acceptLanguage, IReadOnlyList<string> languages)
    {
        if (!string.IsNullOrEmpty(query) && languages.Contains(query)) return query;
        foreach (var entry in (acceptLanguage ?? "").Split(','))
        {
            var code = entry.Split(';')[0].Trim().ToLowerInvariant();
            if (code.Length < 2) continue;
            var primary = code.Split('-')[0];
            return languages.Contains(primary) ? primary : Languages.English;
        }
        return Languages.English;
    }

    public static string Render(string lang, LandingDocument doc, DiscoveryEndpoints.Snapshot discovery,
        string? configuredShowcase, string? analyticsKey, string analyticsHost)
    {
        var page = new Page(lang, lang == Languages.Russian, doc, discovery);
        var ru = page.Ru;

        var title = LandingTexts.PageTitle(ru);
        var heroText = doc.Sections.SelectMany(s => s.Blocks)
            .FirstOrDefault(b => b is { Type: LandingBlocks.Text, Style: "hero" });
        var description = heroText is null ? "" : page.Field(heroText, "body");
        if (description.Length == 0) description = LandingTexts.HeroSub.Pick(ru);
        var showcase = SafeHost(doc.ShowcaseBlog) ?? SafeHost(string.IsNullOrWhiteSpace(configuredShowcase) ? null : configuredShowcase.Trim());

        var groups = new List<(bool Band, List<string> Parts)>();
        var nav = new List<string>();
        foreach (var section in doc.Sections)
        {
            if (section.Hidden) continue;
            var html = RenderSection(page, section);
            if (html.Length == 0) continue;
            var band = section.Layout == LandingLayouts.Band;
            if (groups.Count == 0 || groups[^1].Band != band) groups.Add((band, []));
            groups[^1].Parts.Add(html);
            var label = page.T(section.Nav);
            if (section.Anchor is not null && label.Length > 0)
                nav.Add($"""<a href="#{E(section.Anchor)}">{E(label)}</a>""");
        }
        nav.Add($"""<a class="nav-discovery" href="/discovery">Discovery</a>""");

        var mainDrawn = false;
        var body = string.Join("\n", groups.Select(group =>
        {
            if (group.Band) return string.Join("\n", group.Parts);
            var tag = mainDrawn ? "div" : "main";
            mainDrawn = true;
            return $"<{tag} class=\"wrap\">\n{string.Join("\n", group.Parts)}\n</{tag}>";
        }));
        // The rail's waitlist button opens a dialog that copies the page's form, so a page whose
        // form is hidden or absent still has to carry one.
        var spareForm = page.FormDrawn ? "" : $"""<div hidden>{WaitlistForm(page, E(LandingTexts.JoinTheWaitlist(ru)), E(LandingTexts.WaitlistHint(ru)))}</div>""";

        var others = doc.Languages.Where(l => l != Languages.English && l != Languages.Russian && Languages.IsUiLanguage(l)).ToList();
        var alternates = string.Join("", others.Select(l => $"""

            <link rel="alternate" hreflang="{l}" href="{Consts.URLs.MainHost}/?lang={l}">
            """));
        var languageSwitch = others.Count == 0
            ? PublicControls.LanguageSwitch(ru, "?lang=ru", "?lang=en")
            : PublicControls.LanguageSwitch(lang, [Languages.Russian, Languages.English, .. others], l => "?lang=" + l);

        return $"""
            <!doctype html>
            <html lang="{E(lang)}">
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
            <link rel="alternate" hreflang="ru" href="{Consts.URLs.MainHost}/?lang=ru">{alternates}
            {PublicControls.HeadScript}
            <style>{Stylesheet()}</style>
            </head>
            <body>

            <header class="rail">
                <a class="brand" href="/welcome">{Mark(30)}<span class="rail-name">Cedar Clerk</span></a>
                <span class="rail-chip">{LandingTexts.InviteOnlyBeta(ru)}</span>
                <span class="spacer"></span>
                <nav class="rail-nav" aria-label="{LandingTexts.OnThisPage.Pick(ru)}">{string.Join("", nav)}</nav>
                <div class="pc-controls">{languageSwitch}{PublicControls.MenuHtml(lang)}</div>
                <a class="btn btn-paper btn-sm login-link" href="/login">{LandingTexts.LogIn(ru)}</a>
                <a class="btn btn-pine btn-sm" href="#waitlist" data-waitlist>{LandingTexts.JoinTheWaitlist(ru)}</a>
                <details class="mobile-menu"><summary>{LandingTexts.Menu.Pick(ru)}</summary><nav aria-label="{LandingTexts.OnThisPage.Pick(ru)}">{string.Join("", nav)}<a href="#waitlist" data-waitlist>{LandingTexts.JoinTheWaitlist(ru)}</a></nav></details>
            </header>

            <dialog id="waitlist-dialog" aria-labelledby="waitlist-dialog-title">
                <form method="dialog"><button class="dialog-close" aria-label="{LandingTexts.CloseDialog.Pick(ru)}">×</button></form>
                <h2 id="waitlist-dialog-title">{LandingTexts.JoinTheWaitlist(ru)}</h2>
                <div id="waitlist-dialog-content"></div>
            </dialog>
            {body}
            {spareForm}
            <footer class="ruler">
                <span class="label">Cedar Clerk</span>
                <span>&copy; {DateTime.UtcNow.Year}</span>
                <span class="spacer"></span>
                <a href="/terms">{LandingTexts.Terms(ru)}</a>
                <a href="/privacy">{LandingTexts.Privacy(ru)}</a>
                {(showcase is null ? "" : $"""<a href="https://{E(showcase)}">{LandingTexts.LiveBlog(ru)}</a>""")}
                <a href="/login">{LandingTexts.LogIn(ru)}</a>
            </footer>
            <script>{WaitlistScript(lang)}</script>
            {PublicControls.Script}
            {(analyticsKey is null ? "" : ConsentBlock(ru, analyticsKey, analyticsHost))}
            </body>
            </html>
            """;
    }

    public static string Stylesheet() => Css
        .Replace("{{LIGHT_TOKENS}}", DesignTokens.Declarations(DesignTokens.Light, DesignTokens.MaterialsLight))
        .Replace("{{DARK_TOKENS}}", DesignTokens.Declarations(DesignTokens.Dark, DesignTokens.MaterialsDark))
        .Replace("{{FONT_FACES}}", DesignTokens.FontFaces)
        .Replace("{{PC_CSS}}", PublicControls.Css + PublicControls.PageCss);

    private static string? SafeHost(string? host) => LandingDocument.IsSafeHost(host) ? host : null;

    /// <summary>
    /// Where a stored image reference points, or null for one this page will not draw. Stored
    /// documents are validated on save, and the legacy columns never were.
    /// </summary>
    private static string? ImageUrl(string? file) =>
        string.IsNullOrWhiteSpace(file) || file.StartsWith("//", StringComparison.Ordinal) || file.Contains('\\')
        || file.Any(char.IsControl) || (!file.StartsWith('/') && file.Contains(':'))
            ? null
            : LandingContent.ShotUrl(file);

    private static string RenderSection(Page page, LandingSection section)
    {
        var flow = section.Layout == LandingLayouts.Flow;
        var parts = new List<string>();
        var anchorPending = flow ? section.Anchor : null;
        foreach (var block in section.Blocks)
        {
            if (block.Hidden) continue;
            var faqAnchor = anchorPending is not null && parts.Count == 0 && block.Type == LandingBlocks.Faq ? anchorPending : null;
            var html = RenderBlock(page, block, flow, faqAnchor);
            if (html.Length == 0) continue;
            if (faqAnchor is not null) anchorPending = null;
            parts.Add(html);
        }
        if (parts.Count == 0) return "";

        var inner = string.Join("\n", parts);
        var id = section.Anchor is null ? "" : $""" id="{E(section.Anchor)}" """.TrimEnd();
        var label = page.T(section.Label) is { Length: > 0 } text ? $""" aria-label="{E(text)}" """.TrimEnd() : "";
        return section.Layout switch
        {
            LandingLayouts.Flow => anchorPending is null ? inner : $"""<div id="{E(anchorPending)}"></div>""" + "\n" + inner,
            LandingLayouts.Hero => $"<section{id} class=\"hero\"{label}>\n{inner}\n</section>",
            LandingLayouts.Split => $"<section{id} class=\"examples\"{label}>\n{inner}\n</section>",
            LandingLayouts.Band => $"<section{id} class=\"band\"{label}>\n{inner}\n</section>",
            _ => $"<section{id}{label}>\n{inner}\n</section>",
        };
    }

    private static string RenderBlock(Page page, LandingBlock block, bool flow, string? faqAnchor) => block.Type switch
    {
        LandingBlocks.Text => RenderText(page, block),
        LandingBlocks.Subscribe => RenderSubscribe(page, block),
        LandingBlocks.Hint => RenderHint(page, block),
        LandingBlocks.Screenshot => RenderScreenshot(page, block),
        LandingBlocks.Timeline => RenderTimeline(page, block),
        LandingBlocks.Social => RenderSocial(page, block),
        LandingBlocks.Discovery => DiscoveryEndpoints.RenderLandingPreview(page.Ru, page.Discovery),
        LandingBlocks.Tools => RenderTools(page, block, flow),
        LandingBlocks.Pricing => RenderPricing(page, block),
        LandingBlocks.Faq => RenderFaq(page, block, flow, faqAnchor),
        LandingBlocks.Download => RenderDownload(page, block),
        _ => "",
    };

    // ADR-323 §7 — the one piece of markup an admin's text may carry is a line break in a headline.
    private static string Headline(string text) =>
        E(text).Replace("&lt;br&gt;", "<br>").Replace("&lt;br/&gt;", "<br>").Replace("&lt;br /&gt;", "<br>");

    private static string RenderText(Page page, LandingBlock block)
    {
        var title = page.Field(block, "title");
        var body = page.Field(block, "body");
        var paragraph = body.Length == 0 ? "" : $"<p>{E(body)}</p>";
        switch (block.Style)
        {
            case "hero":
                var kicker = page.Field(block, "kicker");
                return string.Join("\n", new[]
                {
                    kicker.Length == 0 ? "" : $"""<div class="kicker-row"><span class="stamp">{E(kicker)}</span></div>""",
                    $"<h1>{Headline(title)}</h1>",
                    body.Length == 0 ? "" : $"""<p class="hero-sub">{E(body)}</p>""",
                }.Where(part => part.Length > 0));
            case "heading":
                return $"""<div class="section-heading"><h2>{E(title)}</h2>{paragraph}</div>""";
            case "rule":
                var meta = page.Field(block, "meta");
                return $"""
                    <div class="rule">
                        <h2>{E(title)}</h2>
                        {(meta.Length == 0 ? "" : $"""<span class="meta">{E(meta)}</span>""")}
                    </div>
                    """;
            case "copy":
                var label = page.Field(block, "linkLabel");
                var link = label.Length > 0 && LandingDocument.IsSafeLink(block.Url)
                    ? $"""<a href="{E(block.Url!)}">{E(label)} &rarr;</a>"""
                    : "";
                return $"""
                    <div class="example-copy">
                        <h2>{E(title)}</h2>{paragraph}
                        {link}
                    </div>
                    """;
            default:
                return $"<h2>{E(title)}</h2>{paragraph}";
        }
    }

    private static string WaitlistForm(Page page, string button, string hint) => $"""
        <form class="waitlist" id="waitlist-form">
            <input type="email" name="email" required maxlength="254" autocomplete="email" placeholder="you@studio.dev" aria-label="{LandingTexts.Email(page.Ru)}" aria-describedby="waitlist-note">
            <input type="text" name="website" class="hp" tabindex="-1" autocomplete="off" aria-hidden="true">
            <button class="btn btn-pine" type="submit">{button}</button>
        </form>
        <p class="drop" id="waitlist-note" role="status">{hint}</p>
        """;

    private static string RenderSubscribe(Page page, LandingBlock block)
    {
        var button = E(page.Field(block, "button"));
        // The page's scripts find the form by id, so only the first one can be the form.
        if (block.Style != "form" || page.FormDrawn)
            return $"""<a class="btn btn-paper" href="#waitlist" data-waitlist>{button}</a>""";

        page.FormDrawn = true;
        var proof = page.Field(block, "proof");
        var note = page.Field(block, "note");
        return $"""
            <div id="waitlist" class="wait-wrap">
                {WaitlistForm(page, button, E(page.Field(block, "hint")))}
                {(proof.Length == 0 ? "" : $"""<div class="proof">{E(proof)}</div>""")}
                {(note.Length == 0 ? "" : $"""<div class="note">{E(note)}</div>""")}
            </div>
            """;
    }

    private static string RenderHint(Page page, LandingBlock block)
    {
        if (block.Items.Count == 0) return "";
        if (block.Style == "cards")
            return $"""
                <div class="benefits">
                {string.Join("\n", block.Items.Select(item => $"""<div class="benefit">{Icons.Svg(item.Icon ?? "", 32)}<div><h3>{E(page.Field(item, "title"))}</h3><p>{E(page.Field(item, "text"))}</p></div></div>"""))}
                </div>
                """;
        return $"""
            <ol class="workflow">
                {string.Join("", block.Items.Select((item, i) => $"""
                <li><span class="step-number" aria-hidden="true">{i + 1}</span><div><h3>{E(page.Field(item, "title"))}</h3><p>{E(page.Field(item, "text"))}</p></div></li>
                """))}
            </ol>
            """;
    }

    private static string RenderScreenshot(Page page, LandingBlock block)
    {
        var shots = block.Items
            .Select(item => (Url: ImageUrl(item.File), Caption: page.Field(item, "caption")))
            .Where(shot => shot.Url is not null)
            .Select(shot => (Url: E(shot.Url!), shot.Caption,
                Alt: E(shot.Caption.Length == 0 ? LandingTexts.ViewScreen.Pick(page.Ru) : shot.Caption)))
            .ToList();

        if (block.Style == "hero")
        {
            if (shots.Count == 0) return "";
            var shot = shots[0];
            return $"""
                <figure class="hero-shot">
                    <img src="{shot.Url}" alt="{shot.Alt}" fetchpriority="high">
                    {(shot.Caption.Length == 0 ? "" : $"""<figcaption class="cap">{E(shot.Caption)}</figcaption>""")}
                </figure>
                """;
        }

        var id = page.GalleryDrawn ? "" : """ id="shots" """.TrimEnd();
        page.GalleryDrawn = true;
        return $"""
            <div{id} class="gallery">
                {string.Join("", shots.Select(s => $"""
                <figure>
                    <a href="{s.Url}"><img src="{s.Url}" alt="{s.Alt}" loading="lazy"></a>
                    {(s.Caption.Length == 0 ? "" : $"""<figcaption class="cap">{E(s.Caption)}</figcaption>""")}
                </figure>
                """))}
            </div>
            """;
    }

    private static string RenderTimeline(Page page, LandingBlock block)
    {
        if (block.Items.Count == 0) return "";
        if (block.Style == "board")
            return $"""
                <div class="shelves">
                    {string.Join("", block.Items.Select(column => $"""
                    <div class="shelf">
                        <div class="shelf-head"><b>{E(page.Field(column, "title"))}</b><span class="spacer"></span><span class="n">{column.Entries.Count}</span></div>
                        <div class="shelf-sheet"><ul>
                            {string.Join("", column.Entries.Select(entry => $"""
                            <li><span class="mark-{MarkClass(column.Mark)}">{Icons.Svg(MarkIcon(column.Mark), 15)}</span><span>{E(page.T(entry))}</span></li>
                            """))}
                        </ul></div>
                    </div>
                    """))}
                </div>
                """;

        var image = ImageUrl(block.Image);
        return $"""
            <div class="story">
                <div class="timeline">
                    {string.Join("", block.Items.Select(step => $"""
                    <div class="step">
                        <div class="when">{E(page.Field(step, "when"))}</div>
                        <b>{E(page.Field(step, "title"))}</b>
                        <p>{E(page.Field(step, "text"))}</p>
                    </div>
                    """))}
                </div>
                {(image is null ? "" : $"""
                <figure class="paper tight pinned" >
                    <img src="{E(image)}" alt="" loading="lazy">
                </figure>
                """)}
            </div>
            """;
    }

    private static string RenderSocial(Page page, LandingBlock block) =>
        $"""<div class="network-strip"><b>{E(page.Field(block, "lead"))}</b><span>{LandingTexts.Blog(page.Ru)}</span>{string.Join("", PublishNetworks.All.Select(n => $"<span>{E(n)}</span>"))}<span>RSS</span></div>""";

    // ADR-321 / T-413 — a grid of tool buttons on the left and one detail pane on the right;
    // hovering, focusing or clicking a button shows its tool. Without a script the first shows.
    private static string RenderTools(Page page, LandingBlock block, bool flow)
    {
        // The tab and panel ids are the page's, so a second Tools block would repeat them.
        if (page.ToolsDrawn || page.Doc.Features.Count == 0) return "";
        page.ToolsDrawn = true;
        var features = page.Doc.Features;
        var view = E(LandingTexts.ViewScreen.Pick(page.Ru));
        var toolButtons = string.Join("", features.Select((f, index) => $"""
            <button type="button" class="tool-btn" role="tab" id="tool-tab-{index}" aria-controls="tool-panel-{index}" aria-selected="{(index == 0 ? "true" : "false")}" tabindex="{(index == 0 ? "0" : "-1")}">{Icons.Svg(f.Icon, 24)}<span>{E(page.T(f.Title))}</span></button>
            """));
        var toolPanels = string.Join("", features.Select((f, index) =>
        {
            var shot = ImageUrl(f.Shot) is { } url ? E(url) : null;
            var size = shot is not null && shot.StartsWith("/assets/review/", StringComparison.Ordinal) ? """ width="1057" height="891" """.TrimEnd() : "";
            return $"""
                <div class="tool-panel" role="tabpanel" id="tool-panel-{index}" aria-labelledby="tool-tab-{index}"{(index == 0 ? "" : " hidden")}>
                    <h3>{E(page.T(f.Title))}</h3><p>{E(page.T(f.Body))}</p>
                    {(shot is null ? "" : $"""
                    <a href="{shot}">
                        <img src="{shot}" alt="{view}" loading="lazy"{size}>
                    </a>
                    """)}
                </div>
                """;
        }));
        var tag = flow ? "section" : "div";
        return $"""
            <{tag} class="tools" aria-labelledby="tools-title">
                <h3 id="tools-title">{E(page.Field(block, "title"))} · {features.Count}</h3>
                <div class="tools-layout"><div class="tool-grid" role="tablist" aria-orientation="vertical" aria-labelledby="tools-title">{toolButtons}</div><div class="tool-detail">{toolPanels}</div></div>
            </{tag}>
            """;
    }

    // ADR-323 §6 — every figure here is read from the code that enforces it; the block's options
    // only say which parts of the table to show.
    private static string RenderPricing(Page page, LandingBlock block)
    {
        var ru = page.Ru;
        var plans = new (string Name, string Price, string Per, string For, string[] Lines)[]
        {
            (LandingTexts.Free(ru), "$0", LandingTexts.Forever(ru),
                LandingTexts.OneChannelAndYourOwnBlog(ru),
                [
                    LandingTexts.SingleChannelLimit(ru, PlanLimitations.MaxChannels(PlanTiers.Free)),
                    LandingTexts.MediaLimit(ru, Gb(PlanTiers.Free)),
                    LandingTexts.BlogCommentsReactionsRSS(ru),
                    LandingTexts.FreeSignature(ru),
                ]),
            ("Pro", $"${Consts.Plans.ProPrice}", LandingTexts.PerMonth(ru),
                LandingTexts.SeveralChannelsAndYourOwnVoice(ru),
                [
                    LandingTexts.ProChannelLimit(ru, PlanLimitations.MaxChannels(PlanTiers.Pro)),
                    LandingTexts.MediaLimit(ru, Gb(PlanTiers.Pro)),
                    LandingTexts.YourOwnSignatureWithLink(ru),
                    LandingTexts.HeaderSlotLimit(ru, PlanLimitations.MaxHeaderSlots(PlanTiers.Pro)),
                ]),
            ("Pro+", $"${Consts.Plans.ProPlusPrice}", LandingTexts.PerMonth(ru),
                LandingTexts.WithAITranslationAndEditing(ru),
                [
                    LandingTexts.ProPlusChannelLimit(ru, PlanLimitations.MaxChannels(PlanTiers.ProPlus)),
                    LandingTexts.MediaLimit(ru, Gb(PlanTiers.ProPlus)),
                    LandingTexts.DailyAiLimit(ru, PlanLimitations.AiDailyLimit),
                    LandingTexts.EverythingInPro(ru),
                ]),
        };
        var check = Icons.Svg("check", 15);
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
        return $"""
            <div class="plans">{planCards}</div>
            {(block.Options.GetValueOrDefault(LandingBlocks.ShowNotes) ? $"""<p class="plan-foot">{LandingTexts.SeparateCredits.Pick(ru)}<br>{LandingTexts.TrialPrice(ru, Consts.Plans.TrialPrice)}</p>""" : "")}
            {(block.Options.GetValueOrDefault(LandingBlocks.ShowComparison) ? $"""<details class="comparison-toggle"><summary>{LandingTexts.ComparePlans.Pick(ru)}</summary>{comparison}</details>""" : "")}
            """;
    }

    private static string RenderFaq(Page page, LandingBlock block, bool flow, string? anchor)
    {
        var items = string.Join("", block.Items.Select(item =>
            $"""<details><summary>{E(page.Field(item, "question"))}</summary><p>{E(page.Field(item, "answer"))}</p></details>"""));
        var open = flow
            ? $"""<section class="faq"{(anchor is null ? "" : $""" id="{E(anchor)}" """.TrimEnd())}>"""
            : """<div class="faq">""";
        return $"""
            {open}<h2>{E(page.Field(block, "title"))}</h2>
                {items}
            </{(flow ? "section" : "div")}>
            """;
    }

    private static string RenderDownload(Page page, LandingBlock block)
    {
        var body = page.Field(block, "body");
        var meta = page.Field(block, "meta");
        return $"""
            <div class="paper bright download-card" >
                <div class="lead">
                    <b>{E(page.Field(block, "title"))}</b>
                    {(body.Length == 0 ? "" : $"<p>{E(body)}</p>")}
                    {(meta.Length == 0 ? "" : $"""<div class="download-meta">{E(meta)}</div>""")}
                </div>
                <a class="btn btn-pine" href="/downloads/latest">{Icons.Svg("download-simple")}{E(page.Field(block, "button"))}</a>
            </div>
            """;
    }

    private static string MarkClass(string? mark) => mark switch
    {
        "done" => "done",
        "doing" => "doing",
        _ => "next",
    };

    private static string MarkIcon(string? mark) => mark switch
    {
        "done" => "check",
        "doing" => "clock",
        _ => "plus",
    };
}
