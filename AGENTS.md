# AGENTS.md — Cedar Clerk

Shared root context for every AI tool working in this repo (Claude Code, Codex CLI, others). Tool-specific files (`CLAUDE.md`, …) point back here and add only what's genuinely tool-specific.

## What this project is

Cedar Clerk — blog platform application for indie creators.
A web rich-text editor whose posts are published to Telegram channels via a bot, to mirrored blog pages, and other social media. Being turned from a single-operator tool into a multi-tenant public SaaS.

- **CedarClerk.Server** — ASP.NET Core (.NET 10) API + static host for the frontend + Telegram bot host
- **CedarClerk.Core** — the document format and renderers (pure C#, unit-tested)
- **CedarClerk.Localization** — language catalogs, UI/email/public-page text, formatting and language rules; see `docs/tech/LOCALIZATION.md`
- **CedarClerk.Tests** — xUnit application tests.
- **cedarclerk-web** — Angular SPA (standalone components, signals, TipTap editor)
- **CedarClerk.Desktop** — Electron window onto production plus a local filesystem agent (ADR-117, ADR-312); `docs/tech/DESKTOP.md`

It is a **module inside the same codebase, not a fork** (ADR-101), behind `Cedar:Modules:IndieDev` — read `docs/product/INDIEDEV.md` before touching anything in that area.

## Anti-desynchronization mechanism

Before implementation of anything, firstly read `docs/product/PRD.md` and `docs/tech/ARCHITECTURE.md`. If you change ANY decision, record the ADR first, then write code: a new `docs/adr/ADR-xxx.md` (copy of `docs/templates/ADR_Template.md`, first line `# ADR-xxx — Title`) plus its row in the `docs/DECISIONS.md` index. A choice no ADR covers stops the work and becomes a `Q-xx` row in `docs/tasks/BACKLOG.md`; do not guess.

This repo runs the Moo.exe pipeline (`../Docs_AI/Pipeline.md`) under its own names: the inbox is `docs/INPUT_PROMPT.md`, tasks are `T-xxx`, and `docs/DOCS-FLOW.md` is this repo's map of it. Code reviewed by a different model than the one that wrote it; a review is input until the owner turns findings into rows.

## Stack

To see the product's architecture, see `docs/tech/ARCHITECTURE.md`.
.NET 10 (APIs, EF Core + SQLite, ASP.NET Identity, Quartz.NET) + Angular 21/TipTap 3 (standalone components, signals, Vitest).

## Key commands

No operations console: the MooTool `cedar` CLI is gone (ADR-304); a Rust/Ratatui replacement is `T-393`. Commands are bash (macOS is the primary dev machine).

| Task | Command |
|---|---|
| Server, bot off | `mkdir -p CedarClerk.Server/wwwroot && ASPNETCORE_ENVIRONMENT=LocalNoBot ASPNETCORE_URLS=http://localhost:8080 Cedar__Telegram__BotToken=' ' dotnet run --project CedarClerk.Server --no-launch-profile` — see `.claude/rules/telegram-bot.md` §1 |
| Frontend | `npm start` in `cedarclerk-web/` (proxies `/api` → 8080) |
| Backend tests | `dotnet test` |
| Full gate (before deploy) | `dotnet test` + in `cedarclerk-web/`: `npm test`, `npm run check:icons`, `check:contrast`, `check:density`, `npm run build` |
| Smoke suite | `pwsh Scripts/e2e.ps1` (needs `brew install powershell`; `-Serve` keeps it running) |
| New migration | `dotnet ef migrations add <Name> --project CedarClerk.Server` |
| Prod logs | `ssh martycow@periwinkle.mooexe.dev "journalctl -q -u cedarclerk -n 50 --no-pager"` |
| Deploy | `bash docs/for_user/deploy.sh` (`--build-only` to dry-run the build). Gate, upload, stop, backup, swap `app`/`app.prev`, start, health check, then moves `LIVE`/`LIVE-PREV`. From `master` only, clean tree, HEAD tagged with `Consts.CurrentVersion`. Runbook: `docs/for_user/deploy.md` (ADR-308). |

`Scripts/server/backup.sh` does not run here: it is the source of the droplet's nightly backup, installed by hand as `~/bin/backup.sh`.

## Docs map

`docs/` root holds only the high-level files (DOCS-FLOW, the DECISIONS index, the untracked INPUT_PROMPT); everything else lives in category folders — `product/ tasks/ design/ tech/ adr/ fleet/ knowledge_base/ for_user/ archive/` (+ `misc/` when something needs it). The taxonomy and placement rules: `docs/DOCS-FLOW.md` §File placement.

- `docs/DOCS-FLOW.md` — **read this first**: which doc is the source of truth for what, how an item travels INPUT_PROMPT.md → BACKLOG → TASKS → CHANGELOG, and the three rules that keep them in sync
- `docs/templates/` — copies of the ADR, brief, review and manual masters from `Docs_AI/Templates/`. Copy, never fill in place
- `docs/briefs/` — `BR-xxx.md`, one per working session: decisions made, actions taken, actions left, warnings, errors. The next session reads the latest first
- `docs/reviews/` — `REV-xxx_<name>.md`, independent reviews by another model (created with the first one)
- `docs/product/PRODUCT.md` — what Cedar Clerk is, who it's for, pricing
- `docs/product/BUSINESS.md` — the money side: what must be true before public registration opens, where the margin leaks, the four metrics worth counting, and the weekly/monthly checks
- `docs/knowledge_base/STACK.md` — every library, framework and external service, with what each costs and what breaks when it goes down
- `docs/knowledge_base/RESEARCH-2026-09.md` — API and terms research for the research-only backlog rows (IndieDB/LinkedIn write APIs, IGDB autofill, Telegram first comment, events database); each section ends in a verdict a scoping row can be written from
- `docs/product/MULTITENANCY.md` — what happens when there are users: where their blogs live, why the tier quotas outrun the disk, what self-hosted would actually require
- `docs/product/PRD.md` — shipped vs. open requirements, deferred/blocked items
- `docs/tech/ARCHITECTURE.md` — solution layout, data model, API style, deploy pipeline
- `docs/design/DESIGN.md` — design tokens (colors/spacing/typography), component patterns
- `docs/DECISIONS.md` — ADR index; one file per ADR in `docs/adr/`
- `docs/tasks/BACKLOG.md` — the only source of open, not-yet-started ideas/features/tech-debt
- `docs/tasks/TASKS.md` — short-horizon "what's in progress now" list; its **Notes** section is also the fastest place to check current production version and active branch
- `docs/tasks/CHANGELOG.md` — human-readable shipped history by session/date
- `docs/design/UI-INVENTORY.md` — per-element inventory of the frontend UI (location, type, purpose, loading-state check) — update it when adding/changing a UI element
- `docs/for_user/` — manuals: `integrations-setup.md` (provider keys), `deploy.md` (release), `desktop-build.md` (installers). Numbered steps, nothing else
- `docs/INPUT_PROMPT.md` — the dynamic prompt inbox: "considered as a new prompt every time". **Untracked on purpose** (gitignored; rewritten at will) — check its mtime against the last "Input sweep" note in `docs/tasks/CHANGELOG.md`. Content may predate the code — verify against it
- `docs/product/INDIEDEV.md` — the indie-gamedev module (Phase 13): scope, data model, MUST/MIGHT. **Read before implementing any `T-120…T-137` row**
- `docs/tech/SECURITY.md` — the threat model: assets, trust boundaries, a STRIDE table with each mitigation cited, and the gaps still open before registration opens
- `docs/tech/DESKTOP.md` — how the desktop build works (Electron window onto production + local filesystem agent, ADR-117)
- `docs/tech/QA.md` — the permanent verification checklist by surface
- `docs/knowledge_base/TERMINOLOGY.md` — canonical vocabulary (the `cedar-terminology` skill loads it)
- `docs/archive/incidents.md` — index of every recorded incident and what guards against a repeat

## Conventions

Backend: static `XxxEndpoints` classes (minimal APIs, no MVC), entities in one flat `Entities.cs`, GUID PKs, `Consts`/`ErrorMessages` for reused strings only. Frontend: standalone components, `inject()`, signals, thin RxJS→Promise services, `kebab-case.*.ts` naming. Full detail and rationale: `docs/tech/ARCHITECTURE.md`, `docs/design/DESIGN.md`.

**Language — repository content is English, no exceptions except `docs/for_user/`.** Every file that
exists to give a human or an AI tool context is written in English, even though the working chat is
Russian: `AGENTS.md`/`CLAUDE.md`, everything under `docs/` outside `docs/for_user/`, `.claude/rules|skills/*`
(and their `.agents/` mirrors), code comments, commit messages. `docs/for_user/` is the one exception —
those are runbooks meant to be followed by hand, and stay in whatever language reads fastest for the
maintainer. This does **not** reach into the application itself: Russian is a real, tested UI
language (`CedarClerk.Localization`, `RussianDeclensions.cs`, `ru.ts`, glossary/slug tests with
Russian fixtures) — that is product code and reader-facing content, not context, and keeps its own
i18n rules.

**Docs — human-readable first, concise where the facts allow.** Prefer short declarative sentences over multi-clause chains; reach for a bullet list or table before a wall of prose when the content is naturally a list; don't restate context a linked doc already carries. Density is fine when the fact genuinely needs it (an ADR's reasoning, a rule's specific incident) — the goal is nothing wasted, not an artificially short file. Applies to everything under `docs/`, `AGENTS.md`/`CLAUDE.md` and `.claude/rules|skills/*` — not to commit messages (already covered under Commits) or to code comments (next paragraph).

**Comments — the maintainer reads the code, not prose about it.** A comment earns its place only by carrying a *why* that cannot be read off the code: (1) an incident it prevents a repeat of, (2) an external constraint — an API's quirk, a platform's limit, (3) a deliberate choice against the obvious one. Everything else goes: no XML-doc restating a signature, no block explaining what the next five lines do, no essay above a private method. Default to zero comments, and prefer one dense sentence to a paragraph. A comment that describes code will outlive the code it describes and start lying — `Logo.cs` carried a description of a subtitle that had already changed. **Never write what changed and when** — no dates, no "moved/closed/fixed on DD.MM", no "used to be X" in comments or doc prose — change history lives in git and CHANGELOG only. Such notes get deleted on sight.

## Branches

- **`master`** — the latest stable version; **every deploy runs from `master` and only from `master`.** Release commits carry a bare version tag.
- **`dev`** — unused.
- Feature work may use a short-lived branch (`claude/…`, `codex/…`) merged into `master`. The indie-gamedev module lives in `master` behind `Cedar:Modules:IndieDev` (ADR-101).
- **`LIVE` marks what is in production** (ADR-118), `LIVE-PREV` its rollback target. `deploy.sh` moves them after a successful deploy. Local only — never push them; a pushed `LIVE` has leaked to origin twice. The preflight checks that `LIVE` agrees with the version production answers.
- Deploy only from `master`, clean tree, HEAD tagged with `Consts.CurrentVersion`; `deploy.sh` enforces it.

## Commits and versioning

- **Commit each substantial chunk of work** — a chunk can be several features or several bugs together, it does not have to be one item per commit. Don't leave a finished chunk uncommitted.
- **Commit messages are 3–4 words, maximum.** `Fix account menu`, `Add watermark`, `Blog comment cleanup`. No body, no bullet list, no explanation — the explanation belongs in `docs/tasks/CHANGELOG.md`, not the message.
- **No trailers either**: no `Co-Authored-By`, no session-link, nothing after the subject line. Those are a harness default, and a trailer is a body — the same rule the line above states. `git commit -m "Fix account menu"` and nothing more.
- **Bump the version periodically**: `0.9.0` → `0.9.1` → `0.9.2`. Bump the **middle** number (`0.9.x` → `0.10.0`) only after several large tasks land that genuinely change how the app feels to use — that's how `0.7.0`/`0.8.0`/`0.9.0` were used, one per phase.
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
8. **`.claude/rules/repo-hygiene.md`** — what is and isn't safe to commit; the pre-flip checklist for the day this repo's visibility changes

## Verification workflow

- Local: see Key commands. Login: marty@mooexe.dev (ask for the password, do not store it)
- Tests: `dotnet test` from repo root
- Prod logs: see Key commands. **No sudo needed**, keep `-q`, **always bound the query** (`-n`/`--since`).
- Test channel: @testingandfun ("Marty's Channel For Testing and Having Fun"). NEVER post to Dev Dairy Diary (the real channel) without explicit permission
