# Design

Source of truth for all values below: `cedarclerk-web/src/styles.scss` (264 lines, the only global stylesheet — there is no separate tokens file). Component-scoped CSS lives alongside each component (`editor.component.css`, `settings.component.css`, etc.) under Angular's default view encapsulation.

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

### Typography
```
--font-sans: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
--font-mono: ui-monospace, Menlo, Consolas, monospace;
```
**There is no font-size scale token.** Sizes are hardcoded per usage — e.g. editor `.tiptap h1 { font-size: 1.625em }`, `h2 { font-size: 1.3125em }`, inline code `14px`, modal body text `13px`, toast text `13px / weight 500`. This is a real gap, not an oversight to silently "fix" here — flagging it as known debt.

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

- No font-size scale — see Typography above.
- `.btn-*`/`.modal-*` duplicated with drifted values across `editor.component.css` and `settings.component.css` instead of a shared component.
- `--shadow-md` exists as a token but isn't consistently used — some components (modal, toast) hardcode their own box-shadow values instead.

> TODO (Marty): is there a target design-system tool (Figma, Claude Design file) that should be the source of truth going forward, or is `styles.scss` itself the canonical source? The repo history references Claude-Design-generated mockups (`docs/Cedar Clerk Editor.dc.html` etc., now archived to `_Documents_/CedarClerk/OLD/Design/`) as the origin of the current token set — worth confirming whether that pipeline is still active for future design work.
