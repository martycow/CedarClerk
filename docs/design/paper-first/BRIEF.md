---
owner: marty
last_verified: 2026-09-01
source_of_truth_for: the "Paper first" direction — what replaces the bench chrome and why; the artboards beside it are the drawing
guard: none
---

# Paper first — the shell after Cedar Bench

The maintainer looked at ten screenshots of v0.20.1 on a wide monitor (`docs/screenshots/`) and
ruled the UI unfinished and empty. This folder is the answer: eight artboards (`*.dc.html`,
`build.mjs` generates them, `canvas.json` lays them out; rendered copies are the `*.png` beside
them) and this brief. The artboards are the drawing; this file is the reasoning and the rules a
lane must not cross.

## What was wrong

1. **The frame is louder than the content.** Wood rails, sign boards, tab strips, the console bar
   and the ruler bar take the eye; the page sits in a box inside a box.
2. **Nothing grows with the screen.** Fixed-height boards and calendar rows, small cards pinned
   top-left; on 3400px most of the screen is bare paper.
3. **Twelve equal hooks with 10px labels** and a caret above each: navigation with no hierarchy.
4. **Empty states say nothing.** "Nothing here", a blank calendar, a dashboard of zeros.
5. **Three faces per screen** and mono everywhere in the inspector; with the request/error console
   at the foot the app reads as a debugger.

## The direction

The palette, faces and tokens of Cedar Bench stay (ADR-136/137/138 — `styles.scss` is still the
contract). The wood chrome goes. Concretely:

- **Sidebar, 232px**, paper (`--surface`), one hairline right edge. Top: logo + wordmark, then a
  project switcher card (initials tile, name, kind · count, caret). Navigation in three labelled
  groups — **Write** (Documents, Assets, Site), **Plan** (Tasks, Planner, Calendar), **Ship**
  (Builds, Posts, Metrics) — 14px labels, 17px icons, counts right-aligned, the active item a solid
  `--accent` plaque with cream ink. Bottom: All projects, Settings, then a user row (avatar,
  username, bell). Admin lives in the account menu, not the rail.
- **Page header**, not a sign board: title in `--font-display` 27px, a meta line under it
  (status tag · kind · counts · last edit), the page's one primary action right-aligned with at
  most one secondary beside it.
- **Content fills the viewport.** Lists, boards, calendar rows and grids stretch; the document list
  is a scrolling card; a board column is a full-height column; a calendar cell grows.
- **Every empty state names the next action** — a dashed area with one sentence, a hand-written
  margin note (`--font-note`) where the page wants a hint.
- **Console and ruler bar leave the working screens.** The debug console stays reachable from the
  account menu / a keyboard shortcut; the ruler's readouts move into the page header meta line and
  the editor's footer.
- **Editor = one document, three tabs**: Write / Preview / Publish under the document title, a
  shared top bar (project switcher, undo/redo, saved state, date, Publish split button), a footer
  with the next step. Write: toolbar strip, a 680px serif sheet, an inspector (Document, Languages,
  Outline). **Preview**: destinations on the left with readiness (Blog Ready · Telegram Ready · X
  not connected · Bluesky not connected), the rendered page in the middle (Desktop/Mobile, width,
  RU/EN, theme), checks for the selected destination on the right; selecting Telegram swaps the
  render to a phone with the real message split and the checks to Telegram's. Publish: what the
  Export modal holds today — targets, schedule, CTA buttons, the run.
- **Icons are the Phosphor set the app already has** (`app-icon`), never emoji.

## What this does not change

- Tokens, the type scale, density modes, the light/dark axis, the contrast and density gates —
  new chrome is measured by the same tools before it lands.
- The blog and the landing page: server-rendered, ADR-137 contract, untouched by this port.
- The document model, the renderers, the publish pipeline.
- The bench *worktop* components (`spec-row`, `growth-chart`, `module-tile`, `log-line`) keep
  their jobs; they are restyled, not replaced. `shelf-panel` becomes a plain card.

## Direction B (not chosen)

`DirectionB.dc.html` keeps the wood and only fixes scaling and empty states. Cheaper, rejected:
the frame stays the loudest thing on screen.
