# Design

Source of truth for all values below: `cedarclerk-web/src/styles.scss` (264 lines, the only global stylesheet — there is no separate tokens file). Component-scoped CSS lives alongside each component (`editor.component.css`, `settings.component.css`, etc.) under Angular's default view encapsulation.

## Principles (ADR-071, 31.07.2026)

The visual direction, decided by Marty as the answer to Q-11 and binding on Phase 11. Rationale is in ADR-071; what follows is the rule, not the argument.

1. **Warm editorial is the base.** Paper and wood neutrals, olive accent, generous reading measure. This is a continuation of the 08.07.2026 "Cabin" token set below, not a replacement for it — the palette already expresses this direction, and the work is making every screen honour it.
2. **One palette, one type scale, product-wide.** There is no second colour set for any screen, ever. Difference between screens is expressed only in spacing, radius and size tokens.
3. **Density is a surface mode, not a component choice.** A page root opts into `[data-density="compact"]` (same mechanism as `data-theme` on `<html>`); tokens inside resolve tighter — row spacing, control padding, `--radius-md`→`--radius-sm`, and borders rather than shadows for separation. Compact: `/posts`, `/drafts`, `/admin`, `/stats`. Comfortable (default): editor sheet, blog, private-post gate. **A component must never hardcode its density** — several are used on both kinds of screen.
4. **Serif for content, sans for chrome.** Blog post body and editor sheet get a serif; toolbars, tables, forms, menus and every control stay on `--font-sans`.
5. **Light-first, dark as a peer.** Every token added gets both light and dark values in the same edit, never "dark later".
6. **The blog is inside the system.** `BlogEndpoints.cs` maintains its own `:root` and 21 hex literals, `CedarToBlogHtmlRenderer` another 6 — a hand-kept duplicate token set serving roughly half of what a reader sees. Tokens v2 must reach the server-rendered surfaces too.
7. **Zero hardcoded values in components** (T-077). The gap, measured 31.07.2026: **314** hardcoded `font-size` declarations across component CSS against the `--fs-*` scale, plus 44 hex literals in `styles.scss`. **110 of the 314 are half-pixel values** (44×`12.5px`, 30×`11.5px`, 16×`10.5px`, 14×`13.5px`), and 79% of all UI text sits in an 11–13.5px band — the type hierarchy is five barely-distinguishable sizes crowded into 2.5px. **The v2 scale is integers only**; half-steps collapse to the nearest whole pixel.

## Tokens

### Color — light (`:root`)
```
--bg: #ECE9E2;       --canvas: #E2DED4;    --surface: #F7F5EF;
--sheet: #FCFBF8;    --alt: #EFECE4;       --border: #DBD5C8;
--text: #26231D;     --t2: #6B655A;        --t3: #9F988A;
--accent: #5B6E46;   --danger: #B4452C;    --ok: #3E7A4E;
--shadow: 0 1px 3px rgba(40, 35, 25, .10);
--shadow-md: 0 8px 24px rgba(40, 35, 25, .12);
--asoft: color-mix(in srgb, var(--accent) 13%, var(--surface));
--abord: color-mix(in srgb, var(--accent) 38%, var(--border));
```

### Color — dark (`:root[data-theme="dark"]`)
Overrides only the listed properties; everything else (`--shadow-md`, `--asoft`, `--abord`, radius, spacing, fonts) is inherited unchanged from `:root`:
```
--bg: #1D1B17;        --canvas: #171511;    --surface: #25221B;
--sheet: #2B2820;     --alt: #2F2C23;       --border: #3C382D;
--text: #EAE6DB;      --t2: #A69F8F;        --t3: #776F5F;
--accent: color-mix(in srgb, #5B6E46 55%, #E8F0E8 45%);
--danger: #E2745C;    --ok: #82BB8C;
--shadow: 0 1px 3px rgba(0, 0, 0, .45);
```

Theme is applied by `ThemeService` (`cedarclerk-web/src/app/core/theme.service.ts`): a signal-backed `Theme = 'light' | 'dark'`, persisted to `localStorage` (key `cedar-theme`), falling back to the `prefers-color-scheme: dark` media query, applied by setting `document.documentElement.dataset['theme']` — i.e. a `data-theme` attribute on `<html>`, matched by the `:root[data-theme="dark"]` selector above. Toggled via a ☾/☀ control in the editor topbar and both auth pages.

### Radius
```
--radius-sm: 6px;   --radius-md: 10px;   --radius-lg: 14px;
```

### Spacing
```
--space-1: 4px;  --space-2: 8px;  --space-3: 12px;
--space-4: 16px; --space-5: 24px; --space-6: 32px;
```
Roughly a ×2 progression, not a strict 4px-multiple ramp.

### Density (tokens v2, ADR-071)
Comfortable is the default, declared in `:root`; `[data-density="compact"]` overrides it on a page root. **Not one colour differs between the two** — density is spacing, size, radius and separation only.
```
                    comfortable        compact
--dens-row-y        10px               6px
--dens-control-y    7px                5px
--dens-control-x    14px               10px
--dens-gap          var(--space-3)     var(--space-2)
--dens-section      var(--space-5)     var(--space-4)
--dens-radius       var(--radius-md)   var(--radius-sm)
--dens-fs           var(--fs-body)     var(--fs-ui)
--dens-elev         var(--shadow)      none
```

### Typography
```
--font-sans:  -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
--font-mono:  ui-monospace, Menlo, Consolas, monospace;
--font-serif: ui-serif, Georgia, "Iowan Old Style", "Source Serif Pro", "Times New Roman", serif;
```
`--font-serif` (added 31.07.2026, tokens v2) is for reading surfaces **only** — blog post body and the editor sheet. A system stack on purpose: the Pi serves every byte itself, and adding a downloaded face is a performance/licensing decision nobody has made.

Font-size scale — added 27.07.2026 (ADR-052), extended 31.07.2026 (ADR-071) to `--fs-9/10/11/12/13/14/15/16/17/18/19/20/22/27`. **Integers only**: the 10/15/17/18/22 steps were added because they are measured, in-use sizes; the half-pixel sizes found in the sweep are not tokenized and collapse to the nearest integer.

Semantic roles sit on top, and components reach for **these**, not the numbers — the numbers are the palette, the roles are the meaning, and the roles are what the density switch moves:
```
--fs-caption: var(--fs-11);   labels above a field, timestamps
--fs-meta:    var(--fs-12);   secondary row data, counts
--fs-ui:      var(--fs-13);   buttons, menu items, table cells
--fs-body:    var(--fs-14);   default UI text
--fs-title:   var(--fs-19);   page and section titles
--fs-read:    var(--fs-17);   reading surfaces
--lh-read:    1.7;
```

### Icons (ADR-072)
```
--icon-sm: 15px;   --icon-md: 18px;   --icon-lg: 20px;
```
These are exactly the three values `.icon` is currently declared with across 9 files — the inconsistency is why they became tokens. Set: **Phosphor**, delivered as inlined SVG behind one `app-icon` component (T-079 not yet done; Lucide is still what renders).

### Motion
```
--motion-fast: 120ms;   state feedback on something already under the cursor
--motion-base: 180ms;   something appearing or moving
--motion-slow: 280ms;   a full-surface change the eye must follow
--ease: cubic-bezier(.2, .6, .3, 1);
```
All three drop to 1ms under `prefers-reduced-motion: reduce`, which also clamps every animation/transition globally — 1ms rather than 0 so `transitionend` listeners still fire.

## Component patterns (convention, not enforced)

There is **no shared component library** for buttons or modals — `.btn-accent`, `.btn-ghost`, `.modal-overlay`, `.modal-card`, `.modal-head`, `.modal-body`, `.modal-actions` are defined once inside `editor.component.css` (component-scoped, `ViewEncapsulation.Emulated`) and copy-pasted independently into `settings.component.css` with *different* values (e.g. `.btn-ghost` padding is `7px 14px` in the editor vs `5px 12px` in settings). `login.component.css` has neither class — its own separate button styling. `shared/` only contains `PopoverComponent` and `CedarLogoComponent`, neither of which is a button/modal abstraction.

De-facto pattern from `editor.component.css` (lines 458–537), useful as a reference if/when this gets formalized into a real shared component:
```css
.modal-overlay {
    position: fixed; inset: 0;
    background: rgba(24, 21, 16, .45);
    display: flex; align-items: center; justify-content: center;
    z-index: 100;
}
.modal-card {
    width: 380px;
    background: var(--sheet);
    border: 1px solid var(--border);
    border-radius: var(--radius-lg);
    box-shadow: 0 24px 64px rgba(0, 0, 0, .3);  /* not var(--shadow-md) — untokenized */
    padding: 22px 24px;
    color: var(--text);
}
.btn-accent {
    border: none; background: var(--accent); color: #F4F2EA;
    border-radius: var(--radius-sm);
    padding: 7px 16px; font-size: 13px; font-weight: 600;
    cursor: pointer; font-family: inherit;
}
.btn-accent:hover { filter: brightness(1.08); }
.btn-ghost {
    border: 1px solid var(--border); background: none; color: var(--t2);
    border-radius: var(--radius-sm);
    padding: 7px 14px; font-size: 13px;
    cursor: pointer; font-family: inherit;
}
.btn-ghost:hover { color: var(--text); border-color: var(--t3); }
```
Accent-filled = primary action, outlined ghost = secondary — that's the convention, enforced only by copy-paste today.

Channel colors are a separate hardcoded array in `editor.component.ts`, not tokenized: `['#C98A3B', '#5B6E46', '#3E7A4E', '#B4452C', '#6EB2F0', '#8A6FBF']`.

## Editor content styles (`.tiptap` block, `styles.scss`)

Global (not component-scoped, since TipTap content is rendered via `innerHTML` in places): headings in `em` units so they scale with the editor's zoom control (fixed from a past bug — zoom used to be silently overridden by a hardcoded `font-size: 16px`), `blockquote` with a `3px solid var(--abord)` left border, inline `code`/`pre` with `var(--font-mono)`, `tg-spoiler` (spoiler mark → hidden text via `background: var(--t3); color: transparent`, revealed on hover), `.datetime-pill`, `.annotation-block` (comment/reaction anchor), `.toggle-block`, `.media-with-caption`, `.footnote-badge` — one block per custom TipTap node/mark in `tiptap-extensions/`.

## Known design debt

- Font-size scale (`--fs-*`) exists but isn't adopted outside the new shared header — see Typography above.
- `.btn-*`/`.modal-*` duplicated with drifted values across `editor.component.css` and `settings.component.css` instead of a shared component.
- `--shadow-md` exists as a token but isn't consistently used — some components (modal, toast) hardcode their own box-shadow values instead.

> TODO (Marty): is there a target design-system tool (Figma, Claude Design file) that should be the source of truth going forward, or is `styles.scss` itself the canonical source? The repo history references Claude-Design-generated mockups (`docs/Cedar Clerk Editor.dc.html` etc., now archived to `_Documents_/CedarClerk/OLD/Design/`) as the origin of the current token set — worth confirming whether that pipeline is still active for future design work.
