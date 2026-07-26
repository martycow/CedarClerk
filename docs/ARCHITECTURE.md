# Architecture

## Core idea: one document, many renderers

The internal post format is a single TipTap JSON document, stored in SQLite as `Draft.CedarJson`. It is never edited or interpreted per-target — instead, pure-C# renderers in `CedarClerk.Core` turn it into whatever output format a target needs:

```
                        ┌──────────────────────────────────────────┐
                        │              Raspberry Pi 4               │
 cedarclerk.mooexe.dev  │                                          │
  ┌───────────┐  HTTPS  │  ┌────────────────────────────────────┐  │
  │ Cloudflare├────────►│  │        Cedar Clerk Server          │  │
  │  Tunnel   │         │  │        (ASP.NET Core, .NET 8)      │  │
  └───────────┘         │  │                                    │  │
                        │  │  • REST API (drafts, channels,     │  │
  Telegram ◄────────────┼──┤    export, auth)                   │  │
  Bot API    webhook/   │  │  • Bot host (hosted service)       │  │
             long poll  │  │  • Blog renderer (static pages +   │  │
                        │  │    comments/reactions API)          │  │
                        │  │  • SQLite + EF Core                 │  │
                        │  └────────────────────────────────────┘  │
                        │  ┌────────────────────────────────────┐  │
                        │  │  Cedar Clerk Web App (Angular SPA) │  │
                        │  │  served as static files             │  │
                        │  └────────────────────────────────────┘  │
                        └──────────────────────────────────────────┘
```

Renderers, all in `CedarClerk.Core` (pure C#, no ASP.NET dependency, unit-tested):
- `CedarToTelegramHtmlRenderer` — Telegram Rich Message HTML (Bot API 10.1, `sendRichMessage`). Canonical Telegram renderer; see `.claude/rules/telegram-bot.md` for its HTML-mode constraints.
- `CedarToTelegramMarkdownRenderer` — Markdown export alternative.
- `CedarToBlogHtmlRenderer` — blog HTML pages, including anchor nodes for reactions/comments on specific fragments.
- `CedarPackage` — `.cedar` file format (see below).

Going the other direction — external format *into* Cedar JSON — `CedarClerk.Core.MarkdownToCedarConverter` (a scoped, hand-rolled Markdown parser, not a dependency) turns a Notion-shaped Markdown export into a TipTap doc; see ADR-026, `docs/DECISIONS.md`.

## Solution layout

4 projects, all `net8.0`:

| Project | Purpose |
|---|---|
| `CedarClerk.Server` | ASP.NET Core 8: minimal-API REST endpoints, static host for the Angular SPA, Telegram bot host, Quartz.NET scheduled jobs, EF Core/SQLite data layer |
| `CedarClerk.Core` | Document format + renderers. Zero external dependencies — pure C#, fully unit-tested |
| `CedarClerk.Localization` | `ErrorMessages.cs` (shared error strings) and `Languages.cs` (RU/EN constants) |
| `CedarClerk.Tests` | xUnit, references both `Core` and `Server` |

`CedarClerk.Server` subfolders:
- `Ai/` — `IAiEditProvider` + Anthropic/OpenAI implementations for the in-editor AI-edit feature (fix errors / "schizo-izer")
- `Bot/` — `TelegramBotService`, `BotChatAccess` (pure permission logic), `BotKnownChatSync`, Quartz job classes
- `Data/` — `CedarDbContext`, `Entities.cs` (all entities in one flat file)
- `Migrations/` — EF Core migrations
- `Translation/` — `ITranslationProvider` + Anthropic/OpenAI/DeepL implementations for auto-translate
- Top-level: one `XxxEndpoints.cs` static class per feature area (`AuthEndpoints`, `DraftEndpoints`, `BlogEndpoints`, `PostEndpoints`, `AssetEndpoints`, `ChannelEndpoints`, `ScheduledPostEndpoints`, `BillingEndpoints`), plus `SubscriptionPlan.cs` and `Program.cs`

`cedarclerk-web/src/app/`:
- `core/` — Angular services (`auth`, `billing`, `channels`, `comments`, `drafts`, `posts`, `telegram-link`, `theme`, `assets`) + `auth.guard.ts`
- `pages/` — route components: `editor` (the largest surface by far), `comments`, `stats`, `login`, `register`, `settings`
- `shared/` — `PopoverComponent`, `CedarLogoComponent` (the only genuinely reusable components — see `docs/DESIGN.md` for the gap around buttons/modals)
- `tiptap-extensions/` — custom TipTap nodes/marks whose HTML output is the shared contract with the backend renderers (e.g. `spoiler-mark.ts` ↔ `<tg-spoiler>` in `CedarToTelegramHtmlRenderer`)

## API style

Minimal APIs only, no MVC controllers. Each feature area is `public static class XxxEndpoints` with a single `MapXxxEndpoints(this WebApplication app)` extension method, wired flatly in `Program.cs`:
```csharp
app.MapAuthEndpoints();
app.MapDraftEndpoints();
app.MapBlogEndpoints();
app.MapPostEndpoints();
app.MapAssetEndpoints();
app.MapChannelEndpoints();
app.MapScheduledPostEndpoints();
app.MapBillingEndpoints();
```
Blog requests are routed separately, by hostname, before the rest: `app.MapWhen(ctx => ctx.Request.Host.Host == blogHost, ...)`. All API routes live under `/api/...`. Errors are either ad-hoc `Results.Json(new { error = "..." }, statusCode: ...)` at the call site, or a small per-endpoint result record (e.g. `PostEndpoints.PublishResult`) for logic factored out of the lambda. See `CedarClerk.Localization.ErrorMessages` for the handful of error strings reused across call sites — most errors are one-off inline literals by convention.

## Data model

`CedarDbContext : IdentityDbContext<ApplicationUser>` (SQLite). Every entity lives in one flat `CedarClerk.Server/Data/Entities.cs` (not one file per entity), uses a client-generated `Guid Id`, and owner-scoped rows carry a plain `string OwnerId` (+ optional `ApplicationUser? Owner` nav) rather than a strict FK-only model.

Entities: `ApplicationUser` (extends `IdentityUser`; `PlanTier`, `PlanExpiresAt`, `TrialUsedAt`, `FreeChannelCooldownUntil`, Telegram link fields, `PostSignature`, `StripeCustomerId`), `Payment` (audit of all billing events across providers), `AiUsage` (per-user per-UTC-day AI call counter), `Draft` (+ `DraftTranslation` for RU/EN), `Channel` (+ `ChannelStatSnapshot`, `ChannelPost` — see ADR-025), `Asset`, `Reaction`, `Comment`, `BotKnownChat` (+ `BotKnownChatAdmin`), `ScheduledPost`.

Ownership: nearly every table has an `OwnerId` and every endpoint filters by it — see the ownership-audit table in `docs/DECISIONS.md`. Public blog endpoints are the deliberate exception (filtered by `IsBlogPublished` instead, since blog visitors aren't authenticated users).

## Auth

ASP.NET Core Identity (`AddIdentityCore<ApplicationUser>`), cookie-based (`IdentityConstants.ApplicationScheme`), backed by the same SQLite DB via `AddEntityFrameworkStores<CedarDbContext>`. Registration is invite-code gated (`Cedar:InviteCode` config). 401/403 are returned directly instead of redirecting to a login page (`OnRedirectToLogin`/`OnRedirectToAccessDenied` overrides), since the client is a SPA. Telegram account linking is a separate, optional step for an already-authenticated user (HMAC-verified via `TelegramLoginVerifier` in Core) — not an alternate login method; see `docs/DECISIONS.md`.

## Scheduling (Quartz.NET)

Three jobs, registered in `Program.cs`:
- `PublishDueScheduledPostsJob` — every 1 minute, sends due `ScheduledPost` rows
- `SnapshotChannelStatsJob` — daily at 04:00 UTC (cron `0 0 4 * * ?`), records `ChannelStatSnapshot`
- `DowngradeExpiredPlansJob` — hourly, downgrades lapsed paid plans back to Free

## `.cedar` file format

A zip container (chosen 08.07.2026 over base64-in-JSON, which would have cost +33% size) — analogous to `.docx`/`.epub`. Contains `document.json` (`{ formatVersion, meta: {...}, doc: <TipTap JSON> }`) plus an `assets/` folder with original media files. `CedarPackage` (Core) handles roundtrip/corrupt-zip/version cases, covered by unit tests. Export/import guards against path-traversal and zip-bombs (`DraftEndpoints` import path). Translations are **not** currently included in `.cedar` export (deliberate, not yet done).

## Production environment & deploy

See `.claude/rules/production-environment.md` for the Pi/Cloudflare/systemd specifics this architecture assumes, and `.claude/rules/ef-migrations.md` / `.claude/rules/renderers.md` for the invariants that guard it.

Deploy (`Scripts/deploy.ps1`, from repo root):
1. `npm run build` in `cedarclerk-web/` → `cedarclerk-web/dist/cedarclerk-web/browser`
2. `dotnet publish CedarClerk.Server -c Release -o publish/`
3. Copy the Angular build output into `publish/wwwroot`
4. `ssh ... "sudo systemctl stop cedarclerk"`
5. `scp -r publish/* martycow@raspberrypi.local:/home/martycow/cedarclerk/app/`
6. `ssh ... "sudo systemctl start cedarclerk"`
7. Health-check loop against `https://cedarclerk.mooexe.dev/api/health` (10 tries, 3s apart)

`Migrate()` and `PRAGMA journal_mode=WAL;` run automatically on server startup (`Program.cs`), so a deploy applies pending migrations without a separate step — which is exactly why `.claude/rules/ef-migrations.md`'s "migrate immediately after any entity change" rule matters.

## Local development

- Server: `dotnet run --project CedarClerk.Server` (port 8080, bot disabled without a token — see `.claude/rules/telegram-bot.md`)
- Frontend: `ng serve` in `cedarclerk-web/` (proxies `/api` → `http://localhost:8080` via `proxy.conf.json`)
- Tests: `dotnet test` from repo root (xUnit; 162/162 green as of the last verified state, 11.07.2026)
- Frontend tests: `npm run test` in `cedarclerk-web/` (Vitest-backed via `@angular/build:unit-test`, not Karma)
- EF migrations: `dotnet ef migrations add <Name> --project CedarClerk.Server`
