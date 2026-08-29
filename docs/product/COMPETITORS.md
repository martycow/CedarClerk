---
owner: marty
last_verified: 2026-08-29
source_of_truth_for: competitor landscape — who else exists per segment, what Cedar Clerk takes and refuses
guard: none
---

# Competitors

Two research passes feed this file: the 13.08.2026 gamedev-tools market analysis (absorbed into ~18
board rows before this file existed — the report itself lived outside the repo, which was T-192) and
the 29.08.2026 five-agent deep research behind Wave 1 "Reach" (four web reports: social schedulers,
blog/newsletter platforms, indie-gamedev marketing, Telegram tools). This is a landscape, not a
feature matrix: per segment, who they are and what Cedar Clerk deliberately takes or refuses — with
the reason, because a refusal without one gets rebuilt by the next session. Claims here are dated to
those passes; re-verify before building on a specific one.

## Social schedulers — Buffer, Typefully, Hypefury, Publer, SocialBee, FeedHive, Postiz, Mixpost

Write-short-post → queue → cross-post tools. Buffer and Publer are the generalists: every network,
a calendar, team features. Typefully is the writing-first one — a clean editor, thread splitting,
and shareable draft preview links its users name as the beloved feature. Hypefury, SocialBee and
FeedHive are queue-and-recycle engines: named weekly slots, category buckets, evergreen re-sends.
Postiz and Mixpost are the open-source self-hosted pair — proof the scheduling core is a commodity.
**Takes:** draft preview links (shipped, Wave 1); queue slots + evergreen recycling and the content
calendar (Wave 2); best-time hints from own snapshots, no ML. **Refuses:** a unified engagement
inbox (a maintenance trap across N unstable APIs) and bundled AI writing credits (margin) — the
center here is the document and its renderers, not the queue.

## Blog and newsletter platforms — Ghost, Substack, beehiiv, Buttondown, Hashnode, Medium, Bear

Ghost is the self-hostable publishing suite: memberships, email, themes. Substack and beehiiv are
newsletters-first with growth mechanics (recommendation networks, boosts) and reader monetisation.
Buttondown is the small careful email tool; Hashnode is dev-blogging with auto-generated OG images
and custom domains; Medium is the walled garden; Bear is the minimal fast blog. **Takes:** the
found-on-the-web basics this segment treats as table stakes — full-text search, auto OG images (the
Hashnode/beehiiv pattern), sitemap + JSON-LD, related posts — and blog-wide subscription with double
opt-in (all Wave 1). **Refuses:** a broadcast composer and campaigns (ADR-225 — notify-on-publish
only: unbounded email is a margin and deliverability problem a $3–6 plan cannot carry) and paid
reader subscriptions (a second billing product before the first has users).

## Indie-gamedev marketing — presskit(), Keymailer, Gamalytic, the Steam/itch ecosystem

presskit() (dopresskit) is the abandoned de-facto press-page standard everyone still complains
about — the niche is empty. Keymailer distributes review keys to creators; Gamalytic estimates Steam
sales from public data; and the storefronts' own surfaces — Steam news via the partner site, itch.io
devlogs — are where players actually read, with no public write API on either. **Takes:** the press
kit page (T-128 closed in Wave 1 — a renderer over showcase data, not a document to maintain) and
Steam BBCode / itch HTML as clipboard copy targets (ADR-223). **Refuses:** Steam/itch auto-posting
(ToS — the ban risk lands on the account that owns the game), key distribution and influencer
outreach (a different business), and sales-analytics scraping (Gamalytic already exists, with
ToS-gray problems of its own).

## Telegram tools — ControllerBot, Telepost, Fleep, TGStat, InviteMember

ControllerBot, Telepost and Fleep are channel-admin posting bots: scheduling, formatting, per-channel
signatures, silent posts, pinning. TGStat is the market-wide channel-analytics catalog, built on
MTProto. InviteMember sells paid channel memberships. **Takes:** the ControllerBot-class conveniences
a structured editor does better — per-channel signature/footer, silent posts, scheduled pin with
auto-unpin (Wave 2) — and invite-link source analytics with a churn digest: the one under-served
niche, invisible to TGStat and buried by native stats, reachable through the Bot API updates the bot
already receives. **Refuses:** a market-wide analytics catalog (MTProto, ToS-gray — the bot stays a
Bot API citizen) and mass-DM / member export / engagement farming in any form.

## Gamedev project tools — Codecks, HacknPlan, Anchorpoint, IndieViral (13.08.2026 pass)

The adjacent segment: managing the game, not marketing it. Codecks is card-deck project management
with a public roadmap as its main community tool and a Unity playtest SDK. HacknPlan attaches task
boards to a GDD — and is the pricing anti-example: under 1% conversion over 11 years of a
too-generous free tier. Anchorpoint is artist-friendly file/version management whose main
acquisition channel is SEO comparison articles. IndieViral automates indie marketing around a
750-event calendar. **Takes:** the public Cedar Clerk roadmap (T-162), the SEO comparison blog
(T-163), playtest-feedback-to-tasks (T-167), publishing-discipline nudges (T-166), and the pricing
rule "give value free, charge for scale". **Refuses:** a Unity SDK (expensive for one integration)
and XP-style gamification — quiet mechanics instead.

## Name collisions

Recorded here because the knowledge was external and never surfaced in the repo (verified by grep,
18.08.2026): **Clerk.com** (the auth SaaS) and **AWS Cedar** (the authorization policy language)
both sit near the name. Q-17 is closed — the name stays Cedar Clerk; the focus goes into a subtitle,
not a rename.

## Anti-features — what the research says not to build

- Steam/itch auto-posting — ToS; the ban risk lands on the account that owns the game. Copy targets instead (ADR-223).
- Unified cross-network engagement inbox — a maintenance trap.
- AI image/video generation with bundled credits — margin risk, and the gamedev audience is hostile to it.
- Market-wide Telegram analytics catalog — MTProto, ToS-gray.
- Channel stories — business-account only in the Bot API.
- ActivityPub federation, an algorithmic social feed, an ad marketplace — scale mismatch.
- Mass-DM, member export, engagement farming — in any form.
