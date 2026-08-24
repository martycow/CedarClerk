---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: the operating model once external users arrive — quotas, domains, self-hosted
guard: none
---

# How this works once users show up

A breakdown of what happens to the infrastructure at the first few dozen accounts: where their blogs
live, how much space is needed, what to hand off to self-hosted, and why the current quotas won't
survive the first five paying users. Written 13.08.2026 against the actual state of the machine and the code.

Neighboring documents: `docs/knowledge_base/STACK.md` — what it's built from and what it costs; `docs/product/BUSINESS.md` — what
must be done before registration opens; `docs/tech/ARCHITECTURE.md` — how the code is structured.

---

## 1. The main arithmetic everything else follows from

Facts, each verifiable with one command:

- the droplet has **48 GB of disk, ~40 GB free** (`df -h /`);
- right now all data takes up **1.2 GB** (`du -sh ~/cedarclerk/data`), of which 938 MB is Marty's media;
- quotas from `PlanLimitations.StorageLimitBytes`: **Free 100 MB, Pro 1 GB, Pro Plus 3 GB,
  Founder 100 GB** (ADR-129 — the numbers are sized against the disk, not against generosity).

The Founder account's 100 GB is still bigger than the disk — but it isn't a sellable tier, and
there aren't two dozen Founder accounts. Deploy also needs room for a second copy of the app,
and backups need room for 14 daily copies of the database.

What stands between us and the fifth paying user isn't quotas — it's storage. Options, in order of increasing correctness:

1. ~~**Lower quotas** to honest values~~ — done (ADR-129), this is step 1 of `T-172`.
2. **Attach a DigitalOcean volume** — block storage, ~$0.10 per GB per month, mounted at
   `~/cedarclerk/data/media`. Buys time, doesn't change the architecture.
3. **Move media to object storage** (R2 — it's already in the project: the off-box backup T-147 goes
   there every night; DO Spaces is worse — same account as the droplet). This is the correct answer: files
   stop living on the machine, disk stops being a constraint, serving happens outside the app. The cost —
   rewriting storage (`/media/*` becomes a redirect or a proxy to a signed URL) and working out
   private posts, where the link must not be permanent.

The third option needs to happen **before** registration opens, not after: migrating other people's files
is more painful than migrating your own. Tracked as **`T-172` — registration blocker** (step 1 — quotas, step 2 — R2);
overlaps with `T-088` (Meta networks need a public HTTPS media URL) — do it as one piece of work.

## 2. Where user blogs live

Today there is one blog: `blog.mooexe.dev`, host-routed in `Program.cs` (`MapWhen` on `Host.Host`), with all
the routing inside `BlogEndpoints.HandleRequest`. For N users there are three schemes:

| Scheme | What it looks like | Pros | Cons |
|---|---|---|---|
| **Path** | `blog.cedarclerk.app/marty` | nothing to change in DNS or TLS | shared domain = shared reputation; someone else's SEO ban hits everyone |
| **Subdomain** | `marty.cedarclerk.app` | looks like your own site, cookie isolation | needs a wildcard certificate and a wildcard in the tunnel |
| **Own domain** | `martygames.com` | maximum for the user | issuing a certificate for someone else's domain, ownership verification, support |

ADR-020 already chose **subdomains on a dedicated domain** (working name `cedarclerk.app`, not
registered). This is the right call, and it comes with work that doesn't exist yet: a wildcard record
through the Cloudflare tunnel, extending host-routing from one name to `*.domain`, and resolving the
tenant from the subdomain instead of from a constant.

**The user's own domain** is not for the first version: it drags in certificate issuance (Cloudflare
for SaaS or ACME), ownership verification, and support for people who set up their CNAME wrong.

## 3. What happens on registration today

Worth saying plainly, because it affects everything: **multitenancy already exists in the code** — data is
partitioned by `ApplicationUser`, limits are computed per plan, there is one bot shared by everyone that hands out channels by
`TelegramUserId`. What's missing is infrastructural separation: all files in one folder, all rows in
one database, all blogs on one host. That's enough for dozens of users; not for thousands —
but thousands would require an entirely different conversation.

The real order of bottlenecks, not an imagined one:

1. **Disk** (see §1) — hits first, at single-digit user counts.
2. **Media-serving bandwidth** — the app serves files itself, through the tunnel.
3. **Memory**: 2 GB with no swap, and an OOM kills the service rather than slowing it down.
4. **SQLite writes** — one transaction at a time. Very far off.
5. **CPU** — 1 vCPU, and blog rendering is cheap.

## 4. Self-hosted: what it actually means

`docs/product/PRODUCT.md` calls the product "self-hosted", but today that describes how Marty
runs it, not an offer to a user. Genuine self-hosted means three commitments, each of
which costs time:

- **Public source code** (or at least the builds) — the repository is currently private. Opening the code isn't
  ruled out, but it's a separate decision: licensing, other people's issues, community expectations.
- **An installation method** — a Docker image and `docker-compose.yml` (`T-151`), otherwise installing
  requires the .NET SDK, Node, and an understanding of systemd.
- **Updates and compatibility** — migrations have to apply against someone else's database, which
  nobody is fixing by hand.

A sensible order: **Docker for ourselves first** (simplifies environments, `T-150`), then —
as a side effect — an image anyone can run, and only then the decision about
opening the code. The license, if it comes to that: AGPL protects against "took it and stood up a SaaS from your
code", MIT doesn't; for a product that earns money by hosting, that's a different fate.

## 5. How much space to allot

A benchmark more useful than any round number: **one of Marty's posts with images weighs a few megabytes**, and
the 938 MB accumulated over months of working with media, including video. Which means:

- **Free 100 MB** — roughly 25–50 posts with images. Honest as a trial tier.
- **Pro 1 GB** — years of a devlog with images; only someone uploading video hits the ceiling.
- The set lives in one place, §1 (ADR-129, Marty's decision on `T-172`). Raising a quota is easy; lowering one is
  not, hence starting from the low end.

And separately: **video**. Right now `MediaMaxBytes` allows a file up to 1 GB. A single such video
eats the whole Free quota and a noticeable share of Pro. A devlog product needs video, but its place isn't
the app's disk (see §1, option 3).

## 6. Next order of operations

Not a year-long plan, but what makes sense to do in this order:

1. **Quotas down** to values the machine can actually support (a one-line edit to `PlanLimitations`).
2. **Media into object storage** — removes the main constraint and makes everything else possible.
3. **Docker + environments** (`T-151`, `T-150`) — after which staging stops being a luxury.
4. **Domain + wildcard + tenant from subdomain** (ADR-020) — once a second user shows up.
5. **Open registration** — per the checklist in `docs/product/BUSINESS.md` §1.
