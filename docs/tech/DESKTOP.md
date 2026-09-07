---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: desktop build layout and the filesystem agent
guard: none
---

# Desktop Application

Decisions — ADR-104 (Electron on top of our own server) and **ADR-117 (one database in the cloud, the local process is a filesystem agent)**, `docs/DECISIONS.md`. ADR-105 (two modes) and ADR-108 (upstream authentication) were superseded by them — if you encounter those in older entries, that's history.

Reason for existing — the Asset Manager: describing the contents of a game project folder (`MyGame/Assets` on the developer's disk) can only be done by a process running on that same machine (ADR-107).

> **Built and verified 10.08.2026** — shell, free port, exit without orphans.
> **Self-update 11.08.2026 (ADR-116)** — `cedar deploy --desktop`, the installed copy updates itself.
> **Became a cloud client on 12.08.2026 (ADR-117)** — there is no longer a local database.
> **`T-121` remains open on one point**: the built installer has never once been installed on a clean machine.

## Overall diagram

```
┌────────────────────── Electron ───────────────────────┐
│  main                                                  │
│   ├─ CedarClerk.Server.exe with Cedar:Agent:Enabled    │──► folder traversal,
│   │     127.0.0.1:<free port>                          │    file stat,
│   │     Bearer token per launch + granted roots        │    JPEG generation
│   ├─ machine.json     { id, name }                     │
│   ├─ granted-folders.json                              │
│   └─ BrowserWindow.loadURL(https://…/projects)          │
│                    │ IPC (preload, origin-gated)        │
│  renderer ─────────┴─ the same Angular SPA, but from   │
│                        production                       │
└────────────────────────────────────────────────────────┘
                             │ HTTPS, ordinary cookie session
                             ▼
        cedarclerk.mooexe.dev — the ONE database
        AssetEntry: path + metadata + preview in thumbs/
        asset bytes — never
```

Neither the server code nor the frontend is forked (ADR-101). The Angular build is the same one that ships to production; the desktop app doesn't even host it, but opens it from production.

**Who does what: the agent reads the disk, the page sends.** The page already has a cookie to the cloud — so the agent needs no credentials at all, and Electron does not handle authentication at all. The side benefit matters more than the savings: the work happens in an ordinary screen with progress and a cancel button, not in a background process nobody can ask what it's doing.

## Repository layout

| File | What's in it |
|---|---|
| `CedarClerk.Desktop/main.js` | Launching the agent, the token, grants, the window, the bridge, self-update |
| `CedarClerk.Desktop/preload.js` | IPC bridge with `contextIsolation: true` — the only channel between the page and the disk |
| `CedarClerk.Desktop/package.json`, `electron-builder.yml` | Electron + the installer build |
| `CedarClerk.Server/Modules/Agent/` | The agent proper: endpoints, the walker, grants, the scan service |

Building the desktop app is `cedar build`: `npm run build` (Angular) → `dotnet publish -r win-x64` → `electron-builder`. `cedar deploy --desktop` invokes the same steps and publishes the result to the server (ADR-116); without the flag, deploy does not touch the desktop app at all.

## The Agent

The same `CedarClerk.Server.exe`, launched with `Cedar:Agent:Enabled=true`. In this mode `Program.cs` **exits the assembly right at the start**: no database, no migrations, no Identity, no Quartz, no bot, no SPA, no landing page, no `/api/*`. Only Kestrel and the `/agent` group.

An early exit rather than a scatter of `if`s around the rest, and this isn't about tidiness: this way agent mode cannot **accidentally** gain a capability someone adds further down later.

| Endpoint | What it does |
|---|---|
| `GET /agent/health` | Version — the shell waits on this for readiness |
| `POST /agent/grant` | Authorizes a folder. Called **only by main**, right after the OS dialog |
| `POST /agent/scan` | Starts a traversal, returns `scanId` |
| `GET /agent/scan/{id}` | Progress: counted, described, unreadable folders |
| `GET /agent/scan/{id}/files` | A page of described files — **read while the traversal is still running** |
| `DELETE /agent/scan/{id}` | Cancel |
| `POST /agent/stat` | Re-stat a single file — for the "Reindex file" button |
| `POST /agent/thumb` | Preview of a single file, as JPEG bytes |

Why the agent stayed a .NET process rather than moving to Node: it already carries the `.blend` preview parser (Marty's direct request, `CedarClerk.Core/BlendThumbnail.cs`), the WAV parser, and ImageSharp's coverage of TGA/TIFF/QOI/PBM/WEBP. Rewriting that in JS is weeks of work and a second set of format bugs, to save 70 MB that ADR-104 already agreed to pay.

### Two locks, and the second doesn't duplicate the first

The former sidecar was an ordinary Cedar Clerk instance and was locked by the Identity cookie. The agent has no Identity — so it needs its own lock, otherwise a local HTTP server enumerating folders is readable by any process on the machine **and by any page in any browser** (a browser can reach `127.0.0.1`).

1. **Bearer token** — 32 random bytes, generated by the shell per launch, passed to the agent via an environment variable, never written to disk. Checked by a filter across the whole group, not in each handler individually: an endpoint added later is closed by default, not by memory. An agent started without a token refuses everything — closed by default, the only safe direction.
2. **Only granted roots** — the token says "you are the shell," the grant says "and here is the folder the human chose." The comparison is done against the **authorized** path, because `root\..\..\Windows` is a string that starts with the root and a location that has nothing to do with it. The trailing separator matters too: without it, a grant on `C:\Art` would also cover `C:\Artwork`.

**Grants are remembered in `granted-folders.json` and restored on startup.** Otherwise, after every restart, the folder would have to be pointed to again just to reread it — friction with no safety benefit: the gesture was already made, just last week. The list lives **on the shell side** and is never taken from the server, and that's the whole argument: restoring what the cloud recorded would let a compromised page write any path into the project root and ask for it back. The residual cost, named plainly: **a folder chosen once stays readable by the application until the grant file is edited** — the same deal a browser makes with saved directory permissions.

The agent has no CORS at all: its only client is the Electron main process.

## Data

**The desktop app has none.** There is one database, in the cloud; `%APPDATA%\CedarClerk` holds only `machine.json`, `granted-folders.json`, and `update.log`.

This has a consequence that previously had to be worried about separately: **there is nothing to back up for the desktop app** (`T-137` closed as moot, not as done). But another cost appears, and it's named directly in ADR-117: **the desktop app does not work at all without a network.** The thirty-day cookie saves you from logging in again, but not from a missing network.

`cedar.db`, left over in `%APPDATA%\CedarClerk` from versions before 0.11.0, **is opened by no one and deleted by no one**. This is Marty's data; the shell has no right to remove it silently. If it isn't needed, the file can be deleted by hand.

## Machine identity

`machine.json` = `{ id: <guid, once>, name: os.hostname() }`. The name is refreshed on every launch (a renamed machine should carry the new name); the id — never (a renamed machine is still the same machine with the same files).

This is what lets the asset screen distinguish a file from a fingerprint: the cloud remembers which machine indexed a folder, and the client compares against that id. The browser has no bridge, so it has no id either, so it always shows fingerprints — and that's the truth, not a safety margin.

## File or fingerprint

The main consequence of ADR-117 for the interface. The index lives in the cloud and opens from anywhere, so most of the time the asset screen is looking at **fingerprints**: previews and metadata standing in for a file that lives elsewhere.

- Machine mismatch → a "Fingerprint" chip on the tile, in the row, and in the modal; the tooltip names the machine, and the modal has a separate paragraph about exactly what lives here.
- "Show in Explorer" and "Reindex file" are **hidden**, not disabled — a greyed-out button invites the user to go looking for the reason it's grey.
- `isLocal` defaults to **false** — an unknown machine, a project indexed before ADR-117, and any browser all answer "not here." Erring in this direction costs a dead button and a second of doubt; erring the other way costs a promise nobody keeps.

Three preview states, not two: **present** (uploaded), **pending** (a previewable format, just not arrived yet), and **never** (PSD, EXR — the decoder won't open them). Without the middle state, a freshly indexed folder in the browser would look exactly like a folder stuffed with formats nobody decodes.

## How a folder gets indexed

```
pickFolder → OS dialog → grant to the agent
  → PUT  /api/projects/{id}/assets/source   { machineId, machineName, rootPath } → scanStartedAt
  → POST /agent/scan
  → loop: GET /agent/scan/{id}/files → POST .../assets/batch (500 at a time)     phase "indexing"
  → POST .../assets/sweep  { scanStartedAt }                                    phase "marking missing"
  → loop: GET .../assets/thumbs/pending → POST /agent/thumb
          → PUT .../assets/thumbs (20 at a time)                                phase "previews"
```

The orchestration lives entirely in `cedarclerk-web/src/app/core/asset-sync.service.ts`.

Three properties of this pipeline keep it within bounds:

- **Upload runs alongside the traversal, not after it.** The agent appends to the list as it goes; the page pulls a page of 500 as soon as it appears. On a large folder this trades network cost for disk cost instead of paying both back to back.
- **The preview pass is resumable and idempotent.** It is driven by the server's answer (`thumbs/pending` = everything where `ThumbnailForModifiedAt != ModifiedAt`), not by a list assembled on the client. A break at file eight thousand costs a continuation, not a repeat; a catch-up pass can be run a week later, independent of the scan. **This property is exactly what makes "all previews, no limit" achievable.**
- **Rescanning does not touch previews of unchanged files.** Otherwise every repeat index would re-upload the whole folder. A preview is only invalidated when the bytes changed (size or edit time).

Limits exist because this is an open write channel into the cloud: ≤500 rows per batch, ≤200,000 rows per project, path ≤1024 characters with rejection of `..` and absolute prefixes, ≤256 KB per preview with a JPEG magic-byte check, ≤20 previews per request, and a ceiling of `Cedar:AssetIndex:ThumbBudgetBytes` (2 GB per owner by default). "No limit" is about there being no cap for the author, not about a ceiling on the droplet's 45 GB of free disk.

## The Bridge

Nine functions, and **none of them writes, deletes, or executes anything**: `machine`, `pickFolder`, `scan`, `scanProgress`, `scanFiles`, `scanCancel`, `stat`, `thumb`, `reveal`.

`shell.openPath` is **not** in the bridge — Marty's decision. `showItemInFolder` highlights the file in Explorer and launches nothing; that's the difference between reading and executing, and it's worth giving up the double-click that opens Blender.

### The boundary ADR-117 moved

Previously the window loaded `127.0.0.1`, meaning "the page" and "our server" were the same thing. Now the window loads `cedarclerk.mooexe.dev` — a **remote origin** — and the SPA renders TipTap documents and pasted HTML. So: **XSS on the domain turns into reading the chosen folder.** This is an accepted risk, not a solved problem.

What narrows it:

- `contextIsolation: true`, `nodeIntegration: false`, `sandbox: true`, `webSecurity: true`.
- **Origin gate in preload**: the bridge does not appear on a page that arrived from anywhere else.
- **Origin gate in main** on every call (`guard()`). Not a duplicate: the preload check runs inside the renderer and therefore cannot be the final word about it.
- **Navigation blocked** beyond upstream — the window must not turn into a general-purpose browser living inside an application that has disk access.
- Bearer token and granted roots on the agent (above).

The address constant is deliberately duplicated in `main.js` and `preload.js`: the sandboxed preload can only require the documented set of modules, and `process.argv` is not a surface to hang a security gate on. If the two constants ever diverge, the bridge simply doesn't appear — closed by default.

## The window opens `/projects`, not `/`

The landing page intercepts exactly `GET /` without a cookie (`LandingEndpoints.UseLanding`), so any other path serves the SPA. An unlogged-in `authGuard` redirects to `/login`.

This removes a bug Marty found on 10.08.2026 (the app opened onto the marketing pricing page) **by choice of address, not by a flag**: `Cedar:Desktop` is no longer needed for this and has been removed. One fewer flag — one fewer thing that could be set wrong on a public server.

## How the desktop configuration differs from production

| Variable | What it does | Why |
|---|---|---|
| `Cedar__Agent__Enabled` | Turns the exe into an agent | **The main boundary.** With it set, there is no database, no Identity, no `/api/*` |
| `Cedar__Agent__Token` | The agent's lock | Without it, the local disk reader is open to the whole machine |
| `Cedar__Urls` | Free port from the OS | Two instances on one machine can't both hold 8080 |
| `Cedar__BotToken=''` | Bot disabled | The agent never brings up the bot at all; this is insurance against an inherited variable, and the 409 against the production bot justifies it |

Gone: `CEDAR_DATA_DIR` (no database), `Cedar__AssetIndex__Enabled` (no server-side traversal), `Cedar__Auth__Upstream` (no local accounts), `Cedar__Desktop` (no landing page on the path), `Cedar__Registration__Open` (nothing to register locally).

`Cedar:Registration:Open` remains in the server code — it's still the right answer for a single-person self-hosted install, simply nobody sets it anymore.

## Version checking split by purpose

**The agent** is checked against the shell version: they ship in one installer, so a mismatch means a broken build, and the dialog is justified. The cloud version is only displayed and **not** checked: the site ships with every deploy, the installer only with `-Desktop`, and they legitimately diverge (ADR-116) — a dialog about that would fire after every ordinary deploy.

## Build and run

```
cedar build                    Angular + server + desktop
cedar build --desktop-only     rebuild only the shell
cedar build --installer        plus CedarClerk-Setup-<version>.exe
cedar build --run              build and launch immediately
cedar open desktop             launch what's already built (or the installed copy)
```

`BuildPipeline` synchronizes the version in `package.json` with `Consts.CurrentVersion`. `CedarClerk.Desktop/server/` is the build output (~70 MB self-contained runtime), not tracked by git.

Build/test/deploy operations use MooTool’s `cedar` command and the repository’s `cedar.json` profile (ADR-291). Install the command from MooTool before building a fresh clone. See [operations-console.md](../for_user/operations-console.md).

## How an update arrives (ADR-116)

```
cedar deploy --desktop
   │
   ├─ ordinary site deploy (build → archive → swap → health) — the site is already live
   ├─ build the shell + electron-builder  →  CedarClerk-Setup-<version>.exe (~119 MB)
   └─ upload to  ~/cedarclerk/data/downloads/
         ├─ .exe and .blockmap  →  sha256 check  →  mv into place
         └─ latest.yml          →  written last
                    │
                    ▼  https://cedarclerk.mooexe.dev/downloads/latest.yml
        installed copy: checks on launch and every 4 hours,
        downloads in the background, installs when the window closes
```

**The deploy order is mandatory the other way too: the ordinary deploy first, then `-Desktop`.** Production must gain the import endpoints before a copy exists that writes to them.

Three things, each non-obvious for its own reason:

- **`latest.yml` is written last.** It's the only file the installed copy reads. Until it exists, clients see the previous version — an unfinished publish is invisible, not broken.
- **The files live in `data/`, not in `app/`.** `app/` is wiped wholesale by the deploy on every release.
- **The site's version and the installer's version diverge, and that's normal.** The permanent link is `https://cedarclerk.mooexe.dev/downloads/latest`.

**The agent is killed before installation.** NSIS overwrites `resources/server/CedarClerk.Server.exe`, which at that moment is running as a child process; a locked file would abort the install partway through. That's why `stopAgent()` is synchronous (`spawnSync`) and is called before `quitAndInstall()`.

**And it removes the `exit` handler before that.** `taskkill /f` returns exit code 1 to the killed process, and the `exit` handler was set up to show "server crashed unexpectedly" via a modal `showErrorBox`. This wasn't visible before updates existed: every other stop happened after the window closed, which is what the handler was checking for. Installing an update stops the process while the window is still alive — and the modal popped up mid-exit, blocking the main process. Caught by Marty on the first real update, 11.08.2026. Rule: **a stop we asked for ourselves cannot be treated as a crash.**

**The update is applied by the version being replaced.** The old code performs the install, so any fix to the update path only takes effect the time after next. The only workaround is installing the new version by hand via the installer.

**What happened is written to `%APPDATA%\CedarClerk\update.log`.** A packaged app has no console, and an update ends in the process exiting.

**Code signing is still absent** (`T-145`), and for updates this matters more than for a first install: without a certificate, `electron-updater` skips signature verification and relies on the sha512 in the manifest, fetched over the same HTTPS. Trust in an update = trust in `cedarclerk.mooexe.dev`. **ADR-117 raised the stakes on this line**: the same domain now also serves as the bridge to the disk.

In development (`npm start`) updates are disabled: there's no `app-update.yml` next to the unpacked shell.

## `npm start`, not `electron .`

The Electron binary doubles as a Node runtime, and `ELECTRON_RUN_AS_NODE` switches it into that mode. Several editors (VS Code among them) set this in their built-in terminals. In that case `electron .` runs `main.js` under Node: `require('electron')` returns **the path to the binary**, not the API, and the first use of it fails with

```
TypeError: Cannot read properties of undefined (reading 'handle')
```

— a message that says nothing about the cause. Caught on Marty's first launch (10.08.2026). That's why `npm start` goes through `start.js`, which clears the variable, and `main.js` checks at startup that `require('electron')` returned an object.

## Risks, each with a check

| Risk | Check |
|---|---|
| **Disk access from a remote origin** | Preload origin gate + main-process gate on every call + navigation blocking + no `openPath` + the agent's token and grants. The residual risk is named in ADR-117 |
| **The app doesn't work without a network** | A direct consequence of "cloud only." Stated explicitly here and in ADR-117 |
| Agent open to other processes | `curl http://127.0.0.1:<port>/agent/scan` without a token → 401 |
| Folder outside the grant | A path request above the root → 403 |
| "All previews" on a large folder is slow and heavy | Resumable pass, cancel button, a counter with megabytes, a `thumbs/` ceiling with a clear rejection |
| Port taken by another process | OS-assigned free port (`:0`); verified with `dotnet run` already running on 8080 |
| Agent survives the window closing | Kill the process tree on `window-all-closed` and `before-quit`; verified via Task Manager |
| An exe from a different build sitting nearby | Compare `/agent/health` version against the shell version |
| Antimalware Service slows down an unsigned exe | Measure cold start on Marty's machine |
| Distribution size (~119 MB installer, see diagram above) | The cost of the ADR-104 decision; accepted, not fought |
| An update installs over a running agent | `stopAgent()` is synchronous, called before `quitAndInstall()` |
| An update from a spoofed origin | Not closed: `T-145`, code signing |

## What's not resolved

- **Installation on a clean machine** — `T-121`: the installer builds and is verified against build artifacts, but has never once run where there's neither `%APPDATA%\CedarClerk` nor a dev environment.
- **iPad** — `T-011` in `docs/tasks/BACKLOG.md`. Electron doesn't go there.
- **macOS/Linux builds** — technically `electron-builder` can do it, but there's no one and nothing to test them on.
- **Code signing and notarization** — `T-145`: money and accounts, not engineering.
- **Offline mode** — deliberately absent (ADR-117). If it's ever needed, it's a synchronization problem again, the one ADR-105 rightly walked away from.
