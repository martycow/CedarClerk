# Tasks

In-flight work and next actions. Phase-level planning lives in `docs/ROADMAP.md`; this file is the shorter "what's actually next" list. No code-level TODO/FIXME comments exist in the source as of 15.07.2026 (swept across `CedarClerk.Server`, `CedarClerk.Core`, `CedarClerk.Tests`, `cedarclerk-web/src`) — everything here comes from `docs/Handoff_2026-07-15.md` and the Phase 6 tail in `docs/ROADMAP.md`.

## Now (04.08.2026): X/Twitter — credits first, connector second (ADR-092)

Marty decided 04.08: X posting is paid by the author via a **universal prepaid credit wallet**
(1 credit = 1 X post = $0.40; packs 10/$4, 50/$18, 100/$30; nothing included in plans).

- [x] **T-109 backend** — `CreditEntry` ledger + `AddCreditLedger` migration, `CreditWallet`
  (grant/charge, idempotent by (Reason, Ref)), `GET /api/billing/credits`, Stripe one-time
  checkout + webhook branch, Stars invoice + bot payment branch. 6 tests; `dotnet test` 577/577.
- [ ] **T-109 frontend** — balance + pack purchase in Settings → Billing; ledger list.
- [ ] **T-110 — X connector** (`XPublishTarget`, OAuth 2.0 PKCE, media as bytes, credit charge on
  success). **Blocked on Marty: X developer account + app + billing.** `XPostBuilder` is done
  (05.08, 11 tests) — weighted counting, URL=23, teaser/override per ADR-077.
- [ ] Live-verify a credit purchase end to end (Stripe test mode, then Stars).

## Now (01.08.2026): Phase 11 — the code-side work is done, the mockups are not

**v0.9.21 is in production** (deployed 31.07): the whole token migration, all 12 screens, the type
sweep, and Phosphor icons. Health green, zero migrations applied, bot running, blog and RSS 200.

**Since that deploy, four more Phase 11 rows closed and are committed but NOT deployed** — T-080
(icon semantics + `/dev/icons`), T-082 (accessibility), T-051 (long words), T-092 (build budgets).
Details in `docs/ROADMAP.md` Phase 11 and `CHANGELOG.md`; the decisions are ADR-074/075/076.
`dotnet test` 442/442, smoke **42/42** (was 37), `ng build` warning-free for the first time.

**What Marty should look at before the next deploy** — one of these is a deliberate visible change:
- [ ] **Meta text is darker across the whole app** (ADR-074). At AA this palette had room for two
  muted text tiers, not three, so `--t3` stopped being a text colour and 91 declarations moved to
  `--t2`. If it reads as flat rather than as legible, it is one token away from being reverted.
- [ ] **The editor now loads as a lazy chunk** (ADR-076). Preloaded in the background, so it should
  feel identical — but it is the app's core screen and worth opening once on a real connection.
- [ ] `/dev/icons` — the icon inventory, and `/dev/styleguide` — now with a pseudo-locale toggle.

**Still open in Phase 11:**
- [ ] **T-076 — mockups**, starting from `_Documents_/CedarClerk/Design-Handoff-2026-07-28/`.
  Marty's tool, not Claude Code's. **Spacing literals (9/11/14/18px) wait on it** — the scale has no
  such steps and picking one is a mockup decision, not a sweep decision.
- [x] ~~T-101 — the blog's server-rendered surfaces~~ — **done 01.08** (ADR-090): tokens are
  generated from `styles.scss` (`DesignTokens.generated.cs`) and inlined by `BlogEndpoints`,
  `LandingEndpoints` and the single-file HTML export (`DraftEndpoints.StaticExportHtml`);
  the ADR-074 `--t3` sweep now covers the blog and the export too.
- [x] ~~Stripe Customer Portal (T-073)~~ — **already active**, confirmed by Marty 31.07.
- [ ] **T-052 (Terms/Privacy)** — Marty says he doesn't know what to put in the `[BRACKETED]` blanks,
  so the next step is not "fill them in" but sorting them: which need a legal entity/jurisdiction
  (genuinely blocked), which follow from decisions already made and visible in the code, and which
  Claude can draft. Hard prerequisite before public registration opens.

### The small defects, none of them blocking
`T-094`, `T-099`, `T-100` — fixed in the 01.08 night pass (see `CHANGELOG.md`). Still open: `T-098`.
`T-034`: iPhone is still wider than the viewport — Marty called it minor, so it rides along with the
Phase 11 screen migration rather than becoming a hotfix.

### Run the tests
- `Scripts/e2e.ps1` — **42** smoke scenarios against an isolated scratch database, no bot token, ~45s.
- `npm run check:contrast` (in `cedarclerk-web`) — every token pair against its WCAG threshold, both
  themes. `SUGGEST=1` prints the nearest passing value, `VERBOSE=1` the passing pairs too. Also run
  as part of the smoke suite, so it cannot rot.
- `npm run icons:generate` — regenerates the icon data **and** the `/dev/icons` usage table. Run it
  after moving or renaming icons, or the inventory quietly describes the previous commit.
- `AUDIT=1 npx playwright test 99-audit -g pseudo` — the long-word screenshots (`.e2e-audit/75…78`).
- `Scripts/e2e.ps1 -Serve` — the same seeded environment left running, for clicking through by hand.
- `AUDIT=1 npx playwright test 99-audit` (in `cedarclerk-web`) — re-captures the audit screenshots
  into `.e2e-audit/`.

### What still nobody has checked
Each needs a person or a device, not a script: flush-on-hide on a real iPhone; incremental
re-translation preserving manual corrections (needs a provider key and Pro Plus); the uk/be/ka
capability refusal; the Posts Manager submission modal and "mark all as read"; tag rename/delete;
audit paging past the first page; the glossary tooltip on a published post; per-language
cross-links; and whether the Russian wording actually reads well.

## Previous (30.07.2026, after the v0.9.16 session): live-verify, then deploy

Today's session closed thirteen backlog rows (data safety, sessions, version history, incremental
translation, three languages, two form types, Posts Manager, Appearance). `dotnet test` 442/442,
`ng build` clean, frontend 11/11. **The entire remaining risk is that none of it has been used by a
human yet.**

### Deployed 30.07.2026 — v0.9.16 is in production
- [x] **DataProtection keys copied into `~/cedarclerk/data/dataprotection-keys/` before the deploy**
  (T-074/ADR-066), with `cp -p` so the original 06.07 key kept its permissions. Verified after
  restart: the directory still holds that one key and no newly generated one, which is what
  "the app found the existing key" looks like from outside.
- [x] `Scripts/deploy.ps1` run end-to-end. Health check green (`0.9.16`), zero `Applying migration`
  lines (no schema change shipped today), no `warn:`/`fail:` in the startup log, bot
  `@cedar_clerk_bot` running, `/`, `blog.mooexe.dev` and `/rss.xml` all 200.
- [x] **Session survival confirmed** (Marty, 30.07): a cookie issued before the 0.9.17 restart still
  decrypted after it, which is what a persisted key ring looks like from outside. The key copy took.
- [x] **`/login` auto-login** (v0.9.17) — it and `/register` were the only routes with no guard, so
  they showed a form without asking the server anything while every other URL let the same browser
  in. `guestGuard` fixed it; verified live.
- [ ] Still open from before: activate the Stripe Customer Portal in the Stripe Dashboard
  (Settings → Billing) — the code path exists, the portal itself is off.

### Live-verify (nothing below has been clicked)
- [ ] **Save guards**: delete a big table and watch the save get refused; check "restore stored"
  brings the text back; check an ordinary heavy edit is *not* refused (the guard is meant to be
  lenient — a false positive here is the failure mode that would make Marty hate it).
- [ ] **flush-on-hide on a real iPhone** — this is the half of T-018 that cannot be tested on a
  desktop, and the 29.07 incident happened on iOS.
- [ ] **Version history**: open a version, switch what it compares against, restore one, confirm the
  restore itself appears in the history.
- [ ] **Incremental re-translation**: edit one paragraph of a translated post, re-translate, confirm
  the *other* paragraphs keep their existing wording (hand-edit one first to be sure).
- [ ] **uk/be/ka**: with DeepL configured, confirm Belarusian reports a clear "provider can't do
  this" instead of burning a quota call; with Anthropic, confirm all three translate.
- [ ] Translate-all modal, the two new form field types on a real private post's gate, the Posts
  Manager submission modal, "mark all as read", the Appearance panel's autosave.

### Older, still not live-verified
The FI2 export rebuild, the FI3 pickers and folder delete, the FI4 forms editor and per-language
gate, tag rename/delete, article title, audit paging, the emoji panel and the paragraph-mark
toggle, the glossary (its page and the tooltip on a real published post), the 28.07 follow-ups
(the gate's language switcher, per-language cross-links, a semi-public post on the blog index).

### Marty's open questions
Q-1 (product scope / the fifth category) and Q-2 (editor tabs) are still unanswered in
`docs/BACKLOG.md`; Q-2's prerequisite — optimistic concurrency — shipped today, so that one is now
answerable rather than blocked.

## Previous: Phase 9e — the second `Input.md` sweep (closed 27–28.07.2026)
**Done**: DB2, DB3, NF2 (six content languages), **FI3**, **FI2**, **FI4**, FI1, FI5, NF1, NF5 — plus a category sweep of the backlog (forms → posts → stats → admin → editor) on Marty's instruction. Sweep v3 (the 28.07 `Input.md` rewrite) is tracked as Phase 9f in `docs/ROADMAP.md`.

**Closed from the older lists in that sweep**: ideas #3, #4, #7, #8, #12, #13; `B1`, `B9`, `B13`, `B17`; `N6` and `N11` were found already built and the backlog rows corrected.

**Left open on purpose, each needing a decision rather than an implementation:**
- [ ] **NF5 / idea #22 — polls inside a post.** A TipTap node plus renderers on all three surfaces, response storage and a results view. Telegram has native polls but *not* inside `sendRichMessage` Blocks, so that surface likely degrades to a link — worth confirming with Marty before building
- [ ] **NF1 — post templates.** Cheapest honest shape is a flag on `Draft` plus filtering, not a parallel entity; needs Marty's word on whether a template should be editable exactly like a draft
- [ ] **Idea #21 — accounts on the blog.** A different identity model sitting alongside anonymous comments; "verified" has no defined meaning yet
- [ ] **Ideas #9 and #14** — both are questions for Marty, recorded in `docs/BACKLOG.md`'s open-questions list

**Not live-verified** (nothing below has been clicked through in a browser): the FI2 export rebuild, the FI3 pickers and folder delete, the FI4 forms editor and per-language gate, tag rename/delete, article title, audit paging, the emoji panel and the paragraph-mark toggle, the glossary — its page, and the tooltip on a real published post — and the 28.07.2026 follow-ups: the gate's language switcher, the per-language cross-links, and a semi-public post appearing on the blog index with its lock.

## Phase 9e detail (imported 27.07.2026)
~60 items, confirmed as not overlapping the earlier lists. Analysis in `docs/BACKLOG.md`, order in `docs/ROADMAP.md` Phase 9e.

**Three answers needed before building:**
- [~] **FI6.2 deferred by Marty 27.07.2026** — the tier restructure waits; nothing in Phase 9e should assume it
- [x] **NF2 answered**: six content languages — RU, EN, DE, FR, ES, JA. The editor's two-tab model, `Languages.cs`, auto-translate and the blog's `?lang=` all assume exactly two today, so this is the structural change FI4 and FI5 were waiting on
- [x] **NF3 is not blocked** — the Resend key works (verified 27.07.2026). Email confirmation can be built whenever it comes up in the order

**~~Known regression to fix regardless~~ — stale, both already fixed.** This file had lagged `docs/ROADMAP.md` Phase 9e, which already recorded both as done: `DB2.1` (resize handle moved to the right edge, verified in code 28.07.2026 — `startColResize`'s math and the handle's `right: -8px` CSS both check out) and `DB3.1` (flag emoji replaced with two-letter codes everywhere — verified 28.07.2026, zero flag-emoji characters remain anywhere in `cedarclerk-web/src`).

## Admin panel (IF2) — Step 1 done 27.07.2026
Scoped in `docs/admin-panel-scope.md` (decisions and build order are recorded there). Step 1 shipped: `IsAdmin` + migration, `Cedar:AdminEmail` bootstrap, gated `/api/admin` endpoint set, `/admin` page with a user list and summary counts.

- [x] **`Cedar:AdminEmail=cedarworks@mooexe.dev` is set on the Pi** — done by Marty, confirmed 27.07.2026. This file had carried it as pending for two sessions after the fact
- [ ] Live-verify the gate: as a non-admin, `/api/admin/users` must 404 and `/admin` must redirect. No automated test covers this — the project has no HTTP-level integration tests
- [x] **Step 2 done 27.07.2026** — plan/expiry, reset trial, lock/unlock, grant/revoke admin, all self-targeting refused server-side. The **audit log was built with it** rather than deferred (new `AdminAuditEntry` table): a log that starts halfway through is missing exactly what someone would look for
- [x] **Step 3 done 27.07.2026** — real `InviteCode` entity, `ApplicationUser.InviteCodeId`, registration switched to look codes up with `Cedar:InviteCode` kept as the fallback, codes deactivated-not-deleted, and manual attribution for the accounts that predate tracking. Shared usability predicate in `CedarClerk.Core/InviteCodeRules.cs` with tests
- [x] **Steps 4–5 done 27.07.2026 — the admin panel is complete.** Read-only cross-owner post list; payments (completed-only revenue total), storage and AI usage; tab strip; admin button in the editor topbar
- [x] Gate live-verified by Marty: 404 for a signed-in non-admin, `/admin` redirects, self-targeting refused
- [x] **Registration bug fixed**: `/api/auth/register` never signed the new account in, so the client's `/me` check reported "Registration failed" on *every* successful signup — and retrying burned single-use invite codes

## Phase 9d — live-review fixes (27.07.2026, done)
Six items from Marty's browser review of 0.9.2: Posts-tab tag picker, per-post form selection, feedback grouped by post, Forms tab reduced to preset authoring only, the stale toolbar-customize button removed, and three Appearance-panel bugs (line height overridden by `.tiptap`, toolbar group order never stored or read, reset button under the debug-console tab). See `docs/ROADMAP.md` Phase 9d.

## Now: Phase 9c — the `Input.md` sweep (started 27.07.2026)
32 new items from `_Documents_/CedarClerk/Input.md`, scoped in `docs/ROADMAP.md` Phase 9c with the per-item dedup verdict in `docs/BACKLOG.md`. **All 9 bugs go first**, ahead of the improvements, because five of them are on code that shipped in the last two days.

**All 9 bugs closed 27.07.2026** (IB3 only partially — see below). IB5's "reply target can't be cleared" turned out to be a CSS rule overriding the `[hidden]` attribute, which was also breaking the load-more button; fixed globally. **I9 done**: presets are standalone on the Forms tab, the form editor has an explicit Save with dirty state, and the export modal has an empty state linking to preset creation.

**Bug pass done 27.07.2026** — IB1, IB2, IB4, IB6, IB7, IB8, IB9 fixed (`dotnet test` 278/278, `ng build` clean, **none live-verified in a browser**). IB5 (blog comment form) not started. IB3 is **still open**: two real defects on that path were fixed (client clock leaking into the stale comparison; string-vs-instant timestamp compare), but the underlying "an autosave fires ~1.2s after a RU load" is unexplained and needs a live reproduction.

**`I7` (watermark) shipped 27.07.2026** — specced by Marty mid-session and built the same session. Tiled heavy semi-transparent text over the blog post, chip-only in the editor. `dotnet test` 289/289. Not live-verified.

Version bumped to **0.9.1** (`CedarClerk.Core/Consts.cs`) and tagged. **Deployed to production 27.07.2026** — health check green, migration applied, data intact, blog posts serving.

**Migrations collapsed to a single `InitialCreate` on prod 27.07.2026** — `__EFMigrationsHistory` now holds one row (`20260727074652_InitialCreate`). This also fixed real drift (two migrations applied on prod whose files had vanished from the repo). New `SchemaDriftGuardTests` fails the build if `Entities.cs` moves without a migration. Procedure and rollback in `.claude/rules/ef-migrations.md`.

**`I12` done, `IT1` done, `IT2` declined 27.07.2026.** Settings is split into Profile / Account (the account menu opens the Profile half); editor zoom is deleted; toolbar customization stays, and is no longer a standalone question now that `I14` put it in the editor's Appearance panel.

**Low block: all 7 done 27.07.2026** — I3, I5, I6, I8, I13, I17, and `I15` (cross-link wording landed as two profile fields, `AddCrossLinkTexts`; `B18` turned out to be already done — see `docs/ROADMAP.md`). This line had gone stale claiming I15 was still open.

**Middle block: 8 of 9 done 27.07.2026** — I1, I2, I4, I10, I11, I14, I16, I18, I19.

Only `I12` (split Settings) is left in the block, and `I14` shrank it: appearance and toolbar customization moved into the editor's side panel, so what remains to split is profile / header slots / social / billing / integrations.

**`IT2` (delete toolbar customization) is still an open question**, and it now interacts with `I14` rather than with Settings: if it wins, the panel loses its toolbar half and keeps only appearance. Worth deciding before `I12` places anything.

## Critical before the next production deploy
- [x] Push real provider keys to the Pi's `data.conf` — done by Marty; **a real payment goes through in production, tested on his own card** (26.07.2026). Auto-translate uses the same keys mechanism but wasn't called out as tested.
- [ ] Manually activate the Stripe Customer Portal in the Stripe Dashboard (Settings → Billing) — the code path (`POST /api/billing/stripe/portal`) exists but the portal itself isn't turned on yet.
- [x] Resend API key — **working as of 27.07.2026**, verified by Marty from the Resend dashboard: `mooexe.dev` is a verified domain and `POST /emails` returns 200. The earlier "the key 401s" note was stale and had been trusted rather than checked; email sending is not a blocker for anything.
- [x] Run `Scripts/deploy.ps1` end-to-end — done 16.07.2026 (Marty deployed commit `98ec07e`, health check passed).
- [x] Verify a real payment in production — done 26.07.2026 (Marty's own card, not just test mode). A real auto-translate call in production is still unconfirmed.
- [x] Deploy the empty-carousel/collage fix (`CedarToTelegramBlocksRenderer.cs`) — **stale as of 25.07.2026**: the fix has been committed and shipped since the `98ec07e` deploy (16.07.2026); the guard (`Count > 0` before yielding carousel/collage/list/table/etc. blocks) is present in the current code. See ADR-019/027 in `docs/DECISIONS.md`.

## Telegram Bot API 10.2 migration (16.07.2026) — mostly verified live, some gaps remain
Full story: ADR-018/019 in `docs/DECISIONS.md`, `.claude/rules/telegram-bot.md`. Confirmed working against `@testingandfun` and in real production use (Marty's "My plan" post): text formatting (bold/italic/underline/strike/code/link/spoiler), headings, lists, images with real native captions, multi-image carousel/collage.
- [ ] Live-verify tables, toggle/details, code blocks, math, footnotes under the new `CedarToTelegramBlocksRenderer` — implemented against the documented type shapes but not yet exercised with a real post.
- [ ] Frontend: the Markdown/Html format selector in the Export popover is vestigial now (`PublishAsync` always sends via Blocks regardless) — candidate for removal, not yet touched.

## Open from Phase 4
- [ ] Mobile-responsive editor (Write/Preview tabs, drawer for channels/drafts) — deferred at the 08.07.2026 Cabin redesign, still not built.

## Open from Phase 5
- [ ] End-to-end phone check: blog reactions/comments + the "Read on the blog →" cross-link, on a real `@testingandfun` post.
- [ ] RSS feed — rolled into Phase 8 Step 2 (see `docs/ROADMAP.md`).

## Editor redesign (24.07.2026) — partially live-verified
See ADR-035, `docs/DECISIONS.md`, for full scope (toolbar customization, Appearance settings, unified Insert modal, tag cloud, New Draft dialog, `/drafts` screen).
- [x] Toolbar popup menus render and the Export modal is positioned/centered correctly — verified 25.07.2026 while fixing 4 unrelated CSS bugs from this redesign's "Cedar Aero" glass effect (`CHANGELOG.md`, `docs/ROADMAP.md` Phase 8 Step 9)
- [ ] Toolbar preset switching, drag-and-drop between rows, accent presets, the new-draft dialog, `/drafts` filters, the unified Insert modal's clipboard auto-detect — still not clicked through
- [ ] Verify a real published post (Telegram + blog) still looks right after the toolbar/Insert-modal rewiring — no renderer changed, but the client-side node-insertion paths did.

## Phase 8 (v0.8.0) — closed 26.07.2026
See `docs/ROADMAP.md` Phase 8 for the full breakdown — all 9 steps done. **Not yet live-verified**: Step 6 (tags in Telegram export) and Step 7 (comment replies/highlight/reservation/dual-timestamp) — deferred by Marty's choice, do before treating the phase as fully proven in production. `docs/BACKLOG.md` has what's deliberately deferred out of this phase, plus the newer "Cedar Clerk 0.9.0" idea dump.

## Localization (B26) — app UI done 27.07.2026, two pieces deliberately left
Every app screen is on `t()` now: login, register, `/drafts`, `/posts` (incl. its stats and comments tab bodies), Settings, the editor (toolbar tooltips, export modal, AI dialogs, status messages) and the debug console. See ADR-044/ADR-050.
- [ ] Long-word (German) layout pass — no fixed-width buttons, wrapping/ellipsis in the topbar and tab strips. A dev-only pseudo-locale that inflates string length is the cheap way to find the breaks
- [ ] Decide whether server-side messages get localized (`ErrorMessages.cs` + every `{ error }` body) — a Russian UI still shows English failure text
- [ ] `/terms` and `/privacy` stay English — still unfinished `[BRACKETED]` legal drafts, translating them now would be translating a draft
- [ ] **Not live-verified**: the Russian wording has not been read through in a browser screen by screen

## Tech debt
See the tech-debt table in `docs/ROADMAP.md` — OS migration (Bullseye→64-bit, ~Aug 2026), cloud backup duplication (rclone), .NET 8 EOL (Nov 2026, bundled with the OS migration).
