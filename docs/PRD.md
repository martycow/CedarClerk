# Product Requirements

This is a living requirements skeleton, not a spec written up-front — Cedar Clerk was built phase-by-phase with requirements captured after the fact in `docs/ROADMAP.md`. This file organizes *what the product must do*, derived from what's shipped plus what's explicitly planned; it does not restate implementation detail (see `docs/ARCHITECTURE.md`) or historical rationale (see `docs/DECISIONS.md`).

## Shipped requirements (satisfied — Phases 0–6, 8, 10–13, see `docs/ROADMAP.md` for status detail; latest shipped: v0.12.0, 18.08.2026)

**Editor & publishing**
- Rich-text editor (TipTap) with tables, formulas (KaTeX), images/video/audio, spoilers, toggles, footnotes, collages, carousels, date/time inserts, YouTube embeds (thumbnail preview in-editor, real `<iframe>` on the blog, thumbnail+clickable-link on Telegram — see ADR-033)
- Autosave with saved/saving/dirty state, draft list, undo/redo, rename without requiring a page reload
- Editor redesign: customizable two-row toolbar (presets, per-button visibility, drag-and-drop group placement), Appearance settings (accent presets, sheet width/typeface/font size/line height, ruler/paragraph-numbers/focus-mode toggles), full-screen `/drafts` table (filters, archive, search), New Draft dialog, unified Insert modal with clipboard type auto-detection, tag "cloud" picker with autocomplete from previously used tags — see ADR-035
- Structural diff gutter — colored bars beside the primary-language editor showing which top-level blocks changed since a translation was last synced (see ADR-029)
- Export a draft to a connected Telegram channel (immediate or scheduled via Quartz), with a working post link in the result (not a raw message ID); export UI is the version → destinations → per-destination-settings window (ADR-096) covering Telegram, **X and Bluesky (both live)**, the blog and file exports, plus a standalone static single-file HTML export (see ADR-028)
- **X and Bluesky cross-posting** (ADR-077/079/092/093): standalone short post or whole-document thread (`MicroThreadSplitter`, ADR-094), per-target and per-language override text, images to Bluesky with alt text, OAuth PKCE for X / app password for Bluesky, connections managed in Settings → Integrations (ADR-095). X posting costs 1 credit from the **prepaid credit wallet** (ADR-092 — packs bought via Stripe/Stars, ledger, admin adjustments)
- One post to several destinations at once: a **Telegram channel chosen per language version** so a translation reaches its own channel in the same publish (ADR-098); **scheduling as a step of the export window covering every network**, not Telegram only (ADR-099 — threads are excluded and said so, the blog publishes immediately); **X/Bluesky picking which versions they post**, with the X credit estimate following that choice (ADR-100)
- Export-time photo compression control (small/standard/high) on top of automatic Telegram-safe compression for large camera originals, plus a per-draft "files in this draft" list with detach-not-delete management (see ADR-031/032)
- `.cedar` file export/import (round-trippable, media included)
- Markdown (`.zip`) import for Notion-shaped exports — scoped parser (text/headings/lists/images/basic inline marks); complex Notion blocks (tables, toggles, embeds) degrade to plain text rather than being lost or crashing the import (see ADR-026)
- Multiple connected Telegram channels per (paid) account; auto-discovery of chats the bot is already in
- Stats (a Posts Manager tab; `/stats` redirects there): per-channel growth charts (subscribers, blog views, likes, comments) — daily snapshots, see ADR-025 for the attribution approximation and no-backfill caveat; plus a channel-agnostic "Blog" tab showing the same views/likes/comments growth totalled across all of the owner's blog-published drafts, see ADR-030 in `docs/DECISIONS.md`. The Blog tab also splits the period's views by reader country and reader language (`CF-IPCountry` + `Accept-Language` into a daily rollup, ADR-097) — blog only, since the Bot API reports no geography per channel, and with the same no-backfill caveat
- Bottom collapsible debug console (request/response log, available on every page) so a stuck-looking action or a failed request can be inspected without SSH-ing into the server (see ADR-027/028)

**Multilingual content**
- **Nine content languages** — RU, EN, DE, FR, ES, JA + UK, BE, KA (`CedarClerk.Localization/Languages.cs`; the last three added 30.07 with a provider-capability check, since DeepL has no Belarusian or Georgian), and the primary language is **chosen per draft**, not fixed to Russian (ADR-064/065). `Draft.CedarJson` is the primary version; every other language is a `DraftTranslation` row.
- Translations are manual or AI-assisted (auto-translate / re-translate), with a stale-translation indicator and empty-state guidance. Re-translation is incremental: hand-corrected paragraphs survive a resync, because `DraftTranslation.SourceSnapshotJson` records what the source looked like when the translation was last in sync.
- Per-language surfaces all the way out: the blog's `?lang=`, a Telegram channel chosen per language version (ADR-098), a registration form per language, cross-link labels and the post signature per language.
- *(This section described "RU primary + EN translation" until 10.08.2026 — the known staleness flagged in `docs/DOCS-FLOW.md`, fixed here.)*

**Blog**
- Public blog mirror of published posts (`blog.mooexe.dev`), anchor-based reactions (like/dislike) and comments on specific text fragments, anonymous with abuse-resistant visitor hashing
- Comments: one level of replies, the channel owner's own comments highlighted, the owner's display name reserved (visitors can't post under it), both the post's publish time and each comment's write time shown — see ADR-037. Running in production
- Tags with AND-filtering, line-style (git-graph) homepage timeline, post signature appended to both Telegram export and blog page — Free gets a fixed non-removable attribution, Pro can set custom clickable-link text (see ADR-034); tags also extended to the Telegram export path as a trailing hashtag line (see ADR-036)
- Public view counter per post (raw hit count, not deduped/historical) shown on the post page and its blog-homepage card, which lists every tag (see ADR-023)
- **Private posts and reader access**: per-post private/semi-public state, a configurable registration form as the gate (per-language, presets, consent field — ADR-042/047/048/060), email invites, revocable registrations, tiled watermark and copy protection on private pages (ADR-063), and reader-country/language stats without storing raw IPs (ADR-097)
- Auto-generated Table of Contents (works on both the blog and, via Bot API 10.2 anchor blocks, in Telegram), dividers, "back to top/menu" floating nav, RSS feed at `/rss.xml`, offered by a button in the blog header since 09.08.2026 rather than only by the `<link rel="alternate">` in `<head>` (see ADR-024, and the RSS entry in `docs/ROADMAP.md` Phase 8 Step 2)
- Legal pages: Terms of Service and Privacy Policy (`/terms`, `/privacy`) — **filled in 13.08.2026** (operator Viacheslav Chudaev / Moo.exe, Oregon; age 16+; contact `cedarworks@mooexe.dev`; GDPR/CCPA rights block); **not lawyer-reviewed**, which stays a gate before public registration (`docs/BUSINESS.md` §1)
- Header Slot System (Pro-gated 3rd slot): article subtitle line built from up to 3 configurable fields (author signature, URL, map location, published date, length, time-to-read) — extensible by design, a new slot type needs no schema/architecture change (see ADR entries around the Header Slot System, `docs/DECISIONS.md`)
- Cross-link from a Telegram post back to its blog version

**Accounts & monetization**
- Email/password auth (invite-code gated registration), optional Telegram account linking (not a login replacement)
- Multi-tenant ownership scoping on every endpoint (see the audit table in `docs/DECISIONS.md`)
- Four-tier plan model (Free/Pro/Pro Plus/Trial) with quota enforcement; **Stripe live and proven with real money** (26.07.2026, subscriptions and credit packs), Telegram Stars and PayPal code-complete but never exercised with live money. Registration stays invite-only until the `docs/BUSINESS.md` §1 gates close (`T-172` quotas-vs-disk chief among them)
- **Admin panel** (`/admin`, since 27.07.2026): user/plan management, invite-code entity with attribution, cross-owner post list, payments/storage/AI reporting, append-only audit log, credit adjustments — see ADR-122
- Server-rendered **marketing landing** at `/` for signed-out visitors (`LandingEndpoints`, prices from `PlanLimitations`) — RU-first today; devlog-first EN + waitlist is `T-154`
- Profile social links (Twitter/Instagram/Facebook/YouTube/GitHub) — informational only, not yet surfaced anywhere publicly (see ADR-032)

**AI features**
- In-editor AI edit (fix errors / "schizo-izer" rewrite), gated to Pro Plus, daily quota enforced
- AI operations (AI-edit, auto-translate) show an asymptotic pseudo-progress estimate (not real token streaming — neither provider streams today) alongside elapsed time, a 3-minute client-side timeout, and a Cancel button that genuinely aborts the request (see ADR-038)

## Shipped requirements — indie-gamedev module (Phase 13; MUST list complete 11.08.2026)

Decided 10.08.2026 from Marty's brief; full scope in `docs/INDIEDEV.md`, decisions in ADR-101…107 (desktop reshaped by ADR-117). A post is one document type among several, living inside a **project** (a game). All of v1 shipped, in the planned order:
- `Project` as a container, `Draft.DocumentType` (six types; working material refuses to publish), documents attached to a project (T-120)
- A desktop application — since ADR-117 an Electron window onto production plus a **local filesystem agent** (`/agent/*`, bearer token, granted roots); no second database. What remains open: installing the installer on a clean machine (`T-121` note) and code signing (`T-145`)
- Asset index — paths, metadata and thumbnails pushed by the agent; bytes never leave the machine (ADR-107/117), `.blend` previews parsed natively (T-122/T-140/T-141)
- Task Tracker on `GameTask` + `EntityLink` (T-123), Development Planner with date-derived sprint state (T-124), project-scoped glossary (T-125), builds with a changelog-document generator (T-126)

May follow (`T-128…T-135` on the board): Press Kit, references board (likely absorbed by the Miro-style board `T-155`), brainstorm sessions, script and plot writing tools, game-design helpers, workflow planner, code documentation, budget maths — plus new publishing targets (itch.io, Steam, IndieDB, LinkedIn), each blocked on `T-127`'s research into whether the third party offers a write API at all.

## Open requirements — Phase 7 (after Phase 6 closes)
- Interactive posts: polls shipped **blog-only** (ADR-055 — no Telegram surface by decision); what stays open is Telegram-native polls / A-B choice blocks for subscribers
- Integration with GDD-style voting for a related project ("Cedar Station")

**Phase 8 / v0.8.0 is closed** (26.07.2026, see `docs/ROADMAP.md` for the full step-by-step history) — all shipped work folded into the "Shipped requirements" sections above. What's next lives in `docs/BACKLOG.md`, not here.

## Explicit non-requirements (deferred by decision, not oversight — see `docs/ROADMAP.md` §8 and `docs/DECISIONS.md`)
- Pro Plus signature tier (rich links etc.) — three signature tiers before a user base exists was judged premature
- Emoji as a header-slot type — breaks the automatic-slot model
- Comment translation via AI — blocked on a not-yet-built AI-credit metering system
- General "redesign" — refused as a monolithic item; must be broken into concrete pain points first
- Text alignment in the editor — needs evaluation against Telegram HTML export limits before it can be scoped
- PayPal recurring billing — deliberately not built (see ADR-013 in `docs/DECISIONS.md`)

## Blocked (infrastructure prerequisite, not simply deferred)
- **Real Telegram reaction counts** via `message_reaction_count` in the bot's `allowed_updates` — the one slice of Channel Analysis still unbuilt. The rest arrived piecewise: channel growth charts 17.07 (ADR-025), per-post daily snapshots 01.08 (`DraftStatSnapshot` — no data exists before that date, `T-105`), poll result percentages with blog polls (ADR-055), geo/language rollups 08.08 (ADR-097).

Resolved (16.07.2026): no formal acceptance criteria / success metrics for now — the phase checklists in `docs/ROADMAP.md` are the definition of done for this project.
