# AGENTS.md — Cedar Clerk

## Who you're working with
Marty (martycow) — C#/Unity game developer, knows Angular. **Infrastructure: no longer a beginner** (updated 12.08.2026) — he moved production to a DigitalOcean droplet himself and set up the nightly database backup in cron. Explain infra work, but write to someone who runs `systemctl`, `crontab` and `sed` on a live server.
**Always communicate in Russian.** Use English technical terminology with Russian translations in braces on first use.
Workflow: vibe-coding — implement step by step, explain what you're doing concisely, wait for Marty's confirmation (terminal output / screenshot) before the next risky step.

## What this project is
Cedar Clerk — self-hosted personal publishing SaaS. A web rich-text editor whose posts are published to Telegram channels via a bot, and to mirrored blog pages. Being turned from a single-operator tool into a multi-tenant public SaaS (Phase 6, in progress).
- **CedarClerk.Server** — ASP.NET Core (.NET 8) API + static host for the frontend + Telegram bot host
- **CedarClerk.Core** — the document format and renderers (pure C#, unit-tested)
- **CedarClerk.Localization** — shared error strings and language constants
- **CedarClerk.Tests** — xUnit
- **cedarclerk-web** — Angular SPA (standalone components, signals, TipTap editor)

Document model: TipTap JSON stored in SQLite (`Draft.CedarJson`). One document → many renderers (Telegram HTML, blog HTML, `.cedar` export) is the core architectural idea — see `docs/ARCHITECTURE.md`.

## Anti-desynchronization mechanism
Before implementation of anything, firstly read docs/PRD.md and docs/ARCHITECTURE.md. If you change ANY of your decisions, you must record the ADR first, then write code — since 18.08.2026 that means a new `docs/adr/ADR-xxx.md` (first line `# ADR-xxx — Title`) plus its row in the `docs/DECISIONS.md` index.

## Stack
.NET 8 (minimal APIs, EF Core + SQLite, ASP.NET Identity, Quartz.NET) + Angular 21/TipTap 3 (standalone components, signals, Vitest). Full detail: `docs/ARCHITECTURE.md`.

## Key commands
| Task | Command |
|---|---|
| Run server locally | `dotnet run --project CedarClerk.Server` (port 8080) |
| Run frontend locally | `ng serve` in `cedarclerk-web/` (proxies `/api` → 8080) |
| Backend tests | `dotnet test` from repo root |
| Frontend tests | `npm run test` in `cedarclerk-web/` |
| Production build | `npm run build` (Angular) + `dotnet publish CedarClerk.Server -c Release` |
| Deploy | `cedar deploy` (asks first; ADR-119). `.\Scripts\deploy.ps1` no longer exists |
| New EF migration | `dotnet ef migrations add <Name> --project CedarClerk.Server` |

## Docs map
- `docs/PRODUCT.md` — what Cedar Clerk is, who it's for, pricing
- `docs/PRD.md` — shipped vs. open requirements, deferred/blocked items
- `docs/ARCHITECTURE.md` — solution layout, data model, API style, deploy pipeline
- `docs/DESIGN.md` — design tokens (colors/spacing/typography), component patterns
- `docs/DECISIONS.md` — ADR log: why things were built the way they were. Since 18.08.2026 an **index** — one file per ADR in `docs/adr/` (122 at the split)
- `docs/ROADMAP.md` — phase-by-phase execution status (the live plan — update it when closing items)
- `docs/BACKLOG.md` — the only source of open, not-yet-started ideas/features/tech-debt (kept separate from ROADMAP on purpose)
- `docs/UI-INVENTORY.md` — per-element inventory of the frontend UI (location, type, purpose, loading-state check) — update it when adding/changing a UI element
- `docs/for_user/integrations-setup.md` — payment/translation provider setup runbook
- `docs/INPUT_PROMPT.md` — Marty's dynamic prompt inbox: "considered as a new prompt every time". **Untracked on purpose** (gitignored; Marty rewrites it at will) — check its mtime against the last "Input sweep" in ROADMAP. Content may predate the code — verify against it
- `TASKS.md` — short-horizon "what's next" list
- `CHANGELOG.md` — human-readable history by session/date

## Conventions
Backend: static `XxxEndpoints` classes (minimal APIs, no MVC), entities in one flat `Entities.cs`, GUID PKs, `Consts`/`ErrorMessages` for reused strings only. Frontend: standalone components, `inject()`, signals, thin RxJS→Promise services, `kebab-case.*.ts` naming. Full detail and rationale: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`.

## Commits and versioning
- **Commit each substantial chunk of work** — a chunk can be several features or several bugs together, it does not have to be one item per commit. Don't leave a finished chunk uncommitted.
- **Commit messages are 3–4 words, maximum.** `Fix account menu`, `Add watermark`, `Blog comment cleanup`. No body, no bullet list, no explanation — the explanation belongs in `CHANGELOG.md`/`docs/ROADMAP.md`, not the message.
- **Bump the version periodically**: `0.9.0` → `0.9.1` → `0.9.2`. Bump the **middle** number (`0.9.x` → `0.10.0`) only after several large tasks land that genuinely change how the app feels to use — that's how `0.7.0`/`0.8.0`/`0.9.0` were used, one per phase. (Marty's wording calls the third number "minor" and the middle one "major"; the positions above are what he meant.)
- The version lives in `CedarClerk.Core/Consts.cs` (`CurrentVersion`). **Tag the commit with the bare number** — `git tag 0.9.1` — matching the existing `0.7.0`/`0.8.0`/`0.9.0` tags. Bump the const and the tag together, never one without the other.

## Hard rules (violating these has bitten us already)
Full text lives in `.Codex/rules/*.md` — read the relevant one before touching that area:
1. **`.Codex/rules/telegram-bot.md`** — 409 Conflict (one process per bot token), `sendRichMessage` HTML quirks, chat-discovery model
2. **`.Codex/rules/ef-migrations.md`** — migrate immediately after any `Entities.cs` change; hand-edit renames
3. **`.Codex/rules/renderers.md`** — escaping + unit-test invariants for `CedarClerk.Core` renderers
4. **`.Codex/rules/destructive-operations.md`** — explain, then STOP and wait for confirmation
5. **`.Codex/rules/secrets.md`** — never move secrets into the repo; rotate before cleanup if one leaks
6. **`.Codex/rules/production-environment.md`** — droplet/Cloudflare/systemd assumptions not to break

## Verification workflow
- Local: `dotnet run --project CedarClerk.Server` (port 8080) + `ng serve` in `cedarclerk-web`. Login: marty@mooexe.dev (ask Marty for the password, do not store it)
- Tests: `dotnet test` from repo root
- Prod logs: `cedar logs`, or `ssh martycow@periwinkle.mooexe.dev "journalctl -q -u cedarclerk -n 50 --no-pager"` — no sudo needed, the unit runs as `martycow`; keep `-q` and always bound the query (~1.5M lines a day)
- Test channel: @testingandfun ("Marty's Channel For Testing and Having Fun"). NEVER post to Dev Dairy Diary (the real channel) without explicit permission

