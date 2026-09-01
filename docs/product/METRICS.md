---
owner: marty
last_verified: 2026-09-01
source_of_truth_for: product event dictionary, derivation of the four BUSINESS §4 metrics from data
guard: none
---

# Metrics: event dictionary and how they're calculated

Contract for T-153 (ADR-126/236): event names are stable, the analytics provider only transports them.
The provider is live since 01.09.2026 — PostHog, EU cloud, behind a consent banner (ADR-236).
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

## 2. What the provider adds, and what is still not written

The provider is **PostHog, EU cloud** (ADR-236). Events are captured where they happen, so history
through it starts the day it ships — the four §4 metrics below are unaffected, because they are
derived from the database and always were.

Covered now:

- **Funnel up to registration**: `signup_started` fires from the register form, so an attempt that
  was refused is visible; previously only the outcome was — a row in `AspNetUsers`. Traffic source
  comes from the landing's page views.
- **An explicit first-publication event**: `post_published_first` is written beside
  `post_published`, decided by "no earlier succeeded job for this owner". Still not a data-rescue
  measure — §3.1's join remains the source of truth and covers all history.
- **AI refusals**: `ai_used` carries `outcome` (`charged` / `no_credits` / `daily_limit`), because
  "asked and was turned away" is what says whether the limits sit where they should.

Still not written:

- **Feature usage**: which capabilities get touched at all (`.cedar` export, private posts, forms,
  header slots). The only trace is the feature's own data — "opened it and didn't use it" isn't
  visible.
- **Anything about blog readers.** Deliberate, not pending: their statistics are our own and
  cookie-free (§1), and no third-party script goes on a tenant blog.

### Consent

Nothing reaches PostHog until the visitor accepts (ADR-236 clause 7): the library is not loaded at
all before an answer, and the answer lives in one `cedar_consent` cookie shared by the landing and
the app. **A declining visitor is therefore absent from every event above**, which is a real gap in
the funnel numbers and not a bug — read activation and conversion off the database (§3), and read
PostHog for the shape of what happens before an account exists.

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
providers doesn't change the dictionary. The names are `Consts.Analytics.Events` in code (ADR-236
clause 4) — a contract spread across string literals is one a rename silently breaks. "Where it
fires" is the call site; "properties" are the dimensions it carries.

| Name | Metric | Where it fires | Properties |
|---|---|---|---|
| `signup_started` | activation (funnel) | `register.component.ts` — the only client-side event | `invited` |
| `signup_completed` | activation, cohorts | `AuthEndpoints` after `CreateAsync` | `entry` (invitation / invite_code / config_code / open) |
| `draft_created` | activation (step) | `DraftEndpoints` `POST /` — the empty-handed create only, never a copy or an import | — |
| `post_published` | activation, habit | `PublishJobRunner`, on `Succeeded` | `network`, `language`, `threaded` |
| `post_published_first` | activation, TTFP | same, when no earlier succeeded job exists for the owner | same |
| `trial_started` | conversion (step) | Stripe checkout, PayPal capture, Stars payment | `plan`, `provider` |
| `plan_purchased` | conversion, MRR | same three | `plan`, `provider` |
| `plan_renewed` | churn, MRR | Stripe `invoice.paid` (`subscription_cycle`) | `plan`, `provider` |
| `credits_purchased` | revenue outside MRR | Stripe checkout, Stars payment — against the **owner**, never the payer (T-359 finding 7) | `credits`, `pack`, `provider` |
| `ai_used` | Pro Plus cost | `SubscriptionPlan.ChargeAiOrRefuseAsync`, the one gate every AI call passes | `kind`, `credits`, `outcome` |

The distinct id is the account id and nothing else: no email, no display name, no post content
(ADR-236 clause 6).

What's deliberately absent: likes, reach, landing-page traffic as an end in itself — §4 explicitly
forbids measuring the pleasant instead of the decisive while there are fewer than ten paying users.
