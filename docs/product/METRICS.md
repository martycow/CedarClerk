---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: product event dictionary, derivation of the four BUSINESS §4 metrics from data
guard: none
---

# Metrics: event dictionary and how they're calculated

Contract for T-153 (ADR-126): event names are stable, the analytics provider only transports them.
`BUSINESS.md` §4 owns the list of "which metrics matter"; this file owns "exactly how they're
calculated and from what." Updated in the same commit that adds or renames an event.

The core principle is written on the snapshot entities themselves ("History starts the day this
ships"): **a counter with no measurements can't be decomposed retroactively**. What's already being
written is mineable across all history; what isn't will only start existing from the moment it's
wired up.

## 1. What's already being written to the database

Everything below is self-owned, no cookies and no third parties (`VisitorHash` with a salt —
ADR-016; `BlogViewGeoDaily` — an aggregate, not a visit trail). Snapshots are written by
`SnapshotChannelStatsJob` daily at **04:00 UTC** (`Program.cs`, cron `0 0 4 * * ?`).

| Event / series | Where it lives | What it gives | Retroactively |
|---|---|---|---|
| Registration | `AspNetUsers.CreatedAt` | Cohorts by registration date | ✔ full history |
| Draft created | `Draft.CreatedAt` | "Did they reach the editor" | ✔ |
| Publication (each one) | `PublishJob` — append-only, full log, no retention: owner, network, language, status, `FinishedAt` | Who publishes, where and when; first publication | ✔ full history |
| Blog publication | `Draft.BlogPublishedAt` — first-publish semantics (not overwritten) | The post's first appearance in the blog | ✔ |
| Send to Telegram channel | `ChannelPost` — append-only log of successful sends | Linking posts to channels for snapshots | ✔ |
| Post views/reactions | `Draft.ViewCount` + reactions — running totals | Only "how many total right now" | ✘ no measurements |
| Per-post dynamics | `DraftStatSnapshot` — daily per draft | Post growth chart (8.6) | from the launch date |
| Per-blog dynamics | `BlogStatSnapshot` — daily per owner | Trend for the whole blog | from the launch date |
| Per-channel dynamics | `ChannelStatSnapshot` — daily, MemberCount + post aggregate (approximation, ADR-025) | Channel trend, subscribers | from the launch date |
| Reader geography/language | `BlogViewGeoDaily` — (owner, UTC day, country, language) | Where from and in what language people read | from the launch date |
| "New since last visit" | `DraftStatSeen` — baselines per owner×draft (B23) | Deltas in the drafts list | not a metric — UI mechanics |
| AI usage | `AiUsage` — call counter per owner×UTC day | AI spend, cost per Pro Plus | from the launch date |
| Credit movement | `CreditEntry` — ledger, (Reason, Ref) unique | Pack purchases, publication charges | ✔ full history |
| Payment | `Payment` — provider, plan, amount, currency, status | Everything money-related below | ✔ full history |
| Admin actions | `AdminAuditEntry` | Audit of manual interventions (plan, lock, grants) | ✔ |

## 2. What's not being written (will appear only with T-153)

- **Funnel up to registration**: landing page visits, started and abandoned registrations, referral
  source. Today only the outcome is visible — a row in `AspNetUsers`.
- **Feature usage**: which capabilities get touched at all (`.cedar` export, private posts, forms,
  header slots). The only trace is the feature's own data — "opened it and didn't use it" isn't
  visible.
- **An explicit first-publication event** — not needed as a data-rescue measure: it's derived via a
  join (§3.1); in the provider it will become an optimization for live dashboards.

## 3. The four metrics from §4 — derived from what exists

1. **Activation** — the share of registered users who published at least once within the first
   week: `AspNetUsers.CreatedAt` × min(first `PublishJob.FinishedAt` with `Status = Succeeded`,
   `Draft.BlogPublishedAt`) per owner; the numerator counts those with a difference ≤ 7 days.
   Mineable across all history. A supporting metric — **TTFP** (time to first publish): the median
   of the same difference; activation says "how many made it," TTFP says "how long it took."
2. **Free → paid conversion** — from `Payment` (plan, status, `CreatedAt`); the $1 Trial is a
   separate funnel step, not a component of conversion to Pro.
3. **Churn** — from the sequence of `Payment` rows per owner: a paying user with no next charge
   within a month has churned. The primary source for renewals is Stripe (ritual §5 in BUSINESS);
   `Payment` is its local trace, from which the number is reproducible without logging into the
   dashboard.
4. **MRR** — the sum of active subscriptions (the last successful `Payment` on a subscription plan
   within the renewal window); Trial and credit purchases are excluded by the §4 definition.

## 4. Name dictionary for the provider

`snake_case`, action's object first. The provider transports exactly these names; switching
providers doesn't change the dictionary. The "source" column says where the event comes from once
wired up.

| Name | Metric | Source |
|---|---|---|
| `signup_started` | activation (funnel) | client-only — doesn't exist today |
| `signup_completed` | activation, cohorts | `AspNetUsers.CreatedAt` |
| `draft_created` | activation (step) | `Draft.CreatedAt` |
| `post_published` | activation, habit | `PublishJob` (Succeeded) / `BlogPublishedAt` |
| `post_published_first` | activation, TTFP | derivable via the §3.1 join |
| `trial_started` | conversion (step) | `Payment` (trial plan) |
| `plan_purchased` | conversion, MRR | `Payment` (pro/proplus) |
| `plan_renewed` | churn, MRR | `Payment` (repeat) |
| `credits_purchased` | revenue outside MRR | `CreditEntry` (+Delta, purchase) |
| `ai_used` | Pro Plus cost | `AiUsage` |

What's deliberately absent: likes, reach, landing-page traffic as an end in itself — §4 explicitly
forbids measuring the pleasant instead of the decisive while there are fewer than ten paying users.
