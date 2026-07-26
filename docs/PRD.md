# Product Requirements

This is a living requirements skeleton, not a spec written up-front — Cedar Clerk was built phase-by-phase with requirements captured after the fact in `docs/ROADMAP.md`. This file organizes *what the product must do*, derived from what's shipped plus what's explicitly planned; it does not restate implementation detail (see `docs/ARCHITECTURE.md`) or historical rationale (see `docs/DECISIONS.md`).

## Shipped requirements (satisfied — Phases 0–6, see `docs/ROADMAP.md` for status detail)

**Editor & publishing**
- Rich-text editor (TipTap) with tables, formulas (KaTeX), images/video/audio, spoilers, toggles, footnotes, collages, carousels, date/time inserts, YouTube embeds (thumbnail preview in-editor, real `<iframe>` on the blog, thumbnail+clickable-link on Telegram — see ADR-033)
- Autosave with saved/saving/dirty state, draft list, undo/redo, rename without requiring a page reload
- Export a draft to a connected Telegram channel (immediate or scheduled via Quartz), with a working post link in the result (not a raw message ID)
- `.cedar` file export/import (round-trippable, media included)
- Markdown (`.zip`) import for Notion-shaped exports — scoped parser (text/headings/lists/images/basic inline marks); complex Notion blocks (tables, toggles, embeds) degrade to plain text rather than being lost or crashing the import (see ADR-026)
- Multiple connected Telegram channels per (paid) account; auto-discovery of chats the bot is already in
- `/stats` page: per-channel growth charts (subscribers, blog views, likes, comments) — daily snapshots, see ADR-025 for the attribution approximation and no-backfill caveat; plus a channel-agnostic "Blog" tab showing the same views/likes/comments growth totalled across all of the owner's blog-published drafts, see the ADR following ADR-029 in `docs/DECISIONS.md`

**Bilingual content**
- RU (primary) + EN translation per draft, manual or AI-assisted (auto-translate / re-translate), stale-translation indicator, empty-state guidance

**Blog**
- Public blog mirror of published posts (`blog.mooexe.dev`), anchor-based reactions (like/dislike) and comments on specific text fragments, anonymous with abuse-resistant visitor hashing
- Tags with AND-filtering, monthly timeline grouping, post signature appended to both Telegram export and blog page
- Cross-link from a Telegram post back to its blog version

**Accounts & monetization**
- Email/password auth (invite-code gated registration), optional Telegram account linking (not a login replacement)
- Multi-tenant ownership scoping on every endpoint (see the audit table in `docs/DECISIONS.md`)
- Four-tier plan model (Free/Pro/Pro Plus/Trial) with quota enforcement, three complete payment provider integrations (Stripe/Telegram Stars/PayPal) — **built but not yet live in production**

**AI features**
- In-editor AI edit (fix errors / "schizo-izer" rewrite), gated to Pro Plus, daily quota enforced

## Open requirements — Phase 7 (after Phase 6 closes)
- Interactive posts: polls / A-B choice blocks for subscribers
- Integration with GDD-style voting for a related project ("Cedar Station")

## Open requirements — Phase 8 / v0.8.0 (see `docs/ROADMAP.md` for full detail and ordering)
- Blog bugfixes: duplicated En title, missing-translation fallback, full-UI language switch (not just article text), dividers, anchors, "go to top/menu", line-style timeline
- RSS feed
- Legal pages (Terms of Service, Privacy Policy) — **hard prerequisite for opening public registration**
- Header Slot System (Pro-gated 3rd slot; extensibility is an explicit non-negotiable requirement — new slot types must not require architectural changes)
- Signature monetization: Free gets a fixed Cedar Clerk attribution, Pro gets custom clickable-link signature text
- Tags extended to the Telegram export path (currently blog-only) + autocomplete
- Comments: replies, author-comment highlighting, nickname reservation for the channel owner's name, dual timestamps (post publish time + comment write time)
- AI progress bar (currently only an elapsed-time counter)
- Accounts & monetization: founder lifetime plan via a designated invite code — grants a permanent Pro tier at registration, reusing the existing invite-code infra (see ADR-022, `docs/DECISIONS.md`)

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
