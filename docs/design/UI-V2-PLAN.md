---
source_of_truth_for: план переноса фронтенда на Design System V2 (Cedar Bench)
guard: none — plan document, superseded by the ADRs as they land
---

# UI V2 — Cedar Bench port plan

## 1. What V2 is

Cedar Bench (`.design-sync/ds-v2/`) is the Forest Workshop grown from a *repaint* into a *layout system*. V1 (ADR-120, `cedarclerk-web/src/styles/_forest.scss`) is 1 246 lines of attribute-scoped overrides that recolour existing app classes — same screens, wood-and-paper palette. V2 keeps that palette almost byte-for-byte (~34 colour tokens identical, all three noise SVGs identical, all four faces already self-hosted) and adds what V1 never had: an application shell (`RailHeader` / `HookRail` / `ShelfPanel` / `BenchDrawer` / `RulerBar`), a two-class density contract (chrome 30px vs paper 44px), 20 real components instead of class repaints, and layout-dimension tokens. The port is therefore **not a colour job** — it is a chrome-and-component job on a palette that mostly already ships.

---

## 2. Decisions before code

| # | Decision | Recommendation | Reason |
|---|---|---|---|
| **D1** | Theming attribute: V2's `data-time="night"` vs the app's `data-theme` × `data-skin` | **Keep `data-theme` and nothing else.** V2's `:root` ports onto the bare `:root` block at `cedarclerk-web/src/styles.scss:8`, `[data-time="night"]` onto `:root[data-theme="dark"]` at `styles.scss:178`; `data-time` is discarded as a mechanism and `data-skin` is retired with it | `[data-time]` is (0,1,0) and loses to `:root[data-theme="dark"]` (0,2,0) — dropped in verbatim it silently fails on every token the app's dark block also owns. `data-theme` is the only axis with persistence (`cedar-theme`, `core/theme.service.ts:5`), a pre-auth path (the `ThemeService` constructor applies it at `theme.service.ts:13-16`, so login is already themed) and a server-rendered twin (`CedarClerk.Server/BlogEndpoints.cs`, `LandingEndpoints.cs`, fed by `DesignTokens`). A second attribute earns its keep only while two looks exist; with one look `data-skin` selects between a palette and itself |
| **D2** | Is Bench a third skin, or does it replace Forest? | **Neither — Bench becomes the one and only look, and the skin mechanism retires with the port.** The `Skin` union (`core/theme.service.ts:4`), `setSkin`/`applySkin`/`loadInitialSkin` (`theme.service.ts:28-53`), the `cedar-skin` key (`theme.service.ts:6`), the Appearance control (`appearance-panel.component.html:15-21`) and `styles/_forest.scss` (1 246 lines) all go. The `default` palette is not preserved anywhere — its values are overwritten in place | Forest repaints app classes (`.btn-accent`, `.chip`, `.status-badge`) that Stage 6 deletes outright, so the partial outlives its own targets by at most one stage. Two palettes means every new bench component carries a second set of values and `cedarclerk-web/tools/check-contrast.mjs` a second pass — that is how the blog fell a shade behind the app before `DesignTokenDriftTests` existed. `--accent` is injected at the bare `:root` by `core/appearance.service.ts:124`, which lands on the contract directly once nothing is scoped above it. Supersedes ADR-120; the textures in `cedarclerk-web/public/assets/forest/` outlive the partial that referenced them |
| **D3** | Token namespace | **No partial and no attribute scope: bench values are written into the bare `:root` block (`cedarclerk-web/src/styles.scss:8`) and into `:root[data-theme="dark"]` (`styles.scss:178`).** Material names (`--wood --paper --pine --brass --resin --rail-ink --bench-*`) are added **alongside** the contract names (`--bg --canvas --surface --sheet --alt --text --t2 --t3 --accent --danger --ok --warn --border-strong --asoft`, plus `--series-1..6`), which keep their exact spelling and stay flat colours; where a bench material is the same colour as a contract token, the contract name is the one components use | `cedarclerk-web/tools/check-contrast.mjs:30-31` and `cedarclerk-web/tools/generate-design-tokens.mjs:40-41` both `indexOf(':root')` in `styles.scss` and brace-match from there, so the base blocks are the only place a token is contrast-checked *and* emitted into `DesignTokens.Light`/`Dark` for the blog and the landing page. Bench in the base blocks gets both for free — and that makes the names load-bearing: `DesignTokenDriftTests` pins ten by `[InlineData]` (`text t2 t3 accent danger ok warn border bg surface`) and compares the raw declaration text, so a rename is a red test and a gradient is a gradient string painted onto the blog. `--accent` is additionally injected at `:root` by `core/appearance.service.ts:124` |
| **D4** | Component library layout | **`cedarclerk-web/src/app/bench/`**, subfolders mirroring the DS one-to-one (`chrome/ worktop/ display/ forms/ scenery/`), `kebab-case.component.ts`, selector prefix `app-` (`angular.json` `"prefix": "app"`). No barrel file. `<cc-icon>`/`<cc-logo>` are **not** ported | `shared/` is already 44 flat files mixing shells, pickers with real behaviour and page fragments; a design library under one root is what makes "does this already exist?" answerable — `.claude/rules/ui-changes.md` rule 1 applied to code. `app-icon`/`app-cedar-logo` already fill the icon roles, and a custom element would force `CUSTOM_ELEMENTS_SCHEMA` on every consuming template — rejected in ADR-072 |
| **D5** | What gets deleted | `shared/page-header.component.*` (12 call sites) · `shared/count-badge.component.ts` (3) · `shared/debug-console.component.{html,css}` chrome only (service + interceptor survive) · the editor topbar nav block · every duplicated `.btn-accent`/`.btn-ghost`/`.btn-danger`/`.icon-btn`/`.status-badge`/tab-strip block in `src/app/**/*.css` · the skin machinery: `styles/_forest.scss`, the `Skin` union and its three methods in `core/theme.service.ts:4-53`, the `cedar-skin` key, the Appearance skin control (`appearance-panel.component.html:15-21`) with `skinLabel`/`skinDefault`/`skinForest` in `core/i18n/en.ts:1345-1347` and `core/i18n/ru.ts:1310-1312`, the inventory row at `docs/design/UI-INVENTORY.md:122`, §Skins at `docs/design/DESIGN.md:192`, and the old `default` palette values in the base blocks | Each is two objects doing one job once the Bench component exists, and the skin machinery is a switch with one position left. Bulk deletion is a `.claude/rules/destructive-operations.md` event: explain, stop, wait |
| **D6** | Half-pixel sizes | **Round on port**: `--text-lg` 16.5→16, `--text-chrome` 12.5→13 (`--fs-13`), `--text-stamp` 10.5→11 (`--fs-11`) | ADR-071 principle 7 / `DESIGN.md:22` — integers only, a measured decision taken after 110 half-pixel declarations rounded inconsistently across zoom levels. V2 ships 67 half-pixel values; copying them reverses a measurement |
| **D7** | V2's night palette | **Do not port verbatim.** Re-derive night `--series-*`, `--rust` and `--text-body`-on-wall *downward*, against cream paper | V2's night block lightens the series set while night paper stays cream: `--series-pine` #7C9A72 on night `--paper` #E6DCC2 is **2.29:1** against a 3.0 floor. `--text-body` #241E13 on night `--wall-lo` #251F13 is **1.01:1** — invisible. `_forest.scss:96-104` already solved exactly this, and V2 reverses it |
| **D8** | Density | **Two orthogonal axes, both kept.** `data-density="compact"` (page-level, paper only, 9 page roots, ~155 `--dens-*` call sites) stays; `data-surface="chrome"\|"paper"` (element-level) is new. `[data-density="compact"]` must never redefine a `--bench-*`/`--text-chrome*`/`--hit-chrome` token | Compact `--dens-fs` is already 14px — exactly V2's paper floor, so compact is contract-compliant unchanged and must not be tightened. No page-root attribute can express "56px rail and a 30px ruler around a 640px reading sheet", which is why the surface split exists at all |
| **D9** | Icons | Keep `app-icon` and the generated Phosphor set. Add six glyphs to `tools/icon-map.json` — `magnifying-glass dots-three chart-bar flag text-h tree-evergreen` — then `npm run icons:generate` | Only 13 of V2's 36 names exist verbatim; 17 more are renames onto existing Phosphor entries; six are genuinely absent (verified against the map's 96 rows — of the candidates only `tree-structure` is present, and it is a hierarchy diagram, not a conifer). Shipping `bench-icons.js` as the app's source would cut the editor toolbar to 36 glyphs |
| **D10** | Fonts | Drop `.design-sync/ds-v2/tokens/fonts.css` entirely | All four faces are already self-hosted via `@fontsource` (`_forest.scss:7-22`, 16 subsets, 315 KB). Matching the mirror's Google `@import` literally adds 292 KB of weights and italics **no V2 component uses** — the kits use 400/600/700 only, and Caveat ships no italic files at all |

---

## 3. Work breakdown

Task lines follow the board format `- [ ] T-xxx Name — description #tags P1..P3`, and carry the ids the rows hold on the board. Stages 0 and 1 are ticked here because they shipped; their status is `docs/tasks/ROADMAP.md`, and the open rows — Stages 2 through 6 — live in `docs/tasks/BACKLOG.md`, which is the only place they are open.

### Stage 0 — tooling before tokens

`cedar test` is green before any V2 code is written. The mirror lives at `.design-sync/ds-v2/`, outside `docs/`, so `DocsFlowGraphTests.Every_live_doc_is_on_the_map` (`CedarClerk.Tests/DocsFlowGraphTests.cs:28`) never enumerates its 22 `.md` files, and `Every_mapped_path_exists` (`DocsFlowGraphTests.cs:49`) matches only `docs/…\.md`, so the folder's single mention in `docs/DOCS-FLOW.md:146` costs nothing. Stage 0 therefore builds rather than repairs: the two checks the rest of the port is measured by.

```
- [x] T-205 Contrast checker rework — check-contrast.mjs composites alpha in resolveColor, evaluates gradients at every stop and fails on the worst, and rejects outright any contract token that resolves to a gradient #a11y #tooling P1
- [x] T-206 Density lint — new tools/check-density.mjs: chrome 30px/13-11px vs paper 44px/>=14px, no half-pixel font-size, density never touches a bench token; wire into package.json beside check:contrast #a11y #tooling P2
```

Keep the mirror out of `docs/`: a mirror path spelled there puts it under `Every_mapped_path_exists`, which then requires the files on a fresh clone and forces an untracked cache to be committed. Precedent is against it — `docs/design_handoff_indiedev_core_loop/` is referenced from three docs and is not on disk.

The partial is left in place rather than deleted: nothing writes `data-skin`, so nothing in it can match, and the file survives until Marty says the word (`T-235`).

The checker rework is a hard prerequisite for Stage 1: 19 day pairs and 21 night pairs in V2 already fail, and once bench values sit in the base blocks the checker reads every one of them on every run — with alpha discarded and gradients throwing it either lies or crashes.

### Stage 1 — tokens

```
- [x] T-207 Bench base palette — rewrite the :root block in cedarclerk-web/src/styles.scss from ds-v2/tokens/*.css: contract names keep their spelling and stay flat, material names added alongside #tokens P1
- [x] T-208 Bench night block — re-derive night series/rust/text-body against cream paper into :root[data-theme="dark"] at styles.scss:178, carrying the _forest.scss:91-160 corrections #tokens #a11y P1
- [x] T-209 Retire the skin mechanism — delete the Skin union, setSkin/applySkin/loadInitialSkin and the cedar-skin key in core/theme.service.ts, the Appearance skin control and its three i18n keys; drop the UI-INVENTORY row and DESIGN.md §Skins in the same commit #tokens #cleanup P1
- [x] T-210 Bench contrast pass — run check:contrast against the rewritten base blocks and fix the 40 failing pairs with re-derived values #a11y P1
- [ ] T-235 Delete the neutralised forest partial — styles/_forest.scss and its @use in styles.scss; every rule in it is scoped under an attribute nothing sets, so it compiles into the bundle and matches nothing. A 1 229-line deletion is a `.claude/rules/destructive-operations.md` event, so it waits on Marty #tokens #cleanup #decision P2
```

Token rules for this stage. Keep `--space-*` — V2's `--sp-5`/`--sp-6` are 20/26 against the app's 24/32, so a mechanical rename silently shrinks every gap. Keep `--fs-*` — V2's `--text-*` size family collides with the app's `--text` colour. Keep `--motion-*` and `--wk-ease-pop` (identical to `--ease-settle`). Keep `--series-1..6` as the data palette and alias V2's four named series onto the first four; adopting the named set outright cuts categorical capacity from 6 to 4 and breaks the fixed assignment order.

Import only what is genuinely new: the 8 `--bench-*`, `--hit-chrome`/`--text-chrome*`/`--text-readout`, `--rule-*`/`--grid-*`, `--shelf-frame`/`--pegboard`/`--shadow-shelf`/`--shadow-sheet-inset`, `--resin`/`--resin-hi`, `--page-max`/`--page-pad`/`--hit-target`, `--dur-publish`, `--ease-swing`, and `--space-7`/`--space-8` at 34/48. Point `--tex-*` at `/assets/forest/` — the three SVGs are byte-identical to the mirror's; do not create a second copy. `--wk-tex-cork` is currently declared and referenced zero times; V2 gives it a job as `--pegboard`.

### Stage 2 — primitives and the kit page

```
- [x] T-211 Bench icons — add magnifying-glass dots-three chart-bar flag text-h tree-evergreen to tools/icon-map.json, run icons:generate and icon-usage, commit both generated files #icons P1
- [x] T-212 Button component — app/bench/forms/button.component.ts, variants pine|paper|rail|danger, sizes md|sm #components P1
- [x] T-213 Input component — app/bench/forms/input.component.ts implementing ControlValueAccessor; the app binds ngModel (tag-picker.component.html), which a plain input() signal cannot serve #components P1
- [x] T-214 Display primitives — stamp-badge, leaf-tag, resin-drop, paper-card, task-tag, spec-row, brass-pin, brass-hook under app/bench/ #components P2
- [x] T-215 Styleguide rewrite — replace the Buttons/Fields/Status sections of pages/styleguide.component.* with the bench kit and add a surface-class toggle #components P2
```

Prove every primitive on `/dev/styleguide` before it touches a product screen — it exists for this and has no users to break. Its Buttons/Fields/Status sections are *replaced*, not appended to; a styleguide showing both vocabularies is the ambiguity the port exists to end.

Translate the JSX API mechanically: `children` → `<ng-content>`, named slots → `<ng-content select="[actions]">`, `onX` → `output()`, everything else → `input()` signals. The mirror's `style?: React.CSSProperties` idiom does **not** port — it reintroduces the one-off values ADR-071 exists to stop; it becomes explicit inputs (`width`, `rotate`, `labelWidth`) plus `:host` styling from the consumer. The two components that inject a global `<style>` at import (`Button.jsx` `.cb-btn*`, `Input.jsx` `.cb-input*`, plus `ResinDrop.jsx` keyframes) become normal encapsulated styles; no `cb-` class name survives.

### Stage 3 — the shell

The app has no shell today: `src/app/app.html` is `<router-outlet />` plus the debug console, `app.scss` is empty, and every page draws its own header.

```
- [ ] T-216 Chrome components — rail-header, hook-rail, shelf-panel, bench-drawer, ruler-bar, worktop, index-tabs under app/bench/chrome and app/bench/worktop #components P1
- [ ] T-217 Bench shell — app/bench/chrome/bench-shell.component.*, one parent route in app.routes.ts wrapping the authenticated children unchanged; login/register/terms/privacy stay outside it #shell P1
- [ ] T-218 Retire page-header — delete shared/page-header.component.* and its 12 call sites, delete the editor topbar nav block in editor.component.html #shell P1
- [ ] T-219 Chrome touch carve-out — scope the @media (pointer: coarse) 44px rule at styles.scss:304 so it cannot inflate rail, hooks, shelf headers, drawer lip and ruler #a11y P1
- [ ] T-220 Drawer re-house — debug console content becomes the BenchDrawer body; core/debug-log.service.ts and the interceptor survive; hostBarHeight retires; update app.spec.ts #shell P2
- [ ] T-221 Status bar to ruler — word/char/sync readouts move to RulerBar; invisibles and fullscreen move to the editor tool strip; the console toggle disappears because the lip is the toggle #shell P2
```

Vertical chrome budget in the editor: 44 + 58 + 58 + 27 = 187px today, against 56 + 32 + 30 = 118px global plus a 36px in-content tool strip. The two-row toolbar collapsing to one 36px strip is the biggest density delta in the app and voids ADR-035's user-ordered row assignment — see Q3.

Tool budget: V2 allows 5–7 hooks against 10 destinations. Proposed six — Hub (`/projects`, hidden when `auth.indieDev()` is false), Text (`/drafts` + `/editor`), Board (`/projects/:id/tasks`), Assets (`/library`), Metrics (`/posts`), plus bottom-anchored Settings. Glossary, Admin and `/dev/*` go behind the rail's `dots` control, which is what `RailHeader.prompt.md` prescribes for the rare rest.

Two rules the current chrome breaks and this stage must fix: **no transparency or blur** (`.page-header` uses `--glass` + `backdrop-filter`, and `.topbar::before` exists *only* to work around blur creating a containing block — killing blur deletes the hack), and the density split.

One data gap the rail hits immediately: `DraftMeta` (`core/drafts.service.ts:107-140`) carries no `projectId`, so the rail's project switcher has nothing behind it on `/drafts`, `/editor`, `/posts` and `/library`. Either the API grows the field or the tile is account-scoped outside `/projects/:id`.

### Stage 4 — the three reference screens

Smallest blast radius first.

```
- [ ] T-222 Stats screen port — one multi-series GrowthChart replacing four 600x160 single-metric SVGs, sources as LeafTag legend-filters, audience grid into a 320px right ShelfPanel #screens P2
- [ ] T-223 Hub screen port — project.component grid 2 to 3 columns, ModuleTile re-cut from document types to modules, right shelf keeps sprint and up-next #screens P2
- [ ] T-224 Writer shell port — editor.component chrome, inspector shelf replacing the horizontal meta strip, one 36px tool strip #screens P2
- [ ] T-225 Writer outline panel — 224px structure shelf walking the TipTap doc with two-way ProseMirror selection sync #screens P3
```

Each screen carries data the API does not have; every one of these is a feature with its own row, not part of a re-skin.

| Screen | Missing behind the kit |
|---|---|
| Stats | X/Bluesky snapshots (neither has a stat entity), multi-source fetch (blog and channel series are two endpoints with two DTOs, never fetched together), CSV export (zero hits repo-wide), event markers on the axis, six period aggregates |
| Hub | Engine and target platforms, a dated milestone, per-project metrics (`Channel` has `OwnerId`, no `ProjectId`), a project→channel link table, an activity journal, build summary on `ProjectSummary` |
| Writer | The entire pre-publish **checks** system that the drawer tab and the ruler's check count both depend on — no endpoint, no client pass; image alt as an editable property; asset-link-on-insert; block index and count; zoom |

### Stage 5 — remaining pages by pattern

| Pattern | Pages |
|---|---|
| hub-like | `projects` (absorbed as the hub's left shelf), `project-planner`, `project-builds` |
| stats-like | `project-assets`, `media-library`, `drafts`, `admin` |
| writer-like | `posts-manager`, `glossary`, `comments` (a fragment → drawer tab, never its own screen) |
| own pattern | `project-tasks` (kanban — no kit covers it), `settings` (754 lines, comfortable by design), `login`/`register` (workshop-door scenery, no shell), `terms`/`privacy` (one sheet, no chrome), `styleguide`, `icons` |

```
- [ ] T-226 Port hub-like pages — projects, project-planner, project-builds onto Worktop plus ShelfPanel #screens P3
- [ ] T-227 Port stats-like pages — project-assets, media-library, drafts, admin onto IndexTabs plus one panel plus an inspector shelf #screens P3
- [ ] T-228 Port writer-like pages — posts-manager, glossary, comments fragment #screens P3
- [ ] T-229 Task board pattern — project-tasks needs a fourth reference screen from Claude Design before it can be ported #screens #decision P3
```

### Stage 6 — cleanup and ship

```
- [ ] T-230 Delete duplicated control CSS — .btn-accent (12 files), .btn-ghost (16), .btn-danger (2), .icon-btn (7), .status-badge (5), tab strips (8) #cleanup P2
- [ ] T-231 Retire count-badge — carry the hide-at-zero and 99+ cap into IndexTabs, delete shared/count-badge.component.ts #cleanup P3
- [ ] T-232 Close the skin era in the docs — mark ADR-120 superseded by ADR-136 in docs/DECISIONS.md, and check no --wk-* or data-skin reference outlived Stage 1 #docs #decision P3
- [ ] T-233 Smoke suite repair — rebind the e2e selectors listed under Risks to the bench markup #tests P1
- [ ] T-234 Version and merge — bump Consts.CurrentVersion to 0.13.0, tag 0.13.0, merge UI_V2 to master, deploy #release P1
```

`cedar deploy` refuses anything but `master` with a clean tree (`CedarClerk.Cli/Pipelines/GitGuard.cs`), so `UI_V2` merges before it can ship. Eyeball with `cedar run` (ADR-121) — the real `publish/` build on `localhost:8080` with the bot forced off, which is the only safe way to look at a re-skin without touching the production bot token.

Docs that must move with the code: `docs/DECISIONS.md` (index row per ADR, **before** the code), `docs/design/DESIGN.md` (§Principles 3 and 7, §Accessibility, §Known design debt, and §Skins deleted outright), `docs/design/UI-INVENTORY.md` (same commit as the code — new chrome goes in the **Shared components** table, not as 14 new page tables; deletions delete their rows and the replacement note migrates onto the surviving row; the Verification map resets), `docs/tasks/BACKLOG.md` → `TASKS.md` → `ROADMAP.md`, `CHANGELOG.md` at the end, and `.claude/skills/design-tokens/SKILL.md` once ADR-137/138/140 land.

---

## 4. ADRs

Highest existing was ADR-135, so the port starts at **ADR-136**. Each is a new `docs/adr/ADR-1xx.md` whose first line is `# ADR-1xx — Title`, plus its row appended to the index in `docs/DECISIONS.md`, no front-matter (ADR-123). All land **before** the code they govern.

| # | Thesis | Overturns / extends |
|---|---|---|
| ADR-136 | One look: Cedar Bench replaces the skin mechanism — `data-skin`, the `default` palette and forest all retire, and `data-time` is discarded with them; `data-theme` is the only surviving axis | Supersedes ADR-120 |
| ADR-137 | Token contract: the contract names keep their spelling and stay flat colours in the base blocks; bench material names are added alongside them, and the contract name wins wherever the two are the same colour | Protects ADR-071 principle 2, ADR-090, `DesignTokenDriftTests` |
| ADR-138 | Surface class (`data-surface`) as a second density axis: chrome 30px / 13–11px, paper 44px / ≥14px, integers only; contrast is lifted out of the split and applies to both | Narrows ADR-071 principle 7 and the `pointer: coarse` rule at `styles.scss:304` |
| ADR-139 | The bench shell becomes shared chrome; the header nav row and the Electron default menu bar are removed | Reverses ADR-052; required by `.claude/rules/ui-changes.md` rule 1 |
| ADR-140 | Focus ring: two layers — a `--brass-edge` outline inside a cream `--focus-halo` — because no single colour clears 3:1 on both paper and rail. Proposed here as a flat `--brass`; the measurement refused it | Supersedes the ADR-074 clause in `DESIGN.md` §Accessibility |
| ADR-141 | Night values are re-derived against cream paper; V2's night block is not ported verbatim | Defends the ADR-120 fix |
| ADR-142 | One icon runtime — the generated Phosphor set behind `app-icon`; `bench-icons.js` is reference only, missing glyphs go through `icon-map.json` | Extends ADR-072 |
| ADR-143 | Fonts stay self-hosted via `@fontsource`; the mirror's Google `@import` is not ported | Restates ADR-120 against the mirror |
| ADR-144 | The design-system mirror is an uncommitted cache at `.design-sync/ds-v2/`, outside `docs/` and outside git — what that costs: no history, no review, absent on a fresh clone, and every citation of it in this plan unverifiable by CI | Formalises the placement `docs/DOCS-FLOW.md:146` states |

Two more were written because the token work found defects the table above did not predict, and they took the next two numbers:

| # | Thesis | Overturns / extends |
|---|---|---|
| ADR-145 | A wash is a token: `--ok-soft`/`--warn-soft`/`--danger-soft` on `--asoft`'s formula, and the state inks re-derived against the wash they are painted on rather than against bare paper | Carries ADR-141's method one surface further; applies ADR-074 |
| ADR-146 | A painted colour that reaches the DOM through a binding is a token, not a literal: the avatar fills become `--avatar-1…6` + `--avatar-ink` behind one hasher | Narrows the T-077/T-101 literal exemptions |

Six more were written once Marty answered the questions in §6, and they took the next six numbers:

| # | Thesis | Overturns / extends |
|---|---|---|
| ADR-147 | Narrow screens are commissioned from Claude Design, not invented here: the shell is built at the kits' desktop proportions and adds no width breakpoint of its own | Blocks T-034; defers, does not answer |
| ADR-148 | Metrics stays a tab body inside the Posts Manager — no rail hook, no crumb, and the kit's rail-level CSV action moves into the chart panel's header | Keeps ADR-046's N7 against the kit; adjusts ADR-139 clause 4 |
| ADR-149 | The legend is the filter: sources become multi-select, one line per source and never a sum; the kit's five period segments are not ported | Overturns ADR-030's exclusive source selection; preserves ADR-049's N9 and I8; constrained by ADR-025 |
| ADR-150 | The tool strip is adaptive — one row or two, captions only when there is room — and toolbar customization is removed entirely | Voids ADR-035's first decision bullet and its `@angular/cdk` justification |
| ADR-151 | The theme toggle and the Appearance trigger go into the RailHeader dots menu, and the Appearance panel is hoisted into the shell | Extends ADR-139 clause 4 |
| ADR-152 | The accent picker survives; every accent-derived tone is derived from `--accent` and never held, and joins `ACCENT_DEPENDENT` in the same commit | Defends ADR-137 clause 5 and ADR-141 clause 6 |

A seventh followed at the next number, out of the port itself rather than out of a question: **ADR-153** — the debug console's chrome becomes a `BenchDrawer` and the editor's status bar dissolves into it and the `RulerBar`, readouts to the rule and controls to the tool strip (`T-220`/`T-221`).

---

## 5. Risks

| Risk | Concrete failure |
|---|---|
| A gradient reaches a contract token | Worse now than under a skin: the contract *is* the base block, so a gradient there is on the blog's delivery path by construction. `resolveColor` throws `cannot resolve: linear-gradient(…)` uncaught → `cedar test` red with a stack trace, while `generate-design-tokens.mjs` does not resolve values at all, *survives*, and writes the gradient string into `DesignTokens.Light` — `BlogEndpoints.cs` then paints it where a flat colour is expected |
| A `:root` string appears above the base block, in code or in a comment | Both tools `indexOf(':root')` and brace-match from the first hit, so anything earlier mis-anchors the light check onto the wrong block — the incident `styles.scss:1-6` records. Retiring the skin removes both the `@use 'styles/forest'` line and that comment; nothing may reintroduce a `:root` mention above `styles.scss:8` |
| Bench tokens put in a partial out of habit | Neither tool reads partials, so those tokens are never contrast-checked and never reach the blog — a silent coverage hole, not a pass. D3 exists to stop this; ADR-120 already records the same hole for forest |
| Mid-port the app renders bench tokens on unported screens | Stage 1 lands before Stages 4–5, and with `data-skin` gone there is no fallback to switch back to: every page still drawing its own `.page-header`, `.btn-accent`, `.stat-card` renders those rules against bench values, and the only way to see the previous look is `git checkout master`. Any contrast or legibility regression on an unported screen is live on the branch until that screen's stage lands, so `npm run check:contrast` after Stage 1 is the only thing watching them |
| Renaming `--text`→`--text-body`, `--t2`→`--text-muted`, `--series-N`→`--series-pine` | `vars[p.fg]` is `undefined` → `TypeError: Cannot read properties of undefined`; `DesignTokenDriftTests` goes red on ten `[InlineData]` names |
| V2 has no `--ok` and no `--warn` | Ten checker pairs reference them; dropping either crashes the run |
| V2 night ported verbatim | `--text-body` on night `--wall-lo` is 1.01:1 — body text is invisible on the page background; the whole night series set falls to 1.5–2.3:1 against cream paper |
| `--text-faint` used for real content | V2 puts chart axis numbers, Worktop labels, LogLine timestamps and ModuleTile sublines on 3.32:1 — the exact regression ADR-074 was written to stop after moving 91 of 107 `--t3` declarations up to `--t2` |
| `--brass` as the focus ring | 2.39:1 on `--paper`, 1.98:1 on `--wall-lo` — SC 1.4.11 fails on the app's only focus affordance |
| `pointer: coarse` left global | The 44px minimum silently inflates rail, hooks, ruler and drawer lip on touch; the rail becomes 70px, the ruler 44px, and the density contract is dead on arrival |
| Icon-dense chrome without labels | `e2e/12-a11y.spec.ts` fails on any `<button>`/`<a>` holding an `<svg>` with no text and no `aria-label` — HookRail, ShelfPanel actions, RulerBar, drawer toggle |
| Class renames across the smoke suite | 17 spec files bind to `.tiptap` ×21, `.post-card` ×6, `.export-trigger` ×5, `.drafts-title` ×5, `.theme-toggle`, `.btn-accent`, `.stat-card`, `.admin-tabs` and `app-page-header`; `e2e/10-ui-shell.spec.ts` breaks the moment the header is replaced |
| New `pages/*.component.ts` or new `sec-*` id without an inventory row | `UiInventoryDriftTests` red. Worse: a **renamed** page leaves a stale row, so the test stays green while the inventory lies |
| Bulk CSS deletion without confirmation | `.claude/rules/destructive-operations.md` — explain, then stop and wait |
| A glyph name absent from `icon-map.json` | `app-icon` renders **nothing**, silently. Six V2 names are missing from the map's 96 rows today |
| Copying the mirror's inline-`style` idiom | Re-seeds hardcoded values across components — the debt ADR-071 principle 7 exists to prevent, rebuilt one component at a time |
| Half-pixels ported as authored | 67 values reverse a measured ADR-071 decision, and `check-density.mjs` (Stage 0) turns every one of them red |

---

## 6. Questions and their answers

All six are settled and each carries its ADR. Four are Marty's own answers; two were defaulted by the
implementer under a general instruction to proceed, were shown to him as defaults rather than asked,
and are reversible — their ADRs say so in the same words.

1. **Q-19 — does `/stats` become a route again?** **Answered by Marty: no, it stays a tab.**
   `ADR-148`. The kit gives metrics a crumb, a hook, a ruler and a rail-level CSV button; the app
   keeps it as a tab body inside the Posts Manager, the rail spends no hook on it, and the kit's
   export action moves into the chart panel's own header.
2. **Q-20 — stats sources and period control.** **Answered by Marty: the kit's legend-filter, and
   the slider.** `ADR-149`. Sources become multi-select filters that *are* the legend; the kit's five
   fixed period segments are not ported, so N9 and I8 survive intact and the only deliberate fix
   overturned is ADR-030's exclusive source selection. What is lost is one-click "only this channel".
   The plan's framing above — "one of each pair loses" — was wrong: the app already carries the
   slider, the kit is the one with fixed periods.
3. **Q-21 — the editor toolbar.** **Answered by Marty: adaptive, one row or two, captions when
   there is room, customization removed.** `ADR-150`. It voids ADR-035's first decision bullet —
   `ToolbarLayoutJson`, group-level row placement, the per-button catalogue — and with it that
   entry's justification for `@angular/cdk`, whose only consumer in `src` is those drag lists. Group
   captions already ship (`.tb-caption`, ten of them); they become conditional rather than new.
4. **Q-22 — the theme toggle and the Appearance trigger.** **Defaulted by the implementer, not
   asked:** both go into the `RailHeader` dots menu, the slot `RailHeader.prompt.md` reserves for the
   rare rest. `ADR-151`. The Appearance panel is hoisted out of `editor.component` into the shell,
   which is also the first time it is reachable from any screen but the editor.
5. **Q-23 — responsive.** **Answered by Marty: commission the screens from Claude Design.**
   `ADR-147`. The shell is built to the kit at desktop proportions and the narrow behaviour is
   *deferred, not invented* — the shell stages add no width breakpoint of their own, and the app
   below that width keeps whatever it does today. The `≥1100px` fallback this plan proposed above is
   withdrawn: no threshold is designed here. `T-034` is blocked on the deliverable.
6. **Q-24 — does the Appearance accent picker survive?** **Defaulted by the implementer, not
   asked:** yes. `ADR-152`. The constraint it carries is that every accent-derived tone is derived
   from `--accent` and never held, and that a new one joins `ACCENT_DEPENDENT` in the same commit —
   otherwise the gate's five-preset re-run measures a colour the app does not paint.
