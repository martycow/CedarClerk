# CLAUDE.md — Cedar Clerk

## Who you're working with
Marty (martycow) — C#/Unity game developer, knows Angular. **Infrastructure: no longer a beginner** (updated 12.08.2026) — he moved production off the Raspberry Pi to a DigitalOcean droplet himself, and closed both items that move left open: `systemctl enable`, and a nightly `sqlite3 .backup` in cron with a healthchecks.io ping so a silent failure alerts. Explain infra work, don't skip it — but write to someone who runs `systemctl`, `crontab` and `sed` on a live server, not to someone who has never seen them. The sudo password is his alone, and writing commands on the droplet are handed to him to run (see `.claude/rules/production-environment.md`).
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

Document model: TipTap JSON stored in SQLite (`Draft.CedarJson`). One document → many renderers (Telegram HTML, blog HTML, `.cedar` export) is the core architectural idea — see `docs/tech/ARCHITECTURE.md`.

**Since 10.08.2026 the product is turned towards indie game developers** (Phase 13; MUST list complete 11.08.2026, merged to `master`): a post is one document type among several, living inside a `Project`, alongside tasks, sprints and an asset index. It is a **module inside the same codebase, not a fork** (ADR-101), behind `Cedar:Modules:IndieDev` — read `docs/product/INDIEDEV.md` before touching anything in that area.

## Anti-desynchronization mechanism
Before implementation of anything, firstly read docs/product/PRD.md and docs/tech/ARCHITECTURE.md. If you change ANY of your decisions, you must record the ADR first, then write code — since 18.08.2026 that means a new `docs/adr/ADR-xxx.md` (first line `# ADR-xxx — Title`) plus its row in the `docs/DECISIONS.md` index.

## Stack
.NET 8 (minimal APIs, EF Core + SQLite, ASP.NET Identity, Quartz.NET) + Angular 21/TipTap 3 (standalone components, signals, Vitest). Full detail: `docs/tech/ARCHITECTURE.md`.

## Key commands
| Task | Command |
|---|---|
| **Operations console** | `cedar` — menu; or `cedar status` / `logs` / `db` / `test` / `build` / `deploy` / `open` / `claude`. **Since ADR-119 this is the only way to build, test and deploy** — the logic lives in `CedarClerk.Cli/Pipelines/` and the old `.ps1` entry points are gone. Installed by `.\Scripts\install-cli.ps1` — **the first thing to run on a fresh clone, and again after changing the CLI**; `-Uninstall` removes it |
| **Everything is green?** | `cedar test` (backend + frontend + contrast; `--smoke` adds Playwright) |
| **Build everything locally** | `cedar build` (Angular + server + desktop shell; `--no-desktop`, `--desktop-only`, `--installer`, `--run`) |
| **Check it locally in a browser** | `cedar run` — builds front + back, serves `publish/` on `localhost:8080` against the dev database with the bot forced off, opens the browser; Ctrl+C stops it. `--no-build` reuses the last publish. Refuses if 8080 already answers (ADR-121) |
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

`Scripts/server/backup.sh` is a third kind: it does not run here at all. It is the source of truth
for the droplet's nightly backup, and the copy that runs (`~/bin/backup.sh`) is installed by hand —
no deploy path touches it.

## Docs map
`docs/` root holds only the high-level files (DOCS-FLOW, the DECISIONS index, the untracked INPUT_PROMPT); everything else lives in category folders — `product/ tasks/ design/ tech/ adr/ fleet/ knowledge_base/ for_user/ archive/` (+ `misc/` when something needs it). The taxonomy and placement rules: `docs/DOCS-FLOW.md` §Размещение.
- `docs/DOCS-FLOW.md` — **read this first**: which doc is the source of truth for what, how an item travels INPUT_PROMPT.md → BACKLOG → TASKS → ROADMAP/CHANGELOG, and the three rules that keep them in sync
- `docs/product/PRODUCT.md` — what Cedar Clerk is, who it's for, pricing
- `docs/product/BUSINESS.md` — the money side: what must be true before public registration opens, where the margin leaks, the four metrics worth counting, and the weekly/monthly checks
- `docs/knowledge_base/STACK.md` — every library, framework and external service, with what each costs and what breaks when it goes down
- `docs/product/MULTITENANCY.md` — what happens when there are users: where their blogs live, why the tier quotas outrun the disk, what self-hosted would actually require
- `docs/product/PRD.md` — shipped vs. open requirements, deferred/blocked items
- `docs/tech/ARCHITECTURE.md` — solution layout, data model, API style, deploy pipeline
- `docs/design/DESIGN.md` — design tokens (colors/spacing/typography), component patterns
- `docs/DECISIONS.md` — ADR log: why things were built the way they were. Since 18.08.2026 an **index** — one file per ADR in `docs/adr/` (122 at the split)
- `docs/tasks/ROADMAP.md` — phase-by-phase execution status (the live plan — update it when closing items)
- `docs/tasks/BACKLOG.md` — the only source of open, not-yet-started ideas/features/tech-debt (kept separate from ROADMAP on purpose)
- `docs/design/UI-INVENTORY.md` — per-element inventory of the frontend UI (location, type, purpose, loading-state check) — update it when adding/changing a UI element
- `docs/for_user/integrations-setup.md` — payment/translation provider setup runbook
- `docs/INPUT_PROMPT.md` — Marty's dynamic prompt inbox: "considered as a new prompt every time". **Untracked on purpose** (gitignored; Marty rewrites it at will) — check its mtime against the last "Input sweep" in ROADMAP. Content may predate the code — verify against it
- `docs/product/INDIEDEV.md` — the indie-gamedev module (Phase 13): scope, data model, MUST/MIGHT. **Read before implementing any `T-120…T-137` row**
- `docs/tech/DESKTOP.md` — how the desktop build works (Electron window onto production + local filesystem agent, ADR-117; the sidecar model is history)
- `docs/design/indiedev-design-prompt.md` — the brief handed to Claude Design for the module's screens (delivered 10.08; remaining ask — screens 10–11). Since 18.08 it carries **no verbatim token copy** — paste fresh values from `styles.scss` into its marked block before each run
- `TASKS.md` — short-horizon "what's next" list
- `CHANGELOG.md` — human-readable history by session/date

## Conventions
Backend: static `XxxEndpoints` classes (minimal APIs, no MVC), entities in one flat `Entities.cs`, GUID PKs, `Consts`/`ErrorMessages` for reused strings only. Frontend: standalone components, `inject()`, signals, thin RxJS→Promise services, `kebab-case.*.ts` naming. Full detail and rationale: `docs/tech/ARCHITECTURE.md`, `docs/design/DESIGN.md`.

**Comments (Marty, 12.08.2026 — he reads the code, not prose about it).** A comment earns its place only by carrying a *why* that cannot be read off the code: (1) an incident it prevents a repeat of, (2) an external constraint — an API's quirk, a platform's limit, (3) a deliberate choice against the obvious one. Everything else goes: no XML-doc restating a signature, no block explaining what the next five lines do, no essay above a private method. Default to zero comments, and prefer one dense sentence to a paragraph. A comment that describes code will outlive the code it describes and start lying — `Logo.cs` carried a description of a subtitle that had already changed.

## Branches (Marty's rule, 10.08.2026)
- **`master` — only the latest stable version.** Every commit on it is tagged with a version, and **every deploy is run from `master` and only from `master`.**
- **`dev` — general development.**
- **`indiedev_module`** — was branched from `dev` for the indie-gamedev work; **merged into `master` with v0.10.0 and deleted** (branch gone by 18.08.2026). The module lives in `master` behind `Cedar:Modules:IndieDev`; reversibility is the flag plus ADR-101, no longer a branch.
- **`LIVE` marks what is in production** (12.08.2026, ADR-118). `cedar deploy` moves it onto HEAD after the health check passes, keeping the tag it replaces as `LIVE-PREV`; `--rollback` moves it back. Local only — it is never pushed (re-deleted from origin 18.08.2026 after it leaked there a second time). A tag name points at one object, so "one commit at a time" needs no enforcement; what the preflight does check is whether `LIVE` still agrees with the version production answers.
- **Enforced since 10.08.2026**: the deploy refuses to run from a branch other than `master`, from a detached HEAD, or with uncommitted changes, and warns when HEAD carries no tag matching `Consts.CurrentVersion`. `-Force`/`--force` overrides and says what it is overriding. The checks moved from `Scripts/_git-guard.ps1` to `CedarClerk.Cli/Pipelines/GitGuard.cs` on 12.08.2026 (ADR-119) and gained tests on the way.

## Commits and versioning
- **Commit each substantial chunk of work** — a chunk can be several features or several bugs together, it does not have to be one item per commit. Don't leave a finished chunk uncommitted.
- **Commit messages are 3–4 words, maximum.** `Fix account menu`, `Add watermark`, `Blog comment cleanup`. No body, no bullet list, no explanation — the explanation belongs in `CHANGELOG.md`/`docs/tasks/ROADMAP.md`, not the message.
- **No trailers either** (Marty, 12.08.2026): no `Co-Authored-By`, no `Claude-Session` link, nothing after the subject line. Those are a harness default, and a trailer is a body — the same rule the line above states. `git commit -m "Fix account menu"` and nothing more.
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
7. **`.claude/rules/ui-changes.md`** — find the element's existing home in `docs/design/UI-INVENTORY.md` **before** adding a UI element, and update the inventory in the same commit

## Verification workflow
- Local: `dotnet run --project CedarClerk.Server` (port 8080) + `ng serve` in `cedarclerk-web`. Login: marty@mooexe.dev (ask Marty for the password, do not store it)
- Tests: `dotnet test` from repo root
- Prod logs: `cedar logs`, or `ssh martycow@periwinkle.mooexe.dev "journalctl -q -u cedarclerk -n 50 --no-pager"` — **no sudo needed**, the unit runs as `martycow`. Keep the `-q`: without it journalctl prints a "not seeing messages from other users" hint that reads like a refusal. **Always bound the query** — the service logs every EF statement, ~1.5M lines a day
- Test channel: @testingandfun ("Marty's Channel For Testing and Having Fun"). NEVER post to Dev Dairy Diary (the real channel) without explicit permission

