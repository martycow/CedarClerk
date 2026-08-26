---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: design-system principles and the token map (values live in styles.scss)
guard: none
---

# Design

Source of truth for all values below: `cedarclerk-web/src/styles.scss`, and both of its base blocks are the whole of it — there is no partial and no attribute scope holding a second set (ADR-136). `styles/_forest.scss` is still `@use`d and still compiles into the bundle, but every rule in it is scoped under a `data-skin` attribute nothing sets any more, so it can match nothing and declares nothing; it is neutralised and pending deletion (`T-235`). Component-scoped CSS lives alongside each component (`editor.component.css`, `settings.component.css`, etc.) under Angular's default view encapsulation. **When this file and `styles.scss` disagree, `styles.scss` wins.**

## Principles (ADR-071, 31.07.2026)

The visual direction, decided by Marty as the answer to Q-11 and binding on Phase 11. Rationale is in ADR-071; what follows is the rule, not the argument.

1. **Warm editorial is the base.** Paper and wood neutrals, olive accent, generous reading measure. The values below are Cedar Bench (ADR-136), which carries this direction on rather than turning from it: the "Cabin" set it grew out of was rewritten in place, not kept beside it.
2. **One palette, one type scale, product-wide.** There is no second colour set for any screen, ever. Difference between screens is expressed only in spacing, radius and size tokens.
3. **Density is a surface mode, not a component choice.** A page root opts into `[data-density="compact"]` (same mechanism as `data-theme` on `<html>`); tokens inside resolve tighter — row spacing, control padding, `--radius-md`→`--radius-sm`, and borders rather than shadows for separation. Compact: `/posts`, `/drafts`, `/admin` and the Phase 13 list screens (projects list, tasks board, planner, assets, builds). Comfortable (default): editor sheet, project dashboard, blog, private-post gate. **A component must never hardcode its density** — several are used on both kinds of screen.
4. **Serif for content, sans for chrome — content means the blog.** The blog post body gets a serif; toolbars, tables, forms, menus and every control stay on `--font-sans`. **The editor sheet is explicitly excluded** (narrowed 31.07.2026, ADR-073): its typeface is already a user setting with a serif option, so the design system does not get to pick it.
5. **Light-first, dark as a peer.** Every token added gets both light and dark values in the same edit, never "dark later".
6. **The blog is inside the system.** `BlogEndpoints.cs` maintains its own `:root` and 21 hex literals, `CedarToBlogHtmlRenderer` another 6 — a hand-kept duplicate token set serving roughly half of what a reader sees. Tokens v2 must reach the server-rendered surfaces too.
7. **Zero hardcoded values in components** (T-077). The gap, measured 31.07.2026: **314** hardcoded `font-size` declarations across component CSS against the `--fs-*` scale, plus 44 hex literals in `styles.scss`. **110 of the 314 are half-pixel values** (44×`12.5px`, 30×`11.5px`, 16×`10.5px`, 14×`13.5px`), and 79% of all UI text sits in an 11–13.5px band — the type hierarchy is five barely-distinguishable sizes crowded into 2.5px. **The v2 scale is integers only**; half-steps collapse to the nearest whole pixel.

## Tokens

### Color — light (`:root`)
These are the **contract names** (ADR-137): the closed list `tools/check-contrast.mjs` scores, `tools/generate-design-tokens.mjs` copies into the blog and the landing page, `DesignTokenDriftTests` pins, and `appearance.service.ts` rewrites for `--accent`. See "Accessibility" below for the contract each one carries:
```
--bg: #F3EDDE;       --canvas: #E9E1CC;    --surface: #F1EADA;
--sheet: #FAF6EC;    --alt: #F5F0E1;       --border: #CDC1A8;
--text: #2C251A;     --t2: #5B513E;        --t3: #8C7F66;
--wood-ink: #3B2A18;
--accent: #39543C;   --danger: #9E3D27;    --ok: #356842;
--warn: #7A5520;     --border-strong: #8A6F4C;
--series-1: #39543C; --series-2: #3E5A76;  --series-3: #A8721F;
--series-4: #6B7F4A; --series-5: #7A5288;  --series-6: #1F7A6E;
--shadow: 0 1px 3px rgba(58, 38, 16, .14);
--asoft: color-mix(in srgb, var(--accent) 13%, var(--surface));
--abord: color-mix(in srgb, var(--accent) 38%, var(--border));
```

The contract's non-colour carriers are documented where they belong rather than repeated here: `--font-sans`/`--font-mono`/`--font-serif` and `--fs-read`/`--lh-read` under Typography, `--radius-sm`/`--radius-md`/`--radius-lg` under Radius. The list itself lives in `tools/contract-tokens.mjs`, read by both the checker and the generator; that file and the names above are one list stated twice, and whichever is the narrower is the one that is wrong (ADR-137 §1).

Beside them, in the same block and outside the contract — these never cross to a server-rendered surface:
```
--shadow-md: 0 8px 24px rgba(58, 38, 16, .18);
--ok-soft: color-mix(in srgb, var(--ok) 13%, var(--surface));
--warn-soft: color-mix(in srgb, var(--warn) 13%, var(--surface));
--danger-soft: color-mix(in srgb, var(--danger) 13%, var(--surface));
```

The three `-soft` washes are `--asoft` for the state colours (ADR-145): a badge is its own ink on a
tint of itself, and before they existed that tint was a per-component `color-mix` over whichever
paper the component sat on — one idea shipping as twenty-two ratios, none of them measured. They mix
into `--surface`, the deepest paper, so the wash is one flat colour wherever it lands.

`--surface` is the deepest of the three papers, so it is the surface every dark-on-light ratio is measured against — not `--sheet`.

The same block carries what the list above does not name: `--shadow-lg` (the modal shadow), `--hover`/`--hover-strong`/`--hover-danger` (hover tints mixed from `--text`, so they follow the theme), `--scrim` (modal backdrop), the `-soft` washes and `--shadow-md` above, the Cedar Aero glass set (`--blur`, `--glass`, `--glass-strong`, `--glass-border`, `--gloss-top`, `--glow-accent`), and the **bench material names** — `--wood-*`, `--wall-*`, `--rail-*`, `--paper-*`, `--pine-*`, `--brass-*`, `--leaf-*`, the textures, the gradients and the `--bench-*` dimensions. A material name answers "what is this made of", a contract name "what is this for"; where both name one swatch the material aliases onto the contract, never the reverse, and a component reaching for a role uses the contract name (ADR-137).

### Color — dark (`:root[data-theme="dark"]`)
Overrides only the listed properties; everything else (`--abord`, radius, spacing, fonts) is inherited unchanged from `:root`:
```
--bg: #2E2718;        --canvas: #251F13;    --surface: #D9CEAE;
--sheet: #E6DCC2;     --alt: #DFD4B6;       --border: #BCA98A;
--text: #241E13;      --t2: #564C35;        --t3: #7F7257;
--wood-ink: #E3D3AE;
--danger: #85311E;    --ok: #2D5738;        --warn: #68491B;
--border-strong: #7E6440;
--asoft: color-mix(in srgb, var(--accent) 10%, var(--surface));
--ok-soft: color-mix(in srgb, var(--ok) 10%, var(--surface));
--warn-soft: color-mix(in srgb, var(--warn) 10%, var(--surface));
--danger-soft: color-mix(in srgb, var(--danger) 10%, var(--surface));
--series-3: #97661C;  --series-4: #647645;
--shadow: 0 1px 3px rgba(0, 0, 0, .4);
--shadow-md: 0 8px 24px rgba(0, 0, 0, .44);
--shadow-lg: 0 24px 64px rgba(0, 0, 0, .6);
--scrim: rgba(10, 7, 3, .6);
```

**Night darkens the wood, not the paper** (ADR-141), so two ink polarities travel in opposite directions inside this one block: the paper tokens step towards black while the wall and rail tokens step towards white. A contributor's instinct about a dark theme is wrong here in both directions. `--accent` is deliberately not restated — pine already clears its floor on night cream; only the two chart series that do not are stepped down. The material families move with their own halves: `--wood-*`, `--rail-*`, `--brass-*` and `--rule-ink*` are redeclared here, `--wood-ink` being the one ink that flips, because the wall is dark at night and nothing but chrome draws on it.

**Brass is two tokens by job.** `--brass-lo` is the metal — the dark end of a hook shaft or a pin, the kit's `#8A6226` / night `#7E5A20` — and no pair measures it, because nothing is written in it. `--brass-ink` (`#674A1C` / `#684A1A`) is what a stamp or a chart tick writes with, derived against its wash and the graph crossing (ADR-145), and `--brass-soft` mixes from the ink. A rule that needs brass to be read reaches for the ink; one that needs it to be seen reaches for the metal. The rail carries `--rail-ink-dim` for a resting index tab, the two translucent faces `--rail-btn-face` / `--rail-btn-face-hover` and `--hook-face` are tints that need wood under them (ADR-175), and the shelf and sheet shadows are named — `--shadow-sheet`, `--shadow-tag`, `--shadow-worktop` and its inset — rather than written per component. The dried leaf has its own stock: `--leaf-dried-bg` / `-edge` / `-ink`, one value in both themes, also the idle face of a pickable leaf (ADR-176).

Theme is applied by `ThemeService` (`cedarclerk-web/src/app/core/theme.service.ts`): a signal-backed `Theme = 'light' | 'dark'`, persisted to `localStorage` (key `cedar-theme`), falling back to the `prefers-color-scheme: dark` media query, applied by setting `document.documentElement.dataset['theme']` — i.e. a `data-theme` attribute on `<html>`, matched by the `:root[data-theme="dark"]` selector above. Toggled via a ☾/☀ control in the editor topbar, both auth pages, and the Appearance panel. It is the only styling axis the app has — there is one look (ADR-136), and light/dark is the whole of the choice.

### Radius
```
--radius-sm: 4px;   --radius-md: 8px;   --radius-lg: var(--radius-md);   --radius-shelf: 5px;
```
Paper is cut square and wood is eased: 4px is a field, 8px a plaque. Nothing on the bench is rounder than a plaque, so `--radius-lg` resolves to `--radius-md` — the name stays for the cards and modals that reach for it, the step above a plaque does not exist. `--radius-shelf` is the shelf board's own corner. The material aliases `--radius-field`, `--radius-plaque` (`--radius-sm`/`--radius-md`) and the flat `--radius-stamp`/`--radius-paper` at 3px live beside them.

### Spacing
```
--space-1: 4px;  --space-2: 8px;  --space-3: 12px;  --space-4: 16px;
--space-5: 24px; --space-6: 32px; --space-7: 34px;  --space-8: 48px;
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
--font-sans:    'Source Sans 3', -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, system-ui, sans-serif;
--font-mono:    'Martian Mono', ui-monospace, Menlo, Consolas, monospace;
--font-serif:   'Literata', Georgia, "Iowan Old Style", serif;
--font-display: 'Vollkorn', Georgia, serif;
--font-note:    'Caveat', cursive;
```
**Dates and times are always Pacific** (ADR-115), formatted through the `zonedDate` pipe — never
`| date:`, which follows whatever zone the machine is in and misreads the server's timestamps.
Patterns stay the ones already in use (`d MMM`, `d MMM, HH:mm`, `d MMM yyyy, HH:mm`). The zone is
spelled out (`14:05 PDT`) on the blog and left unspoken in the app: a reader could be anywhere, the
author is in one place.

`--font-serif` is for reading surfaces **only** — blog post body and the editor sheet. The five faces it and `--font-sans`/`--font-mono`/`--font-display`/`--font-note` name — Literata, Source Sans 3, Martian Mono, Vollkorn, Caveat — are self-hosted via `@fontsource` (ADR-143, ADR-191), one file per weight per named subset, so the server still serves every byte itself and the explicit list is what bounds the payload. `--font-readout` is a sixth, Departure Mono, committed rather than a package (ADR-180) and fenced to 11px call sites only — `--font-mono` covers everything else mono, code blocks included, regardless of size (ADR-191 supersedes ADR-180's rejection of that).

`--font-display` is the heading face, on the chrome and on the sheet alike: the hub's hero title at `--fs-34`, the worktop and shelf headings, and `.tiptap h1`–`h3` at `--fs-27`/`--fs-19` — a display serif over whichever body face the author chose, which is the bench's letterpress contrast and is independent of the sheet typeface preference (ADR-073 governs the body only). `--font-note` is allowed in exactly one place, the global `.margin-note` rule — `--fs-17`, `--t2`, rotated a degree and a half — for margin notes and empty states; nothing else reaches for the hand-written face. `body` is set at `--fs-ui`, so unstyled text is the control size, not the reading size.

Font-size scale (ADR-052, extended by ADR-071 and ADR-138): `--fs-9/10/11/12/13/14/15/16/17/18/19/20/21/22/27/34`. `--fs-34` is the hero title and nothing else. **Integers only** — the scale grows by measured, in-use sizes, and a half-pixel size collapses to the nearest integer. `--fs-21` is the readout size the bench chrome needs (ADR-138); `npm run check:density` fails on a half-pixel `font-size` anywhere under `src/` — in a `.css`, `.scss`, `.html` or a `styles` literal inside a `.ts` alike — and on a fractional `px` in any custom-property value there. Its reach stops at `src/`: the CSS the blog, the landing page and the single-file export write from C# is not scanned, and it still carries half-pixel sizes.

Semantic roles sit on top, and components reach for **these**, not the numbers — the numbers are the palette, the roles are the meaning, and the roles are what the density switch moves:
```
--fs-caption: var(--fs-12);   labels above a field, timestamps
--fs-meta:    var(--fs-13);   secondary row data, counts
--fs-ui:      var(--fs-14);   buttons, menu items, table cells
--fs-body:    var(--fs-15);   default UI text
--fs-title:   var(--fs-20);   page and section titles
--fs-read:    17px;           reading surfaces — a literal, not an alias
--lh-read:    1.75;
```
`--fs-read` and `--lh-read` are contract names, copied verbatim into `DesignTokens` for the blog and the landing page. `var(--fs-17)` there would arrive as nothing, because the numeric scale is outside the contract — which is why the reading role holds the number itself (ADR-137).

The bench chrome has three roles of its own, on the same scale and outside the contract (ADR-138):
```
--text-chrome:    var(--fs-13);   rail, shelf header, tab strip
--text-chrome-sm: var(--fs-11);   the smallest chrome label
--text-readout:   var(--fs-21);   the number a chrome strip exists to show
```

### Icons (ADR-072)
```
--icon-xs: 13px;   --icon-sm: 17px;   --icon-md: 20px;   --icon-lg: 24px;
```
The tokens were born as the three values `.icon` was declared with across 9 files (15/18/20 — the inconsistency is why they became tokens), then **raised one step each on 01.08.2026**; `--icon-xs` was added by the `/drafts` migration. Set: **Phosphor**, inlined SVG behind one `app-icon` component (T-079 done 31.07.2026; `@lucide/angular` is gone). What each icon *means* in this app, and where a meaning has two glyphs, is on `/dev/icons` — generated from the call sites by `npm run icons:generate`, never hand-kept (ADR-075).

A token is a nominal size, not an apparent one: Phosphor fills its 256-unit canvas to whatever width each shape wants, from 192 units to 240, so the same `--icon-sm` reads three pixels bigger on a cube than on a checked square. `tools/generate-icons.mjs` measures each glyph and emits a per-icon `viewBox` that cancels the difference, bounded so that only the tails move — ADR-214 has the rule and what it deliberately leaves alone.

### Motion
```
--motion-fast: 120ms;   state feedback on something already under the cursor
--motion-base: 180ms;   something appearing or moving
--motion-slow: 280ms;   a full-surface change the eye must follow
--ease: cubic-bezier(.2, .6, .3, 1);
```
The bench kit brings its own three, by what moves rather than by how far the eye travels, plus the two settle curves its hardware uses:
```
--dur-tap: 150ms;       a press — a button face, a hook, a leaf
--dur-control: 190ms;   a control changing state — a field focusing, a tab lighting
--dur-move: 250ms;      something travelling — a drawer, a shelf, a panel
--dur-publish: 600ms;   the one long one: the resin drop setting after a publish
--ease-settle / --ease-swing   overshoot curves for brass and resin
```
All of them drop to 1ms under `prefers-reduced-motion: reduce`, which also clamps every animation/transition globally — 1ms rather than 0 so `transitionend` listeners still fire.

## Accessibility (T-082, ADR-074)

The rules below are checked, not remembered: `cedarclerk-web/e2e/12-a11y.spec.ts` fails the smoke suite if any of them regresses.

**Contrast.** `npm run check:contrast` reads the tokens straight out of `styles.scss`, resolves the `color-mix()` derivations, composites alpha over the backdrop a pair is measured against — the page ground it bottoms out on is read off the `body` rule, not off `--bg`, because body paints paper while `--bg` is the wall and at night they are opposite poles (ADR-141) — and grades a table of pairs in both themes. Every pair whose value graph runs through the accent is scored again against each of the five presets `appearance.service.ts` offers, and reported at its worst with the preset named. It also refuses a gradient on a contract name outright, printing the alias chain that leads to it — that value ships verbatim to the blog, where a flat colour is expected (ADR-137). `SUGGEST=1` prints the nearest passing value for a failure, moving towards black or white by whichever the failing background is further from; `VERBOSE=1` prints the passing pairs too.

**A gradient is walked, not sampled at its stops.** A foreground crosses its background *between* two stops and never at one, so a pair scored only at the stops reads that crossing as its best moment instead of its worst. Sixteen steps span each pair of adjacent stops; a hard edge — two stops meeting at one position — is not interpolated across, since a colour invented there can sit nearer the foreground than any the value paints; and where the walk finds the ink passing through the surface's own luminance, it bisects to that point and records the 1.00 it is worth, at a colour no stop names.

**The pair table is written by hand, so it is not the last word.** `npm run check:contrast:census` walks the stylesheets the app ships instead — `.css` and `.scss` under `src/`, the `styles` template literals inside a `.ts`, and the CSS inside the C# raw strings of the blog, the landing page and the draft preview, which are painted from `DesignTokens` and had nothing reading them — and lists every foreground-on-background the app declares that no pair in the table covers, matched on resolved colour rather than on token names. Each line says how its backdrop was arrived at: an opaque surface the rule paints itself, an enclosing rule in the same sheet, or an assumed page ground — which is a guess, and is labelled as one on the line and counted in the summary. It reports and never fails, and `cedar test` runs the table, not the census; the census is what tells you the table has stopped following the CSS.

The thresholds are per token, and this is the contract:

| Token | Contract |
|---|---|
| `--text`, `--t2` | text — 4.5:1 against every paper surface (`--surface`, `--sheet`, `--alt`) **and against every wash it is painted on** |
| `--accent`, `--danger`, `--ok`, `--warn` | text — 4.5:1 against every paper surface, and on its own wash (`--asoft`, `--ok-soft`, `--warn-soft`, `--danger-soft`) |
| `--hover`, `--hover-strong`, `--hover-danger` | translucent, so measured over each paper in turn; the ink riding it sets the floor — 4.5:1 for a label, 3:1 for an icon glyph |
| `--glass` | 4.5:1 over paper. Over the document it is a named exception, not a number: the backdrop is user content (ADR-145 §5) |
| `--t3` | **not text.** Placeholder, disabled, decoration — 3:1 |
| `--border-strong` | the boundary of a field/select/toggle — 3:1 |
| `--wood-ink` | the only ink allowed on the wall (`--bg`, `--canvas`) — 4.5:1 |
| `--border`, `--abord` | decorative hairline — no threshold, deliberately |
| `--series-1…6` | graphical object — 3:1 on `--surface` |

Paper ink and wall ink are measured separately because they travel in opposite directions (ADR-141): the wall is dark at night while the sheet stays cream, so no one value clears 4.5:1 on both, and pairing a paper token against `--bg` would be an unsatisfiable demand rather than a standard.

The one that matters when writing CSS: **`--t3` never carries information.** If text is quiet because it is secondary, that is `--t2` plus a smaller `--fs-*` role.

The one after it: **an ink is derived against the wash it lands on, not against bare paper** (ADR-145). A value tuned to 4.5:1 on `--surface` has nothing left for the accent wash, the hover wash or its own tint, and every one of those is a surface the app paints under it.

**Focus.** The ring is **two layers**, one global `:focus-visible` rule, every surface (ADR-140):
```
:focus-visible { outline: 2px solid var(--brass-edge); outline-offset: 2px;
                 box-shadow: 0 0 0 5px var(--focus-halo); }
```
`--focus-halo` is the cream `rgba(238, 217, 163, .85)`, fixed in both themes: its job is to be the lighter of the two layers whatever the theme does to the metal. No single colour clears 3:1 on both paper and rail — flat brass vanishes on the ruler it is drawn from, flat accent vanishes on a pine button — but the dark outline against the cream halo is a constant boundary inside the band whatever lies under it, so one rule covers every surface. **Neither layer may be `--accent`**: that is injected at runtime from the user's preset, and the accessibility floor must not be an input. The halo costs a focused control its own `box-shadow` for as long as it is focused, which reads as a glow. The editor sheet (`.tiptap`) is the only exclusion and clears **both** layers — `outline: none` alone would leave the halo drawn around the sheet — because the caret is the indicator there. A component that removes an outline owes a replacement.

**Two numbers, not one** (ADR-182). `--hit-target: 38px` is the box a paper control is **drawn** at — the mirror's `.btn-md`, and what every button, field, leaf tag, task tag and picker row spends. `--hit-touch: 44px` is the floor a **finger** is owed, spent under `@media (pointer: coarse)` and nowhere else. One token answering both questions is what let every paper control ship six pixels taller than the design it was ported from.

**Touch targets.** `@media (pointer: coarse)` gives every `button`, `[role="button"]`, `summary`, `select` and `a.icon-btn` a 44px minimum in both axes — keyed on pointer type, not viewport width, because an iPad in landscape is 1024px wide and entirely touch-driven. Written as `min-height`/`min-width` so it beats a component's fixed `width` by property rather than by specificity. Inline links, checkboxes and radios are exempt.

**44px is paper's floor, not chrome's** (ADR-138 §5). The bench chrome is a second surface class, `data-surface="chrome"`, with its own minimum control box `--hit-chrome: 30px`: a global coarse-pointer rule would push the ruler from 30px to 44, the drawer lip from 32 to 44 and the rail past its own height, spending the whole chrome budget on the first touch device. So the selector list takes a paper qualifier that exempts `[data-surface="chrome"]` and its descendants — a named exception to WCAG 2.5.5 (Target Size Enhanced, AAA), with 2.5.8's 24×24 AA floor cleared by every chrome control and not exempted. `tools/check-density.mjs` asserts the qualifier and skips that one assertion while no `[data-surface]` exists in the tree, since there is then no chrome to exempt.

**Long words (T-051).** A dev-only pseudo-locale inflates every UI string ~30% and welds a German compound onto its longest word, wrapping the result in `⟦ ⟧` so a screenshot can never be mistaken for a translation. Switch it on with `?pseudo=1` on any URL, or the toggle on `/dev/styleguide`; it is per-browser and never touches the profile. Captured by `AUDIT=1 npx playwright test 99-audit -g pseudo` into `.e2e-audit/75…78`. The rule it enforces: **a label shrinks and ellipses; it never widens its container.** Every truncating control keeps a `title`, so the full text stays one hover away.

## Component patterns (convention, not enforced)

There is **still no shared button abstraction** — `.btn-accent`/`.btn-ghost` are copy-pasted between component stylesheets with drifting values (e.g. `.btn-ghost` padding differs between the editor and settings). But the modal gap closed: `shared/` now holds ~15 reusable components including a real `app-modal` (used across the app), `app-icon`, `page-header`, `account-menu`, the pickers, the appearance panel and more — `docs/design/UI-INVENTORY.md` §Shared is the census. The paragraph below survives as the button-pattern reference.

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
    box-shadow: var(--shadow-lg);  /* tokenized since — exactly this value */
    padding: 22px 24px;
    color: var(--text);
}
.btn-accent {
    border: none; background: var(--accent); color: var(--sheet);
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

Global (not component-scoped, since TipTap content is rendered via `innerHTML` in places): `h1`–`h3` in `--font-display` at `--fs-27`/`--fs-19` (the sheet's own typeface preference moves the body, never the headings), the body size inherited so the editor's zoom control still works (fixed from a past bug — zoom used to be silently overridden by a hardcoded `font-size: 16px`), `blockquote` with a `3px solid var(--abord)` left border, inline `code`/`pre` with `var(--font-mono)`, `tg-spoiler` (spoiler mark → hidden text via `background: var(--t3); color: transparent`, revealed on hover), `.datetime-pill`, `.annotation-block` (comment/reaction anchor), `.toggle-block`, `.media-with-caption`, `.footnote-badge` — one block per custom TipTap node/mark in `tiptap-extensions/`.

## Known design debt

- ~~Font-size scale isn't adopted outside the shared header~~ — closed by T-077 (31.07–01.08.2026): 314 → 0 hardcoded `font-size` declarations in the Angular app.
- `.btn-accent`/`.btn-ghost` duplicated with drifted values across component stylesheets instead of a shared component (modals *are* shared now — `app-modal`).
- Spacing literals 9/11/14/18px on cards and badges — the scale has no such steps; picking one waits on T-076's mockups, not on a sweep.
- The blog's server-rendered surfaces get their tokens generated from `styles.scss` (ADR-090), but the renderer still carries a handful of its own hex values.

> ~~TODO (Marty): target design-system tool?~~ — **answered by practice, 08–18.08.2026**: Claude Design is the active pipeline for new work, delivered as handoff packages (`docs/design_handoff_indiedev_core_loop/` for the module screens; Cedar Bench, mirrored at `.design-sync/ds-v2/`, for the app's one look), while **`styles.scss` stays the canonical source of token values** — packages copy from it, never the reverse.
