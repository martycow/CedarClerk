---
owner: marty
last_verified: 2026-09-01
source_of_truth_for: the frozen build contract of the Paper-first port — component APIs, CSS vocabulary, file zones, lane interfaces, acceptance and order of battle
guard: none
---

# Paper first — build contract

The decision is ADR-239; the drawing is the eight artboards beside this file. This file is what
four lanes build against without meeting. Nothing here is a suggestion: a lane that needs something
the contract does not give it stops and asks, it does not widen its zone.

Names below are real. Icon names are members of `IconName` in `shared/icon-data.generated.ts`
(106 today); i18n groups are the 28 top-level keys of `core/i18n/en.ts`; routes are
`app.routes.ts`; selectors are the ones the e2e suite binds.

## A. Shell component API

All new files live in `cedarclerk-web/src/app/shell/`. Standalone components, `inject()`, signals,
`ChangeDetectionStrategy.OnPush`, `host: { 'data-surface': 'paper' }` on every one of them.

### `app-shell.component.ts` — `AppShellComponent`, selector `app-shell`

The parent route. `app.routes.ts` line 51 changes from
`import('./bench/chrome/bench-shell.component').then(m => m.BenchShellComponent)` to
`import('./shell/app-shell.component').then(m => m.AppShellComponent)`; the children are untouched.

```
template:  <div class="shell" [class.is-rail]="mode() === 'rail'">
             <app-sidebar [mode]="mode()" … />
             <main class="body" data-surface="paper"><router-outlet /></main>
           </div>
           <app-appearance-panel /> <app-feedback-panel /> <app-search-overlay />
           <app-debug-console />          // the overlay, mounted once (ADR-239 clause 11)
```

- `mode = computed<'full' | 'rail'>` — `'rail'` when the path starts with `/editor`, `'full'`
  otherwise. Route-driven, never width-driven (ADR-147).
- Owns everything `BenchShellComponent` computed today and the sidebar consumes: `projectId`,
  `openProjectId`, `projectRole`, `projectNames`/`namesLoaded`, `switcher`, `onHub`, the
  `CurrentProjectService.remember/reconcile` effects, `feedback.refreshNewCount()`, the credit-chip
  refresh. Copy the logic; delete the hooks/crumbs/ruler parts.
- Host: `'[class.console-open]': 'log.open()'` is **not** carried over — the console is an overlay
  and reserves nothing.
- Keyboard: `(document:keydown)` handling `` Ctrl+` `` → `log.open.update(v => !v)`. Ctrl+K stays
  the search overlay's own listener.

### `sidebar.component.ts` — `SidebarComponent`, selector `app-sidebar`

```ts
export interface NavItem {
    id: string;                 // stable id, also the aria-current key
    label: string;              // already translated by the shell
    icon: IconName;
    link: string | readonly unknown[];
    count?: number;             // drawn only when defined and > 0; never 0
    countTitle?: string;        // what the count counts (accessible name = "{label} {count} {countTitle}")
    title?: string;             // tooltip when the label is a short form
}
export interface NavGroup { id: 'write' | 'plan' | 'ship'; label: string; items: readonly NavItem[]; }
export interface SidebarProject { id: string; name: string; kind: string; link: string | readonly unknown[]; }

readonly mode        = input<'full' | 'rail'>('full');
readonly groups      = input<readonly NavGroup[]>([]);
readonly foot        = input<readonly NavItem[]>([]);         // All projects, Settings
readonly activeId    = input('');                             // computed by the shell from the route-prefix table
readonly project     = input<SidebarProject | null>(null);    // the switcher card; null draws "All projects" as the card
readonly projects    = input<readonly SidebarProject[]>([]);  // switcher entries, hub last (ADR-186/221)
readonly projectHint = input('');                             // t().shell.switchProject
readonly user        = input<{ name: string; avatarUrl: string | null; initial: string }>();
readonly alerts      = input(0);                              // new comments + reactions; the bell's dot
readonly navLabel    = input('');                             // t().shell.screens — the <nav> aria-label
readonly picked      = output<string>();
```

Template: `<a class="side-brand" routerLink="/projects">` (logo + `t().shell.brand`), the switcher
card (`button.side-project` with `aria-haspopup`, panel of `a.side-project-item`, same
open/Escape/outside-click handling as `RailHeaderComponent` today — copy it), one `<nav>` holding
the groups (`.label` per group, `a.side-item` per item with `[attr.aria-current]="'page'"` on the
active one, `.side-count` right-aligned), a spacer, the foot items, then `.side-user` — a
`<button class="side-user-trigger">` that opens `app-account-menu`'s popover, with the bell
(`chat-teardrop` icon + `.side-dot` when `alerts() > 0`). In `rail` mode the card and counts are
not rendered and each item is icon over a 12px caption (`--fs-12`), width 92px.

**The nav item list** (ids are the `HOOK_PREFIXES` ids of `bench-shell.component.ts`, so
`activeHook` logic carries over):

| Group | id | i18n key (`shell.*`) | icon | link |
|---|---|---|---|---|
| write | `documents` | `documents` → **rename value to `'Documents'`** | `file-text` | `['/projects', id]` (owner) · `/drafts` (module off) |
| write | `assets` | `assets` | `images` | `['/projects', id, 'assets']` · `/library` |
| write | `canvas` | `canvas` | `squares-four` | `['/projects', id, 'canvas']` |
| write | `dialogues` | `dialogues` | `tree-structure` | `['/projects', id, 'dialogues']` |
| write | `showcase` | `showcase` (`'Site'`) | `globe` (new, see below) | `['/projects', id, 'showcase']` |
| plan | `board` | `board` → **rename value to `'Tasks'`** | `check-square` | `['/projects', id, 'tasks']` |
| plan | `planner` | `planner` | `flag` | `['/projects', id, 'planner']` |
| plan | `calendar` | `calendar` | `calendar-blank` (new) | `/calendar` |
| ship | `builds` | `builds` | `cube` | `['/projects', id, 'builds']` |
| ship | `posts` | `posts` (new key, `'Posts'`) | `paper-plane-tilt` | `/posts` |
| ship | `metrics` | `metrics` | `chart-bar` | `/posts?tab=stats` |
| foot | `hub` | `allProjects` | `folder-open` | `/projects` |
| foot | `settings` | `settings` | `gear` | `/settings` |

Group labels: `shell.groupWrite`, `shell.groupPlan`, `shell.groupShip` (new keys). Visibility is
`bench-shell.component.ts` `hooks()` verbatim: module off → `documents`, `calendar`, `assets`,
`posts`, `metrics` only; member role → `canvas`, `calendar`, `posts`, `metrics`; owner → all.
`admin` leaves the list (ADR-239 clause 4). `metrics` carries `count = feedback.newComments() +
feedback.newReactions()` with `countTitle = t().editor.newBadge`; `documents` and `board` carry
`p.documents.length` / `p.openTaskCount` **only if** `ProjectsService.list()` already returns them
for the open project — otherwise no count (`T-368`).

**Four icons are added** to `tools/icon-map.json` by the shell lane and regenerated with
`npm run icons:generate`: `"Globe": "globe"`, `"Calendar": "calendar-blank"`,
`"Monitor": "desktop"`, `"Smartphone": "device-mobile"`. All four exist in
`@phosphor-icons/core/assets/regular`. The committed generated files (`icon-data.generated.ts`, the
icon-usage inventory) are regenerated in the same commit (ADR-173).

### `page-header.component.ts` — `PageHeaderComponent`, selector `app-page-header`

```ts
export interface HeaderMeta { text: string; title?: string; tone?: 'ok' | 'muted' | 'warn'; tag?: boolean; }

readonly title    = input.required<string>();
readonly kicker   = input('');                        // the line above the title (Editor.png: "Blog · 23 documents")
readonly meta     = input<readonly HeaderMeta[]>([]); // rendered "a · b · c"; tag:true draws a .tag of the tone
readonly headingLevel = input<1 | 2>(1);
```
Slots: `<ng-content select="[primary]">` (one `app-button variant="pine"` or an anchor with
`.btn.primary`), `<ng-content select="[secondary]">` (at most one control), `<ng-content
select="[title-tail]">` (a `.tag` beside the title, Editor.png's "Live"). Renders `<header
class="page-header">` with `<h1 class="page-title">` (`--font-display`, `--fs-27`) and
`<p class="page-meta">`. **This is where every `ruler.publish` left/right readout goes**, as
`HeaderMeta` items, and where every `rail.publish` primary action goes, as the `[primary]` slot.

### `document-frame.component.ts` — `DocumentFrameComponent`, selector `app-document-frame`

The editor's frame: top bar, title line, tabs, body, footer. The editor lane fills the slots; the
shell lane owns the component.

```ts
export type DocumentTab = 'write' | 'preview' | 'publish';

readonly title       = input.required<string>();        // drawn read-only here; the editable input.title stays on the Write sheet (ADR-159 clause 4)
readonly kicker      = input('');                        // "Blog · 23 documents"
readonly statusTag   = input<HeaderMeta | null>(null);   // "Live" / "Draft" / "Scheduled"
readonly tab         = input<DocumentTab>('write');
readonly tabs        = input<readonly { id: DocumentTab; label: string; icon: IconName }[]>([]);
readonly saveWord    = input('');                        // t().editor.synced etc. — the top bar's "Saved 2 min ago"
readonly saveState   = input<'saved' | 'saving' | 'error'>('saved');
readonly dateLabel   = input('');
readonly footerText  = input('');                        // "Last saved 2 min ago · 366 words"
readonly canUndo     = input(false);
readonly canRedo     = input(false);
readonly project     = input<SidebarProject | null>(null);   // the top-bar switcher in rail mode
readonly projects    = input<readonly SidebarProject[]>([]);
readonly tabChange   = output<DocumentTab>();
readonly undo        = output<void>();
readonly redo        = output<void>();
```
Slots: `[primary]` (the Publish split button — the editor lane provides `app-button variant="pine"`
+ the caret button), `[title-actions]` (the "⋯" and "Details" buttons), `[body]` (the tab body),
`[footer-start]` (Share preview), `[footer-end]` (the next-step button). Tabs are
`role="tablist"` / `role="tab"` with `aria-selected`; the tab strip's accessible name is
`t().editor.tabsLabel` (new key, editor lane). Icons: write `pencil-simple`, preview `eye`, publish
`upload-simple`. The frame renders the top bar only in the shell's `rail` mode; in `full` mode it
renders nothing above the title (the sidebar carries the switcher) — read `AppShellComponent.mode`
through `inject(AppShellComponent)`.

### `empty-state.component.ts` — `EmptyStateComponent`, selector `app-empty-state`

```ts
readonly icon    = input<IconName | null>('plus');
readonly title   = input('');
readonly text    = input('');
readonly note    = input('');   // rendered in --font-note as a .margin-note under the box
```
Slot: `<ng-content>` for one action control. Renders `<div class="empty-state">`. A page never
draws its own dashed box; it uses this.

### Deleted (shell lane, stage B)

`bench/chrome/bench-shell.component.{ts,spec.ts}`, `hook-rail.component.{ts,spec.ts}`,
`rail-header.component.{ts,spec.ts}`, `ruler-bar.component.{ts,spec.ts}`,
`bench-drawer.component.{ts,spec.ts}`, `bench/scenery/brass-nail.component.{ts,spec.ts}`,
`bench/display/resin-drop.component.{ts,spec.ts}` (after `grep -r app-resin-drop src/` returns only
the shell — ADR-170). `core/rail-actions.service.{ts,spec.ts}` and `core/ruler.service.{ts,spec.ts}`
are deleted in the **stage C close** (§F), after every page has stopped calling them; until then
they exist and nothing renders them.

### Kept and restyled (their owners: `bench/**` is the shell lane's)

`bench/chrome/shelf-panel` (becomes `.card`: no wood frame, header is a `.label` line with the
count in `--t3` and the `[actions]` slot; `tone="cork"` and `flush` keep working; the
no-nesting guard stays), `bench/chrome/index-tabs` (stays chrome: 24px trim tiles, restyled to
`.seg` values but keeps `role="tablist"` and its API), `bench/worktop/worktop` (no lamp, no grid,
no chalk edge; `label`/`meta` inputs render a `.label` line; keeps `scroll`), `spec-row`,
`growth-chart`, `module-tile`, `log-line`, `paper-card`, `stamp-badge`, `task-tag`, `leaf-tag`,
`input`, `button`, `brass-hook`, `brass-pin` (measure; delete if unreferenced).

`shared/account-menu.component.ts` grows the entries of ADR-239 clause 4 in this order, with
hairlines between groups: Profile, Account · Glossary, Presets, Teams (module on) · Appearance,
Toggle theme, Fullscreen · Send feedback · Admin (`auth.isAdmin()`), Style guide, Icons (admin) ·
Debug console (`` Ctrl+` `` hint), About (`href="/welcome"`) · Log out. `shared/debug-console.*`
drops `app-bench-drawer` and renders a fixed bottom overlay (`.console-overlay`, 280px, `z-index`
above the page, `role="dialog"`, Escape closes) with the clear button and the rows; no summary line.

## B. Global CSS vocabulary

Added to `styles.scss` by the shell lane, after the `body` rule, under one comment header
`/* Paper-first vocabulary (ADR-239) */`. Every other lane **uses** these and **never redefines**
them in a component stylesheet; a component may add its own descendant rules
(`.doc-list .row { grid-template-columns: … }`) but not restate the base class.

| Class | Artboard value → token |
|---|---|
| `.page` | `display:flex; flex-direction:column; flex:1; min-height:0; padding: 0 var(--space-8) var(--space-6)` (artboards: 40px sides, 32px bottom → `--space-8`=48 is the nearest step; the shell lane adds `--space-10: 40px` to the scale and uses it) |
| `.page-header` | `display:flex; align-items:flex-end; justify-content:space-between; gap:var(--space-5); padding: 28px 0 20px` → `var(--space-7) 0 var(--space-5)` (34/24; nearest steps) |
| `.page-title` | `font-family:var(--font-display); font-size:var(--fs-27); font-weight:700; line-height:1.1; margin:0` |
| `.page-meta` | `display:flex; gap:10px; font-size:var(--fs-14); color:var(--t2)`; separators `·` in `var(--border)` |
| `.card` | `background:var(--sheet); border:1px solid var(--border); border-radius:var(--radius-md); box-shadow:var(--shadow)` |
| `.card.is-tinted` | `background:var(--surface)` (board columns) |
| `.seg` | `display:inline-flex; background:var(--canvas); border-radius:6px→var(--radius-md); padding:3px; gap:2px` |
| `.seg > button`, `.seg > a` | `padding:5px 12px; border-radius:var(--radius-sm); font-size:var(--fs-13); font-weight:600; color:var(--t2); min-height:var(--hit-trim)`; `.is-on` → `background:var(--sheet); color:var(--text); box-shadow:var(--shadow-paper-sm)`; carries `role="tablist"`/`role="tab"` or `role="group"`+`aria-pressed` |
| `.tag` | `display:inline-flex; align-items:center; height:22px; padding:0 var(--space-2); border-radius:var(--radius-stamp); font-size:var(--fs-12); font-weight:700; letter-spacing:.04em; text-transform:uppercase` |
| `.tag.ok` | `background:var(--ok-soft); color:var(--ok)` |
| `.tag.muted` | `background:var(--canvas); color:var(--t2)` |
| `.tag.warn` | `background:var(--warn-soft); color:var(--warn)` |
| `.tag.danger` | `background:var(--danger-soft); color:var(--danger)` |
| `.tag.is-plain` | `text-transform:none; letter-spacing:0` (hashtags) |
| `.empty-state` | `display:flex; flex-direction:column; align-items:center; justify-content:center; gap:10px; text-align:center; border:1px dashed var(--border); border-radius:var(--radius-md); color:var(--t3); padding:var(--space-5)` — 1px, not the artboard's 1.5px (integers only, ADR-138) |
| `.row-list` | `overflow:auto; flex:1; min-height:0` |
| `.row` | `display:grid; align-items:center; gap:14px; min-height:var(--hit-touch); padding:0 var(--space-4); border-top:1px solid var(--paper-edge); font-size:var(--fs-15)`; `:hover` → `background:var(--alt)`; columns are the page's own |
| `.row .t` / `.row .m` | title `font-weight:600; color:var(--text)` / meta `color:var(--t3); font-size:var(--fs-13)` |
| `.kv` | `display:grid; grid-template-columns:96px 1fr; gap:var(--space-2) var(--space-3); font-size:var(--fs-13); align-items:center`; `.kv b` → `color:var(--t3); font-weight:600` |
| `.label` | `font-size:var(--fs-11); font-weight:700; letter-spacing:.08em; text-transform:uppercase; color:var(--t3)` |
| `.btn` | `display:inline-flex; align-items:center; gap:var(--space-2); min-height:var(--hit-target); padding:0 14px; border-radius:var(--radius-sm); border:1px solid var(--border); background:var(--sheet); color:var(--text); font-weight:600; font-size:var(--fs-14)`; `.primary` → `background:var(--pine); border-color:var(--pine); color:var(--text-on-pine)`; `.ghost` → transparent; `.sm` → `min-height:var(--hit-chrome); padding:0 10px; font-size:var(--fs-13)`. **`app-button` is restyled to these same values** so the two are one object; pages use `app-button` where they already do and `.btn` only on anchors. |
| `.side-*`, `.frame-*`, `.console-overlay` | shell-private, declared in the components, listed here so no page reuses them |

Tokens added by the shell lane: `--space-10: 40px` (page gutter), `--fs-30: 30px` (the editor's
document title, Editor.png), `--sidebar-w: 232px`, `--sidebar-rail-w: 92px`, `--topbar-h: 56px`,
`--footer-h: 60px`. Every colour above is an existing token; **no new colour** enters the contract.

Removed from `styles.scss` by the shell lane, in the stage C close, after `grep` proves nothing
matches (ADR-170): the `--rail-*` family, `--sign-tile-*`, `--surface-rail`, `--grad-sign-tile`,
`--pegboard`, `--hook-face`, `--rail-btn-face*`, `--tile-edge`, `--frame-board`, `--shelf-frame`,
`--shadow-rail`, `--shadow-shelf`, `--border-rail-btn`, `--bench-rail-h`, `--bench-tool-w`,
`--bench-drawer-lip`, `--bench-drawer-open`, `--bench-ruler-h`, `--bench-bottom-h`,
`--bench-panel-hd`, `--bench-worktop-edge-h`, `--lamp`, `--grid-worktop`. What stays because
something still paints it: `--wood-*` (the cover plates), `--brass-*`, `--resin*`, `--leaf-*`,
`--tab-badge`, `--tex-paper`, `--tex-cork`, `--grid-graph` (the chart), `--rule-ink*`, all `--hit-*`,
`--text-chrome*`, `--text-readout`, the dock widths while a page still names one.
`tools/check-contrast.mjs` loses the pairs of every removed token in the same commit; the run must
still end `0 failing pair(s)`. `tools/check-density.mjs` `CHROME_PARTS` becomes `['index-tabs',
'worktop']` plus any component that still declares `data-surface="chrome"` in host metadata.

Surface census after the port — **chrome**: `app-index-tabs`, `app-worktop` (its label line),
`app-spec-row`, the editor tool strip (`.strip[data-surface="chrome"]`, unchanged), `app-log-line`
inside the console. **Everything else is paper**, the sidebar, the page header, the top bar and the
footer included; their 36px buttons and 14px labels are paper's numbers. The tool strip's 28px
artboard buttons stay at `--hit-chrome` 30px — the gate decides, not the drawing.

## C. File zones

Overlap is zero by construction. A path not listed belongs to nobody this sprint and is not edited.

| Lane | Owns |
|---|---|
| **ui-shell** | `cedarclerk-web/src/app/shell/**` (new); `bench/**` (delete/adapt/restyle); `styles.scss`; `app.routes.ts`; `app.spec.ts`, `app.routes.spec.ts`; `core/rail-actions.service.*`, `core/ruler.service.*` (delete, stage C close); `shared/account-menu.*`, `shared/debug-console.*`, `shared/appearance-panel.*`, `shared/search-overlay.*`, `shared/feedback-panel.ts`; `tools/icon-map.json` + `npm run icons:generate` outputs; `tools/check-contrast.mjs`, `tools/check-density.mjs`; `e2e/10-ui-shell.spec.ts`, `e2e/16-media.spec.ts`, `e2e/17-density.spec.ts`; i18n: the `shell` group only; inventory: `## Shared components`, `## Bench kit`, new `## shell`, `## Icon inventory` |
| **ui-editor** | `pages/editor.component.*`; `pages/editor-preview/**` (new: `editor-preview.component.ts`, `preview-destinations.component.ts`, `preview-phone.component.ts`, `preview-checks.component.ts`); `core/preview.service.ts` (new); `shared/document-outline.*`; `core/posts.service.ts` (may add the two preview calls — no other edit); `e2e/helpers.ts`, `e2e/03-editor.spec.ts`, `e2e/14-thread.spec.ts`, `e2e/99-audit.spec.ts` lines that bind the editor; i18n: the `editor` group only; inventory: `## editor.component` |
| **ui-pages-a** | `pages/projects.*`, `pages/project.component.*`, `pages/project-tasks.*`, `pages/project-assets.*`, `pages/project-builds.*`, `pages/project-planner.*`, `pages/project-canvas.*`, `pages/project-boards.*`, `pages/project-dialogue.*`, `pages/project-dialogues.*`, `pages/project-showcase.*`, `pages/calendar.*`, `pages/canvas-geometry.spec.ts`; i18n: `projects`, `calendar`; inventory: the `## projects.component` … `## project-dialogue.component`, `## project-planner.component`, `## project-tasks.component`, `## project-assets.component`, `## calendar.component` sections |
| **ui-pages-b** | `pages/drafts.*`, `pages/posts-manager.*`, `pages/stats.*`, `pages/comments.*`, `pages/settings.*`, `pages/teams.*`, `pages/glossary.*`, `pages/presets.*`, `pages/admin.*`, `pages/media-library.*`, `pages/login.*`, `pages/register.*`, `pages/onboarding.*`, `pages/invite-accept.*`, `pages/external-complete.*`, `pages/download.*`, `pages/styleguide.*`, `pages/icons.*`, `shared/legal-page.*`; `e2e/01`, `02`, `04`, `05`, `08`, `09`, `11`, `12`, `13` and the non-editor lines of `99-audit`; i18n: `drafts`, `manager`, `stats`, `feedback`, `settings`, `teams`, `glossary`, `presets`, `admin`, `media`, `login`, `register`, `onboarding`, `externalAuth`, `download`, `debug` (labels only); inventory: the matching `## <page>.component` sections, `## Development surfaces` |
| **server-preview** (C#) | `CedarClerk.Server/PreviewEndpoints.cs` (new, flat like every `*Endpoints.cs`); `CedarClerk.Server/DraftPreviewEndpoints.cs` (adds `GET`); `CedarClerk.Server/BlogEndpoints.Preview.cs` (extract the page render); `CedarClerk.Server/Program.cs` (one line: `app.MapPreviewEndpoints();` after line 327); `CedarClerk.Core/TelegramPreviewProjection.cs` (new); `CedarClerk.Tests/TelegramPreviewProjectionTests.cs` (new), `CedarClerk.Tests/PreviewEndpointTests.cs` (new). **No `Entities.cs` change, no migration.** |

Shared files and the rule for them:

- **`core/i18n/en.ts` and `ru.ts`** — each lane edits only the groups listed against it, with the
  `Edit` tool and anchored insertions inside the group (`Edit` on the group's last existing line),
  never a `Write` of the file. `ru.ts` is `typeof en`, so a key added to one side without the other
  fails the build — add both in one edit pair. The shell lane's renames (`documents`, `board`) are
  value changes on existing keys; no key is deleted this sprint.
- **`docs/design/UI-INVENTORY.md`** — same rule, by section heading. The shell lane adds
  `## shell` (sidebar, page header, document frame, empty state, the account-menu entries, the
  console overlay) directly under `## Shared components`, and deletes the retired rows from
  `## Bench kit`. A page lane's rows for retired chrome (a page's ruler readouts, its rail action)
  move into that page's own section as header meta / header action rows.
- **`docs/design/DESIGN.md`** — the project-manager updates §Principles and §Component patterns
  from this file after stage C; no lane edits it.
- **`e2e/*.spec.ts`** — a spec is edited by the lane whose change breaks it, as listed.

## D. Interfaces between lanes

### D1. Page → `app-page-header` (shell publishes, page lanes call)

Every page that today calls `ruler.publish({ label, left, right })` renders instead:

```html
<app-page-header [title]="…" [meta]="headerMeta()">
  <app-button primary variant="pine" (clicked)="…">…</app-button>
</app-page-header>
```
`headerMeta()` is the page's former `left`+`right` readouts as `HeaderMeta[]`, in the same order,
with the state word (`Active`/`Archived`/`Live`) as `{ tag: true, tone }`. Pages that called
`rail.publish({ primary })` put that action in the `[primary]` slot. The call to `ruler.clear()` /
`rail.clear()` in `ngOnDestroy` is deleted with the publish. Header strings that were the bench's
short forms stay whatever the page group already has; no new key is needed for a readout that
exists.

The migration census — one line per file, so no page is forgotten:
`editor` (footer, not header — D3), `glossary`, `presets`, `teams`, `posts-manager`, `projects`,
`project`, `project-tasks`, `project-planner`, `project-builds`, `project-boards`,
`project-canvas`, `project-dialogue`, `project-dialogues`, `project-showcase`.

### D2. Page → `app-empty-state`

Every `@empty`, every `.hint` that says nothing is here, every `p.media-empty`/`.drafts-empty`
becomes `<app-empty-state [title] [text]><app-button …>next action</app-button></app-empty-state>`.
The e2e string `No files yet` (`16-media.spec.ts`, `.media-empty`) survives as the empty state's
title; the shell lane rebinds the selector to `app-empty-state`.

### D3. Editor → `app-document-frame`

The editor renders `<app-document-frame …>` as its root, puts today's `.main` (outline shelf,
strip + worktop, inspector) in `[body]` when `tab() === 'write'`, `<app-editor-preview>` when
`'preview'`, and on `'publish'` calls `openExportModal()` and keeps the previous tab. `tab` is a
signal read from `queryParamMap.get('tab')` and written with `router.navigate([], { queryParams:
{ tab }, queryParamsHandling: 'merge' })`. The `rulerFeed` and `railFeed` effects become
`footerText = computed(...)` and the `[primary]` slot (`Publish` — the e2e name changes from
`Export` to `Publish`, `14-thread` and `99-audit` rebind, editor lane). The word `Synced`
(`t().editor.synced`) is drawn in the frame's top-bar save state **and** the footer text;
`helpers.ts` `expectSynced` rebinds from `app-ruler-bar` to `app-document-frame .frame-footer`.
The `Details` title action toggles the inspector shelf's visibility (`inspectorOpen` signal).

### D4. Server → editor: the preview projection

Routes, both in `PreviewEndpoints.cs` under `app.MapGroup("/api/drafts").RequireAuthorization()`,
owner-scoped with the drafts idiom (`d.Id == id && d.OwnerId == uid`; the tenant filter is on):

**`GET /api/drafts/{id:guid}/preview/telegram?lang=`** → `200 TelegramPreview`, `404` when the draft
or the language is not the owner's (language resolved with `DraftRevisionService.ResolveAsync`;
`lang` omitted = `draft.PrimaryLanguage`). Never sends, never touches `Asset.TelegramFileId`.

```csharp
// CedarClerk.Core/TelegramPreviewProjection.cs
public sealed record TelegramPreviewBlock(string Kind, string Text, IReadOnlyList<string> Urls, string? Caption);
//   Kind ∈ paragraph | heading | list | code | quote | divider | table | math | details | footer | photo | video | audio | slideshow | collage
//   Text: plain text (TelegramThreadSplitter's PlainText rules; list items joined with "\n"; media → "")
//   Urls: media only (photo/video/audio one entry, slideshow/collage N); Caption: media only
public sealed record TelegramPreviewMessage(int Index, IReadOnlyList<TelegramPreviewBlock> Blocks, int Characters, int MediaCount, string CutReason, string StartsWith);
public sealed record TelegramPreviewButton(string Text, string Url);
public sealed record TelegramPreview(
    string Language,
    int MessageCount,
    int Characters,                 // sum over messages
    int MaxCharactersPerMessage,    // Consts.Telegram.ThreadPartChars
    int MaxMediaPerMessage,         // 10
    IReadOnlyList<TelegramPreviewMessage> Messages,
    IReadOnlyList<TelegramPreviewButton> Buttons);   // ParseCtaButtons(draft.CtaButtonsJson); attached to the last message

public static class TelegramPreviewProjection
{
    public static TelegramPreview Project(string language, IReadOnlyList<ThreadPart> parts,
        IReadOnlyList<(string Text, string Url)> buttons, PublishCapabilities capabilities);
}
```
The endpoint builds `parts` exactly as `PublishEndpoints` `/thread-preview` does:
`CedarToTelegramBlocksRenderer.Render(cedarJson, mainHost)` then
`TelegramThreadSplitter.Split(blocks, telegramTarget.Capabilities)`; media URLs are absolute
against the blog host when one exists, relative `/media/…` otherwise. `RichAnchorBlock` is not
projected. `TelegramThreadSplitter`'s private `PlainText` becomes `public static string PlainText(RichRun?)`
so the projection and the splitter count the same string — that is the only edit to an existing
Core file.

**`GET /api/drafts/{id:guid}/preview/blog?lang=&theme=`** → `text/html`: the page
`BlogEndpoints.HandleDraftPreviewAsync` renders today, extracted into
`BlogEndpoints.RenderDraftPreviewPage(Draft draft, string language, string cedarJson, string title, string? theme)`
and called from both places; `theme` ∈ `light|dark` sets `data-theme` on the served `<html>`,
absent = the reader's default. Owner-scoped, cookie-authenticated, `noindex` headers as today,
`X-Frame-Options`/`frame-ancestors 'self'` so the editor's iframe may load it. Served under the app
host, not the tenant host — media stays `/media/*` relative.

**`GET /api/drafts/{id:guid}/preview-link`** (added to `DraftPreviewEndpoints.cs`) → `200 { url }`
when `draft.PreviewToken` is set, `404` otherwise — so "Share preview" can show the existing link
without rotating it. `POST` and `DELETE` are unchanged.

TypeScript mirror, in `core/preview.service.ts` (editor lane):

```ts
export interface TelegramPreviewBlock { kind: 'paragraph'|'heading'|'list'|'code'|'quote'|'divider'|'table'|'math'|'details'|'footer'|'photo'|'video'|'audio'|'slideshow'|'collage'; text: string; urls: string[]; caption: string | null; }
export interface TelegramPreviewMessage { index: number; blocks: TelegramPreviewBlock[]; characters: number; mediaCount: number; cutReason: 'heading'|'size'|'media'|'end'; startsWith: string; }
export interface TelegramPreviewButton { text: string; url: string; }
export interface TelegramPreview { language: string; messageCount: number; characters: number; maxCharactersPerMessage: number; maxMediaPerMessage: number; messages: TelegramPreviewMessage[]; buttons: TelegramPreviewButton[]; }

@Injectable({ providedIn: 'root' }) export class PreviewService {
    telegram(draftId: string, lang: string): Promise<TelegramPreview>;         // GET …/preview/telegram
    blogUrl(draftId: string, lang: string, theme: 'light'|'dark'|null): string; // the iframe src
    previewLink(draftId: string): Promise<{ url: string } | null>;             // GET …/preview-link, null on 404
}
```
JSON is camelCase (serializer default). Absent facts are omitted, never `0`/`""` (ADR-238's rule).

**Readiness and checks are composed on the client**, from calls that exist: `GET
/api/publish/networks` (accounts per network → *not connected* when `accounts` is empty, the
account's `lastError` when set), `GET /api/channels` (Telegram: no channel → *not connected*),
`POST /api/posts/validate` per network (the `PublishIssue` list — codes in
`PublishValidator.PublishIssueCodes`), `POST /api/posts/preflight` (empty version, dead links per
language), `GET /api/publish/thread-preview?network=x|bluesky` (part counts for the X/Bluesky rows).
The checks column for Blog = title present, cover image (the `coverImagePath` the draft DTO
carries), slug, visibility (`isPrivate`), languages in sync (`staleLanguages`), preflight links;
for Telegram = channel connected and `botCanPost`, length (`messageCount` · `characters` of
`maxCharactersPerMessage`), media count, CTA buttons present or not, schedule (the draft's
`scheduled` fact), the second-language channel when a translation exists. Every check is a
`{ label, detail, tone }` row; none blocks anything.

### D5. Shell → pages: what a page may assume

`main.body` is the one scroll container (`overflow:auto`), is `display:flex; flex-direction:column`,
and carries `data-surface="paper"`. A page's root is `.page` and fills it with `flex:1;
min-height:0`; a page that wants an inner scroller makes it a `.card` with `.row-list`. No page
declares `height: 100vh`, `min-height: 100vh` or `padding-bottom: var(--bench-bottom-h)`. The
sidebar is outside the page and the page never reads its width.

## E. Acceptance, per lane

Every lane, before it reports done:

1. Full test gate green — backend `dotnet test`, frontend `vitest`, `npm run check:icons`,
   `npm run check:contrast` (ends `0 failing pair(s)`), `npm run check:density` (nine rules, none
   `FAIL`). The four guards inside it: `UiInventoryDriftTests`, `ErrorMessageLocalizationTests`,
   `SchemaDriftGuardTests`, `DocsFlowGraphTests`.
2. `npm run build` completes; the two budget warnings of `T-363` may not grow.
3. `docs/design/UI-INVENTORY.md` updated in the same commit as the code, in the lane's own sections
   only, column shape `Element | Location | Type | Purpose | Loading state | Notes`.
4. No half-pixel size, no hex literal, no raw `font-size: NNpx` in a component stylesheet; every
   colour and size resolves through a token or a §B class.
5. Every nav item, group label, header string, tab label, empty-state sentence and check label on
   `t()`, in both `en.ts` and `ru.ts`.
6. The e2e bindings in the lane's zone either survive or are updated in the same commit; the
   accessible names that stay: `New document`, `Heading 2`, `Restore stored`, `Restore`, `More`
   (moves to the account-menu trigger's name only if the shell lane keeps the word — otherwise
   `10-ui-shell` rebinds), `Toggle theme`, `Appearance`, `All languages`, `Show paragraph marks`,
   `RU · Русский`, tab `Account`, tablist `Admin sections`, tablist `List view`, tabs
   `Grid`/`Table`, titles `/^Bold/`, `/history/i`, placeholders `Document title`,
   `Search title or tag`; the texts `Synced`, `Save stopped`, `No files yet`, `Design System`,
   `Icons`, `Stats`; the classes `.tiptap`, `.block-dropdown`, `.drafts-title`, `.drafts-page`,
   `.post-card`, `.post-search`, `.manager-tabs`, `.dest-card`, `.publish-issues li.blocking`,
   `.thread-toggle`, `.thread-parts`, `.mode-toggle`, `.export-section`, `.export-section-title`,
   `#cc-login-email`, `#cc-login-pass`, `.primary-btn`, `app-modal`, `app-tag-picker`.
   `17-density.spec.ts` keeps one real chrome measurement: rebind its `app-rail-header` line to
   `app-index-tabs[data-surface="chrome"] button` on `/projects` (shell lane).
7. `12-a11y.spec.ts` rule: every icon-only `button`/`a` on `/drafts`, `/posts`, `/settings`,
   `/editor` carries `aria-label` or `title` — the sidebar's rail mode, the top bar's undo/redo and
   the frame's "⋯" included.
8. Lane-specific: **ui-shell** — `bench-shell.component.spec.ts`'s assertions reappear as
   `app-shell.component.spec.ts` (nav visibility by module and role, active item, project memory,
   console toggle); `check-density` rule 9 does not `skip`. **server-preview** —
   `TelegramPreviewProjectionTests` cover: one message, a size cut, a heading cut, a media cut,
   captions, slideshow URLs, buttons on the last message only, `Characters` equal to the splitter's;
   `PreviewEndpointTests` cover owner scoping (another owner's id → 404), `lang` resolution, and
   that no `PublishJob`/`ScheduledPost` row is written. **ui-editor** — `?tab=preview` deep-links;
   the phone render draws `messageCount` bubbles from the DTO and no client-side split.
   **ui-pages-a / -b** — no `ruler`/`rail` import remains in the lane's files (`grep` is the proof).

## F. Order of battle

- **Stage B** — `ui-shell` ‖ `server-preview`. The shell lane lands the shell, §B vocabulary, the
  restyled bench components, the account menu, the console overlay, the four icons, and keeps
  `RailActionsService`/`RulerService` alive but unrendered so every page still compiles. The server
  lane lands the three endpoints and their tests. Both end with the full test gate green on their own
  files.
- **Stage C** — `ui-editor` ‖ `ui-pages-a` ‖ `ui-pages-b`, each branched from the shell lane's
  result. Every page migrates D1/D2 (and the editor D3/D4). No lane touches another's zone; a
  vocabulary gap is reported to the tech lead, not patched into a component stylesheet.
- **Stage C close** — `ui-shell`, one commit: delete the two services, the dead tokens, the dead
  bench components and their contrast pairs; `CHROME_PARTS` to its final census; full test gate green.
- **Stage D** — tester: the full test gate plus the smoke suite, the ten-screenshot walk of
  `docs/screenshots/` repeated on the new shell at 1440×900 in both themes, and the checklist of
  §E on every lane's diff.
- **Stage E** — project-manager: `docs/tasks/CHANGELOG.md`, the `T-365…T-368` rows on the board,
  `docs/design/DESIGN.md` §Principles/§Component patterns and `docs/design/UI-INVENTORY.md`'s
  header note, `docs/knowledge_base/TERMINOLOGY.md` (sidebar, page header, document frame, tab),
  and the `T-236` brief rewritten against the sidebar before anyone runs it.
