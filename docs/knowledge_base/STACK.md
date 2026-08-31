---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: the stack, dependency versions and costs
guard: none
---

# Stack and Costs

Everything Cedar Clerk is built from, and what it costs. Checked against the project files on 13.08.2026,
re-checked on 18.08.2026 (R2 rows, social networks, .NET EOL).
Versions come from `*.csproj` and `package.json`; **the amounts are estimates — verify against the actual
bills**, because the bill knows the truth and the document only remembers the moment it was written.

---

## 1. Backend (.NET 8)

| Package | Version | Why |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.* | The whole database. SQLite is a deliberate choice — one machine, one file, `sqlite3 .backup` as the backup |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 8.0.* | Accounts, cookies, password hashing |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.* | Migrations (`dotnet ef`) |
| `Telegram.Bot` | 22.10.2 | The bot and publishing: Bot API 10.2, the Blocks mechanism |
| `Quartz` + `Quartz.Extensions.Hosting` | 3.18.2 | Scheduled publishing, background jobs |
| `SixLabors.ImageSharp` | 3.1.12 | Image compression, previews, watermarking |
| `Anthropic` | 12.35.1 | Auto-translation and AI editing (default model `claude-haiku-4-5`). OpenAI/DeepL are implemented in code as alternative providers, not configured in production |
| `ClosedXML` | 0.105.0 | The dialogue tool's xlsx translation sheet — export and import (ADR-230) |
| `Spectre.Console` (+ `.Cli`, `.Testing`) | 0.55.0 | The `cedar` console |
| `xunit` + `Microsoft.NET.Test.Sdk` | 2.5.3 / 17.8.0 | ~935 tests as of 18.08.2026 — `cedar test` always has the exact count, not this file |

**What's deliberately not in the stack**: an ORM on top of EF, Redis, a message queue, Docker (for now —
`T-151`), a service mesh, and everything else usually added "for future growth." While the app lives on
one machine, each of those would add a failure mode rather than resilience.

## 2. Frontend (Angular 21)

Angular 21 (standalone components, signals), TipTap 3 as the editor — `starter-kit` plus extensions
for images, tables, formulas, checklists and alignment. Plus `@angular/cdk` (drag-drop in toolbar settings),
`katex` (formulas), `@phosphor-icons/core` (icons), `rxjs`. Tests via Vitest, 18 of them.

## 3. Desktop

Electron 33 + `electron-builder` 25 + `electron-updater` 6. Since ADR-117 the desktop app is a thin client
onto production plus a filesystem agent; it has no database of its own.

## 4. External services

| Service | Role | What happens if it goes down |
|---|---|---|
| **DigitalOcean** | droplet `cedarclerk-periwinkle` (1 vCPU / 2 GB / 48 GB, fra1) | everything is down |
| **Cloudflare** | DNS, tunnel (the only outside entry point), R2 for off-site backup (code is ready, **not enabled** — `T-147`) | the site is unreachable; SSH still works by IP |
| **Telegram** | publishing to channels, login, Stars payments | posts stop going out, the blog still works |
| **X (Twitter)** | cross-posting and threads (`XPublishTarget`, `api.x.com`; a post costs the author 1 credit, the app has its own pay-per-use balance) | X publishes fall into the queue with a clear error, everything else works |
| **Bluesky** | cross-posting and threads (`BlueskyPublishTarget`, `bsky.social`, app password) | same, for Bluesky |
| **Stripe** | subscriptions and credits, the money lands in Marty's bank account | payment is unavailable, the service still works |
| **PayPal** | an alternative payment method (no recurring, ADR-013) | same |
| **Anthropic** | auto-translation, AI editing | the AI features return an error, everything else works |
| **Resend** | emails (invites to private posts) | invites stop going out |
| **healthchecks.io** | watchdog for the nightly backup | the backup still runs, just silently |
| **UptimeRobot** | external monitoring + status page | nobody gets notified of an outage |

## 5. What it costs

Fixed monthly costs, at public prices as of 13.08.2026 — **verify against the real bills**:

| Item | Estimate | Note |
|---|---|---|
| Droplet, 1 vCPU / 2 GB / 48 GB | ~$12/mo | fra1, the cheapest tier this runs on |
| Weekly droplet images | ~20% of the droplet's price | enabled by Marty on 11.08.2026 |
| Cloudflare (DNS + tunnel) | $0 | free plan |
| Cloudflare R2 | $0 | up to 10 GB free; **nothing is being uploaded yet** — the upload is waiting on keys (`T-147`), would take up ~1.2 GB |
| Domain `mooexe.dev` | ~$15/yr | |
| healthchecks.io | $0 | free tier |
| UptimeRobot | $0 | **a custom domain for the status page is paid**, so the link points at `stats.uptimerobot.com` |
| Stripe | 2.9% + $0.30 per charge | on a $3 subscription that's ~13% |
| **Anthropic** | **variable** | the only line item that grows with the number of users |

**Total fixed: around $15 a month.** Everything else is variable, and exactly one line there is
dangerous: Anthropic. The breakdown of why Pro Plus at $6 might not pay for itself is in
`docs/product/BUSINESS.md` §3.

Stripe's fee is worth a closer look on its own: on a $3 subscription it eats 13%, on a $6 one, 7.5%.
That's an argument for a discounted annual plan (one fee instead of twelve), once there's time to
revisit the pricing (`T-152`).

## 6. What this stack costs us

- **A single machine** — no horizontal scaling, no fault tolerance. Deliberate: while there's one user,
  this is the cheapest and simplest option by far.
- **SQLite** — one write at a time. That's more than enough headroom for publishing and editing posts;
  the bottleneck will be the disk, not this (see `docs/product/MULTITENANCY.md`).
- **Media on the droplet's disk** — this is what will hit the ceiling first. The quotas have been trimmed
  down to honest numbers (Pro 1 GB / Pro Plus 3 GB, ADR-129), but the disk stops being the ceiling only
  once media moves to object storage — step 2 of `T-172`, a blocker for registration.
- **.NET 8 (runtime 8.0.29 on the droplet) goes out of support in November 2026** — `T-072`; the
  deadline moved with the app from the Pi and hasn't gone anywhere.
- **Manual deploy** — `cedar deploy` from a laptop. There's no CI; only Marty can build and roll it out.
