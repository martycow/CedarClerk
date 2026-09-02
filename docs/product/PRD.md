---
owner: marty
last_verified: 2026-09-02
source_of_truth_for: requirement invariants, non-requirements, blocked items
guard: none
---

# Product Requirements

A thin requirements skeleton, slimmed on 18.08.2026 after the docs audit: this file
had the project's worst drift record (statuses corrected 10.08 and again 18.08), because it
duplicated shipped-feature enumerations that `docs/tasks/CHANGELOG.md` already tracks. What remains here is
what no other file holds: **requirement-level invariants** (what must stay true, not what was
built when), the explicit non-requirements, and the blocked items. For "what shipped and when" read
`docs/tasks/CHANGELOG.md`; for "what the product is" read `docs/product/PRODUCT.md`; for the indie module
`docs/product/INDIEDEV.md`; for hard invariants that have bitten before, `.claude/rules/*.md`.

## Requirements the product must keep satisfying

- **One document, many renderers.** A post is authored once (TipTap JSON in `Draft.CedarJson`) and
  every surface — Telegram, blog, X/Bluesky short posts and threads, `.cedar`/HTML/zip exports —
  renders from it. No surface gets hand-maintained parallel content (per-target *override text* is
  an authored variant, not a fork). Renderer invariants (escaping, per-mark tests):
  `.claude/rules/renderers.md`.
- **A draft is never silently lost.** Autosave with guards: the server refuses a save that wipes the
  text, conflicts 409 on concurrent edits, revisions are kept and restorable, and a restore is
  itself undoable (ADR-065/066/067 — the most incident-hardened code in the project).
- **Multilingual is first-class.** Nine content languages, primary language chosen per draft; every
  reader-facing surface follows the language (blog `?lang=`, per-language channels, forms,
  signatures, cross-links). Translations are assistable by AI but never silently overwrite manual
  corrections (incremental re-translation).
- **The blog is a first-class destination**, not a mirror: reactions and comments on fragments,
  tags, RSS, view/geo stats without storing raw IPs.
- **Discovery never widens publication by inference.** An account must opt in, a Project must have a
  live Showcase, and a post must already be public. Private posts, including tenant-local
  `IsListedWhilePrivate` teasers, never enter the cross-account feed. Independent Blogs remain
  eligible without a Project (ADR-243).
- **Private posts stay private on every path**: registration-form gate, revocable access, watermark
  and copy-protection on the page — and any new public surface (OG tags, media endpoints — `T-174`,
  `T-088`) must decide its private-post behaviour *before* shipping.
- **Ownership scoping on every endpoint.** Every owner-scoped row filters by `OwnerId`; the public
  blog filters by `IsBlogPublished` instead. Audit reference: the ownership-audit table kept with
  the ADR log (`docs/DECISIONS.md`).
- **Paid features are enforced server-side**, not hidden client-side: plan quotas
  (`PlanLimitations`), AI gating with a daily quota, the prepaid credit wallet for per-use costs
  (X posting today; `T-152` proposes all AI follows).
- **Registration stays invite-only** until the `docs/product/BUSINESS.md` §1 gates close.
- **The indie module adds, never replaces** (ADR-101): everything above holds with
  `Cedar:Modules:IndieDev` off, and working-material document types refuse to publish.

## Open requirements — Phase 7 (Entertainer role; the one phase never started)

- Interactive posts for subscribers: Telegram-native polls / A-B choice blocks (polls shipped
  **blog-only** by decision, ADR-055 — the Telegram half is what remains)
- Integration with GDD-style voting for a related project ("Cedar Station")

## Explicit non-requirements (deferred by decision, not oversight)

- Pro Plus signature tier (rich links etc.) — three signature tiers before a user base exists was
  judged premature
- Emoji as a header-slot type — breaks the automatic-slot model
- Comment translation via AI — waits on AI metering (`T-152`)
- General "redesign" as a monolithic item — refused; concrete pains only (ADR-070 precedent)
- Text alignment in the editor — needs evaluation against Telegram HTML limits first
- PayPal recurring billing — deliberately not built (ADR-013)
- Multi-user projects / collaboration — `Assignee` stays a free string; nobody asked yet

## Blocked (infrastructure prerequisite, not simply deferred)

- **Real Telegram reaction counts** via `message_reaction_count` in the bot's `allowed_updates` —
  the one slice of Channel Analysis still unbuilt; everything else arrived piecewise (growth charts
  ADR-025, per-post daily snapshots since 01.08 — no earlier data exists, `T-105` — poll
  percentages ADR-055, geo rollups ADR-097).

Resolved (16.07.2026): no formal acceptance criteria / success metrics — the phase checklists in
`docs/tasks/CHANGELOG.md` (history through Phase 13: `docs/archive/roadmap-phases-0-13.md`) are the
definition of done for this project.
