# Tasks

In-flight work and next actions. Phase-level planning lives in `docs/ROADMAP.md`; this file is the shorter "what's actually next" list. No code-level TODO/FIXME comments exist in the source as of 15.07.2026 (swept across `CedarClerk.Server`, `CedarClerk.Core`, `CedarClerk.Tests`, `cedarclerk-web/src`) — everything here comes from `docs/Handoff_2026-07-15.md` and the Phase 6 tail in `docs/ROADMAP.md`.

## Next: Phase 9e — the second `Input.md` sweep (imported 27.07.2026, not started)
~60 items, confirmed as not overlapping the earlier lists. Analysis in `docs/BACKLOG.md`, order in `docs/ROADMAP.md` Phase 9e.

**Three answers needed before building:**
- [ ] **FI6.2** — collapse to a single paid tier? It reads as a settings tweak but is a pricing restructure (PlanTiers, PlanLimitations, billing, admin panel, ADR-012/013/014)
- [ ] **NF2** — how many content languages? The editor's two-tab model, `Languages.cs`, auto-translate and the blog's `?lang=` all assume two. FI4 and FI5 wait on this
- [ ] **NF3** — email confirmation needs a working Resend key; the configured one still 401s

**Known regression to fix regardless**: `DB2.1` — drafts-table column resize behaves inverted (`N1`). And `DB3.1`: flag emoji don't render on desktop Windows, which invalidates the flag choice made in `I1`/`I17`.

## Admin panel (IF2) — Step 1 done 27.07.2026
Scoped in `docs/admin-panel-scope.md` (decisions and build order are recorded there). Step 1 shipped: `IsAdmin` + migration, `Cedar:AdminEmail` bootstrap, gated `/api/admin` endpoint set, `/admin` page with a user list and summary counts.

- [ ] **Before it works in production: set `Cedar:AdminEmail=cedarworks@mooexe.dev` on the Pi** — see `docs/integrations-setup.md` §3b. Without it nobody is an admin and the panel is unreachable by design
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

**Low block: 6 of 7 done 27.07.2026** — I3, I5, I6, I8, I13, I17. Left: `I15` (custom cross-link text), which needs a stored setting and both renderers, and should be done together with the open `B18`.

**Middle block: 8 of 9 done 27.07.2026** — I1, I2, I4, I10, I11, I14, I16, I18, I19.

Only `I12` (split Settings) is left in the block, and `I14` shrank it: appearance and toolbar customization moved into the editor's side panel, so what remains to split is profile / header slots / social / billing / integrations.

**`IT2` (delete toolbar customization) is still an open question**, and it now interacts with `I14` rather than with Settings: if it wins, the panel loses its toolbar half and keeps only appearance. Worth deciding before `I12` places anything.

## Critical before the next production deploy
- [x] Push real provider keys to the Pi's `data.conf` — done by Marty; **a real payment goes through in production, tested on his own card** (26.07.2026). Auto-translate uses the same keys mechanism but wasn't called out as tested.
- [ ] Manually activate the Stripe Customer Portal in the Stripe Dashboard (Settings → Billing) — the code path (`POST /api/billing/stripe/portal`) exists but the portal itself isn't turned on yet.
- [ ] Fresh Resend API key — the configured one 401s (issued for a since-deleted domain), so invite emails still don't send. See `CHANGELOG.md`, 26.07.2026 deploy follow-ups.
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
