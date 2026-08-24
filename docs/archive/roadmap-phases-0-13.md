---
owner: marty
last_verified: 2026-08-24
source_of_truth_for: closed record — phase-by-phase status through Phase 13, superseded by CHANGELOG
guard: none
---

# Roadmap (archived 24.08.2026)

Retired as a live doc: it had come to narrate the same shipped work as `docs/tasks/CHANGELOG.md`,
by phase instead of by date. Current status now lives in `docs/tasks/TASKS.md` §Notes (production
version, active branch) and the latest `docs/tasks/CHANGELOG.md` entry (what shipped last). This
file is kept as the phase-by-phase record through Phase 13 — read as history, not as a plan; nothing
below is updated going forward.

Live phase-by-phase execution log, folded in from the former `Plans/cedar-clerk-saas-plan.md` (v1.7, 15.07.2026) and `Plans/session-brief-v0.8.0-planning.md`, which are now archived under `Plans/OLD/`. Architectural/product decisions referenced below (why something was built a certain way) live in `docs/DECISIONS.md`, not here — this file tracked *status*, DECISIONS tracks *rationale*.

## Status summary (as of 24.08.2026)

**Telegram engagement and project-scoped assets (`T-282`, `T-283`, ADR-204…205, v0.14.2).** Two
reports that both turned out to be unbuilt features rather than defects. A channel's likes and
comments were the blog's numbers attributed to it (ADR-025's own stated gap); reactions now come from
`message_reaction_count` — which meant naming the update in `allowed_updates` and leaving the
library's event API for `StartReceiving` — comments from the linked discussion group, and views are
withdrawn for a Telegram source because the Bot API reports none. And an uploaded file now belongs to
a project or to nobody: filing a post moved the document and left its pictures behind, while
`/library` had no entry point at all while the module was on, which is why nothing showed the
glossary's images. Both halves ship with additive migrations only.

**Second annotated round (`T-281`, ADR-200…203, v0.14.1).** Nineteen screenshots and six product
notes, closed as one pass. The bulk of it was a single missing box: ADR-200's trim tier (24px) for
chips and for controls riding a chrome band, which is what every "too tall" note was pointing at.
Index tiles are now cut into the board they switch on every screen that has them; a panel's name
and counter share a baseline; the fullscreen toggle moved to the drawer lip (ADR-201); the blog
index carries its own language pick again, scoped to the index (ADR-202); a post can be filed into
a project from the Posts Manager (ADR-203); Telegram channel pictures are copied down and drawn in
the blog header and in Settings; and numeric dates read MM/DD/YYYY across the app. Three unreported
defects were found and fixed on the way — `app-input`'s dense mode had never been dense, a small
paper button kept its desktop box under a coarse pointer, and a dialog's actions could sit under
the drawer lip. `cedar test --smoke` is green end to end (1551), which it had not been: five smoke
tests were failing on stale expectations and were brought up to what ships.

**Annotated screen correction pass (`T-280`, ADR-194…199).** All 25 annotated screenshots and the
cross-screen notes were reconciled across navigation, projects, tasks, posts, registration,
settings, statistics, glossary, credits and Writer. The final Writer slice adds Document as the
Structure root, explicit document selection, compact grouped history controls, per-language
Glossary exclusions, a unified Git-like revision diff, independent AI progress modals, persistent
destination rows in Inspector and a two-column Export destination rack that keeps unsupported
networks visible. The authenticated visual pass still needs a signed-in local browser session; the
production build, component suite and backend suite are green.

**UI V2 — Cedar Bench becomes the one look; Stages 0 through 6 (19–20.08.2026, branch `UI_V2`, not
merged, no version bump).** The design system mirrored from Claude Design at `.design-sync/ds-v2/`
stops being a second palette and becomes the app's only one. The port was planned before it was
written — `docs/design/UI-V2-PLAN.md` settles ten questions (theming attribute, token namespace,
component layout, half-pixels, night, density, icons, fonts) and thirty-eight ADRs (136–173) landed
ahead of the code they govern. Every screen in the app is now drawn from the bench kit.

**The blog joins it (22.08.2026, ADR-177…179).** The last surface still on the old look is the
one a stranger sees first, and it is ported: the plaster wall as the ground, a park-sign rail for
a header, the carpenter's rule for a footer, and every post a paper sheet at the reading numbers.
Two mechanisms had to exist first. The bench materials now cross to the server as a second
generated list beside the contract (ADR-177), because a contract name may not hold the gradient a
material is; and the four faces are copied unhashed into `assets/fonts/` and declared in
`DesignTokens.FontFaces` (ADR-178), which closes the hole ADR-143 left open — the blog had been
asking for *Source Sans 3* and drawing `system-ui` ever since. What the blog does not take is the
tool's chrome: no hook rail, no shelves, no drawer, no tabs. A reader has no commands.

- **Stage 0 — the two checks the rest of the port is measured by** (`T-205`, `T-206`).
  `check-contrast.mjs` was rewritten: it composites alpha over the backdrop instead of discarding
  it, resolves a layered background to every colour it can paint, walks a gradient along its whole
  ramp and bisects to the exact crossing where an ink passes through the surface's own luminance,
  scores the two-layer focus ring of ADR-140 against every surface a control can sit on, and fails
  outright when a token on the server-rendered contract list resolves to a gradient — that list is
  now one file, `tools/contract-tokens.mjs`, read by both the checker and the token generator.
  `npm run check:contrast:census` is the second mode: it walks what the app actually paints —
  component stylesheets, CSS inside a `.ts` `styles:` array, and the CSS inside the C# raw strings
  of the blog, the landing page and the draft preview — and reports every ink-on-surface no pair in
  the table covers. `tools/check-density.mjs` enforces ADR-138 behind `npm run check:density`, and
  `cedar test` gained a Density phase whose place in the phase list is pinned by `PipelineTests`.
- **Stage 1 — the palette, and the end of the skin** (`T-207`…`T-210`). The bench values are
  written into the bare `:root` and `:root[data-theme="dark"]` blocks of `styles.scss`, which is
  the only place a token is both contrast-checked and emitted into `DesignTokens` for the blog and
  the landing page; contract names keep their spelling and stay flat colours, bench material names
  sit alongside (ADR-137). Night was re-derived against cream paper rather than copied — the design
  system's own night block puts body text at 1.01:1 on the page ground (ADR-141). The skin
  mechanism is gone: no `data-skin`, no `Skin` union, no `setSkin`/`applySkin`/`loadInitialSkin`,
  no `cedar-skin` key, no Appearance control and no strings behind it in either language;
  `data-theme` is the only styling axis left (ADR-136). `styles/_forest.scss` is neutralised rather
  than deleted — nothing writes the attribute its every rule is scoped under — and the deletion
  itself waits on Marty as `T-235`.
- **What measuring found that remembering had not.** The census answered a checker that had
  reported zero failures over three rounds by scoring the wrong pairs: 22 rules each hand-mixed a
  state tint out of an ink and whatever paper the component sat on, so one visual idea shipped as
  22 ratios, the worst at 3.63 against a 4.5 floor. Those became `--ok-soft`/`--warn-soft`/
  `--danger-soft` on `--asoft`'s formula, and the state inks were re-derived against the wash they
  are painted on rather than against bare paper (ADR-145) — which also caught `--t3`, barred from
  carrying content, as the only label of a button, and `--alt` used as ink on `--sheet` at 1.06:1.
  Then a blind spot behind the census itself: a colour bound with `[style.background]` never enters
  a stylesheet at all. Two components hashed an identity into a six-colour array declared in their
  own `.ts` — the editor's channel list with a white ink that fails on three of the six (worst
  2.26), the admin user list with a cream one that fails on all six, and the same fill shipped on
  the public blog's channel avatar at 2.92. The array became `--avatar-1…6` + `--avatar-ink`
  behind one hasher, with hue kept and luminance moved until white clears (ADR-146). Page roots and
  the server-rendered surfaces moved off the wall onto paper on the way.
- **Stage 2 — the primitives, and the page that proves them** (`T-211`…`T-215`). Ten components
  under `cedarclerk-web/src/app/bench/`, in subfolders mirroring the design system: `app-button`
  and `app-input` in `forms/`, five display primitives, `app-spec-row` on the worktop and the two
  brass pieces in `scenery/`. React was translated rather than copied — slots became `ng-content`,
  handlers `output()`, everything else `input()` signals — and the mirror's two escape hatches were
  dropped on the way: the `style` prop, which would have re-seeded the one-off values ADR-071
  exists to remove, and the `cb-` class names `Button.jsx` and `Input.jsx` inject at import, which
  became ordinary encapsulated styles. Every component spells its surface (ADR-138) except the two
  brass ones, which are hardware sitting *on* a surface rather than being one. `/dev/styleguide`
  was rewritten as the kit's only call site, and its spec asserts the rule the rewrite exists for:
  the page draws no control of its own, so one vocabulary is on display instead of two.
- **What Stage 2 cost in colour.** The primitives paint materials nothing had measured — leaf
  stock, the rail as a button face, a wood plaque, a resin chip, `--paper-bright` as a surface
  carrying a field's ink. The census found one real failure among them: the brass stamp put
  `--brass-lo` on a tint of itself at 3.87 by day and 3.42 at night. The tint became `--brass-soft`
  on the same formula as the other three washes (ADR-145 — a wash is a token, not a percentage
  written in a rule), and the ink was re-derived against it, day keeping its hue and night stepping
  down (`#8A6226`→`#7B5822`, `#7E5A20`→`#684A1A`), which leaves the brass ramp's order intact.
  `--paper-bright` joined the paper family rather than being special-cased, so it now carries the
  whole ink set and the five accent presets. Fourteen pairs were added; the census reports **no**
  bench-sourced combination it cannot account for.
- **Stage 3 — the shell** (`T-216`…`T-221`). Seven chrome components under `bench/chrome/` —
  `app-bench-shell` and the `app-rail-header`, `app-hook-rail`, `app-shelf-panel`, `app-index-tabs`,
  `app-bench-drawer` and `app-ruler-bar` it assembles — plus one parent route in `app.routes.ts`
  wrapping every authenticated child unchanged. `/login`, `/register`, `/terms` and `/privacy` sit
  outside it and keep their own theme toggle, so the debug console is absent from them by the shape
  of the route tree rather than by a URL list (ADR-139). `shared/page-header.component.*` and the
  editor topbar's nav row are deleted: navigation is the hook rail, and the crumb and the account
  are the rail header's. The editor's status bar dissolved (ADR-153) — word, character and sync
  readouts are published to the ruler through a new `RulerService`, while the invisibles and
  fullscreen toggles and the error-state retry-save moved to the tool strip, because a rule takes
  no controls; the debug console kept its rows and its service and lost its tab, its panel and its
  own animation to the drawer, `hostBarHeight` retiring with the bar it measured. The
  `@media (pointer: coarse)` rule is scoped to paper with a chrome counterpart beside it, so a touch
  pointer can no longer inflate the rail past `--bench-rail-h` (ADR-138); `npm run check:density`
  has gone from six failures to eight rules all measuring, the coarse-pointer one included.
- **What Stage 3 decided, and who decided it.** Four answers are Marty's: metrics stays a tab body
  inside the Posts Manager rather than becoming a route again (ADR-148, `Q-19`); the stats sources
  become the kit's legend-filter while the app's 7–180-day slider stays, one line per source and
  never their sum, because ADR-025's per-channel attribution makes a cross-source total
  double-counted by construction (ADR-149, `Q-20`); the tool strip is adaptive, one row or two by
  measurement, with group captions the first thing it gives up and toolbar customization deleted
  outright (ADR-150, `Q-21`); and the narrow screens are commissioned from Claude Design rather than
  invented here, since the kit states `1440x900` and no second geometry (ADR-147, `Q-23`). Four are
  the implementer's defaults taken under a general instruction to proceed, and are recorded as
  defaults rather than as Marty's answers: the theme toggle and the Appearance trigger go behind the
  rail's dots menu, which also hoists the Appearance panel out of `editor.component` into the shell
  (ADR-151, `Q-22`); the accent picker survives on ADR-141's five vetted pairs (ADR-152, `Q-24`);
  the console becomes a drawer and the status bar dissolves (ADR-153); and the shell's content
  region declares `data-surface="paper"` (ADR-154).
- **What Stage 3 cost in colour.** The shell paints chrome nothing had measured, and the census
  found one real defect: the rail's dots menu is a paper board pinned under a cream-inked rail, and
  it inherited that cream onto its own `--sheet` surface — a menu whose every label sat at 1.12:1,
  invisible. It takes `--text` now, and `rail-header.component.spec.ts` no longer asks that *every*
  colour on the rail be the cream but splits the rule in two: a rule that declares a paper surface
  must take paper ink, and exactly one rule may, so a second cannot appear quietly. The cork sheet
  made `--wood-hi` a text background for the first time — the wood block's own note allows only
  `--wood-ink` there — and that pair stood at 4.477 by day against a 4.5 floor; the ink took the
  smallest step that clears with its hue kept, `#3B2A18`→`#3A2918` (ADR-074), which also moves the
  contract copy in `DesignTokens.generated.cs`. Two translucent faces that carry a caption became
  tokens, `--hook-face` and `--tab-badge`, because a surface with text on it has to be nameable in
  the pair table and an inline `color-mix` is not; two rules hid a gradient in `background-image`
  with no `background-color`, so the census read the frame behind them instead of the tile they
  paint. Six pairs were added and the two accepted `--glass` exceptions retired with `page-header`,
  the surface that painted them. What the census still cannot reach is named rather than excluded:
  it resolves a translucent backdrop only through CSS nesting, and these components write flat
  selectors, so the hook's own fill and the tab badge read against the page ground there — both are
  measured properly by the pair table instead, at 9.29 and 6.28.
- **What rendering found that reading did not.** Two verifiers drove the shell in real Chromium and
  turned up four defects every static check passes over; all four are closed. The touch floor was
  being *selected* rather than inherited — `[data-surface="X"] button` matches at every depth, so in
  a nesting (a shelf sheet inside chrome, a ruler on paper) source order decided the floor, and three
  of seven measured cases stood at 30px where they owed 44. It is a custom property now, the one
  thing in CSS that resolves to the nearest declaring ancestor in either direction (ADR-156,
  superseding ADR-138 item 5); `check-density.mjs` gained a seventh rule, verified red on the defect
  and on both of the naive repairs, one of which leaves a thrice-nested control at no floor at all.
  Eleven stylesheets re-declared the focus ring ADR-140 says is never re-declared: `outline: none` on
  the settings fields, the editor title and the three pickers; an `--accent` ring on `/drafts` and an
  `--abord` one on four editor controls, measuring 2.19–2.76 against a 3.0 floor in both themes; and
  an Appearance slider that painted no indicator at all. The only `outline` declarations left in the
  front end are the global rule and the `.tiptap` exclusion, and a component `box-shadow` on a
  focusable control is now written `:not(:focus-visible)` so it cannot quietly delete the halo. The
  hook rail carried no unread signal — both badges that did died with `page-header` — so the Metrics
  hook gained a tally, a work ticket on the hook rather than a notification dot, taking the index
  tabs' own tokens and label rules (ADR-155). And the dots menu lied three ways: Escape dropped focus
  to `<body>`, a click that navigated left the panel standing, and the project tile was a `button`
  with `aria-haspopup` that opens no popup — it is an `<a routerLink>` now, so middle-click works.
- **One stale rationale, and it was hiding behind a green gate.** The contrast run xfailed the ring
  on `--wood-hi` with the printed reason "nothing focusable is placed on a bare frame". The drawer
  pull falsifies it: a transparent button filling the lip, whose background is `--shelf-frame`
  itself. The ramp is in `RING_SURFACES` now and the exception states what actually carries the ring
  there — the 4.33:1 boundary inside the band, walked end to end, which is the argument ADR-140
  already rests cork and the ruler on; ADR-140 and the styleguide's own caption now say the same
  thing, so the record no longer contradicts the code. The census found nothing the branch had not
  already: its 42 sub-floor combinations are byte-identical to the commit before these repairs, 33 of
  them in the neutralised forest partial (`T-235`) and the rest third-party brand tiles and dots that
  are not text. Three pairs were added for the tally, which the census structurally cannot reach —
  its resin is 90% opaque, so its backdrop is part of its colour, and an absolutely positioned ticket
  resolves to the page ground there rather than to the hook it hangs on; measured against the wall, a
  hook tile and a hovered hook tile, it holds 6.03–6.89.
- **Stage 4 — the three reference screens** (`T-222`…`T-225`). Stats, the hub and the writer, in
  that order of blast radius, and six more ADRs (157–162) ahead of them. **Stats**: four
  single-metric SVG sparklines became one multi-series `app-growth-chart`; the sources became the
  legend *and* the filter, multi-select, one line each and never a sum (ADR-149); the four fixed
  stat cards became one readout per drawn source; the audience grid moved into a 320px right shelf;
  and the metric control the kit does not have was added because one chart needs one, while the
  window became a function of the selection rather than a fixed period (ADR-161). The chart may not
  claim a number the curve does not draw: the slip, the crosshair dots, the live region and the
  emitted event all read out of one computed, `openTail` is a required input because a half-finished
  bucket plots as a collapse, and every line carries its own name beside its own dot because the
  six-colour palette fails the CVD separation check in both themes (ADR-158). **The hub**: the
  project head became a worktop, the projects list absorbed as the left shelf's switcher, four
  module tiles replaced the document-type cards, and the two plates the kit draws that the database
  cannot answer are absent rather than dashed (ADR-160). **The writer**: the topbar is gone, its
  save state and its one primary action published to the rail as data through `RailActionsService`
  rather than projected as a template; the horizontal meta strip became a 340px inspector that
  describes either the selection or the document and never both, each row declaring its own scope;
  the two user-ordered toolbar rows became one strip that fits itself by measurement, which voided
  `ToolbarLayoutJson` and took the four Settings → Toolbar controls, `toolbar-layout.service.ts` and
  `POST /api/auth/toolbar-layout` with it (ADR-150, ADR-159); and a 224px structure shelf walks the
  document with two-way selection sync that holds no selection of its own, so neither direction can
  loop (ADR-162).
- **What Stage 4 cost in colour.** The three screens paint combinations nothing had measured, and
  the census found the tool strip to be the first band in the app carrying captions and buttons at
  once. Fourteen pairs were added — the strip's hover wash over the rail, brass on the rail and on a
  recessed chip, a stamped chip against the wood, the six series and the brass event label on graph
  paper, and the two bar fills against their tracks. Four values were re-derived by day, hue kept and
  the smallest step that clears taken (ADR-074): `--series-3`, `--series-4` and `--series-6` failed
  3:1 where the chart's two rules cross and the ground is at its darkest, and `--brass-lo` failed
  4.5:1 for the same reason. Two call sites moved rather than their token: a group caption and the
  GIF button are words, and the soft cream measures 3.35:1 on the rail — a glyph's floor, not a
  label's — so both take `--rail-ink`, leaving the soft cream to the crumb separator and the resting
  tool glyph, which is what it was derived for. The retry button was the one real defect: rust ink
  laid straight on the rail is 1.13:1, so it became a stamped chip on the opaque `--danger-soft`
  wash, which is the system's own way of saying something is wrong on chrome.
- **Stage 5 — every remaining screen, by pattern** (`T-226`…`T-229`), and six more ADRs (163–168)
  ahead of them. **The hub-like three** — `/projects`, the planner and the builds screen — became a
  worktop, cards and a summary shelf; the project index deliberately survived the hub's switcher,
  because what the dock absorbed is the *switch* and not the list (ADR-168). **The stats-like four**
  — the media library, project assets, `/drafts` and `/admin` — became one board each: an index-tab
  strip, one panel, and a right column that is never empty, which is what turned two detail *modals*
  into inspector shelves and the drafts folder popover into a Folders shelf (ADR-164). **The
  writer-like two** — the Posts Manager and the glossary — became an index shelf, one sheet and an
  inspector whose scope is exclusive; the manager's hand-rolled two-path SVG became
  `app-growth-chart` with its metric buttons as a legend-filter, and `/comments` was investigated and
  deliberately *not* moved into the drawer: it has been a fragment under its post since N7, and the
  drawer is mounted once by the shell, so a Feedback tab there would follow you onto every screen
  and need chrome to reach into a page's selection (ADR-167). **The board** the kit does not draw
  took the bench's materials and kept its own geometry, its title and counts moving to the ruler, and
  the fourth reference screen is now a written brief rather than an assumption (ADR-165,
  `docs/design/bench-board-prompt.md`). **The four doors outside the shell** — login, register, terms
  and privacy — declare their own surface, because nothing above them does (ADR-166). Settings kept
  every one of its 17 `sec-*` ids and gained 22 bench buttons, and `/dev/styleguide` gained the half
  of the kit it had never shown.
- **What Stage 5 cost, and what it retired.** The census came back clean of the ports: fourteen
  screens added **no** new ink-on-surface combination, because they took primitives instead of
  page-local colour. It did surface one the palette had never covered — `--danger` on `--asoft`,
  which the poll editor's remove glyph paints — failing at 4.46 under the darkest accent preset, so
  `--danger` took the smallest hue-preserving step that clears (ADR-074) and the pair joined the
  table. Three defects were fixed rather than boarded: `app-input` and `app-button` declared their
  boolean inputs without `booleanAttribute`, so the bare-attribute form was a compile error at every
  call site; the `/stats` redirect was a *string*, which the router reads as a path, so an old
  metrics bookmark silently lost its `?tab=stats`; and two screens navigated to `/editor?id=`, a
  parameter the editor does not read, so they opened whichever draft was newest.
  `shared/count-badge.component.ts` is deleted — its last consumer took its tally as a tab badge, and
  the hide-at-zero and 99+ rules live in `indexTabBadgeLabel` alone (`T-231` closed). The palette
  change then failed `DesignTokenDriftTests`, which turned out to have been blind: it compared ten
  hand-listed tokens, so Stage 4's re-derivation of `--series-3`, `--series-4` and `--series-6`
  never reached the generated C# copy and the blog has been drawing the old chart colours since.
  The generator was re-run and the guard now walks every token it emits.
- **Stage 6 — the cleanup, and what measuring it found** (`T-230`…`T-233`). Four sweeps, and three
  of them found the remembered numbers were wrong. The dead-CSS sweep (ADR-170) deleted one selector
  fragment and four dictionary strings against a plan that had forecast forty-two files: `.btn-accent`
  and `.btn-ghost` are live in fourteen blocks across eight page stylesheets, not dead, and
  `.icon-btn` was gone already.
  What it turned up instead is on the board — eight modal buttons whose CSS went with the port while
  their markup did not, so four dialogs footer in platform grey (`T-262`). The count-badge retirement
  (`T-231`) needed no code: the component was already deleted, and its null/zero/`99+` rules are
  carried by `indexTabBadgeLabel`, which three mutations proved is what five screens actually read.
  The docs sweep (`T-232`) marked ADR-120 superseded and corrected the token skill, which had been
  telling every reviewer the wrong radii. The smoke suite (`T-233`, ADR-171) was the largest: sixteen
  of fifty-seven tests red, fourteen of them bound to class names the repaint moved rather than to
  any behaviour, and two red since ADR-135 rewrote the landing on `master` — a suite red for one
  reason hides every other reason it is red. It is rebound to roles and accessible names, and it
  found the one thing the port genuinely broke (`T-265`, the writer with no sheet at 390px) plus an
  order-coupling through the server that had been masked by a test too broken to leak.
- **What settling the branch found.** The closing census surfaced one combination the pair table had
  never covered — the brass bar marking the current tool, which reads 4.42 at night and 2.91 by day
  against the 3.0 a graphical object owes. It is now measured and accepted with its reason (ADR-172),
  on the strength of three facts that were measured rather than assumed: the raised sign tile is
  relief and not colour, scoring 1.00 against the very wall it hangs on, so it cannot be the cue the
  bar falls back on; the bold caption is the cue and already passes; and the bar is `aria-hidden`
  behind an `aria-current` that carries the state properly. Mutating the brass then exposed a
  weakness in the mechanism itself — `except` was a bare string consulted in both themes, so
  accepting a day shortfall bought silence at night, where the same pair passes by a wide margin.
  Exceptions are now scoped to the theme they were derived in, which tightens ADR-140's three as
  well. The second find was that `icon-usage.generated.ts` had shipped stale twice in this port with
  nothing to notice: the generator gained a `--check` mode rendering through the same code path as
  the write, and `cedar test` gained an Icon inventory phase (ADR-173).
- **What is open.** **The port is done and both remaining rows are Marty's**: `T-234`, the version
  bump to 0.13.0 with its tag, the merge into master and the deploy — deliberately not done here,
  since a version is his call and `cedar deploy` refuses anything but master — and `T-235`, deleting
  the 1 229-line forest partial, which is a `.claude/rules/destructive-operations.md` event and waits
  for the word. Nothing else in the port blocks either. What remains around it is work the port
  surfaced rather than work it owes: three gaps in the kit itself (`T-251`, `T-252`, `T-254` — no
  `autocomplete`/`maxlength`/hidden label on the field, no select, textarea or checkbox at all, no
  query merging on a task tag; the fourth, the button's missing anchor form, closed under ADR-169),
  twenty features the screens found the API cannot answer (`T-238`…`T-250`, `T-255`…`T-261`), the
  three rows ADR-170's sweep left in place of `T-230` (`T-262`, `T-263`, `T-264`), and `T-236`, the
  narrow-screen designs, which is a deliverable rather than a task and blocks `T-034`, `T-237` and
  `T-265`. **Nobody has looked at any of it**: the verification map's marks are reset for every
  ported screen, which is now most of the app, and that is the real gate in front of a deploy rather
  than anything a checker reports. Two measured defects are left open on purpose. The drawer lip and
  the shelf-panel header write `--rail-ink` on `--shelf-frame`, whose light stop is `#B68B60`, so a
  title reads 2.51:1 by day and no flat ink clears the whole ramp — which material those bands are
  is a design call, not a settle-time patch. And the current-tool bar sits at 2.91 by day against a
  3.0 floor, accepted under ADR-172 rather than closed by moving a palette at the end of a port.
- **Fidelity pass — 22.08.2026, one commit on `UI_V2`, ADR-174…176.** The port was measured against
  the prototype rather than against its own checks: 48 captures at `1440x900`, both themes, on an
  isolated stack, 399 raw findings merged to 294, of which 172 defects were applied by file zone and
  98 were left standing as ADR-bound. The ground is the wall in both themes and the shell owns the
  viewport (ADR-174); the doors' toggle is a paper face at the chrome box (ADR-175); a pickable leaf
  draws idle pale and active green (ADR-176). Gates green: 1002 backend, 442 frontend, contrast,
  density, icons. What it left is on the board as `T-266`…`T-274`, two of them Marty's decisions;
  `T-234` and `T-235` are unchanged, and nobody has yet opened the screens.

**v0.12.2 — every P1 closed in one session (18–19.08.2026, not yet deployed).** Marty's directive
was "do everything P1", with five decisions resolved on the way in: T-164 (PRGE) removed outright,
Q-17 closed (the name stays Cedar Clerk), Q-18 (NSFW) removed, T-172's quota figures confirmed,
T-147's R2 keys confirmed already live. What shipped:

- **Registration blockers**: quotas cut to disk-honest numbers (Free 100 MB / Pro 1 GB / Pro Plus
  3 GB, ADR-129); the off-box R2 backup verified live against the bucket and closed (`T-147`); the
  first backup restore actually performed (`T-149`) — integrity ok, 4 users / 21 drafts / 18
  published, blog renders from the restored copy, and the three pending 0.12.1 migrations applied
  cleanly over production data on the way. `T-172`'s remainder (media into R2) stays open.
- **T-175 — EXIF/GPS stripping** (ADR-130): lossless segment/chunk removal in Core on all three
  media write paths, plus `ExifProfile` nulled on both ImageSharp re-encode paths (the Telegram
  derivative was carrying GPS to Telegram). Old files on the droplet are `T-202`.
- **All five growth anchors**: `T-161` Discord webhook publishing (ADR-131 — fourth network, no
  bot, no OAuth, `allowed_mentions` off); `T-158` sprint → devlog draft assembler (ADR-132 — the
  "work → story" button on the planner card); `T-160` onboarding (ADR-133 — starter documents born
  with per-type skeletons, and "Cedar Quest", the example project, on demand from the empty state);
  `T-159` public project showcase (ADR-134 — `/games/{slug}` on the blog host: devlog feed under
  the index's exact visibility rule, opt-in roadmap, store links; migration `AddProjectShowcase`);
  `T-154` landing rework (ADR-135 — devlog-first copy, English default, and a waitlist
  (`WaitlistEntry` + `POST /api/waitlist`, honeypot, dedupe) as the primary CTA; migration
  `AddWaitlist`).

Seven ADRs (129–135), three migrations, 1010 backend tests green, frontend 18/18, contrast clean.
The landing and waitlist verified live against a local server on the restored production copy.
Follow-ups became `T-202`/`T-203`/`T-204`; the live-verify checklist grew five entries. Deploy is
Marty's call.

**v0.12.1 — six board rows in one session (18.08.2026, not yet deployed).** `T-186` (index cards
hardcoding "RU" — fixed), `T-174` (OG/Twitter/canonical/hreflang meta, ADR-124), `T-178` (post
series as an entity with `/series/{slug}` and prev/next, ADR-125), `T-193` (the metrics event
dictionary, `docs/product/METRICS.md`, ADR-126), `T-177` (media library v1: `/library` page,
scan-guarded delete, insert-from-library, paste/drop upload, ADR-127) and `T-181` both halves (the
document tree and `[[`-wiki-links with backlinks, ADR-128). Three migrations, 964 backend tests
green. Deferred follow-ups became `T-200`/`T-201`; the live-verify checklist in `TASKS.md` grew
seven entries. Deploy is Marty's call.

**v0.12.0 — the Forest Workshop skin and `cedar run` (18.08.2026).** The skin is a second styling axis (`data-skin="forest"`, ADR-120), orthogonal to light/dark, scoped to one partial with a toggle in the Appearance panel; `cedar run` (ADR-121) builds and serves the real `publish/` artifact locally with the bot forced off. **Deployed 18.08** — production answers 0.12.0. The same day Marty's competitor report was absorbed (see "Competitor analysis absorbed" below: `T-158…T-172`) and `docs/INPUT_PROMPT.md` appeared as a new in-repo inbox — its first sweep is the section right after it.

**v0.11.0 — the desktop stopped being a second Cedar Clerk (12.08.2026, ADR-117).** Marty's report was that using the same email on the desktop still meant a different account — which is exactly the price ADR-108 had written down and accepted ("one identity is not one data set"). The answer was not to build synchronisation but to remove the second copy: the shell now loads `cedarclerk.mooexe.dev`, there is one database, and the local process is stripped to a filesystem **agent** (walk a folder, stat a file, render a preview). ADR-105 and ADR-108 are superseded; the merge problem ADR-105 feared is not solved, it no longer exists. Asset bytes still never leave the machine — paths, metadata and small JPEG previews do, and every screen now says whose machine holds the file. `T-121` closes; `T-137` (no backup for the desktop database) closes as moot, there being no such database. Middle digit moved because how the app is used changed, not because fixes accumulated. Deployed as 0.11.1 on 13.08.2026, in the order the Phase 13 note demands — site first, desktop after.

**v0.10.0 — the first version carrying the indie-gamedev module.** The middle number moved because the product changed shape, not because a pile of fixes accumulated (CLAUDE.md's rule for that digit). `indiedev_module` merged into `dev` and then `master`, both fast-forward; tagged `0.10.0`. **Deployed** — production answers `0.10.5` (health-checked 11.08.2026), and it answers from a different machine: see the infrastructure row below.

**Infrastructure — production moved to DigitalOcean (11.08.2026).** The Raspberry Pi is no longer production. The droplet is `cedarclerk-periwinkle` (fra1, Ubuntu 24.04.4, x86_64, 1 vCPU / 2 GB, 45 GB free), reached over the same Cloudflare Tunnel, serving the same `cedarclerk.mooexe.dev` and `blog.mooexe.dev` from one Kestrel process on loopback. The move was a copy rather than a port because the server publishes as framework-dependent portable IL, so armhf → x86_64 changed nothing. Journal: `docs/archive/migration-to-digitalocean.md`; current state: `.claude/rules/production-environment.md`, rewritten from the running machine.

Two things came out of the move and are **not** closed. The nightly `sqlite3 .backup` did not travel — production now has only DigitalOcean's weekly whole-droplet backup, which means a loss window of up to a week, restores that take the whole machine with them, and a copy that lives in the same account as the original (`T-071`, raised to High). And `cedarclerk.service` is `disabled`, so a host-maintenance reboot leaves the site down until someone looks (`T-143`). It also retired `T-070` (Pi OS upgrade) outright.

**Deploy — rewritten the same day (ADR-113).** `scp -r` of 174 loose files, run *after* stopping the service, kept dying near the end and leaving production down with half a build; the rewrite ships one resumable tarball, verifies it, and stops the service only for two directory renames — measured downtime about a second, with `-Rollback` on the release before.

**Phase 13 — IndieDev Module — MUST list complete (11.08.2026).** All seven rows shipped: projects and document types, the desktop shell, the asset index, the task tracker, the planner, the project-scoped glossary and build/version records. What is left of the phase is MIGHT and one research row (`T-127`). Marty's brief turns the product towards indie game developers: a post becomes one document type among several, living inside a project. Seven decisions written first (ADR-101…107), scope in `docs/product/INDIEDEV.md`, desktop mechanics in `docs/tech/DESKTOP.md`, design brief in `docs/design/indiedev-design-prompt.md`. **This also closes `Q-1`**, open since 30.07.2026 — the product has one audience now instead of four. Work happens on the `indiedev_module` branch, which the brief explicitly allows deleting if the business model doesn't hold.

**Phase 10 — UI Verification Sweep — closed 31.07.2026.** The frontend has a 37-scenario Playwright smoke suite where it had nothing, the UI inventory covers the screens and the blog, and 7 defects were found of which 4 are fixed. The audit-before-redesign ordering is ADR-070. **Deployed: production is on v0.9.20** (health check 31.07) — 0.9.18 plus the two iPad fixes that followed it, both verified live by Marty.

**Phase 11 — Design System 2.0 — unblocked and started 31.07.2026.** Marty answered Q-11: **warm editorial as the base, with the dense-product school's density borrowed on the table-shaped screens — one palette, one type scale, two density modes** (ADR-071). The measured finding that shapes the phase: the current token set is *already* warm editorial (the 08.07 "Cabin" set), so this is systematization plus a density layer, not a repaint — which is why T-081 can migrate screen by screen with the smoke suite green after each. Q-12 was answered the same day (Phosphor, ADR-072). Phase 12 (Publishing Targets) stays blocked on Q-1/Q-13/Q-14/Q-15.

**Previous: Phase 9f** — the 28.07 `Input.md` rewrite (sweep v3) plus AI/translation robustness. The ADR-064 audit's fixes landed as ADR-065 and the T-063 row is closed. Phases 9c/9d/9e closed 27–28.07.2026. `docs/tasks/BACKLOG.md` was restructured into a task board (30.07) — item IDs there are now `T-xxx`/`Q-xx`.

### Phase 11 — Design System 2.0 — started 31.07.2026

- [x] **T-075 — the visual direction is decided and written down.** ADR-071 plus a new Principles section at the top of `docs/design/DESIGN.md`. Seven rules; the two that constrain everything else are "one palette, one type scale, product-wide" and "density is a surface mode, not a component choice".
- [x] **Q-12 answered — Phosphor** (ADR-072), delivered as inlined SVG behind one `app-icon` component rather than the icon font or the web-components package. T-079 is unblocked but not started.
- [x] **Tokens v2 exist** (`styles.scss`) — `--font-serif`, the size scale extended to `--fs-9…--fs-27` and made integer-only, semantic roles (`--fs-caption/meta/ui/body/title/read`, `--lh-read`), the `--dens-*` density set with a `[data-density="compact"]` override, `--icon-sm/md/lg`, and `--motion-fast/base/slow` + `--ease` with a global `prefers-reduced-motion` clamp. **Not one existing value changed** — v2 is additive, which is what let the smoke suite stay green without touching a single test.
- [x] **T-078 — `/dev/styleguide`** (authGuard'd). Every token and control state on one screen, with live theme and density toggles; captured into `.e2e-audit/70…73` by `AUDIT=1 npx playwright test 99-audit -g styleguide` in all four combinations. Its own CSS is held to principle 7 — every value a token — so the page is also the worked example.
- [x] **T-081, screen 1 of 12 — `/drafts` migrated.** Zero hardcoded font-sizes and zero colour literals left in `drafts.component.css`; the page opts into `data-density="compact"`. Three tokens were added because the screen needed them and every other screen will too: `--hover`/`--hover-strong`/`--hover-danger` (the app was carrying `rgba(128,120,100,.08/.1/.14)` — a colour belonging to neither theme) and `--icon-xs`. **T-034 closed on this screen at the same time** (ADR-070's rule that breakpoints ride with the migration): the toolbar wraps instead of growing past a phone's viewport, and the column set now has three tiers rather than one — ≤1280 drops Tags/Activity, ≤900 also drops Folder/Updated so the row actions stop falling off an iPad portrait, ≤560 keeps State alone at a phone-appropriate width. Verified by capture at 1180/820/390, not by eye.
- [x] **T-081, screen 2 of 12 — `/posts` migrated**, same treatment. Two pre-existing defects surfaced by the phone capture and fixed: every `.chat-input` on the page was 22px wider than its card (`width: 100%` with padding and no `box-sizing: border-box`), and long post titles clipped mid-word because `text-overflow` does not apply to a flex container. Card/badge paddings at 9/11/14/18px are left alone on purpose — the spacing scale has no such steps, and picking one is a decision for T-076's mockups rather than for a sweep.
- [x] **T-081, screen 3 of 12 — `/settings` migrated**, and the first screen to stay **comfortable**: it is a form, not a table, so the same `--dens-control-*` tokens resolve to 7px/14px instead of 5px/10px purely because the page root carries no `data-density`. That contrast — identical components, different surface — is principle 3 working, and it is now visible side by side with `/drafts`. Two colour literals kept deliberately (Telegram's `#2AABEE` and its `#fff` foreground: a brand mark must not shift hue between themes). Its narrow-screen pass was the first ever — it had one breakpoint, and 26px card padding inside a 20px body left under 300px usable on a phone; the sticky anchor chips also stop being sticky there, since on a short screen they cover what they scroll to.
- [x] **T-081, screens 4–6 — `admin`, `comments`, `stats`**, migrated in one pass (same shape, same edits). Two findings worth keeping: **`--warn` was added to the palette**, completing a status triad the app has had since the admin panel shipped while only two thirds of it was tokenized (the third was a hand-mixed hue plus a theme override eighty lines from the rule it corrected); and **`comments`/`stats` needed no density attribute** — they are Posts Manager tab bodies and inherit `compact` through the cascade, which is exactly why density is custom properties rather than a class each component must remember. `admin` had no breakpoint at all; its wide tables keep scrolling in place (right for a log) while the user cards got the narrow pass.
- [x] **T-077 — the type sweep is done: 314 → 0 hardcoded `font-size` declarations** in the Angular app. Colour literals are gone too, with two kept on purpose (`#fff` on a channel avatar and on the Telegram brand mark — those backgrounds are a generated colour and a brand colour, so a theme token would go dark behind them and become unreadable). Two more tokens fell out of the work rather than being invented for it: `--scrim` (the modal backdrop was one fixed value in both themes) and `--warn`, which turned out to cover three further editor cases (`#B08618` twice, `#C9A227` in the diff marker). **Shared components deliberately use fixed role tokens, not `--dens-*`** — otherwise the page header and account menu would change size depending on which page sits under them, and chrome has to be stable.
- [x] **T-081, screens 7–12** — `glossary`, `login`, `register`, the eight shared components, and the editor (67, the largest single file).
- [x] **Q-16 closed by ADR-073** — the serif rule covers the blog post body only; the editor sheet keeps its user setting. (This row said "open" for a day after the ADR was written.)
- [x] **T-079 — icons migrated to Phosphor** (ADR-072). 206 call sites, 80 icons, 15 TS files; `@lucide/angular` removed from `package.json`. Delivered as a generated TS constant (`tools/generate-icons.mjs` → `icon-data.generated.ts`, regular + bold) behind a single `app-icon` component, so size comes from `--icon-*` tokens and weight is a prop. Every entry in `tools/icon-map.json` was verified against the package's asset files before use. `brand-icon.component` is untouched — no general-purpose set carries brand marks — and the 10 non-set glyphs (`☾ ✦ ◷ ⤢ ¶ ⏰ 👍👎 ☰ ↑`) remain, still flagged on the styleguide: this replaced an icon set, not text characters doing an icon's job.
- [x] **T-080 — icon semantics + `/dev/icons`** (ADR-075). All 58 icon-only controls carry an `aria-label` beside the tooltip they already had; the inventory page is **generated from the call sites** (`npm run icons:generate`), not hand-kept, and shows what each icon means here plus every meaning drawn twice. Its first run reported six duplicate meanings and all six were false — a busy button swaps its icon for a spinner, and the analyser was reading the spinner as the button's meaning. Once taught that a spinner is a state, `arrow-clockwise` fell from 11 meanings to 3 and the duplicate list emptied: the app is consistent here. What is left overloaded is `x` (12 labels), `trash` (5), `plus` (5) — the universal actions. Drive-by: the shared modal's close button was the last hardcoded English string in the app's chrome, and the editor's AI menu still had three.
- [x] **T-082 — accessibility** (ADR-074). Measured first: **32 failing token pairs** across both themes. The finding that forced a decision rather than a fix — at 4.5:1 this palette has room for **two** muted text tiers, not three — so `--t3` stopped being a text colour (placeholder/disabled/decoration at 3:1) and its 91 informational uses moved to `--t2`, which means **meta text across the app is darker now**. `--t2/--accent/--danger/--ok/--warn` each moved one step towards black in light; `--border` stays a decorative hairline on purpose and the affordance got its own token, `--border-strong`. Plus the app's first global focus ring and 44px touch targets keyed on `pointer: coarse`, not viewport width. Enforced by `e2e/12-a11y.spec.ts`: **the smoke suite is 37 → 42.**
- [x] **T-051 — the long-word pass** (ADR-076). A dev-only pseudo-locale (`?pseudo=1`, or the toggle on `/dev/styleguide`) inflates every string ~30% and welds a German compound onto its longest word. Three defects on the first run, none of them visible in English or Russian: the page header could not shrink (every screen using it scrolled sideways; the editor by 770px), the `/drafts` column headers could not ellipsize because `text-overflow` does not apply to a flex container, and a toolbar caption could widen its group.
- [x] **T-092 — build budgets** (ADR-076). Not raised: **measured**. Every route became `loadComponent` with `PreloadAllModules`, which took the initial bundle from **1.87 MB to 531 kB** (410 kB → 127 kB transferred) by moving TipTap/ProseMirror/KaTeX out of what loads before `/drafts` can paint. Budgets were then set against that — 650 kB warning / 800 kB error, and 30/36 kB for component styles. `ng build` is warning-free for the first time.
- [ ] T-076 (mockups) — not started. **Spacing literals remain** (9/11/14/18px card and badge paddings) — the scale has no such steps and picking one is a mockup decision. The blog's server-rendered surfaces (its own `:root`, 21 hex in `BlogEndpoints.cs` + 6 in the renderer) are separate work from the Angular sweep and are still on the old system.

**Verified 31.07**: `ng build` clean (the two pre-existing budget warnings only, T-092), smoke suite **37/37**, and the token resolution checked programmatically in both themes rather than by eye — `--bg` reads `rgb(236,233,226)` light and `rgb(29,27,23)` dark.

**Verified 01.08 (after T-080/T-082/T-051/T-092)**: `ng build` **warning-free**, smoke suite **42/42**, `dotnet test` 442/442, contrast **0 failing pairs** in both themes. **v0.9.21 was deployed to production first** (health green, no `Applying migration` lines, no `warn:`/`fail:` in the startup log, bot running, `/` + blog + `/rss.xml` all 200) — everything in this block is committed but **not deployed**. One flaky smoke test was fixed on the way: the UI-language test reloaded the page while the profile POST was still in flight, so `/api/auth/me` answered with the old language and it read as a persistence bug.

### Phases 0–10 — closed, archived 18.08.2026

The full log of everything closed by 31.07.2026 moved whole to
`docs/archive/ROADMAP-phases-0-10.md` (371 lines, ~72 KB — over half of this file was history).
One line each; details, dates and the deliberate scope cuts are all in the archive:

- ✅ **Phase 0 — Infrastructure** — DONE 05.07.2026 (on the Pi; production moved to a DigitalOcean droplet 11.08 — see the Status summary)
- ✅ **Phase 1 — Server skeleton** — DONE 06.07.2026
- ✅ **Phase 2 — Bot host** — DONE 06.07.2026
- ✅ **Phase 3 — Web App MVP: editor** — DONE 06.07.2026
- ✅ **Phase 4 — Channels & quality-of-life** — effectively done 08.07.2026; the deferred mobile-responsive editor rides Phase 11's screen work (`T-034`), not this phase
- ✅ **Phase 5 — Blog platform** — DONE 09.07.2026 (RSS landed in Phase 8 Step 2)
- ✅ **Phase 6 — Multi-tenancy & public SaaS core** — code complete 11.07.2026; registration stays invite-only until the `docs/product/BUSINESS.md` §1 gates close (`T-172` chief among them)
- **Phase 7 — Entertainer role** — the one phase never started; its two bullets live in `docs/product/PRD.md` §Open (Phase 7). Polls shipped blog-only meanwhile (ADR-055)
- ✅ **Phase 8 — v0.8.0** — all steps done 26.07.2026
- ✅ **Phases 9 / 9b / 9c / 9d / 9e / 9f — the brainstorm and Input sweeps** — closed 26–30.07.2026 (the `T-xxx` board was born out of them)
- ✅ **Phase 10 — UI Verification Sweep** — closed 31.07.2026 (the Playwright smoke suite and the audit-before-redesign rule, ADR-070)
- *(the superseded 30.07 planning stub for Phase 11 is archived with them; live Phase 11 is above)*

### Phase 12 — Publishing Targets — started 01.08.2026

**Unblocked.** Marty answered Q-13 (**Bluesky first**) and Q-14 (**a cross-post is a standalone post with a manual per-target override**, empty override falling back to an automatic teaser) — ADR-077. Q-1 no longer blocks the phase: it was only ever needed to choose the first network, and that choice was made on other grounds. Q-15 (public media endpoint vs private posts) stays open but is **not a blocker for Bluesky** — the AT Protocol takes an uploaded blob directly, unlike the Meta networks that can only fetch a public HTTPS URL.

- [x] **T-083 — what a publish target is** (ADR-078). A (tenant, network, remote account) triple that owns credentials and can name what it created. Three obligations and no more: name its network, describe its limits **as data** (an editor cannot display a method call), and publish returning a receipt. Explicitly *not* its job: statistics, delete/edit, and its own connect flow — each left out with a reason, since all three would have been designed around Telegram. **The blog is deliberately not a publish target**: no credentials, no remote account, one destination per draft, and its publish is a local flag. What a target cannot represent is handled by the rule already set by ADR-019 — render a valid degraded form or refuse readably, never send something silently wrong.
- [x] **T-084 — the abstraction exists in code.** `PublishCapabilities` + `PublishNetworks` in Core (pure, so the editor's future warnings are unit-testable); `IPublishTarget`/`PublishRequest`/`PublishOutcome` in `Server/Publishing/`, with `PublishOutcome` deliberately the same shape as the `PublishResult` it replaces in T-085 so that refactor stays a move; `PublishTarget` entity + migration `AddPublishTarget` (unique on owner+network+remote id — connecting the same account twice is an update, not a second credential blob); and `PublishTargetSecrets`, which encrypts per-tenant credentials with the DataProtection key ring that T-074 already moved under `CEDAR_DATA_DIR` and into the backup. **The pairing this creates is written down**: `cedar.db` restored beside a lost key ring leaves credentials unreadable, so `TryUnprotect` returns null and the owner is asked to reconnect rather than a Quartz job dying. 12 tests; `dotnet test` **454/454**.
- [x] **T-085 — Telegram moved onto the abstraction, before any Bluesky code exists.** `TelegramPublishTarget` now holds everything Telegram knew: the bot check, media compression to the URL-fetch cap, the Blocks renderer, the whole `RichBlock`→wire mapping (moved out of `PostEndpoints`, since "the one place that knows about Telegram.Bot" is this target, not an endpoint file), the send with its two catch blocks, and the Telegram-shaped bookkeeping (`Draft.LastTelegram*`, `ChannelPost`). What is left in `PostEndpoints.PublishAsync` is network-agnostic: resolve the draft and language, resolve the target, pick the implementation by network, record success or failure on the target row. `Channel` is **projected** into `PublishTarget` (`TelegramTargetProjection`) on connect, deactivated on disconnect, and backfilled at startup — idempotent C# with 7 tests rather than a one-shot SQL data migration, because a data migration that runs on the production database deserves to be runnable twice and provable in a test. **No behaviour change**: same order, same error strings, same status codes, same rows. `dotnet test` **461/461**, smoke **42/42**. Verified automatically only for the refusal path the suite covers (publishing to a channel the account does not own, still 403 with the same wording) — the successful send needs a real post to `@testingandfun`, which no automated test can do without a bot token.
- [ ] ~~T-085~~ superseded above. This is the next row and the order is the whole point (ADR-070): `PostEndpoints.PublishAsync` writes `Draft.LastTelegramChatId/MessageId/Username` and a `ChannelPost` row, so an abstraction extracted after a second connector would have been shaped around those. `Channel` is *projected* into `PublishTarget`, not replaced — `ChannelPost`, `ChannelStatSnapshot` and `BotKnownChat` all key off it. No behaviour changes, which is exactly what the Phase 10 smoke suite is there to prove.
- [x] **T-086 — the capability matrix** (ADR-079). `PublishCapabilities` as data, `PublishValidator` in Core (9 tests) answering what a network will do to a given document, split into blocking and not. Shown in the export window before the send.
- [x] **T-087 — per-target text** (ADR-079). `DraftTargetText` per (draft, network, language); an empty text deletes the row rather than storing a blank that would read as an intentional empty post.
- [x] **T-089 — Bluesky** (ADR-079). Handle plus app password, encrypted; a session per publish rather than a stored refresh token, because a rotated token that fails to save locks the account out while an app password is revocable from Bluesky's own settings. Keyed by DID, not handle. `BlueskyPostBuilder` (10 tests) builds the fallback teaser and the link facet — **UTF-8 byte offsets, not character indices**, which on a Cyrillic post is the difference between a link and a link over the wrong words.
- [x] **T-090 — the publish queue** (ADR-081), pulled forward by the 502 in ADR-080. Durable `PublishJob` rows, one per destination, kicked in-process and swept every 15s. Retries only what could not have posted; a job left running by a restart becomes `Unknown` and is never resent, because a blind retry is how one post becomes two. 6 tests.
- [x] **T-113 — connections live in Settings → Integrations** (ADR-095, 07.08.2026). Marty's read of the shipped state: the Integrations panel existed but knew only the Telegram account, the bot and a channel count, while all three real connect flows had grown inside the export window. Telegram channels (connect from the discovered-chat list, `@name`/id by hand, disconnect), Bluesky (handle + app password) and X (OAuth) now connect there and nowhere else; the export window links to it instead of holding a form. X's callback returns to `/settings?tab=account&x=connected` — it used to land on `/` with a parameter no screen read, so a finished connect looked like nothing had happened. The N4 rule it retires ("connect where you publish") was written when there was one network and connecting meant pasting a chat id.
- [x] **T-114 — the export window is three questions** (ADR-096, 07.08.2026). Version → destinations → per-destination settings, replacing a column layout in which the language lived inside Telegram and Bluesky/X each had their own Publish button beside a window-wide one. One Publish for all four networks (the checklist already had a row per network per language). For X and Bluesky the announcement-plus-link and whole-post-as-a-thread choice became a **two-way mode toggle** showing only the fields that mode uses, and the short-post override text became per-language — one field was writing the same text into every ticked version. Pre-flight update confirmation extended to every destination (`/api/posts/update-preview` learned the network kinds it was already keying revisions by), deduped to one row per language. `dotnet test` 598/598, frontend 11/11, smoke **52/52** (+1: a stubbed connected network proves the mode toggle swaps the fields).
- [x] **T-116 — a Telegram channel per version** (ADR-098, 09.08.2026). Marty created an English copy of the main channel and the window could not use it: it ticked several versions but held one chat id, so a translation needed a second trip through the whole window. The Telegram panel now has a row per ticked version; Publish is disabled while any ticked version has no channel. Two consequences worth the row: the post link is built from the channel that produced it, and ADR-065's overwrite confirmation asks about each version's own channel instead of asking the EN question about the RU channel. The mapping is remembered in `localStorage`, not the database — it is a habit of the author's, not a property of the draft.
- [x] **T-117 — scheduling is a step of the window, not a field of Telegram** (ADR-099, 09.08.2026). `ScheduledPost` gains `TargetId` + `Network` (migration `AddScheduledPostTarget`, legacy rows default to `telegram` and keep publishing through their chat id); `PublishDueScheduledPostsJob` routes through `PostEndpoints.PublishToTargetAsync`, so X and Bluesky can be scheduled. Deliberately **not** through the `PublishJob` queue: nothing here waits on an HTTP request, and a direct call keeps `Sent`/`Failed` the network's real answer. Threads are not schedulable — the toggles that promised one are now disabled with a note, where before they were ticked and silently dropped. The blog is not a publish target, so it publishes immediately and the step says so.
- [x] **T-118 — X and Bluesky choose their own versions** (ADR-100, 09.08.2026). Ticking two versions meant two posts from one account and two X credits. Each short-post panel carries its own language pills, a subset of the window's and all of them by default; `xCreditCost()` follows the network's set rather than the window's, which is the same case in which it used to over-count.
- [x] **T-119 — RSS button in the blog header** (09.08.2026). `/rss.xml` has existed since ADR-024 and was reachable only through the `<link rel="alternate">` in `<head>` — i.e. by a reader who already knew to look. Styled secondary beside "Open in Telegram".
- [ ] T-088/Q-15 (public media endpoint vs private posts — still not a blocker for Bluesky, which uploads blobs), T-091 (Threads).

**Original scoping note, kept because the cost profile still decides the order after Bluesky**: which network comes first follows from who the product is for, and the cost profiles differ by an order of magnitude — X charges per post (~$0.015, ~$0.20 with a link, and Cedar Clerk almost always posts a link) with the app owner paying under a shared-app model; Instagram needs a Business/Creator account, a linked Facebook Page and a 2–4 week app review; Threads is free of charge but needs Tech Provider Verification and per-scope review; Bluesky and Mastodon need neither and are the natural proving ground for the abstraction.

Scope: an ADR for the publishing-target abstraction → `IPublishTarget` + a `PublishTarget` entity with encrypted per-tenant credentials → **refactor the Telegram export onto the abstraction with no behaviour change, before any second network exists** → capability matrix (character limits, media, formatting) with editor-side validation → degradation rules with per-target override and preview (Q-14) → resolve the public-media-endpoint conflict with private posts, watermark and copy protection (Q-15, overlaps Q-8) → Bluesky connector → `PublishJob` with retry, partial-failure handling and idempotency → Threads connector.

API integration is roughly 20% of this phase; the rest is the degradation model, the capability matrix, the media endpoint and partial failures. Rows: `T-083…T-091` in `docs/tasks/BACKLOG.md`; the older umbrella row `T-001` is superseded by them.

### Phase 13 — IndieDev Module — started 10.08.2026

Branch `indiedev_module` (from `dev`). Source of the turn: Marty's out-of-repo brief `Gamedev_Focused_Rework.md`. Full scope: `docs/product/INDIEDEV.md`.

**Done in the scoping session (10.08.2026) — documents only, deliberately no code:**
- [x] **Research of the shipped state**, against the code rather than the docs. The reusable base is larger than expected: `IPublishTarget` already generalises publishing, `Folder`+`Draft.FolderId` already model unowned grouping, `IsTemplate` is already the precedent for "a document kind as a column", and `CEDAR_DATA_DIR` already makes a local server a configuration change rather than a port. What is genuinely new is five entities, two columns and an Electron shell.
- [x] **ADR-101…107 written before any code**, per the CLAUDE.md rule: module-not-fork; document type as a column on `Draft`; "a project always has a document" as a UI rule rather than a constraint; Electron over the existing server as a sidecar; no cloud sync in v1; a task as its own entity; the asset index storing paths, not bytes.
- [x] **`docs/product/INDIEDEV.md`, `docs/tech/DESKTOP.md`, `docs/design/indiedev-design-prompt.md`** created; `PRODUCT.md`, `PRD.md`, `ARCHITECTURE.md`, `BACKLOG.md`, `DOCS-FLOW.md`, `CLAUDE.md` updated.
- [x] **`Q-1` closed** — the audience is indie game developers.
- [x] **A finding that changes one line of server code**: the listening address is a literal in `app.Run(Consts.URLs.Localhost)`, so `ASPNETCORE_URLS` cannot move it and the desktop shell cannot pick a free port until it becomes configurable. Recorded in ADR-104 and `docs/tech/DESKTOP.md`; it is the only server change the desktop needs.

**T-120 backend done 10.08.2026 — `Project`, document types, the module skeleton.** `DocumentTypes` in Core (six types, `IsKnown`, `IsPublishable`); `Project` in its own `Entities.IndieDev.cs`; `Draft.DocumentType`/`ProjectId`; migration `AddProjectsAndDocumentTypes`; `Modules/IndieDev/ProjectEndpoints.cs` behind `Cedar:Modules:IndieDev`, reported to the client through `/api/me`. `dotnet test` **633/633**, smoke **53/53** on a scratch database. Frontend not started.

Three things the implementation changed or added, each recorded in `docs/DECISIONS.md`:
- **EF generated a migration that would have broken production.** It does not read a property initialiser, so `DocumentType` got `defaultValue: ""` — and an empty type passes neither `IsKnown` nor `IsPublishable`, which would have made **every already-written post refuse to publish on the first deploy.** Fixed on the model with `HasDefaultValue`, not by hand-editing the migration, so regenerating it cannot lose the default again.
- **ADR-103 narrowed**: the create dialog picks the starting *document* type, not a project type. The project-type taxonomy was invented in the ADR and asked for by nobody.
- **Working material does not publish.** `design`/`script`/`plot`/`note` are refused on the networks' shared path and, separately, on the blog — which is deliberately not a publish target and so inherits nothing. `post` and `changelog` publish. Nothing changes for existing content, which is all `post`.

**T-120 frontend done 10.08.2026, from Marty's Claude Design package** (`docs/design_handoff_indiedev_core_loop/` — prototype, 26 screenshots, a README carrying exact token values). Built: `/projects` (compact list, create-project dialog), `/projects/:id` (comfortable dashboard, new-document and project-settings dialogs), "Projects" first in the shared topbar with the crumb walking to the project name, eight new Phosphor icons, `projects` blocks in both dictionaries. `dotnet test` **639/639**, frontend 11/11, `ng build` warning-free at 587 kB initial (650 kB budget), contrast 0 failing pairs, smoke **53/53** — and the screens were captured live in both themes rather than trusted from the code.

**The package overturned one of this phase's own decisions**: ADR-103 had removed the project-type taxonomy as invented, and the design brings it back with four real types (Full game / Game jam entry / Prototype / Released game), each naming its starter document. It is a product decision now rather than a guess, so `ProjectTypes` exists in Core and `Project.ProjectType` in the schema (migration `AddProjectType`). Five deliberate deviations from the handoff, each because the backing feature or the designed state does not exist yet, are listed in `docs/product/INDIEDEV.md`.

**MUST, in build order** — rows `T-120…T-126` in `docs/tasks/BACKLOG.md`:
1. ~~`Project` + `Draft.DocumentType` + `Draft.ProjectId`~~ — **done 10.08.2026**, backend and screens
2. ~~Desktop shell~~ — **local mode done 10.08.2026** (T-121). `CedarClerk.Desktop/` is an Electron main + preload + builder config and nothing else; the server and the SPA are the ordinary ones. Verified by running it, not by reading it: a free port taken from the OS, `/api/health` answering `env: Desktop`, the SPA served, **the bot logging "disabled"** (the check that matters — a desktop bot would knock the Pi's off its token), `%APPDATA%\CedarClerk` created with `cedar.db`/`media`/`dataprotection-keys`, and closing the window leaving **no orphaned server** — an orphan holds the WAL lock and the next launch would fail to open the database. One server line changed to get here: the listening address was a literal that overrode `ASPNETCORE_URLS`, so no port but 8080 was reachable. **Self-update added 11.08.2026 (ADR-116)**: `.\Scripts\deploy.ps1 -Desktop` builds the installer and publishes it into `data/downloads/` — staged, checksummed, moved into place, with `latest.yml` written last — and `electron-updater` in the shell reads it from `https://cedarclerk.mooexe.dev/downloads`. Both steps run after the health check, so a failed installer build cannot cost a deploy that already worked. Verified on the artifacts and the routes locally; the first `-Desktop` deploy is what proves the Cloudflare side.

   **Rebuilt as a cloud client 12.08.2026 (ADR-117), and that closes `T-121`.** The sidecar was a whole second Cedar Clerk — its own SQLite, its own accounts, its own copy of every draft — which is what made "the same email is a different account" true. It is now an **agent**: the same exe with `Cedar:Agent:Enabled`, which builds no database, no Identity, no bot and no SPA, and answers only `/agent/*`. The window loads production. Two locks replace the Identity cookie that used to close the sidecar: a per-launch bearer token (an unauthenticated loopback folder-lister is readable by every process on the machine, and by any browser that can reach 127.0.0.1) and granted roots, persisted by the shell and never taken from the server. The walk endpoints and `Cedar:AssetIndex:Enabled` are **deleted**, not switched off — production can no longer enumerate its own filesystem in any configuration, which is a stronger statement than the flag made. `Cedar:Desktop` went too: the landing is avoided by opening `/projects` instead of `/`, so a presentation flag no longer sits next to security ones. The cost stated rather than hidden: a remote origin now gets a bridge to the disk, so an XSS on the domain becomes a read of the chosen folder — narrowed by an origin gate in preload *and* in main, blocked navigation, and no `shell.openPath` at all (Marty's choice: reading is what the feature needs, executing is what an attacker needs). **Still open**: an installer that has actually been installed on a clean machine, and code signing (`T-145`) — which ADR-117 makes weightier, since the same domain now also hands out disk access.

   **Deploy order is not interchangeable**: ship the site first (plain `deploy.ps1`, so production has the import endpoints), then `deploy.ps1 -Desktop`. The reverse gives a copy pushing batches at a server that knows nothing about them.

   **Build/test/deploy scripts landed with it** (T-138, now closed): `Scripts/test.ps1`, `Scripts/build.ps1` and a git guard on `Scripts/deploy.ps1` — master only, clean tree only, with a version-tag warning. Marty's branch rule had been written down that morning and enforced by nothing; the guard was proven by running it, including the `-Force` path and the dirty-tree refusal.
3. ~~Asset Manager~~ — **done 10.08.2026** (T-122). `AssetKinds` in Core, `AssetEntry` + `Project.AssetRootPath`, a two-pass scanner with progress and cancellation, `/projects/:id/assets` with a grid, a list, kind chips, a not-found chip, the scanning banner and the asset view. **Indexing sat behind a second flag** (`Cedar:AssetIndex:Enabled`) that only the desktop shell set: the endpoint made the server walk its own disk, which is the feature on a laptop and a filename disclosure on a shared host. **Replaced 12.08.2026 (ADR-117)** — the walk lives in the desktop agent and the cloud accepts a pushed description (`source`/`batch`/`sweep`/`thumbs`), so the flag and the walk endpoints are gone from the server entirely. Previews changed with it: generated and uploaded for **every** previewable file rather than on request (Marty's call), made affordable by a resumable pass driven off `thumbs/pending` and bounded by a stated disk ceiling. Proven by running it — a real folder scanned, a deleted file **marked and kept** rather than removed, the mark cleared when it came back, and a 403 with the flag off while listing still worked. Three honest gaps became backlog rows, and **two of them were closed the same day**: thumbnails and header metadata (`T-140`) and document↔asset links (`T-141`). What is left is paging rather than virtualisation (`T-142`).

   **T-140/T-141 (10.08.2026).** Thumbnails are generated on demand into `CEDAR_DATA_DIR/thumbs/` — never beside the source, which is somebody's version-controlled game project — and served owner-scoped rather than through the public `/media/*`. Marty asked for the formats he actually works in, so the extension table grew to cover Unity/Unreal/Godot files, DCC sources and DAW projects, and **`.blend` got its own parser**: Blender writes an RGBA preview inside the file, and `CedarClerk.Core/BlendThumbnail.cs` reads it out — the one important file no image library opens. Image dimensions come from a header read, WAV duration from a pure parser in Core; MP3/OGG/FLAC stay silent, because a wrong duration is worse than none. `EntityLink` generalises ADR-106's `TaskLink`, with the pair ordered so A→B and B→A are one row. Links are **stated, not discovered** — an indexed file lives outside Cedar Clerk, so nothing can reference it — and the label says "Linked documents" rather than "Used in". `dotnet test` **698/698**; verified against a fixture folder rather than by reading. One thing unverified: the orientation of a preview from a *real* `.blend`.
4. ~~Task Tracker~~ — **done 11.08.2026** (T-123). `TaskStatuses`/`TaskPriorities` in Core, `GameTask` + migration `AddGameTasks`, `Modules/IndieDev/TaskEndpoints.cs`, and `/projects/:id/tasks` with both required views — a four-column board and a sortable list — plus the task card as a centered modal, the design's chosen variants. The dashboard's "Up next" rail and the list's Tasks/Assets columns stopped being placeholders on the same commit.

   **The links are `EntityLink`, not a second table.** ADR-106 specified `TaskLink(TaskId, EntityType, EntityId)`; T-141 had already generalised that row, so a task link is an `EntityLink` whose one side is a task, and the pair-ordering that makes A→B and B→A one row came free. The reader that draws chips on every card at once had to learn one thing the asset screen never needed: when **both** sides are tasks, one row belongs on two cards — it is pinned by a test.

   **Sorting lives on the server**, in one function, because the board, the list and the dashboard rail all answer "what next" and three implementations is three chances to disagree. Overdue outranks priority deliberately: a P3 due last week needs answering before a P1 due next month, which is exactly what a priority-first rail would get backwards.

   **A finding, fixed rather than filed**: deleting a project left its asset index, and deleting a document left its links — neither has a navigation property, so EF cascaded neither. Tasks would have been the third orphan. All three delete paths clean up now.

   **Sprints are deliberately absent** — that is `T-124`, next. `GameTask.SprintId` exists so the planner adds a table rather than altering this one, and the sprint card on the dashboard says it is not built rather than rendering empty. `dotnet test` **737/737**, frontend 11/11, contrast clean, smoke **53/53**; verified against a running server — board order, reciprocal task links, an overdue rail, `completedAt` surviving an unrelated edit and clearing on reopen, and a deleted task taking its links off the other card.
5. ~~Development Planner~~ — **done 11.08.2026** (T-124, ADR-111). `SprintStates` in Core, `Sprint` + migration `AddSprints`, `SprintEndpoints`, `/projects/:id/planner` with stacked cards in the design's order (current → planned → No sprint → finished, collapsed). The board gained sprint chips and a filter; the dashboard's placeholder card became the real one.

   **A sprint has no status column** — the three states come from the dates on every read, because a stored one is wrong the moment the clock passes the end date and then needs machinery to keep it true. And a sprint is never itself overdue: it holds tasks that are, and the card says so in words.

   **The number caught a bug worth keeping.** `S14` has to be stored (parsing it out of a name breaks on a sprint called "Polish"), and assigning it as `MAX(Number) + 1` reuses the highest number the moment its sprint is deleted. Running the endpoints found it; the test came after. It is a counter on the project now — a gap in the numbering is honest, a second "S3" for a different fortnight is not.

   `dotnet test` **752/752**, smoke **53/53**; verified by running it, including two deletions without number reuse and a task outliving its sprint.
6. ~~Project-scoped glossary~~ — **done 11.08.2026** (T-125, ADR-112). One nullable `GlossaryTerm.ProjectId`, the same move `Draft.ProjectId` made. A document renders with global terms **plus** its project's, and the project's wins where both define the same word — a narrower scope is a more precise definition. The glossary screen's scope row doubles as "where a new term goes", and says so under itself.
7. ~~Build/version tagging~~ — **done 11.08.2026** (T-126, ADR-112). The brief's "extensive tagging system" describes a result, not a mechanism: a version has a number, a date, notes and a set of things in it, and a flat tag string holds none of them. So `Build` is an entity, tasks carry a `BuildId` column and documents attach through the existing `EntityLink` — an asymmetry that follows the question each side is asked. `POST /api/builds/{id}/changelog` writes a **real changelog document** rather than text to copy, and links it back to the build. A build is deliberately not connected to git: no repository tags, no CI, no artefacts, and the empty state says so.

**Phase 13's MUST list is complete.** What remains is MIGHT (press kit, references board, brainstorm, the writers, budget maths) and `T-127`, which is research into somebody else's APIs rather than work on ours.

**MIGHT**, after v1: Press Kit, references board, brainstorm sessions, script/plot writers, game-design helpers, workflow planner, code documentation, budget maths.

**Blocked on somebody else's API, not on us** — `T-127`: itch.io, Steam, IndieDB and LinkedIn are each an `IPublishTarget` implementation (cheap), but whether each even offers a write endpoint is unverified. Research before scoping; the precedent is `Q-13`, where the network order had to be redone once X turned out to charge per post.

### Competitor analysis absorbed — 18.08.2026

Marty's report "Cedar Clerk vs everyone" (13.08.2026: Codecks, HacknPlan, Anchorpoint, IndieViral — 126 screenshots + web research) was read against the code and the backlog. Outcome:

- **15 new backlog rows** (`T-158…T-172`), the anchors being the bridge "sprint → devlog draft" (`T-158`, the one feature nobody in the category has and both halves of which this product already ships), the public project page (`T-159`), onboarding templates (`T-160`), Discord webhook publishing (`T-161`) and the quotas-vs-disk registration blocker (`T-172`, promoted out of `MULTITENANCY.md` §1 into a tracked row).
- **Descriptions corrected against reality**: `T-154` claimed the site root shows a login form — the landing has existed since `T-009` (`LandingEndpoints`, verified live), so the row now describes the real work: devlog-first repositioning, EN default, waitlist. `T-152`, `T-088`, `T-127`, `T-128` gained the report's inputs.
- **`T-148` closed** — Marty set up UptimeRobot on 13.08 (three monitors, status page checked live 18.08); the row lagged `BUSINESS.md` by five days.
- **`docs/product/PRODUCT.md` pricing table corrected**: it said 1/5GB storage while `PlanLimitations` grants 8/16GB — the code is the arbiter, and the 8/16 numbers are exactly why `T-172` is a blocker.

### Input sweep — 18.08.2026 (`docs/INPUT_PROMPT.md`, first sweep)

A new inbox appeared in `docs/`: `docs/INPUT_PROMPT.md` ("consider everything below as a new prompt every time"). It was committed with this sweep — **reversed the same day**: Marty ruled it stays untracked (a dynamic prompt file he rewrites at will, never committed), so it is gitignored and rewrite moments are tracked by mtime against the latest "Input sweep" here — the model the out-of-repo `Input.md` used. Later the same day Marty **retired `Input.md` itself**: `docs/INPUT_PROMPT.md` is the only inbox now, and every live doc was cleaned of the old one (history keeps its mentions as a record). This sweep processed the file's three embedded documents; the next sweep is due whenever its mtime moves past this date.

**1. "Big Feature Scope v1" (authored 29.07.2026)** — ~100 candidate features (`GDD-*`/`MED-*`/`PUB-*`/`WEB-*`/`COM-*`/`MON-*`/`AI-*`/`ANL-*`/`PLT-*`). Written before Phase 13 existed; its positioning wedge (an indie developer’s workplace that turns development into an audience) is the one the product has since adopted, so the document aged into a mix of done, tracked and genuinely new. Triage was done against the code:

- **Already shipped, no action**: GDD-03 (version history, ADR-067), GDD-09 (TOC), GDD-11 (glossary + project scope), GDD-16 largely (9 content languages, linked versions, stale indicator, incremental re-translation), MED-06 (Telegram media groups via Blocks), MED-11 (YouTube embeds), MED-13 (watermark), MED-15 (audio block), PUB-05 (teaser fallback, ADR-077), PUB-12 (durable publish queue, ADR-081), PUB-13 (threads, T-111), WEB-05 (RSS), ANL-01 largely (Draft/Blog/Channel snapshots + geo rollup; Telegram reaction events via `allowed_updates` remain the unbuilt sliver), AI-02 (translation).
- **Already tracked on the board**: MED-07 → `T-172`/`T-088` (R2), WEB-11 → `T-128` (press kit), COM-03 → `T-167`, COM-07 → `T-161` (Discord), GDD-18 ≈ `T-158` (devlog from work), WEB-12 ≈ `T-112`, COM-09 → deferred list, AI metering → `T-152`, PLT-03 partially (the `cedar` CLI exists; publish-from-CI would ride `T-127`'s answers).
- **New rows added**: `T-174` (OG/Twitter tags on blog posts, **High** — verified: only the landing emits any), `T-175` (EXIF stripping, **High** — GPS coordinates currently reach the public blog), `T-176` (FTS search, GDD-13+WEB-08), `T-177` (media library, MED-01/02), `T-178` (post series, PUB-04), `T-179` (content calendar, PUB-01), `T-180` (edit-after-publish sync to Telegram, PUB-03), `T-181` (doc tree + wiki-links, GDD-01/02), `T-182` (living public GDD: block-level visibility + snapshots, GDD-04/05 — the document's own "key combination"), `T-183` (AI video/voice → draft, AI-04/05, gated on `T-152`), and `Q-18` (NSFW policy, its §14.4).
- **Deliberately not taken** — the 18.08 analysis' defocus rule stands: entity database (GDD-06), git sync (GDD-17), diagrams/inline-comments/doc-status (GDD-08/10/12), image pipeline & albums & 3D viewer & annotation & AI alt-text (MED-03/05/09/10/12/14/16), per-channel variants beyond the shipped override (PUB-02), shortener/optimal-time/evergreen/digest/approval (PUB-06…11), custom domains before Q-5 (WEB-02), email newsletter (WEB-06 — revisit after `T-159`), the whole MON-* tenant-monetisation block before PMF, AI-01/03/06…11 (AI-09 RAG explicitly), ANL-02…08 beyond what `/stats` already does (T-153 first), PLT-* wholesale (launch-blocking pieces already live in `BUSINESS.md` gates).
- **Its §14 open questions**: #1 (order) — answered de facto: registration stays closed, own scenario first; #2 (GDD entity) — answered by ADR-102; #3 (storage trigger) — `T-172`; #4 (NSFW) — now `Q-18`; #5 — is `Q-5`; #6 (credits) — ADR-092 wallet + `T-152`.

**2. "Cedar Clerk For Indie Game Developers"** — the same brief as `Gamedev_Focused_Rework.md` that spawned Phase 13 on 10.08. Fully absorbed weeks ago: every feature maps to `T-120…T-135`/`T-127`/`Q-17`, and all six of its TASKS items were completed in the 10–12.08 sessions. No action.

**3. "Session Brief — Visual Polish Sweep" (OG + motion + view transitions)** — written against 0.10.9-era stop-gates that no longer exist (the tag divergence is resolved, tests are ~930 not 442). Verified against the code: Task A is real — blog **post pages emit no OG tags at all** → `T-174` (High), with the brief's hard rules (private posts emit nothing, semi-public title-only, escaping, absolute URLs) carried into the row and required to become an ADR before code. Task B is half-shipped — motion tokens exist since Phase 11 (`--motion-*` + reduced-motion clamp; the brief's `--cabin-dur-*` set would be a duplicate naming), skeletons do not → `T-184` (Low). Task C not built → `T-185` (Low).

**Board hygiene found on the way**: `T-109`/`T-110`/`T-111` had shipped weeks ago (credits wallet, X connector, threads — all live in production) but still sat open — removed; duplicate ID `T-143` (flaky login smoke test renumbered `T-173`); `T-107`/`T-108` sat in the questions table — moved to Improvements; `T-018`/`T-060` remainders were pure live-verification — moved to `TASKS.md` where the live-verify checklist lives; `LIVE`/`LIVE-PREV` tags had reached origin again — deleted from origin per the CLAUDE.md rule. One real bug found while verifying: blog index cards hardcode "RU" as the primary-language label (`BlogEndpoints.cs`, against ADR-064/065) → `T-186`.

### DOCS-FLOW hardening — 18.08.2026 (ultracode)

Marty pasted an external DOCS-FLOW analysis (P0/P1 doc gaps, scheme problems, quick wins, three
waves of agents) with the ultracode keyword. Executed the same evening, verification first:

- **Claims checked by a 12-agent workflow** (6 adversarial claim-checkers with file:line evidence +
  5 terminology extractors + merge). **Refuted**: the 0.10.8/0.10.9 tag divergence (both ARE
  ancestors of master — proven by `git merge-base --is-ancestor`); "release process undocumented"
  (documented in CLAUDE.md + ARCHITECTURE + five ADRs + GitGuard **tests** — a RELEASE.md is
  rejected; the one real gap became `T-195`); "T-052 is a hard blocker" (closed 13.08, lawyer gate
  remains); "Clerk.com/AWS Cedar conflicts already surfaced" (zero evidence anywhere in the repo).
  **Numbers corrected**: inline English server strings = **48 interpolated** `$"…"` (not ~130 —
  plain literals are guarded by `ErrorMessageLocalizationTests` since 01.08) → `T-194`; open Q-xx =
  **12**; TASKS.md live-verify = 8 entries / 19 items. **Confirmed**: no QA / SECURITY / API /
  incidents docs; `knowledge_base/` held only STACK; localization tables live only in code.
- **The same-day batch shipped** (commit `8a8ac75`): **ADR-123** + front-matter
  (`owner`/`last_verified`/`source_of_truth_for`/`guard`) on all 17 live docs;
  **`DocsFlowGraphTests`** — every live doc must be on the DOCS-FLOW map and every mapped path must
  exist, proven to go red with an orphan file (the `SchemaDriftGuardTests` pattern, now guarding
  the docs scheme itself); the mermaid got an **edge legend** (truth-flow / hard gate /
  reading-order), the reading-order edges went dotted, and the two missing nodes joined the scheme
  (`production-environment.md` as the production source of truth, Terms+Privacy with the lawyer gate).
- **`knowledge_base/` seeded**: `docs/knowledge_base/TERMINOLOGY.md` — ~80 project terms across five
  domains, each extracted from real code with a source reference.
- **Two live contradictions fixed on the way**: `BUSINESS.md` §4 claimed all four metrics are computable from existing data while `T-153` says activation needs analytics — reworded honestly;
  a stale `T-052` pointer in `AuthEndpoints.cs` now points at the BUSINESS §1 gates.
- **13 board rows** (`T-187…T-199`): QA.md + verification-planner (High), Wave-1 agents
  docs-auditor + session-closer (High) and input-sweeper (Medium), FEEDBACK / COMPETITORS import /
  the metrics event dictionary (Medium), threat model (Medium), and the Low tail (server-string translation
  with real numbers, preflight backup check, Waves 2–3 + skills, OpenAPI-over-handwritten-API,
  incidents index, Q-xx aging). **No agent was created today** — Marty's own order puts Wave 1 at
  week 1, and his warning (every agent is one more drifting .md file; add them one at a time) is
  taken as binding.
