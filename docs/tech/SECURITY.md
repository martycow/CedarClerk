---
owner: marty
last_verified: 2026-09-05
source_of_truth_for: the threat model — assets, trust boundaries, what each threat is answered by, and what is still open
guard: none
---

# Security — the threat model

What an attacker could want from Cedar Clerk, where the lines are that stop them, and which lines
are still missing. It exists because registration is about to open to strangers (`docs/product/BUSINESS.md`
§1) and the app holds three things worth stealing at once: other people's tenants, money, and
personal data. Every mitigation below is cited to code or an ADR; every gap is a candidate board
row, listed at the end and nowhere else.

Neighbouring rules that this file does not repeat: `.claude/rules/secrets.md` (where keys live),
`.claude/rules/production-environment.md` (the host), `.claude/rules/telegram-bot.md` (the shared
bot), `.claude/rules/renderers.md` (output escaping).

## 1. Assets

| Asset | Where it lives | Why it matters |
|---|---|---|
| Tenant rows — drafts, posts, assets, channels, credits | `cedar.db`, partitioned by `OwnerId` | One account reading another's is the product failing at its one promise |
| Account PII — email, display name, Telegram id, Stripe customer id | `ApplicationUser` | Regulated; the only identity the app has for a person |
| Reader PII — gate submissions, comments, waitlist | `PostRegistration`, `Comment`, `WaitlistEntry` | Given by strangers to an author, on the author's word that it stays private |
| Money — plan state, credit ledger, payments | `Payment`, `CreditEntry`, `CreditWallet.cs` | A forged webhook or replayed capture is free credits |
| Provider secrets — bot token, Stripe/PayPal keys, social OAuth tokens | systemd drop-in; `PublishTargetSecrets.cs` encrypts tenant tokens at rest | The bot token alone reaches every user's channels |
| The DataProtection key ring | `{CEDAR_DATA_DIR}/dataprotection-keys` | Signs auth cookies, media grants, private-post access, encrypted credentials |
| Media files | `data/media`, served at `/media/*` | Private posts publish files; the file must not outlive the post's audience |
| Availability | one process, 1 vCPU / 2 GB, no swap | An OOM is an outage, not a slowdown |

## 2. Trust boundaries

| Boundary | What crosses it | What holds it |
|---|---|---|
| Internet → Cloudflare → tunnel → Kestrel on `127.0.0.1:8080` | Every request; TLS ends at Cloudflare | The droplet exposes only SSH; the tunnel is the only way in. Kestrel sees plain HTTP and **no forwarded-header trust is configured** — absolute URLs come from `Cedar:MainHost`, never from the request (ADR-124) |
| Host → tenant | The `Host` header names whose blog this is | `TenantHost.Resolve` (ADR-206…212): suffix match includes the dot, reserved labels refuse, unknown names 404; a tenant host serves the blog and nothing else — API and SPA are unrouted there, and a signed-in identity is dropped with a warning (ADR-210) |
| Request → tenant scope | Which rows a query can see | `OwnerId` is the tenant id; a tenant context and a platform context compile to two models; an unset tenant reads nothing (ADR-206/207/208). `TenantFilterGuardTests` fails the build for any owned entity without a filter and any unlisted unowned one. Cross-owner reads have to be said out loud in `PlatformPaths` or a `CreatePlatformScope` |
| Browser → session | The Identity cookie | `AuthCookie.cs`: `HttpOnly`, host-only, `SameSite=Lax`, 30-day sliding; 401/403 instead of redirects. No antiforgery service — `Lax` is the whole CSRF defence, stated in `AuthCookieTests` |
| Stranger → account | Registration, Google, Telegram login | One invite gate for every door (`ResolveInviteAsync`, ADR-237); Google never merges into an existing address on the provider's word; Telegram login is HMAC-verified against a 24 h `auth_date` (`TelegramLoginVerifier`) and never creates an account |
| Account → project it does not own | Collaboration and the canvas hub | `ProjectAccessResolver` resolves once in a platform scope and opens the *owner's* tenant scope for the work; a membership opens the canvas and nothing else (ADR-217/235); the hub re-resolves every persisting call (`CanvasHub.cs`) |
| Provider → app | Stripe webhook, PayPal return, Telegram updates | `StripeWebhookVerifier`: HMAC over `t.payload`, ±5 min, constant-time compare; idempotent by event id. PayPal is captured server-to-server with the app's own OAuth token, idempotent by capture id. Telegram is long-polled — there is no inbound webhook to spoof (ADR-005) |
| Shared bot → account | Which account an update belongs to | Four attribution checks from the T-359 audit, kept in `.claude/rules/telegram-bot.md` §"The bot is shared" |
| Reader → private post and its files | `?invite=` link, access cookie, media grant | Signed `PrivateAccess` cookie per draft (ADR-084); a file inherits the audience of the posts that publish it (`MediaOwnershipMiddleware`, ADR-211/219); a `MediaGrant` is a 15-minute signed key bound to one filename, issued only at Telegram send time |
| Author text → rendered HTML | Post bodies, notes, comments | Every renderer escapes `< > &` before markup — invariant 1 of `renderers.md`, unit-tested per mark and block |
| Browser session → desktop shell | A provider sign-in finished in the system browser | One-time code, two minutes, bound to the account and to the SHA-256 of a verifier only the shell holds; minted on a click, never on arrival; redeem refuses a request without `X-Cedar-Desktop`, which a web page cannot add to a navigation (ADR-327, `DesktopHandoffTests`) |
| Desktop shell → local agent | Filesystem reads on the maintainer's machine | Per-launch 32-byte token, never on disk; granted roots only, compared as resolved paths (`docs/tech/DESKTOP.md`) |
| Repo → secrets | Config keys | Every `Cedar:*` secret is an environment variable in the systemd drop-in; `appsettings.Development.json` is gitignored; rotate before cleaning history |

## 3. Threats — STRIDE by boundary

**Status** reads *held* (a cited mitigation exists), *partial* (a mitigation exists with a known hole), or *open* (nothing but luck; see §Gaps).

| # | Threat | Category | What answers it today | Status |
|---|---|---|---|---|
| T1 | Account A reads or writes account B's rows through any endpoint | Elevation / disclosure | Query filters compiled into the tenant model; strict unset default; the guard tests | held |
| T2 | A member of a shared project reaches the owner's tasks, documents or dashboard | Elevation | Owner-only routes stay owner-only; a member gets 404, the frontend never offers the link (ADR-217) | held |
| T3 | A viewer writes to a board | Elevation | Hub throws `NoWriteAccessToProject`; drag/cursor frames may ride a 10 s cached membership, persisting calls never do | held (residual §4) |
| T4 | A stranger connects a channel the shared bot is already in | Spoofing | Caller's own Telegram must be admin/creator (`BotChatAccess.IsAdminOrCreator`) | held |
| T5 | Comment counts inflated by forwarding a post into a private chat with the bot | Tampering | Supergroup + automatic-forward + channel-origin checks in `TelegramEngagement` | held |
| T6 | A draft names another owner's asset guid and mutates its `TelegramFileId` | Tampering | Asset queries in `TelegramPublishTarget` carry `OwnerId` explicitly | held |
| T7 | Forged Stripe event grants a plan or credits | Spoofing / tampering | Signature + tolerance + constant-time compare; missing secret answers 501 | held |
| T8 | Replayed Stripe event or PayPal capture double-grants | Tampering | `Payment.ExternalId` (session/invoice id) and `captureId` checked first | held |
| T9 | Stars invoice paid from a third account leaks the payer's balance or expiry | Disclosure | Confirmation omits both when `From.Id != user.TelegramUserId` | held |
| T10 | PayPal refund or chargeback never reaches the app | Repudiation | Nothing — there is no PayPal webhook, only the browser return | open (G8) |
| T11 | Password spraying, invite-code guessing, username enumeration, gate/comment flooding | DoS / brute force | Identity lockout (per account, 5 failures / 5 min) and the gate's 3 submissions per visitor per 24 h. Nothing else is rate-limited | open (G1) |
| T12 | The one rate limit is defeated by setting `CF-Connecting-IP` | Tampering | `VisitorHash` prefers that header and nothing validates the proxy chain | open (G2) |
| T13 | Session cookie sent over plain HTTP | Disclosure | `Secure` is left at `SameAsRequest`, and Kestrel's request is HTTP — the attribute is not set. Cloudflare is HTTPS-only today, which is what actually holds | partial (G3) |
| T14 | CSRF via a state-changing GET on top-level navigation | Tampering | `Lax` permits it; `GET /api/billing/paypal/capture` needs a live PayPal order id, `GET /api/auth/confirm-email` a token | partial (G5) |
| T15 | Clickjacking, MIME sniffing, referrer leakage on the app and the blogs | Disclosure / tampering | Only the preview iframe sets `X-Frame-Options`/`frame-ancestors`; no global headers, no HSTS at the origin | open (G4) |
| T16 | A file uploaded under a lying content type is served as something else | Tampering | Allowlist keyed on the declared type, extension derived from it, no `text/html`/`svg` in the list; the sniffer exists but only guards the `.cedar` import | partial (G6) |
| T17 | A 700 MB upload takes the process down | DoS | Kestrel refuses above 210 MB — but the allowlist promises 1 GB, and the upload path buffers the whole body in memory first | partial (G7) |
| T18 | Path traversal through `/media/*` or import temp files | Elevation | `MediaFileNames.TryParse` accepts only `asset_{guid}[_tg].{ext}`; deletion re-resolves with `GetFullPath` | held |
| T19 | GPS in a phone photo published to a channel | Disclosure | EXIF/XMP/IPTC stripped losslessly on every write path (ADR-130) | held |
| T20 | A private post's picture readable by URL after the post is locked | Disclosure | Visibility recomputed per request from the publishing posts; `private, no-store` on non-public files | held |
| T21 | Reader identity recoverable from stored `VisitorHash` | Disclosure | SHA-256 of ip + a **committed constant** salt — pseudonymous, reversible over IPv4 | partial (G9) |
| T22 | Arbitrary reaction `Kind` strings persisted | Tampering | Only a non-blank check; the client writes counts with `textContent`, so not XSS today | partial (G10) |
| T23 | A team of fifty reaches a project capped at ten | Elevation | The cap counts `ProjectMember` rows only; team access is resolved separately | partial (G11) |
| T24 | Tracked link `/l/{code}` used as an open redirect for phishing | Spoofing | Creation needs an account and an absolute http(s) URL; the redirect is the feature | partial (G12) |
| T25 | Backup copy in R2 read by whoever holds the bucket key | Disclosure | Second account, `--backup-dir` against deletion; no client-side encryption. The key ring is deliberately not in the set | partial (G13) |
| T26 | A leaked secret found in git history | Disclosure | `.gitignore`, the pre-flip scan in `repo-hygiene.md`, rotation before cleanup | held |
| T27 | A vulnerable dependency shipped unnoticed | Tampering | No audit step; runtime on .NET 10 LTS (ADR-305) | open (G14) |
| T28 | Presence and revocation break when a second instance starts | Elevation | The hub is process-local by design; a second Kestrel needs a backplane first (ADR-218) | held (constraint) |
| T29 | Link probe reaches an internal address | SSRF | Every hop checked and only the checked address dialled (ADR-268); board image URLs must start with `/media/` | held |
| T30 | Admin route probed by a non-admin | Disclosure | `/api/admin` answers 404, not 403; `IsAdmin` is grant-only from `Cedar:AdminEmail` | held |

## 4. Accepted residuals

Stated so nobody rediscovers them as bugs.

- **Ten seconds of stale membership on cursor and drag frames** (`CanvasHub.TransientAccessTtl`). A cursor position discloses nothing, and anything that persists re-resolves.
- **Thirty seconds of a renamed or deleted tenant still answering** under its old host (`TenantOwnerCache`, ADR-212). Expiry is the only invalidation, on purpose.
- **The 401-vs-404 rule** (ADR-217): a row that is not yours is 404, a reader's refused write is 403. A tester who sees 404 on an existing id is seeing the rule, not a routing bug.
- **The key ring is not backed up.** A restore silently loses stored social credentials and every outstanding grant; they are re-issuable, the database is not.
- **Reader data is pseudonymous, not anonymous** — see T21 until G9 lands.

## 5. Reporting

The repo is private and there is no external reporter yet. A root `SECURITY.md` with a reporting
channel belongs to the day visibility flips — add it as an item on the pre-flip checklist in
`.claude/rules/repo-hygiene.md` rather than now, so it names a real channel instead of a
placeholder.

## Gaps — candidate board rows

Not added to `docs/tasks/BACKLOG.md` by this document; each is a row waiting for a decision. Order is roughly by
exposure once strangers can register.

- **G1** Rate limiting: `AddRateLimiter` with a per-IP policy on `/api/auth/*`, `/api/waitlist`, blog comments/reactions/polls and `/api/assets`; per-account lockout is the only brake today. #security P1
- **G2** Trust the proxy chain or stop reading it: `UseForwardedHeaders` with Cloudflare's ranges, or derive `VisitorHash` from the socket only — today `CF-Connecting-IP` is attacker-settable. #security P1
- **G3** `Cookie.SecurePolicy = Always` in `AuthCookie.cs`, asserted in `AuthCookieTests`. #security P1
- **G4** Global security headers: `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `X-Frame-Options`/`frame-ancestors`, `Permissions-Policy`; a CSP needs nonces for the blog's inline scripts. HSTS is Cloudflare's to set. #security P2
- **G5** Turn the two state-changing GETs into POSTs, or add antiforgery for the browser-return shape: PayPal capture, e-mail confirmation. #security P2
- **G6** Sniff uploads with `ImageContentSniffer` on `/api/assets`, not only on the `.cedar` import; refuse a declared type the bytes contradict. #security P2
- **G7** One upload ceiling: align `MediaMaxBytes` with Kestrel's 210 MB (or raise Kestrel per endpoint) and stream to disk instead of buffering in memory. #security #techdebt P2
- **G8** PayPal webhook with signature verification, so refunds and disputes reach the ledger. #billing #security P2
- **G9** Per-installation `VisitorHashSalt` from config (or a daily rotating salt), so the hash is not reversible by anyone with the source. #privacy P2
- **G10** Allowlist reaction kinds on the server. #security P3
- **G11** Count team members against the project cap, or say the cap is per-invitation. #canvas P3
- **G12** Tracked links: an interstitial or a domain allowlist, and an abuse-report path. #security P3
- **G13** Client-side encryption for the R2 copy (`rclone crypt`), and a decided stance on the key ring. #ops #security P2
- **G14** Dependency audit as a test-gate step (`dotnet list package --vulnerable`, `npm audit`) . #techdebt P2
- **G15** EF command logging at `Information` in production fills the journal at ~1.5 M lines a day; `Warning` for `Microsoft.EntityFrameworkCore.Database.Command`. #ops P3
- **G16** Root `SECURITY.md` with a reporting channel, on the pre-flip checklist (§5). #docs P3
