---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: scope and data model of the indie-gamedev module (Phase 13)
guard: none
---

# Module for indie gamedev

The single entry point for turning Cedar Clerk into an indie game developer's toolkit. The original source of the idea is the out-of-repo brief `Gamedev_Focused_Rework.md`. Decisions — `docs/DECISIONS.md`, ADR-101…107. Desktop architecture — `docs/tech/DESKTOP.md`. Phase 13 work order — archived in `docs/archive/roadmap-phases-0-13.md`.

The `indiedev_module` branch (created from `dev`) **has been merged into `master` with v0.10.0 and deleted**. The reversibility that the brief demanded of the branch now lives in the `Cedar:Modules:IndieDev` flag (ADR-101): turning it off keeps core project/document organization and publishing available. **The module's MUST list was closed on 11.08.2026** — status is recorded in `docs/archive/roadmap-phases-0-13.md` (Phase 13); the open remainder (MIGHT + `T-127`) lives on the board.

## What changes

Cedar Clerk stops being "a post editor that publishes to several networks" and becomes a platform where **a post is one type of document**, and documents live inside a **project** (in Marty's terms — a game). Around the documents appear things that are not posts at all: tasks, sprints, an asset index, reference boards, writer and game-designer assistants.

Nothing existing is replaced in the process. The module **adds** — the editor, `/drafts`, `/posts`, `/settings` keep working as they worked, and turning off the `Cedar:Modules:IndieDev` flag keeps core project/document organization and publishing available.

## Positioning

Since ADR-243, this is Cedar Clerk's deepest craft-specific module inside a broader platform for
independent makers. Projects and Devlogs participate in Discovery, but a personal Blog remains a
complete first-class path without this module. The flag removes game-specific tools. Core project organization, document ownership,
publishing and the Blog discovery lens remain available (ADR-306).

This is also the answer to `Q-1` in `docs/tasks/BACKLOG.md`, which had stood open since 30.07.2026: back then four focuses were named (bloggers, photographers/videomakers, writer assistance, indie gamedev assistance) and the wording of the fifth was lost. The brief picks one — **indie game developer** — and picks it for an honest reason: it is Marty himself, i.e. the only audience whose needs are checked here not by guesswork but by his own work.

Importantly, the audience is not narrowed down to "a programmer" in the process. The brief's wording lists the roles of one person: programmer, game designer, producer, writer, sound designer, composer, director, marketer, analyst. The toolkit is addressed to this set of roles, not to a type of employment.

The working name from the brief is `Cedar Clerk For Indie Developers`. The product's name remains an open question (`Q-17`, `docs/tasks/BACKLOG.md`); the recommendation is to keep `Cedar Clerk` as the platform's name, because a rename drags along the domain, the bot's name, the `.cedar` extension and all hundred-odd ADRs, while a subtitle solves the marketing task.

## What already exists in the code and is being reused

Checked against the code on 10.08.2026, not against the docs (`docs/DOCS-FLOW.md` warns that the docs lag — and that rule has already found three discrepancies). Version `0.9.40`, the last ADR before this session was ADR-100, the last phase was Phase 12.

| What the module needs | What is already ready | Where |
|---|---|---|
| New publish targets (itch.io, Steam, IndieDB, LinkedIn) | `IPublishTarget`, `PublishTarget`, `PublishJob`, `PublishCapabilities`, network-agnostic `PublishAsync`, credential encryption | `CedarClerk.Server/Publishing/`, `PostEndpoints.cs` |
| Grouping documents | `Folder` — per-owner, create/rename/delete; `Draft.FolderId` as a plain scalar `Guid?` with no FK | `Data/Entities.cs:452`, `:275` |
| Document type as a column | `Draft.IsTemplate` — a ready precedent of the same move | `Data/Entities.cs:285` |
| Build/version tags | `Draft.Tags` (a flat string), `DraftRevision` (immutable history with `Kind`) | `Data/Entities.cs:260`, `:481` |
| Document media | `Asset` + plan quotas + Telegram pre-upload | `Data/Entities.cs:590` |
| Glossary | `GlossaryTerm` — per-owner, per-language, aliases, translation links via `SourceTermId` | `Data/Entities.cs:419` |
| Lazy loading of module screens | All routes are already `loadComponent` with background preloading | `cedarclerk-web/src/app/app.routes.ts` |
| ~~Local server run for the desktop~~ | ~~`CEDAR_DATA_DIR`, SQLite, `Database.Migrate()` on startup~~ — **this line died with ADR-117** (12.08): the desktop has no local database, the local process is a filesystem agent | `docs/tech/DESKTOP.md` |
| Autosave, version history, protection against losing text | Guards from ADR-065/066/067 — the most battle-scarred part of the codebase, inherited by new document types for free | `DraftEndpoints.cs`, `editor.component.ts` |

Practical conclusion: most of the module's infrastructure is already in place. What is genuinely new is the entities `Project`, `GameTask`, `Sprint`, `AssetEntry` and the link (`EntityLink` — `TaskLink` was planned, but T-141 generalized it earlier), two columns on `Draft`, and the Electron shell.

## Data model

Full rationale for every row — ADR-102, ADR-106, ADR-107.

**Changes to what exists:**
- `Draft.DocumentType` — string, default `post`. Existing rows become posts with no data migration.
- `Draft.ProjectId` — `Guid?`, a plain scalar with no FK and no nav property, like `FolderId`. Null = a document outside a project (all of today's).
- `GlossaryTerm.ProjectId` — `Guid?`. Null = a global term (today's behavior), filled = a project's local glossary.
- `Project.AssetRootPath` / `Project.AssetsIndexedAt` — the folder being indexed, and when it was last indexed. One root per project, not on every index row (ADR-107, clarification 2).

**New:**

| Entity | Meaning | Key fields |
|---|---|---|
| `Project` | A game or other body of work that documents are gathered around | `OwnerId`, `Name`, `Description`, `CoverUrl?`, `CreatedAt`, `ArchivedAt?` |
| `GameTask` | A task-tracker task | `OwnerId`, `ProjectId`, `Name`, `Status`, `Priority`, `Description`, `Assignee`, `SprintId?`, `DueAt?` |
| `TaskLink` | A link from a task to anything | `TaskId`, `EntityType`, `EntityId` |
| `Sprint` | A planning interval built on top of tasks | `OwnerId`, `ProjectId`, `Name`, `StartsAt`, `EndsAt` |
| `AssetEntry` | A row in the local asset index — path and metadata, the file itself is not copied | `OwnerId`, `ProjectId`, `RelativePath`, `FileName`, `Extension`, `Kind`, `SizeBytes`, `ModifiedAt`, `IndexedAt`, `MissingSince?` |
| `ProjectMember` | Who besides the owner may act on a project (ADR-217) | `OwnerId` (**the project owner**), `ProjectId`, `Email`, `MemberUserId?`, `Role`, `InviteToken?`, `InvitedByUserId`, `InvitedAt`, `AcceptedAt?`, `LastSeenAt?` |
| `CanvasBoard` | One reference board inside a project (ADR-218) | `OwnerId`, `ProjectId`, `Name`, `Background`, `CreatedByUserId`, `UpdatedAt`, `UpdatedByUserId`, `Version` |
| `CanvasItem` | One thing on a board — image, note, frame or link | `OwnerId`, `ProjectId`, `BoardId`, `Kind`, `X`, `Y`, `Width`, `Height`, `Rotation`, `Z`, `Color`, `Payload`, `Version` |

The project's conventions are followed: GUID primary keys, a flat `string OwnerId` on every owner-scoped row, filtering by owner in every endpoint (`docs/tech/ARCHITECTURE.md`). The module's entities live in `Data/Entities.IndieDev.cs` — a second file, not thirty: the convention "everything in one flat `Entities.cs`" exists so as not to spawn a file per entity, and splitting by module does not contradict it.

**Migration is mandatory.** Any of the above touches `Entities.cs` — which means `dotnet ef migrations add <Name> --project CedarClerk.Server` in the same session, immediately. `SchemaDriftGuardTests` will fail `dotnet test` if this is forgotten (`.claude/rules/ef-migrations.md`).

**A trap already stepped on (T-120):** EF **does not read** a property initializer (`= DocumentTypes.Post`) when generating a migration, and substitutes `defaultValue: ""`. For a column where an empty value means "unknown type," this would have filled all existing rows with a non-working value. The default is declared in `OnModelCreating` via `HasDefaultValue` — that way it lands in both the migration and the model snapshot. Check this in the generated file **before** the first run: `Database.Migrate()` on startup runs the migration silently.

## Design

`docs/design_handoff_indiedev_core_loop/` — a package from Marty (10.08.2026), made in Claude Design: an interactive prototype `IndieDev Module.dc.html`, 26 screenshots, and a README with exact values. Choices made: the project list as a table (not a card grid), an "Overview-first" dashboard, task links as chips, the task card as a centered modal window, "Projects" as a button in the shared top bar.

**The package's README is the source of truth for these screens, the screenshots are secondary** (they carry DOM-capture artifacts, which the README warns about). All values are existing tokens from `styles.scss`; the package introduces no new tokens.

The package covered more than was built that day; by 11.08.2026 the task board, the planner, and the asset grid from it were implemented (`T-122`/`T-123`/`T-124` closed). Of what was drawn, only the reference board (`T-129`, likely absorbed by the `T-155` board) and the press kit (`T-128`) remain unbuilt.

## What has been implemented (T-120, 10.08.2026)

*(Written on the morning of 10.08 — the frontend caught up the same evening, see the next section.)* Backend foundation:

- `CedarClerk.Core/DocumentTypes.cs` — six types, `IsKnown`, `IsPublishable`. Pure C#, covered by tests.
- `Data/Entities.IndieDev.cs` — `Project`. `Draft.DocumentType` + `Draft.ProjectId` in `Entities.cs`.
- Migration `AddProjectsAndDocumentTypes`, indexes `(OwnerId, ArchivedAt)` on projects and `(OwnerId, ProjectId)` on drafts.
- `Modules/IndieDev/ProjectEndpoints.cs` — `/api/projects` (list, dashboard, creation with a first document, rename, archive, deletion with document unlinking, adding/linking/unlinking documents) and `/api/documents/{id}/type`.
- Registration in `Program.cs` behind `Cedar:Modules:IndieDev`; the same flag in the `/api/me` response as `modules.indieDev`.
- Refusal to publish working material — on the shared `PostEndpoints` path, i.e. for all networks at once.
- Refusal texts — in `CedarClerk.Localization/ErrorMessages.cs`, including interpolated ones: the `ErrorMessageLocalizationTests` test only catches `error = "..."` and would miss `error = $"..."`, but in English such a response would sound the same either way.

`dotnet test` 633/633.

### Frontend T-120 (10.08.2026, evening)

- `core/projects.service.ts` — types, icon maps and HTTP; `core/indiedev.guard.ts` — the same trick as `adminGuard`.
- `pages/projects.component.*` — the list: compact density, toolbar → filter chips → table, a project-creation dialog with a type choice.
- `pages/project.component.*` — the dashboard: comfortable density, documents grouped by type (three cards + three tiles), new-document and project-settings dialogs.
- `shared/page-header.component` — a "Projects" button first in the row (hidden when the module is off) and a second breadcrumb segment: `Projects / Cedar Station`.
- Eight Phosphor icons added to `tools/icon-map.json` (`npm run icons:generate`, 89 icons).
- Dictionaries `en.ts`/`ru.ts` — a `projects` block.

**Deviations from the handoff — deliberate, each one due to a missing backend piece or an undrawn state:**

| What | Why |
|---|---|
| The dashboard's right column — one "Not built yet" card instead of three (Up next / Sprint / Recent assets) | There are no tasks, sprints or asset index yet. An empty "Up next" card would read as "you have no tasks," not as "tasks aren't built yet" |
| The "Tasks" and "Assets" columns in the list show `—` | A zero would lie in the same way. The explanation is in the hover tooltip |
| The list's empty state was invented | The handoff explicitly marks it as "not drawn yet" |
| The project-settings dialog was invented | The handoff has a "Settings" button, but no screen behind it. A minimum was built: name, description, archive, delete |
| There is no project-type icon in the "Name" column | It's not in the handoff. Consequence: the project type is not visible in the list at all — it can be returned as one line if Marty wants it |

Verified: `dotnet test` 639/639, frontend 11/11, `ng build` with no warnings (initial 587 kB against a 650 kB budget), `npm run check:contrast` — 0 violations in both themes, smoke 53/53. Screens were captured live in both themes on an isolated database and checked against the package's screenshots.

## What has been implemented (T-123, 11.08.2026) — task tracker

- `CedarClerk.Core/TaskWorkflow.cs` — `TaskStatuses` (four board columns, order = left-to-right order) and `TaskPriorities` (1–3, `Clamp`). Pure C#, covered by tests.
- `Data/Entities.IndieDev.cs` — `GameTask`. Migration `AddGameTasks`: only a new table, existing ones untouched; defaults declared in `OnModelCreating`, not via property initializers (the T-120 trap).
- `Modules/IndieDev/TaskEndpoints.cs` — `/api/projects/{id}/tasks` (list, create) and `/api/tasks/{id}` (read, update, delete, links).
- `Modules/IndieDev/ProjectLinks.cs` — a shared helper for `EntityLink`; `AssetIndexEndpoints` was moved onto it so there aren't two copies of the linking logic.
- Frontend: `core/tasks.service.ts`, `pages/project-tasks.component.*` (board + list + card-modal), route `projects/:id/tasks`, a `tasks` block in both dictionaries.
- Dashboard: the "Up next" rail became real; the "Tasks" and "Assets" columns in the project list show numbers instead of dashes.

**Decisions made during implementation:**

| What | Why |
|---|---|
| Links are `EntityLink`, not a separate `TaskLink` from ADR-106 | T-141 had already generalized this line. Ordering the pair came for free; a separate table would have been a second one doing the same job |
| Sorting happens on the server, in one function | The board, the list, and the rail all answer one question: "what's next." Three implementations are three chances to drift apart |
| Overdue matters more than priority | A P3 that's a week overdue needs a response sooner than a P1 due in a month. A rail sorted by priority would get this exactly backwards |
| The board is returned whole, with no pagination | A project has tens of tasks, not tens of thousands. A kanban column with pagination lies about its own count. Pagination belongs to the asset index, and that's a different kind of screen by nature |
| "Tasks" in the project list is a count of **open** ones | Otherwise a finished project would show its biggest number on the day there's nothing left to do in it |
| `CompletedAt` is stored rather than derived from `UpdatedAt` | Editing the title of a closed task would shift the date it was closed |
| No sprints yet — that's T-124 | The `SprintId` column is set up right away so the planner adds a table rather than changing an existing one. The sprint card on the dashboard honestly says "not built yet" instead of rendering empty |

**A side finding fixed on the spot:** deleting a project left the asset index behind, and deleting a document left its links behind. Neither has a navigation property, so EF cascaded nothing; tasks would have become a third orphan. All three deletion paths now clean up after themselves.

Verified: `dotnet test` **737/737**, frontend 11/11, contrast clean, smoke **53/53**. Plus on the live server: board ordering, a mutual link between two tasks (one row — two cards), overdue first in the rail, `completedAt` unaffected by an unrelated edit and cleared when returned to work, a deleted task removes its links from another card.

## What has been implemented (T-124, 11.08.2026) — planner

- `CedarClerk.Core/SprintStates.cs` — three states derived from dates, and ordering for the planner. Pure C#, covered by tests.
- `Data/Entities.IndieDev.cs` — `Sprint` + counter `Project.NextSprintNumber`. Migrations `AddSprints` (new table) and `AddSprintNumberCounter` (a column with `defaultValue: 1`).
- `Modules/IndieDev/SprintEndpoints.cs` — `/api/projects/{id}/sprints` and `/api/sprints/{id}`.
- Frontend: `core/sprints.service.ts`, `pages/project-planner.component.*`, route `projects/:id/planner`, a `planner` block in both dictionaries.
- Board: sprint filter chips, an `S` chip on the card, sprint selection in the task card. Dashboard: the sprint card became real.

**Decisions made during implementation** (rationale — ADR-111):

| What | Why |
|---|---|
| A sprint has no status column | A stored status becomes wrong the very second the clock passes `EndsAt`, and requires machinery to service a copy of a fact already recorded in two dates |
| A sprint is never "overdue" | It's the tasks inside that are overdue. The card says so directly — "1 task overdue" — rather than turning red entirely. The same rule as on the board |
| "Finished" means the days ran out, not "everything is done" | A finished sprint collapses **only if everything inside is closed**. If something remains unclosed, the card stays open: hiding it would be lying that the work vanished along with the date |
| The sprint number is stored, not parsed from the name | A regex would break on the first sprint named "Polish." The number answers "which one in order," the name answers "about what" |
| The number comes from a counter on the project, not `MAX+1` | `MAX+1` **reuses** a number right after the topmost sprint is deleted. Caught by running it, not by reading the code; the test was written afterward |
| Deleting a sprint unlinks its tasks | The same rule as for a project: deleting the container is not a request to delete its contents |
| A task can only join a sprint from its own project | Otherwise an id from a foreign project would be accepted, and the task would drop out of both planners at once |

Verified: `dotnet test` **752/752**, frontend 11/11, contrast clean, smoke **53/53**. On the live server: three sprints in three states and their ordering, the count of overdue tasks inside the current one, rejection of a foreign sprint, rejection of "end before start," numbers after two deletions (`S2, S4, S5` — no reuse), a task survived the deletion of its own sprint.

## What has been implemented (T-125 and T-126, 11.08.2026) — project glossary and builds

**T-125.** `GlossaryTerm.ProjectId` (`Guid?`, migration `AddGlossaryProjectScope`, nullable with no default — existing terms become global). `GET /api/glossary` understands `?projectId=` and `?scope=global`; `LoadForAsync` obtains the document's project, and the blog renders a post with shared terms **plus** its project's terms. The glossary screen gained a scope selector, which also decides where a new term will land (and the caption underneath it says so).

**T-126.** `Build` + `GameTask.BuildId` (migration `AddBuilds`), `Modules/IndieDev/BuildEndpoints.cs`, the `/projects/:id/builds` screen, version selection in the task card, `LinkTargets.Build` for documents.

| What | Why |
|---|---|
| A term's scope is a column, not a second entity | A project term and a global one have the same shape. A separate table would be nine duplicated columns and the impossibility of moving a term between scopes without a re-entry |
| A document sees shared **plus** its own | Not "or": "Unity" is global, "the ferry" is about one game, an article about that game needs both |
| On a collision, the project's term wins | A narrower scope is a more precise definition; that's exactly what a local glossary is set up for |
| A build is an entity, not a tag | A version has a number, a date, notes and a composition. The flat `Tags` string stores none of that and can't answer "what's in 0.4.2" except by scanning and parsing |
| A task gets a `BuildId` column, a document gets an `EntityLink` | A task ships in exactly one build, and is filtered by that field. A document's relationship is looser, and `Draft` already has about thirty-five fields with recorded debt to split it up |
| The changelog is a document, not text to copy | From there it lives a document's ordinary life: it gets edited, translated, published. A plain string would be a generator with nowhere for its output to land |
| A build knows nothing about git | No repo tags, no CI, no artifacts. It's a record the author keeps; pretending to be an integration that doesn't exist is worse than honestly being a record |

Verified: `dotnet test` **752/752**, frontend 11/11, contrast clean, smoke **53/53**. On the live server: a project's glossary doesn't show another project's terms, a repeated version is rejected, a build from a foreign project is rejected, a changelog assembled from two closed tasks (a task in progress excluded), a task survived the deletion of its own version.

**This closes the MUST list for phase 13.**

## MUST — v1 module composition. **Fully completed 10–11.08.2026**

Order — the owner's priority from 10.08.2026, adjusted by one dependency: the desktop was moved up to second place, because the Asset Manager without it degenerates into the same file upload that already exists (ADR-107). The table is the *composition*; status is recorded in `docs/archive/roadmap-phases-0-13.md` (per the DOCS-FLOW rule); all seven rows there are closed.

| # | What | Why here |
|---|---|---|
| 1 | `Project` + `DocumentType` + document binding (T-120) | The foundation — without it, everything else has nowhere to go |
| 2 | Desktop shell (T-121; Electron + filesystem agent — the "sidecar" died with ADR-117) | A prerequisite for #3, not a want on its own |
| 3 | Asset Manager / Viewer (T-122 + T-140/T-141) | Named by Marty; the only thing that physically requires a local process |
| 4 | Task Tracker (T-123; `GameTask` + `EntityLink` — the planned `TaskLink` was never built, T-141 generalized the link earlier) | Named by Marty |
| 5 | Development Planner (T-124; `Sprint`, deadlines, built on top of tasks) | Named by Marty; on top of #4 and therefore cheap |
| 6 | Local glossary (T-125; scope = project) | `GlossaryTerm` already exists, needs one column |
| 7 | Build and version tagging (T-126; `Build` as an entity) | "Extensive tagging" turned out to be an entity with fields, not a tag string (ADR-112) |

## MIGHT — after v1

From the brief, none is cancelled — simply none is a prerequisite for anything. Rows on the board:

- **Press Kit** (`T-128`) — a self-updating generated document ("like `presskit()`, but better"). Fits neatly on the "one document, many renderers" axis with no new mechanism. A competitive analysis on 18.08 raised its value: the niche is empty.
- **References board** (`T-129`) — a moodboard gallery. **Absorbed by the `T-155` canvas board** (ADR-218): a moodboard is a board whose items happen to be images, so there is one screen and not two. See "Reference board and collaborators" below.
- **Brainstorm session** (`T-130`) — organizing raw thoughts.
- **Game Script Writer** and **Game Plot Writer** (`T-131`) — writer's tools, the second shorter than the first.
- **Game Design Helpers** (`T-132`) — assistants for designing mechanics and systems.
- **Workflow planner** (`T-133`) — planning the development pipeline.
- **Code Documentation** (`T-134`) — browsing code documentation.
- **Budget calculations** (`T-135`).
- **New publish targets** (`T-127`) — see below, the risk isn't in the code.

## Reference board and collaborators (T-301)

The first MIGHT row to be built, and the first place in the module where a project holds more than one
person. Decisions: ADR-218 (the board and its realtime model), ADR-217 (membership). Both live inside
the module — endpoints behind `Cedar:Modules:IndieDev`, screens behind `indieDevGuard`.

**The board.** A project holds up to 20 `CanvasBoard`s; each is an endless surface carrying up to 2000
`CanvasItem`s of four kinds — image, note, frame, link. Position, size, rotation and z-order are
columns, so a drag writes numbers and render order sorts in SQL; whatever is specific to a kind (a
frame's title, an image's natural size, a link's address) is one JSON payload replaced whole. Images
are ordinary uploaded `Asset` media served from `/media/`, not `AssetEntry` fingerprints — a board has
to show pixels, and an index row is a path on somebody's disk.

**Who can be on it.** The owner, plus accepted `ProjectMember` rows: an `editor` writes, a `viewer`
reads the board and sees who else is there. An invitation is an address and a single-use token mailed
to it; the token is the credential, and the invite link is handed back to the owner even when mail is
not configured. Membership rows live in the **project owner's** tenant, and canvas work runs in the
owner's scope after one membership lookup — the query filter is never loosened.

**What sharing does not reach.** Only the boards, the people list, and a new `GET /api/projects/shared`.
The project dashboard, tasks, sprints, builds, documents, the asset index and the glossary stay
owner-only, and a member who opens `/projects/:id` still gets a 404. `Assignee` on a task remains free
text: assigning work to an account is a tracker decision, and the tracker is not shared. ADR-217 carries
the full list and the reason it is deliberately short.

**Live editing.** One SignalR hub at `/hubs/canvas`, one group per board. The server is the source of
truth and the conflict rule is last-writer-wins with a server-assigned `Version` — no rejection, no
locking. A drag broadcasts at pointer rate and persists nothing; the pointer-up persists once. Cursors,
selection and presence exist only in the hub's memory. A reconnect re-joins and replaces local state
with a fresh snapshot, and `GET /api/canvas/{boardId}` plus its export return that same snapshot, so a
board is readable without a socket.

## What needs verification before scoping

The four publish targets from the brief are architecturally trivial: each is an implementation of `IPublishTarget`, the mechanism already proven on Telegram, Bluesky and X. **The risk is entirely in other people's APIs, and it needs to be resolved by research before the line item goes into the plan.** None of the claims below were checked in this session — these are hypotheses requiring confirmation:

- **itch.io** — there appears to be no public API for publishing a devlog: `butler` uploads builds, the OAuth API reads data. It may turn out to be unachievable at all, and then the honest answer is a link to manual publishing, not an imitation.
- **Steam** — announcements are made through the partner Web API; a partner account and a publisher key are needed. Available only to the owner of a published game.
- **IndieDB** — whether a write API exists is in question.
- **LinkedIn** — the UGC Posts API exists, requires app review.

Precedent for why this is called out separately: the network queue in `Q-13` has already been rearranged once after it turned out that X has been charging for posting since February 2026. Checking other parties' terms before planning is cheaper than after.

## What is deliberately not being done

- ~~**Syncing the desktop with the cloud** — ADR-105. The mode is chosen explicitly.~~ **Removed 12.08.2026 (ADR-117)**, and not by building the sync, but by removing the second copy: the desktop opens the cloud, the database is one, and all that remains local is a process reading the disk. The offline TipTap edit merge that ADR-105 feared has not been solved — it no longer needs to be.
- **Moving `Draft`'s publishing fields into a separate table** — tech debt, filed as a line in `docs/tasks/BACKLOG.md`; accepted as the price of ADR-102.
- **A shared tracker** — a project can hold collaborators (ADR-217), but membership reaches the reference boards and the people list and nothing else. Tasks, sprints, builds, documents and the asset index stay owner-only, `Assignee` stays a free-text string, and there are no notifications, comments or activity feed. Widening any of those is one more endpoint whenever someone asks; a widening that shipped unasked would be a data question.
- **Renaming the product** — closed (Q-17, 18.08.2026): the name stays Cedar Clerk, the focus goes into the subtitle.

### T-122 — Asset Manager (10.08.2026)

An index of local files: paths and metadata, **bytes are never copied**. Full rationale and four clarifications to ADR-107 — in `docs/DECISIONS.md`.

- `CedarClerk.Core/AssetKinds.cs` — file kind by extension, a list of engine folders to skip. Pure C#, covered by tests.
- `AssetEntry` + `Project.AssetRootPath`, migration `AddAssetIndex`.
- `Modules/IndieDev/AssetIndexService.cs` — a two-pass walk (count, then index), progress, cancellation, batched writes. In-memory, like `AiJobService`: a scan lost on restart costs a re-scan and nothing more.
- `Modules/IndieDev/AssetIndexEndpoints.cs` — a filtered, paginated list, detail, scan start/status/cancel, re-index of a single file.
- `pages/project-assets.component.*` — grid and list views, kind chips and "not found," a scanning banner with progress and cancellation, a folder-picker screen, an asset modal.

~~**A second capability behind a second flag.** `Cedar:AssetIndex:Enabled` is enabled only in the desktop shell.~~ **Replaced 12.08.2026 (ADR-117).** The disk walk moved from the server endpoint to the desktop agent, and the cloud now **accepts** a folder description (`PUT .../assets/source`, `POST .../assets/batch`, `POST .../assets/sweep`, `PUT .../assets/thumbs`). The flag was removed along with the walking endpoints: the capability isn't disabled in production, it's simply absent there — its code lives only in the agent. That's a stronger guarantee than the flag ever gave. Accepting a list of names from the owner is not a dangerous capability; what bounds it is numeric ceilings (500 rows per batch, 200,000 per project, 256 KB per preview, a cap on `thumbs/` volume).

**The index is now shared, and that changes the screen.** Rows live in the cloud and open from anywhere, so most of the time the asset screen shows **fingerprints**: previews and metadata instead of a file sitting on another machine. `Project.AssetRootMachineId` is what makes the question "is the file here, or is this its fingerprint?" answerable; a browser has no bridge, hence no machine id, hence it's always a fingerprint. Details — `docs/tech/DESKTOP.md` §"File or fingerprint."

**Deviations from the handoff §9-10, all from missing data, not from laziness:**

| What | Why |
|---|---|
| No "Music" chip | A track can't be told apart from ambience by extension. Everything sound-related is `audio` |
| Paginated (60), not a virtualized list | Virtualization is needed at tens of thousands of rows; pagination honestly works already and doesn't pretend to be an infinite list (`T-142`) |

Verified by running it: a scan of a real folder, marking a missing file while keeping its row, unmarking it when it returns, 403 without the flag.

### T-140 and T-141 — previews, metadata and links (10.08.2026)

Close two of the three honest gaps left by T-122. Rationale — in ADR-107 (addendum).

**Previews.**
- `CedarClerk.Core/BlendThumbnail.cs` — a parser for the preview Blender embeds inside a `.blend`. Marty's request: it's the project's main file and the only one a graphics library won't open. The first megabyte is read, the image is flipped (Blender stores rows bottom-up), a compressed file honestly fails.
- `Modules/IndieDev/AssetMetadata.cs` — on-demand thumbnail generation into `CEDAR_DATA_DIR/thumbs/`, **never** next to the source.
- Endpoint `GET .../assets/{id}/thumb` — owner-scoped, not through `/media/*`.
- Whether a preview is **possible** is decided by extension, not by kind: PSD, EXR and Aseprite are images a decoder won't open, and asking them for a thumbnail means showing a broken-file icon.

**Metadata.** Image dimensions (`Image.Identify`, header only) and WAV duration and sample rate (`CedarClerk.Core/WavHeader.cs`, a pure parser). MP3/OGG/FLAC stay silent: each needs a real parser, and a wrong duration is worse than a missing one — "2:14" invites no doubt. Only new and changed files are read.

**The format set is expanded** (Marty's request): Unity/Unreal/Godot engine files, DCC source files, DAW project files, shaders and scripts. Engine files remain kind `other` — a Unity scene is neither a picture nor a document.

**Links.** `EntityLink` — a generalization of `TaskLink` from ADR-106 with pair ordering, so that A→B and B→A are one row. Set **by hand**: an indexed file lives outside Cedar Clerk, no document can reference it, so "used in" cannot be detected — only stated. The label changed to "Linked documents."

`dotnet test` 698/698. Verified by running it on a fixture folder; the one thing left unverified is the orientation of a real `.blend`'s preview (the flip was done per documented behavior, but confirmed only on a synthetic file).
