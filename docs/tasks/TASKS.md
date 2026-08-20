---
owner: marty
last_verified: 2026-08-20
source_of_truth_for: what is in progress now, decisions waiting on Marty, the live-verification checklist
guard: none
---

# Tasks

Short horizon: what is in progress, what waits on Marty, and the live-verification checklist.
Phase status: `docs/tasks/ROADMAP.md`; open tasks: the board in `docs/tasks/BACKLOG.md`; history: `CHANGELOG.md`.

## Now

- [ ] UI V2 — the Cedar Bench port on branch `UI_V2`. Stages 0 through 4 are done: the contrast checker rework and the density lint, the bench palette in the base blocks, night re-derived against cream paper, the skin mechanism retired, the ten primitives with the styleguide that proves them, the shell — seven chrome components, one parent route wrapping every authenticated screen, `app-page-header` retired, the status bar dissolved into the ruler and the debug console re-housed in the drawer — and the three reference screens (ADR-136…162, `T-205`…`T-225`). Stage 4 replaced four sparklines with one multi-series chart whose sources are the legend and the filter, turned the project head into a worktop with a wall of module tiles, and left the writer with no chrome of its own: its save state and its one action are published to the rail, its meta strip became a 340px inspector, its two toolbar rows became one strip that fits itself, and a 224px outline walks the document. Next is Stage 5 — the remaining pages by pattern, `T-226`…`T-228`. The remainder is on the board as `T-226`…`T-237`, and the thirteen features the three screens found missing behind the kit are `T-238`…`T-250`; plan in `docs/design/UI-V2-PLAN.md` #design P1
- [ ] Growth anchors — all five are done: T-154 (landing: devlog-first, EN default, waitlist — ADR-135), T-158 (devlog assembler, ADR-132), T-159 (showcase page, ADR-134), T-160 (starter skeletons + example project, ADR-133), T-161 (Discord webhook, ADR-131). Hands-on checks below; the editor screenshot for the landing is T-204 #growth P1
- [ ] Cheap safety — T-175 (EXIF/GPS stripping) is done, ADR-130; hands-on check below, old files on the droplet are T-202. T-174 (OG tags) is done, hands-on check below #security P1
- [ ] Registration blockers — T-172 remainder: media into R2 (quotas cut, ADR-129; first backup restore tested 18.08 — monthly cadence per `docs/product/BUSINESS.md` §5) #infra P1

## Waiting on Marty

- [ ] Narrow-screen designs (`T-236`) — the one thing the port cannot take from the kit: the design system states `1440x900` and nothing else, so the shell ships with no width breakpoint of its own (ADR-147, Marty's answer to `Q-23`). The brief is written — `docs/design/bench-responsive-prompt.md`, nine questions — and the run is Marty's to make; paste fresh token values from `styles.scss` into its marked block first. Blocks `T-034` and `T-237` #design #decision P1
- [ ] Delete the neutralised forest partial (`T-235`) — 1 229 lines that compile into the bundle and match nothing, the skin attribute never being set. A deletion that size is a `.claude/rules/destructive-operations.md` event, so it waits for the word #design P2
- [ ] Show Terms/Privacy to a lawyer — before public registration opens; the texts are filled (13.08), no lawyer has seen them — a gate in `docs/product/BUSINESS.md` §2. The privacy text also needs a line about the waitlist email (ADR-135) before that pass #legal P1

## Live verification

Code is written and covered by tests, but never checked by hand or on a device:

- [ ] UI V2 palette by eye — `cedar run` and walk both themes: the state washes, the avatar initials in the editor and the admin list, the blog's channel avatar, and the focus ring on paper and on rail. The ring and the density floors are no longer static-only — both were measured in real Chromium against the built bundle — but a measurement is not a look, and nobody has looked #design P1
- [ ] UI V2 Stage 4 screens by eye — none of the three has been opened. Stats: no source selected, one source versus several, a dried leaf, the table view, and the axis shortening when a young channel is ticked. The hub: no projects, no documents, no sprint, no tasks, an archived project. The writer: the strip at one row, at two, and with the captions dropped; the inspector on a selection and on the document; the outline against a long document, both directions of the selection sync. Their verification-map marks are reset for this reason #design P1
- [ ] The drawer lip and the shelf-panel header — both write `--rail-ink` on `--shelf-frame`, whose light stop is `#B68B60`: the lip's title measures 2.51:1 by day and its summary 1.79:1, and no flat ink clears that ramp end to end, so this is which material those two bands are made of rather than which ink they take. Found while settling Stage 4 and left for the decision #design #a11y #decision P1
- [ ] UI V2 shell by eye — `cedar run` and walk every authenticated screen inside it: which hook lights on which route and what the crumb says, the dots menu (theme, Appearance, Glossary, Admin, `/dev/*`), the drawer lip opening onto the journal, the ruler showing the editor's counts and blank everywhere else, and the four routes that stay outside the shell. Static checks cover the CSS; the route table and the crumbs are behaviour and nobody has watched them #design P1
- [ ] cedar restart — the CLI's only destructive command, never run (drops the blog together with the app for seconds; run when it costs nothing) #cli P2
- [ ] cedar build --installer and cedar deploy --desktop — not run end-to-end since the pipeline moved to C# (ADR-119); installer build and Cloudflare distribution were verified only on the old pipeline (0.10.7–0.10.9) #desktop P2
- [ ] Desktop installer on a clean machine — T-121: builds and is verified on artifacts, never executed #desktop P2
- [ ] Desktop after ADR-117 — the list in `docs/tech/DESKTOP.md` §Risks: cloud `/projects` without the price table, a scan of a real Unity/Blender folder, the orientation of a real `.blend` preview, cancelling mid-preview-pass and resuming, zero processes after closing the window, `curl` to the agent without a token → 401 #desktop P2
- [ ] Bluesky post with an image — Marty only: `uploadBlob` against real bsky.social (list building and text are unit-tested, ADR-109) #publishing P2
- [ ] flush-on-hide on a real iPhone — remainder of T-018 (the 29.07 incident was on iOS; Safari kills a tab differently than desktop) #mobile P2
- [ ] Save guards + restore from history — remainder of T-060: see the wipe-save refusal, "restore stored" and a version rollback by hand; confirm a heavy honest edit does not false-positive #editor P2
- [ ] OG previews (T-174) — via @WebpageBot or opengraph.xyz with `?v=2` on the URL (Telegram caches the old scrape): public post — full card with image and description; semi-public — title + fallback only; private and the gate — nothing. `/og-default.png` serves from the blog host #blog P1
- [ ] EXIF stripping (T-175) — upload a phone photo with GPS, download it back from `/media/`, confirm the coordinates are gone (any EXIF viewer); the Telegram derivative too #media #security P1
- [ ] Paste/drop into the editor (T-177) — a screenshot from the real clipboard (Win+Shift+S → Ctrl+V) and a multi-file drag&drop; the progress panel appears, files land at the drop point #editor P1
- [ ] Media library (T-177) — insert from the library → publish → media renders on the blog and Telegram; deleting a used asset → 409 with post titles; a free one — the file AND the `_tg` derivative disappear from disk #media P1
- [ ] The `[[` trigger on a Russian layout (T-181) — how the suggester behaves when `[` needs the Latin layout; if unreachable from Russian, an alternative is needed (a button/command) #editor P1
- [ ] A series of 3 posts (T-178) — the `/series/{slug}` page, prev/next on posts, "Part N of M"; a private unlisted post does not shift a stranger's numbering #blog P1
- [ ] The tree (T-181) — moving via the "Move under…" menu, up/down among siblings, editor breadcrumbs open the document, the backlinks chip counts correctly after saving with a `[[` link #editor P1
- [ ] Discord webhook (T-161) — connect a real channel webhook in Settings → Integrations, publish a post with an image: the announcement lands, the blog link unfurls into a card with the OG image, no @everyone ping even if the text contains one #integrations P1
- [ ] Sprint → devlog (T-158) — on a real sprint with done/open tasks and a released build: the "Devlog draft" button on the planner card assembles the three sections in the document's language and opens the editor #editor P1
- [ ] Onboarding (T-160) — on a fresh account: "Create an example project" from the empty `/projects` builds Cedar Quest whole (board, sprint, build, devlog), and a new ordinary project's starter document opens with its skeleton in the UI language #growth P1
- [ ] Showcase page (T-159) — enable the public page in project settings, mark two tasks public: `/games/{slug}` shows cover, links, the devlog feed (private-listed posts locked, unlisted absent) and the roadmap; archiving the project 404s the page #blog P1
- [ ] Landing + waitlist (T-154) — after deploy: an incognito visit shows the EN devlog-first page (RU browser gets RU), the waitlist form accepts an email and swaps to the done-line, the row lands in `WaitlistEntries` (`cedar db`) #growth P1
- [ ] Old unverified small things — once each, no rush: incremental re-translation preserving manual edits; DeepL's uk/be/ka refusal with a clear message; the glossary tooltip on a live published post; per-language cross-links; tag rename/delete; audit paging past page one; Russian wording screen by screen #misc P3

## Notes

- Production: 0.12.0 on the droplet, `LIVE` = `0.12.0`; master is ahead by the 0.12.1 session (OG tags, series, media library, tree + wiki-links, metrics dictionary) — deploying is Marty's call. `LIVE`/`LIVE-PREV` never go to origin (the local-tag rule in CLAUDE.md).
- `UI_V2` branched off master at the 0.12.2 commit and carries the Cedar Bench port, Stages 0 through 3. It cannot deploy from there — `cedar deploy` refuses anything but master — so the merge is `T-234`, at the end of the port.
- The `indiedev_module` branch is merged and deleted; the module lives in master behind `Cedar:Modules:IndieDev` (reversibility: ADR-101). `dev` is a stale pointer behind master with no commits of its own.
