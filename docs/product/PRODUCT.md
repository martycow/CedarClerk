---
owner: marty
last_verified: 2026-09-02
source_of_truth_for: what the product is, who it's for, pricing snapshot
guard: none
---

# Product

> **Turning point, 02.09.2026 — Cedar Clerk is a platform for independent makers.** Projects,
> Devlogs and personal Blogs are three first-class ways to publish work; Discovery gives people who
> opt in a shared place to be found. "Maker" is deliberately broad: games, apps, tools, comics,
> illustration, film, animation, music, audio and hardware all fit.
>
> The 10.08 indie-game-developer turn remains a shipped specialist module, not the platform's outer
> boundary. It adds Projects, document types, tasks, sprints, builds, the asset index and desktop
> tooling behind `Cedar:Modules:IndieDev` (ADR-101); it does not make a Project mandatory for a Blog.

## Who it's for

An independent developer or creator who makes something and wants one durable home for explaining
the work: a solo game developer, an app or tool builder, an artist, filmmaker, musician, hardware
maker, or a person whose work is simply a Blog. One person may still hold the roles of programmer,
designer, producer, writer, composer and marketer; Cedar Clerk serves that working pattern rather
than requiring one industry label.

The common need is not a specific medium. It is to publish progress, keep an owned archive, reach
several networks without rewriting the same update, and optionally let new readers discover the
public result.

The product name stays **Cedar Clerk** (Q-17): the name now carries the platform while each module
can speak to its own craft.

## What Cedar Clerk is

A hosted publishing and project-presence SaaS for independent makers. A web rich-text editor (TipTap)
is the spine: write once, publish to Telegram, a hosted Blog, X and Bluesky, and keep the owned page
as the durable source. A Project adds Devlogs, planning and a public Showcase; an independent Blog
needs no Project. Discovery is an opt-in lens over work that is already public (ADR-243). True
self-hosting remains a future option (`docs/product/MULTITENANCY.md` §4, `T-151`). Multilingual
content is first-class: **nine content languages** (`Languages.ContentLanguages`) with a per-Draft
primary language (ADR-064/065) and AI auto-translate.

Telegram is currently the most-developed output (furthest along, most battle-tested — see the Bot API 10.2 renderer work in `docs/DECISIONS.md` ADR-018/019) but is not the product identity; the architecture is channel-agnostic at the core (`docs/tech/ARCHITECTURE.md` — "one document, many renderers").

Currently a single-operator product (Marty is both the builder and the first user). The multi-tenant
machinery is code-complete — ownership filtering, quotas and billing — but **registration stays
invite-only on purpose** until the gates in `docs/product/BUSINESS.md` §2 close. Strangers meet the
server-rendered landing at `/`; `/discovery` introduces opted-in public Projects, Devlogs and Blogs.

## What the publishing half offers

Write once, reach readers across several channels at once:
- A better writing/editing experience than any single platform's native composer (rich text, tables, media, formulas, spoilers, etc. — see the TipTap extension list in `docs/tech/ARCHITECTURE.md`), drawn in one look, Cedar Bench — a workshop of paper, wood, pine and brass, with light and dark as the whole of the choice (ADR-136)
- A hosted blog as a real destination — not just an archive — with reader engagement (reactions, comments) none of the individual channels offer well on their own
- Scheduled/delayed publishing, for every connected network
- Multilingual content without maintaining parallel workflows
- Reach beyond Telegram into X and Bluesky without re-writing the post per platform — shipped; itch.io/Steam devlog channels are the researched next step (`T-127`)

Not a Telegram-only tool for Telegram-only creators — channel-agnostic by design, even though Telegram is where the most engineering investment has landed so far.

## Pricing (as implemented, `CedarClerk.Core/Consts.cs` + `PlanLimitations.cs` — code-verified 18.08.2026)

| Tier | Price | What it unlocks |
|---|---|---|
| Free | $0 | 1 channel, 100MB asset storage, stats history capped at the last 30 snapshots (`PlanLimitations.MaxChannels`/`StorageLimitBytes` — code-confirmed, not the originally-planned estimate) |
| Pro | $3/mo | Up to 3 channels, **1GB** storage, no "Powered by Cedar Clerk" badge — **same 30-snapshot stats cap as Free**: `ChannelEndpoints.cs`'s stats query has no plan check at all, so "full history" was never actually true for any tier |
| Pro Plus | $6/mo | Everything in Pro + AI features (`PlanLimitations.HasAiFeatures`): auto-translate, AI edit (fix errors / "schizo-izer"), daily AI-call quota via `AiUsage`. Up to 10 channels, **3GB** storage |
| Trial | $1 one-time | 7 days of Pro Plus, usable once per account (`ApplicationUser.TrialUsedAt`) |
| Founder / Lifetime | one-time, via a designated invite code | Permanent Pro tier, granted at registration through a separate founder invite code — no payment flow, no AI (see ADR-022, `docs/DECISIONS.md`) |

> Quotas are sized to the droplet's disk (ADR-129, `docs/product/MULTITENANCY.md` §1). The remaining registration blocker in `T-172` is step 2: media into object storage, so the disk stops being the ceiling.

Beside the subscription tiers there is a **prepaid credit wallet** (ADR-092, live): packs of 10/$4, 50/$18, 100/$30 (Stars 200/900/1500), bought in Settings → Credits, spent 1 credit per X post — the pattern every metered cost is meant to move to (`T-152` proposes moving all AI calls onto it, since Pro Plus's flat AI quota has unproven margin — `docs/product/BUSINESS.md` §3).

Payment providers: **Stripe is live and proven with real money** (first real payment 26.07.2026, subscriptions and credit packs both); Telegram Stars and PayPal are code-complete but have never processed a live payment. Details and setup in `docs/for_user/integrations-setup.md`; the decision history (including what was *not* built, like PayPal recurring) is in `docs/DECISIONS.md`.

## Open product questions

Carried forward from planning sessions — genuine unknowns, not implementation gaps:
> TODO (Marty): name for the shared Telegram bot (public-facing, used for onboarding every new user's channel).
> TODO (Marty): domain strategy — direction now resolved (ADR-020: separate dedicated domain for tenant blogs, working name `cedarclerk.app`), but three sub-questions remain open: exact domain name; whether `blog.mooexe.dev` migrates or stays Marty's personal blog; subdomain vs. path scheme for tenants.
> ~~TODO (Marty): target market positioning~~ — **answered 02.09.2026**: independent makers, with
> indie game development as the deepest specialist module rather than the whole addressable audience.
> Competitors and success metrics remain unarticulated.
> ~~TODO (Marty): long-term vision beyond Phase 7/8~~ — **answered 02.09.2026**: an owned publishing
> home plus optional public discovery, extended by craft-specific project modules.
> ~~TODO (Marty): product name~~ — **answered 18.08.2026**: the name stays Cedar Clerk (Q-17 closed), the focus lives in a subtitle.

Lifetime-deal pricing is resolved: yes, via the Founder/Lifetime invite-code plan (ADR-022, `docs/DECISIONS.md`) — the only open piece is the invite code's actual value.
