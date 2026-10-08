---
owner: marty
last_verified: 2026-10-08
source_of_truth_for: the only list of open tasks and questions (T-xxx, Q-xx)
guard: none
---

# Backlog — task board

The **only** home of open, not-yet-started tasks. Board rules:

- Canonical line: `- [ ] T-xxx Name — description #tags P1..P3` (P1 = High, P2 = Medium, P3 = Low).
- IDs are stable (`T-xxx` tasks, `Q-xx` open questions) and never reused. The original numbering (B*, N*, I*, NF*, FI*, idea N) stays in descriptions so old references in CHANGELOG/DECISIONS keep working.
- `#decision` means the maintainer's decision is needed first (see the questions table at the bottom).
- Done rows are deleted; what shipped is recorded in `docs/tasks/CHANGELOG.md`. The live-verification checklist of already-built things lives in `docs/tasks/TASKS.md`, not here.
- `docs/tasks/TASKS.md` deliberately does **not** follow this file's strict one-line-per-task shape — it's a short-horizon narrated status file by design (see `docs/DOCS-FLOW.md`), not a second copy of this board.

## Sprint v0.2.0 — open-beta polish (INPUT_PROMPT sweep 31.08)

The final polish sprint before open beta. Source: the 31.08 INPUT_PROMPT (10 annotated screenshots
+ 21 thoughts); the visual plan groups these with T-152/T-153/T-172/T-190/T-191/T-003/T-293 into
six milestones (SEP–DEC). Naming and pricing decisions stay with Marty throughout.

**Closed 01.09.2026 (v0.20.0)** — T-331, T-337, T-338, T-350, T-353, T-358, T-359 and T-361 are done
(CHANGELOG 01.09, ADR-234/235), and T-358 closed T-301, T-302 and T-304 on the way. What is left in
this section is one decision (T-356) and the rows below it that were never part of the sweep. **None
of it has been opened by a person** — the live-verification rows in `docs/tasks/TASKS.md` are the
real gate, and the ones this sprint added are listed there.

- [ ] T-356 Dialogue editor verdict — unfinished and unclear why a regular blogger needs it; decide its fate #product #decision P3

## Sprint v0.26 — the 0.25.1 review

Source: `Docs_CedarClerk/Reviews/0.25.1_Review.md` (owner's section/number in parentheses). Decisions taken 08.10.2026 are ADR-316…325. Order: wave 0 → 1–2 → 3–6 and 9 → 7–8.

**Wave 0 — bugs** — done 08.10.2026 (CHANGELOG); T-398 deferred.

**Wave 1 — sign-in and registration (ADR-325)** — done 08.10.2026 except T-405, which needs the owner's Discord credentials.
- [ ] T-405 Discord sign-in in production — code is wired; set `Cedar:Auth:Discord:*` in `data.conf` and register `https://<host>/signin-discord` in the Discord portal, then sign in for real (Login 2) #auth #operations P1

**Wave 2 — shell (ADR-322, ADR-324)** — done 08.10.2026.

**Wave 3 — theme and public pages (ADR-321)** — done 08.10.2026.

**Wave 4 — projects (ADR-318, ADR-319)** — done 08.10.2026.

**Wave 5 — publishing (ADR-316, ADR-317)**
- [ ] T-418 Publishing Manager per `UI_Prototypes/Publishing_Manager.png` — rename, two panes, filter chips, Overview/Publishing/Engagement/Details tabs, next-step hint, Telegram activity from `ChannelPost.PublishedAt` (Posts 1–2) #publishing #ui P1
- [ ] T-419 `/metrics` and `/forms` as pages — StatsComponent with its own header and CSV, Forms extracted from the manager, redirects from `/stats` and `?tab=` (Metrics 1, Forms 1) #publishing #ui P1
- [ ] T-420 Calendar shows published posts — directly published documents appear (from `ChannelPost`/`PublishJob`/`BlogPublishedAt`), scheduled ones styled distinctly from published (Calendar 1–2) #publishing #ui P2

**Wave 6 — assets**
- [ ] T-421 Asset viewer: grid left, preview + properties right; visual pass (Assets 1, 3) #assets #ui P2
- [ ] T-422 Asset origin is legible — replace "No project / Uploaded / On disk" with clear scope labels and a per-file "from" line; add the terms to `TERMINOLOGY.md` (Assets 5) #assets #ui P2
- [ ] T-423 Downloads page with OS detection — On disk's download button opens it; installers per OS (Assets 4) #desktop #ui P2

**Wave 7 — glossary (ADR-320)**
- [ ] T-424 Glossary entry model — `GlossaryEntry` + per-language rows with localized name, spellings, description; migration grouping by `SourceTermId`; renderers and editor lookup rewritten; one editor form (Glossary 1) #glossary #backend P2
- [ ] T-425 Glossary AI — auto-translate names and spellings into selected languages; auto-description from the term and/or image (provider image input, credit price in BUSINESS.md) (Glossary 2–3) #glossary #ai P2

**Wave 8 — landing builder (ADR-323)**
- [ ] T-426 Landing block model and migration — sections → typed blocks, per-item hide, language maps with added languages, current content migrated unchanged, feature list as data (Admin Panel 1–2) #growth #backend P2
- [ ] T-427 Admin › Landing block editor — add/order/hide sections and blocks, per-block properties, per-language text, live preview (Admin Panel 1–2) #growth #admin #ui P2

**Wave 9 — Writer (ADR-326; mockup `Docs_CedarClerk/Reviews/ChatGPT Image Oct 8, 2026, 08_53_49 AM.png`)**
- [ ] T-432 Writer toolbar: one fixed row + "More ▾" — undo/redo, block type, B/I/U, link, lists, table; the rest grouped in More; remove toolbar customization (Settings editor, presets, `ToolbarLayoutService`), keep `ToolbarLayoutJson` as a vestigial column #editor #ui P1
- [ ] T-433 Slash command menu — `/` in the body opens a filterable list of every block and inline command; "Insert with /" hint in the toolbar #editor P1
- [ ] T-434 Writer frame header — breadcrumb "Project / Documents / Title", "All changes saved" + last-edited date on the right; tabs left-aligned with an underline on the active one #editor #ui P2
- [ ] T-435 Writer Properties panel — header with collapse, Document | Selection switch that auto-jumps to Selection, Organize (Type, Folder, Series, Tags), Languages chips + hint, collapsible Publishing/Structure/Glossary/Advanced with summaries, protected-fields footer; slug/location/backlinks into Advanced; one panel, the shell rail does not repeat it (editor half of T-390) #editor #ui P1
- [ ] T-436 Writer polish — footer reads "N words · saved N min ago"; sidebar head gets the "Workspace" label; sidebar counts cap at "99+" #editor #ui P3

**Needs discussion**
- [ ] T-437 Queue slots and project scope — ADR-322 scopes the calendar's scheduled posts by project, but `QueueSlot` belongs to a destination, not a project, so slots still show account-wide. Settle with T-428 #publishing #decision P3
- [ ] T-428 Queue: keep, clarify or remove — weekly per-destination send slots that `FillQueueSlotsJob` fills nightly from evergreen drafts by category (Calendar 3) #publishing #decision P3
- [ ] T-429 Builds rethink — today a `Build` entity with tasks and "Make changelog"; decide what replaces it (Builds 1) #indiedev #decision P3
- [ ] T-430 Skill Points research — paid per-upgrade skill tree vs. the current tiers + credit wallet; fit with `docs/product/BUSINESS.md` (Ideas 1) #billing #decision P3

## New features

- [ ] T-385 Durable AI operation log — `core/ai-operations.service.ts` keeps the record in `localStorage` per account (ADR-301 clause 5), so it dies with the browser profile and never reaches a second device. Give it an `AiOperation` entity (task, scope, protected fields, per-object changes with their revision ids, model, credits, timings) plus a migration, and have every AI path that exists today write to it — `POST /api/drafts/{id}/ai-edit/…`, auto-translate, glossary translate-all, profile texts — instead of only the panel that started the run #ai P2
- [ ] T-392 Two backend tests measure wall-clock and go red under a loaded machine — `MediaAccessCacheTests.A_page_full_of_one_owners_images_asks_about_visibility_once` (expected 2 lookups, got 4) and `LinkCheckServiceTests.A_hanging_link_is_cut_off_by_the_cap_and_reports_unreachable`. Both took 36–43s in a run where they normally take about a second, both pass in isolation, and they alternate between runs. Seen on a branch that changes no C# at all, so the suite is measuring the machine rather than the code. Give the cache test a deterministic clock and the link test an injected timeout instead of a real one #tests P3
- [ ] T-390 Inspector duplication remainder — Documents selection uses the shared Properties panel (ADR-306). Reconcile the remaining Glossary and Posts local readouts with the shell; retain editor-specific mark/table controls. #ui #decision P2
- [ ] T-389 Multi-select on the list screens, so an AI run can have a real batch — every screen publishes exactly one object today (`WorkspaceContextState.selection` is `[]` everywhere, T-386), so the AI panel's «N selected objects» line is unreachable and the whole scope mechanism is only ever exercised on a single row. The drafts list and the Posts Manager are where a batch actually makes sense; ticking rows there fills `selection` with no further change to the rail #ui P2
- [ ] T-387 Localization grid — the table view the Framer review asks for: source and translation side by side, per-row status (untranslated / needs review / approved / stale), selectable rows and cells feeding the AI panel's scope, a glossary of pinned terms and do-not-translate names, approved text never overwritten automatically, and a selective "update only the stale ones" run. The glossary screen covers terms, not documents, so this is a new surface; check `docs/design/UI-INVENTORY.md` for where its controls belong before drawing any #localization #ui P2
- [ ] T-326 Telegram document block — Bot API 10.3's `InputRichBlockDocument`, deferred from Wave 2: no TipTap node carries a file attachment today, so there is nothing to map from. Scope together with an attachment node, not before #telegram #editor P3
- [ ] T-180 Post-publish edit sync to Telegram: the live check — PUB-03 from INPUT_PROMPT. Server and editor button shipped (ADR-275/278): `POST /api/posts/{id}/telegram-sync` edits the one message the last send left, `Draft.LastTelegramSentAt` is what "edited since" is measured against, Telegram's own verdict is the answer, and the editor's Telegram state row carries the Sync button (`PostsService.syncTelegram`). Single-message posts only — a thread answers 409. What remains is the live check on `@testingandfun` against a post older than 48 h: Bot API edit limits are documented, not observed #telegram #editor P2
- [ ] T-380 IndieDB copy target — research verdict (`docs/knowledge_base/RESEARCH-2026-09.md` §T-127): IndieDB has no write API, so it is the same class as Steam and itch — a copy target, never a connector. Render the article body as HTML for IndieDB's news editor (check its tag allowlist by hand first; `CedarToItchHtmlRenderer` is the sibling to start from), preview + clipboard card in the export rack. S #integrations P3
- [ ] T-383 Telegram first comment: auto-comment on the discussion-group forward — research verdict (`RESEARCH-2026-09.md` §T-170), IndieViral's "conditional first comment". Per-post text + rule (immediately / after N reactions / after T minutes) in the Telegram step of Publish; reply to the automatic forward in the discussion group; `ChannelPost.DiscussionMessageId` + `FirstCommentSentAt` (migration); a Quartz job for the timer. Reaction counting already ships (ADR-205). M #telegram P3
- [ ] T-384 Curated industry events seed — research verdict (`RESEARCH-2026-09.md` §T-171): no upstream feed exists (itch jams have no API), so ~30 hand-curated anchor events as JSON loaded into an `IndustryEvent` table, plus user-added events, shown in the calendar and refreshed by hand (~30 min/month). S #growth P3
- [ ] T-182 Living public GDD: block visibility + snapshots — GDD-04/05 from INPUT_PROMPT, the "key combination" nobody else has. Mark a block `public/patrons/private` → one document yields different public projections without spoilers; frozen snapshots ("GDD v0.3") at a stable URL while the working copy stays live. L/XL — visibility-model ADR first (overlaps Q-8/Q-15 and the semi-public mechanics) #phase13 #blog #differentiation P2
- [ ] T-183 AI converters: video/voice → draft — AI-04/05 from INPUT_PROMPT, the most investor-shaped item: gigabytes of process recordings → content. Transcribe video and voice notes into a post draft. **Only after T-152** (all AI operations on credits): transcription costs several times more than translation; without metering it is unbounded loss #ai #decision P2
- [ ] T-003 Provider sign-in remainder — Apple implementation; diagnose the reported production Google failure beyond its successful initial redirect; configure and verify Discord end to end. Google/Telegram use ADR-237, Discord uses ADR-306. Production log access from this machine is blocked by SSH host-key verification. #auth P1
- [ ] T-001 Cross-posting: remaining networks — ideas 1/15/16/17, ADR-021, the positioning axis. X and Bluesky live in production (T-089; a real X thread published 12.08, credits charged). Umbrella row for the unscoped remainder: Threads (T-091), Instagram, Mastodon; IndieDB is scoped separately in T-380, LinkedIn shipped as a post-now connector (ADR-299), Steam and itch are copy targets already (ADR-223) #integrations P2
- [ ] T-004 Reader accounts on the blog — idea 21. Optional registration: comment under a name, reserve a nick, a "verified" badge (meaning undefined). Tied to T-023 — one reader-identity model for both #blog #identity P2
- [ ] T-091 Threads connector + Tech Provider Verification — a lower-priority Phase 12 item. Free of charge but requires verification and review per scope; 250 posts per profile per day, tokens live 60 days #phase12 #integrations P3
- [ ] T-006 Polls in private posts — idea 20.3. Unblocked by NF5 (polls exist, blog-only) but not scoped #polls #private P3
- [ ] T-005 Payment form in a post (arbitrary amount) — NF6. A public unauthenticated payment is a separate flow with its own fraud surface #payments P3
- [ ] T-007 Founder/Lifetime plan via invite code — idea 5, ADR-022. Permanent Pro without a new billing scheme; code price undecided #billing P3
- [ ] T-008 Chunked upload for files >100MB — idea 23, ADR-058. Cloudflare cuts bodies >100MB; the current one-off workaround goes through SSH (described in Marty's out-of-repo note `large_file_uploader.md`). Build properly if anyone besides Marty needs it #import P3
- [ ] T-203 Empty-state audit across all screens — the remainder of T-160 (templates + example project shipped, ADR-133): walk every screen the IndieViral way and make each empty state say what belongs there and offer the first step; planner, board and assets already do #growth #ux P3
- [ ] T-162 Public Cedar Clerk roadmap — the showcase of Cedar Clerk itself as a project: dogfood + marketing + a feedback channel, like Codecks' public roadmap (930 cards, their main community tool). Nearly free after T-159 #growth #dogfood P2
- [ ] T-163 SEO comparison blog — the Anchorpoint method (their main acquisition channel — "Anchorpoint vs Perforce" articles): "Codecks vs HacknPlan vs Cedar Clerk", "HacknPlan alternatives", "how to write a devlog". EN, ~1 article per 2 weeks, on our own blog (dogfood). The niche is cheap — competitors write little and rarely. A process, not code #growth #marketing #process P2
- [ ] T-156 Full redesign "like a game engine" — Marty's idea 13.08: an interface loosely resembling a game engine — dock panels, a property inspector, viewport tabs. Hypothesis: an indie developer finds that layout more familiar than "a text editor". Not a restyle but a different screen model, so first a one-screen prototype and self-testing, not a wholesale rebuild #design #idea P3
- [ ] T-011 iPad app (App Store) — OP3. Full-screen mode, App Store publishing. Electron does not go there — the desktop (T-121) does not bring this closer #application P3

## Indie-gamedev module (Phase 13)

Scope and rationale: `docs/product/INDIEDEV.md`, ADR-101…107. Build order: `docs/tasks/CHANGELOG.md`, Phase 13 (MUST closed 11.08; the `indiedev_module` branch is merged into master and deleted, the module sits behind `Cedar:Modules:IndieDev`).

- [ ] T-139 Project type invisible in the projects list — a side effect of following the handoff exactly: the Name column on `/projects` carries no type icon, and the type has nowhere else to show — it is only visible on the project dashboard. Fixed with one icon in the name cell, but that changes the row's anatomy, so it is Marty's decision, not a drive-by #phase13 #ui P3
- [ ] T-121 Desktop: install on a clean machine — the shell, installer and self-update are done (ADR-104/116); cloud mode is done (ADR-117) and not as a second mode but as the only one: no local database, the desktop opens production, the local process is a filesystem agent. Exactly one thing remains: run the built installer on a clean machine — it builds and is verified on artifacts, but has never been executed #phase13 #application P2
- [ ] T-130 Brainstorm sessions — organising raw thoughts into structure #phase13 #might P3
- [ ] T-131 Game Script Writer + Game Plot Writer — screenwriter tools; the second is the short form of the first #phase13 #might P3
- [ ] T-132 Game Design Helpers — aids for designing mechanics and systems #phase13 #might P3
- [ ] T-133 Workflow planner — planning the development pipeline #phase13 #might P3
- [ ] T-134 Code documentation — comfortable code-docs viewing #phase13 #might P3
- [ ] T-135 Budget calculations — from the brief: "make calculations for a budget"; shape undefined #phase13 #might P3
- [ ] T-167 Playtest feedback → tasks — the cheap version of the Codecks loop (theirs is a Unity SDK, expensive): a public feedback form on the project showcase (T-159), answers land as tasks in the tracker. Forms and the tracker exist — the bridge between them does not. QA/playtests is one of the four role gaps on the competitor-analysis role map #phase13 #growth P3
- [ ] T-382 IGDB autofill in Create Project — research verdict (`docs/knowledge_base/RESEARCH-2026-09.md` §T-169): buildable and small — a server-side proxy `GET /api/igdb/search?q=`, pick a result → Name, Description, CoverUrl (linked, not copied), links as EntityLinks, Twitch keys in the drop-in, a "Data from IGDB" line; the control goes into the existing create-project dialog. **Blocked on one email to partner@igdb.com** confirming commercial use and cover storage. S–M #indiedev #decision P3
- [ ] T-378 Grid-view virtualisation for project assets — the list view is virtualised (cdk viewport, fixed row height); the grid view appends on scroll by decision, because a grid needs a fixed tile plus a `ResizeObserver` to know its columns. Do it if a real folder ever makes the grid slow #assets P3
- [ ] T-168 Composer: timecode comments on audio — all four competitors ignore the composer role (at most a task category or a waveform preview). Audio metadata already exists in assets (WAV: duration, sample rate); a player with timecode comments in a document makes Cedar Clerk the only tool with something for a composer. By demand of first users, not by plan #phase13 #assets P3

## UI V2 — the Cedar Bench port (branch `UI_V2`)

Rationale: ADR-136…176 (the port plan and Claude Design briefs were deleted; Cedar Bench is superseded by `docs/design/paper-first/`). Stages 0 through 6 shipped and `UI_V2`
is merged into `master` (`T-234`, `T-235` both done — the port's version bump/tag/deploy landed and
`styles/_forest.scss` is gone from the tree); status is in `docs/tasks/CHANGELOG.md`. The kit gaps
the ports found (`T-251`, `T-252`, `T-254`) closed with ADR-260, and the modal-button cleanup
(`T-262`, `T-263`) with it. The rows below are what is still open: the task-board pattern, the two
narrow-screen rows waiting on Claude Design, and — under its own heading — the features the screens
found the API does not have. Those are not re-skin remainders: each is a thing the kit draws and the
database cannot answer.

- [ ] T-229 Task board pattern — the materials half is done under ADR-165 (project-tasks wears the bench's primitives and keeps its own geometry) and the brief was written (deleted with the Cedar Bench docs; re-derive from ADR-165 if needed), eight forks covering column-as-panel, how a card moves between columns, tag density, link chips, modal vs inspector, empty and overfull states, the list view, and whether the ruler wants a third readout. What remains is the run and the arrangement it returns #screens #decision P3
- [ ] T-236 Commission narrow-screen designs — ADR-248 supplies a scoped safety rule at phone width: the shell temporarily uses its rail and keeps the document sheet visible without changing the stored desktop preference. The full Cedar Bench system below desktop width is still undesigned. The deleted brief (ADR-147) asked for the five chrome pieces, tool strip, coarse-pointer density and breakpoint set across 1180, 820 and 390px. Paste fresh token values from `styles.scss` into its marked block before the run. Blocks the remaining T-034 work #design #decision P2
- [ ] T-237 Port the narrow-screen designs — turn T-236's answers into ADRs for chrome collapse, touch density and the breakpoint set, then replace the scoped phone safety rule with the complete responsive system and re-run the device captures in `e2e/99-audit.spec.ts`. Unblocks the remaining T-034 work #shell #ui #mobile P2

### What the fidelity pass left open

The audit of 22.08 compared the port to the prototype pixel by pixel (ADR-174…176 record what it
changed). These rows are what it could not close: two are Marty's decisions, the rest are small and
named.

- [ ] T-273 Rule on the six ADR-bound fidelity divergences — the audit found six places where the app differs from the prototype because an ADR says so, and a fixer may not touch them: `--t2` by day darker than the kit's secondary ink (the pair stands at 3.0 under ADR-145/138); the sheet typeface default `system` where the kit is a serif (ADR-073 keeps the default — a fresh account still writes in Source Sans); the input border at 3:1 (ADR-074 `--border-strong`) against the kit's hairline; the ruler ink (the kit's `#4A340F` cannot share the `--brass-ink` name, so the rule keeps `--rail-edge`); the pegboard hole at a fractional px (ADR-138 item 7 forbids it); the cork tone's ink at night at 1.58:1. Each needs a word — keep, or a new ADR that reverses #design #decision P1
- [ ] T-274 Revisit the four remaining look-changing ADR adaptations against the prototype — ADR-249 settled the Editor as compact, equal Write / Preview / Publish navigation with an explicit save state. The open decisions are ADR-150's adaptive two-row toolbar against the kit's one 36px strip; ADR-138's paper floors making controls and the brand larger than drawn; ADR-160/168's four tiles plus a Documents panel and cover actions against the 3×2 tile wall; and ADR-148/149/161's metrics as a tab with a period picker against a screen with period segments. Each needs an ADR to reverse; say which, if any #design #decision P1
- [ ] T-268 Night gridlines in the growth chart, contrast unverified — was a hardcoded `#DAD1B6`/`#DAD1B8` pair; `growth-chart.component.ts` now reads `var(--rule-ink)` with distinct day/night values, but nobody has re-measured the night contrast since that change. Confirm it actually reads before closing #ui #a11y P3
- [ ] T-272 Drawer open reflows the hub — the journal reserves its height (ADR-153 clause 6, kept in ADR-174), so the shelves shrink above it and the Documents shelf can be left a 37px viewport; the prototype overlays the drawer instead. Reserve or overlay is the decision #shell #ui #decision P3

### What the ported screens found missing behind the kit

Every row here is a feature. They came out of the ports — Stage 4's three reference screens
(ADR-158…162) and Stage 5's remaining fourteen (ADR-163…168) — each of which drew what the data
could answer and left out what it could not, rather than drawing over a number nobody computes.

- [ ] T-248 Per-project metrics — `Channel` carries `OwnerId` and no `ProjectId`, so no view total can be attributed to a project and two of the kit's six hub plates are absent by decision (ADR-160). A project-to-channel link table is the schema half of it; the readout is the other #api #indiedev #stats P3
- [ ] T-250 Project milestone date — the kit's hub counts down to a dated milestone; `Project` has no such field, and `Sprint.endsAt` is a different promise. One field, or a decision that a sprint's end is the milestone #api #indiedev #decision P3

- [ ] T-259 Per-post reach beyond the blog — the Posts Manager's inspector can only state what `BlogStatSnapshot` holds; there is no per-post channel, reach or retention series to put beside it, so a post published to four networks reports one of them #api #stats P3
- [ ] T-379 Bluesky engagement counts skip our own thread replies — `SnapshotPublishTargetStatsJob` reads `getAuthorFeed` with `filter=posts_no_replies` (ADR-273), so the parts of a micro-thread after the first are not in the nightly sum. Switch to `posts_and_author_threads` if a thread's whole engagement is what the Stats tab should show — a one-word change and a decision about what the number means #stats P3
- [ ] T-261 `/drafts` tree and card rows still open from a `<div>` — the table view now gives the title a native address and keyboard path. Tree and card views retain the click on a non-control while also holding folder and action controls, so a wrapping anchor is invalid and a nested title link must not navigate twice. Settle their row anatomy before changing them #screens #a11y #decision P2

- [ ] T-276 Walk the blog by eye — the port is measured and four page types were rendered in both themes: index, post, series and gate. Author links were rendered and opened by browser automation; focus, press, Escape and outside-click behaviour is covered by tests, not a human pass. Still not rendered for review: the Showcase, a poll, a glossary tooltip, the image viewer and the watermark over the 3px sheet. Each is a seeded fixture away, and a measurement is not a look #design P2
- [ ] T-278 Nothing checks the readout face's own rule — ADR-180 clause 2 binds `--font-readout` to 11px (or an exact multiple), because a pixel font off its grid goes soft and reads as a rendering fault. The rule is stated and applied by hand across twelve call sites; `check-density.mjs` scores sizes by surface and knows nothing about faces, so the next declaration that pairs the token with 13px will ship. A per-face size assertion in that tool is the fix, and it is small — it already parses every rule it would need #tooling #design P2
- [ ] T-279 The blog's paper type dips under the 14px floor — meta lines, card stats, poll percentages and the language stamp sit at 11–13px on paper, where ADR-138 puts the floor at 14. `check-density.mjs` walks `src/` only, so the blog is unmeasured and this was carried in rather than decided. Either the lines move up, or the rule gains a stated exception for a reading page's marginalia — a decision, and it wants Marty's eye on a real post first #design #decision P2
- [ ] T-277 The DigitalOcean badge on brass — it ships as a white plate and the footer is now a brass rule, which is the brightest ground it has ever sat on; `opacity: .72` was tuned against a grey footer. Either a darker dim on the rule alone or the badge moves off it #ui P3

## Improvements

- [ ] T-152 Monetisation, the later steps — the core landed 31.08 (AI on credits, tier table approved and in `docs/product/BUSINESS.md` §3; the daily cap stays as the abuse guard): what the row still holds is annual −20% from launch (Stripe's fee on a $3 receipt is ~13%), a team tier $12–15/seat when teams exist (T-358), a referral program modelled on Codecks after the first users, and verifying `AiTranslateCost` against the actual Anthropic invoice #billing P2
- [ ] T-157 Second security key and an access-recovery plan — on 13.08 Marty moved key accounts to PassKey/Security Key with a YubiKey 5 — much better than before, and a new single point of failure: the key is one and lives at home. Need a second key registered in the same accounts and stored elsewhere, or printed recovery codes in another physical place. Verify a backup sign-in path exists for every critical account: DigitalOcean, Cloudflare, Stripe, GitHub, email #security P2
- [ ] T-034 Responsive layout: full pass — browser-emulated captures at 1180, 820 and 390px now keep Posts, Preferences and the Editor sheet usable; ADR-248's phone rail is a scoped safety rule, not the complete responsive design. Remaining: physical iPad 16 and iPhone 13 touch checks, Safari keyboard/viewport behaviour, both tablet orientations and the broader chrome/touch-density system from T-236/T-237. Automated screenshots are evidence of layout, not real-device or accessibility verification #phase11 #ui #mobile P1
- [ ] T-036 Posts Manager: response country/IP — FI6.2. Only a salted SHA-256 of the IP is stored — no raw IPs by design (ADR-016). Showing a country requires storing IP/geo at submit → see Q-10 #postsmanager P1
- [ ] T-318 Export rack: the three connector-less placeholder cards — Instagram, Threads and YouTube are drawn as destinations (ADR-028's roadmap mockups) with nothing behind them: selectable for management, nothing to manage, never includable. Was five: Steam and itch.io left the list in the Wave 1 session (ADR-223) — their cards are copy targets now, a rendered preview and a clipboard button, not connectors. For the remaining three: keep as roadmap honesty, gray them harder, or drop them — Marty's call; the API research in `docs/knowledge_base/RESEARCH-2026-09.md` is the precedent for what a card can honestly promise #editor #ui #decision P3
- [ ] T-202 Strip metadata from pre-ADR-130 media files — uploads are stripped since ADR-130, but files already in `media/` on the droplet keep their EXIF (GPS included) and serve on the public blog. A one-off pass with the same stripper over existing images; server-write, so Marty runs it. Cheap once scripted #media #security P2
- [ ] T-184 Skeleton loaders: the remaining surfaces — Task B from INPUT_PROMPT. `app-skeleton` shipped (ADR-286: text/card/avatar/table-row, CSS shimmer under the `--motion-*` tokens, `aria-busy` on the region, held for 300 ms) and admin uses it. Remaining: the Posts Manager and editor loading states — same component, no second name set #ui P3
- [ ] T-103 Fields whose only label is the placeholder: the editor's dialogs — a side finding of ADR-074. The placeholder deliberately stays at `--t3` (3:1): at `--t2` it reads as a filled value, so a field with no visible label gets a name per field, not a darker placeholder. Posts Manager, the form picker, glossary and Settings are done. Remaining: the editor's own dialog fields — footnote, formula, insert URL/caption, CTA, invite, watermark, tracked link, draft title #a11y #ui P3
- [ ] T-373 `--accent-ink` consumer sweep — the token is set (`styles.scss`, `appearance.service.ts` picks light or dark ink per custom accent, ADR-288) but nothing that paints sheet ink on an accent fill reads it yet (`.btn-accent` and its kin still take the sheet). Once every consumer reads it, the custom-accent floor can drop below 3:1, because the ink follows the fill #design P3
- [ ] T-107 Manual thread break in the editor — from ADR-083: part boundaries are chosen by the splitter (a heading if close, else size), and the author sees them but cannot move them. The honest control is an explicit "break here" node in the document: a TipTap node, a render rule, and a decision how it looks on the blog and in `.cedar`. A feature, not a detail of T-106 #telegram #editor P3
- [ ] T-046 Monochrome YouTube button — B8. The only coloured toolbar button; likely already fixed (`app-brand-icon` only renders `currentColor` now, no coloured override found) — a screenshot check away from closing #ui P3
- [ ] T-188 Wave-1 agents: docs-auditor + session-closer — external DOCS-FLOW analysis, "week 1 — 2 agents, no more". `docs-auditor` — a weekly read-only docs-vs-code checker (saves main context: the audit reads half the repo); `session-closer` — end of session: CHANGELOG/board cleanup/version+tag. Definitions in `.claude/agents/`, fleet docs in `docs/fleet/`. The analysis' warning stands: add one at a time, kill any not invoked for two weeks #process #agents P1
- [ ] T-189 Agent input-sweeper — Wave 1, third agent, after T-188. Trigger: `docs/INPUT_PROMPT.md` mtime newer than the latest "Input sweep" note in `docs/tasks/CHANGELOG.md` → triage against code → board + a CHANGELOG record. The procedure has been run by hand twice #process #agents P2
- [ ] T-196 Wave 2–3 agents + five skills — the analysis' "week 4 and later". Wave 2: verification-planner (T-187's other half — `docs/tech/QA.md` shipped 01.09, the agent that generates click-through lists for unverified features did not), release-guard (was `cedar deploy --preflight`; returns with `T-393`), adr-writer (rule + number collisions). Wave 3: product-strategist, design-keeper, growth. Skills: adr-format, doc-frontmatter, commit-version, session-brief, ru-en-ui-copy. Not before T-188/T-189 take root #process #agents P3
- [ ] T-199 Q-xx aging policy — the analysis' point: questions pile up with no deadline. 9 open (Q-2…Q-6, Q-8…Q-10, Q-15). The forcing mechanism is Marty's decision: a deadline per question, a monthly batch review, or a deliberate "let them pile" #process #decision P3
- [ ] T-321 FTS snippet highlighting — the blog `/search` results and the Ctrl+K overlay show title and a plain excerpt; FTS5's `snippet()`/`highlight()` would mark the matched terms in context. Deferred from the Wave 1 session (ADR-224) to keep the result row simple; pure read-path work, no schema change #blog #editor P3
- [ ] T-200 Media library v2: folders/tags, alt, dedup — deferred from ADR-127: asset folders/tags, alt texts, content-hash dedup on upload (the same file twice = two rows and double quota today), a usage table instead of the on-demand scan if delete gets slow at real volume #media #editor P3

## Multitenancy — what the review left open

Built and recorded in ADR-206…213 (session in `docs/tasks/CHANGELOG.md`, 25.08). The boundary itself
holds — 1170 backend tests green plus a live three-tenant matrix — but **nothing here has been deployed**.
Backend, migrations and the live matrix were verified locally.

Closed after that review: T-290 (the desktop-download files are skipped on a tenant subdomain —
the convenience route needed no guard, `UseBlogOnlyHost` had already dropped its endpoint),
T-292 (deleting an account forgets its host→owner entry immediately instead of leaving the
subdomain to answer with an empty blog until the entry aged out), T-285 (an unrecognised `Host:` no longer switches the boundary off — a
file is public, gated, or the owner's own, and which name the reader typed does not enter into it;
Telegram's anonymous fetcher gets a signed short-lived grant per file instead of every unclaimed
file being readable), T-288 (the host and media caches are separate registered types, so a flood of
invented subdomains cannot evict the asset answers a page needs), T-289 (a failed refresh restores
the expiry it extended), T-284 (un-publishing opened a private post's media — the gate now counts
private posts whether or not they are published), T-286 (cross-post links carrying the legacy host —
`MicroThreadPlan.BlogUrl` is gone, `-t:Rebuild` reports 0 warnings), and T-287/T-291, which described
the legacy blog host: that host no longer exists in the code at all. A blog is reached at
`{username}.{tenant domain}` and nowhere else, so `Cedar:BlogHost` and `Cedar:BlogOwner` are gone too.

- [ ] T-293 The tenant domain is live; what is left is the checklist around it — `cedarclerk.app` is registered, a wildcard answers (`*.cedarclerk.app` reaches Kestrel: an unregistered name gets the app's own 404, not Cloudflare's), TLS is valid and the apex serves the application. Verified 25.08 from outside. What remains is not infrastructure: the app host moved to the apex while `Cedar:MainHost` still defaulted to the old name (now fixed in code, so the `Cedar__MainHost` override in the drop-in is redundant), `blog.mooexe.dev` has no ingress rule and answers Cloudflare's 404 — decide whether it is retired for good or redirected to `martycow.cedarclerk.app`, and the dead `Cedar__BlogHost`/`Cedar__BlogOwner` keys can come out of `data.conf` #infra P2

## Reference board — what the canvas left open

Built and recorded in ADR-217…219 (session in `docs/tasks/CHANGELOG.md`, 27.08). It closes `T-129`
(references board) and `T-155` (whiteboard), which were one thing all along — both rows are deleted.
Thirteen of the nineteen review findings were fixed in the same pass, and the 05.09 sweep closed the
rest of the review rows (`T-303`, `T-306`…`T-314`, `T-316`) and gave the two screens their page
specs. What is left is two decisions and one race the race test found. **Nothing has been deployed**,
and the realtime path has only ever run on one machine with two browser contexts on it.

- [ ] T-377 Canvas: an in-flight patch to a surviving item is lost when a concurrent delete rolls the batch back — found by T-307's race test (`CanvasBoardTests.An_update_racing_a_delete_answers_with_the_survivors_and_no_error`) and pinned as current behaviour, not fixed: when a patch batch names an item another writer deleted between the SELECT and the UPDATE, `SaveAsync` rolls the whole batch back, answers the survivors unpatched and no error, and relies on the client to re-send from `itemsDeleted`. Either retry the survivors server-side or make the client do it; today the patch is simply gone #canvas P3
- [ ] T-305 Accepting an invitation twice answers 404, not 200 — ADR-217's contract asked for an idempotent accept, and the implementation clears the token on acceptance; clearing and replay-idempotency cannot both hold, and `ProjectMemberTests` pins the clearing. Decide which is wanted: a spent token that stays resolvable, or a contract line that says 404 #canvas P2
- [ ] T-315 Fit-to-content zooms to 400% on a board holding one small item — clamped there from 348%, so the clamp is doing the only work. The arithmetic is right and the result is uncomfortable; a sensible ceiling for fit (100%, or the item's natural size) is a decision rather than a bug fix #canvas #ui P3

## Bugs

- [ ] T-388 `17-density.spec.ts` "the shipped shell resolves the same two floors" fails in the full smoke run and passes alone — reproduced on `master` and on a feature branch with byte-identical results (1 failed, 18 skipped, 70 passed), so it is the spec, not a regression. Its paper half reads `main[data-surface="paper"] app-button button`, and on the hub with zero projects the only control in `main` is the empty state's plain `<button>`; the test waits for `app-index-tabs` and then measures something it never waited for. Point it at a control that is always there, or wait for the one it measures #tests P2
- [ ] T-375 `core/display-time.spec.ts` leaks locale under parallel vitest — seen once in the 05.09 sweep: an expectation of `11 Aug` got `11 авг`, so another worker's Russian locale bled into the formatter under test. Pin the locale inside the spec (or the formatter takes it as an argument) rather than re-run #tests P3

## Tech debt

- [ ] T-395 Project modules, steps 14a/14b (ADR-293) — the sidebar still hardcodes its sections and ignores the `modules` map (14a); Settings gets a toggle per module (14b). Data and write path (`PUT /api/projects/{id}/modules`) exist #indiedev #ui P2
- [ ] T-393 Cross-platform operations CLI in Rust + Ratatui — replaces the removed MooTool `cedar` (ADR-304). Must run on macOS (primary), Linux and Windows with no PowerShell dependency. Scope: run locally with the bot forced off (`LocalNoBot`, whitespace token, `--no-launch-profile`, `wwwroot` created), test (backend + frontend + smoke), build (web + portable server), deploy (master-only, clean tree, version tag, upload → stop → swap `app`/`app.prev` → start → health check, `LIVE`/`LIVE-PREV` tags), rollback, status, bounded logs, backup verify (`data/backups`, `cedar-*.db.gz`). Port `Scripts/e2e.ps1` into it or to bash #cli #infra P1
- [ ] T-376 SECURITY.md gaps G1–G16 into board rows — `docs/tech/SECURITY.md` §Gaps lists sixteen candidate rows with tags and priorities (rate limiting, the proxy chain, `Cookie.SecurePolicy`, security headers, the two state-changing GETs, upload sniffing, the upload ceiling, the PayPal webhook, the visitor-hash salt, reaction kinds, the member cap, tracked-link abuse, R2 encryption, a dependency-audit phase, EF log level, a root `SECURITY.md`). Triage them into rows here — the P1 ones (G1–G3) before registration opens — and delete this umbrella when the last one has its own ID #security P1
- [ ] T-371 `docs/design/DESIGN.md` cites `editor.component.css` line numbers that no longer exist — the "lines 458–537" pattern reference predates the split into `editor.component.css` + `editor-toolbar.css` / `editor-workspace.css` / `editor-dialogs.css` / `editor-publish.css` (ADR-263). Point it at the file and rule name, not a line range #docs #cleanup P3
- [ ] T-372 `shared/account-menu.component.css` draws the avatar at a literal 26px — `--avatar-size` is the token (`styles.scss`); one literal left behind by the peer-face pass #ui #cleanup P3
- [ ] T-374 `cedarclerk-web/proxy.conf.json` pins 8080, so `e2e.ps1 -ApiPort` cannot move the API — the dev server needs a `proxy.conf.js` that reads `E2E_API_PORT`, and `15-landing.spec.ts` / `99-audit.spec.ts` still hard-code `http://localhost:8080` while `helpers.ts` already derives its origins #tests P3
- [ ] T-172 Media into object storage — a registration blocker (`docs/product/MULTITENANCY.md` §1). Step 1 (quotas cut to disk-honest numbers) is done — ADR-129; media itself is still served from local disk (`Program.cs`/`AssetEndpoints.cs`), unstarted. What remains: media into Cloudflare R2 (`/media/*` becomes a redirect/proxy to a signed URL), the disk stops being the ceiling, and Meta cross-posting gets solved on the way — Meta networks require a publicly reachable HTTPS media URL and no direct upload is supported, which also conflicts with private posts, watermark and copy protection (was tracked separately as T-088, folded in here — see Q-15, overlaps Q-8). R2 is already live for backups (T-147, closed 18.08) #infra #billing P1
- [ ] T-150 Environments: Local / Test / Stage / Prod — currently two: the local machine and prod. Nowhere to test a deploy and a migration on production-like data — every release is verified on the live server. Full stage = a second droplet with its own DB (+$12/mo) and a second tunnel; the cheap variant — same server, a second systemd unit, its own `CEDAR_DATA_DIR` and a subdomain. Do after T-151: with Docker an environment becomes a file, not a procedure #infra P2
- [ ] T-151 Docker and Docker Compose — Marty's request 13.08. The server is built on a laptop and travels as a tar; the droplet environment is reproduced by hand (runtime, paths, systemd unit, drop-ins). Docker gives three things at once: a reproducible environment, a cheap stage (T-150), and the ability to hand a self-hosted build outward (`docs/product/MULTITENANCY.md` §4). Caution: SQLite and `media/` are volumes that must survive container recreation, and the deploy must stay as safe as it is now (ADR-113) #infra P2
- [ ] T-145 Code signing for the desktop build — with self-update (ADR-116) the missing signature stopped being only about SmartScreen on first install: `electron-updater` without a certificate skips signature checks and relies on the manifest's sha512 fetched over the same HTTPS. Trust in updates = trust in `cedarclerk.mooexe.dev`, so an origin/DNS takeover delivers an arbitrary exe. Acceptable with one own install; not before the first external user. This is money and accounts (an OV/EV certificate), not engineering. Heavier since ADR-117: the same domain now also grants the bridge disk access, so an origin takeover is not just an arbitrary exe but reading the chosen folder #infra #phase13 #security P2
- [ ] T-197 API contract: generate instead of hand-writing — an earlier analysis proposed an API reference doc; verified: no contract doc, no Swagger/OpenAPI in the project, and ARCHITECTURE §API style deliberately does NOT enumerate endpoints ("a list here rotted once already"). If a contract is needed, it is `AddEndpointsApiExplorer`+OpenAPI from code, not a second handwritten list; decide when an external API consumer appears #techdebt P3
- [ ] T-370 Upgrade audited Angular and TipTap runtime dependencies — `npm audit --omit=dev` reports 39 advisories: 6 high and 33 moderate. Upgrade past Angular GHSA-jhpw-976m-542j and GHSA-jj27-h5hq-8x99 and TipTap GHSA-cp6q-959q-f8rh without changing the editor's CedarJson contract, then rerun the full build and smoke gate #security #dependencies P1
- [ ] T-136 Split `Draft`'s publishing fields into their own table — the accepted price of ADR-102. With `DocumentType`, `Draft` has ~35 columns, and `BlogSlug`, `IsBlogPublished`, `WatermarkText`, `LastTelegram*`, `DisableCopy` mean nothing for a `design` document — they silently sit as nulls. Splitting into `Draft` + `DraftPublishing` fixes that at the cost of a join on every document read, so it is not done now. Revisit when document types visibly outnumber posts #techdebt #phase13 P3
- [ ] T-146 Vestigial `ApplicationUser.RemoteUserId` column — left over from ADR-108 after ADR-117: the `UpstreamAuth` code is fully deleted, the column deliberately untouched. Dropping a column in SQLite = rebuilding `AspNetUsers`, and Identity touches that table on EVERY authorized request (the `no such column` incident in `.claude/rules/ef-migrations.md`). Zero win against nonzero risk on a live database — remove only together with the next migration-chain collapse, when tables get rebuilt anyway #techdebt P3

- [ ] T-431 Blog flag emoji on Windows Chrome/Edge — the editor picker applies the flags polyfill (ADR-314) but the public blog does not, so a reader there sees letters #blog #emoji P3

## Deferred (a deliberate "not now")

| What | Why |
|---|---|
| Pro Plus signature tier | Three signature tiers before a user base is extra complexity |
| Emoji as a Header Slot | Unclear value |
| AI translation of comments | Waits for the AI credit metering system |
| T-398 Documents page layout defects (0.25.1 review, Documents 1) — folder menus without a background, overlapping state badges, buttons in a grid | Owner: not now (08.10.2026) |
| A general "redesign" | Cancelled by ADR-070: no direction, no UI inventory, no tests — all three became Phase 11 preconditions. For an unstructured restyle, "split into concrete pains" still applies |

## Open questions (need Marty)

| ID | Question |
|---|---|
| Q-2 | Editor tabs (30.07, item 7): 2–3 isolated working tabs with their own history/state. Claude's take in the 30.07 report — optimistic concurrency (T-018.3) first, otherwise tabs are new data-loss paths |
| Q-3 | Old FI6.2: collapse tiers into one paid plan? Contradicts ADR-012/013/014; deferred by Marty 27.07 |
| Q-4 | Public name of the shared Cedar Clerk bot |
| Q-5 | ~~Register the tenant-blog domain~~ — answered by doing it: `cedarclerk.app` is live with a working wildcard, and the legacy blog host was removed from the code entirely (25.08), so a blog is only ever `{username}.cedarclerk.app`. What is still open is narrow: is `blog.mooexe.dev` retired or redirected? See T-293. Original wording: Now urgent rather than theoretical — the subdomain work (ADR-206…213) ships that name as the default `Cedar:TenantHost` and hands it out in real URLs, and ADR-209 already answers the second half for the existing blog: the legacy owner keeps the legacy host. Blocks T-293 |
| Q-6 | "Progressive reveal" — what exactly is wanted (impossible for channels via `SendRichMessageDraft`) |
| Q-8 | Private-post watermark: fixed text or per-viewer (embedding the viewer's email = leak traceability)? |
| Q-9 | Old FI6 (account settings): sub-items 1/3/4/5 were lost when the old inbox was overwritten — needs re-specification |
| Q-10 | T-036 (response country/IP): raw IPs are deliberately not stored (hash only). Start storing IP/geo for form responses? The privacy policy must reflect it. Precedent (ADR-097): view country comes from `CF-IPCountry` into a daily aggregate — geography without storing IPs. Not a full answer for T-036 (it needs a country per response, not a sum), but it removes the "showing a country requires storing IPs" premise |
| Q-15 | Public media endpoint vs private posts (T-172). Meta networks require a publicly reachable HTTPS media URL; direct upload is unsupported. Conflicts with private posts, watermark and copy protection (ADR-063). Overlaps Q-8 |
| Q-16 | ~~Public version scheme~~ — answered 31.08: the sprint ships as **0.20.0** (reads as the wished-for "0.2.0", still sorts after 0.17.x everywhere versions compare); `Consts.CurrentVersion` bumped. Original wording: the sprint is named v0.2.0 while the code read 0.17.0 |
