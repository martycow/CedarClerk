# CLAUDE.md — Cedar Clerk

## Who you're working with
Marty (martycow) — C#/Unity game developer, knows Angular, does NOT know infrastructure/DevOps.
**Always communicate in Russian.** Use English technical terminology with Russian translations in braces on first use.
Workflow: vibe-coding — implement step by step, explain what you're doing concisely, wait for Marty's confirmation (terminal output / screenshot) before the next risky step.

## What this project is
Cedar Clerk — self-hosted personal publishing SaaS. A web rich-text editor whose posts are published to Telegram channels via a bot, and to mirrored blog pages. Being turned from a single-operator tool into a multi-tenant public SaaS (Phase 6, in progress).
- **CedarClerk.Server** — ASP.NET Core (.NET 8) API + static host for the frontend + Telegram bot host
- **CedarClerk.Core** — the document format and renderers (pure C#, unit-tested)
- **CedarClerk.Localization** — shared error strings and language constants
- **CedarClerk.Cli** — `cedar`, the operations console (Spectre.Console). Wraps `Scripts/*.ps1` and read-only `ssh`; see ADR-118 for what it deliberately will not do
- **CedarClerk.Tests** / **CedarClerk.Cli.Tests** — xUnit
- **cedarclerk-web** — Angular SPA (standalone components, signals, TipTap editor)

Document model: TipTap JSON stored in SQLite (`Draft.CedarJson`). One document → many renderers (Telegram HTML, blog HTML, `.cedar` export) is the core architectural idea — see `docs/ARCHITECTURE.md`.

**Since 10.08.2026 the product is turning towards indie game developers** (Phase 13, branch `indiedev_module`): a post becomes one document type among several, living inside a `Project`, alongside tasks, sprints and an asset index. It is a **module inside the same codebase, not a fork** (ADR-101) — read `docs/INDIEDEV.md` before touching anything in that area.

## Anti-desynchronization mechanism
Before implementation of anything, firstly read docs/PRD.md and docs/ARCHITECTURE.md. If you change ANY of your decisions, you must update docs/DECISIONS.md first, then write code.

## Stack
.NET 8 (minimal APIs, EF Core + SQLite, ASP.NET Identity, Quartz.NET) + Angular 21/TipTap 3 (standalone components, signals, Vitest). Full detail: `docs/ARCHITECTURE.md`.

## Key commands
| Task | Command |
|---|---|
| **Operations console** | `cedar` — menu; or `cedar status` / `logs` / `db` / `test` / `build` / `deploy` / `open` / `claude`. **Since ADR-119 this is the only way to build, test and deploy** — the logic lives in `CedarClerk.Cli/Pipelines/` and the old `.ps1` entry points are gone. Installed by `.\Scripts\install-cli.ps1` — **the first thing to run on a fresh clone, and again after changing the CLI**; `-Uninstall` removes it |
| **Everything is green?** | `cedar test` (backend + frontend + contrast; `--smoke` adds Playwright) |
| **Build everything locally** | `cedar build` (Angular + server + desktop shell; `--no-desktop`, `--desktop-only`, `--installer`, `--run`) |
| **Deploy** | `cedar deploy` — **asks before it stops production, default no.** Refuses anything but `master`, or a dirty tree. `--preflight` runs the checks and stops, `--skip-build` continues an interrupted upload, `--rollback` puts the previous release back, `--desktop` also publishes the installer (ADR-113, ADR-116, ADR-119) |
| Run server locally | `dotnet run --project CedarClerk.Server` (port 8080) |
| Run frontend locally | `ng serve` in `cedarclerk-web/` (proxies `/api` → 8080) |
| Open the product | `cedar open` (production) / `open desktop` / `open blog` / `open local` |
| Run the desktop app | `cedar open desktop`, or `cd CedarClerk.Desktop; npm start` (after a build) |
| Backend tests | `dotnet test` from repo root |
| Frontend tests | `npm run test` in `cedarclerk-web/` |
| Smoke suite | `.\Scripts\e2e.ps1` (scratch database, no bot token); `-Serve` leaves it running |
| New EF migration | `dotnet ef migrations add <Name> --project CedarClerk.Server` |

The three top rows are the ones to reach for. **`Scripts/deploy.ps1`, `build.ps1`, `test.ps1` and
`_git-guard.ps1` no longer exist** (12.08.2026, ADR-119) — that logic is `CedarClerk.Cli/Pipelines/`,
with tests. `GitGuard.cs` holds the branch/tree checks the guard used to; `test` and `build`
deliberately do **not** apply them, since running and building a feature branch is the normal case,
and only deploying does.

Two scripts stay, and both are real scripts rather than redirection: `e2e.ps1` (it owns a server
process, an isolated data directory and a browser — `cedar test --smoke` calls it) and
`install-cli.ps1` (installing `cedar` with `cedar` is a circle; this is where it is cut). If `cedar`
is ever broken, the way round needs nothing from that folder:
`dotnet run --project CedarClerk.Cli -- deploy --preflight`.

## Docs map
- `docs/DOCS-FLOW.md` — **read this first**: which doc is the source of truth for what, how an item travels Input.md → BACKLOG → TASKS → ROADMAP/CHANGELOG, and the three rules that keep them in sync
- `docs/PRODUCT.md` — what Cedar Clerk is, who it's for, pricing
- `docs/PRD.md` — shipped vs. open requirements, deferred/blocked items
- `docs/ARCHITECTURE.md` — solution layout, data model, API style, deploy pipeline
- `docs/DESIGN.md` — design tokens (colors/spacing/typography), component patterns
- `docs/DECISIONS.md` — ADR log: why things were built the way they were
- `docs/ROADMAP.md` — phase-by-phase execution status (the live plan — update it when closing items)
- `docs/BACKLOG.md` — the only source of open, not-yet-started ideas/features/tech-debt (kept separate from ROADMAP on purpose)
- `docs/UI-INVENTORY.md` — per-element inventory of the frontend UI (location, type, purpose, loading-state check) — update it when adding/changing a UI element
- `docs/integrations-setup.md` — payment/translation provider setup runbook
- `docs/admin-panel-scope.md` — scoping for the admin panel (IF2): what exists, what must be decided, build order
- `docs/INDIEDEV.md` — the indie-gamedev module (Phase 13): scope, data model, MUST/MIGHT. **Read before implementing any `T-120…T-137` row**
- `docs/DESKTOP.md` — how the desktop build works (Electron + the existing server as a sidecar)
- `docs/indiedev-design-prompt.md` — the brief handed to Claude Design for the module's screens. Copies design tokens verbatim, so **re-check it against `styles.scss` before each use**
- `TASKS.md` — short-horizon "what's next" list
- `CHANGELOG.md` — human-readable history by session/date

## Conventions
Backend: static `XxxEndpoints` classes (minimal APIs, no MVC), entities in one flat `Entities.cs`, GUID PKs, `Consts`/`ErrorMessages` for reused strings only. Frontend: standalone components, `inject()`, signals, thin RxJS→Promise services, `kebab-case.*.ts` naming. Full detail and rationale: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`.

## Branches (Marty's rule, 10.08.2026)
- **`master` — only the latest stable version.** Every commit on it is tagged with a version, and **every deploy is run from `master` and only from `master`.**
- **`dev` — general development.**
- **`indiedev_module`** — branched from `dev` for the indie-gamedev work, because the business model is not yet proven. May be deleted outright if it doesn't work out; keep the module reversible (ADR-101).
- **`LIVE` marks what is in production** (12.08.2026, ADR-118). `deploy.ps1` moves it onto HEAD after the health check passes, keeping the tag it replaces as `LIVE-PREV`; `-Rollback` moves it back. Local only — it is never pushed. A tag name points at one object, so "one commit at a time" needs no enforcement; what the preflight does check is whether `LIVE` still agrees with the version production answers.
- **Enforced since 10.08.2026**: the deploy refuses to run from a branch other than `master`, from a detached HEAD, or with uncommitted changes, and warns when HEAD carries no tag matching `Consts.CurrentVersion`. `-Force`/`--force` overrides and says what it is overriding. The checks moved from `Scripts/_git-guard.ps1` to `CedarClerk.Cli/Pipelines/GitGuard.cs` on 12.08.2026 (ADR-119) and gained tests on the way.

## Commits and versioning
- **Commit each substantial chunk of work** — a chunk can be several features or several bugs together, it does not have to be one item per commit. Don't leave a finished chunk uncommitted.
- **Commit messages are 3–4 words, maximum.** `Fix account menu`, `Add watermark`, `Blog comment cleanup`. No body, no bullet list, no explanation — the explanation belongs in `CHANGELOG.md`/`docs/ROADMAP.md`, not the message.
- **Bump the version periodically**: `0.9.0` → `0.9.1` → `0.9.2`. Bump the **middle** number (`0.9.x` → `0.10.0`) only after several large tasks land that genuinely change how the app feels to use — that's how `0.7.0`/`0.8.0`/`0.9.0` were used, one per phase. (Marty's wording calls the third number "minor" and the middle one "major"; the positions above are what he meant.)
- The version lives in `CedarClerk.Core/Consts.cs` (`CurrentVersion`). **Tag the commit with the bare number** — `git tag 0.9.1` — matching the existing `0.7.0`/`0.8.0`/`0.9.0` tags. Bump the const and the tag together, never one without the other.

## Hard rules (violating these has bitten us already)
Full text lives in `.claude/rules/*.md` — read the relevant one before touching that area:
1. **`.claude/rules/telegram-bot.md`** — 409 Conflict (one process per bot token), `sendRichMessage` HTML quirks, chat-discovery model
2. **`.claude/rules/ef-migrations.md`** — migrate immediately after any `Entities.cs` change; hand-edit renames
3. **`.claude/rules/renderers.md`** — escaping + unit-test invariants for `CedarClerk.Core` renderers
4. **`.claude/rules/destructive-operations.md`** — explain, then STOP and wait for confirmation
5. **`.claude/rules/secrets.md`** — never move secrets into the repo; rotate before cleanup if one leaks
6. **`.claude/rules/production-environment.md`** — droplet/Cloudflare/systemd assumptions not to break

## Verification workflow
- Local: `dotnet run --project CedarClerk.Server` (port 8080) + `ng serve` in `cedarclerk-web`. Login: marty@mooexe.dev (ask Marty for the password, do not store it)
- Tests: `dotnet test` from repo root
- Prod logs: `ssh -t martycow@deploy.mooexe.dev "sudo journalctl -u cedarclerk -n 50 --no-pager"` (asks for the sudo password; **without `sudo` the journal looks empty rather than refused** — `martycow` is not in `systemd-journal`)
- Test channel: @testingandfun ("Marty's Channel For Testing and Having Fun"). NEVER post to Dev Dairy Diary (the real channel) without explicit permission

