# Product Requirements

This is a living requirements skeleton, not a spec written up-front — Cedar Clerk was built phase-by-phase with requirements captured after the fact in `docs/ROADMAP.md`. This file organizes *what the product must do*, derived from what's shipped plus what's explicitly planned; it does not restate implementation detail (see `docs/ARCHITECTURE.md`) or historical rationale (see `docs/DECISIONS.md`).

## Shipped requirements (satisfied — Phases 0–6 and 8, see `docs/ROADMAP.md` for status detail)

**Editor & publishing**
- Rich-text editor (TipTap) with tables, formulas (KaTeX), images/video/audio, spoilers, toggles, footnotes, collages, carousels, date/time inserts, YouTube embeds (thumbnail preview in-editor, real `<iframe>` on the blog, thumbnail+clickable-link on Telegram — see ADR-033)
- Autosave with saved/saving/dirty state, draft list, undo/redo, rename without requiring a page reload
- Editor redesign: customizable two-row toolbar (presets, per-button visibility, drag-and-drop group placement), Appearance settings (accent presets, sheet width/typeface/font size/line height, ruler/paragraph-numbers/focus-mode toggles), full-screen `/drafts` table (filters, archive, search), New Draft dialog, unified Insert modal with clipboard type auto-detection, tag "cloud" picker with autocomplete from previously used tags — see ADR-035
- RU/EN structural diff gutter — colored bars beside the RU editor showing which top-level blocks changed since the English translation was last synced (see ADR-029)
- Export a draft to a connected Telegram channel (immediate or scheduled via Quartz), with a working post link in the result (not a raw message ID); export UI redesigned as a categorized modal (Site/Blog, Telegram, other-platform placeholders, file exports) plus a standalone static single-file HTML export (see ADR-028)
- Export-time photo compression control (small/standard/high) on top of automatic Telegram-safe compression for large camera originals, plus a per-draft "files in this draft" list with detach-not-delete management (see ADR-031/032)
- `.cedar` file export/import (round-trippable, media included)
- Markdown (`.zip`) import for Notion-shaped exports — scoped parser (text/headings/lists/images/basic inline marks); complex Notion blocks (tables, toggles, embeds) degrade to plain text rather than being lost or crashing the import (see ADR-026)
- Multiple connected Telegram channels per (paid) account; auto-discovery of chats the bot is already in
- `/stats` page: per-channel growth charts (subscribers, blog views, likes, comments) — daily snapshots, see ADR-025 for the attribution approximation and no-backfill caveat; plus a channel-agnostic "Blog" tab showing the same views/likes/comments growth totalled across all of the owner's blog-published drafts, see ADR-030 in `docs/DECISIONS.md`
- Bottom collapsible debug console (request/response log, available on every page) so a stuck-looking action or a failed request can be inspected without SSH-ing into the Pi (see ADR-027/028)

**Bilingual content**
- RU (primary) + EN translation per draft, manual or AI-assisted (auto-translate / re-translate), stale-translation indicator, empty-state guidance

**Blog**
- Public blog mirror of published posts (`blog.mooexe.dev`), anchor-based reactions (like/dislike) and comments on specific text fragments, anonymous with abuse-resistant visitor hashing
- Comments: one level of replies, the channel owner's own comments highlighted, the owner's display name reserved (visitors can't post under it), both the post's publish time and each comment's write time shown — see ADR-037. **Not yet live-verified in a browser** as of writing
- Tags with AND-filtering, line-style (git-graph) homepage timeline, post signature appended to both Telegram export and blog page — Free gets a fixed non-removable attribution, Pro can set custom clickable-link text (see ADR-034); tags also extended to the Telegram export path as a trailing hashtag line (see ADR-036, **not yet live-verified against `@testingandfun`**)
- Public view counter per post (raw hit count, not deduped/historical) shown on the post page and its blog-homepage card — **known bug**: the card currently only shows the post's first tag, not all of them, see `docs/BACKLOG.md` idea #8 (see ADR-023)
- Auto-generated Table of Contents (works on both the blog and, via Bot API 10.2 anchor blocks, in Telegram), dividers, "back to top/menu" floating nav, RSS feed at `/rss.xml` (see ADR-024, and the RSS entry in `docs/ROADMAP.md` Phase 8 Step 2)
- Legal pages: Terms of Service and Privacy Policy (`/terms`, `/privacy`) — **structure built, content still placeholder `[BRACKETED]` text**, needs Marty to fill in jurisdiction/entity specifics before public registration can open
- Header Slot System (Pro-gated 3rd slot): article subtitle line built from up to 3 configurable fields (author signature, URL, map location, published date, length, time-to-read) — extensible by design, a new slot type needs no schema/architecture change (see ADR entries around the Header Slot System, `docs/DECISIONS.md`)
- Cross-link from a Telegram post back to its blog version

**Accounts & monetization**
- Email/password auth (invite-code gated registration), optional Telegram account linking (not a login replacement)
- Multi-tenant ownership scoping on every endpoint (see the audit table in `docs/DECISIONS.md`)
- Four-tier plan model (Free/Pro/Pro Plus/Trial) with quota enforcement, three complete payment provider integrations (Stripe/Telegram Stars/PayPal) — **built but not yet live in production**
- Profile social links (Twitter/Instagram/Facebook/YouTube/GitHub) — informational only, not yet surfaced anywhere publicly (see ADR-032)

**AI features**
- In-editor AI edit (fix errors / "schizo-izer" rewrite), gated to Pro Plus, daily quota enforced
- AI operations (AI-edit, auto-translate) show an asymptotic pseudo-progress estimate (not real token streaming — neither provider streams today) alongside elapsed time, a 3-minute client-side timeout, and a Cancel button that genuinely aborts the request (see ADR-038)

## Open requirements — Phase 7 (after Phase 6 closes)
- Interactive posts: polls / A-B choice blocks for subscribers
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
- **Channel Analysis UI, full scope** (per-post `PostStatSnapshot` granularity, real Telegram `message_reaction_count` reactions via `allowed_updates`, poll percentages): still blocked — not built. The channel-growth-chart slice of this (see Shipped below, `/stats`, ADR-025) was unblocked on 17.07.2026 via an approximation (`ChannelPost` publish log + extended `ChannelStatSnapshot`), not the originally-envisioned per-post infra.

Resolved (16.07.2026): no formal acceptance criteria / success metrics for now — the phase checklists in `docs/ROADMAP.md` are the definition of done for this project.
