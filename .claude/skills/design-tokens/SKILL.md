---
name: design-tokens
description: Cedar Clerk design-token rules for building or reviewing UI — the seven ADR-071 principles, token families, density modes, the two style systems (Angular app + server-rendered blog), the icon pipeline, and the inventory-first law. Values live in cedarclerk-web/src/styles.scss; decisions in docs/design/DESIGN.md.
---

# Cedar Clerk design tokens

Source of truth for values: `cedarclerk-web/src/styles.scss`. Decisions and
component patterns: `docs/design/DESIGN.md`. Always style through tokens — never
raw hex, never a hardcoded `font-size`. (Skill pattern borrowed from Cowtext's
design-tokens.)

## The laws (ADR-071, condensed from DESIGN.md §Principles)

1. **Warm editorial is the base** — paper/wood neutrals, olive accent
   (`--accent` #566842 light), generous reading measure.
2. **One palette, one type scale, product-wide.** Screens differ only in
   spacing/radius/size tokens, never in colour sets.
3. **Density is a surface mode, not a component choice** — a page root opts into
   `[data-density="compact"]`; components must never hardcode their density
   (several live on both kinds of screen). Compact: /posts, /drafts, /admin,
   /library, module list screens. Comfortable: editor sheet, blog, dashboards.
4. **Serif for content = the blog body**; chrome stays `--font-sans`; the editor
   sheet's typeface is a user setting — the system does not pick it (ADR-073).
5. **Light-first, dark as a peer** — every new token gets both values in the
   same edit. There is also a third axis: the skin (`data-skin="forest"`,
   ADR-120) — orthogonal to theme, scoped to one partial (`styles/_forest.scss`).
6. **The blog is inside the system** — but it is a SECOND style surface:
   `BlogEndpoints.cs` `ShellTemplate` carries its own CSS. A token added for the
   app does not exist there; blog styling is edited in the template string.
7. **Zero hardcoded values in components** — integer-only `--fs-*` scale.

## Token families (names — values live in styles.scss)

- **Surfaces**: `--bg` / `--canvas` / `--surface` / `--sheet` / `--alt`;
  `--scrim` for overlays; `--shadow`/`--shadow-md`/`--shadow-lg`.
- **Text**: `--text` / `--t2` / `--t3`; semantic `--danger` / `--ok` / `--warn`.
- **Accent derivatives**: `--asoft` (soft fill) / `--abord` (accent-tinted
  border) — the standard active/selected pair (`background: var(--asoft);
  color: var(--accent)`).
- **Hovers**: `--hover` / `--hover-strong` / `--hover-danger` — never a literal
  rgba (that was the pre-token bug: a colour belonging to neither theme).
- **Borders**: `--border` / `--border-strong`.
- **Type**: semantic roles `--fs-caption/meta/ui/body/title/read` (+ numeric
  `--fs-9…--fs-27`), `--lh-read`; fonts `--font-sans`/`--font-serif`/`--font-mono`.
- **Spacing**: `--space-1…6` (4/8/12/16/24/32); **Radii**: `--radius-sm/md/lg`
  (6/10/14).
- **Density**: `--dens-*` set (row-y, gap, control-x/y, fs, radius) resolved by
  `[data-density="compact"]`.
- **Icons**: `--icon-xs/sm/md/lg`; **Motion**: `--motion-fast/base/slow` +
  `--ease`, with a global `prefers-reduced-motion` clamp.
- **Charts**: `--series-1…6` — legend always carries label+count, identity is
  never colour-alone.

## Icon pipeline (ADR-072)

Phosphor, inlined SVG behind `app-icon`. The available set is generated from
`cedarclerk-web/tools/icon-map.json` — **an icon name not in the generated set
renders nothing**. Before using a name, verify it exists (`grep icon-map.json`
or an existing `app-icon name="…"` usage); adding a new one means editing the
map and regenerating, not just typing the name.

## The inventory-first law (`.claude/rules/ui-changes.md`)

Before ADDING any UI element, grep `docs/design/UI-INVENTORY.md` for the area —
most controls already have a home, and a second home for the same concern is how
the UI comes apart (the X/Bluesky-in-Export-modal rebuild, ADR-095). Update the
inventory **in the same commit**; `UiInventoryDriftTests` fails the build for a
page component or `sec-*` section missing from it entirely.

## Hard checks when reviewing UI

1. Every colour and font-size resolves through a token; no half-pixel sizes.
2. New strings go through `t()` — `en.ts` defines the shape, `ru.ts` is
   `typeof en` (build enforces).
3. Component CSS is view-encapsulated: `btn-ghost`-style utility classes must be
   defined in the page's own stylesheet (they are per-component, not global).
4. Both themes AND the forest skin still read; the contrast contract test
   (`cedar test`) stays green.
5. Popover-in-modal fights fixed positioning — use the `inline` pattern
   (folder-picker's lesson) instead of nesting `app-popover` in `app-modal`.
6. Blog-side changes edit `ShellTemplate` CSS, not styles.scss — and vice versa.
