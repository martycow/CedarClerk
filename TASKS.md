# Tasks

In-flight work and next actions. Phase-level planning lives in `docs/ROADMAP.md`; this file is the shorter "what's actually next" list. No code-level TODO/FIXME comments exist in the source as of 15.07.2026 (swept across `CedarClerk.Server`, `CedarClerk.Core`, `CedarClerk.Tests`, `cedarclerk-web/src`) — everything here comes from `docs/Handoff_2026-07-15.md` and the Phase 6 tail in `docs/ROADMAP.md`.

## Critical before the next production deploy
- [ ] Push real provider keys to the Pi's `data.conf` — billing (Stripe/Telegram Stars/PayPal) and auto-translate are dead in production until this happens. Full instructions: `docs/integrations-setup.md`.
- [ ] Manually activate the Stripe Customer Portal in the Stripe Dashboard (Settings → Billing) — the code path (`POST /api/billing/stripe/portal`) exists but the portal itself isn't turned on yet.
- [x] Run `Scripts/deploy.ps1` end-to-end — done 16.07.2026 (Marty deployed commit `98ec07e`, health check passed).
- [ ] Verify, in production only (bot is disabled in local dev): a real Stripe test-mode payment (card `4242 4242 4242 4242`) and a real auto-translate call. (Telegram post-signature path — verified working 16.07.2026 as part of the Blocks migration testing below.)
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

## Phase 8 (v0.8.0) — well underway
See `docs/ROADMAP.md` Phase 8 for the full breakdown: Steps 1–5 done, Step 6 (tags) half-done, Steps 7–8 (comments improvements, AI progress bar) not started, plus a large amount of unplanned Step 9 work (view counter, stats Blog tab, Markdown import, exception handling, debug console + export redesign, RU/EN diff gutter, image compression control, YouTube embeds, the editor redesign) that shipped without ever being tracked here until the 25.07.2026 docs audit. `docs/BACKLOG.md` has what's deliberately deferred out of this phase.

## Tech debt
See the tech-debt table in `docs/ROADMAP.md` — OS migration (Bullseye→64-bit, ~Aug 2026), cloud backup duplication (rclone), .NET 8 EOL (Nov 2026, bundled with the OS migration).
