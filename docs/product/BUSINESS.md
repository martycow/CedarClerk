---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: registration gates, unit economics, metrics, rituals
guard: none
---

# Business: checklists, metrics and rituals

What needs to be done for Cedar Clerk to earn money, not just work. Product, audience and pricing
tiers are in `docs/product/PRODUCT.md`; this file covers only the operational side.

Rule of this file: **every line is either verifiable, or marked as a decision for Marty.** If an
item can't be checked with a command or opened in someone else's dashboard — it isn't an item, it's
wishful thinking.

---

## 1. Before opening public registration

Registration is currently closed: `Cedar:Registration:Open` is off, entry only by invite code
(`AuthEndpoints.cs`). This is the right state — it's worth opening once the whole list below is
closed.

| | Item | How to verify | Status |
|---|---|---|---|
| 1 | Terms and Privacy have no placeholders | `/terms`, `/privacy` — not a single `[BRACKETS]` | ✅ 13.08.2026 (`T-052`) |
| 2 | Lawyer has read both documents | — | ❌ not done, the only remaining legal item |
| 3 | Nightly database backup | newest `data/backups/cedar-*.db.gz` < 36 h + healthchecks green | ✅ (`T-071`) |
| 4 | Off-droplet copy | `~/bin/rclone size r2:cedar-backup` responds | ✅ `T-147`, daily to R2 |
| 5 | External monitoring + alerts | UptimeRobot, three monitors green | ✅ 13.08.2026, status page at `stats.uptimerobot.com/jKcnizZ9vU` (a custom domain there is paid) |
| 6 | Restore verified | deploy yesterday's dump into an empty database locally and open it | ✅ `T-149`, repeat monthly (§5) |
| 7 | Payment keys live on prod | a test purchase with your own card, money arrived | ✅ **Stripe verified with real money** (first real payment on 26.07.2026; checked off on 13.08 — payments go through and land on Marty's bank account). The credits wallet goes through the same flows. PayPal and Telegram Stars have not been verified with real money |
| 8 | Subscription taxes configured | Stripe Tax enabled, jurisdictions registered | ❌ Marty's decision, see §2 |
| 9 | Support address works | mail to `cedarworks@mooexe.dev` arrives and gets read | ⏳ to verify |
| 10 | Clear what to do on payment failure | the path is documented: subscription didn't renew → what the user sees | ❌ not documented |
| 11 | Post link previews work | a blog post link unfurls in Telegram/X/Discord with an image and description | ❌ **there are no OG tags on post pages at all** (only on the landing page) — `T-174`. Breaks channel #1 from §6: every shared post right now is a bare link |
| 12 | Landing page speaks to the target audience | `/` — EN, devlog-first, waitlist | ❌ `T-154`; without it, acquisition from §6 leads to the RU page for a "universal publisher" |

**Item 6 is the most underrated.** A backup that has never been restored isn't a backup, it's hope.
The check takes ten minutes: download the daily copy, `gunzip`, open it locally with
`CEDAR_DATA_DIR`, make sure the posts are there.

**Item 7 is half closed**: Stripe has been run with real money and it reached the bank account —
meaning the main payment path works end to end, not just in code. PayPal and Telegram Stars remain
unverified, and they need to be checked the same way: with your own card and your own stars, before
the first customer. A customer won't have access to the logs — they'll just leave.

---

## 2. Money and taxes — things decided once

None of this gets written into code, but all of it affects whether money is left over afterward.

- **Who receives the payments.** The operator is an individual (Oregon, USA). Stripe for a US
  individual requires an SSN or EIN and a bank account. An EIN is obtained for free on the IRS
  website; it also avoids having to expose the SSN to counterparties. **Marty's decision: get an EIN
  or make do with the SSN.**
- **Sales tax.** Oregon has none, but tax is paid based on the customer's location, not the seller's:
  most states have a threshold (economic nexus) around $100k or 200 transactions a year. Below the
  threshold — nothing is needed; above it — registration in that state. **While sales are small this
  isn't relevant, but the threshold needs to be known in advance**, because it arrives silently.
- **VAT/GST.** Selling a digital service to an individual in the EU/UK is subject to VAT at the
  buyer's country rate from the first euro — there is no threshold. Stripe Tax calculates and
  withholds this automatically (a checkbox in the dashboard + OSS registration), otherwise it has to
  be handled by hand.
- **Telegram Stars and PayPal** don't resolve the tax side for you — it's the same income.
- **1099-K.** Stripe will send the form if turnover crosses the thresholds; it's not a new tax, just
  reporting.

None of the above is legal or tax advice: it's a list of topics you take to an accountant, not
resolve by googling.

---

## 3. Unit economics: where the money leaks

Fixed costs (check against the actual bills, not from memory):

| Item | How much | Where to check |
|---|---|---|
| Droplet `cedarclerk-periwinkle` | 1 vCPU / 2 GB | DigitalOcean dashboard |
| Weekly droplet images | ~20% of the droplet price | same place |
| Cloudflare R2 | $0 up to 10 GB — off-box backup target (`T-147`) | Cloudflare dashboard |
| Domain `mooexe.dev` | once a year | registrar |
| Stripe/PayPal fees | ~2.9% + $0.30 per payment | Stripe dashboard |
| Anthropic (AI features, paid in credits) | **variable** | console.anthropic.com |

**T-152 closed in substance on 31.08.2026 — AI moved onto credits** (prices unchanged: $3 / $6 /
$1 trial; the table below was approved by Marty whole). The live tier composition, mirroring
`PlanLimitations` + `CreditPacks`:

| | Free | Pro $3 | Pro+ $6 |
|---|---|---|---|
| Publishing channels | 1 | 3 | 10 |
| Storage | 100 MB | 1 GB | 3 GB |
| Content languages (creating versions) | EN + JA | all | all |
| Custom signature | — | ✓ | ✓ |
| Header slots | 2 | 3 | 3 |
| Channel switch | once per 7 days | free | free |
| X posts | credits (1) | credits (1) | credits (1) |
| AI: document translate / edit | — | credits (2) | credits (2), **30/mo included** |
| AI: small calls (glossary, forms, profile) | — | credits (1) | credits (1), from the same 30 |
| AI: glossary description from an image | — | credits (2), **provisional** — T-438 | credits (2), from the same 30 |

Mechanics: `SubscriptionPlan.TryChargeAiAsync` = the 20/day ceiling (kept as an abuse guard,
T-352) + a wallet charge; Pro+ receives `CreditPacks.ProPlusMonthlyCredits` (30) with every
successful subscription payment (Stripe checkout/renewal, PayPal, Stars; the trial does not — $1
would otherwise buy an allowance with a $12 list value). The margin is now capped from above: the
worst Pro+ case is 30 credits spent on translations = 15 calls a month, against the previous
potential 600. The §5 ritual still verifies against the actual bill: the average cost of one
translation must sit well under $0.40 (the list price of two credits), or `AiTranslateCost` goes
up.

---

## 4. Metrics: four things, not twenty

A solo project needs the ones that change decisions. Three of the four (conversion, churn, MRR) are
calculated from data that's already in the database; activation only half so: publications are in
the database, but the funnel up to the first publication (where they came from, where they dropped
off) will appear together with analytics (`T-153`) — the contradiction between this line and T-153
was resolved on 18.08.2026 in favor of the honest phrasing.

1. **Activation**: the share of registered users who published at least one post within the first
   week. Low activation means the problem is in onboarding, not acquisition, and it's too early to
   buy traffic.
2. **Free → paid conversion** (including the $1 Trial as a separate step). Trial here works as an
   intent filter: someone who paid a dollar and someone who paid nothing are different people.
3. **Churn**: how many paying users didn't renew within a month. For a service where people move
   their work into, churn above 5% a month means the product hasn't become a habit.
4. **MRR** — the sum of active subscriptions. Not monthly revenue, but specifically the recurring
   part: one-off Trials and credit purchases are not included.

What deliberately **not** to measure at this stage: landing page traffic, likes, "reach." They're
nice and decide nothing while there are fewer than ten paying users.

---

## 5. Rituals

**Weekly** (fifteen minutes):
- `/api/health` + `df -h`/`free -m` over ssh — version, disk, memory;
- `ls -lt data/backups | head` — the copy is fresh;
- UptimeRobot — were there any outages, and why;
- Stripe — did renewals go through, are there any stuck payments;
- error log: `ssh … "journalctl -q -u cedarclerk -n 200 --no-pager | grep -i error"`.

**Monthly**:
- calculate the four metrics above;
- reconcile costs against the bills (§3) and calculate the AI cost per Pro Plus user;
- verify that the daily backup **actually restores** (item 6 from §1) — once a month, not once a
  year.

**Quarterly**:
- reread Terms/Privacy: have they diverged from what the code does (new integrations = new third
  parties in the policy);
- check expiry dates: payment keys, X/Bluesky tokens, domain expiration;
- update `docs/product/PRODUCT.md` if pricing tiers or limits changed.

---

## 6. How the first users arrive

The audience is indie developers, and they have specific places they hang out. The order here is by
"effort → result" ratio for a solo founder with no budget:

1. **Your own devlog as a showcase.** Marty's Dev Diary is run *inside the product itself*: every
   post is a demonstration. It's the only channel that works while you sleep, and it already exists.
2. **itch.io devlogs and Steam announcements** — the readers there are exactly the people who run
   devlogs, meaning they already have the pain the product solves. Publishing there is `T-127`
   (research first whether a write API exists).
3. **Communities**: r/gamedev, r/indiegames, gamedev Discords, local jams. One rule — show up with a
   story about how the thing was made, not with an ad.
4. **Themed jams** — hand out Founder codes (ADR-022) to participants of a specific jam: a narrow,
   motivated audience, and a reason to talk.

What not to do: paid advertising before activation from §4 becomes decent. Bought traffic through a
leaky onboarding is paid-for churn.

---

## 7. Danger spots already visible

- **Opening registration before payments are verified** — the most expensive possible mistake: the
  first users will show up exactly once.
- **Promising an SLA or data-safety guarantees beyond what actually exists.** Right now, honestly: a
  nightly database copy, a weekly machine image, one server with no redundancy. That's exactly what's
  written in Privacy.
- **AI features with no spending ceiling.** There's a daily limit, but no monthly cap per account.
- **One person is the single point of failure.** Backups, keys and access need to be recoverable by
  someone else if the situation demands it. Right now everything is tied to one laptop and one
  account.
- **One security key.** On 13.08.2026 the main accounts were switched to PassKey/Security Key with a
  YubiKey 5 — the right move, and it also turns one piece of hardware into the only path to
  DigitalOcean, Cloudflare, Stripe and email. A second key or printed recovery codes kept elsewhere —
  `T-157`.
- **Media lives on the droplet's disk.** Quotas have been cut down to what the disk can back
  (ADR-129), but the files are still on one machine — until the move to object storage (step 2 of
  `T-172`), the disk remains the ceiling. Detailed breakdown — `docs/product/MULTITENANCY.md` §1.
