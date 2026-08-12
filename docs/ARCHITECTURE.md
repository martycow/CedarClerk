# Architecture

## Core idea: one document, many renderers

The internal post format is a single TipTap JSON document, stored in SQLite as `Draft.CedarJson`. It is never edited or interpreted per-target — instead, pure-C# renderers in `CedarClerk.Core` turn it into whatever output format a target needs:

```
                        ┌──────────────────────────────────────────┐
                        │   DigitalOcean droplet (Ubuntu 24.04)    │
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

Publishing targets (Phase 12, ADR-078; Telegram moved onto it 01.08.2026, T-085): `PostEndpoints.PublishAsync` is the network-agnostic half — resolve draft, language and target, pick the implementation by network, record the outcome on the target row. `IPublishTarget` in `CedarClerk.Server/Publishing/` is what a network must implement — name itself, describe its limits as data (`CedarClerk.Core.PublishCapabilities`, so the editor can warn before a send), and publish returning a receipt. It does **not** fetch statistics, delete/edit, or run its own connect flow. Credentials live on the `PublishTarget` entity, encrypted by `PublishTargetSecrets` with the DataProtection key ring under `CEDAR_DATA_DIR` — which makes the key ring and `cedar.db` a pair: restoring one without the other leaves credentials unreadable (by design; the owner reconnects). The blog is deliberately not a publish target.

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
- `Publishing/` — `IPublishTarget` (ADR-078) + `PublishTargetSecrets` (per-tenant credential encryption)
- `Translation/` — `ITranslationProvider` + Anthropic/OpenAI/DeepL implementations for auto-translate
- Top-level: one `XxxEndpoints.cs` static class per feature area (`AuthEndpoints`, `DraftEndpoints`, `BlogEndpoints`, `PostEndpoints`, `AssetEndpoints`, `ChannelEndpoints`, `ScheduledPostEndpoints`, `BillingEndpoints`), plus `SubscriptionPlan.cs` and `Program.cs`

`cedarclerk-web/src/app/`:
- `core/` — Angular services (`auth`, `billing`, `channels`, `comments`, `drafts`, `posts`, `telegram-link`, `theme`, `assets`) + `auth.guard.ts`
- `pages/` — route components: `editor` (the largest surface by far), `comments`, `stats`, `login`, `register`, `settings`
- `shared/` — `PopoverComponent`, `CedarLogoComponent` (the only genuinely reusable components — see `docs/DESIGN.md` for the gap around buttons/modals)
- `tiptap-extensions/` — custom TipTap nodes/marks whose HTML output is the shared contract with the backend renderers (e.g. `spoiler-mark.ts` ↔ `<tg-spoiler>` in `CedarToTelegramHtmlRenderer`)

## Modules (ADR-101, 10.08.2026)

A **module** is a set of endpoints and screens behind a config flag — not a separate project, process, database or `DbContext`. The first one is the indie-gamedev toolkit (`docs/INDIEDEV.md`); the shape is meant to be reusable if a second appears.

- Backend: `CedarClerk.Server/Modules/<Name>/` holding the same `static class XxxEndpoints` convention as the top-level feature areas, registered in `Program.cs` behind `Cedar:Modules:<Name>`.
- Frontend: `cedarclerk-web/src/app/modules/<name>/` with lazy routes. This costs nothing because every route is already `loadComponent` with background preloading (T-092/ADR-076) — a disabled module simply never fetches its chunks.
- The flag reaches the client in the `/api/me` response; a guard hides the menu entries, not only the pages.
- **The schema is shared.** A second `DbContext` over the same SQLite file would mean two independent `Database.Migrate()` calls on startup, which `.claude/rules/ef-migrations.md` does not survive — and the module's entities reference `Draft` and `ApplicationUser` directly, so a context boundary would fall exactly across the links the module exists for.
- Module entities live in their own `Data/Entities.<Name>.cs`. The "one flat `Entities.cs`" convention exists to avoid a file per entity, and a file per module does not violate it.

A module **adds**; it never replaces an existing screen. That is what makes it reversible: turning the flag off restores today's application whole.

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

Entities (`PublishTarget` added 01.08.2026, T-084): `ApplicationUser` (extends `IdentityUser`; `PlanTier`, `PlanExpiresAt`, `TrialUsedAt`, `FreeChannelCooldownUntil`, Telegram link fields, `PostSignature`, `StripeCustomerId`), `Payment` (audit of all billing events across providers), `AiUsage` (per-user per-UTC-day AI call counter), `Draft` (+ `DraftTranslation` for RU/EN), `Channel` (+ `ChannelStatSnapshot`, `ChannelPost` — see ADR-025), `Asset`, `Reaction`, `Comment`, `BotKnownChat` (+ `BotKnownChatAdmin`), `ScheduledPost`.

Ownership: nearly every table has an `OwnerId` and every endpoint filters by it — see the ownership-audit table in `docs/DECISIONS.md`. Public blog endpoints are the deliberate exception (filtered by `IsBlogPublished` instead, since blog visitors aren't authenticated users).

**A `Draft` is a document, not specifically a post** (ADR-102, planned in Phase 13): `Draft.DocumentType` (default `post`, so every existing row is already correct) and `Draft.ProjectId` turn the same entity into a game-design doc, a script or a changelog. The reason it is a column rather than a new entity: `Draft` is really "a TipTap document with autosave, revision history, translations, tags and a folder", and every one of those is needed verbatim by the other document types — a parallel entity would mean duplicating the most safety-critical code in the project (the ADR-065/066/067 save guards). The cost is that publishing columns (`BlogSlug`, `WatermarkText`, `LastTelegram*`…) sit unused on non-post documents; that is tracked as `T-136`, not pretended away.

## Auth

ASP.NET Core Identity (`AddIdentityCore<ApplicationUser>`), cookie-based (`IdentityConstants.ApplicationScheme`), backed by the same SQLite DB via `AddEntityFrameworkStores<CedarDbContext>`. Registration is invite-code gated (`Cedar:InviteCode` config). 401/403 are returned directly instead of redirecting to a login page (`OnRedirectToLogin`/`OnRedirectToAccessDenied` overrides), since the client is a SPA. Telegram account linking is a separate, optional step for an already-authenticated user (HMAC-verified via `TelegramLoginVerifier` in Core) — not an alternate login method; see `docs/DECISIONS.md`.

**There is one installation, and identity is simply its own (ADR-117).** ADR-108's `Cedar:Auth:Upstream` — the desktop verifying credentials against production while keeping its own data — is gone, along with `UpstreamAuth`. It existed to make one email mean one person across two databases; with one database the problem it solved does not arise. The desktop is a window onto this installation and signs in against it like any browser. `ApplicationUser.RemoteUserId` survives as a vestigial column: dropping one in SQLite rebuilds `AspNetUsers`, which Identity touches on every authorized request, and that is a real risk for no return.

## Scheduling (Quartz.NET)

Three jobs, registered in `Program.cs`:
- `PublishDueScheduledPostsJob` — every 1 minute, sends due `ScheduledPost` rows
- `SnapshotChannelStatsJob` — daily at 04:00 UTC (cron `0 0 4 * * ?`), records `ChannelStatSnapshot`
- `DowngradeExpiredPlansJob` — hourly, downgrades lapsed paid plans back to Free

## Time: UTC on the wire, Pacific on the page (ADR-115)

The server stores and transports instants in UTC — `DateTime.UtcNow` everywhere, file times as
`LastWriteTimeUtc`, and `UtcDateTimeConverter` guarantees every `DateTime` leaves the API as UTC with
a trailing `Z`. That converter is not cosmetic: SQLite cannot store `DateTimeKind`, so EF returns
`Unspecified` for values that are UTC in fact, they serialized without a suffix, and a browser reads
an offset-less timestamp as **local time** — the app was showing times seven hours out everywhere the
one hand-rolled `utcDate()` helper had not been applied.

Everything a human reads is rendered in one display zone, named once per side:
`Consts.General.DisplayTimeZone` (backend, used through `CedarClerk.Core.DisplayTime` and the
`…Local` wrappers on `BlogDateFormatter`) and `DISPLAY_TIME_ZONE` in `core/display-time.ts` (frontend,
used through the `zonedDate` pipe, which replaced every `| date:`). It is `America/Los_Angeles` rather
than a fixed −8 because Los Angeles is on PDT for most of the year, and the blog labels the zone
(`PDT`/`PST`) while the app does not — readers are worldwide, the operator is not. Machine-facing
timestamps stay UTC: RSS `pubDate`, `<time datetime="…Z">`, and every API field.

When timezones become per-user, those two constants are the place that changes.

## `.cedar` file format

A zip container (chosen 08.07.2026 over base64-in-JSON, which would have cost +33% size) — analogous to `.docx`/`.epub`. Contains `document.json` (`{ formatVersion, meta: {...}, doc: <TipTap JSON> }`) plus an `assets/` folder with original media files. `CedarPackage` (Core) handles roundtrip/corrupt-zip/version cases, covered by unit tests. Export/import guards against path-traversal and zip-bombs (`DraftEndpoints` import path). Translations are **not** currently included in `.cedar` export (deliberate, not yet done).

## Production environment & deploy

See `.claude/rules/production-environment.md` for the droplet/Cloudflare/systemd specifics this architecture assumes, and `.claude/rules/ef-migrations.md` / `.claude/rules/renderers.md` for the invariants that guard it.

**Where this logic lives changed on 12.08.2026 (ADR-119).** The build, the test run and the deploy are
C# in `CedarClerk.Cli/Pipelines/`, and `Scripts/deploy.ps1`, `build.ps1`, `test.ps1` and
`_git-guard.ps1` are **gone** — one implementation, one entrance, which is `cedar`.

- `Pipelines/GitGuard.cs` — branch, clean tree, version tag, and the `LIVE`/`LIVE-PREV` tags (ADR-118 d12)
- `Pipelines/BuildPipeline.cs` — Angular, the portable server publish that ships, the self-contained
  desktop server, Electron, the installer
- `Pipelines/TestPipeline.cs` — backend, frontend units, the contrast contract, and `e2e.ps1` for smoke
- `Pipelines/DeployPipeline.cs` — the pipeline below, **master only, clean tree only** (T-138)
- `Pipelines/StageBoard.cs` — the live screen all three run behind: the plan drawn up front, timings,
  and a running step's own detail (upload bar, braille throughput chart)
- `Scripts/e2e.ps1` — the smoke suite against a scratch database with no bot token. **Still a script**:
  it owns a server process and an environment, which is what a shell script is for. `cedar test
  --smoke` runs it as a phase
- `Scripts/install-cli.ps1` — packs and installs `cedar` as a .NET global tool. **Still a script**, and
  necessarily so: it is what makes the name exist, and the first thing to run on a fresh clone

Deploy (`cedar deploy`). Rewritten 11.08.2026 (ADR-113) so that **everything slow happens while the old
version is still serving**; the service is stopped only for two directory renames, and the command asks
before that swap with the default set to no:
1. Preflight: git guard (branch `master`, clean tree, HEAD tagged with `Consts.CurrentVersion` — the tag
   is a warning only), `tar` on PATH, and one round trip that reports the service state, free disk and
   what is in `app/` today
2. `npm run build` in `cedarclerk-web/` → `cedarclerk-web/dist/cedarclerk-web/browser`
3. `dotnet publish CedarClerk.Server -c Release -o publish/`
4. Copy the Angular build output into `publish/wwwroot`
5. Pack `publish/` into one `cedar-<version>.tar.gz`, cached in `%TEMP%\cedarclerk-deploy` and keyed to a
   signature of the publish folder, so a re-run ships byte-identical bytes and can continue a transfer
6. Upload it as a **single resumable stream** (`stat -c %s` on the far side, then the local tail piped
   into `cat >>`) — a dropped connection continues from the byte it reached, not from zero
7. Verify sha256 on both sides, unpack into `app.new`, check `CedarClerk.Server.dll`, `wwwroot/index.html`
   and the file count. Production is still untouched up to this point — any failure above aborts with the
   old version still running
8. Stop the service, `app` → `app.prev`, `app.new` → `app`, start it (measured downtime: ~1s)
9. Health-check loop against `https://cedarclerk.mooexe.dev/api/health` (40 tries, 3s apart), which must
   answer with the version that was just built

`--skip-build` re-ships what is already in `publish/` (this is how a dropped upload is continued),
`--rollback` swaps `app.prev` back in and restarts, `--force` turns the git guard into a warning,
`--desktop` adds the installer steps (ADR-116), and `--preflight` runs step 1 and stops. `--dry-run`
executes nothing at all — neither processes nor file deletions (`ICommandRunner` and `IFileWriter` are
both swapped for it).

`-Desktop` adds two steps **after** the health check, so nothing here can affect the site: it builds the
installer (`build.ps1 -DesktopOnly -Installer`) and publishes it into `data/downloads/` — `.exe` and
`.blockmap` staged, checksummed and moved into place first, `latest.yml` written last, older installers
pruned to the last two. That directory is the one place a deploy writes inside `data/`, and it is what
`https://cedarclerk.mooexe.dev/downloads` serves for the desktop shell's self-update (ADR-116). Without
the flag the deploy does not touch the desktop at all, so the site's version and the published
installer's version legitimately differ — the deploy report prints which one is live.

`Migrate()` and `PRAGMA journal_mode=WAL;` run automatically on server startup (`Program.cs`), so a deploy applies pending migrations without a separate step — which is exactly why `.claude/rules/ef-migrations.md`'s "migrate immediately after any entity change" rule matters.

## Desktop distribution (ADR-104/116/117)

A window onto this installation plus a process that can read one machine's disk — not a second application and, since ADR-117, not a second database either. The Electron shell loads `https://cedarclerk.mooexe.dev/projects` and starts the published `CedarClerk.Server` with `Cedar:Agent:Enabled`, which strips it to `/agent/*`: walk a folder, stat a file, render a thumbnail. Neither the server nor the frontend is forked.

The reason it exists is the asset index (ADR-107) — only a process on the developer's own machine can walk a game project's folder. **The division of labour is the whole design: the agent reads the disk, the page uploads what it found.** The page already holds a session cookie, so the agent needs no credentials and the shell does no authentication; the work also ends up inside an ordinary screen with a progress bar rather than in an unobservable background process. Mechanics, the two locks on the agent, and the file-versus-fingerprint distinction: `docs/DESKTOP.md`.

`CEDAR_DATA_DIR` is no longer part of this story — the desktop stores nothing. It still decides where the droplet keeps SQLite and media (`/home/martycow/cedarclerk/data`). One server change came out of ADR-104 and stayed: the listening address used to be a literal in `app.Run(Consts.URLs.Localhost)`, so `ASPNETCORE_URLS` could not override it, and `Cedar:Urls` now exists for the agent's free port.

**Updates come from our own server** (ADR-116): `electron-updater`'s generic provider reads `https://cedarclerk.mooexe.dev/downloads/latest.yml`, which `DownloadEndpoints` serves as plain static files out of `CEDAR_DATA_DIR/downloads`. There is no update service and no third-party account — the whole protocol is a manifest, an installer and a blockmap in one folder, put there by `deploy.ps1 -Desktop`.

## Local development

- Server: `dotnet run --project CedarClerk.Server` (port 8080, bot disabled without a token — see `.claude/rules/telegram-bot.md`)
- Frontend: `ng serve` in `cedarclerk-web/` (proxies `/api` → `http://localhost:8080` via `proxy.conf.json`)
- Tests: `dotnet test` from repo root (xUnit; 162/162 green as of the last verified state, 11.07.2026)
- Frontend tests: `npm run test` in `cedarclerk-web/` (Vitest-backed via `@angular/build:unit-test`, not Karma)
- EF migrations: `dotnet ef migrations add <Name> --project CedarClerk.Server`
