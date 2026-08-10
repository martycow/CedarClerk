# Handoff: Cedar Clerk — Indie-Dev Module, Core Loop

## Overview
Three new screens plus navigation integration for Cedar Clerk's indie-gamedev module: **Projects list**, **Project dashboard**, **Task board** (with an opened **Task card**). A project = a game; documents, tasks and assets live inside it. Designed as a continuation of the Phase 11 visual language (warm editorial, tokens v2), not a new product.

**Chosen directions** (variants A/B/C were explored in the prototype; these won):
- Projects list: **table rows** (not card grid)
- Project dashboard: **Overview-first** — documents by type dominate, tasks in a right rail
- Task board: task links shown as **chips** on cards; opened task card is a **centered modal** (not slide-over)
- Navigation: **variant A** — "Projects" as a nav button in the existing shared topbar (not a workspace switcher)

## About the Design Files
`IndieDev Module.dc.html` is a **design reference created in HTML** — an interactive prototype showing intended look and behavior, not production code. The task is to **recreate these screens in the existing codebase**: Angular standalone components in `cedarclerk-web/src/app/pages/`, using the app's established patterns (see "Reuse from the codebase" below). Ignore the prototype's floating bottom variant-switcher bar — it is prototype scaffolding, not product UI.

## Fidelity
**High-fidelity.** Every color, size, spacing and radius in the prototype is one of the existing tokens from `cedarclerk-web/src/styles.scss` — recreate pixel-perfectly by using those tokens, never literals. The only new UI conventions introduced here are listed under "New patterns" below.

## Reuse from the codebase (do not rebuild)
- **Tokens**: everything from `styles.scss` `:root` / `[data-theme="dark"]` / `[data-density="compact"]`. Both themes come free if only tokens are used — the prototype hardcodes zero colors outside its prototype bar.
- **Topbar**: `shared/page-header.component` — extend, don't fork (see Navigation).
- **Icons**: `shared/icon.component` (Phosphor, `--icon-*` sizes). Icon names used are listed under Assets.
- **Buttons/modal**: the `.btn-accent` / `.btn-ghost` / `.modal-*` de-facto pattern (documented in `docs/DESIGN.md` "Component patterns"). This module is a good moment to formalize them as shared components — the prototype uses one consistent definition (editor's values: accent `padding: 7px 16px`, ghost `7px 14px`).
- **List-screen anatomy**: toolbar → filter chips → table, copied from `pages/drafts.component` (h1 `--fs-22` 700 `-0.02em`, sub-line `--fs-meta` `--t2` margin-top 3px, `.search-input` 240px, pill filter chips, table container `--sheet` bg + `--border` + `--radius-lg`, uppercase `--fs-10` `.07em` header row on `--alt`, row hover `--hover`).
- **Density**: `data-density="compact"` on Projects list and Task board page roots; Project dashboard is comfortable (default). Components read `--dens-*`, never hardcode.

## Screens

### 1. Projects list (`/projects`) — compact
- Body: `max-width: 1600px; margin: 0 auto; padding: var(--space-4) var(--space-5) var(--space-6)`, column flex, `gap: var(--space-3)`.
- Toolbar: title block ("Projects" + "21 projects · 18 active" sub), spacer, search input, `.btn-accent` "+ New project" (opens the create-project dialog — separate task, out of this handoff's scope).
- Filter chips: All / Active / Archived with counts. Active chip: `--asoft` bg, `--abord` border, `--accent` text, 600.
- Table columns: `56px | minmax(220px,1fr) | 80px | 80px | 80px | 150px | 110px` → cover, Name, Docs, Tasks, Assets (numbers right-aligned, `--fs-meta` `--t2`), Last activity, State.
- Cover cell: 44×30, `--radius-sm`, `--border`, striped placeholder (`repeating-linear-gradient(45deg, var(--alt), var(--alt) 4px, var(--surface) 4px, var(--surface) 8px)`) with 2-letter mono initials in `--t3` until the project has a real cover image.
- Name: `--dens-fs` 600, ellipsized. Archived projects: name in `--t2`.
- State badge: same shape as drafts `.status-badge` (`--fs-caption` 600, `2px 8px`, `--radius-sm`). Active = `--asoft`/`--accent`; Archived = `--alt`/`--t2`.
- Row click → project dashboard.
- States still to design (next round): zero projects, one project, loading, error.

### 2. Project dashboard (`/projects/:id`) — comfortable, the module's central screen
- Body: `max-width: 1360px`, padding `var(--space-5)`, column gap `var(--space-5)`.
- Header row: 56×38 cover, title block ("Cedar Station" `--fs-22` 700 + status line "Active · Sprint 14 — Ferry Terminal · 26 documents · 812 assets · updated today 11:42" in `--fs-meta` `--t2`), spacer, `.btn-ghost` Settings, `.btn-accent` "+ New document" (opens doc-type dialog — out of scope here).
- Grid: `minmax(0,1fr) 320px`, gap `var(--space-5)`, items start-aligned. **Sections are deliberately unequal**:
- **Main column — documents by type.** Three full cards (Devlog, Game design, Script): card = `--sheet` bg, `--border`, `--radius-md`, `--shadow`, padding `16px 18px`. Card head: type icon `--icon-md` in `--accent`, type name `--fs-body` 600, count `--fs-caption` `--t2`, spacer, "View all →" link `--fs-meta`. Then 3 recent doc rows: title `--fs-ui` / meta `--fs-meta` `--t2`, separated by 60%-faded `--border` hairlines, hover `--hover`.
- Below: three small tiles in a `repeat(3,1fr)` grid (Story outline, Changelog, Notes): icon + name `--fs-ui` 600 + count, last-item line `--fs-meta` `--t2` ellipsized. Hover: border → `--abord`.
- **Right rail (320px), three cards** same card chrome, padding `14px 16px`:
  - *Up next*: 5 tasks sorted by urgency — title `--fs-ui`, below it priority chip + due date. Overdue due date: `--danger` 600 with "· overdue" suffix — only the date is red, nothing else. "Board →" link in head. Task click → board with that task's card open.
  - *Sprint*: name 600, progress bar (6px track `--alt`, radius 3px, fill `--accent`), "9 of 14 done" / date range `--fs-meta` `--t2`, and an "1 task overdue" line in `--danger` when true.
  - *Recent assets*: rows of type icon (`--t2`) + filename in 12px `--font-mono` + time `--fs-caption` `--t2`. **Missing-on-disk row**: warning icon + time text in `--danger`, filename in `--t2` — states "not found at path", never pretends the file is gone.

### 3. Task board (`/projects/:id/tasks`) — compact
- Same toolbar anatomy as Projects list; sub "Cedar Station · 12 open · Sprint 14 ends Aug 16".
- View toggle (board/list), same control as drafts `.view-toggle`: `--alt` pill, active segment `--sheet` bg + `--accent` icon + `--shadow`. Both views required.
- Sprint filter chips: All / Sprint 14 / Sprint 15 / No sprint, with counts.
- **Board view**: `repeat(4, minmax(0,1fr))` columns, gap `--dens-gap`. Column: `--alt` bg, `--radius-md`, 8px padding, header = uppercase `--fs-10` name + count. Card: `--sheet`, `--border`, `--dens-radius`, 10px padding, hover border `--abord`. Card contents, top to bottom:
  - title `--dens-fs` 600, line-height 1.35
  - meta row: priority chip, due date, sprint chip (`S14` in 11px mono on `--alt`)
  - **link chips** (chosen variant): icon + label, `--fs-caption` `--t2` on `--alt`, `--radius-sm`, `2px 7px`, ellipsized
- **List view**: table `minmax(260px,1fr) | 110px | 60px | 110px | 110px | 170px` → Task, Status, Prio, Due, Sprint, Links (links as text summary "2 documents · 1 task"). Sortable headers like drafts.
- Statuses: Backlog, Planned, In progress, Done. Status badge: In progress = `--asoft`/`--accent`; Done = `color-mix(in srgb, var(--ok) 16%, var(--sheet))`/`--ok`; others `--alt`/`--t2`.
- Priority chips: P1/P2/P3 in 11px `--font-mono` 600, `2px 8px`, `--radius-sm`. P1 = `color-mix(in srgb, var(--warn) 16%, var(--sheet))` bg + `--warn` text; P2/P3 = `--alt`/`--t2`.
- Overdue: date text `--danger` 600 + "· overdue" — never the whole card or column.

### 4. Task card — centered modal (chosen variant)
- Scrim `var(--scrim)`, click closes. Card: 560px (max 92vw), max-height 82vh, `--sheet`, `--border`, `--radius-lg`, `--shadow-lg`, column flex, inner scroll.
- Head row (padding `14px 20px`, bottom `--border`): status badge, sprint id in mono `--t2`, spacer, × icon-button.
- Body (padding `18px 20px`, gap 18px): title 19px 700 lh 1.3; 2-column field grid (Priority chip, Due, Sprint full name, Assignee — plain mono text, single user, no people UI); Description — plain text `--fs-ui` lh 1.6, `white-space: pre-line` (**not** a rich editor; empty state in `--t3` italic: "No description. A task that needs tables or media is really a document."); Linked — rows of icon (`--accent`) + label + kind (`--fs-caption` `--t2`), hover `--hover`; created/updated line `--fs-caption` `--t2`.
- Field labels: `--fs-caption` uppercase `.05em` 600 `--t2`.
- Foot (padding `14px 20px`, top border): `.btn-ghost` Archive, spacer, `.btn-accent` "Mark done".

### 5. Navigation (variant A)
- Extend `PageHeaderPage` with `'projects'` and add a "Projects" nav button to `page-header.component.html`, **first in the nav row** (before Posts). Icon: `game-controller` (add to Phosphor set via `npm run icons:generate` flow). Active (`.on`) on all module routes.
- Crumb walks the hierarchy: `Projects` → `Projects / Cedar Station` → `Projects / Cedar Station / Tasks` (project name segment should ellipsize per T-051; never widen the header).
- Existing screens (/drafts, /posts, /glossary, /settings, editor) are untouched.

## Interactions & Behavior
- Project row/card click → dashboard. Dashboard "Up next" task click → board + open that task's modal. "Board →" link → board.
- Board card / list row click → task modal. Scrim click, ×, Escape → close.
- Board/list view toggle persists (localStorage, like drafts' view).
- Transitions: hover feedback `--motion-fast`; modal in `--motion-slow`; all with `--ease`. Honor `prefers-reduced-motion` (global rule already exists).
- Hovers: rows `--hover`; cards border → `--abord`; icon-buttons `--hover-strong`; `.btn-accent` `brightness(1.08)`.
- Focus: rely on the global `:focus-visible` ring — remove nothing.
- Touch: existing `@media (pointer: coarse)` 44px floor covers the buttons; keep the board usable on iPad landscape (columns may scroll horizontally).

## State Management
- `projects.service`: list (id, name, coverPath, docCount, openTaskCount, assetCount, lastActivityAt, isArchived); current project.
- `tasks.service`: per project — id, title, status (backlog|planned|in_progress|done), priority (1|2|3), description (plain text), assignee (free text), dueDate?, sprintId?, links[] ({kind: document|asset|task, targetId, label}).
- `sprints`: id, name, startDate, endDate; progress derived from tasks.
- Dashboard aggregates: doc groups by type with 3 most recent, up-next tasks (overdue first, then by due date), recent assets with `foundOnDisk` flag.
- UI state: board view mode, sprint filter, open task id (route param so a task is linkable).

## Design Tokens
None new. Everything references `cedarclerk-web/src/styles.scss` tokens v2 — colors, `--dens-*`, `--fs-*` roles, `--icon-*`, `--space-*`, radii, `--motion-*`. Both themes and the WCAG contract (`npm run check:contrast`, `--t3` never carries information) come along automatically. The prototype's few `color-mix` chip tints (`--warn`/`--ok`/`--danger` at 16% over `--sheet`) mirror the existing `.status-badge.tone-*` pattern in drafts.

## New patterns introduced (worth adding to docs/DESIGN.md when built)
- Striped cover placeholder with mono initials (projects without cover art).
- Priority chip (mono P1/P2/P3, warn tint on P1 only).
- Link chip (icon + label on `--alt`) — used on board cards; degrades to a text summary in dense list view.
- Kanban column on `--alt` with `--sheet` cards — compact-density surface, borders not shadows.

## Assets
No image assets. Phosphor icons used: tree-evergreen (logo stand-in), game-controller (Projects nav), newspaper, book-bookmark, gear, plus, file-text, check-square, images, image, waveform, cube, film-slate, book-open, tree-structure, list-numbers, note, flag, timer, link, kanban, list, x, warning, pencil-simple, check. Sample content depicts "Cedar Station" (low-poly psychological thriller, opossum protagonist, PNW) — replace with real data.

## Screenshots
`screenshots/` — every screen in the chosen variants (modal/dialog shots are isolated on `--bg`; in product they sit over the underlying screen behind `--scrim`):
1–2 Projects list · 3–4 Dashboard · 5–6 Task board · 7–8 Task card modal · 9–10 Planner · 11–12 Asset grid · 13 Assets scanning state · 14 Pick-folder state · 15–16 Reference board · 17–18 Press kit edit · 19 Press kit public · 20–21 New-project dialog · 22–23 New-document dialog · 24–25 Asset view (audio, honest no-preview) · 26 Asset view (missing file). DOM-capture artifacts exist (e.g. odd chip/count wrapping, dialog title wraps); trust the token values in this README and the live prototype over pixel-picking the PNGs.

## Files
- `IndieDev Module.dc.html` — the interactive prototype (all screens; bottom bar switches screens/variants; ☾ toggles theme). The chosen variants are: Rows / Overview / Chips / Modal / Nav A.

## Round 2 screens (all in the same prototype file)
Same tokens, same conventions as above; density noted per screen.

### 6. Create-project dialog
Modal (560px, standard modal chrome). A project is never empty — the dialog is a **type picker**, not a name field: 4 radio-card rows (Full game / Game jam entry / Prototype / Released game), each with Phosphor icon in `--accent`, name `--fs-ui` 600, one-line blurb `--fs-meta` `--t2`, and an italic `--fs-caption` "Starts with: <starter document>" line. Selected row: `--asoft` bg + `--abord` border. Name input below (label = uppercase `--fs-caption` pattern). Footer: ghost Cancel / accent "Create project".

### 7. New-document dialog
Modal (640px). 2×3 grid of type cards: Devlog post, Game design doc, Script, Story outline, Changelog, Note — icon + name + one-liner. Hover: `--abord` border + `--asoft`. Footer note is a product rule: **document type is marked by icon only, everywhere it's listed — no colour labels.** Click creates and opens the editor (existing surface).

### 8. Planner (`/projects/:id/planner`) — compact
Stacked sprint cards, one per sprint: header strip on `--alt` (name 600, date range, state chip Current/`--asoft` · Planned/`--alt` · Done/ok-tint, honest "1 overdue" in `--danger` 600 when true, progress `n of m` + 120px bar). Rows: task grid `minmax(240px,1fr) 60px 110px 130px` → title / prio chip / status badge / due (overdue = danger date text only). Order: current, next, No sprint, then done sprints collapsed to a "11 tasks completed · collapsed" line. Row click → task modal.

### 9. Asset grid (`/projects/:id/assets`) — compact
This is an **index of local paths, not uploads** — the UI must never promise more. Toolbar: count + mono folder path + "3 not found" in `--danger`; search; grid/list toggle; ghost Re-index; ghost Change folder. Type filter chips incl. a danger-toned "Not found" chip. Grid tile: 4/3 preview area — sprites get the striped placeholder (thumbnail in product), models/audio/music get an honest icon + "no preview · <kind>" in `--t3` on `--alt` (never an empty rectangle); missing files get `ph-warning` + "not found at path" on a `color-mix(in srgb, var(--danger) 8%, var(--surface))` tint; name in 11px mono, meta caption. List view: File/Type/Details/Status table, status "on disk" `--ok` / "not found at path" `--danger` 600. Footer: "1–18 of 812 · rows are virtualized" — virtualize for tens of thousands. **Scanning state**: banner with spinning `ph-circle-notch` (`spin` keyframes), mono path, "4,812 of ~12,300 files", progress bar, Cancel; skeleton tiles below. **Pick-folder state**: centered 520px card — copy explains nothing is uploaded and a disconnected drive reads "not found at path", not deleted; path input (mono), Recent list, Cancel/accent "Index this folder".

### 10. Asset view — modal (780px)
Header: kind chip + full mono path. Body grid `320px | 1fr`: left preview pane (same honesty rules as tiles, 34px icon); right — filename mono 600, field grid (Type/Details/Status/Indexed; status coloured `--ok`/`--danger`), "Used in" (documents; honest "Not referenced by any document." when empty), "Linked tasks" (rows with status badge, click → task modal). Footer: ghost "Reveal in file manager", ghost "Re-index file", caption reminder that the file stays on disk.

### 11. Reference board (`/projects/:id/refs`) — comfortable
The only irregular grid in the product: CSS `columns: 4 240px`, `break-inside: avoid` cards — image (real thumbnails in product) + caption `--fs-ui` + source `--fs-caption` `--t2`. "Add images" accepts drops.

### 12. Press kit (`/projects/:id/press`) — comfortable
Two views, segmented toggle in the toolbar (product UI, not prototype scaffolding) + ghost "Copy public link". **Edit view** (820px column): section cards with provenance chips — `auto · from project` (accent chip; Facts), `auto · from asset index` (Screenshots, 16/9 thumbs with include-checkbox overlay), `manual` (`--alt` chip; Description, Trailers with Edit→/Add→ links). **Public view**: single `--sheet` card, 56/64px padding — the one new reading surface, so body copy is `--font-serif` 17px/1.7 (title 34px serif 700); facts as a 2-col grid with uppercase caption labels; screenshots 2-col; footer caption "This page regenerates from project data · updated <ts>".

### State additions for round 2
- `assets.service`: indexed folder path, per-file {path, kind, size, dims/duration, modifiedAt, indexedAt, foundOnDisk}; scan progress stream; recent folders. Missing ≠ deleted.
- `presskit`: facts derived from project + settings; manual description/trailers; screenshot include-set (asset ids).
- `refs`: image list with caption/source.
- Project types & doc types are static config: type → starter document template.
- New Phosphor icons: squares-four, arrows-clockwise, folder-open, clock-counter-clockwise, circle-notch, music-notes, text-aa, flask, rocket-launch, play, warning, square, pencil-simple.

## Out of scope / still undesigned
Empty/loading/error states for projects list, dashboard, and board (assets has its states above); zero-projects and one-project list states; iPad layouts; Russian localization pass (English chosen for mockups; German length stress-test rule applies: labels truncate, never widen).
