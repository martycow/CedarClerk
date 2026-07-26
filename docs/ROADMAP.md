# Roadmap

Live phase-by-phase execution log, folded in from the former `Plans/cedar-clerk-saas-plan.md` (v1.7, 15.07.2026) and `Plans/session-brief-v0.8.0-planning.md`, which are now archived under `Plans/OLD/`. **This file is the one live roadmap going forward** — update it when a phase item closes, don't recreate a parallel plan doc. Architectural/product decisions referenced below (why something was built a certain way) live in `docs/DECISIONS.md`, not here — this file tracks *status*, DECISIONS tracks *rationale*.

## Status summary (as of 26.07.2026)

Phases 0–5 done and production-verified. Phase 6 (multi-tenancy & public SaaS) is code-complete except billing/translation are waiting on real provider keys being pushed to the Pi. Phase 7 (Entertainer role) not started. **Phase 8 (v0.8.0) is code-complete** — all 8 steps plus the unplanned "Step 9" work done — but Steps 6 (tags in Telegram) and 7 (comments) have **not been live-verified** (deferred by Marty's choice on 26.07.2026); do that before calling the phase fully closed. 4 UI/blog bugs fixed and live-verified 25.07.2026 (view-count double-count on language switch, toolbar popups clipped by a CSS regression, export modal mispositioned, iPad horizontal scroll — see `CHANGELOG.md`). `Consts.CurrentVersion` was bumped to `0.9.0` ahead of Phase 8 actually closing — flagged as a real inconsistency, not yet reconciled with Marty.

---

### ✅ Phase 0 — Infrastructure — DONE 05.07.2026
- [x] Pi: OS full-upgrade, SSH enabled, console boot
- [x] Cloudflare: nameservers moved, zone active, DNSSEC
- [x] cloudflared installed, `cedarpi` tunnel as a systemd service
- [x] Public hostname `cedarclerk.mooexe.dev` → localhost:8080, verified externally
- [x] .NET 8 ASP.NET Core runtime 8.0.28
- [x] Backups: microSD ext4 at /mnt/backup, script + 3:30 cron, run manually once
- [~] Deploy script — moved to Phase 1 (nothing to deploy yet)

### ✅ Phase 1 — Server skeleton — DONE 06.07.2026
- [x] .NET SDK on laptop: 9.0.200 (targets net8.0)
- [x] Solution: `CedarClerk.Server`, `CedarClerk.Core`, `CedarClerk.Tests` (all net8.0)
- [x] Kestrel on port 8080; `/` and `/api/health` endpoints
- [x] Deploy: `deploy.ps1` (publish → scp → restart → health check with retry), SSH keys, sudoers NOPASSWD for `systemctl start/stop cedarclerk`
- [x] SQLite + EF Core: `cedar.db` in `CEDAR_DATA_DIR`, WAL mode, `Migrate()` on startup; entities `ApplicationUser`, `Draft`, `Channel`, `Asset`
- [x] ASP.NET Identity (cookies, 401/403 instead of redirects), invite-code registration
- [x] CRUD drafts: `/api/drafts`, all requests filtered by `OwnerId`
- [x] Verified in production: register, login, create/read a draft via `cedarclerk.mooexe.dev`

### ✅ Phase 2 — Bot host — DONE 06.07.2026
- [x] Prototype logic moved into `TelegramBotService : BackgroundService`. No token = disabled, server stays up (enables local dev without a 409 Conflict)
- [x] Token via Pi drop-in `Cedar__BotToken`; **long polling** (webhook considered, deferred — see ADR-005 in `docs/DECISIONS.md`)
- [x] `SendRichMessage` typed API confirmed to work; `Html`/`Markdown` on `InputRichMessage` are alternatives — renderer generates Html
- [x] `CedarToTelegramHtmlRenderer` v0: paragraphs, bold/italic/underline/strike/code/link, code blocks, blockquote, headings. User text escaped. 4 unit tests
- [x] `POST /api/posts/export` (auth + OwnerId): draft → renderer → `SendRichMessage`. `ChatId` accepts numeric IDs and `@username`
- [x] Verified: post published to `@testingandfun`, bot is admin with Post Messages right
- Note: channel comments live in a linked discussion group (Telegram attaches it automatically) — not needed for posting

### ✅ Phase 3 — Web App MVP: editor — DONE 06.07.2026
- [x] Angular workspace `cedarclerk-web` (SCSS, no SSR), `/api` proxy to localhost:8080 (same-origin cookie auth)
- [x] Frontend auth: `AuthService` (signals), `authGuard`, `LoginComponent`, `/login` and `/editor` routes
- [x] TipTap direct integration: StarterKit, toolbar with active-state highlighting, live JSON debug panel
- [x] Layout: topbar + toolbar, left dock (drafts+channels), center sheet, right dock (preview + settings — later removed, see ADR-011), status bar with a 32768-char limit
- [x] Draft list + autosave: 1.2s debounce, saved/saving/dirty indicator, draft switching without losing edits, sorted by `updatedAt`
- [x] Export button → `POST /api/posts/export` (saves before publishing). Full browser→DB→renderer→Telegram cycle verified
- [x] Renderer v0.2: confirmed Html mode DOES support block tags (earlier belief it didn't was a wrong-test-channel error). `CedarToTelegramHtmlRenderer` (HTML-only) is canonical — see ADR-007. 6 tests
- [x] `PostEndpoints` returns 503 with a clear message when the bot has no token
- [x] Media upload: `POST /api/assets` (whitelist jpeg/png/gif/webp, 5MB limit — Telegram's URL-download cap), files under `~/cedarclerk/data/media`, public `/media` serving (no auth — Telegram must download it). TipTap image extension + toolbar button
- [x] Frontend build deployed to Pi: `ng build` → `dist/browser` → `publish/wwwroot`, `deploy.ps1` builds front+back in one command. Verified from laptop and phone 06.07.2026
- **Milestone hit**: "a post written in the browser reaches the channel", including posts with images

### Phase 4 — Channel management & quality-of-life (in progress → effectively done, one item open)
- [x] Channel connection via the bot, target-channel selection — `ChannelEndpoints` + `ChannelsService`, "Channels" popover in the editor topbar
- [x] Scheduled publishing — Quartz.NET (`ScheduledPostEndpoints` + `PublishDueScheduledPostsJob`), "Schedule" section inside the Export popover, quick time presets, scheduled-post list with removal regardless of status
- [x] **"Cabin" UI/UX redesign** (08.07.2026) — new visual language from Marty's Claude Design mockups (now archived under `_Documents_/CedarClerk/OLD/Design/`):
  - Design tokens + dark theme (see `docs/DESIGN.md`), `ThemeService`, ☾/☀ toggle
  - New topbar, "Paragraph/Heading N" dropdown replacing 6 separate buttons, "⋯" overflow menu (tables/formulas/toggle/indent), status bar (zoom/word count/char count/sync indicator), draft deletion with confirm
  - Table row/column management in the overflow menu
  - Login/Register redesigned to match the token set
  - **Deliberately not built / rejected** — see ADR-011 in `docs/DECISIONS.md` (live preview bubble, right "Publish" panel); mobile responsive layout and channel-stats card deferred to a separate session
  - Investigated: `SendRichMessageDraft` cannot do a "progressive reveal" effect for channel posts (private-chat-only API) — see `.claude/rules/telegram-bot.md`
- [x] **"Stats + .cedar" session** (08.07.2026, deployed & verified in prod) — channel stat snapshots + sparkline, `.cedar` export/import (see ADR-006)
  - [x] MVP-sprint tail verification: undo/redo, rename-without-reload, honest post-link fallback, no nested `.git` in `cedarclerk-web` — all 4 were already closed before this session started
- [x] **LLM buttons** (verified 15.07.2026 by reading the code — turned out to already be fully implemented, the backlog just hadn't been updated): auto-translate/re-translate and fix-errors/"schizo-izer", gated to Pro Plus, `AiUsage` quota, styled confirm modals (not `window.confirm()`) for both flows
- [ ] Mobile-responsive editor (Write/Preview tabs, drawer for channels/drafts) — from the original mockup, deferred at the 08.07.2026 redesign, **still open**

### Phase 5 — Blog platform — DONE except RSS
- [x] `blog.mooexe.dev`: `.cedar` → HTML pages, host-routed in the same Kestrel process, `CedarToBlogHtmlRenderer`, publish/unpublish from the Export popover, KaTeX, carousel, CSS collage mosaic
- [x] Anchor-based like/dislike reactions on text fragments, anonymous with rate-limiting via `VisitorHash` (no raw IPs stored)
- [x] Comments (simple, post-hoc moderation via deletion — see ADR-016), right-side panel in the editor
- [x] Production-verified 09.07.2026: deploy confirmed, tests green, `blog.mooexe.dev` reachable externally (including from Russia via VPN) — Cloudflare Tunnel ingress works
- [ ] End-to-end phone check: reactions/comments + the "Read on the blog →" cross-link on a real `@testingandfun` post
- [ ] RSS feed — folded into Phase 8 Step 2 (low implementation cost, scheduled early there)

### Phase 6 — Multi-tenancy & Public SaaS — in progress, core code complete, started 09.07.2026
- [x] **Step 1 — Multi-tenancy core** (09.07.2026): ownership audit (see table in `docs/DECISIONS.md`), `PlanTier` quotas (`ChannelEndpoints` max-1-channel on Free, 200MB storage quota on `AssetEndpoints`/`.cedar` import). 110/110 tests green at the time.
- [x] **Step 2 — Telegram account linking** (09–10.07.2026, corrected mid-session — see ADR-009): `TelegramLoginVerifier` (HMAC, 7 tests incl. a real test vector cross-checked via `openssl`), `ApplicationUser.TelegramUserId`/`Username`/`FirstName` (unique partial index), link/unlink endpoints, `TelegramLinkService` on the frontend. 117/117 tests green.
- [x] **Bot chat auto-discovery** (10.07.2026): `BotKnownChat`/`BotKnownChatAdmin` populated from `my_chat_member` updates, "Bot is already in" section in the Channels popover, `BotChatAccess.CanPost` as shared pure permission logic (9 tests). 126/126 tests green.
- [x] **Bilingual content — RU/EN posts** (10.07.2026): `DraftTranslation` model, RU|EN tabs in the editor (one TipTap instance, tab switch = flush autosave then load), stale-translation indicator, `?lang=en` on the blog with fallback to RU (no 404), language badges. 126/126 tests green. Deliberately not included: `.cedar` export doesn't carry translations; blog comments/reactions are shared across both language versions of a post.
- [x] **Blog: timeline + tags + post signature** (10.07.2026): monthly timeline grouping, `Draft.Tags` with AND-filtering (`?tags=a,b`), `ApplicationUser.PostSignature` appended to both the Telegram export and the blog page.
- [x] **Billing + auto-translate — code complete, waiting on provider keys** (10–11.07.2026, billing refactored 11.07.2026, commit `788d421`): see ADR-012/ADR-013/ADR-014 in `docs/DECISIONS.md` for the tier/provider decisions, and `docs/integrations-setup.md` for the setup runbook. Stripe Customer Portal added 11.07.2026 (needs manual activation in the Stripe Dashboard). `dotnet test` 162/162, `ng build --configuration production` clean as of 11.07.2026. **Not yet verified in production** — real payment/translation flows can only be tested once keys are on the Pi.
- Out of scope for this phase: blog subdomains (see ADR-017, refined by ADR-020 — hybrid domain split, tenant blogs move to a separate dedicated domain rather than `mooexe.dev` subdomains), BYO bots, custom domains

**Dependency, recorded not scheduled** (ADR-020, `docs/DECISIONS.md`): tenant blogs are decided to live on a separate dedicated domain (working name `cedarclerk.app`, not yet registered), not `mooexe.dev` subdomains. Before any multi-tenant blog work proceeds: register the domain, add a Cloudflare zone + wildcard `*.<domain>` through the tunnel, extend the existing Kestrel host-routing (`Program.cs` `MapWhen`, already used for `blog.mooexe.dev`) to the new domain and per-tenant subdomains. Not attached to Phase 6 or any other numbered phase yet — genuinely blocked on the open TODOs below.

### Phase 7 — Entertainer role — not started
- [ ] Interactive posts for subscribers: polls / A-B choice blocks
- [ ] Integration with GDD-style voting for a related project ("Cedar Station")

### Phase 8 — v0.8.0 — planned 15.07.2026, all steps done (26.07.2026) — live verification of Steps 6/7 still pending
Step order below follows implementation dependencies, not the lettered order (A–H) of the original brainstorm.

- [x] **Step 1 — Blog polish & bugfixes** (completed 16.07.2026)
  - [x] BUG: article title duplicated in the En version (16.07.2026) — **root cause was structural, not EN-specific**: `RenderPostAsync` always rendered `<h1>{Draft.Title}</h1>` above the document body, but authors routinely also type the "real" title as the document's own first heading, so RU posts showed the same stacked-heading duplication (e.g. `hello-world`: `<h1>Hello World</h1><h1>Всем привет!</h1>`), just more noticeable on the EN translation Marty happened to be reviewing. Fixed via `HeadingOutline.StartsWithHeading(cedarJson)`: skip the page-level `<h1>` when the document already opens with a heading, fall back to it otherwise. Verified live against all 5 published posts (RU + EN) via curl.
  - [x] No En translation → "Not translated" fallback (16.07.2026) — `RenderPostAsync` now only swaps `lang`/`title`/`cedarJson` when a `DraftTranslations` row actually exists for the requested language; otherwise it renders the original with a visible `.not-translated-notice` banner instead of silently mis-tagging the page as the requested language.
  - [x] Language switch must change the entire site UI language, not just the article text (16.07.2026) — **scoped to the individual post page**, since that's the only place a language switcher exists today (the homepage has no lang toggle/persistent preference — that would be a separate, bigger feature: cookie/session persistence, default-language detection — not built, flagging as open scope below if wanted later). Localized: back-link ("All posts"/"Все посты"), "View in Telegram" label, TOC nav title ("Contents"/"Оглавление"), and the annotation/comment UI strings (Comments, Show more comments, placeholders, Send) all now follow the post's active `lang`. `CedarToBlogHtmlRenderer.Render`/`AnnotationControlsHtml` gained an optional `lang` param (plain `"en"`/`"ru"` string — Core still has no project reference to `CedarClerk.Localization`).
  - [x] Dividers (horizontal rules) on the blog page (16.07.2026) — renderers already supported `horizontalRule`→`<hr>`/`InputRichBlockDivider`; just needed an editor toolbar button (`insertDivider()`, `LucideSeparatorHorizontal` icon) plus `hr` styling on both the editor and the blog page (neither existed before).
  - [x] Anchors (in-page links) — **done as a side effect of Table of Contents below (16.07.2026)**: every heading gets a stable `id`/anchor now, on both the blog and in Telegram
  - [x] **Table of Contents** (16.07.2026, added mid-session, not in the original brainstorm) — auto-generated from the document's headings, works on the blog (`<nav class="toc">` linking to heading `id`s) **and** in Telegram (Bot API 10.2 anchor blocks, verified live against `@testingandfun` — tapping a TOC entry jumps to the section within the message). See ADR-024, `docs/DECISIONS.md`. New toolbar button (`tableOfContents` node, content computed at render time, nothing authored)
  - [x] "Back to top" / "Back to menu" buttons (16.07.2026) — floating `.floating-nav` widget on the post page (fixed bottom-right, appears after 400px scroll): "back to menu" is a plain link to `/`, "back to top" smooth-scrolls. Not added to the homepage (nothing to scroll back from at the top).
  - [x] ~~Increase/decrease indent~~ — **investigated 16.07.2026, decided against**: Telegram Bot API 10.2's Blocks model has no generic indent/margin concept for a plain paragraph (only list nesting and blockquote/pull-quote offsets — confirmed against the current API docs), so it would only ever work in the blog. Marty confirmed not worth building blog-only. Existing `indent()`/`outdent()` toolbar buttons already cover list nesting (`sinkListItem`/`liftListItem`) — that's a separate, already-shipped feature and unaffected by this decision.
  - [x] Line-style timeline on the blog homepage (16.07.2026) — vertical chronology line down the left edge of `.post-list` with a dot per post (`.timeline-item`/`.timeline-dot`), git-graph/commit-history style, as Marty confirmed. Note: the "existing month-grouped timeline (Phase 6)" this was meant to be distinct from **doesn't actually exist in the current code** — `RenderIndexAsync` was always a flat reverse-chronological card list, no month headers/grouping found anywhere in `BlogEndpoints.cs`. Docs/backlog drift (see the recurring note on this in memory) — the 10.07.2026 ROADMAP entry's "monthly timeline grouping" claim doesn't match shipped code. Built directly on top of the flat list rather than reconciling with a grouping feature that isn't there.
- [x] **Step 2 — RSS** (completed 17.07.2026)
  - [x] Blog RSS feed — `GET /rss.xml` on the blog host (`BlogEndpoints.RenderRssAsync`), standard RSS 2.0, latest 30 published posts (`RssItemLimit`, matching the existing 30-item convention already used by `ChannelEndpoints`' stats query), title/link/guid/pubDate/excerpt-as-description per item, `<atom:link rel="self">` for feed-reader validators. Auto-discovery `<link rel="alternate" type="application/rss+xml">` added to every blog page's `<head>`. Verified live against a local test post: valid XML (parsed clean), correct escaping of `< > & "` in title/content via the same `WebUtility.HtmlEncode` used everywhere else in this file.
- [ ] **Step 3 — Legal pages** (hard prerequisite before public registration opens) — **structure built 17.07.2026, content still placeholder**
  - [x] Terms of Service — `/terms` route, `TermsComponent`, drafted with `[BRACKETED]` placeholders for everything jurisdiction/business-specific (operator entity, address, governing law, refund policy, minimum age, active payment processors, etc.) — **not real legal text, needs Marty to fill in the placeholders and get it reviewed** before it's usable
  - [x] Privacy Policy — same treatment, `/privacy` route, `PrivacyComponent`. Content accurately reflects what the app actually collects per the current code (account email/password hash, Telegram user ID/username/first name if linked, draft content/media, plan tier + payment-processor customer ID — never raw card numbers, a salted SHA-256 hash of IP for blog reaction dedup — not the IP itself, theme preference in `localStorage` only) and which third parties get data (Telegram, whichever payment processor(s) are active, whichever AI translation provider is configured) — still needs the bracketed jurisdiction/entity/policy blanks filled in.
  - Shared `LegalPageComponent` (`cedarclerk-web/src/app/shared/`) holds the common page frame (logo, title, back link, prose styling) so Terms/Privacy don't duplicate layout markup. Register page footer now links both. Verified in-browser (both pages render, register→Terms navigation works).
- [x] **Step 4 — Header Slot System (premium)** (completed 17.07.2026) — **blog-only**, confirmed with Marty (does not touch the Telegram export). Configured globally in Settings, not per-post.
  - [x] Article header renders as `Article Name / [slot1] • [slot2] • [slot3]` — new `.post-header-slots` line under the blog `<h1>`, distinct from the existing date/views/tags meta row
  - [x] Slot 3 gated to Pro — `PlanLimitations.MaxHeaderSlots(tier)`, enforced both server-side (blog render clamps a downgraded account's leftover 3rd-slot config; `POST /api/auth/profile` rejects with 403 if a non-Pro account submits one) and in the Settings UI (disabled dropdown + PRO badge)
  - [x] Slot types: Author Signature (new dedicated `AuthorDisplayName` profile field — explicitly **not** the existing free-form `PostSignature`, confirmed with Marty), URL (fixed profile URL, clickable), Map Location (fixed profile location, text + clickable Google Maps search link), Published Date, Length (character count), Time to Read (200 wpm, 1-minute floor) — all via `CedarClerk.Core/HeaderSlotRenderer.cs`, unit-tested
  - [x] Settings UI: new "Header slots" card — 3 profile-value inputs (author name / URL / location) + 3 slot-type dropdowns, verified live in-browser (fresh test account, save round-trip survives reload, direct-API bypass of the disabled slot-3 dropdown correctly 403s)
  - [x] **Extensibility**: new `HeaderSlotType` enum (`CedarClerk.Core/HeaderSlotType.cs`) + one switch arm in the renderer is the entire footprint of a future slot type — no schema or architectural change, per the hard requirement
  - Drive-by fix: `settings.component.ts`'s `saveSignature()` had no `catch` block (a 403 from the existing Pro-signature-gate was an unhandled rejection with zero UI feedback) — fixed alongside the new `saveProfile()`, both now use the existing `httpErrorMessage()` helper
  - Two judgment calls made without a further round-trip (see the plan for reasoning, not re-litigated here): "Time/Date/DateTime" became one "Published Date" slot type, not three; "Length (symbols/words)" became character count, not word count (Time to Read already covers word-count); build this in from the start
- [x] **Step 5 — Signature monetization (Free + Pro only)** (completed 23.07.2026, see ADR-034)
  - [x] Free: fixed Cedar Clerk attribution signature — "Published with Cedar Clerk", linked to the main site
  - [x] Pro: custom signature text — extends the existing `ApplicationUser.PostSignature` (Phase 6) to a tier-gated version, not built from scratch
  - [x] Links in the signature must be clickable — new `PostSignatureUrl`, single text+URL pair (not per-line rich text)
  - [ ] Pro Plus signature tier — deferred to backlog below, not part of Phase 8
  - [ ] Not yet verified live against `@testingandfun` or on a real blog post
- [x] **Step 6 — Tags** (closed 26.07.2026, see ADR-036)
  - [x] Extend `Draft.Tags` (currently blog-only) to the Telegram export path — `PostEndpoints.BuildHashtagLine`, appended as a trailing hashtag line in `PublishAsync`. **Not yet verified live against `@testingandfun`** — deferred by Marty's choice, do before considering this fully closed
  - [x] Autocomplete from previously used tags — shipped as part of Step 9/ADR-035 below: the tag "cloud" picker (`editor.component.ts:234-236,674-680`, `GET /api/drafts/tags` via `drafts.service.ts:64-65`)
- [x] **Step 7 — Comments improvements** (closed 26.07.2026, see ADR-037)
  - [x] Replies to comments — `Comment.ParentCommentId`, one level of nesting only (by design, see ADR-037)
  - [x] Highlight the channel owner's own comments (and only theirs) — whole-article comment box only, not per-fragment inline annotations (see ADR-037's scoping note)
  - [x] Reserve the owner's display name so visitors can't comment under it — case-insensitive match against `owner.AuthorDisplayName`, 409 on collision, no reservation table
  - [x] Show both the post's publish time and each comment's write time — "Post published: {date}" line above the comment list
  - **Not yet verified live in a browser** — deferred by Marty's choice this session
- [x] **Step 8 — AI progress bar** (closed 26.07.2026, see ADR-038)
  - [x] Progress indicator for in-editor AI operations — was a plain elapsed-time counter, now an asymptotic pseudo-progress estimate (`pseudo-progress.util.ts`) capped at 90% until the real response arrives, jumping to 100% on completion; **not real token-level streaming** (neither AI provider streams today — see ADR-038 for why that was scoped out). Also added, per Marty's ask: elapsed time still shown alongside the %, a 3-minute client-side timeout, and a Cancel button that genuinely aborts the in-flight request
- [x] **Step 9 — Additional shipped work, not part of the original 8-step brainstorm** (16–24.07.2026; backfilled into this checklist 25.07.2026 — these existed only in `docs/DECISIONS.md`/`git log` before, with no ROADMAP tracking at all, which is exactly the kind of drift this file exists to prevent)
  - [x] Public blog view counter (16.07.2026, ADR-023) — `Draft.ViewCount`, shown on the post page and its blog-homepage card; deliberately not the blocked Channel Analysis infra (raw hit count, no dedup, no history)
  - [x] `/stats` per-channel growth dashboard (17.07.2026, ADR-025) — `ChannelPost` publish log + `ChannelStatSnapshot` gains View/Like/Comment counts, new `stats.component`, extends the Phase 4 subscriber-only sparkline
  - [x] Markdown (`.zip`) import (17.07.2026, ADR-026) — hand-rolled scoped parser (`MarkdownToCedarConverter`), `POST /api/drafts/import-markdown`, unsupported blocks degrade to plain text rather than crashing
  - [x] Global exception handling + readable error bodies (19.07.2026, ADR-027) — every endpoint now returns `{ error }` instead of a bodyless 500; extended the empty-container drop guard (ADR-019) to blockquote/toggle/lists/table
  - [x] Debug console panel + Export redesigned as a categorized modal + static HTML export (19.07.2026, ADR-028) — bottom collapsible request/response console (`DebugLogService`), new shared `ModalComponent`, Export modal split into Site/Blog · Telegram · "Другие площадки" (inert placeholders) · file exports, new `GET /api/drafts/{id}/export-html`
  - [x] RU/EN structural diff gutter (19.07.2026, ADR-029) — `DraftTranslation.SourceSnapshotJson` + client-side top-level-block LCS diff, colored gutter bars next to the RU editor (>1200px viewports only)
  - [x] `/stats` gains a channel-agnostic "Blog" tab (19.07.2026, ADR-030) — `BlogStatSnapshot` keyed by `OwnerId`, on-demand first snapshot so the tab isn't empty before the nightly job runs
  - [x] Large photo auto-compression for Telegram sends (19.07.2026, ADR-031) — root-caused a real prod 502 (Telegram rejects URL-fetched photos above ~10MB); `ImageCompressor`/`SixLabors.ImageSharp`, `Asset.TelegramLocalPath` derivative, generated eagerly on upload and lazily on publish
  - [x] Compression-level control, per-draft files list, profile social links (19.07.2026, ADR-032) — export-time small/standard/high compression choice, "Файлы в черновике" list with detach-not-delete, 5 new social-link profile fields (informational only, not yet surfaced publicly), mobile topbar overflow fix
  - [x] YouTube embed block (23.07.2026, ADR-033) — new `youtube` TipTap node, real `<iframe>` on the blog, thumbnail+link on Telegram (no native Blocks link-preview exists)
  - [x] Editor redesign — toolbar customization, Appearance settings, `/drafts` screen, unified Insert modal, tag cloud, New Draft dialog (24.07.2026, ADR-035) — see `TASKS.md` "Editor redesign" entry for the still-open live-verification checklist; 4 CSS regressions from this redesign's "Cedar Aero" glass effect were found and fixed 25.07.2026 (toolbar popups clipped, export modal mispositioned, iPad horizontal scroll — `CHANGELOG.md`)

**Dependency, recorded not scheduled**: Channel Analysis UI needs a data-collection layer (`PostStatSnapshot`, `ReactionEvent`, a view beacon, `message_reaction_count` in the bot's `allowed_updates`) that doesn't exist in the code yet — historical data can't be backfilled. It's excluded from Phase 8 on purpose; a dedicated data-collection phase needs to run first. See `docs/PRD.md` "Blocked" section.

### Phase 9 — Brainstorm sweep — started 26.07.2026, in progress
Executing `_Documents_/CedarClerk/Brainstorm_Features.md` (27 items, Marty's own priorities) in **High → Medium → Low** order. Full item text lives in `docs/BACKLOG.md` under "Brainstorm 26.07.2026"; this is the status checklist. One commit per item, short messages.

**High**
- [x] B22 — Topbar layout (26.07.2026) — brand/divider/drafts/title/save-state left, Export + theme + profile right; `.cedar` download back into Export, import onto `/drafts`; stats/comments moved into the account popover next to Settings. Reversed part of the same-day topbar work, as expected
- [x] B21 — Channels menu moved into the top of the Export window (26.07.2026), outside the `currentId()` guard since connecting a channel isn't draft-specific
- [x] B5 — Export window redesign (26.07.2026): a checkbox per destination gating its settings, one Publish button firing every ticked destination in sequence, file list now shows count + total size
- [x] B24 — `/drafts` table scrolls horizontally (26.07.2026) — `overflow:hidden` (there only to clip rounded corners) was cutting off the fixed-width column grid with no way to reach it
- [x] B25 — Draft state strip above the language tabs (26.07.2026): private/public, LIVE, and links to the live blog/Telegram post
- [x] B14 — Re-translate is now always offered on the EN tab (the stale dot clears itself, which used to leave delete as the only action) and gets the same progress bar + cancel as first-time auto-translate (26.07.2026)
- [ ] B23 — `/drafts` gains view/reaction counts with a since-last-session delta
- [ ] B26 — UI language picker (RU/EN), laid out to survive long-word languages
- [x] B3 — Registration form for private posts (26.07.2026, ADR-042) — per-post configurable form (name/nickname/email/social + custom text/choice questions), shown instead of the 404 when configured, grants access on submit, first rate limit on the public blog. Owner configures it and reads submissions in the Export modal. **Not yet live-verified in a browser**

**Medium**
- [ ] B4 — Export channel selector: no free-text entry, icons
- [ ] B10 — Uniform popup behaviour (backdrop, close only via ✕/action)
- [ ] B11 — Diff markers level with (and covering) the changed region
- [ ] B12 — Ruler overlays the writing area; paragraph numbers actually render
- [ ] B19 — Remaining Insert-group buttons become popups

**Low**
- [ ] B1 stats range slider · B2 channel icons · B6 single settings entry (gear) · B7 iPad email overflow · B8 monochrome YouTube icon · B9 emoji panel (overflow + more emoji) · B13 whitespace-reveal toggle · B15 customization moves into an editor side panel · B16 custom accent + writing-area presets · B17 bold signature · B18 custom YouTube link text · B20 better drafts icon · B27 single AI button with progress + cancel

---

## Backlog

Not-yet-started ideas, deferred items, tech debt, and open questions moved to `docs/BACKLOG.md` (25.07.2026) — kept separate so backlog isn't mixed in with phase status here.
