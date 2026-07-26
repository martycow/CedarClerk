# Product

## What Cedar Clerk is

A self-hosted, write-once-publish-everywhere SaaS for creators who maintain a presence across multiple channels (see ADR-021, `docs/DECISIONS.md`). A web rich-text editor (TipTap) is the spine — a post is written once and published to co-equal destinations: a Telegram channel via a shared bot, a hosted blog page with anchor-based reactions and comments on specific fragments, and (planned) Twitter/Bluesky cross-posting. The blog is not a "Telegram mirror" — it's a first-class output in its own right. Bilingual (RU/EN) posts are a first-class feature, not a bolt-on.

Telegram is currently the most-developed output (furthest along, most battle-tested — see the Bot API 10.2 renderer work in `docs/DECISIONS.md` ADR-018/019) but is not the product identity; the architecture is channel-agnostic at the core (`docs/ARCHITECTURE.md` — "one document, many renderers").

Currently a single-operator product (Marty is both the builder and the first user, running his own Telegram channel and Dev Diary/blog through it) that is being turned into a multi-tenant public SaaS — Phase 6 in `docs/ROADMAP.md`.

## Who it's for

Creators who want to write once and reach readers across several channels at once:
- A better writing/editing experience than any single platform's native composer (rich text, tables, media, formulas, spoilers, etc. — see the TipTap extension list in `docs/ARCHITECTURE.md`)
- A hosted blog as a real destination — not just an archive — with reader engagement (reactions, comments) none of the individual channels offer well on their own
- Scheduled/delayed publishing
- Bilingual content without maintaining two separate workflows
- (Planned) reach beyond Telegram into Twitter/Bluesky without re-writing the post per platform

Not a Telegram-only tool for Telegram-only creators — channel-agnostic by design, even though Telegram is where the most engineering investment has landed so far.

## Pricing (as implemented, `CedarClerk.Core/Consts.cs` + `PlanLimitations.cs` — code-verified 16.07.2026)

| Tier | Price | What it unlocks |
|---|---|---|
| Free | $0 | 1 channel, 200MB asset storage, stats history capped at the last 30 snapshots (`PlanLimitations.MaxChannels`/`StorageLimitBytes` — code-confirmed, not the originally-planned estimate) |
| Pro | $3/mo | Up to 3 channels, 1GB storage, no "Powered by Cedar Clerk" badge — **same 30-snapshot stats cap as Free**: `ChannelEndpoints.cs`'s stats query has no plan check at all, so "full history" was never actually true for any tier |
| Pro Plus | $6/mo | Everything in Pro + AI features (`PlanLimitations.HasAiFeatures`): auto-translate, AI edit (fix errors / "schizo-izer"), daily AI-call quota via `AiUsage`. Up to 10 channels, 5GB storage |
| Trial | $1 one-time | 7 days of Pro Plus, usable once per account (`ApplicationUser.TrialUsedAt`) |
| Founder / Lifetime | one-time, via a designated invite code | Permanent Pro tier, granted at registration through a separate founder invite code — no payment flow, no AI (see ADR-022, `docs/DECISIONS.md`) |

Three payment providers, all code-complete but not yet live in production (waiting on real keys — see `TASKS.md`): Stripe, Telegram Stars, PayPal. Details and setup steps in `docs/integrations-setup.md`; the decision history (including what was *not* built, like PayPal recurring) is in `docs/DECISIONS.md`.

## Open product questions

Carried forward from planning sessions — genuine unknowns, not implementation gaps:
> TODO (Marty): name for the shared Telegram bot (public-facing, used for onboarding every new user's channel).
> TODO (Marty): domain strategy — direction now resolved (ADR-020: separate dedicated domain for tenant blogs, working name `cedarclerk.app`), but three sub-questions remain open: exact domain name; whether `blog.mooexe.dev` migrates or stays Marty's personal blog; subdomain vs. path scheme for tenants.
> TODO (Marty): target market positioning / competitors / success metrics — not yet articulated anywhere in the project's docs or history.
> TODO (Marty): long-term vision beyond the currently-planned Phase 7 (Entertainer role — interactive posts/polls) and Phase 8 (v0.8.0 feature set in `docs/ROADMAP.md`).

Lifetime-deal pricing is resolved: yes, via the Founder/Lifetime invite-code plan (ADR-022, `docs/DECISIONS.md`) — the only open piece is the invite code's actual value.
