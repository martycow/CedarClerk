---
owner: marty
last_verified: 2026-09-05
source_of_truth_for: what is in progress now, decisions waiting on Marty, which verification checks are still outstanding (how to run them: docs/tech/QA.md)
guard: none
---

# Tasks

Short horizon: what is in progress, what waits on Marty, and which verification checks are still
outstanding — **how** to run each one is `docs/tech/QA.md` (T-187), the permanent checklist.
Current status: this file's §Notes below; open tasks: the board in `docs/tasks/BACKLOG.md`; history: `docs/tasks/CHANGELOG.md`. (Phase-by-phase status through Phase 13 used to live in a separate ROADMAP doc, retired 24.08.2026 as a near-duplicate of CHANGELOG — see `docs/archive/roadmap-phases-0-13.md`.)

## Now

- [x] Screenshot-derived UI coherence pass — one persisted sidebar model, task-specific page measures, fluid panes, one scroll owner per axis, shared row and selection anatomy, mutually exclusive overlays, actionable empty states, and an editorial Discovery zero state across the full 18-screen evidence set. ADR-246 #design #ux #a11y P0
- [x] `T-357` Advanced Showcase editor — an ordered safe-block model, block list + live result + per-block inspector, reviewed AI prose suggestions, and canonical `/showcase/{slug}` routes for Projects of any kind. ADR-245 #showcase #editor P3
- [x] `T-323` Account display timezone — IANA timezone in the profile, one formatter across the app/calendar/blog, with UTC on the wire unchanged. ADR-244 #ui #decision P2
- [x] `T-322` Calendar week view — Month and Week over the same scheduled posts and queue-slot projection, with no new endpoint. #publishing #ui P3
- [x] `T-369` Turn the public front door into an independent-maker platform — Landing now names Projects, Devlogs and personal Blogs; `/discovery` is a server-rendered shuffled public commons with `#ScreenshotSaturday`, Project Showcase, Blog/Devlog lenses, search and fixed Project categories; Settings owns an off-by-default account opt-in, Showcase owns the category, and Admin owns the page copy and section switches. ADR-243 #growth #discovery #blog #phase13 P0
- [x] `T-129` + `T-155` The reference board, and a project that can have other people on it — the moodboard and the whiteboard were one thing and are built as one: boards of notes, images, frames and links under a single pan/zoom transform, live over SignalR, last writer wins by an item version, with a membership that grants the canvas and nothing else and a fourth case on the media gate so a picture on a shared board is readable by the board's people. ADR-217/218/219. **What it shipped without is `T-301`…`T-316`, and the biggest of them is `T-301`**: the shell has no notion of a shared project, so a member sees seven hooks that 404 #canvas #phase13 P1
- [x] Showcase gets its own screen, and three layout defects go with it — `/projects/:id/showcase` with a rail hook, because the settings modal had a whole site hidden behind its scroll bar; the tool wall cut to one width for every hook; `accent-color` on checkboxes so the export modal's language ticks stop rendering OS blue; Settings' language rows; and a 401 interceptor that ends the session and returns the reader to the URL they were on rather than leaving an open screen silently dead #ui P1
- [x] `T-283` Assets belong to projects — Asset.ProjectId with null as a real bucket, a file follows the document that uses it, a sweep button for the backlog, a project strip on the library and a source strip (Uploaded / On disk) on the project's Assets screen. Closes "blog assets did not move" and "shared assets are unreachable". ADR-204 #assets #phase13 P0
- [x] `T-282` Telegram engagement — reactions via message_reaction_count (explicit allowed_updates, so the bot left the event API for StartReceiving), comments from the linked discussion group, both stored on ChannelPost and summed nightly. Views withdrawn for a Telegram source: the Bot API reports none. ADR-205 #telegram #stats P0
- [x] `T-281` Second annotated round — trim tier for chips and chrome-band passengers, index tiles cut into the board they switch, panel name and counter on one baseline, fullscreen on the drawer lip, blog index language pick, post-to-project filing, Telegram channel pictures, MM/DD/YYYY numeric dates, and three unreported defects: a dense field that was never dense, a small paper button below its touch floor, and a dialog under the drawer lip. ADR-200…203 #design #ux #blog #phase13 P0
- [x] `T-280` Annotated screen corrections — reconciled the full annotated screenshot pass: contextual navigation; compact controls and bounded overlays; Blog projects, Project logo upload, and task files; localized forms; discrete stats periods; paged Glossary and credit activity; Document-facing vocabulary and writer structure; full Glossary coverage with per-language exclusions; Git-like revision diff; independent AI progress; and the Export destination rack. ADR-194…198 #design #ux #editor #phase13 P0
- [ ] UI V2 follow-through — the Cedar Bench port and fidelity pass are merged, and every screen uses the kit (ADR-136…176, `T-205`…`T-233`). The 05.09 sweep closed the kit gaps (`T-251`/`T-252`/`T-254`, ADR-260), the modal-button and i18n cleanup (`T-262`/`T-263`/`T-264`) and the fidelity rows `T-266`/`T-269`. Still on the board: the feature rows `T-248`/`T-250`/`T-259`/`T-261`, decisions `T-273`/`T-274`, fidelity rows `T-268`/`T-272`, and `T-229`/`T-236`/`T-237`; plan in `docs/design/UI-V2-PLAN.md` #design P1
- [ ] Growth anchors — all five are done: T-154 (landing: devlog-first, EN default, waitlist — ADR-135), T-158 (devlog assembler, ADR-132), T-159 (showcase page, ADR-134), T-160 (starter skeletons + example project, ADR-133), T-161 (Discord webhook, ADR-131). Hands-on checks below; the editor screenshot for the landing is T-204 #growth P1
- [ ] Cheap safety — T-175 (EXIF/GPS stripping) is done, ADR-130; hands-on check below, old files on the droplet are T-202. T-174 (OG tags) is done, hands-on check below #security P1
- [ ] Registration blockers — T-172 remainder: media into R2 (quotas cut, ADR-129; first backup restore tested 18.08 — monthly cadence per `docs/product/BUSINESS.md` §5) #infra P1

## Waiting on Marty

`T-234` and `T-235` are gone from here: `Consts.CurrentVersion` reads 0.20.0, the port is merged and
deployed, and `styles/_forest.scss` no longer exists in the tree. What is still his:

- [ ] Narrow-screen designs (`T-236`) — ADR-248 gives the phone a scoped rail/document safety rule, but the complete Cedar Bench response below desktop width is still Marty's design decision: chrome collapse, touch density and the breakpoint set. The brief is `docs/design/bench-responsive-prompt.md`; paste fresh token values from `styles.scss` into its marked block first. Blocks the remaining `T-034` and `T-237` work #design #decision P1
- [ ] The drawer lip and the shelf-panel header — both write `--rail-ink` on `--shelf-frame`, whose light stop is `#B68B60`: the lip's title measures 2.51:1 by day and its summary 1.79:1, and no flat ink clears that ramp end to end, so this is which material those two bands are made of rather than which ink they take. Found while settling Stage 4 and left for the decision. It sat under §Live verification until 01.09, which was the wrong shelf — nobody can check it, it has to be decided #design #a11y #decision P1
- [ ] Show Terms/Privacy to a lawyer — before public registration opens; the texts are filled (13.08), no lawyer has seen them — a gate in `docs/product/BUSINESS.md` §2. The privacy text also needs a line about the waitlist email (ADR-135) before that pass #legal P1

## Live verification

**How to run any of these is in `docs/tech/QA.md`** (T-187), which is the permanent checklist. This
section is only the list of what is outstanding *now*: a row leaves it when the check is done, not
when the feature ships, and the behaviour stays in QA.md for the next time someone touches that
surface.

- [ ] The 05.09 backlog sweep by eye — everything in it is unit/e2e-verified only, nobody has opened a screen. **Editor and sheet**: the bench form controls in the project-tasks modal and Settings (ADR-260); a custom accent that fails 3:1 falling back, and the four area presets changing the sheet (ADR-288); Escape closes a dialog, a scrim click does not (ADR-285); the SPA cross-fade and the blog's cover→hero transition, both gone under reduced motion (ADR-287); tree drag on `/drafts` with the grip, depth from sideways travel (ADR-283); the split editor stylesheet and the single-language initial bundle in a real browser (ADR-263). **Hub and stats**: engine and platforms pickers and the hero tag (ADR-274/276); the journal and the days-since-last-post nudge (ADR-277/281); the Stats tab on one aligned window with a Bluesky/X leaf after a night's snapshot (04:10 UTC) and the CSV button (ADR-273/279); per-day bars on the invite shelf (ADR-271); admin skeletons and audit severity. **Telegram, to `@testingandfun`**: silent + pin on an immediate send; sync of an edited post (T-180 remainder — needs the button); server errors read in each of the nine UI languages. **Forms**: revoke and restore a registration from the Forms tab. QA.md sections as named #sweep #screens P1
- [ ] The Advanced Showcase builder by hand (02.09, T-357, ADR-245) — visual acceptance is complete: the scratch E2E Project was enabled, given a slug and prose, saved, reopened through its canonical public renderer and captured with the authenticated builder at 3440×1392. Still exercise reorder, hide, remove and add; structured links/gallery/trailer data; and one reviewed Pro Plus AI suggestion before this hands-on row can close #showcase #screens P1
- [ ] The Publish / Export tab by eye (02.09, ADR-242) — Write → Preview → Publish / Export at 1536×1024 in both themes: the rack's two controls per card, the stepper's focus moves, a ticked destination's settings, the review's Blocking group emptying as a channel is picked, the compatibility disclosure's count against the table, the utility strip, Back and the Publish button; then Preview and Publish near 1180px and Publish below 760px. Screenshots were taken on the E2E stack, nobody has opened it #editor #screens P1
- [ ] The 02.09 publishing batch by eye — a real X publish with pictures after reconnecting the account (ADR-241, `media.write`) and the publish matrix under Preview's destinations and in the Publish window #x #publishing P1
- [ ] Connected short-post previews by eye (02.09) — Preview on X, Bluesky and Discord with real connected accounts: *Post + link* against *Thread · N*, the link card on X only, pictures on Bluesky only and Mobile in the bezel #editor #screens P1
- [ ] Sprint S-15 by eye (01.09) — five things in one pass. **The editor's inspector**: retype a post's blog address in the slug row (a taken one refuses in place, under the row, and the field shows the stored answer back rather than the keystrokes); the media source, the link href and the Location rows no longer look typeable, while alt and Type still do and each draws exactly one box, not a box inside a box. **A picture's own facts**: select one inserted today and read resolution, size and library file; select one inserted before this sprint — no `assetId` on the node — and confirm they still fill in from the media path; select an image pasted from an external URL and confirm the three rows are absent rather than blank. **`/drafts`**: the (i) button describes the row on the shelf while the row's own click still opens the editor, the Folders shelf comes back when nothing is selected, and neither shelf appears in tree view. **Admin**: a user card opens the modal, Escape closes it, and Delete account replaces it instead of stacking on it. **Glossary**: a term used in documents reads a real count, and two terms of the same scope sharing a spelling make the loser say so in rust rather than read `0` #editor #screens #glossary P1
- [ ] Sprint v0.2.0 by eye (01.09) — none of it has been opened by a person: teams end to end (T-358), the invited stranger's way in (T-304), Shared with me (T-302), project and export presets (T-331), post card thumbnails (T-337), stats sparklines (T-338), the asset window on cover and gallery (T-353), and a Stars payment by a third party (T-359 — real Stars, Marty alone). QA.md §Collaboration, §UI presets and the shell, §Media and assets, §Stats and analytics, §Telegram #collaboration #ui P1
- [ ] The 31.08 batch by eye — the onboarding door (T-328), the language paywall (T-350), Settings' four tabs (T-348), AI credits end to end (T-152), plan locks (T-349) and the four project presets. QA.md §Auth and onboarding, §Billing and plans, §UI presets and the shell #billing #settings P1
- [ ] Blocked on configuration only Marty can create — sign in with Google and Telegram (T-003: an OAuth client plus `@BotFather /setdomain`), analytics end to end (T-153: the PostHog project key), and every check that sends mail (Resend, which is also the only run that ever renders `EmailTexts.ProjectInviteBody` and `TeamInviteBody`). Until each key exists the feature draws nothing at all, so these cannot fail — they cannot start. `docs/for_user/integrations-setup.md` §3c/§3d says where each one comes from #auth #analytics #email P1

Code is written and covered by tests, but never checked by hand or on a device. The UI V2 row below
stays Marty's after the fidelity pass: the audit looked through a capture script, not a person, and
its before/after shots live in the session scratchpad only — untracked, so `cedar run` is the way to
see them.

- [ ] UI V2 ported screens by eye — **this is now most of the app and none of it has been opened.** Stage 5's fourteen: the three hub-like screens (an empty planner, a deckled "No sprint" pile, a released build against an unreleased one, a project with a cover and one without); the four stats-like boards (a kind at zero losing its badge, an inspector shelf standing where a modal used to cover the list, the drafts Folders shelf and its withdrawal in tree view, the admin journal as log lines); the two writer-like ones (the manager's inspector on a selection and on the document, the per-post growth chart's three series and its leaf legend, the glossary inspector with and without a term); the task board's ruler readout and its clearing on leave; and the four doors — login, register, terms, privacy — in both themes. Then Stage 4's three, which were already unopened. Stats: no source selected, one source versus several, a dried leaf, the table view, and the axis shortening when a young channel is ticked. The hub: no projects, no documents, no sprint, no tasks, an archived project. The writer: the strip at one row, at two, and with the captions dropped; the inspector on a selection and on the document; the outline against a long document, both directions of the selection sync. Their verification-map marks are reset for this reason #design P1

## Notes

- **The 05.09 backlog sweep lives on `claude/backlog-sweep`, not merged, not deployed.** Fifty-five
  board rows closed (CHANGELOG 05.09, ADR-260…288), `Consts.CurrentVersion` bumped to **0.22.0**
  (the tag is the dispatcher's to set). Four migrations ride with it, all add-column/add-table:
  `AddPublishJobSilentPin`, `AddPublishTargetStatSnapshot`, `AddProjectEngineAndPlatforms`,
  `AddDraftLastTelegramSentAt`. Verified by tests only — the smoke suite's cold run (60 passed, 18
  audit skips) and the login spec's ten-times repeat (40/40) were green on the branch; the eye-checks
  are the sweep row under §Live verification.
- **T-003 (sign in with Google/Telegram) is built but not configured**: same shape as the analytics
  row below — nothing is registered without credentials, and both doors draw no provider button at
  all, so production looks unchanged until Marty creates the Google OAuth client and runs
  `@BotFather /setdomain`. Apple and Discord stay on T-003; Apple's four costs are written on that
  row now rather than discovered later.
- **T-153 (analytics) is built but not configured**: the code is on master and off by default —
  without `Cedar:Analytics:Enabled` and a project key the provider is never registered, no banner is
  shown and `/api/health` carries no `analytics` section. Turning it on is three lines in the systemd
  drop-in plus a PostHog EU account (`docs/for_user/integrations-setup.md` §3c), and it is Marty's to
  do: the account is his. Until then nothing is being measured, which is the honest state and not a
  defect. `/privacy` already names PostHog — it is dated 1 September 2026 and goes to the lawyer with
  the rest of the text under the `BUSINESS.md` §2 gate.
- Production: **0.20.0 on the droplet, deployed 01.09.2026**, `LIVE` = `4755566`, `LIVE-PREV` = `95fa976`. Everything that had been queued since 0.12.0 is live: the `UI_V2` port and fidelity pass, multitenancy (ADR-206…213), the showcase site and screen, the reference board (ADR-217…219), the 0.17.0 maintainer batch, Waves 1–2, the dialogue tool, and sprint v0.2.0. **The `95fa976` project-frame design (ADR-234 as it was then) was live for three hours and this deploy removed it** — reverted on master by Marty's call, recoverable from the reflog. `LIVE`/`LIVE-PREV` never go to origin (the local-tag rule in CLAUDE.md); the `0.20.0` version tag is local so far.
- **The version string can no longer tell two builds apart**: the deploy before this one also called itself 0.20.0, so the health check's version match proved nothing. What proved the swap was `GET /api/teams` answering 401 instead of 404. Bumped to 0.20.1 on 01.09.2026 for exactly this reason; the rule stands for every deploy after it.
- Sprint v0.2.0 is **closed on master (01.09.2026)**: T-331/T-337/T-338/T-350/T-353/T-358/T-359/T-361
  are done, and T-358 closed T-301/T-302/T-304 with them (CHANGELOG 01.09, ADR-234/235). Three
  migrations rode along — `BotKnownChatAdminsSyncedAt`, `DraftCoverImage`, `AddTeams`. Every eye-check
  the sprint earned is in §Live verification above and **none of it has been done**.
- **Sprint S-15 (ADR-238) is on `master` and uncommitted**, together with the analytics and
  external-sign-in work of the same day (ADR-236/237): T-239, T-240, T-256, T-257 and T-260 are
  built, T-270 was found already shipped and closed as such, and T-187/T-198 landed
  `docs/tech/QA.md` and `docs/archive/incidents.md`. One migration, `AddGlossaryTermUsage`, add-table
  only. **Committed to master as `3b93004`, not deployed.**
  `Consts.CurrentVersion` was bumped to **0.20.1** with it, so the health check can tell this build
  from the one on the droplet — which the previous pair of deploys could not do.
- Active branch: `claude/backlog-sweep` (above). `codex/ui-feedback-pass` holds the UI feedback pass, committed locally and not deployed. `showcase_menu_and_layout` and the `UI_V2` port remain merged into `master`.
- The `indiedev_module` branch is merged and deleted; the module lives in master behind `Cedar:Modules:IndieDev` (reversibility: ADR-101). `dev` is a stale pointer behind master with no commits of its own.
