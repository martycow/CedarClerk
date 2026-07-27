# Admin panel — scoping (IF2)

Research done 27.07.2026 against the code as it stands, before writing anything. `IF2` in `docs/BACKLOG.md` (the `Input.md` sweep) plus the older idea #12 — they're the same feature, and the Input version has the real wish list:

> Управление постами, управление пользователями, создание инвайт-кодов, просмотр какие пользователи по каким инвайтам пришли, возможность активировать/деактивировать подписку и многое-многое другое.

This is the largest single item across all three brainstorm lists. This document is what has to be decided and built, not a plan to start from today.

## What exists today — verified, not assumed

| Thing | State |
|---|---|
| Role / admin concept | **None at all.** `ApplicationUser : IdentityUser` has no role or admin field; `Program.cs` calls `AddIdentityCore` **without** `.AddRoles(...)`, so `AspNetRoles`/`AspNetUserRoles` don't even exist; there is not one `RequireRole`/`IsInRole` in the codebase |
| Invite codes | **One shared string in configuration** (`Cedar:InviteCode`, checked in `AuthEndpoints.cs:47-49`). No entity, no per-code record, and **nothing is written to the user about which code they used** |
| Subscription grant | Already solved: `SubscriptionPlan.ApplyPurchase` is a clean reusable helper, and `PlanExpiresAt = null` on a paid tier already means "manual grant, never expires" — the entity documents this explicitly |
| Billing history | `Payment` already audits every billing event across providers (owner, provider, plan, amount, currency, status, timestamp) |
| Per-user data | All of it exists and is queryable: drafts, channels, assets, comments, reactions, form presets, AI usage per day |
| Owner scoping | **61 owner-filtered queries** across 8 endpoint files. Every single read and write in the app is scoped to the calling user |
| Production accounts | Two: `cedarworks@mooexe.dev` (tier Forever, created 06.07.2026) and `test@test.test` (tier Pro) |

## The three things that actually need deciding

### 1. How does someone become an admin, and how does the first one get made?

Two options:

**A. A bool on `ApplicationUser`** (`IsAdmin`). One column, one migration, one check. No new tables.
**B. Real ASP.NET Identity roles** (`.AddRoles<IdentityRole>()`). Brings `AspNetRoles` + `AspNetUserRoles`, `[Authorize(Roles = "Admin")]`, and room for future roles like the Phase 7 "Entertainer".

**Recommendation: A.** There is one admin and there will be one admin for the foreseeable future; B adds two tables and a join to express a single boolean. Phase 7's Entertainer is a *content* role for subscribers, not an authorization role, so it isn't the second user of this mechanism.

The bootstrap question is separate and needs an answer either way: the first admin can't be granted through the admin panel. Cleanest is a config value (`Cedar:AdminEmail`) checked on startup that flips the flag on that account — it works on a fresh database, survives a restore, and needs no manual SQL on the Pi. `cedarworks@mooexe.dev` is the account.

### 2. Invite codes: creating them is easy, attribution is not — and can't be backfilled

Creating codes needs a new `InviteCode` entity (code, label, created, expires?, max uses, uses so far, active) and swapping the config check in `AuthEndpoints` for a lookup. That part is straightforward.

**"Which user came in on which invite" has no data behind it.** Nothing records the code a user registered with, so:
- it needs a `ApplicationUser.InviteCodeId` written at registration, and
- **the two existing accounts can never be attributed** — they came in on the shared config code and there is no record of it. Same shape as the Channel Analysis blocker: history can't be invented.

This is worth knowing before the feature is built, not after it ships showing "unknown" for every current user.

Keeping the config code working alongside real codes is also a decision — I'd keep it as a fallback so a database problem can't lock registration out entirely.

### 3. Cross-owner access is the risky part, and it should not be a flag

Every read in this app filters by `OwnerId`. The tempting implementation is "if admin, skip the filter" threaded through the existing endpoints. **That is the single most dangerous change available in this codebase**: 61 call sites, and one missed or wrongly-placed check is a cross-tenant data leak — exactly the class of bug the `GET /api/channels/known` filter note in `.claude/rules/telegram-bot.md` already warns about.

**Recommendation: a separate `AdminEndpoints` file with its own queries, under `/api/admin`, with the admin check on the route group.** Existing endpoints stay untouched and stay owner-scoped. Duplication of a few queries is the price, and it's worth paying: the security property becomes "everything under `/api/admin` is admin-only, everything else is owner-scoped", which is one sentence a person can actually verify.

## Suggested scope, in the order it should be built

**Step 1 — the role and the shell.** `IsAdmin` + migration, config-driven bootstrap, `/api/admin` group with the check, an `/admin` route hidden from non-admins, and a user list (email, tier, created, counts of drafts/channels/comments). Useful on its own, and it's the piece everything else hangs off.

**Step 2 — user management.** Per-user detail; activate/deactivate a subscription (reuse `ApplyPurchase`, plus a manual grant with no expiry); reset trial; lock/unlock an account (Identity already has `LockoutEnd`, so this is nearly free).

**Step 3 — invite codes.** The `InviteCode` entity, CRUD, registration switched to look codes up, `InviteCodeId` recorded on new users, and an attribution list that is honest about pre-existing accounts.

**Step 4 — cross-owner posts.** Read-only first: every post across owners, with links out. Editing other people's content is a different risk level and shouldn't be in the first version.

**Step 5 — the "much more".** Billing history from `Payment` (nearly free), AI-usage per user, storage per user, a global activity feed. All read-only reporting on data that already exists.

Steps 1–3 are the actual ask; 4–5 are where "чем больше функций, тем лучше" gets satisfied without adding risk.

## Things I'd flag before starting

- **Destructive actions need care.** Deleting a user cascades to their drafts, published blog posts (public URLs!), comments and media. If user deletion is in scope it needs a confirmation flow and probably soft-delete, per `.claude/rules/destructive-operations.md`.
- **There is no audit log.** If an admin can change someone's plan, "who changed what and when" is worth recording from the start — retrofitting it means the early changes are invisible.
- **`CLAUDE.md` says the verification login is `marty@mooexe.dev`; production actually has `cedarworks@mooexe.dev`.** Minor doc drift, but it matters here since that's the account that becomes admin.
- **This is a multi-tenant feature on a codebase with two accounts.** Everything above is correct and worth doing, but the panel's value scales with users; it's worth agreeing how much of Step 5 is wanted now versus when there are people to administer.

## Open questions for Marty

1. `IsAdmin` bool or real Identity roles? (I recommend the bool.)
2. Should user **deletion** be in scope at all, and if so hard or soft?
3. Should the panel be able to **edit other users' posts**, or read-only with links out?
4. Do the existing invite-less accounts matter for attribution, or is "unknown for pre-existing users" acceptable?
5. Keep `Cedar:InviteCode` working as a fallback alongside real codes?
