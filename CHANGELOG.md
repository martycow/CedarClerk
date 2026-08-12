# Changelog

## 2026-08-12 — Cedar CLI: one console over the five scripts (ADR-118)

Managing this project had spread across five PowerShell scripts with about twenty flags between them and a dozen `ssh` invocations retyped every time. The ask was to gather that into one interactive tool on Spectre.Console — menu, prompts, and terminal graphics worth looking at — with one condition attached: **this stage wraps the scripts, it does not reimplement them.**

That condition is the whole design. `deploy.ps1` is 875 lines in which nearly every branch is scar tissue: resumable upload because `scp -r` left production down twice (ADR-113), `.exe` → `.blockmap` → `latest.yml` in that order because that is what `electron-updater` reads (ADR-116), sha256 on the far side because a truncated upload looks like a success. Porting that to C# while also changing the interface that invokes it would make the first failed deploy unanswerable — new logic or new wrapper? — with rollback of both as the only way to find out. One thing changes at a time.

**So `cedar deploy` does not deploy.** It runs every check `deploy.ps1` would run — branch, clean tree, version tag, ssh, the live version against the working copy, free disk for the two-copy swap — prints `0.11.0 → 0.11.1`, and hands over the command. `deploy.ps1` has no safe stopping point before the swap, so "take it to the last step and stop" would have meant cutting the script open in the first sentence. What the tool can do instead is guarantee that nothing about the command is a surprise when Marty types it.

`cedar restart` is the one destructive command here, and it is destructive on the server's own terms: sudoers on the droplet grants NOPASSWD for exactly `systemctl start|stop|restart cedarclerk`. It shows what is running, warns that the blog goes down with it (one Kestrel serves both hosts), asks with the default set to no, and health-checks afterwards.

**Four hours of the work were spent finding out what the droplet can actually answer, before a line of code.** Three findings changed the plan:

- **`journalctl -u cedarclerk` reads without sudo.** The unit runs `User=martycow`, so its entries belong to that user. `.claude/rules/production-environment.md` asserted the opposite and has been corrected — it was sending anyone who read it down a `sudo` path they did not need. The catch is the opposite of a permission problem: the journal holds **1.49 million lines a day** (EF logs every statement), so `cedar logs` has no "show everything" mode and never will.
- **`sysstat` is installed and sampling every ten minutes.** That is what makes the CPU and memory graphs real measurements rather than decoration. It also settled the window: an hour is six points and does not draw, so the default is 24 hours and 144.
- **There is no backup on that machine at all** — no crontab, no `~/bin`, no `/mnt/backup`. The Pi's nightly `sqlite3 .backup` did not travel (ADR-114). So `cedar backup verify` is not a tick-box: it reports the absence, and names what does exist and what that costs — a week-wide loss window, restores that take the whole machine, and a copy in the same account as the droplet. **`backup now` was deliberately not built**: making a backup is `T-071`, new behaviour on the server, and building it inside a session about an interface would be smuggling a server change in as a button.

**`--dry-run` is an implementation swap, not a branch.** Every external call — local process and ssh alike — goes through `ICommandRunner`; the dry-run implementation prints and returns success. No command contains an `if (dryRun)`, because such a branch is eventually forgotten on exactly one path, and the one it is forgotten on will be the one that ships. The same seam covers HTTP: `--dry-run` does not even fetch `/api/health`, since a GET to production is still reaching out.

**The graphs are braille, and Spectre has no line chart, so there is one now** — `BrailleChart`, a real `Renderable` emitting `Segment`s, at 2×4 subpixels per cell. Four rows resolve sixteen levels where a bar chart of the same height resolves four, which is the difference between a line and a flat strip on a droplet that lives between 5% and 9% CPU. The axis is zero-based with an auto top, and the top tick prints the number, so the scale is stated rather than implied. Two rules the renderers keep: a finite sample never draws as nothing — zero draws the baseline, because "measured, and it was zero" and "no data" must not look identical on a server dashboard — and `NaN` is a gap, because sar leaves holes and interpolating across one invents history.

**Colour is never the only carrier of meaning.** Every bar has its percentage beside it, every status glyph its word. `--no-unicode` turned out to be a bigger promise than the flag first suggested: it now also flips the console profile, so Spectre's own panel and table borders degrade with it, and it replaced the `BreakdownChart` with a hand-drawn one — that widget draws its bar and legend swatch with fixed Unicode characters whatever the profile says, and was the last thing on the screen still printing box glyphs into `cmd.exe`. Em dashes were swapped for hyphens throughout for the same reason; the middle dot stayed, being present in CP437 and CP1252.

**The splash animates**, at Marty's request: the cedar grows out of the ground, the letters wipe in from the left, a highlight sweeps across them. It skips on a keypress, does not animate into a pipe, drops the tree before it wraps on a narrow terminal, and transliterates to ASCII through one mapping rather than a second hand-kept asset.

**`cedar test` draws a tick per test as the results arrive** — also Marty's ask, and the one place where the pretty thing needed a guard rail. The verdict is the script's exit code and nothing else; the grid is built by parsing output formats that belong to `dotnet test`, `vitest` and Playwright and can change under us, so an unrecognised line is counted as nothing rather than guessed at. Three runners report at three granularities, and reconciling them is why the tracker is more than a regex: `ng test` refuses to forward a reporter flag to vitest, so the frontend names nothing and reports only a summary — counting only named tests would have silently under-reported it by eighteen. Each phase is therefore topped up from that runner's own summary when it closes. Two false-positive traps found while writing it: `Passed: 10` in a summary is a count and not a result, and `check-contrast.mjs` prints `=== light ===` in exactly the shape `test.ps1` uses for a phase.

`Scripts/test.ps1` gained one opt-in switch, `-Detailed`, which adds `--logger console;verbosity=normal` to `dotnet test`. It changes console verbosity and nothing else, and an ordinary run keeps the short output.

**Nothing here stores a secret.** The config in `%APPDATA%/cedar/config.json` holds a host, paths and a *path to* an ssh key; authentication stays with the agent that already works. There is a test that fails if a plausible secret name ever appears in it, and another that fails if the binary name is written down anywhere but `CliConsts` — the product name is still an open question (`Q-17`), and a rename should be one edit.

Verified against the live droplet, not only against fixtures: `status`, `logs`, `db`, `backup verify` and `deploy --dry-run` all ran against `cedarclerk-periwinkle`. **78 new tests, `dotnet test` 881/881**, frontend 18/18, contrast clean — all of it run through `cedar test` itself. The parser fixtures are real output captured from that machine on the day, on purpose: a parser test that needs fra1 to be reachable fails on a train and passes when the format has quietly changed.

**And `cedar` is now actually a command** (ADR-118 decision 9). It installs as a .NET global tool — `PackAsTool` in the `.csproj`, `.\Scripts\install-cli.ps1` to pack and install, `-Uninstall` to take it back. A global tool is the option that leaves the environment alone: the SDK already owns `%USERPROFILE%\.dotnet\tools` and already put it on PATH, so there is no variable to edit and none to clean up afterwards. Re-running the script is the entire update procedure, and it packs with the version out of `Consts.CurrentVersion` rather than a number invented at install time.

That move broke one assumption quietly, which is why it is written down: `ConfigStore.FindRepoRoot` looks for `CedarClerk.sln` by walking up from the executable, and a global tool lives in the SDK's store where walking up finds nothing. So the installer seeds `RepoRoot` into `%APPDATA%\cedar\config.json`, merging into an existing config rather than replacing it. Checked from `C:\Users\marty`: `cedar deploy` reached the droplet and refused for the right reasons — wrong branch, dirty tree, same version already live.

**The logo keeps moving now** (ADR-118 decision 10). A highlight crosses the letters twice a cycle and, once every ten seconds, each letter hops a single row in turn. Doing that meant the menu could no longer be a `SelectionPrompt`: the prompt owns the bottom of the screen and repaints only its own list, so anything drawn above it is frozen by construction. The menu is now one live frame that contains the logo, the header and the options together — which costs the arrow-key handling and buys, besides the animation, a header that never scrolls away and a submenu that replaces the screen instead of stacking a second copy of everything beneath the first. It falls back to the old prompt on a redirected, non-ANSI or simply too-short terminal, because a menu that draws half a logo and then eats the keyboard is worse than a plain list.

Two details worth keeping. The animation is a pure function of elapsed time, not a coroutine — the menu asks what the logo should look like now and repaints only when that answer changes, so an idle menu is a sleeping thread rather than 25 repaints a second down an ssh pipe. And the hop is implemented as one blank row above the art: "this letter is up" becomes "read its columns one row lower", the same lookup for a letter in the air as for one at rest. Without that headroom the top row of CEDAR would be clipped by the edge of the block, and the jump would read as the letter losing its lid.

**`cedar claude`** opens a terminal in the repository with `claude /remote-control` already running — a new menu entry and a command. A second window rather than a child process, because both programs read the keyboard and one console between them means two readers fighting over every keystroke. It uses Windows Terminal when it is installed and plain pwsh otherwise; `wt` splits its own arguments on `;`, so its form carries no `Set-Location` at all — `-d` already puts the tab in the right place. `ICommandRunner` gained `LaunchDetachedAsync` for it, a separate method rather than a flag on `RunLocalAsync`, since that one captures stdout and blocks until exit — the two things an interactive session must not do. It goes through the interface regardless, so `--dry-run` still means "touch nothing", opened windows included.

**A `LIVE` tag now names the commit production is running** (ADR-118 decision 12). `deploy.ps1` moves it onto HEAD — but only after the health check has heard the shipped version back from the server, because until then "this commit is live" is an intention rather than a fact, and a `LIVE` on something that never finished shipping is worse than no tag at all: a wrong answer to "what is running" gets acted on, a missing one gets investigated.

The half of the request that asked for policing turns out to need none. A tag name resolves to exactly one object, so `git tag -f` moves the label rather than adding a second one; there is no reachable state where `LIVE` sits on two commits. What does need watching is the tag being *stale* — after a deploy from another machine, a hand-edit on the server, or a rollback git never heard about. So the preflight reads `CurrentVersion` at the tagged commit and compares it with what production actually answers, and says so when they disagree. A warning, not a stop: the deploy such a stop would block is the thing that fixes the tag.

`LIVE-PREV` mirrors the server's `app.prev` and stops there. The server keeps one previous release, so a longer chain of tags would promise a rollback the server cannot perform. Three consequences fall out of that: the outgoing `LIVE` becomes `LIVE-PREV` even when it is the same commit (re-deploying a commit makes `app.prev` that commit too), a first-ever deploy *deletes* any `LIVE-PREV` instead of leaving one from a previous life, and `-Rollback` moves `LIVE` back and deletes `LIVE-PREV` — keeping nothing behind it, exactly as the server keeps nothing behind `app.prev`. With nowhere to go back to, `LIVE` is removed outright: after that kind of rollback the running commit is genuinely unknown here, and no tag is the only honest way to say so.

The tag stays local — Marty's call. The cost is stated rather than hidden: from another machine, and from GitHub, there is nothing to look at. What it buys is a `deploy.ps1` that still touches no network for git, and no force-push inside the riskiest script in the project.

Exercised in a throwaway repository rather than reasoned about: deploy, re-deploy of the same commit, drift, rollback, and rollback with nothing to return to. `cedar deploy` grew a matching row, and all three of its branches were checked against the real repository with a temporary tag that was removed afterwards.

**A glossary, grouped by tile** (ADR-118 decision 13). `cedar legend`, a menu entry, and `cedar status --legend` which puts the panel directly under the dashboard; without the flag, `status` leaves one grey line pointing at it, because otherwise the glossary is a command you have to already know about to find. The order follows the screen rather than the alphabet — an A-to-Z list is easier to build and needs you to already know which word confused you. And the definitions are about *this* machine: "rss — resident set size" is a translation, not an explanation; what earns the line is that on 2 GB with no swap, that is the number that gets the service killed. Same for `wal` (growing means checkpoints are not running), `avail` (no swap, so this is all there is) and `no autostart` (the unit is disabled, so a maintenance reboot leaves the site down).

**The test grid is a field of squares now** (decision 14) — `□ ▣ ⊠ ⊡`, filling in as results land. The ticks were not violet because of the palette: U+2714 is in the emoji set, so Windows renders it from Segoe UI Emoji in that font's own colour and ignores the ANSI colour entirely. One tick per line hides that; a wall of eight hundred does not. Geometric Shapes have no emoji presentation, so changing the glyph fixes the cause rather than the symptom. Three requirements decided the family, none of them taste: one column wide in both modes (`Ok`/`Bad` are `OK`/`XX` in ASCII, and a grid that wraps by counting results rather than characters was quietly drawing at double width), not an emoji, and distinguishable by shape as well as colour — filled, crossed and dotted survive a monochrome terminal where three shades of one square do not. The filled cell keeps its outline, so a finished grid still reads as cells instead of one green slab.

The field is drawn up front from what the last run produced (`%APPDATA%\cedar\last-run.json`, keyed by the flag set, since `-Backend` and `-Smoke` are not the same suite). Without that there is no "filling" to watch — a grid that appears as it goes has nothing to fill. The number is a guess and is handled as one: the field is the larger of remembered and actual, the final frame is redrawn from the actual results alone, and nothing decides anything from it. A stale count makes one run's animation slightly wrong and is then overwritten; it cannot make a red run look green, because the verdict is still the exit code.

Fixed on the way past: `Live` hides the cursor, and on Windows that throws `IOException: The handle is invalid` when output is redirected — so `cedar test` from a script died before running a single test. Non-interactive output now skips `Live` and writes the finished frame once.

And `install-cli.ps1` no longer lies. A `cedar` left open in another terminal holds its own files in the tool store; the uninstall then fails with "Access to the path … is denied", and the install that follows prints "Tool is already installed" and exits **zero**. The script cheerfully reported a new version over a binary that had not changed — which surfaces later as a command that is missing for no visible reason. The uninstall is now allowed exactly one failure ("not installed"), anything else stops the run and names the processes holding the lock, and an install that says "already installed" is treated as the failure it is rather than as success.

**88/88** on `dotnet test CedarClerk.Cli.Tests`, eleven of them new: the hop moves a letter exactly one row and disturbs no neighbour, every letter hops once per cycle, the cycle spends more than half its time at rest and returns to rest before it wraps, and both terminal forms of the Claude session start in the repository. The grid added four more: the field is drawn to the remembered size and fills in, a finished run leaves no placeholders behind, every cell glyph is one column wide in both modes, and none of them is an emoji that would paint itself.

## 2026-08-12 — the desktop stopped being a second Cedar Clerk

Marty's report: "I use the same email on the desktop, but it is actually a different account." That is not a defect — it is the price ADR-108 wrote down and accepted two days earlier, in the sentence "one identity is not one data set", with the warning that a shared account makes the expectation of shared data *stronger* rather than weaker. It took a day of real use to become intolerable.

The ask was that everything should sync with the cloud where possible, with assets staying put — at most auto-generated thumbnails riding along with indexing — so the desktop reaches real files and the browser sees only previews, marked as fingerprints rather than files.

**The answer was not to build synchronisation. It was to remove the second copy.** The window now loads `cedarclerk.mooexe.dev`, there is one database, and nothing to reconcile. ADR-105 refused two-way sync because merging offline TipTap edits is either a CRDT or somebody's lost paragraph, and this project has already lost text once (`T-060`, three guards built around it). That reasoning was right; the conclusion drawn from it was not. The correct one was never "sync is dangerous" but "there should not be two copies" — ADR-105 chose a second instance as the lesser evil because the asset index needed a local process, and now only the *process* is local, not the data.

**The sidecar became an agent.** The same `CedarClerk.Server.exe`, launched with `Cedar:Agent:Enabled`, exits `Program.cs` before anything else exists: no data directory, no SQLite, no migrations, no Identity, no Quartz, no bot, no SPA, no landing, no `/api/*`. It answers `/agent/*` and does three things — walk a folder, stat a file, render a JPEG. An early return rather than conditionals around the rest, and that is not tidiness: it means agent mode cannot quietly inherit a capability somebody adds below later.

It stayed a .NET process rather than moving into Node for one checkable reason: it already contains the `.blend` preview parser (Marty's own request), the WAV header parser and ImageSharp's coverage of TGA/TIFF/QOI/PBM/WEBP. Rewriting that in JavaScript is weeks of work and a second set of format bugs, to save 70 MB that ADR-104 already agreed to pay.

**Division of labour: the agent reads the disk, the page uploads.** The page already holds a session cookie, so the agent needs no credentials and the shell performs no authentication at all. The side benefit turned out to matter more than the saving — the work now happens inside an ordinary screen with a progress bar and a cancel button, instead of in a background process nobody can interrogate.

**The agent needed a lock the sidecar never did.** The sidecar was a real Cedar Clerk, closed by Identity's cookie. An agent has no Identity, and an unauthenticated loopback service that lists folders is readable by every other process on the machine — and by any page in any browser, since browsers reach 127.0.0.1 too. So: a 32-byte bearer token generated per launch and never written to disk, checked by a filter over the whole group so a later endpoint is closed by default rather than by memory; and **granted roots**, because a token only says "you are the shell" while a grant says "and this is the folder the human picked". Grants compare *resolved* paths, since a path that climbs out with `..` is a string starting with the root and a place nowhere near it, and they require the trailing separator, or a grant on `C:\Art` would also cover `C:\Artwork`.

Grants persist in `granted-folders.json` and are restored at launch — otherwise re-scanning after a restart would mean re-picking the folder, friction with no safety in it, since the gesture was made, just last week. They are kept **by the shell and never read from the server**: restoring what the cloud recorded would let a compromised page write any path into a project's root and then ask for it back. The residual cost, stated plainly: a folder picked once stays readable until that file is edited — the same bargain a browser strikes with persisted directory permissions.

**The hosted server lost the ability to walk a disk, rather than having it switched off.** `Cedar:AssetIndex:Enabled` and the index endpoints are deleted; the cloud now *accepts* a description (`PUT source`, `POST batch`, `POST sweep`, `PUT thumbs`). That is a stronger statement than the flag ever made — no configuration of production can enumerate its own filesystem, because the code is not there. Accepting a list of filenames from the account that owns them is ordinary data entry, bounded by numbers rather than intentions: 500 rows per batch, 200 000 per project, paths capped at 1024 characters with `..` and absolute prefixes refused, 256 KB per preview with a JPEG magic-byte check, 20 previews per request.

**`Cedar:Desktop` went too, and its bug went with it.** The landing page intercepts exactly `GET /` without a cookie, so a shell opening `/projects` never meets it. The thing Marty found on 10.08 — the app greeting him with a price table — is now solved by which address the window asks for, not by a flag. One less flag sitting next to the security ones.

**Every preview, no limit — Marty's call, after the arithmetic was on the table**: 20 000 images is 20 000 decodes and roughly 600 MB. Three things make it affordable rather than reckless. The pass is **resumable and idempotent**, driven by the server's own answer to "what is still missing" (`thumbs/pending`), so an interruption at the eight-thousandth file continues instead of restarting, and can be caught up next week. A re-scan **keeps** previews of unchanged files; dropping them would re-upload the whole folder every time. And there is a system ceiling (`Cedar:AssetIndex:ThumbBudgetBytes`, 2 GB) that refuses out loud and names the number — "no limit" is about the author having none, not about a 48 GB disk having none. Silently filling it would be denial of service dressed as generosity.

**Fingerprint, not file.** The index now opens from anywhere, so most of the time a screen is looking at a preview and some numbers standing in for a file elsewhere. `Project.AssetRootMachineId` records which machine holds the folder; a client compares it against its own. A browser has no bridge, so it has no machine id, so it always shows fingerprints — the truth rather than a fallback. `isLocal` defaults to **false**: an unknown machine, a project indexed before today, any browser. Being wrong that way costs a dead button and a moment of doubt; being wrong the other way makes a promise nothing will keep. Previews now have three states rather than two — stored, coming, and impossible — because without the middle one a freshly indexed folder seen from a browser looks exactly like a folder full of formats nothing can decode.

**The cost, said out loud rather than buried: without a network the desktop does not work at all.** Local mode was the only offline story and it is gone. A 30-day cookie saves the sign-in, not the connection. And the boundary this moves is real: the window loads a remote origin and the SPA renders TipTap documents and pasted HTML, so **an XSS on the domain becomes a read of the chosen folder**. What narrows it: an origin gate in preload *and* an independent one in the main process on every call (the preload check runs inside the renderer and so cannot be the last word about it), blocked off-origin navigation, and no `shell.openPath` — Marty chose to lose the double-click that opens Blender, because reading a folder is what the feature needs and executing a file is what an attacker needs. `T-145` (code signing) got heavier by the same logic: the domain that delivers updates now also hands out disk access.

Two smaller things fixed on the way, both mine. The agent's English strings were about to reach the screen through the sync service; agent prose is now caught as a *kind* of failure and replaced with the page's own localised wording, with the original going to the console — a page cannot translate a sentence a machine invented. And the first version of the walker test used a hand-copied "1×1 PNG" whose IDAT checksum was wrong; ImageSharp's `Identify` verifies it and refused, so the test was measuring a corrupt file rather than the walker. It now uses a real, deliberately non-square PNG, which also catches a transposed width and height.

`T-121` closes. `T-137` (no backup for the desktop database) is **moot rather than done** — there is no such database. The `cedar.db` left in `%APPDATA%\CedarClerk` by earlier versions is neither opened nor deleted: it is Marty's data, and removing it quietly is not the shell's call.

Verified: `dotnet test` **803/803** (30 new, covering the walk, the grant boundary, the import path check and the re-scan judgements), frontend 18/18, contrast clean, smoke **54/54** — the landing still answers a stranger with prices from the code, which is the check that removing the flag did not change the public server. Not yet deployed; the order matters and is written down in `docs/DESKTOP.md`.

## 2026-08-12 — the first real desktop update, and the bug it walked straight into

Marty deployed with `-Desktop`, the installed copy found the update on launch — and then quit with "exit code 1" when he accepted it. The installer itself ran fine, which is the detail that names the culprit: nothing was wrong with the update, only with how the app got out of its own way.

**`taskkill /f` gives the process it kills an exit code of 1.** The server child had an `exit` handler that reports "the local server stopped unexpectedly" in a modal `showErrorBox` — correct behaviour for a server that dies on its own, and invisible until now because every other stop happens after the window is already gone, which the handler checks for. Installing an update stops the server **while the window is still open**, in the middle of quitting, so the box appeared and blocked the main process mid-hand-over. The fix is one line and a rule worth keeping: an intentional stop detaches the crash reporter before killing, because **a shutdown we asked for cannot be a failure**.

**The shell speaks English again.** The update dialog was the only Russian text in a shell whose other dialogs ("Version mismatch…", "The local server stopped unexpectedly…") are English. Marty asked for English; half-translating an interface reads as a bug rather than as care. The app's own UI is unaffected — it has real translations.

**There is a log now**: `%APPDATA%\CedarClerk\update.log`. A packaged app has no console and an update ends by quitting, so this incident had to be reasoned out from first principles instead of read. Each check, version, download, choice and error is one line.

**The consequence to remember, now written into ADR-116 and `docs/DESKTOP.md`: an update is installed by the version being replaced.** The fix ships in 0.10.9, but the copy installing it is the old one — so this same dialog appears one last time on the way in. Installing 0.10.9 by hand from `/downloads/latest` skips that; everything after it is clean.

Verified on the real packaged build rather than in development: three launches, each starting the server and answering the update check against production (`latest version: 0.10.8` — the whole chain through Cloudflare works), each closing with zero Electron and zero server processes left behind, and the log written where it should be.

## 2026-08-11 — pictures on the blog open where you are reading them

Marty's ask, from a desktop browser: click an image in a post and have it fill the screen — **not** open in a new tab. That is the whole feature, and the two words that shaped it are "not" and "tab": the article has to still be there when the picture closes.

**One viewer per page, and it is a gallery.** The script collects every image in `.post-sheet` once, so a collage of eight photos is eight frames of one viewer with arrows and an `N / M` counter, rather than eight unrelated popups. Keys work the way they look: ←/→ move, Escape closes, and clicking anywhere — the picture included — closes it, because "click it again to get out" is the gesture people try first.

**The caption comes along.** If the image has a `figcaption`, the enlarged view shows it; without that, the big version would be *less* informative than the small one it replaced. It is written with `textContent`, never `innerHTML` — owner-authored text on a public page, same rule the glossary tooltip follows.

**The zoom cursor is added by JavaScript, not by a CSS selector.** So "this looks clickable" and "this actually opens" cannot drift apart, and a reader with scripts off is not invited to click something nothing will answer.

**A hole this opened, and closing it is the part worth remembering.** Copy protection on private posts (ADR-063) was bound to `.post-sheet`. The viewer's overlay is appended to `<body>` — outside it — so the first working version made every picture on a protected post right-clickable and draggable the moment it was enlarged. A feature about looking at images had quietly disabled a feature about not taking them. The guard now listens on `document` and filters with `closest('.post-sheet,.lightbox')`, which also covers an overlay that does not exist yet when the guard runs.

Verified in the smoke suite rather than by eye: a post with an image and a collage, click → the overlay is visible, the URL is still the post's, the source matches the picture clicked, the caption and `1 / 2` are there, → moves to the second image, Escape closes it. Full suite green.

## 2026-08-11 — the desktop app updates itself, and the deploy is what publishes it (ADR-116)

Marty asked for two things in one sentence: the deploy should produce the desktop installer, and an installed copy should end up on the new version by itself. Before this, the installer was a local artifact of `build.ps1 -Installer` that never left his machine, and "updating" meant rebuilding and reinstalling by hand.

**The update mechanism is three files in a folder.** `electron-updater`'s generic provider needs `latest.yml` (version + sha512), the installer it names, and a `.blockmap` that lets a client fetch only the chunks that changed. `DownloadEndpoints` serves them with ordinary static-file middleware out of `CEDAR_DATA_DIR/downloads`; there is no update service, no GitHub release, no token in the client. GitHub Releases was the obvious alternative and was rejected for needing either a public repository (there isn't one) or a credential shipped inside the app.

**The folder lives under `data/`, and that is the interesting part.** `app/` is replaced wholesale by every deploy, so an installer stored there would vanish on the next ordinary release and take the manifest pointing at it along — every installed copy would then be checking for a file that no longer exists. `data/` survives by construction, so it is where the installers go. This is the single exception to ADR-113's "the deploy never touches `data/`": it writes `data/downloads/` and nothing else there, and that directory is the only thing under `data/` that a rebuild can recreate.

**`latest.yml` is written last, on purpose.** The `.exe` and its blockmap are uploaded into a staging subdirectory, checksummed against the local file and only then moved into place; the manifest follows. Since the manifest is the only file a client reads, a publish that dies halfway is *invisible* rather than broken — copies keep seeing the previous version instead of downloading half a file and failing on sha512. The upload reuses the deploy's own resumable stream (ADR-113), because 119 MB over a home uplink is exactly the size that gets dropped.

**Building the installer is opt-in: `.\Scripts\deploy.ps1 -Desktop`.** electron-builder costs minutes and ~119 MB of upload on top of the usual ~50, and most deploys change the server and the frontend, which reach the desktop with whatever desktop build comes next anyway. The honest cost is that the site's version and the published installer's version can differ; the deploy prints which installer is live, and `/downloads/latest` redirects to whatever the manifest actually names rather than guessing from the running server's version.

**Both new steps run after the health check**, so a failed electron-builder cannot cost a deploy that already succeeded — it reports in yellow and the summary box still prints. The deploy also refuses to start if `CedarClerk.Desktop/package.json` and `Consts.CurrentVersion` disagree: it corrects the file and stops, rather than shipping an installer whose version exists in no commit.

**Two traps handled in the shell.** NSIS overwrites `resources/server/CedarClerk.Server.exe` while our own sidecar is holding it open, so `stopServer()` became synchronous (`spawnSync`) and runs before `quitAndInstall()` — which also fixes a quieter pre-existing bug, since `process.on('exit')` never ran the old asynchronous kill at all. And update checks are skipped entirely when `app.isPackaged` is false: an unpackaged `npm start` has no `app-update.yml` beside it and would have opened on an error box.

**What is not solved: code signing.** Without a certificate `electron-updater` skips signature verification and trusts the sha512 in a manifest fetched over the same HTTPS as the file. Practically, trust in an update equals trust in `cedarclerk.mooexe.dev`. With one installation on Marty's own machine that is fine; with a first outside user, signing stops being a SmartScreen annoyance and becomes a security boundary. Written into ADR-116 and the risk table rather than left to be discovered.

Verified locally, not assumed: the installer builds (118.7 MB) with `latest.yml` and a blockmap beside it, `electron-updater` is inside `app.asar`, `app-update.yml` carries the generic provider URL, and a real published server answers `/downloads/latest.yml` as `text/yaml` with `no-cache`, `/downloads/latest` with a 302 to the installer, and `/downloads/*.blockmap` with a week-long cache — while the same binary in desktop mode answers 404, as it should. `dotnet test` **764/764**; the localization guard caught two hardcoded English strings in the new endpoint before they shipped, and they moved into `ErrorMessages`.

## 2026-08-11 — every time on screen is Pacific, and the app stops being seven hours out (ADR-115)

Marty's ask was a display rule: the server keeps UTC, the blog and the app print Pacific time. Reading the code turned up that the second half was already broken in a way nobody had named.

**The bug underneath the request.** Everything is stored in UTC — `DateTime.UtcNow` appears 125 times, `DateTime.Now` never, and file times are taken as `LastWriteTimeUtc`. But SQLite has nowhere to keep `DateTimeKind`, so EF hands those values back as `Unspecified`, and `System.Text.Json` prints them **without a trailing Z** — which a browser reads as *local* time. The app was showing UTC numbers labelled as local: seven hours ahead, today. Two components had a hand-rolled `utcDate()` helper that patched it; about eighteen others did not. `UtcDateTimeConverter` now writes every `DateTime`/`DateTime?` as UTC with its `Z`, in one place, because a converter cannot be forgotten by the next component and a helper obviously can.

**The zone is `America/Los_Angeles`, not a literal PST.** "Pacific Standard Time" is the winter half; from March to November Los Angeles is on PDT, so a fixed −8 would have been an hour wrong on the day it was asked for. The named zone follows the change and the label follows reality — `PDT` in summer, `PST` in winter. It is spelled once per side (`Consts.General.DisplayTimeZone`, `DISPLAY_TIME_ZONE`), which is where a per-user timezone would land.

**On the blog** every date now goes through `DisplayTime`, including the timeline's month grouping — a post published at 00:30 UTC on the 1st belongs to the previous month here, and grouping it by the UTC month would have filed it under a heading its own card contradicts. Times carry the zone name because the audience is worldwide; the app's do not, because the operator is in one place and `PDT` next to every row is noise. Machine-facing timestamps are untouched: RSS `pubDate` stays GMT, `<time datetime>` stays UTC.

**In the app** a `zonedDate` pipe replaced all 42 `| date:` usages across 13 screens, and the three remaining `toLocale*` spots (the scheduled-post chip, the stats sparkline labels, the editor's date pill) now go through the same formatter. The pipe reads an offset-less value as UTC rather than local, so it is correct even where the old wire format resurfaces.

**Not changed, deliberately**: the scheduling picker still reads its `datetime-local` field in the browser's zone. That is input rather than display, it already shows a PT line in its hint, and doing half of it silently would make that hint the first thing to lie.

`dotnet test` **764/764** (7 new for the zone, 5 for the converter), frontend **18/18**, contrast clean. One existing test changed its expectation rather than its subject: a header slot given midnight UTC now prints the previous day, which is the correct answer to "what date was that here".

## 2026-08-11 — production is a DigitalOcean droplet now (ADR-114)

Marty ran `docs/migration-to-digitalocean.md` end to end. Production lives on `cedarclerk-periwinkle` (fra1, Ubuntu 24.04.4 LTS, x86_64, 1 vCPU / 2 GB, 45 GB free) and the Raspberry Pi is out. Same paths, same unit, same drop-ins, same Cloudflare Tunnel onto `127.0.0.1:8080`, same single Kestrel process serving both hosts — the application did not notice the platform change. It was a copy rather than a port because the server publishes framework-dependent with no RID: portable IL, which is a property that existed for a different reason (the Pi never built anything) and paid off on the day it was needed.

Everything that described the Pi as production was rewritten **from the running machine, not from memory**: `.claude/rules/production-environment.md` (rewritten whole), the telegram-bot / secrets / ef-migrations rules, `docs/ARCHITECTURE.md`, `CLAUDE.md`, `AGENTS.md`, `docs/DESKTOP.md`, `docs/integrations-setup.md`, the roadmap, the backlog, `TASKS.md` and the build/test/e2e scripts. `CHANGELOG.md` and `docs/DECISIONS.md` were deliberately left alone — they record what was true then, and editing them to match today's machine would lose why the decisions were made.

**Backups got worse, and that is written down rather than smoothed over.** The Pi's nightly `sqlite3 .backup` + rsync to a microSD with 14 dated copies did not travel; there is no crontab and no `~/bin` on the droplet. In its place is DigitalOcean's paid **weekly** droplet backup. Three concrete differences: the loss window went from a day to a week; a restore takes the whole machine, so "yesterday's database with today's code" is not a thing that can be asked for; and the copy now lives in the same account as the original, which the card in another device did not. `sqlite3` is installed and a nightly job is ten lines — `T-071`, raised to High, with an off-account target as the second half.

Found while auditing, not fixed: **the unit is `disabled`**, so a host-maintenance reboot leaves the site down until somebody notices (`T-143`). One command, but it needs the sudo password — `NOPASSWD` is scoped to `systemctl start|stop|restart cedarclerk` on purpose. The move retired `T-070` (the Pi OS upgrade) outright and took the disk-pressure problem with the machine.

## 2026-08-11 — the deploy stopped being able to take production down (ADR-113)

`scp -r publish/* host:app/` kept dying near the end — `client_loop: send disconnect: Connection reset` — and it always died **after** the service had been stopped. So every dropped connection left production down with half a build in `app/` and no `wwwroot` at all, answering 502, and the re-run started the same 50 MB from zero because scp cannot resume. That is the state this rewrite was written in: 50 of 174 files on the server, service `inactive`.

**Everything slow now happens while the old version is still serving.** Build, pack, upload, checksum and unpack all run against `app.new`, which nobody reads. The service is stopped only for two `mv` calls — measured on the server itself and printed in the report, about a second — and the previous release stays as `app.prev`, so `-Rollback` is those same two renames rather than a rebuild.

**One tarball instead of 174 files, uploaded as a resumable stream.** Not only because 50 MB compresses to about half and one sequential transfer spends no round trip per file: a byte stream has a *position*, and a dropped `scp -r` cannot say where it got to. The script asks `stat -c %s` on the far side and sends the local tail from that byte into `cat >>`. Verified against the live server — a transfer cut off at 2 MB of 5 continued and matched sha256. The artefact is cached in `%TEMP%` keyed to a signature of `publish/`, because repacking would change the bytes (gzip stamps a time) and glue a tail onto a different beginning; `-SkipBuild` continues the same file.

**Nothing is touched until it has been proved good**: sha256 on both sides, `CedarClerk.Server.dll` and `wwwroot/index.html` present in the unpacked directory, file counts equal. Any of those failing aborts with the old version still running — a working half-build is worse than a deploy that did not happen, because it looks like one that did.

The output was rebuilt around the same idea: a header saying which version is live and which is about to replace it, per-step timings, a progress bar with speed and ETA during the upload, and a closing panel with a timeline, the measured downtime and the rollback command. Failures print what state the server is actually in and the exact command that fixes it.

## 2026-08-11 (Phase 13) — the last two MUST rows, and a referral badge (T-125/T-126, ADR-112, 0.10.5)

**Phase 13's MUST list is finished.** The glossary learned about projects, versions became a thing the app records, and the blog footer carries Marty's DigitalOcean badge.

**T-125 — a glossary term can belong to a project.** One nullable column, the same move `Draft.ProjectId` made: a project's term and a global one have identical shape, and a second table would have meant nine duplicated columns and no way to move a term between scopes without retyping it. What a document sees is global terms **plus** its own project's — not "or", because "Unity" is global and "the ferry" is about one game and an article about that game needs both. Where both define the same word the project's wins: a narrower scope is a more precise definition, which is the reason to write one. The glossary screen grew a scope row that doubles as "where a new term goes", with a line under it saying so — a filter that silently decided a property would be a trap.

**T-126 — builds are a record, not a tag.** The brief asked for "build/version tagging, and therefore an extensive tagging system"; that describes a result, not a mechanism. A version has a number, a release date, notes and a set of things in it, and a flat `Tags` string holds none of them and cannot answer "what is in 0.4.2" except by scanning and parsing. So it is an entity — the same line ADR-106 drew for tasks.

A task carries a `BuildId` column (it ships in one build, and that gets filtered and counted); a document attaches through the existing `EntityLink` (its relationship is looser — a changelog, a devlog, a design doc it implements — and `Draft` is already thirty-five fields wide with a recorded debt for splitting it). The asymmetry is deliberate and follows the question each side is asked.

**The changelog comes out as a document, not as text to copy.** `POST /api/builds/{id}/changelog` writes a real `changelog` document into the project — heading, the build's notes, one bullet per finished task — and links it back to the build. It then lives an ordinary document's life: edited, translated, published, versioned. Handing back a string would have been a generator whose output has nowhere to go.

**What a build deliberately is not**: connected to git. No repository tags, no CI, no artefacts. It is a record the author keeps, and pretending to be an integration that does not exist would be worse than honestly being a record. The empty state says so in as many words.

**The blog footer carries a DigitalOcean referral badge** on its own row under the made-with line — beside it, the fixed-size hosted SVG would have pushed the centred text off-centre. Explicit dimensions and `loading=lazy` so a slow CDN cannot shift the page as it arrives.

`dotnet test` **752/752**, frontend 11/11, contrast clean, smoke **53/53**. Verified against a running server: a project's glossary view excluding another project's terms, a duplicate version refused, a build from another project refused, the changelog built from two finished tasks with the in-progress one left out, and a task surviving the deletion of its build.

Also written: **`docs/migration-to-digitalocean.md`** — a Pi→droplet checklist built from the machine's actual state rather than from a generic guide. The short version of why it is mostly easy and once dangerous: the published app is portable IL, so armhf→x86_64 changes nothing, and the one real hazard is that two processes must never hold the Telegram token at the same time.

## 2026-08-11 (Phase 13) — the development planner (T-124, ADR-111, 0.10.4)

Fifth of Phase 13's seven MUST rows, and the last one the design handoff had drawn. `/projects/:id/planner` stacks one card per sprint — current, then planned, then "No sprint", then the finished ones collapsed — with a progress bar, the task rows inside, and the dates that decide everything.

**A sprint has no status column.** "Current / planned / finished" is worked out from the two dates every time it is asked. A stored status is wrong the second the clock passes the end date, and keeping it right needs a background job, or a fix-on-read, or a "close this sprint" button — machinery serving a copy of a fact already written down twice. Compared by calendar day and inclusive at both ends: a sprint that ends today is still the current one today.

**A sprint is never "overdue" — it holds tasks that are.** The card says "1 task overdue" in words rather than turning red, which is the same rule the board already follows: only the date is ever red, never the card, never the column.

**"Finished" means the days ran out, not that everything got done.** A finished sprint collapses to a one-line summary *only when everything in it is actually done*; leave one unfinished and it stays open on the screen. Hiding it would be pretending work disappeared along with the date, and that is the one thing a planner must not do.

**The sprint number is stored, and the first attempt got it wrong.** `S14` on a task card has to be real data — parsing a number out of a name breaks on the first sprint called "Polish" — so the number is a column. Assigning it as `MAX(Number) + 1` looked right and is not: delete the highest sprint and the next one you create takes its number straight back. Running the endpoints caught it (the test I wrote afterwards would not have — I wrote it because the run failed). It is a counter on the project now, never wound back, so a deletion leaves a gap: a gap is honest, a second "S3" standing for a different fortnight is not.

**Deleting a sprint frees its tasks rather than deleting them** — the same rule as deleting a project, which leaves the documents. They land back in "No sprint", which is a real group on the planner rather than nowhere. And a task can only join a sprint of its own project; without that check an id from another project would be accepted and the task would fall out of both planners at once.

The board gained sprint filter chips and an `S` chip on its cards, the task card gained a sprint picker, and the dashboard's placeholder became the real sprint card — with the honest empty state when today falls outside every sprint, which is a fact about the calendar rather than a missing feature.

`dotnet test` **752/752**, frontend 11/11, contrast clean, smoke **53/53**. Verified against a running server: three sprints in three states, the order, the overdue count inside the current one, a sprint from another project refused, backwards dates refused, numbers surviving two deletions without reuse, and a task outliving the sprint it was in.

## 2026-08-11 (publishing) — a thread with no progress, and links nobody could find (ADR-110, 0.10.3)

Marty published a 25-part thread to X and reported two things: no progress while it ran, and no links to the posts anywhere afterwards. The database said the publish went perfectly — all 25 parts `Succeeded`, every one with its URL stored, the whole thread out in **17 seconds**. Both symptoms were in the interface, and there were three defects behind them.

**The part chips never moved.** `awaitJobs` takes an `onProgress` callback that repaints them on every poll; the Telegram path passes it and the short-post path never did. So the chips were drawn once as "waiting" and flipped to done all at once at the end. The helper behind them could only address `tg-<lang>` rows anyway, so there was nothing to pass — it is keyed by step id now and works for any network.

**The counter under them was invisible in both themes.** `.pr-count` was painted with `--alt` — a *surface* token used as a text colour: #EFECE4 on a #FCFBF8 sheet in light, #2F2C23 on #2B2820 in dark. The automated contrast check did not catch it because it verifies defined role pairs, not whatever a stylesheet happens to combine.

**The links deleted themselves.** The success toast lives inside the export modal, the progress checklist stacks on top of it, and the toast removed itself after ten seconds — so watching a publish to the end and then closing the checklist reliably showed nothing at all. It now waits to be dismissed.

**And nothing anywhere survived closing the window.** That is the part worth more than the three fixes: the URLs were in the database the entire time, and the only thing that ever read them was a modal. `GET /api/publish/published` reads them out of the publish queue — no second table, because a table beside the one that already stores the answer is two sources of truth waiting to disagree — and the Posts Manager shows, per post, every network it reached with a link to it. A thread contributes one row pointing at its head, labelled with how many messages it is: 25 links to one thread is not a list of posts, it is a list of replies. Republishing shows the newest link only, and a success with no public address (a Telegram channel without a `@username`) is left out rather than rendered as a dead button.

Verified against a copy of production: the reader returns nine rows for Marty, including the 25-part X thread and a 23-part Bluesky one. Seven tests pin what it may return. `dotnet test` **744/744**, frontend 11/11, contrast clean, smoke **53/53**.

**The lesson**: publishing had no surface that outlived a modal. Progress, result and links all lived in a window, and windows close. Value produced by an action needs an address where it can be found tomorrow.

## 2026-08-11 (Phase 13) — the task tracker (T-123, 0.10.2)

Fourth of Phase 13's seven MUST rows. A task is its own entity, not a seventh document type — ADR-106 drew that line months of decisions ago and it held: the content of a task is a set of fields that get filtered, sorted and counted, and its text is only one of them. `Description` is a plain textarea, and its empty state says why out loud: **a task that needs tables or media is really a document**, and should be created as one and linked.

**Both views are built, and neither is a fallback.** The board answers "what is happening", the sortable list answers "what is due and in what order"; the design asks for both and the toggle remembers which one you use. The card is a centered modal, opened through a `?task=` query parameter — which is what makes a task linkable, so the dashboard's "Up next" rail can open one directly.

**The links reuse `EntityLink` rather than adding the `TaskLink` table ADR-106 specified.** T-141 had already generalised that row when documents needed linking to assets, so a task link is an `EntityLink` whose one side is a task, and the pair-ordering that makes A→B and B→A a single row came along for free. The batch reader that draws chips on every card at once needed one thing the asset screen never did: when **both** sides are tasks, one row belongs on two cards. That is a test, not a comment.

**Sorting lives on the server, in one function.** The board, the list and the dashboard rail all answer "what next", and three implementations of that would be three chances to disagree with each other. Within it, overdue outranks priority deliberately: a P3 that was due last week needs answering before a P1 due next month — precisely what a priority-first rail gets backwards.

**A gap found on the way, fixed rather than filed.** Deleting a project left its asset index behind, and deleting a document left its links behind — neither `AssetEntry` nor `EntityLink` has a navigation property, so EF cascaded neither, and both had been quietly accumulating since T-122/T-141. Tasks would have been the third orphan. All three delete paths clean up after themselves now.

**Sprints are deliberately not here** — that is T-124, next. `GameTask.SprintId` exists already so the planner adds a table instead of altering this one, and the dashboard's sprint card says it is not built rather than rendering empty. The two placeholder cells in the projects list (Tasks, Assets) stopped being em-dashes and became real counts on the same commit; Tasks counts **open** tasks, so a finished project does not show its largest number on the day it ran out of things to do.

`dotnet test` **737/737**, frontend 11/11, contrast clean, smoke **53/53**. Verified against a running server rather than by reading: board order, a reciprocal task↔task link showing on both cards, the overdue task first in the rail, `completedAt` surviving an unrelated edit and clearing on reopen, a deleted task taking its links off the other card, and both validation refusals.

## 2026-08-11 (admin) — moving a balance by hand (0.10.1)

Marty asked for a way to put credits on an account from the admin panel. The mechanism that already existed underneath — the ledger from ADR-092, where a balance is `SUM(Delta)` and never a stored number — made the shape of the answer obvious: an adjustment is one more row, not an edit of a total.

**The control is signed, so the same place that gives also takes back.** An amount and an optional note, then Add or Take back. Separating the two directions into two features would have been the wrong instinct: an admin who granted 100 instead of 10 needs the correction to be as easy as the mistake, and correcting through the same ledger leaves both movements visible afterwards rather than making the error disappear.

**It refuses to go below zero.** `CreditWallet.TryAdjustAsync` checks the balance before writing a deduction and returns false rather than leaving a negative number nothing else in the app knows how to read — the charge path already assumed a balance is never negative, and an admin's typo is not a good reason to break that assumption. Zero is refused too: it would write a decision that changed nothing.

**Each grant is its own event.** The ledger's `(Reason, Ref)` uniqueness makes `GrantAsync` idempotent on purpose — a Stripe webhook fired twice must not pay twice. That is exactly wrong for an admin who deliberately grants 10 and then 10 again, so the endpoint passes a fresh reference per movement: two decisions, two rows, twenty credits.

**Self-targeting is allowed here, unlike lock and admin.** Those two are refused server-side because they are one-way doors out of the panel — an admin who locks themselves cannot get back in. A balance is not a privilege, and testing a paid post needs credits on the account doing the testing, so the refusal would have bought nothing and cost something real. The audit log records it either way, actor and target both, along with the note and the before/after numbers.

Seven ledger tests cover the boundaries, including the one that matters most — a deduction past zero refuses **and writes no row**, so a refused correction leaves no trace of having been attempted. Verified against a running server end to end: grant, grant again (50, not 25), take back, refuse −999, refuse 0, and the three audit lines that came out the other side.

## 0.10.0 — the indie-gamedev turn (10.08.2026)

The middle number moved for the first time since Phase 11, and for the reason CLAUDE.md reserves it: this is not a list of fixes, it changes what the app is. A post stopped being the only kind of thing Cedar Clerk holds.

**What the release contains**, in the order it was built — each with its own entry below:

- **Phase 13 scoped on paper first** (ADR-101…107): module not fork, document type as a column, a project that is never empty, Electron over the existing server, no cloud sync in v1, a task as its own entity, an asset index of paths rather than bytes. `Q-1` — the audience question, open since 30.07 — closed: indie game developers.
- **Projects and document types** — `/projects`, the project dashboard, the create dialogs, built from Marty's Claude Design handoff.
- **A desktop application** — Electron around the ordinary server, and the three build/test/deploy scripts that came with it, including a git guard that refuses to ship anything but `master`.
- **The asset index** — a project's local files by path, never copied, with thumbnails (including `.blend`, read out of the file Blender writes them into), header metadata, and document links.
- **One account across both** (ADR-108) — the desktop asks the Pi who you are; the data stays local, and every screen says so.
- **Export fixes** (ADR-109) — Bluesky's advertised images were never actually sent, and a YouTube video reached the short networks as nothing at all. Both now travel.

**Nothing here is deployed yet.** This is the first version of the module to reach `master`; the Pi still runs 0.9.40 until `Scripts/deploy.ps1` is run from `master`.

Human-readable, grouped by session/date, derived from `git log` (33 commits, `6ace957`→`6065cd9`) and the richer context already captured in `docs/ROADMAP.md`/`docs/DECISIONS.md`. Not a raw commit dump — see `git log` directly for that.

## 2026-08-10 (export) — the pictures and the video that never left (ADR-109)

Marty reported that images and a YouTube link had not gone out to Bluesky and X. Reading the code turned one symptom into three defects, and the one that mattered most was not the visible one.

**Bluesky's capability was a lie.** It advertised four images, a byte cap and alt-text support; the record it actually posted carried `text`, `facets` and `reply` and nothing else, and `uploadBlob` was never called anywhere in the file. That matters more than a missing feature: the editor's pre-flight check reads capabilities to decide what to warn about, so a post with four pictures passed every check and arrived with none of them. The app promised and silently dropped. It uploads them now — up to four, in the order a reader meets them, with alt text, compressed through the same `ImageCompressor` the Telegram path uses because Bluesky's blob cap is 1MB and almost no real screenshot fits. A picture that cannot be uploaded is skipped and logged rather than failing the publish: a post without an image is worth far more than a post that never goes out. Only the first part of a thread carries them, because repeating the same four pictures on every part is not what a thread looks like anywhere.

**The YouTube video disappeared with no signal whatsoever.** A `youtube` node was unknown to `CedarPlainText`, which builds the short-post text, and unknown to `PublishValidator`, which decides what to warn about — so it contributed no link, no text and no warning. The blog embeds an iframe and Telegram sends a thumbnail plus a watch link; these two got nothing. They now carry the link, caption first so a thread part reads as a sentence rather than a bare URL. That also fixes threads, where the video used to fall out of the middle of the document.

**X takes no media, which was true and badly said.** `MaxMediaItems = 0` is honest — media was never in its v1 (ADR-093) — but the warning read "3 media items, and this network takes 0", which is arithmetic where a sentence was needed. Zero is not a smaller limit; it is a different fact, and the message says so now. Implementing uploads for X is a separate mechanism and separate money per post, so it was not quietly started inside a bug report.

The lesson is worth more than the three fixes: **`PublishCapabilities` is a promise the pre-flight check reads.** A gap between it and the implementation produces neither an error nor a warning — it produces content that silently disappears. A capability has to arrive with the code that honours it, never before.

`dotnet test` **711/711**, frontend 11/11, contrast 0, smoke 53/53. Still unverified and flagged rather than glossed: a real upload to bsky.social, which needs Marty's account.

## 2026-08-10 (one account) — the desktop asks the Pi who you are (ADR-108)

Marty had signed in on the desktop with the same address he uses on the website and could see what was coming: two accounts, one email, nothing saying they were different. So the desktop now **asks the Pi who you are** — same email, same password, one identity — while the data stays on the machine.

**Why not the full cloud mode** that ADR-105 sketched, where the desktop just opens the website: it would have made the data one set too, and cost the asset index, because the Pi cannot read anybody's disk (ADR-107 keeps that capability off there deliberately). That would have switched off the one thing the desktop exists for. Cloud mode remains a separate, unbuilt option; this is not it.

**Identity is keyed by id, not by address** — `ApplicationUser.RemoteUserId` — because an email can be changed and an identity cannot. Until the Pi is redeployed its `/api/auth/me` reports no id, so the email carries the identity in the meantime; it sharpens itself on the next deploy. And the account Marty made locally yesterday is **adopted** on first remote sign-in rather than duplicated, which is precisely the mess he asked to avoid.

**The honest cost, stated in the ADR and now on every screen**: one identity is not one set of data. Drafts on the Pi and documents on the desktop remain separate, and with a shared account the expectation that they are the same gets *stronger*. The header carries a permanent "local data" chip for exactly that reason. Without it this change would make the confusion worse.

**An unreachable Pi answers 503, never 401.** Telling somebody their password is wrong when the server merely failed to answer sends them to fix the one thing that is not broken. Verified along with the rest by standing up two servers — one playing the Pi, one playing the desktop — so no real credential was involved: sign-in works, a wrong password is 401, registering locally is refused with a message pointing at the server, the Pi going away produces 503 with its own wording, and a session already issued keeps working offline.

**A latent crash found on the way, and it was mine.** `IConfiguration.GetValue<bool>` *throws* on an empty string — and an empty string is an ordinary thing to find in an environment variable; the desktop shell already sets `Cedar__BotToken=''` deliberately. Any flag cleared that way would have taken down whatever endpoint read it with an `InvalidOperationException`. All five boolean flags now go through one helper that treats unreadable as **off**, which is the only safe reading for something that gates a capability.

**And the flaky login test was diagnosed rather than silenced.** Under `--repeat-each=10` against a warm dev server it passed **40/40**, which said the code was fine and the race was with Angular's first lazy-chunk compile — a build that `webServer.url` had already declared finished. A `globalSetup` now opens `/login` once before the suite. Four cold runs since: 53/53. That is consistent with a fix and **not proof** — at the original ~25% failure rate, four clean runs happen by chance about a third of the time — so `T-143` stays open at a lower priority instead of being called closed.

## 2026-08-10 (after the first real launch) — the desktop app was unusable, twice over

Marty opened the desktop app for the first time and asked two mild questions. Both were defects, and both were the same defect really: the shell was built and never signed into.

**A fresh install could not create an account.** Registration is gated by an invite code; the desktop shell set none, and `appsettings.json` no longer carries one since it was correctly removed as a secret. So the first launch met "Invalid invite code" with no code in existence and no way to make one — the app could not be used at all. The gate exists to keep strangers off a shared server, and a desktop app binding to `127.0.0.1` for one person has no strangers, so `Cedar:Registration:Open` drops it there. The invite field disappears from the form, and the line reading "Cedar Clerk is invite-only — ask Marty for a code" becomes "this account lives on this computer, in this app", which is the true thing to say.

**And it opened on the marketing landing page**, complete with the price table. That page is for someone who found the product on the web; in a local application it is nonsense. `Cedar:Desktop` skips it, and `/` serves the app.

Those are now **five** separate environment variables the shell sets, and keeping them separate is deliberate: two are about working at all (port, no bot token), two are access decisions (reading the disk, registering without an invite), and one is presentation (no landing). A single "desktop mode" flag would mean a change to how the product *looks* could open a door to the filesystem.

**And then `npm start` itself would not start.** `Cannot read properties of undefined (reading 'handle')`, thrown before any of the app's own code could run. The cause is that Electron's binary is also a Node runtime, switched over by `ELECTRON_RUN_AS_NODE` — a variable several editors set in their integrated terminals and which is inherited by anything launched from one. Under it, `require('electron')` returns the path to the binary rather than the API, so every destructured name is undefined and the first one used throws. Reproduced here exactly, down to the line number in the stack, rather than guessed at. `npm start` now goes through a launcher that strips the variable, and `main.js` checks what it got back and says so plainly instead of failing on the first property access.

Verified against the real published build rather than the debug one — `GET /` returns the SPA shell with no price table, `POST /api/auth/register` with an empty code returns 200, and `/api/auth/me` answers with the new account. The hosted server is untouched: it still serves the landing and still demands an invite, which the smoke suite (53/53) is what proves.

Also worth recording honestly: the login smoke test has now failed **twice in eight full runs**, always as the first test of the run, always passing alone in about two seconds. That is a flake, not a regression — but it was not "fixed" by making the assertion looser, because the cause has not actually been found. `T-143` says so and says how to reproduce it.

## 2026-08-10 (latest) — previews, metadata and links (T-140/T-141)

Two of the three gaps the asset index shipped with, closed the same day. Marty's one instruction shaped both: **the formats he actually works in have to be supported, `.blend` among them.**

**`.blend` got its own parser, and it is the point of the feature.** A Blender file is what a lot of game projects actually *are*, and it is the one important file no image library will open. Blender writes a small RGBA preview inside the file; `CedarClerk.Core/BlendThumbnail.cs` walks the block headers and lifts it out — 12-byte header, block stride that depends on whether the file was saved 32- or 64-bit, then the `TEST` block's pixels. Only the first megabyte is read, because a scene file can be hundreds of megabytes and the preview sits near the front. The rows come out bottom-up, the way OpenGL hands them over, so the image is flipped. A zstd- or gzip-compressed `.blend` refuses honestly: the preview is in there, but shipping a decompressor to make a thumbnail is not the trade.

**The format table grew from "what a web app would think of" to what a game project holds**: Unity scenes and prefabs, Unreal assets, Godot scenes, `.aseprite`, `.kra`, `.ztl`, `.sbsar`, Reaper and FL Studio projects, FMOD banks, shaders, scripts. Engine files stay kind `other` on purpose — a Unity scene is neither an image nor a document, and filing it under "text" to have somewhere to put it would be a lie of convenience.

**Thumbnails are generated on demand, never during a scan** — a scan that decoded every image would take minutes to produce previews almost nobody looks at — and they are written to `CEDAR_DATA_DIR/thumbs/`, **never beside the source**. The folder being indexed is somebody's game project, usually under version control; dropping files into it would be both a surprise and a diff. They are served through an owner-scoped endpoint rather than the public `/media/*`, which is for what an author chose to publish, not for what sits on their disk.

**Whether a thumbnail is even possible is decided per extension, not per kind.** "Image" covers both a PNG and a Photoshop document; one decodes here and the other does not. Getting this wrong is how a grid fills with broken-image icons.

**Metadata is read from headers, and only when a file is new or has changed.** Image dimensions via `Image.Identify` (no pixels decoded), WAV duration and sample rate via a pure parser in Core. MP3, OGG and FLAC say nothing: each needs a real parser, and an author who reads "2:14" has no reason to doubt it — a wrong duration is worse than none.

**Links (`T-141`) generalise ADR-106's `TaskLink` into `EntityLink`** rather than sitting beside it, because the moment a second pair of things needed linking it would have been two tables doing one job. The pair is ordered before it is written, so linking from either end is one row and the unique index can say so. And the links are **stated, never discovered**: an indexed file lives outside Cedar Clerk, so no TipTap document can reference it — which is why the label reads "Linked documents" rather than the design's "Used in".

Verified by running it against a fixture folder rather than by reading the code: PNG dimensions, WAV duration and rate, `.blend` and `.blend1` previews rendered as JPEGs, `.fbx` correctly refusing one, `.exe` and `Library/` skipped, and a link that survives being added twice and disappears when removed. One thing genuinely unverified and flagged rather than glossed: the orientation of a preview from a *real* `.blend`, since every Blender file in this session was synthetic.

A layout defect caught by looking rather than by testing: the thumbnail was a flex item, so a tall image made its tile taller than its neighbours and a row of sprites came out ragged. It is out of flow now.

`dotnet test` **698/698**, frontend 11/11, `ng build` warning-free, contrast 0 failing pairs, smoke **53/53** (one login test flaked once across six runs and passed alone and on re-run — recorded as `T-143` rather than called green).

## 2026-08-10 (late) — the asset index (T-122, ADR-107)

The reason the desktop app existed a few hours earlier: a screen that reads a game project's asset folder. `/projects/:id/assets` indexes **paths and metadata, never bytes** — and every state on it is built to keep saying so, because the failure mode of this feature is a person believing their files were copied somewhere.

**A second flag, and it is a security decision rather than a preference.** These endpoints make the server walk the server's own disk on a tenant's say-so. On a laptop that is the entire feature; on the Pi, which serves every account from one process, it is a stranger enumerating `/etc` and reading back filenames. So the module's own flag is not enough: indexing needs `Cedar:AssetIndex:Enabled`, and the only thing that sets it is the desktop shell, for its own single-user process. Listing what is already indexed stays available everywhere — those rows are owner-scoped like everything else, and a project indexed on the desktop should still open from the web.

**The walk is hand-rolled**, because `Directory.EnumerateFiles(.., AllDirectories)` throws on the first folder it cannot open and abandons everything after it — one permission-denied directory would end a scan of a whole drive. This one skips that folder, counts it, and reports the number rather than swallowing it. It also skips `Library/`, `Temp/`, `node_modules/`, `.git/`, `Intermediate/` and their kin by name: an engine cache holds more files than the project and not one authored asset.

**Missing is not deleted.** A file the scan cannot find gets a timestamp, not a `DELETE`. An unplugged external drive would otherwise erase an entire index, and the row is what lets the screen say "not found at path" instead of quietly forgetting the file existed. Verified by doing it: scan, delete a file, re-scan — the row survives and is marked; put the file back, re-scan — the mark clears.

**What it does not pretend to know.** There are no thumbnails, so every kind says "no preview · model" rather than showing an empty frame that reads as a broken image. There is no Music chip despite the design having one: nothing in a file extension distinguishes a score from an ambience loop, and a chip filled by guesswork is worse than no chip. "Used in" is always empty and says so in words, because nothing links a document to an indexed file yet. Three backlog rows, not three silences: `T-140`, `T-141`, `T-142`.

Also caught before it could confuse anyone: the new endpoints class was originally called `AssetEndpoints`, which the server root already had for uploaded post media — two extension methods with one name on `WebApplication`, waiting to resolve the wrong way.

`dotnet test` **655/655**, frontend 11/11, `ng build` warning-free, contrast 0 failing pairs, smoke **53/53**.

## 2026-08-10 (night) — the desktop shell, and scripts that refuse to ship the wrong branch (T-121/T-138)

**The desktop app exists and runs.** `CedarClerk.Desktop/` is an Electron main process, a preload script and a builder config — that is the whole shell. It starts the ordinary `CedarClerk.Server` as a child process on a port it asks the OS for, waits for `/api/health`, and opens the ordinary Angular SPA against it. Neither the server nor the frontend is forked, which was the entire argument for this approach (ADR-104).

**One line of server code had to change**, and it was the one found while writing the ADR: the listening address was a literal in `app.Run(Consts.URLs.Localhost)`, and an argument to `app.Run` silently overrides `ASPNETCORE_URLS` — so no port but 8080 was reachable, and two instances on one machine could never both start. It now reads `Cedar:Urls`, then `ASPNETCORE_URLS`, then the old default, so `dotnet run` and the Pi notice nothing. A side effect worth naming: `Scripts/e2e.ps1` had been setting `ASPNETCORE_URLS` for weeks and being silently ignored.

**Verified by running it, not by reading it.** The published server answering on a deliberately odd port (8123); `/api/health` reporting `env: Desktop`; the SPA served; `%APPDATA%\CedarClerk` created with its database, media folder and DataProtection key ring; migrations applied from nothing. Two checks matter more than the rest: the startup log says **`Cedar:BotToken not set — bot is disabled`** — a desktop bot would knock the Pi's bot off its token, and this project has two 409 incidents behind it — and closing the window leaves **zero** processes of either kind, because an orphaned server holds the SQLite WAL lock and the next launch would find a database it cannot open.

**Three scripts, and a guard that had been a rule on paper only.** Marty's branch rule was written into CLAUDE.md that morning: master holds the latest stable version, and deploys run from master and only from master. `deploy.ps1` knew nothing about it and would have shipped `dev` or `indiedev_module` just as readily, leaving a version answering in production that nobody meant to release. `Scripts/_git-guard.ps1` now refuses a wrong branch, a detached HEAD and a dirty working tree, and warns when HEAD carries no tag matching `Consts.CurrentVersion`. The guard was proven rather than assumed — the refusal, the allowed branch, the `-Force` path and the dirty-tree stop were each run and watched, which is how the `-Force` message got fixed: it said "Deploy refused" and then continued anyway.

`test.ps1` runs backend tests, frontend units and the contrast contract in one command (`-Smoke` adds Playwright); `build.ps1` builds the Angular app, the Pi-shaped server and the desktop shell, keeping the shell's version in step with `Consts.cs` so a mismatched pair reports itself at startup. Neither checks the branch, on purpose: building a feature branch is the normal case, and a guard that fires twenty times a day teaches people to reach for `-Force` without reading.

## 2026-08-10 (evening) — the module gets screens (T-120, Phase 13)

Marty generated a UI prototype in Claude Design and dropped the package into `docs/design_handoff_indiedev_core_loop/` — an interactive prototype, 26 screenshots and a README carrying exact token values for twelve screens. Three of them are backed by code that now exists, so those are the three that got built: the projects list, the project dashboard, and the two dialogs that create things.

**The design overturned a decision made this morning.** ADR-103 had removed the project-type taxonomy on the grounds that it was invented here and asked for by nobody. The handoff has it, with four real types and a starter document each — Full game starts with a GDD, a jam entry with a jam plan, a prototype with a hypothesis note, a released game with a changelog. That is a product decision rather than a guess, so the taxonomy is back: `ProjectTypes` in Core, `Project.ProjectType` in the schema, and the create dialog is a type picker exactly as drawn. The starter document's *title* still comes from the client, because the server has one language and the client has two.

**Five deliberate deviations, each because something behind the pixels does not exist yet.** The dashboard's right rail is drawn as three cards — Up next, Sprint, Recent assets — and tasks, sprints and the asset index are all still unbuilt. Rendering them empty would tell the reader "you have no tasks" when the truth is "tasks are not built", so the rail is one card that says the true thing. The same reasoning put an em-dash rather than a zero in the list's Tasks and Assets columns. The list's empty state and the project-settings dialog were built without a design because the handoff marks both as undrawn. And the project-type icon is **not** in the list's Name column, because the handoff does not put one there — which leaves the project type invisible on that screen, so that is a backlog row (`T-139`) rather than a silent improvement.

**Looked at, not just compiled.** The screens were captured live in both themes against the scratch database and compared against the package's own screenshots, which caught four things the tests could not: the shared modal's head put the hint *before* the title and wrapped "New project" onto two lines, the breadcrumb stopped at "Projects" instead of walking to the project name, and the Last activity column was showing each project's creation date — the project row never moves, so the column now reads the newest edit among its documents.

`dotnet test` **639/639**, frontend 11/11, `ng build` warning-free at 587 kB initial against a 650 kB budget, contrast 0 failing pairs in both themes, smoke **53/53**.

## 2026-08-10 — the indie-gamedev turn, on paper (ADR-101…107, Phase 13, no version bump)

Marty's brief redirects the product: Cedar Clerk stops being "an editor that publishes posts" and becomes a toolkit for an indie game developer, where a post is one document type among several living inside a **project**. Around it: tasks, sprints, an asset index, a press kit, script and design tooling; and new publishing targets aimed at that audience (itch.io, Steam, IndieDB, LinkedIn). The brief is explicit that this is a **module inside one codebase, not a fork**.

**Deliberately documents only.** No code, no migrations. The CLAUDE.md rule is decisions before code, and a turn this size deserves writing down before the first migration makes part of it irreversible — particularly since the brief allows deleting the `indiedev_module` branch outright if the business model doesn't hold, which makes reversibility a requirement rather than a nicety.

**The research changed the plan.** Checked against the code rather than the docs, the reusable base is larger than it looked: `IPublishTarget` already generalises publishing across networks, `Folder` + `Draft.FolderId` already model grouping without foreign keys, `Draft.IsTemplate` is already the precedent for expressing a document *kind* as a column, and `CEDAR_DATA_DIR` already makes running a local server a configuration change rather than a port. What is genuinely new is five entities, two columns and an Electron shell — not a second application.

**Seven decisions (ADR-101…107).** The module is endpoints and screens behind a flag, over one shared schema — a second `DbContext` would mean two `Database.Migrate()` calls against one SQLite file, and the boundary would fall exactly across the links the module exists for. Document type is a column on `Draft`, not a new entity, because `Draft` is really "a TipTap document with autosave, revision history, translations, tags and a folder" and a parallel entity would duplicate the most safety-critical code in the project. "A project always has a document" is a rule of the create endpoint, not a schema constraint — as a constraint it would be violated by its own first `INSERT`. A task is its own entity even though a document type would have been cheaper, because "what's overdue" must be a query rather than a scan of parsed JSON. The asset index stores paths and never bytes, which is the whole reason the desktop app is a prerequisite rather than a preference. And the desktop does not sync in v1, said out loud, because merging offline edits to a TipTap document is either a CRDT or silent data loss, and this project already has one text-loss incident behind it.

**One finding that costs a line of code.** The server's listening address is a literal — `app.Run(Consts.URLs.Localhost)`, i.e. `http://localhost:8080` — which means `ASPNETCORE_URLS` cannot override it and a desktop shell cannot ask the OS for a free port. That single line becoming configurable is the only server change the desktop needs, and it was found by reading the code rather than by assuming, which is exactly what `docs/DOCS-FLOW.md`'s "the code is the arbiter" rule is for.

**`Q-1` is closed** — the audience question, open since 30.07 with four candidate focuses and a fifth whose wording had been lost. There is one audience now, and it is the one whose needs get verified by doing the work instead of by guessing.

New: `docs/INDIEDEV.md` (scope, data model, MUST/MIGHT), `docs/DESKTOP.md` (Electron over the existing server, two run modes, risks), `docs/indiedev-design-prompt.md` (a design brief carrying the token set verbatim). Updated: `PRODUCT.md`, `PRD.md` — whose "RU primary + EN translation" section had been stale for two weeks and is now six languages with a per-draft primary — `ARCHITECTURE.md`, `ROADMAP.md` (Phase 13), `BACKLOG.md` (`T-120…T-138`, `Q-17`), `DOCS-FLOW.md`, `CLAUDE.md`. `dotnet test` 617/617: the branch has not drifted.

## 2026-08-09 — one post, several destinations (T-116…T-119, ADR-098/099/100, v0.9.40)

Four requests from Marty, and three of them are the same shape: the export window still assumed one post goes to one place.

**A Telegram channel per version (ADR-098).** Marty created an English copy of the main channel and immediately hit the wall: the window could tick two versions but held one chat id, so RU and EN went to the same channel and the translation needed a second trip through the whole window. The Telegram panel now shows a row per ticked version, each with its own channel; with one version it looks exactly as it did. Publish stays disabled while any ticked version has no channel — "RU picked, EN not" is an incomplete request, not one with a default. Two follow-on fixes fell out of it: the post link is built from the channel that actually produced it, and the "you are about to overwrite a live post" check (ADR-065) now asks about each version's own channel instead of asking the EN question about the RU channel. The mapping is remembered in the browser, because sending EN to the EN channel is a habit, not a property of the draft.

**Scheduling became its own step, for every network (ADR-099).** It had been living inside the Telegram panel, which was true while Telegram was the only schedulable network. `ScheduledPost` now carries a `TargetId` (`PublishTarget`, ADR-078) and a `Network`; rows written before the column still publish through their chat id, which is all they have. The due job publishes through `PublishToTargetAsync` rather than the publish queue — nothing is waiting on an HTTP request here, and a direct call keeps the row's `Sent`/`Failed` the network's real answer instead of "handed to a queue". **Threads are not schedulable, and the window now says so**: the toggle used to stay ticked and be silently dropped, since the scheduled path never had thread support at all. The blog is not a publish target, so a ticked blog publishes now while the networks wait — also said out loud rather than left to be discovered.

**X and Bluesky pick their own versions (ADR-100).** Ticking two versions meant two tweets from one account and two credits. The panel now carries its own language pills — a subset of the window's, all of them by default, so nothing changed for anyone who wants both. The X credit estimate follows the network's own set, which is the same situation in which it used to be wrong.

**An RSS button in the blog header.** The feed has existed since ADR-024 and was discoverable only by a reader who already knew to look for `<link rel="alternate">`. Styled secondary next to "Open in Telegram" — subscribing is an offer, not the header's main action.

`dotnet test` 617/617, `ng build` warning-free, and the blog header plus `/rss.xml` verified against a local run (bot disabled, scratch database) rather than by reading the template.

## 2026-08-08 — who is reading (T-115, ADR-097, v0.9.39)

Marty's request: views by country, by language, on the stats page. The blocker was that nothing had ever been recorded — a view was one increment of `Draft.ViewCount` with no dimension attached to it, so no query could have answered this.

**`BlogViewGeoDaily`** is a daily rollup, one row per (owner, UTC day, country, language), not a log of visits: the page only ever asks "how many", so a row per reader would buy nothing except a trail. Country comes from Cloudflare's `CF-IPCountry` (the tunnel is the only way in), language from the top primary subtag of `Accept-Language` — the reader's own language, which is what answers "is this worth translating", rather than which version was served, which would just re-report the post's primary language. Both normalize through `CedarClerk.Core.ReaderGeo` (19 tests): anything that isn't a well-formed code — a missing header on a local run, Cloudflare's own `XX`, a Tor exit's `T1`, junk — lands in one honest "unknown" bucket instead of minting rows. The upsert is by hand because EF has no `ON CONFLICT`; two first views of a day race into the insert, and the unique index makes the loser bump the winner's row rather than drop the view.

**On the page**, two cards under the metric charts on the Blog tab: bars scaled to the leader, the count, and the share of the period. Country names and language names come from `Intl.DisplayNames` in the UI language, flags from the alpha-2 code itself — no icon set, no name table to maintain. The long tail folds after eight rows. Telegram tabs show nothing here, because the Bot API reports no geography and an empty card would be a promise the data can't keep.

**History starts today** — same as `DraftStatSnapshot` and for the same reason, and the empty state says so instead of showing zeroes.

`dotnet test` 617/617, `ng build` warning-free, smoke **53/53** — one new, and it reads the whole path rather than the unit: a blog page fetched with `CF-IPCountry: DE` has to become a "Germany" row on the stats tab, which is the only assertion that proves the header is being read at all.

## 2026-08-07 — where you connect, and where you publish (T-113/T-114, ADR-095/096, v0.9.38)

Two of Marty's observations, and they turn out to be one problem: the export window had quietly become the place where accounts are set up, and the place where posts are sent, at the same time.

**Connections moved to Settings → Integrations (ADR-095).** The panel existed but only knew the Telegram account, the bot and a channel count with "manage in editor →". It now owns all three connect flows: Telegram channels (the chats the bot is already in, each with Connect; connected ones with Disconnect; `@name`/id by hand behind a disclosure; a Refresh that re-reads the `my_chat_member` cache), Bluesky (handle + app password, verified before it is stored), X (one button out to x.com and back), and a named strip of the networks that are planned rather than hidden. X's callback now returns to `/settings?tab=account&x=connected` and says so — it used to redirect to `/` with a parameter no screen read, so a completed OAuth round trip looked like nothing had happened. The N4 rule this retires ("connect where you publish") was written when there was one network and connecting meant pasting a chat id; with an OAuth redirect the page leaves entirely, and there is no modal to come back to.

**The export window reads as three questions (ADR-096).** Version → where → settings for each destination. The language moved out of the Telegram section, where it had been living, to the top where it belongs to the post; a one-line note names the languages with no version yet instead of listing nine "no translation" chips. Destinations are cards, and an unconnected network keeps its card — un-tickable, pointing at Settings — rather than vanishing, which is how X and Bluesky went unnoticed. Step 3 shows a panel per ticked destination and nothing else. Invitations and the watermark became their own band, visible whenever the post is private: they describe the private page, not the act of publishing, and were previously two clicks inside a destination.

**X and Bluesky: two modes, not a checkbox.** "Announcement + link" (ADR-077) and "the whole post as a thread" (ADR-094) are different publications, so the panel asks which and then shows only that mode's fields — the old checkbox sat beside a text field the thread never reads. The override text is now per-language with its own tabs: one field was writing the same text into every ticked version. Both networks lost their private Publish buttons; the one button at the bottom fires all four, and the publish checklist already had a row per network per language.

Fixed on the way: the pre-flight "you are about to rewrite a live post" confirmation only ever covered blog and Telegram, so republishing to X would 409 mid-run and re-run the destinations that had already gone out. `/api/posts/update-preview` learned the network kinds its revisions were already keyed by, and the confirmation is deduped to one row per language instead of one per destination-and-language.

`dotnet test` 598/598, frontend 11/11, `ng build` warning-free, smoke **52/52** — one new: a stubbed connected network proves picking the thread mode removes the text field and shows the part count. Not verified live: an actual post to X or Bluesky through the new window (no connected account outside production).

## 2026-08-05 (later) — microblog threads for X and Bluesky (T-111, ADR-094, v0.9.37)

The whole document as a reply chain, not just a teaser. `MicroThreadSplitter` (Core, 8 tests) packs paragraphs into parts measured the way each network measures — X's weighted units, Bluesky's graphemes — breaking an oversized paragraph on sentences, then words; every part is numbered "N/M" inside a reserved budget and the blog link rides the last part (spilling into its own closing part when full). The queue and the targets recompute the same plan from the same inputs (T-106's principle); X replies by `in_reply_to_tweet_id`, Bluesky by a reply record whose root+parent each need uri **and** cid — so a Bluesky job's `RemoteId` now stores `uri|cid`. An X thread costs one credit per part, checked up front for all the parts still ahead so a thread never stops halfway for money; the export modal shows "N messages · N credits" before the send.

Fixed on the way, because threads forced it into the light: `PublishResult.MessageId` is an `int`, so an X tweet id (int64) and a Bluesky at:// URI both parsed to null — `job.RemoteId` was empty for every non-Telegram publish, and `job.PublicUrl` was **never assigned at all**, which is why "open the post" никогда не появлялась. The receipt's string id and URL now flow through. `dotnet test` 598/598, smoke 51/51.

## 2026-08-05 — the credit wallet (T-109, ADR-092)

X posting will be paid by the author, so the wallet came before the connector. Marty's three decisions (04.08): one **universal** credit wallet rather than an X-only counter, ~2× markup (1 credit = 1 X post = $0.40; packs 10/$4, 50/$18, 100/$30, Stars 200/900/1500 ⭐), and no credits bundled into plans.

Backend: `CreditEntry` is a ledger — the balance is `SUM(Delta)`, so every number has an audit trail — and both directions are idempotent by `(Reason, Ref)`: a replayed Stripe webhook or a retried publish job cannot double-move the wallet (that's the unique index, not application luck). A charge refuses rather than overdrawing. Purchases ride the existing plan flows: Stripe one-time Checkout with a `credits_pack` metadata branch in the webhook, Stars invoice with a `credits-{pack}:{user}` payload branch in the bot. 6 tests; `dotnet test` 577/577.

Frontend: a Credits section on Settings → Account between Subscription and Integrations — balance banner, three pack cards reusing the plan-card/pay-method markup (Stripe redirect / Stars invoice, same per-method gating and tooltips), inline error, and the last 50 ledger rows with localized reasons (RU plural forms via the existing `plural()` helper). `ng build` warning-free, frontend 11/11, smoke 51/51.

Next: T-109 live-verify (Stripe test mode), then T-110 — the X connector itself, blocked on Marty registering the X developer app.

### The X connector backend (T-110, ADR-093)

`XPublishTarget` — the third `IPublishTarget`. v1 posts text plus the blog link (media and threads are follow-up rows; the post is a teaser by design, ADR-077). The OAuth 2.0 PKCE connect flow lives in `PublishEndpoints` (`POST /api/publish/x/connect` → authorize URL; `GET /api/targets/x/callback` — the exact URI registered in the developer portal); state and verifier wait in process memory for ten minutes. The rotation rule ADR-079 dodged for Bluesky is faced here: X kills the old refresh token on every refresh, so the new pair is **saved to the database before the access token is first used** — a failed save aborts the publish. One credit is checked before the send (402, which the queue never retries) and charged after success, anchored to the created post id.

The build surfaced a latent Bluesky bug: `PublishValidator` blocked "too long" on the whole document even for networks that post a derived teaser — every real document queued to Bluesky would have 422'd. `PublishCapabilities.DerivesShortPost` (true for Bluesky and X) downgrades `too-long`/`too-many-media` to informational. `dotnet test` 590/590.

Still to do on T-110: the connect button + X row in the export modal (frontend), and a live end-to-end post once Marty finishes the portal's User authentication settings (OAuth 2.0 Client ID/Secret → Pi drop-in).

### …and its frontend (v0.9.36)

The X section in the export modal, mirroring Bluesky's: the override textarea with a live weighted counter (a TS mirror of `XPostBuilder`'s rules — the two counters must agree or the field would refuse posts the server sends), publish button, post link on success. Connecting is one button — the browser goes to x.com and comes back through the server's callback. Under the publish button: "1 credit per post · balance: N" (ADR-092). Marty put the OAuth 2.0 client keys into the Pi drop-in (key names verified, values untouched); "Twitter / X" left the planned-platforms mock list. `ng build` warning-free, frontend 11/11, smoke 51/51.

### X errors fail fast instead of spinning (first live publish, 05.08)

Marty's first real X publish surfaced three production configuration faults (a `Enritonment=` typo hiding the client secret from systemd, an unquoted `FromAddress` that systemd cut at the first space — the 422 behind "no confirmation email" — and the email-confirm card nested inside the anchor-chips nav, stretching them into tall pills) and one mapping bug worth recording: X answered **"credits depleted"** (the developer app's pay-per-use balance, empty until topped up) and the catch-all mapped it to 502 — retryable — so the queue re-billed the refusal three times over ~75 seconds while the author watched a spinner. Now: "credits depleted"/usage-cap answers become a non-retryable 402 with a message that says explicitly it is the X Developer Portal balance, *not* the Cedar wallet; every other 4xx except a plain 429 is non-retryable too (a 4xx is X's verdict on the request, and each retry is a billable call). The job fails within seconds and the export modal shows X's actual reason.

Same contract as `BlueskyPostBuilder` (override wins, teaser falls back, the blog link survives body truncation — ADR-077), but X's counting rules, which are the whole reason this is its own class: 280 **weighted** units (twitter-text config v3 ranges — Latin/Cyrillic weigh 1, CJK 2), an emoji ZWJ sequence is one element of 2 however many codepoints compose it, **every URL is exactly 23** after the t.co rewrite, and the count runs on the NFC form. The paragraph extraction both builders share moved to `CedarPlainText` instead of being copied. 11 tests pinning each rule separately (a Russian post at exactly 280, CJK at 140/141, a 130-character URL costing 23); `dotnet test` 588/588.

## 2026-08-04 — production down: disk full, and the log flood that caused it

The deploy of the 01–02.08 work (v0.9.35) failed its health check because the Pi's root filesystem was at 100% — SQLite answered `disk I/O error` on the first query and the service crash-looped. The culprit: the Pi lost connectivity to `api.telegram.org` around 02.08, and the bot's polling error handler logged a full stack trace per retry with no delay between retries — `syslog` + `daemon.log` grew to 5.5 GB *each* in two days. Recovery: truncated both logs, `apt clean`, journal vacuum (13 GB freed), service restarted — v0.9.35 healthy, bot reconnected on its own, app/blog/RSS all 200.

The root-cause fix in `TelegramBotService.OnError`: consecutive polling errors now back off exponentially (2s → 60s cap; the Telegram.Bot 22.10.2 polling loop awaits the handler before retrying, verified against its source) and the full stack trace is logged once per streak, then one summary line per 100 errors. A two-minute quiet gap resets the streak. Worst case is now ~1.4k retries/day and a handful of log lines instead of millions.

### The cache-buster broke YouTube embeds (ADR-091)

The first post after recovery — text plus one YouTube embed, no local media — failed with `failed to get HTTP URL content`. `img.youtube.com` serves the thumbnail at 200 but answers **404 the moment ADR-087's `?v=<stamp>` is appended**: Google's thumbnail host refuses unknown query strings instead of ignoring them. The stamp exists to bust Telegram's negative cache of *our* origin, so it now applies only to URLs that point into our own `/media/` (the same `TryLocalMediaFileName` test that gates upload eligibility); external URLs go out byte-for-byte as rendered. `dotnet test` 571/571.

## 2026-08-01 (late) — T-101 closed end to end

The night pass put the blog on the generated tokens; this closes the tail. The single-file HTML export (`DraftEndpoints.StaticExportHtml`) was the last hand-copied palette — still on the pre-ADR-074 values — and now inlines `DesignTokens.Declarations()` like the blog and the landing page, staying self-contained without owning its colours. And ADR-074's "`--t3` is not a text colour" sweep, which had stopped at the app's edge, now covers the public surfaces: 15 blog declarations and 2 export ones moved to `--t2`; the spoiler background and a hover border stay on `--t3` — decoration, which is the token's contract. `dotnet test` **571/571**. Recorded as ADR-090; the stale "still open" note in ADR-074 and the T-101 row in `TASKS.md` corrected with it.

## 2026-08-01 (evening) — the first real thread run, and what it taught

**v0.9.33, then v0.9.34.** The design doc's first threaded publish sent parts 1–2, collapsed part 1 behind Telegram's "Show more", failed part 3, held back 4–12 — and reported the *held-back* message as the error. One run, four fixes — and the re-run after them taught the fifth. `dotnet test` **571/571**.

### Flags in the emoji picker

A Flags group (50 country and generic flags, 🇺🇦/🇬🇪 up front) and a Flag-sequences group for the flags Unicode never encoded: ⚪️🔴⚪️ and 🤍❤️🤍 for the white-red-white, 🤍💙❤️ for the 1991–1993 Russian tricolour with its lighter blue, 💙💛 — each a single button inserting a colour run, plain text end to end (editor → Telegram → blog). No codepoint exists for those flags and how 🇷🇺 renders is the reader's platform font's decision, so the sequences are the honest representation — and, unlike regional-indicator flags (which Windows still draws as letter pairs, the DB3.1 fact), they render everywhere.

### A form submission can be deleted

The owner's own test answers (and any other noise) sat in the registration list and skewed the distribution charts with no way out. Every submission now has a trash button (and a Delete in its detail modal) behind the same confirm pattern as deleting a post or a preset. It is a hard delete, deliberately: the row carries that reader's access grant (ADR-084), so removing a test account also closes the door it opened — and the confirm dialog says so out loud. The charts recompute from the shortened list by themselves.

### …and the upload goes through a storage chat (ADR-089, v0.9.35)

ADR-088's first real send answered `can't parse InputRichBlock: media not found`, and reflection over Telegram.Bot 22.10.2 explained it: `SendRichMessageRequest` is JSON-only (`RequestBase`), so an `InputFileStream` inside Blocks serialises to an `attach://…` reference whose bytes never leave the machine — while `SendPhoto`/`SendMediaGroup` are the multipart-capable ones (`FileRequestBase`). So each local file is now pre-uploaded once through `SendPhoto`/`SendVideo`/`SendAudio` to the owner's own bot chat (silent, buffer message deleted on capture), the bot-scoped `file_id` is cached on the Asset — `TelegramFileId`, a column the initial schema carried unused, plus a new `TelegramFileIdSourcePath` — and Blocks go out by file_id, which is pure JSON. A 23-part thread uploads every file once; a retry re-sends JSON only. No linked Telegram or a failed upload falls back per-file to the stamped URL.

### Telegram gets media as uploaded bytes, not URLs (ADR-088, v0.9.34)

The re-run on v0.9.33 failed part 3 again — `failed to get HTTP URL content`, on the first part with nine images. Not flood control (that answers 429, which the queue retries): Telegram's *fetcher* choking on nine concurrent downloads from the Pi, made worse by the fresh cache-buster sending every fetch past Cloudflare's cache to a residential upload link. The fix removes the round-trip instead of tuning it: media that lives in the server's own media directory is now **uploaded as multipart bytes** (`InputFileStream`) — nothing to fetch, nothing to time out, nothing to cache-poison, and the upload limits are better than the fetch limits anyway. External media (YouTube thumbnails) keeps the URL path and the ADR-087 stamp. `Cedar:Telegram:MediaDelivery=url` in the systemd drop-in is the no-redeploy escape hatch; it is deliberately not an author-facing setting — "which bytes should Telegram receive" has one correct answer.

### A thread part gets its own character budget (ADR-086)

The splitter budgeted parts against `MaxPostChars = 32,768` — the most a message *can* carry, not the most a subscriber will read as one message. In a media-heavy document every cut came from the 10-media limit and the character rule never fired: part 1 went out at 6,412 characters and Telegram collapsed it. `PublishCapabilities.ThreadPartCharacters` (Telegram: **3,000** — a 1,787-char part rendered fully, 6,412 collapsed, so the threshold sits between) now drives the split. The same doc goes from 12 parts to 23, nearly all cut at headings, none above 2,967 characters.

### Telegram media URLs carry a per-send cache-buster (ADR-087)

Part 3 failed with `wrong type of the web page content` while all ten of its images served `200 image/*` through Cloudflare. Telegram had cached the *failed* fetches from the morning's whole-document attempts and kept refusing those exact URLs — the second such incident (first: 16.07, then judged not worth a code change; a thread raising the cost to ten held-back messages changed the verdict). Every media URL sent to Telegram now gets a per-send `?v=` stamp, so a cached failure cannot outlive its incident. The stored document, blog and `.cedar` export never see it.

### The publish checklist modal

Publishing now opens a checklist that runs like a test suite: one row per phase (save → blog → each Telegram language), live statuses, a thread unfolding into numbered part chips that fill green as messages land, links on the successful rows, and the error pinned to the step that broke. Critically it is the **first** failed part's error — the root cause — where the export window used to overwrite it with each later "held back" message and report part 12 instead of part 3.

### Two queue-watching fixes the thread exposed

`GET /api/publish/jobs` returned the 20 most recent rows — fewer than a 23-part thread, so the client could wait forever on jobs it would never see finish; now 100, ordered stably within a thread. And the export flow's per-job loop no longer clobbers `exportError` with the last failure it meets.

## 2026-08-01 — threads, portable access, and word forms

**v0.9.29 deployed** (the night pass). Everything below is committed on top and not deployed. `dotnet test` **560/560**, smoke **47/47**.

### A long document can go out as a thread (T-106, ADR-083)

The design doc is 44,474 characters and 105 media against limits of 32,768 and ~10. It is not a Telegram post; now it can be eight of them.

**Splitting is offered, never applied.** The switch appears only when the post genuinely does not fit, is off by default, and shows the parts before anything is sent — number, what each opens with, its size, its media. Turning one post into eight messages is a loud act in someone's channel, and the difference between a tool and a liability is whether the author saw it coming.

**Where the cuts land is the whole feature.** A part is closed at the next heading once it is 60% full, rather than filled to the brim and cut mid-sentence: a message that ends where a section ends reads like a chapter, one that ends mid-section reads like a transmission error. Blocks are never split. Both limits count. Each part replies to the previous one so Telegram renders a thread, **only the first part notifies**, and the signature, cross-link and hashtags go on the last part alone.

**One job per part**, extending the queue: a thread that fails on part four is resumable at part four, because retrying the publication would send parts one to three again and a channel cannot un-see them. A part runs only after its predecessor succeeded; if an earlier part failed, the rest are held back rather than leaving parts 1 and 3 of a document in a channel.

That work exposed a race in the queue shipped hours earlier: `Kick` is fire-and-forget and competed with the sweep's own loop, so part 3 could be decided while part 2 was still running. The sweep is sequential now and does not kick successors.

### A reader's access travels in the link (T-064/T-023, ADR-084)

The reported incident: a friend filled in a private post's form in Telegram's in-app browser, opened the post in Chrome, and was asked to register again. Reading the code for it found the second half: the access cookie's value was the string `"1"` — it proved nothing, and anyone who knew a draft's id could write it by hand.

Both are one mistake seen twice. The cookie is signed now, and a successful registration mints a per-reader token that comes back in the redirect, so the link works in any browser. **Cookies issued before today stop working** — a reader already through the gate meets it once more. Keeping them working would have meant keeping the forgery open.

### Word forms for the glossary (T-040)

Russian inflects, so a term entered as "рендерер" never matched "рендерера". A button proposes the forms into the alias field — **suggestions, not silent generation**: Russian declension has more exceptions than rules, and a wrong form would mark the wrong word in someone's post with no way to notice. Fleeting vowels are handled (уровень → уровня, not уровеня), and anything the rule is not confident about suggests nothing rather than something wrong.

## 2026-08-01 (night) — autonomous pass: the backlog went 50 → 37

No deploy in any of this; every item below is committed and green. `dotnet test` **536/536**, smoke **44/44**.

### The blog stopped being a separate product

Three defects on it, all found by reading rather than by clicking. **Dates disagreed with each other on the same page** (T-094): a hardcoded Russian month heading above an English card date, with neither following the language the reader asked for. `BlogDateFormatter` (Core, 6 tests) carries explicit month tables per language — explicit because the Pi runs without ICU data, which is why this codebase formats with `InvariantCulture` and how the two formats came to disagree in the first place. Russian and Ukrainian get the genitive after a day number, because "17 Август" is the kind of wrong that makes a page look machine-made.

**The footer sat wherever the content ended** (T-099) — on a two-post index that is the middle of the screen. **The blog's palette was its own copy** (T-101), which is why the contrast pass fixed the app and left the blog a shade behind, still on the pre-AA values. It is generated from the app's stylesheet now (`DesignTokens.generated.cs`, `npm run tokens:generate`), and a drift test fails the build if the two part ways again. Four colour literals stay by name and by reason — a generated avatar colour, a code block's own dark scheme, white on an accent fill.

### The server speaks Russian now (T-050, closed)

The ~60 inline English literals in the endpoint files are in `ErrorMessages`, which reads the reader's language. **The guard is the point**: a test fails the build when a 61st is written, and it was verified to actually go red by writing one. Server messages were the biggest remaining hole in a UI that has been fully translated since 27.07.

### "Ctrl+B doesn't always fire" was never intermittent (T-100)

It fires exactly as often as the keyboard is in a Latin layout. ProseMirror matches shortcuts by `event.key`, and on a Cyrillic layout Ctrl+B arrives as `Ctrl+и` — bound to nothing. For someone who writes in Russian that is most of the time, which is what "не всегда" was describing. A small extension binds the *physical* key (`event.code`) and stands aside when the layout already produced the right letter, so a Latin layout cannot toggle bold twice. Both directions are covered by tests, and the Cyrillic one was checked red first.

### Also

- **Email confirmation** (T-002) — sent on registration, with a reminder in Settings and a resend. Deliberately not a gate: blocking unconfirmed accounts would lock out every account that predates this, and the gate that matters (public registration) is not open yet.
- **A reply to whoever fills in a form** (T-033) — per language, and empty means no mail: an owner who has not written one has not agreed to write to their readers.
- **Reactions and comments can be switched off per post** (T-039) — two flags, not one, because a post can reasonably take likes but not a discussion. Enforced server-side, not by hiding buttons.
- **File exports say they are working** (T-042) — the plain `<a download>` was silent for as long as the server took to package a hundred images.

## 2026-08-01 — Second review pass: a CSS regression, a translate button that filled nothing, and terms from the editor

**The card styles I shipped an hour earlier were not applied at all.** The rule that replaced `.post-row` landed *inside* an unclosed `@media (pointer: coarse)` block — `.post-row` had been one member of a two-selector list there, so replacing it swallowed the media query's close and nested every card rule inside it. The Posts and Forms lists rendered as bare buttons. Repaired by lifting the rules back to the top level and restoring the media query; the duplicate `.post-search` rule left over from FI3.10 went with it, since the sticky version is the one that survives.

**Auto-translate ran and then appeared to do nothing.** It called `setSignatureLanguage(source)` to refresh the fields — and that method returns immediately when the language has not changed, which it never had. The request succeeded, the maps filled, the inputs kept showing what was there before. It now reloads both draft maps directly.

It also had no progress indication, unlike every other AI operation in the app. It now uses the same asymptotic pseudo-progress (ADR-038) with an elapsed-seconds counter: neither provider streams, so there is nothing real to report, but a bar that keeps moving is the honest way to say "still working" without claiming to know how far along it is.

**A per-language translate button**, beside the translate-everything one, for the case where one language came back wrong and only it needs redoing. It always translates *from* the primary language into the selected one, and is hidden while the primary language is the one on screen — without a fixed source the button would have to translate a language into itself, and choosing one silently is how you get a translation nobody ordered.

**A glossary term can be created from the editor.** Right-click over a selection offers it; with nothing selected the browser's own menu is left alone, because replacing spellcheck, copy and paste with one disabled item is a worse trade. The form is `app-glossary-term-form`, now shared with `/glossary` rather than copied from it — "exactly the menu in Glossary" is only true if there is one of it. The new term takes the *content* language being written, not the UI language and not the draft's primary one, because that is what the blog will scan it against.

## 2026-08-01 — Marty's review pass: tables could never publish, icons were black, and the Posts Manager became cards

### The bug: a post with a table has never once published

`Publish failed: JsonException: Can't serialize value 0 for enum RichBlockTableCellAlign`. `RichBlockTableCell.Align` and `.Valign` are non-nullable enums whose members start at **1**, and the mapping never set them — so the wire value was 0 and the client refused to send before the request left the machine. Any post containing a table failed. TASKS.md had listed tables as "implemented against the documented type shapes but not yet exercised with a real post" since 16.07; this is what that meant.

The fix is two lines. The part worth keeping is the test: **every block type is now serialized with the client's own `JsonBotAPI.Options`** — the renderers had unit tests for the Core tree (renderers.md invariant 2) and nothing at all for the step after it, the mapping onto Telegram's wire types. 19 new tests, and they would have caught this the day the renderer was written.

### Icons were black, and it was never a theme bug

Phosphor's assets carry `fill="currentColor"` on their own `<svg>` element, and the generator strips that wrapper to keep only the paths. So `app-icon`'s `<svg>` had no fill and every icon in the app painted in the SVG default — black. On the light theme that reads as "a bit heavy"; on dark it is nearly invisible. It was never inherited from anywhere and never a `--text` problem: it was simply lost in generation.

Sizes went up a step each (`--icon-sm` 15→17px and the rest with it) and so did every semantic type role (`--fs-ui` 13→14, `--fs-body` 14→15, and so on). Deliberately the roles, not the numbers: the scale is unchanged, and after T-077 every component reaches for a role, so it is one edit. Product-wide rather than editor-only — ADR-071 principle 2 allows exactly one type scale, and the dense screens stay tighter through `--dens-fs`, which is what density is for.

### Glossary

- **The edit form now opens where the term is.** It used to live only at the top of the page, so editing the last term of a long glossary meant scrolling up to fields with no visible connection to the row they belonged to. One template, rendered in two places.
- **Click a term to see its real tooltip**, with a language switcher across the translation group. That group needed a link that did not exist: translated terms are separate rows keyed by translated text (ADR-061) and nothing recorded where they came from, so `GlossaryTerm.SourceTermId` now holds the group root. Rows translated before today stay unlinked — guessing by text would pair the wrong terms.
- **"Case-sensitive" per term.** Off by default, because a term at the start of a sentence is the same term; on where the casing *is* the meaning — "IT" the industry against "it" the pronoun. Applies to the term and its aliases alike.

### Signature and cross-links translate themselves

The last per-language texts anyone still had to write by hand in every language — the post signature and both cross-link lines. One press fills every other content language from the one selected, with the same bargain the glossary's translate-all makes: the narrow `ITextsTranslationProvider` capability, the plan gate before the provider call, one AI call for the batch, and a blank result left alone rather than written over the existing text.

### Posts Manager

- **Each post is a card**: indicators and title on one line with a single publish state on the right, the languages it exists in on the next. Exactly one state chip per post now — archived beats live, live beats unpublished — instead of a wrapping line where a language chip and a publish state sat as equals. The card also fixed a quiet omission: `DraftMeta.languages` holds only *translations*, so a post written in one language used to show no language at all.
- **The list scrolls in its own box** with the search field pinned, instead of growing the page until the detail pane started below the fold.
- **The details pane collapses into groups** — Post, Placement, Where it went, Growth, Reactions, Private access, Submissions. Native `<details>`, not a hand-rolled accordion: it collapses without JavaScript, is keyboard- and screen-reader-correct for free, and its `<summary>` already counts as a control for the 44px touch rule. Which groups are open is remembered per browser.
- **The forms editor's ✕ buttons had no styling at all** — `.mini-remove` is declared in `editor.component.css` and Angular's view encapsulation keeps it there, so every one of them rendered as a bare browser button.
- **Per-post growth chart** (views / likes / comments). This needed a data layer that did not exist: `DraftStatSnapshot`, written nightly by the existing snapshot job. **History starts today** — the counters are running totals with no timestamps in them, so there is nothing to backfill from, and a post's chart stays empty until the job has run twice. The empty state says "not measured yet" rather than drawing a flat line that would read as "no growth" (T-105).

Removed: the Settings → Account card whose only content was "Appearance moved to the editor". A settings card that exists to say a setting is elsewhere is a dead end that has to be read every time to be dismissed.

**Verified**: `dotnet test` **484/484**, smoke **42/42**, `ng build` clean.

## 2026-08-01 — Phase 12 starts: what a publish target is, and Telegram becomes one

Marty answered the two questions that had Phase 12 blocked (ADR-077): **Bluesky first** — the only candidate needing neither app review nor payment, so the abstraction can be proved without anyone else's approval in the way — and **a cross-post is a standalone post with a manual per-target override**, not a teaser with a link. The second is the more expensive answer and the right one: ADR-021 already decided every destination is co-equal, and a network that only ever receives "read this elsewhere" is a billboard, not a destination. It also reshapes T-087 from "truncation rules" into per-target text storage plus a target tab in the editor.

### T-083 — the abstraction, decided before it was written (ADR-078)

A publish target is a **(tenant, network, remote account)** triple that owns credentials and can name what it created. Its obligations are three: name its network, describe its limits **as data** (an editor cannot display a method call, and the whole point of the capability matrix is warning the author before the send), and publish returning a receipt.

What it is explicitly *not* asked to do is the more useful half, because each one is a plausible extension that would have leaked Telegram's model into every other network: it does not fetch statistics (member counts come from the bot, and most networks have no equivalent), it does not delete or edit (nothing in the product offers it, so `UnpublishAsync` would have been designed around `deleteMessage` and hope), and it does not run its own connect flow (bot-membership discovery, a handle plus an app password, and a review-gated OAuth have nothing in common).

**The blog is deliberately not a publish target**: no credentials, no remote account, one destination per draft, and its publish action is a flag on a row this server already owns. Modelling it as one would mean an implementation whose credential is empty and whose "send" is a local UPDATE — uniformity bought by making the abstraction describe something it doesn't.

### T-084 — and the first third-party credentials in the product

`PublishCapabilities`/`PublishNetworks` in Core, `IPublishTarget`/`PublishRequest`/`PublishOutcome` in `Server/Publishing/`, the `PublishTarget` entity, and `PublishTargetSecrets`.

`PublishOutcome` is deliberately the same shape as the `PublishResult` it replaces, so the refactor that followed stayed a move rather than a redesign. Failure is returned, not thrown: both callers (an endpoint and a Quartz job) have to turn it into a response or a stored error, and a job that dies on an unhandled exception loses the reason.

The credentials are the part worth being careful about — a tenant's own social account, sitting in a database that is copied to a microSD card every night. They are encrypted with the DataProtection key ring that T-074 had already moved under `CEDAR_DATA_DIR` and into the backup. **The consequence is now written down rather than discovered later**: the key ring and `cedar.db` are a pair, so a database restored beside a lost ring leaves credentials unreadable — which is why `TryUnprotect` returns null and the owner is asked to reconnect, instead of a publish job crashing. Twelve tests, including that a different key ring cannot read the payload and that a flipped character reads as null.

### T-085 — Telegram moved onto it, before any Bluesky code exists

The order is the whole argument (ADR-070), and the code showed why: `PublishAsync` wrote `Draft.LastTelegramChatId/MessageId/Username` and a `ChannelPost` row, so an abstraction extracted after a second connector would have been shaped around those three columns.

`TelegramPublishTarget` now holds the bot check, the media compression, the Blocks renderer, the entire `RichBlock`→wire mapping (moved out of `PostEndpoints` — "the one place that knows about Telegram.Bot" is the target, not an endpoint file), the send with both of its catch blocks, and the Telegram-shaped bookkeeping. What remains in `PostEndpoints.PublishAsync` mentions no network at all.

`Channel` is **projected** into `PublishTarget`, not replaced: `ChannelPost`, `ChannelStatSnapshot` and `BotKnownChat` all key off it and none generalise. The projection is idempotent C# with 7 tests — including the sequence that would otherwise hit the unique index, disconnect and reconnect the same channel — rather than a one-shot `INSERT…SELECT` in the migration, because a data migration that runs against the production database deserves to be runnable twice and provable in a test.

**No behaviour change** was the requirement: same order of operations, same error strings, same status codes, same rows written. `dotnet test` **461/461**, smoke **42/42**. Honest limit on that claim: the suite covers the refusal path (publishing to a channel the account does not own still 403s with the same wording); the successful send has no automated coverage without a bot token and wants a real post to `@testingandfun`.

One inherited oddity was preserved rather than fixed, and recorded as **T-104**: the revision written after a send holds the document with media paths already rewritten to their Telegram-safe derivatives. It does not affect the publish guard, but it does make the next publish's diff show every compressed image as changed. Fixing it is a behaviour change, which is exactly what this refactor promised not to be.

## 2026-08-01 — Phase 11: accessibility, the icon inventory, long words, and a bundle that is a third of what it was

**v0.9.21 went to production first** (health green, no migrations applied, no `warn:`/`fail:` in the startup log, bot running, `/` + `blog.mooexe.dev` + `/rss.xml` all 200) — that shipped the whole token migration and the Phosphor icons. Everything below is committed on top and **not deployed yet**.

### The initial bundle: 1.87 MB → 531 kB (T-092, ADR-076)

The two build warnings had been scenery for long enough that the backlog row asked for the threshold to become a decision rather than a number that gets raised whenever it breaks. Measuring first turned out to answer a different question: all thirteen page components were imported eagerly, so **TipTap, ProseMirror and KaTeX downloaded in full before `/drafts` — the landing screen — could paint**, on an app served entirely by one Raspberry Pi behind a tunnel.

Every route became `loadComponent`, with `withPreloading(PreloadAllModules)` so the split costs nothing on navigation: the chunks are fetched in the background once the first screen renders, and the editor's 996 kB is usually already in cache by the time anyone opens it. Transferred bytes on first load: **410 kB → 127 kB**. Only then were the budgets set against the new measurement — 650 kB warning / 800 kB error, 30/36 kB for component styles. **`ng build` is warning-free for the first time.**

### `--t3` is no longer a text colour (T-082, ADR-074)

The contrast pass started with a measurement — `tools/check-contrast.mjs`, which reads the tokens out of `styles.scss` and resolves the `color-mix()` derivations the way a browser does — and found **32 failing pairs** across both themes. One of them forced a decision rather than a fix: raising `--t3` to 4.5:1 lands it on `#676259` while `--t2` is `#6B655A`. **At AA this palette has room for two muted text tiers, not three.**

So `--t3` stopped being a text colour. Its job is now placeholder, disabled and decoration, where 3:1 applies, and the 91 declarations that used it to say something moved to `--t2`. The visible consequence, stated plainly: **meta text across the app is darker now** — timestamps, counts, column headers, hints. The type scale still carries the hierarchy; colour no longer carries it twice. It is one token away from being reverted.

`--t2`, `--accent`, `--danger`, `--ok` and `--warn` each moved one step towards black in the light theme — the smallest step that clears 4.5:1 on `--canvas`, which is the binding surface because contrast falls as the background darkens. Hue untouched. **`--border` is deliberately exempt**: it is a hairline between cards, never the only way to identify a control, and at 3:1 the whole warm-paper surface reads as a wireframe. The boundary that *is* an affordance got its own token, `--border-strong`.

Also: the app's **first global focus ring** (it had exactly two `:focus-visible` rules before, both on surfaces only a developer opens), and 44px touch targets keyed on `pointer: coarse` rather than viewport width — the device this broke on is an iPad in landscape, 1024px wide and entirely touch-driven. All of it is asserted by `e2e/12-a11y.spec.ts`, so the smoke suite went **37 → 42**.

### The icon inventory, generated rather than kept (T-080, ADR-075)

All 58 icon-only controls already had a tooltip and none had an `aria-label`; that sweep was mechanical. The interesting half is `/dev/icons`, whose data is **generated from the call sites** — a hand-kept inventory answers "which icon means what here" only until the next commit.

Its first run reported six meanings drawn with two icons each. **All six were false**: a busy button swaps its icon for `arrow-clockwise` with `class="spin"`, and the analyser was reading the spinner as if it meant what the button means. Teaching it that a spinner is a state dropped `arrow-clockwise` from 11 meanings to 3 and emptied the duplicate list entirely — the app is consistent here, and the tool that says so is only worth having because it was wrong first. What remains overloaded is `x` (12 labels), `trash` (5) and `plus` (5), the universal actions that legitimately repeat.

Two hardcoded English strings fell out on the way: the shared modal's close button — the last one in the app's chrome, and it appears in every modal — and the editor's AI menu.

### Long words (T-051, ADR-076)

A dev-only pseudo-locale (`?pseudo=1`, or a toggle on `/dev/styleguide`) inflates every UI string ~30% and welds a real German compound onto its longest word, wrapped in `⟦ ⟧` so no screenshot can be mistaken for a translation. Three defects on the first run, none visible in English or Russian and all the same family:

- **The page header could not shrink** (`flex: none` + `nowrap`), so every screen using it scrolled sideways and clipped its last button — the editor by 770px.
- **The `/drafts` column headers could not ellipsize**, because `text-overflow` does not apply to a flex container. LANGUAGES printed straight over FOLDER. (The same trap `/posts` hit with long titles a day earlier — worth remembering as a class, not an incident.)
- A toolbar group caption could widen its group and push the next group off the row.

The rule that came out of it: a label shrinks and ellipses, it never widens its container, and it keeps its `title` so the full text stays one hover away.

### Fixed on the way

A flaky smoke test: the UI-language test reloaded the page while the profile POST was still in flight, so `/api/auth/me` answered with the old language and `adoptProfileLanguage` put the UI back — which looks exactly like a persistence bug. It now waits for the write, not just for the UI.

**Verified**: `dotnet test` 442/442, smoke **42/42**, contrast 0 failing pairs in both themes, `ng build` warning-free.

## 2026-07-31 — Phase 11, T-079: the icon set is Phosphor, behind one component

**206 call sites, 80 icons, 15 TypeScript files — and `@lucide/angular` is gone from `package.json`.**

Everything the app draws now goes through `<app-icon name="…" size="…" weight="…">`. Size comes from the `--icon-*` tokens and never from a caller, weight is a prop, so "make the icons bolder" is one change instead of a fourth sweep through two hundred templates. The set is delivered as a **generated TypeScript constant** (`tools/generate-icons.mjs` → `icon-data.generated.ts`, 80 icons × regular and bold): the package ships raw `.svg` assets and Angular has no loader for those without extra build config, so generating the inner markup keeps the icons tree-shakeable, leaves the build configuration untouched, and makes the set a build-time dependency rather than something the browser fetches. `bypassSecurityTrustHtml` appears exactly once, on a build-time constant — Angular's HTML sanitizer drops SVG children, so there is no alternative that renders anything at all.

The Lucide→Phosphor name map is `tools/icon-map.json`, and **every one of its 80 entries was checked against the package's asset files before use** rather than guessed from memory. Two pairs collapsed: `Sigma`/`SigmaSquare` and `Sparkle`/`Sparkles` were the same idea under two names.

**Two migration scripts went wrong in ways worth recording**, because both were the same class of mistake — a regex that looked bounded and was not:
- `import\s*\{[\s\S]*?\}\s*from '@lucide/angular';` is lazy, but it still *starts* at the first `import {` in the file, so it swallowed every import statement above the Lucide one. Fourteen files lost their entire import section; the compiler caught it immediately, `git checkout` undid it, and `[^{}]*` — which cannot cross another import's braces — is what actually bounds the match to one statement.
- The follow-up check `from '.*icon\.component'` matched **`brand-icon.component`**, so `settings.component.ts` was judged to already have the import it was missing. One file, one build error, but the lesson is the same: a pattern that is merely plausible is not a check.

Also fixed by hand: the first script only matched `<svg lucideX …>` where the directive is the *first* attribute, missing the 20 `<svg modal-icon lucideX …>` tags and the inline templates that live in `.ts` files rather than `.html`.

`brand-icon.component` survives unchanged and its comment now says why: no general-purpose set carries brand marks — that was true of Lucide and is equally true of Phosphor. The **10 non-set glyphs** (`☾ ✦ ◷ ⤢ ¶ ⏰ 👍 👎 ☰ ↑`) are also untouched and still labelled as a problem on the styleguide: this task replaced an icon set, not a hunt for text characters doing an icon's job.

The styleguide now renders the real set at four sizes and both weights, and gained `--warn`, `--hover` and `--scrim` swatches. `ng build` clean — the bundle is *smaller* than before despite 57 kB of inlined icon data, since Lucide left with more than it. Smoke **37/37**.

## 2026-07-31 — Phase 11: the type sweep is finished — **0 hardcoded font-sizes left in the app**

The remaining eleven stylesheets (`glossary`, `login`, `register` and the eight shared components) went in one pass, then the editor's 67 — the single biggest file — closed it out. **314 → 0.**

**Shared components deliberately do not follow density.** A component that appears on both `/drafts` (compact) and `/settings` (comfortable) would otherwise render at two different sizes, and for the page header and the account menu that means the app's chrome changing size depending on which page is under it. Chrome must be stable, so these use the fixed role tokens (`--fs-ui`, `--fs-body`) rather than `--dens-fs`. Density variance stays opt-in, expressed by a page in its own stylesheet.

Two more tokens fell out of the sweep rather than being invented for it:
- **`--scrim`** — the modal backdrop was a fixed `rgba(24, 21, 16, .45)` identical in both themes, which is wrong in the direction that matters: the same 45% veil that separates a dialog from a light page barely registers against a dark one. Dark now gets its own value.
- **`--warn` earned its keep immediately.** The editor turned out to hold three more instances of the same tone the admin panel had — `#B08618` on the unsaved-state dot and the sync indicator, and `#C9A227` mixed into the RU/EN diff marker. All four were the same idea written three different ways.

**Two literals stay, both for the same reason**: `#fff` on a channel avatar and on the Telegram brand icon. Those backgrounds are a generated colour and a brand colour — not theme surfaces — so `var(--sheet)` would go dark behind them and become unreadable. A token would be actively wrong there.

`ng build` clean, smoke **37/37**. `editor.component.css` grew from 26.70 kB to 27.44 kB against its 20 kB budget, purely because `var(--fs-ui)` is longer than `13px` — that budget was already over and is T-092.

**Open question this surfaced (Q-16):** ADR-071's principle 4 says the editor sheet gets a serif, but the sheet's typeface is *already a user setting* (`AppearancePrefs.typeface`, five system stacks, default `system` = sans). Honouring the principle means changing a default that every existing user's editor already reflects. Left alone rather than decided quietly.

## 2026-07-31 — Phase 11, screens 4–6: admin, comments, stats — and the palette's missing third status

Three screens in one pass because they are the same shape: a list, a log and a set of stat cards, all compact, all wanting identical edits. 58 hardcoded font-sizes and 6 colour literals gone.

**The palette had two status tones and the app had three.** `--danger` and `--ok` were tokens; the third lived in `admin.component.css` as `color-mix(in srgb, #C9A227 18%, transparent)` with `#8A6A10` text — and a hand-written `:root[data-theme="dark"] .chip.warn` override to `#E3C35C` sitting eighty lines below the rule it corrected. The value existed and was already split by theme by hand, so **`--warn` only gives it a name**: light `#8A6A10`, dark `#E3C35C`, and `.chip.warn` is now the same three lines as `.chip.danger` instead of a special case.

**`comments` and `stats` needed no density attribute at all.** They are tab bodies inside the Posts Manager and inherit its `data-density="compact"` through the cascade. That is the whole reason density is expressed as custom properties rather than as a class each component has to remember to carry — a component that moves to a different surface adapts without being edited, and one that appears on two surfaces cannot be wrong on either.

`admin` also had no breakpoint of any kind. Its two wide tables (audit log, invite codes) already scroll inside their own containers, which is right for a log nobody reads on a phone; what needed the work is the user cards, which are the part an admin taps. Their action footer bleeds to the card edges via a negative margin equal to the card's padding, so both had to move together — written as `calc(-1 * var(--space-3))` rather than as two numbers that agree today.

`ng build` clean, smoke **37/37**. **142** hardcoded font-sizes remain, from 314.

## 2026-07-31 — Phase 11, screen 3: `/settings`, the first comfortable-density screen

The first screen to demonstrate the *other* half of ADR-071: `/settings` is a form, not a table, so it stays **comfortable** and its controls get roomier rather than tighter. Same tokens as `/drafts` and `/posts` — `--dens-control-y/x` — resolving to 7px/14px here instead of 5px/10px, because the page does not carry `data-density="compact"`. Nothing about the components changed; the surface they sit on decides. That is the whole claim of principle 3, and this is the first place it is visible side by side.

43 hardcoded font-sizes and 5 colour literals gone. **Two literals stay on purpose**: `#2AABEE` is Telegram's brand blue and `#fff` its paired foreground. A brand colour is not a theme value — tokenizing it would mean the mark shifting hue between light and dark, which is the one thing a brand mark must not do. Same rule the brand icons already follow (ADR-054).

This screen was on the "never checked on a narrow viewport" list in `TASKS.md`, and it had exactly one breakpoint — the plan grid. Everything else kept desktop measurements: 26px of card padding inside a 20px-padded body left under 300px of usable width on a 390px phone, the card frame eating a tenth of the screen. The anchor chips also stop being sticky there, because on a short screen they cover the content they scroll to, and they were wrapping to three rows anyway. The toolbar-row editor stacks; the accent presets go from five columns to three.

`ng build` clean, smoke **37/37**. 200 hardcoded font-sizes remain across the other 16 stylesheets, from 314 at the start of the phase.

## 2026-07-31 — Phase 11, screen 2: `/posts`, and two defects the phone capture exposed

Same treatment as `/drafts`: zero hardcoded font-sizes, zero colour literals, `data-density="compact"` on the page root, control padding on the density tokens, plus the narrow-screen and touch-target passes.

**What the migration deliberately did not do**: the card and badge paddings here are 9/11/14/18px, steps the spacing scale does not have. Forcing them onto the nearest token would be a visual decision, and that decision belongs to the mockups (T-076), not to a sweep. They stay as they are and are named here rather than quietly left.

Two real defects turned up, **both pre-existing and neither introduced by the migration** — they are simply what happens when a screen is finally looked at on a phone:

- **Every input on the page hung past the right edge of its card.** `.chat-input` has `width: 100%` with padding and a border and no `box-sizing: border-box`, so it was wider than its container by exactly 22px. Invisible on a desktop, where there is slack to absorb it; unmissable at 390px. The same class in `drafts.component.css` has always carried the line.
- **Long post titles were clipped mid-word with no ellipsis.** `text-overflow` does not apply to a flex container, and `.post-row-title` has to be one so the lock icon can sit beside the text — so the property had been sitting there doing nothing. The text now has its own element.

A third suspicion did not survive checking: the phone capture looked like the post list was overflowing its card, and the fix I reached for first (`min-width: 0` on the grid children) was aimed at a grid track that measurement showed was already fine — `.post-row` was 348px inside a 348px box. The rule is kept, because a nowrap title in an auto-minimum grid track is a real hazard, but the actual overflow was the input above. Worth recording as the reason to measure rather than pattern-match: the guess and the bug were both about width, and they were not the same bug.

`ng build` clean, smoke **37/37**, captured at 1440 / 1180 / 820 / 390.

## 2026-07-31 — Phase 11, screen 1: `/drafts` on the new tokens

Migration is **screen by screen**, not all at once, and that is a recorded decision rather than a preference (ADR-070/071): the smoke suite runs green after each one, so "broken by the migration" stays separable from "was already broken". Doing the lot in one commit throws away the only instrument Phase 10 was built to provide.

`/drafts` went first because it is table-shaped — the clearest place to see compact density — and because Marty's iPad and iPhone complaints live on it. **The stylesheet now holds zero hardcoded font-sizes and zero colour literals**, and the page opts into `data-density="compact"` on its root.

Three tokens were added along the way, each because this screen needed it and every other screen will too: `--hover`, `--hover-strong` and `--hover-danger` — the app was carrying `rgba(128, 120, 100, .08 / .1 / .14)` in a dozen places, a colour that belongs to neither theme and merely looked tolerable in both — and `--icon-xs`. Two icon classes turned out to be 15px and 14px: a one-pixel difference carrying two names, now both resolving to `--icon-sm`.

**T-034 closed on this screen at the same time**, per ADR-070's rule that breakpoints ride with the migration rather than following it — written afterwards they cost a second full pass over markup that just moved. The toolbar wraps instead of growing past a phone's viewport (that was the whole of "on an iPhone the page is wider than the screen": a nowrap flex row holding a title, a 240px search field, three pickers and a button, with the global `overflow-x: hidden` quietly clipping whatever fell off). And the column set went from one tier to three: ≤1280 drops Tags and Activity, **≤900 also drops Folder and Updated** — which is what had been pushing the row actions off an iPad in portrait, making archive and delete unreachable — and ≤560 keeps State alone, at 80px rather than the 200px a desktop had chosen for a one-word badge.

The phone tier needed a second pass. Handing the grid one column width while the template still rendered two cells put the extra cell on an implicit second row, so every row grew to double height with the actions wrapped underneath — visible immediately in the capture, and exactly the CSS-versus-TypeScript disagreement the code already carries a warning about from 0.9.19. The `ROW_GAP`/`ROW_PADDING` constants were also updated to match the compact tokens, for the same reason.

Verified by capture at Marty's three real sizes (1180 / 820 / 390), not by eye; `ng build` clean, smoke **37/37**.

## 2026-07-31 (v0.9.21) — Phase 11 opens: the direction is chosen, and deploys stop being invisible

**Q-11 answered.** Marty picked the hybrid: warm editorial as the base, the dense-product school's density borrowed on the table-shaped screens, one palette and one type scale throughout. Recorded as **ADR-071** with seven binding principles at the top of `docs/DESIGN.md`. **Q-12 answered too — Phosphor** (ADR-072), delivered as inlined SVG behind one `app-icon` component rather than the icon font (ships the whole face, poor host for the labels T-080 needs) or the web-components package (needs `CUSTOM_ELEMENTS_SCHEMA`, which switches off template type-checking for a whole component).

The finding that changed the shape of the phase: **the current tokens are already warm editorial** — `#ECE9E2` paper, `#5B6E46` olive — because that is what the 08.07 "Cabin" redesign built. So Phase 11 is systematization plus a density layer, not a repaint, and the screens can migrate one at a time with the smoke suite green after each instead of needing a big-bang cutover.

**Tokens v2** landed additively — not one existing value changed, which is why 37/37 smoke tests stayed green without touching a test. New: `--font-serif` (system stack; the Pi serves every byte, so a webfont is a decision nobody has made), the size scale extended to `--fs-9…--fs-27`, semantic roles (`--fs-caption/meta/ui/body/title`, `--fs-read`/`--lh-read`), the `--dens-*` set with a `[data-density="compact"]` override on a page root, `--icon-sm/md/lg`, and `--motion-fast/base/slow` + `--ease` with a global `prefers-reduced-motion` clamp at 1ms (not 0 — a few places wait for `transitionend`).

Measuring the sweep found something worse than its own size. There are **314** hardcoded `font-size` declarations, not the 191 first counted — that regex matched whole pixels only, which is exactly how the real problem stayed invisible: **110 of the 314 are half-pixels** (44× `12.5px`, 30× `11.5px`, 16× `10.5px`, 14× `13.5px`). A browser rounds those per element at render, so two controls a half-step apart can look identical at one zoom and different at another. And 79% of all UI text sits in an 11–13.5px band — the whole type hierarchy is about five distinguishable sizes crowded into 2.5 pixels. The v2 scale is integers only.

**`/dev/styleguide`** (T-078) puts every token and control state on one screen with live theme and density toggles, and is captured into `.e2e-audit/70…73` in all four combinations. Its own CSS obeys principle 7 — every value a token — so it doubles as the worked example. One suspicious-looking thing was checked programmatically rather than by eye: the `--bg` swatch looked white in the dark screenshot, and the computed value is `rgb(29,27,23)`. No defect; a downscaling artefact.

**A real bug found by Marty not being able to open that page.** He reported `/dev/styleguide` bouncing him back into the app after it was deployed — and the page was genuinely in the deployed bundle. The origin was sending **no `Cache-Control` and no `ETag` on `index.html`, only `Last-Modified`**, which lets a browser apply heuristic freshness and never ask the server again. A stale `index.html` pins the browser to hashed bundle names that the deploy has already deleted — and because Cloudflare independently caches those hashed assets for 4 hours, the *old bundle is still being served from the edge*, so the whole old app keeps working and the new release is simply invisible. This was never specific to the styleguide: **every deploy has been invisible to returning browsers for an unpredictable window.** `index.html` now goes out `no-cache, must-revalidate`, applied both to the static-file middleware and to `MapFallbackToFile` — the latter matters, because that is the branch every deep link takes, and fixing only the first would have helped nobody who did not arrive at `/`. The hashed assets are deliberately untouched: their names change with their content, which is what makes caching them hard correct.

Verified on the actual published build rather than the dev server (the dev server never exercises this middleware): `/`, `/dev/styleguide`, `/drafts` and `/posts` all answer `must-revalidate, no-cache`, and `main-*.js` still answers with no `Cache-Control` of its own. `dotnet test` 442/442, smoke 37/37, `ng build` clean.

## 2026-07-31 (v0.9.20) — the editor's topbar didn't fit on an iPad either

Second screenshot from Marty's iPad, two things circled: **"Posts Manager" broken across two lines and sitting on top of the Export button**, and **the account email running off the right edge of the viewport**.

One cause behind both: the editor's topbar is the crowded row — it carries the title field, the save state and Export on top of the four nav buttons every screen has — and the only rule that thinned it out fired at 768px. Between 768 and full width there was nothing, so at an iPad's 1180px everything stayed and the row overflowed. Now the nav buttons drop their labels at ≤1280 and keep their icons (each has a title, and the same row is labelled on every other screen, where it does fit), and labels are `nowrap` so a squeezed one clips instead of becoming two lines and making its button taller than the row.

The email was a different failure: `.user { display: none }` sat in the editor's own stylesheet, and had been dead since IB9 moved the account menu into a shared component — Angular's view encapsulation means an editor selector cannot reach into `app-account-menu`. It never hid anything, on any screen size. The rule now lives where the element does, and the avatar beside it still opens the menu, which shows the full address — so what is hidden is a duplicate, not a fact. The other orphaned rules (`.user`, `.profile-email`) were deleted rather than left looking load-bearing.

iPad portrait (820px) got two more: the wordmark broke across two lines, and with that fixed the row was still ~30px long and pushed the avatar off the edge — so "Cedar Clerk" drops below 900px and the logo carries the branding alone.

Verified at 1180 and 820 by capture, not by eye. **Still open** (T-034, Phase 11): the `/drafts` table scrolls sideways in portrait so the row actions sit past the edge, and on an iPhone the whole page is wider than the viewport.

## 2026-07-31 (v0.9.19) — the drafts table had no titles in it on an iPad

Marty sent a screenshot from his iPad: the drafts list, with `TITLE` and `STATE` drawn on top of each other and **not one draft name visible** in any row.

The arithmetic was simply wrong. `.drafts-row` carried `min-width: 1020px`, described in its own comment as "sum of the fixed columns + gaps + padding, leaving room for the 1fr title". The real sum is 960px of fixed columns + 80px of actions + seven 12px gaps + 32px of padding = **1156px before the title gets a single pixel** — so the stated minimum was 136px short. A grid gives a fractional track whatever is left after the fixed ones, and on an iPad's ~1130px of content width that is nothing: the title collapsed to zero and its header slid under the next one.

Three changes, and the first is the one that matters: **the title track has a floor** (`minmax(200px, 1fr)`), so it cannot be squeezed out of existence again. **The row's min-width is now computed** from the columns actually showing rather than written down and left to drift. And below 1280px the table **drops Tags and Activity** instead of scrolling sideways — they are the two columns you can lose and still recognise a post, which is what makes iPad landscape fit whole. The resize handles only render at full width: in compact mode the column indices no longer line up with the stored widths, and a 5px pointer target is not something a finger hits anyway.

Verified at Marty's actual device sizes, not round numbers — iPad landscape (1180), iPad portrait (820) and iPhone 13 (390) are now captured by the audit script. **Still not fixed, and recorded rather than quietly left**: iPad portrait shows every title but scrolls to reach the row actions, and on an iPhone the whole page is wider than the viewport. Both belong to T-034's full responsive pass in Phase 11.

En route, the smoke suite was failing intermittently on one test and the trace said why: the text was typed and saved, `Shift+Home` selected the line, and `Ctrl+B` then did nothing at all — no mark, no document change, so no autosave to wait for. The same test passes when run alone. The suite now clicks the toolbar's Bold button, which is what a person presses anyway, and the shortcut's inconsistency is **T-100** — to be reproduced by hand before deciding whether it is a real bug or an artefact of synthetic key events. `withSave` also got a longer window, because a suite that fails under load rather than on breakage stops being read.

`dotnet test` 442/442, smoke 37/37, `ng build` clean.

## 2026-07-31 (v0.9.18) — Phase 10: the audit that had to come before the redesign

Marty asked for four things at once: a full UI check, a redesign in one modern style, reworked icons, and one more social network. **ADR-070** splits them into three sequential phases and this session is the first of them, because the project's dominant risk is verification debt, not code debt — a large amount of shipped work had never been opened in a browser, and restyling on top of that destroys the ability to tell an old defect from an introduced one. Phase 11 (Design System 2.0) and Phase 12 (Publishing Targets) are documented, not started; both are blocked on product decisions (Q-11, Q-1).

**The frontend got a safety net.** There was none: 442 tests covered the backend and Core, three spec files covered two utilities. A Playwright smoke suite now runs **37 scenarios** over the critical paths — session survival, the draft round-trip, ShrinkGuard's refusal and recovery, version restore, the private-post gate end to end, blog reactions and comments, the admin gate, the Posts Manager, the locale and theme switches. `Scripts/e2e.ps1` owns the run: it wipes a scratch `CEDAR_DATA_DIR`, starts the server **with no bot token** (the environment name is deliberately not `Development`, which is the only thing loading the file the token lives in — so the local process never long-polls the Pi's token), seeds the account, restarts once so the admin bootstrap can promote it, then runs the suite. Nothing it does can reach the real local data, let alone production; Playwright ships no armhf browsers, so this never enters the deploy pipeline.

What the first run taught, all recorded as comments in the tests: the save indicator **cannot** be asserted directly — right after a keystroke it still reads "Saved" from the previous save, so the suite waits for the PUT itself; Node resolves no `*.localhost` (only Chromium special-cases it), so blog API calls go by address with a `Host` header; and navigating straight after the gate's submit button aborts the fetch in flight, which looks exactly like a broken gate rather than a broken test.

**`docs/UI-INVENTORY.md` was extended, not rewritten** — the ten per-element tables are untouched. Added: a verification map with one row per route/surface, `admin.component` (missing entirely), **the blog's thirteen server-rendered surfaces** (also missing entirely — roughly half of what a reader ever sees, and a second style system the redesign will have to touch), everything from 0.9.16–0.9.17, and an icon inventory measured rather than recalled.

That inventory corrected an assumption: **no icon control is unlabelled** — every one carries a `title`. The gap is that `title` never appears on touch, so on iPad and iPhone they are unlabelled in practice, and `aria-label` appears exactly once in the whole app. It also found that `.icon` is declared in **nine files with three different values** (15/18/20px) because Angular's view encapsulation makes each component redeclare it, plus ten kinds of glyph (`☾ ✦ ◷ ⤢ ¶ ⏰ 👍👎 ☰ ↑`) used as icons outside the set and rendered in the OS emoji font.

**Seven defects found, four fixed.** The audit walked every screen — through Playwright, after the browser extension proved unreliable here (screenshots timing out, zoom returning the wrong region, keystrokes never reaching the TipTap surface). Fixed: the editor's status bar was still hardcoded English (`words`, `Synced`, `Syncing…`, and "1 words") — a miss of the ADR-050 sweep, not a decision; the two Appearance sliders rendered in the browser's own blue because `accent-color` was never set on them, in the panel that exists to choose the app's colours; the export window told you to connect a channel "in the Channels section **above**" while that section is 66 lines **below**; and the save-guard dialog put the accent on **"Save anyway"** while the safe "Restore stored" was the ghost button — backwards for a dialog whose whole reason to exist is a document that was about to be deleted.

Left in the backlog on purpose: **T-094** — the blog's month header is hardcoded Russian (`RuMonthNames`) while card dates use `InvariantCulture`, so "ИЮЛЬ 2026" and "31 Jul 2026" sit on the same page and neither follows the page's language; the invariant culture is a deliberate choice (no ICU on the Pi), so the fix is a month array per language, not a culture change. **T-098** (version-history timestamps in the browser's locale) and **T-099** (the blog footer not pinned on short pages) go to Phase 11, which will touch that markup anyway.

Also confirmed working live, having never been looked at before: semi-public posts appear on the blog index with a lock **and no excerpt**, all four registration-form field types render on a real gate (short, long, static block, consent), and the save guard explains itself in plain numbers ("512 characters before, 1 left").

`dotnet test` 442/442, `ng build` clean, smoke suite 37/37. **Nothing deployed** — Phase 10 ships no visual change by design, and the four fixes are one commit each.

## 2026-07-30 (v0.9.17) — /login was the one page that never asked whether you were already signed in

Reported right after the 0.9.16 deploy: log in, close the browser, reopen it, go to `/login` — and
it asks for the password again. The cookie was never the problem (`isPersistent: true`, 30-day
lifetime as of 0.9.16); `/login` and `/register` were simply the only two routes with no guard, so
they rendered their form without asking the server anything. Every other URL, including `/`, goes
through `authGuard` and would have let the same browser straight in.

New `guestGuard` — the mirror of `authGuard` — redirects to `/drafts` when a session is live. A
server that doesn't answer deliberately falls through to the login page instead of redirecting:
the session is then unknown rather than proven, and the page already has the retry for that case
(T-062). The login component's own startup probe is gone with it; the guard has asked by the time
the page renders, and probing again just repeated the retry backoff.

## 2026-07-30 (v0.9.16) — the 29.07 wipe answered from both ends, and re-translation stopped being all-or-nothing

A full day's pass over the highest-priority rows of the restructured backlog. `dotnet test` 442/442, `ng build` clean, frontend tests 11/11. **Nothing below has been clicked through in a browser or deployed.**

**Saving can now refuse.** ADR-066. The 29.07 incident needed three things to go wrong at once and all three did: the 1.2s autosave honestly saved the empty document that exists for an instant while a table is being deleted, the server took it without a word, and closing the tab on iOS killed the timer carrying the restored text. ADR-065 closed the publishing half in the morning; this closes the saving half. `ShrinkGuard` (Core) measures *extracted text*, not JSON length — a table becoming paragraphs rewrites most of the JSON while keeping every word — and when a save would leave 20% or less of a document that had at least 200 characters, both save endpoints answer 409 and the editor asks. The dialog's second button is "restore stored", which is the recovery the incident had no button for at all. A pending save is now flushed on `pagehide`/`visibilitychange` through `fetch(..., {keepalive:true})` (`HttpClient` cannot do this; past ~30k characters the browser rejects a keepalive body outright, so the ordinary save is used instead). A save may carry `expectedUpdatedAt` and gets a 409 rather than silently overwriting — optional, so the Posts manager's rename-PUT and the import paths are untouched, and it is the prerequisite recorded against editor tabs (Q-2). A failed save retries itself at 2s/5s/15s instead of waiting for the next keystroke.

**Sessions stop dropping, and the keys that decrypt them get backed up.** `refresh()` treated *any* `/api/auth/me` failure as a logout — a network blip, a 5xx, the 502 Cloudflare returns while the Pi restarts mid-deploy. It now retries, clears the session only on a 401, and otherwise reports "unavailable", which the login page shows as a retry instead of a password prompt. The auth ticket also got an explicit 30-day `ExpireTimeSpan`: Identity's default is 14 days, so under a 30-day cookie the shorter one silently won and looked random. Separately, the DataProtection keys moved from `~/.aspnet/DataProtection-Keys` into `CEDAR_DATA_DIR` (T-074) — they decrypt every auth cookie and the nightly backup had never seen them. **This one has a manual prerequisite before the deploy — see `TASKS.md`.**

**Version history became reachable.** ADR-067. It has been written on every save since ADR-065 and read-only ever since, which meant recovering a version was a `sqlite3` session on the Pi. A row now opens the version: its text, its diff against what is stored or against any other version, and a Restore button. Restoring records the version being replaced *first*, so a restore is itself undoable through the same history, and leaves a `restore` marker that pruning never touches.

**Re-translation only translates what changed.** ADR-068, the feature ADR-064 promised and ADR-065 withdrew. An LCS alignment of the stored source snapshot against the current source says which top-level blocks moved; those go to the provider as a real (partial) TipTap document, and the rest are copied out of the existing translation — **manual corrections included**, which is the actual reason "re-translate" was avoided. It refuses itself and falls back to a full translation whenever positional reuse can't be trusted: no snapshot, a snapshot from a different primary language, a translation restructured by hand, or a document where everything changed. Zero changed blocks costs no provider call at all.

**Ukrainian, Belarusian and Georgian**, with a real capability check: DeepL has no target for the latter two, and the check now happens *before* the daily AI quota is charged rather than after. The private-post gate is translated into all three (not by a native speaker — flagged in the code). En route, several strings that still said "the English version" and "from Russian" — leftovers from before per-draft primary languages — became functions of the actual language code.

**Also**: translate into every ticked language in one press (T-014, modelled on the glossary's batch translate, cost shown up front); two new form field types — a long multi-line answer and a static text/image block the reader only reads (T-031/T-032); the Posts Manager opens a submission in full on click, gained "mark all as read", and hides sent schedules behind a toggle (T-035/T-037/T-038); the Appearance panel's Apply button — real, but indistinguishable from decoration next to a live preview and a self-saving toolbar half — is gone, replaced by autosave and a status line (ADR-069).

**Half-done on purpose**: server-side messages now answer in the reader's language through `CultureInfo.CurrentUICulture`, set per request from the account's profile, so no call site passes a language. `ErrorMessages` is translated; the ~130 inline `{ error = "..." }` strings in the endpoint files are not, so a Russian UI still shows a mix. The mechanism was the hard half — the rest is mechanical — and the backlog row says exactly that instead of claiming T-050 shipped.

## 2026-07-30 — ADR-064 corrected: the publish guard actually guards

Acting on the audit below. **ADR-065** (`docs/DECISIONS.md`) records what changed and, as importantly, which of ADR-064's own claims were withdrawn.

**The guard moved to the server.** `ConfirmedFingerprint` was declared and never read or sent — the confirmation modal was decoration. Both publish paths now *require* the fingerprint of the version the owner was shown whenever that target already has a publication revision, and answer `409` with a freshly-calculated diff otherwise; the client re-opens the modal on that body instead of showing an error. The client also **flushes the pending autosave before asking for the preview** — with a 1.2s debounce, "type, hit Update" previewed the previous version and published the new one, which is the same defect the guard exists to prevent. The guard now covers **the blog** (ADR-064 said it did; only Telegram had it — and blog-only publishing was the exact path of the 29.07 wipe), keys off the server's `publishedBefore` rather than the client knowing a public post URL (channels without an `@username` silently skipped it entirely), and shows **every already-live language**, since one click publishes them all and a single language's "no changes" could hide a rewritten translation.

**`PrimaryLanguage` is now primary everywhere.** Static HTML export, `.zip` export, AI edit and the v1 registration-form slot still compared against a literal `"ru"`, so a draft whose primary is English got a 400 for its own language, an English document labelled and styled Russian, a duplicate `index.html` in the archive, and a guaranteed 404 from every AI edit — after the quota was already spent (the charge now happens once there is something to edit). There is one list of content languages and one predicate; `TranslationLanguages`/`IsTranslationLanguage` are gone, because "is this a translation" is a per-draft question and answering it statically is what produced both a duplicated `ru` entry and translation rows shadowing a draft's own primary language (which also made the swap 500 on a unique-index collision). The frontend's `PRIMARY_LANGUAGE` became `DEFAULT_PRIMARY_LANGUAGE` — it is only the language a *new* draft starts in — which is what the diff gutter had been getting wrong.

**Revisions stopped being a disk leak and a privacy defect.** They were a full copy of the document on every autosave pause, with no deduplication, no ceiling and no cleanup. Now a revision is written only when the content actually changed, `save` revisions are pruned to the newest 50 per draft+language (publication revisions never are — they are the diff baselines), and deleting a draft or a language deletes its revisions, which previously left complete copies of a deleted private post in production and in all 14 backup generations. **No migration**: an FK with cascade would be a table rebuild on SQLite, which is the class of migration `.claude/rules/ef-migrations.md` exists about — explicit deletes are deterministic and need no schema change, so Codex's migration ships byte-for-byte as generated.

**A no-op save is now a no-op** — the root cause of the false-dirty language tabs (the old `IB3`), finally explained: staleness is a timestamp comparison and the server bumped `UpdatedAt` on saves that changed nothing (a touched title, a typed-then-undone edit, a Posts Manager rename that PUTs the unchanged body back). Both draft and translation saves return early on byte-identical content. Relatedly the primary-language swap carries the promoted version's own timestamp instead of stamping "now", which preserves every relative recency so a pure relabeling flips nothing to stale; it also gives the demoted language a real snapshot, nulls the *other* translations' snapshots rather than leaving them pointing at a document in a language they were never translated from, and `SourceLanguage` — previously write-only — now decides whether a snapshot is offered to the editor at all.

New `DraftRevisionServiceTests` (12 tests) pins the dedup, the pruning-keeps-publications rule, per-language and per-destination isolation of the guard, and that the canonical slot follows `PrimaryLanguage`. All the new UI strings went onto `t()` — they had shipped as hardcoded Russian in the most safety-critical dialog of the change.

`dotnet test` 408/408, `ng build` clean, frontend tests 7/7. **Not yet live-verified in a browser or deployed.**

## 2026-07-30 — audit of the ADR-064 changes + docs actualization (no code changed)

**Audit of Codex's uncommitted ADR-064 work** (per-draft primary language, `DraftRevision` history, publish-diff guard) — a 14-agent adversarial review, every major finding independently re-verified against the working tree. Direction confirmed, but 9 major defects found before anything gets committed: the server-side stale-publish guard the ADR promises is not implemented (`ConfirmedFingerprint` is dead code, the confirm modal is client-side-only and skips the blog path entirely — the exact path of the 29.07 data-loss incident); several endpoints still hardcode `ru` as primary (static export, AI edit, ZIP export → broken for any non-ru-primary draft); the primary-language swap corrupts translation snapshots; `DraftRevisions` grow unboundedly (full document copy per autosave, no dedup/pruning/FK). Same session root-caused three long-standing issues: the 29.07 wipe (transient-empty autosave + no flush-on-close + guard-free PUT; would replay identically today), the false-dirty language tabs (unconditional `UpdatedAt` bump on byte-identical PUTs vs timestamp-only staleness — the old IB3, finally explained), and the frequent re-logins (any `/api/auth/me` failure — network, 5xx, deploy-window 502 — is treated as logout while the cookie is alive; plus the auth ticket's default 14-day `ExpireTimeSpan` under the 30-day cookie). Also mapped: cross-browser private-post access (cookie-only by design; fix = post-registration `?invite=` token) and the translation pipeline's readiness for uk/be/ka + translate-all + incremental re-translation. Full fix plan in the session report; consolidated in `docs/BACKLOG.md` as T-013…T-023, T-060…T-064, T-074.

**Docs actualization**: `docs/BACKLOG.md` restructured into a task board (ID/Имя/Приоритет/Теги/Описание — Marty's own Input.md format; done rows deleted, history in git); the 28.07 `Input.md` rewrite registered as Phase 9f in `docs/ROADMAP.md` (with what already shipped from it — DB1-3, consent, copy protection, multi-language presets — marked done); stale rows fixed (IF2 admin panel claimed "not started" while fully built 27.07; `TASKS.md` claimed I15 open while shipped 27.07); the stale "(latest, uncommitted)" markers above cleared for entries whose commits landed (`4c4737e`, `8221201`, `db89a03`, `c4f2628`, `0f5364e`).

## 2026-07-29 — private posts: copy protection on the blog page

Marty's ask: a switch in the Export window and the Posts manager forbidding copying (and the context menu) on private posts' blog pages. ADR-063 (`docs/DECISIONS.md`): new `Draft.DisableCopy` + `POST /api/drafts/{id}/disable-copy` (the `/listed`/`/watermark` endpoint shape), and `BlogEndpoints.RenderPostAsync` injects — only when the post is *both* private and flagged — a `user-select: none` style plus a tiny script blocking `contextmenu`/`copy`/`cut`/`dragstart`, scoped to `.post-sheet` so the comment/annotation UI below stays fully usable. A deterrent, not protection (the page source is one Ctrl+U away), and the UI hint says so. Checkboxes: Export modal's blog section (visible while Private is on, next to "show in list anyway") and the Posts manager's private-post section. Migration `AddDraftDisableCopy`.

`dotnet test` 396/396, `ng build` clean. Not yet live-verified in a browser or deployed.

## 2026-07-29 — glossary: translate a whole language at once

Follow-up ask from Marty on ADR-061: translate *all* terms of the selected language into the other languages in one action. Doing it through the per-term endpoint would burn one daily-AI-quota call per term per language, so ADR-062 (`docs/DECISIONS.md`) amends ADR-061's "no batch endpoint": new `POST /api/glossary/translate-all` `{ sourceLanguage, targetLanguage }` — same gates (Pro Plus, `ITextsTranslationProvider`, daily quota), but **one quota call and one chunked provider call per target language** covering every term+description pair of the source language. Upsert per ADR-061's rule, with existing target-language terms loaded once up front and same-batch creations joining the case-insensitive match (two sources translating to the same word update one row). Unusable translations (blank/over-length term) are skipped and counted, not fatal. UI: a "Translate all" ghost button next to the language tabs (shown only when the language has terms) opens a modal identical in shape to the per-term one — target-language checkboxes, sequential per-language calls, stop-on-first-failure with the rest left checked.

`dotnet test` 396/396, `ng build` clean. Not yet live-verified in a browser or deployed.

## 2026-07-29 — glossary: auto-translate terms + the missing button styles

Two glossary asks from Marty (ADR-061, `docs/DECISIONS.md`):

**Feature — auto-translate a term into selected languages.** New `POST /api/glossary/{id}/translate` — the ADR-060 form-preset shape verbatim: Pro Plus + daily AI quota, `ITextsTranslationProvider` (Anthropic chunked / DeepL batch, others → 501), synchronous. Term + description are translated; **aliases deliberately are not** (they cover one language's inflections — a machine rendering of "рендерер, рендерера" into English is noise), and the image is copied as language-neutral. Upsert by translated term text (case-insensitive), so a second press refreshes the description instead of duplicating the row. UI: a languages icon on every term card opens a modal with checkboxes for the other five content languages; the frontend calls the endpoint once per checked language sequentially, stops on the first failure and leaves the untranslated languages checked for a retry. En route, the three auto-translate gate strings inlined in `DraftEndpoints`/`FormPresetEndpoints` moved to `ErrorMessages` (third use).

**Fix — the glossary page's buttons had no styling at all.** Button/input classes (`btn-accent`, `btn-ghost`, `mini`, `mini-remove`, `chat-input`, `field-hint-inline`) are per-component in this project, and `glossary.component.css` shipped without them — every button rendered as a bare browser default. Copied the definitions from `drafts.component.css`; the per-card "edit" text button became a pencil icon button to match the icon-button row pattern everywhere else.

`dotnet test` 396/396, `ng build` clean. Not yet live-verified in a browser or deployed.

## 2026-07-30 — forms: multi-language presets, consent field, and two real fixes

Three form asks from Marty in one pass (ADR-060, `docs/DECISIONS.md`):

**Fix 1 — "у меня есть пресет, а страница Постов говорит, что пресетов нет."** Two causes, both real: `loadPresets()` only ran from `setTab()`, which a direct landing on `/posts` never calls — so the preset library was simply never fetched on the page where it's most used; and the Export modal lacked the Posts tab's DB1 "form attached but library empty" hint wording. Presets now load eagerly in `ngOnInit`, and the Export modal passes `noPresetsSavedLabel` like the Posts tab does.

**Feature — multi-language form presets done right.** The old FI4.1 model (one independent blob per language, question ids = timestamps, options = bare strings) had no way to make "Да" and "Yes" count as the same answer. New v2 blob: one skeleton with stable question **and option** ids, per-language text dictionaries on top (`{"v":2,"languages":["ru","en"],...}`), versioned inside the same JSON columns — zero schema migration, v1 blobs stay readable everywhere. The rendered `<option>`/checkbox `value` is now the option **id**, so submitted answers are language-neutral and the distribution pie aggregates across languages (pre-v2 rows stored label text, which just misses the id map and displays as-is — nothing breaks). `RegistrationFormSet.Pick`/`LanguagesWithForm` understand both shapes, so `BlogEndpoints`/`DraftEndpoints` call sites didn't change. Forms tab editor reworked: language chips with **+** (add), **⟳** (auto-translate from the primary language — a new `POST /api/form-presets/{id}/translate`, Pro Plus + daily AI quota, reusing the chunked flat-string translation via a new narrow `ITextsTranslationProvider` on Anthropic/DeepL), and **×** (remove); per-language inputs on every question; options as id-stable rows with one input per language, replacing the comma-separated field (which couldn't keep ids aligned across languages). Applying a v2 preset to a post attaches **all** its languages in one click; the raw blob travels untouched (re-serializing the single-language projection would have silently stripped the other languages — both Posts tab and Export modal were rewritten to pass the raw JSON through).

**Feature — "Согласие" (consent) question type.** A block of agreement text with a single checkbox the reader must tick (`RegistrationQuestionType.Consent`): always required (forced in the editor, in Core's parser, and by the browser's own `required`), stored as `"yes"` in the same answers map, rendered as `reg-consent` text + checkbox with a localized "I agree" label in all six gate languages. The page script reads `.checked` via a dedicated `data-question-consent` attribute — the generic handler reads `.value`, which a checkbox reports whether ticked or not.

`dotnet test` 396/396, `ng build` + frontend tests 7/7 clean. Not yet live-verified in a browser or deployed.

## 2026-07-29 — Anthropic auto-translate: chunked instead of whole-document

Marty hit the same large-document pain again: a real ~47,000-character draft ran past 1000 seconds and had already 502'd once before with "Model returned malformed translation output." Root cause (ADR-059, `docs/DECISIONS.md`): `AnthropicTranslationProvider` sent the entire `Draft.CedarJson` to the model and required it to echo back the *whole* TipTap JSON structure with only `"text"` values translated — slow, and a single truncated/malformed response wasted 100% of the tokens for zero result, with no way to chunk since the contract required one valid JSON document as output.

**Fix**: `AnthropicTranslationProvider` now follows the pattern `DeepLTranslationProvider` already used successfully — `TipTapTextNodes.ExtractTexts` pulls only the human-visible strings out, the flat list is split into chunks (`Consts.Anthropic.TranslationChunkCharBudget`/`TranslationChunkMaxStrings`), chunks translate **in parallel** (up to `Consts.Anthropic.MaxParallelChunks` = 4, own `AnthropicClient` per chunk, the existing bounded overload/rate-limit retry kept per-chunk), then `TipTapTextNodes.ReplaceTexts` splices the results back into the untouched original JSON. The model never sees or reproduces document structure. New `TranslationChunkPromptGenerator` carries the flat string-array-in/array-out contract; `Consts.Anthropic.ChunkRequestTimeout` (2 min) is now the per-chunk HTTP timeout, decoupled from `AutoTranslateTimeout` (20 min, unchanged, still the overall `AiJobService` job ceiling across every chunk). `OpenAiTranslationProvider` intentionally untouched — not the active provider.

**Also fixed, found during the same investigation**: `TipTapTextNodes.Walk` only ever looked at `text`-node strings, never `attrs` — poll question/options, footnote body text, image/video/audio captions+alt, and toggle summary were silently never translated by *any* provider, despite the LLM prompt's own rule claiming "every human-visible text." `Walk` now visits these attrs strings too (own accessor per leaf instead of assuming `node["text"]`), fixing the gap for DeepL as well since it shares the same utility.

**Follow-up (30.07.2026), from the first real chunk translated**: a 72-item chunk failed with "Model returned 74 translations, expected 72" — a plain positional array has no error-correction, so one over-split entry shifts every index after it and fails the whole chunk. `TranslationChunkPromptGenerator` now uses a JSON object keyed by input index instead of a bare array (extra keys the model invents are ignored; a genuinely missing key names the exact failed index), blank/whitespace-only strings are filtered out before ever reaching the model, and `TranslateChunkAsync`'s retry loop now also covers parsing itself (previously only the HTTP call was retried, so a malformed/mismatched response failed immediately with zero retries).

`dotnet test` (380/380) clean. Not yet live-verified against a real large document or deployed.

## 2026-07-29 — auto-translate/AI-edit stop dying to Cloudflare's own timeout

Root-caused all the way, not just patched: Marty's real ~360-line/114KB document auto-translated *successfully* — the server saved a fresh EN translation to the database — but the browser never found out. `cedarclerk.mooexe.dev` sits behind a Cloudflare Tunnel, and the old auto-translate/ai-edit endpoints held one HTTP request open for the entire Anthropic call. For a large document that call can legitimately run long enough to outlive Cloudflare's own edge-to-origin timeout, which then returns its own `cloudflare_error: true` 502 straight to the browser — independent of anything this app does, and even though the origin goes on to finish the work. Confirmed directly: `journalctl` + a read-only `sqlite3` query against the Pi's live DB showed the translation land at 22:27:15, two seconds before a `DELETE /translations/en` request (Marty, having watched a dead spinner, assumed it failed and cleared it) — the retry-on-overload fix from earlier today never had a chance to matter here, since Anthropic wasn't the problem this time.

**Fix: auto-translate and ai-edit no longer hold a request open at all.** New `AiJobService` (`CedarClerk.Server/AiJobService.cs`) — an in-memory, single-process job tracker (deliberately not persisted; a job is cheap to lose on redeploy, the client's poll just gets a 404 and reports a clean failure). Both `POST /translations/{lang}/auto` and `POST /ai-edit/{lang}/{kind}` now do their existing fast synchronous checks (language validity, ownership, Pro Plus gate, daily AI quota) exactly as before, then hand the slow part (the actual Anthropic call + persisting the result) to a background job and return `202 { jobId }` immediately. New `GET /api/ai-jobs/{id}` / `DELETE /api/ai-jobs/{id}` (`AiJobEndpoints.cs`) let the client poll status and cancel. The background work opens its own DI scope (`IServiceScopeFactory`) for a fresh `CedarDbContext`, since the request's own `db` is disposed long before a large translation finishes.

Frontend: `editor.component.ts`'s `runAutoTranslate()`/`aiEdit()` moved from one RxJS `subscribe()` to `start → poll loop → apply`, polling `GET /api/ai-jobs/{jobId}` every 2s. Same pseudo-progress bar, same cancel-button UX (now cancels the job server-side via `DELETE`, not just the HTTP connection) — the change is invisible to the user except that it can no longer die to a timeout that was never really this app's own.

**Follow-up, same session — two more real limits found testing against the actual document:**
- **Client poll budget bumped 3 min → 10 min** (`AI_OPERATION_TIMEOUT_MS`, `drafts.service.ts`), to match `Consts.Anthropic.RequestTimeout` — which turned out to already be 600s/10 min, not 60s as earlier notes (including this file) had it; that was a plain misread of the constant, not a real value change. Fixed the stale comment above it while there.
- **`MaxTokens` raised 16,000 → 64,000** (`Consts.Anthropic.MaxOutputTokens`, used by both `AnthropicTranslationProvider.cs` and `AnthropicAiEditProvider.cs`). Root cause of a second, distinct failure on the same real document: the translation prompt requires the model to return the *entire* translated TipTap JSON (structure included, not just prose) as one JSON object, and 16,000 tokens wasn't enough to hold that for a ~47,000-character document — the response cut off mid-JSON, which `TranslationPromptGenerator.ParseResult` correctly reported as "Model returned malformed translation output" (a real parse failure, not a mislabeled timeout). Verified Haiku 4.5's actual output ceiling (64,000 tokens, Anthropic's own model table) before raising it, rather than guessing.

**Follow-up #2, same session — the actual root cause of the 10-minute timeouts.** Even after the two fixes above, the same real document's translation ran past 14 minutes with zero errors anywhere — confirmed live: `journalctl` showed the fast synchronous checks complete, then nothing (no DB write, no exception, no log line) for 14+ minutes on a continuously-running process, well past `Consts.Anthropic.RequestTimeout` (600s), which never fired. Root cause: **`max_tokens` is a hard cap shared between thinking tokens and the actual answer** (confirmed against Anthropic's own adaptive-thinking docs), and `ThinkingConfigAdaptive()` defaults to `effort: "high"` — which the docs themselves warn "may think extensively" on complex-looking input. A 47,000-character document reads as complex, so the model burned enormous latency (and much of the 64,000-token budget) on reasoning before producing any answer, for a task — preserve JSON structure, translate text values — that doesn't benefit from deep reasoning at all.

First fix: `OutputConfig = new OutputConfig { Effort = Effort.Low }` added to both providers' `MessageCreateParams`, turning down thinking for a mechanical transformation task it was never suited to. Two defensive additions alongside it, since trusting the SDK's own `Timeout` property to actually bound a call turned out not to be reliable in practice: `AiJobService.Start` now takes an optional `hardTimeout` (wired to `Consts.Anthropic.RequestTimeout`) enforced via `CancellationTokenSource.CancelAfter` — independent of whatever the Anthropic client does or doesn't honor internally — and `AiJobService` now logs job failures/cancellations/hard-timeouts (`ILogger<AiJobService>`), closing a real observability gap: a job dying in the background used to be completely invisible in `journalctl`, discoverable only by directly querying the SQLite DB.

**Follow-up #3, same session — the *actual* actual root cause.** While chasing why `effort:low` still hadn't sped anything up, found that the Pi's production config had `Cedar__Anthropic__Model` explicitly overridden to `claude-opus-4-8` this whole time (a leftover from before this session, in `/etc/systemd/system/cedarclerk.service.d/override.conf` — a *second* drop-in file, alphabetically after `data.conf`, silently winning the same env var and shadowing Marty's edit there). Every "Haiku" assumption made earlier in this document was wrong: this was Opus 4.8, Anthropic's largest/slowest model, the whole time — which explains both the extreme latency and a ~600,000-token usage spike on the Anthropic console. Fixing the config to point at `claude-haiku-4-5` immediately surfaced a *cleaner, faster* failure: `400 invalid_request_error — "adaptive thinking is not supported on this model"`. Per Anthropic's own docs, adaptive thinking is Opus/Sonnet-only — Opus silently accepted `effort:low` (still slow, just less absurdly so) while Haiku rejects the `Thinking` config outright.

**Real fix**: removed `Thinking`/`OutputConfig.Effort` entirely from both providers, rather than tuning it — translation and AI-edit are pure structure-preserving text substitution, which never benefited from reasoning at any effort level, and the config is now portable across whichever model ends up set (no per-model support to track). `dotnet build`/`dotnet test` (362/362) clean.

`dotnet build`/`dotnet test` (362/362) clean. Not yet deployed.

`dotnet build`/`dotnet test` (362/362) and `ng build`/`ng test` (7/7) all clean. Not yet live-verified against a real large document or deployed.

## 2026-07-29 — five follow-ups from Marty's own use

**Emoji panel moved from popover to modal** — its grid genuinely scrolls (120 emoji, 4 groups, `max-height:320px`), and `PopoverComponent` closes on any document-level scroll (it can't tell the panel's own scroll from the page's), so scrolling the panel closed it. Same root cause ADR-057 already fixed for the Appearance panel. Date/time insertion moved alongside it for consistency, per Marty's own ask, even though its content is too short to hit the bug independently.

**The reaction/annotation block gets a real delete control.** It had none at all — the 💬 corner marker is decorative and `pointer-events:none` by design (I4). New NodeView on `annotation-node.ts` (dom/contentDOM split, mirroring `toggle-node.ts`) adds a corner × that **unwraps** the block — `liftTarget`/`tr.lift` on the range inside it — removing the wrapper while leaving its content in the document, rather than deleting both.

**Blog text alignment** (left/center/right/justify) — `@tiptap/extension-text-align` (pinned `3.27.2`, matching the rest of the TipTap suite exactly rather than the incompatible `3.29.x` latest), scoped to `paragraph`/`heading` only. Four toolbar buttons in the Text group, a new `align` `ToolbarButtonId`. `CedarToBlogHtmlRenderer.cs` reads `attrs.textAlign` on both node types and emits a whitelisted `style="text-align:..."` (never the raw value) — omitted entirely for `left` (the extension's own default) or an unrecognized value, so untouched documents render byte-identical to before. Telegram's renderers never read this attr — deliberately blog-only, Telegram has no alignment concept. 7 new `BlogHtmlRendererTests.cs` cases.

**Auto-translate/AI-edit 502 on a large document, root-caused.** Marty's real ~360-line document 502'd with an "overloaded" message on the very first attempt. `AnthropicTranslationProvider.cs`/`AnthropicAiEditProvider.cs` both set `MaxRetries = 0` on the SDK client — a deliberate choice (its own comment: the SDK default of 2 retries + a 10-minute timeout could leave a request looking hung for ~30 minutes) — but that means a *transient* Anthropic capacity signal (529 `overloaded_error`) got zero retry anywhere in the stack, surfacing immediately as a 502. Fixed with a narrow, bounded retry (up to 3 attempts, 2s/4s backoff) specifically for `AnthropicServiceException` where `ErrorType` is `OverloadedError` or `RateLimitError` — both return fast from Anthropic (not after a hang), so worst case is a few extra seconds, not the 30-minute problem the SDK default was disabled to avoid. Anything else (bad request, auth, a genuine 60s timeout) still fails immediately, unchanged.

`dotnet build`/`dotnet test` (362/362) and `ng build`/`ng test` (7/7) all clean. Not yet live-verified in a browser or deployed.

## 2026-07-28 — a local-only bypass for imports over Cloudflare's 100MB edge limit

ADR-058, `docs/DECISIONS.md`. The 100MB-upload investigation ended somewhere unfixable in app code (see the previous entry's three follow-ups), so the real next question was what to do about it. Two options were scoped: a general chunked-upload protocol (works for any size, any future user, but zero existing scaffolding to build on — realistically a few hours) versus a one-off local-only bypass (reuses the existing import logic via a small refactor, solves exactly today's need — a single ~148MB Notion export). Marty chose the local bypass now, chunked upload deferred to backlog idea #23.

**`POST /api/drafts/import-markdown-local`** — triggered over SSH directly on the Pi (`curl http://localhost:8080/...`), never through the tunnel, so Cloudflare's limit never applies. The existing `/import-markdown` handler's zip/markdown/image-matching/quota/persist logic (`DraftEndpoints.cs`) moved into a shared `ImportMarkdownZipAsync`, called by both endpoints — a pure extraction, verified with a live smoke test (small real zip through both endpoints, identical `201` + draft) before and after.

**The security design is the part worth reading closely** (full reasoning in ADR-058): a bare loopback-IP check would NOT have been enough — Kestrel binds only to `localhost:8080`, and Cloudflare Tunnel itself reaches the app over that same address, so *every* tunneled request also arrives at Kestrel from a loopback IP. The real gate requires loopback IP **and** `Host: localhost` together — the tunnel always forwards the client's original `Host` (`cedarclerk.mooexe.dev`), a property this deployment already depends on for the blog's own host-based routing. Verified live: a legit local call (`Host: localhost`) returns `201`; the identical request with `Host: cedarclerk.mooexe.dev` spoofed in returns `404` (not `403` — same "don't confirm the route exists" instinct as the admin gate).

`dotnet build`/`dotnet test` (355/355) clean. Live-verified against a local dev server (register a test account, drop a zip in `import-tmp/`, both the new and existing endpoints return matching `201`s with the draft correctly owned and titled from the `.md` heading). Not yet run against the real production import it was built for.

## 2026-07-28 — the form's one reference shape, and two follow-ups

**The private-post form reference, unified.** Design review's last unaddressed cross-cutting item: the form is defined on the Forms tab, assigned on the Posts tab, and re-picked in the Export modal — three real actions, but the latter two were near-identical bare `<select>`s with drifted wording. New shared `app-form-ref` (`shared/form-ref.component.ts`) is the one shape both now render — status line, language chips, the picker, an always-present link back to Forms — driven entirely by inputs/outputs so each caller keeps its own already-translated strings; behavior (DB1's "form attached but preset library empty" distinction, FI3.7's explicit-clear dropdown) lives in the component once instead of twice.

**Drafts button moved leftmost** in the editor topbar, ahead of the logo — it's the way out to every other draft, not a peer of the branding.

**New Draft dialog moved from the editor to `/drafts`.** Clicking "New draft" used to navigate to `/editor?new=1` immediately and open the dialog once already there, so a creation failure landed on a half-loaded editor page. Creation (and the dialog asking title/languages/tags/template/private/folder) now happens on `/drafts` itself — same shape `onImportCedarChosen` already used — and only navigates to `/editor?draft=<id>` once the draft actually exists. The shared constants it needs (`DRAFT_TITLE_MAX`, `EMPTY_DOC`, `NEW_DRAFT_TEMPLATES`) moved from `editor.component.ts` to `core/drafts.service.ts` so both pages import the same values; `editor.component.ts` keeps its own `newDraft(opts)` for the two cases that still need it (empty account on first load, deleting the last remaining draft), since those need the live TipTap instance the dialog itself never did.

**New standing convention (Marty)**: hints, notifications and error messages get a colored bordered box with an icon (`!`/`?`), never bare text; errors may use a toast instead. Applied it to every plain-text error on `/drafts` in passing (`.channel-error` → new `.error-box`, danger-toned, matching `.hint-bubble`'s shape) — not a repo-wide sweep, the rest of the app's plain-text hints are untouched for now.

**Markdown-zip import: real upload progress + a stall timeout.** Root-caused a report of the import "erroring out or hanging forever": the concrete zip was a Notion multi-part export (a zip whose only entry is another zip, `...-Part-1.zip` — Notion does this once an export gets large), which the backend correctly and immediately rejects with 400 "No .md file found inside the zip" once it doesn't find a `.md` at the top level — not a hang. The *actual* gap, confirmed by reading the code: `importMarkdown` had no timeout and no progress reporting at all (unlike the AI operations, which have both), so a genuinely slow-but-live upload of a large, correctly-shaped export (Notion exports run ~100MB+ of images) was indistinguishable from a dead one. Fixed: `DraftsService.importMarkdown$` now streams real `HttpEvent`s (`reportProgress: true`) instead of a Promise, and `/drafts` shows a real percentage bar (not the AI operations' pseudo-progress — an upload's byte count is genuine) with a Cancel button, same shape as the editor's auto-translate progress. `.pipe(timeout({ each: UPLOAD_STALL_TIMEOUT_MS }))` (60s) resets on every progress tick rather than capping total time, so a real multi-minute upload over the Pi's connection isn't cut off — only genuine silence (a dropped connection) is.

**Follow-up the same day**: live-tested against the actual `...-Part-1.zip`, and the new progress bar sat at 0% until the 60s stall timeout killed it — on an upload that was otherwise working. Root cause: `app.config.ts` had `provideHttpClient(withFetch(), ...)`, and the **Fetch API has no upload-progress mechanism in browsers at all** — `reportProgress: true` silently never emits a single `UploadProgress` event under the fetch backend, so the stall timeout (which only resets on a progress tick) had nothing to reset on and always fired at 60s regardless of whether the upload was healthy. Removed `withFetch()` — this app has no SSR (`docs/ROADMAP.md`), so fetch's usual reason for existing didn't apply here, and XHR (Angular's other backend) does support real upload-progress events. Also, per Marty's ask, the progress bar moved from an inline block into an `<app-modal>` (dimmed backdrop, closes-and-cancels on Escape/backdrop-click/×) — same shape every other modal in the app already uses.

**Second follow-up, same day — the real ceiling was never the app.** After the fetch→XHR fix, the reported symptom changed but didn't go away: stuck at ~1% with nothing in the browser console. Verified directly against production rather than guessing: `curl`-posted a 150MB dummy file straight to `https://cedarclerk.mooexe.dev/api/drafts/import-markdown` and got a bare **413**, in under a second, after only ~1.1MB of the body went out — no JSON `{error}` body (Kestrel's own 200MB cap and our `MarkdownZipMaxBytes` check both return our own JSON 400, not a bare 413), so this is Cloudflare's edge rejecting the request in front of the tunnel, before it ever reaches the Pi. Cloudflare's default max upload size is 100MB on Free/Pro plans (Business=200MB, Enterprise=configurable) — a 148MB Notion export is over that regardless of anything the app does. Not fixable in this codebase; two real options are raising the Cloudflare plan/upload-size setting (if the current plan allows it) or shrinking the export under the limit (compress or drop some images) before importing. Added a specific `413` branch to the import error handler (`t().drafts.errors.importTooLarge`) so this shows an actionable message instead of the generic "import failed" text next time.

**Third follow-up, same day — fail fast client-side.** Marty declined the Cloudflare Business plan ($250/mo — 200MB isn't much more headroom anyway). New `CLOUDFLARE_UPLOAD_LIMIT_BYTES` (100MB, `drafts.service.ts`) checked against `file.size` before `onImportMarkdownChosen` even opens the upload — an over-limit file now shows the same `importTooLarge` message instantly, instead of running the progress bar for real (however long that takes over the Pi's connection) only to hit the same 413 a minute later. This is a known, documented external constraint, not something derived from any response — there's no way to ask Cloudflare's edge what its limit is from inside the app.

**Also**: the current version now shows next to the "Cedar Clerk" wordmark, both in `app-page-header` and the editor's own topbar (new `VersionService`, one `GET /api/health` call at startup — the same endpoint the deploy script's own health check hits — rather than a second copy of `Consts.CurrentVersion` on the frontend). Requested mid-troubleshooting, so it's clear which deployed version is actually being tested.

`ng build` clean, `ng test` 7/7. Not yet live-verified in a browser or deployed.

## 2026-07-28 — three bugs from the newest Input.md sweep

Three independent fixes, all from the newest batch of reported bugs (reusing the same DB1-DB3 numbering as an earlier, unrelated sweep — see that sweep's own entry below for the drafts-table bugs it covered instead):

**DB1 — Posts Manager's contradictory "no forms yet" banner.** It showed whenever the reusable preset library was empty, even when the post itself already had its own attached form with real answers and charts sitting right below it. The two conditions aren't the same thing; the banner now only claims "no forms" when neither is true, with a separate, honest message for the "form attached, no saved presets" case.

**DB2 — dead paragraph-display status-bar button.** Same root cause as the historical "B12" bug: the `¶` marker CSS lived in `editor.component.css`, scoped by Angular's emulated view encapsulation, targeting ProseMirror's runtime-rendered DOM — which carries no `_ngcontent` attribute, so the rule could never match. Moved to global `styles.scss`, the established fix for this exact bug class.

**DB3 — publishing with zero channels connected threw a raw DB error.** Two compounding bugs: `editor.component.ts`'s `chatId` defaulted to the hardcoded string `'@testingandfun'` (a leftover dev value), which let the Publish button's "a channel is picked" gate pass even with nothing actually selected — so the request reached the backend instead of being blocked client-side. Once there, `SubscriptionPlan.ResolveOwnedChannelAsync`'s username lookup used `Equals(username, StringComparison.CurrentCultureIgnoreCase)` inside an EF Core query — untranslatable by the SQLite provider, so instead of returning null (clean 403) it threw at query time, surfacing to the user as exactly the kind of raw, DB-flavored error reported. Fixed both: `chatId` now defaults to `''`, and the lookup compares via `.ToLower()` (translates to SQL `LOWER()`). Added `SubscriptionPlanTests.cs` as a regression guard, since this failure mode only shows up against a real query provider, not by reading the code.

Version bumped to **0.9.13**.

## 2026-07-28 — Phase 9e continues: appearance, profile, polls, templates

Five items from the FI6/FI1/FI5/NF5/NF1 queue, in order. **FI6 (account settings) was skipped** — its own sub-item text had been lost when `Input.md` got overwritten before this session, and neither Marty nor this file's own notes retained it; only the pricing-restructure sub-item (already deferred separately) survived. Everything else landed:

**FI1 — Appearance panel.** The Light/Dark toggle now actually switches the theme — it used to only pick which theme's accent the swatches below would edit, with no visible effect of its own, which is exactly the "real ambiguity" Marty's feedback named. The panel moved to an explicit Apply for its preference controls (sheet width, typeface, font/line size, table size, the five checkboxes): still live-previewed instantly since that's the whole point of the side panel, but the save request no longer fires on every slider tick. Two more typefaces, and a global thin scrollbar — there wasn't a single scrollbar style anywhere in the app before this.

**FI5 — Profile settings.** Real inlined brand-mark icons for Twitter/X, Instagram, Facebook, YouTube, GitHub in the social-links row, replacing generic Lucide glyphs that didn't read as their brands (Lucide carries no logos at all — checked). Two new header-slot types (word count, view count). Post signatures can now differ per content language, following the same pattern already used for the cross-link labels — caught along the way: the `.zip` export had been quietly reusing the *primary*-language signature on every page in the archive, since no per-language mechanism existed until now.

**NF5 — Polls.** Blog-only, per Marty's explicit call — no Telegram surface at all, not even a degraded link. A new poll content block (question + options), one vote per anonymous visitor (same hashing approach the like/dislike reactions already use), results shown only after you vote. Not built on the form-preset entity as originally suggested — a poll is content anyone can vote on inline, a private-post access form is a different thing entirely.

**NF1 — Post templates.** A `Draft.IsTemplate` flag plus a new `/drafts` filter tab, exactly the "cheapest honest shape" already scoped for it. A template is written and autosaved exactly like any other draft; it's just filtered out of the main list once marked. No "duplicate into a new draft" flow yet — that's real, separate work.

Also: `TASKS.md`'s "known regression" note for `DB2.1`/`DB3.1` was stale — both were already fixed (verified directly in code this session), the file just hadn't caught up with `docs/ROADMAP.md`.

Version bumped to **0.9.12**.

## 2026-07-27 — one header, everywhere

Claude Design brief + spec came back for a header/navigation redesign covering the editor topbar and the four "secondary" screens (Posts Manager, Glossary, Settings, Admin). Those four had quietly drifted into two different visual styles — Posts and Admin had the glass material the editor topbar uses, Glossary and Settings had a flat solid fill instead — and none of them had real navigation buttons to each other. The only way from Glossary to Settings was opening the account popover.

All four (plus `/drafts`, which would otherwise have lost its only route to the others) now share one component, `app-page-header`: back-to-editor, logo, breadcrumb, the same glass material, and a nav row — Posts/Glossary/Settings/Admin — with the current page filled in accent, same shape as the editor topbar's own nav buttons. The account popover's Posts/Glossary/Settings links are gone; with a nav row on every screen they were pure duplication.

Bundled in two small, low-risk fixes the spec flagged along the way: the export/publish modal's shadow was hardcoded to the light-theme value even in dark mode (now reads `--shadow-lg`, which has a proper dark value), and a font-size token scale (`--fs-9`…`--fs-27`) was added for the new header to use — not a repo-wide sweep, existing hardcoded sizes elsewhere are untouched. Full reasoning in `docs/DECISIONS.md` ADR-052.

## 2026-07-27 — the console moves into the status bar

Marty reported the editor's fullscreen button as unclickable, "something is covering it". It was the debug console. Its host is a fixed full-width strip pinned to the bottom of the viewport, and the 27px `margin-bottom` added on 25.07.2026 to lift the closed tab clear of the status bar is *inside* that strip's box — so the strip covered the whole status bar and ate every click aimed at it. The margin had fixed how it looked without fixing what it did.

The host is now `pointer-events: none`, with the tab and panel opting back in, so nothing invisible sits over the bar again. On top of that, Marty's second point — the console belongs *in* the status bar and should slide out of it — is what the console now does: its open state and the host page's bar height moved into `DebugLogService`, the editor renders the toggle as a status-bar button next to fullscreen (with the in-flight/error badges), and the panel animates open above the bar instead of over it. Pages that have no status bar of their own still show the old floating tab, and so does the editor below 768px where the bar itself is hidden.

Also replaced `app.spec.ts`'s scaffold "should render title" test, which asserted an `<h1>` the app shell has never had and had been red for the whole life of the project.

## 2026-07-28 (later still) — the profile tab, properly

The single Save was necessary but not sufficient. Three separate faults were stacked on that screen, and only the first one was mine from this session:

**1. `loadLinkTexts()` called itself.** A blanket search-and-replace I ran while adding the per-language fields rewrote the primary-language branch of that method into a call to the method itself — infinite recursion. That is what made clicking a language "do nothing": the handler blew the stack before it changed anything. Caught by reading the method, not by the build, since infinite recursion is perfectly valid TypeScript.

**2. A lapsed Pro plan made the profile unsaveable forever.** The endpoint rejected *any* request carrying a third header slot when the plan didn't allow three. An account that had once been Pro, with a third slot still stored, therefore failed every profile save — on a field the user wasn't editing, with an error message about header slots regardless of what they had actually changed. The gate now applies only to *setting* the slot: an unchanged stored value passes through. That isn't a loophole, because `PlanLimitations` decides what actually renders, so a lapsed account still doesn't get three slots on its blog.

**3. The language buttons had no styles.** `.pill` is styled in the posts manager, and Angular's emulated encapsulation keeps that stylesheet to that component, so here they rendered as bare browser buttons with no active state — a second, independent reason clicking a language looked inert.

**And the design was wrong regardless**: switching language fired a save. Marty said as much — it "just triggers api/auth/profile". Every language is now held locally and the single Save sends them all in one request, so clicking through languages makes no request at all and a failure can never leave half the languages written.

## 2026-07-28 (later) — one Save for the profile tab

Marty: adding a language to the cross-links broke the social fields and the header slots, with "failed to save header slots" on screen.

The per-language switcher was the trigger, but not the cause. `/api/auth/profile` takes the **entire** profile in one request, and the page sent it from two buttons carrying different subsets: the header-slots button omitted the social URLs, the social button omitted the cross-link wording. Each therefore wrote null over whatever the other one owned. That was already true before this session — saving one section had always been quietly wiping the other — and making the language switcher save on every click turned an occasional loss into one per click.

One Save for the whole tab now, sending every field it owns, sticky at the bottom so it stays reachable while the sections above are edited. The error text was also wrong in a way worth naming: every failure of that request said "failed to save header slots", because that was the fallback message of whichever button happened to send it.

## 2026-07-28 — five follow-ups from Marty's review

**A language switcher on the registration gate.** Since the gate became per-language, a reader of a private post had no way to reach the version written for them: the post body they would normally switch languages from is behind that very form. The gate now lists the languages the post has a form for, and the submission carries the language so the server validates against the form the visitor actually saw.

**The Glossary is a topbar button**, next to Posts Manager and Settings, instead of living only in the account menu two clicks away from the screen where terms get written.

**Six language chips plus LIVE plus a lock is a long line, and it was breaking two layouts.** In the Posts Manager list the chips ran past the row's edge: the row wraps now, and the language chips collapse past the third into a "+N" carrying the rest as its tooltip — six two-letter boxes in a list column say little more than three and a count. Above the writing area the row holding the language tabs, the add button, the re-translate/delete pair, the tag row and the folder picker never wrapped, so the Appearance panel's narrow sheet width pushed it off the side; it wraps now.

**Cross-links can differ per language.** `LocalizedTextMap` (Core, 10 tests) with the same split `RegistrationFormSet` uses — the primary-language wording stays in its own column, the rest go into a JSON map beside it, so no existing row needed migrating. Settings edits one language at a time and flushes what is typed before switching, the way the forms tab already does.

### Semi-public posts

A new checkbox in Export's blog section: **a private post can be listed on the blog index anyway**, with a lock on its card, still opening the registration form rather than the article. That is the shape Marty asked for — posts that advertise themselves and collect a registration to be read.

Two deliberate limits, both about not handing out through a side door what the gate exists to withhold:

- **No excerpt on the card.** The card carries the title, the date, the tags and the lock; the excerpt is the one part of it that would be actual content. Easy to reverse if the teaser turns out to be the point.
- **Not in RSS.** An RSS item carries an excerpt and is pulled by readers that never see a gate.

## 2026-07-27 (latest) — the glossary

Idea #11, specced by Marty in one paragraph and built the same session: a page holding every term, each with a description, other spellings and an optional image; published text is scanned, terms are marked, and hovering or tapping one shows the description.

**The scan runs at blog render time**, against the owner's terms in the language being shown — not in the editor. Marty's wording was "при публикации", and marking as you type would mean a TipTap decoration plugin racing the autosave for something no reader ever sees.

Four rules had to be decided rather than just coded, and each is a judgement about reading rather than about code:

- **Only the first occurrence per page is marked.** An article that uses a word twenty times would otherwise become a page of dashed underlines. This is the call every encyclopaedia makes.
- **Never inside code.** A term appearing in a code sample is code, not prose.
- **Never inside a link.** Nesting the tooltip in an `<a>` puts two different destinations under one word.
- **Aliases instead of stemming.** Russian inflects: a canonical "рендерер" misses "рендерера" and "рендереру". A comma-separated list of forms beats guessing at per-language stemming rules, and it is honest about what it does.

The scanner is a separate, tested unit (19 tests) because of *where* it sits: it runs on text that has already been HTML-escaped and injects markup into it. That means the description has to go into its attribute through attribute-escaping, the matcher has to skip HTML entities whole so it can't mark "amp" and split `&amp;` in half, and the page script writes the description with `textContent` and never `innerHTML`. Tests pin all three, plus the "a description cannot break out of the attribute" case.

Terms are per content language, since the same word needs a different explanation depending on which language's version of a post the reader is on — a Russian description under an English article would be worse than no tooltip. Images go through the ordinary asset upload and are restricted to `/media/...`, the same rule the avatar upload uses: accepting an arbitrary URL would let a glossary tooltip point the blog's own chrome at someone else's server.

**Not built, deliberately**: the original backlog line also asked for inline highlighting in the editor and an auto-detect pass before posting. Neither is in Marty's spec, and both are separately scoped work.

## 2026-07-27 (latest) — a pass over the backlog, by category

Marty asked for everything in the backlog touching forms, then posts, then stats, then the admin panel, then new editor features. Two of those five turned out to be mostly answered already, which is the recurring shape of this backlog.

### Forms (FI4)

**A form preset now has a language, and a post can carry one form per language.** `FormPreset.Language` plus `Draft.RegistrationFormTranslationsJson`, with `RegistrationFormSet` in Core deciding which form a given reader gets. It is deliberately *not* one map holding every language: the single-language post is the common case, and its form stays exactly where every existing row, endpoint and test already looks for it. Ten unit tests pin the picking, the fallback and the "a corrupt blob must not take a published page down" rule.

Two things came out of that which were plainly broken before: the private-post gate always rendered in the primary language, so an English reader of a private post was greeted in Russian even when an English form existed; and the gate's own chrome existed in exactly two languages, four short of the six the app has had since NF2. Both fixed. What is **not** translated is the questions themselves — a form's wording is the owner talking to their reader, and machine-translating that would be putting words in their mouth.

The editor for it stopped being a flat stack of inputs, checkboxes and outlined rows with nothing saying what belonged to what: three labelled blocks, and each question is a card carrying its own type, options and required flag.

**N6 and N11 were already built.** Server-side name validation and the Telegram DM on a form submission both exist in the code. The backlog rows were stale, not the features.

### Posts

**Idea #4 — the draft's name and the article's headline are now two fields.** `Draft.ArticleTitle`, null meaning "same as the name", used by the blog page, the post cards, RSS and both file exports. The per-language half of that item turned out to already be done: `DraftTranslation.Title` has always been that language's own article title.

**Idea #8** — a blog card emitted `tags[0]` and silently dropped every other tag, while the single-post page had always shown them all. **Idea #3** — tags can be renamed and deleted across every draft that carries them, from a `[manage]` mode on the shared picker; a rename onto an existing tag merges rather than duplicating, and the blog follows with no extra step because it reads `Draft.Tags` directly. **B17** — the Telegram signature is bold; a linked signature is bolded *inside* the link, since Telegram renders a bold run within a link but not a link within bold.

### Stats — nothing open

`N9` shipped the custom range, `I8` widened it and labelled the notches, and `B1` was superseded by `N9`. Checked rather than assumed; there is no open stats work in the backlog.

### Admin — one gap, now closed

The panel's five steps were already complete. The single thing `docs/admin-panel-scope.md` still listed was the audit log having no paging: it showed the newest 100 entries and nothing could reach the rest. It pages now (`?skip=`, `hasMore`, a "Load more" button). Retention stays deliberately absent — an append-only log that starts halfway through is missing exactly what someone would go looking for.

### Editor

**B9** — the emoji panel had 40 emoji in one unlabelled grid that overflowed the popover to the right. Four captioned groups now, about 120 emoji, and the popover scrolls instead of growing. Hand-picked rather than a full Unicode table on purpose: a complete picker needs search, and search needs emoji names in six UI languages.

**B13** — a status-bar toggle that reveals where a block actually ends. Paragraph marks only, and that limit is real rather than laziness: in a contenteditable, spaces and tabs can't be drawn without either inserting characters that would end up in the exported text or fighting the browser's own whitespace handling.

## 2026-07-27 (latest), Phase 9e — FI2: the export window does only export

Eleven sub-items, but one rule underneath them, and it is Marty's: **"По хорошему Экспорт управляет ТОЛЬКО экспортом"**. Everything that was really *managing an already-published post* left the window.

**Unpublishing and the scheduled-post list moved to the Posts Manager.** Scheduled sends are shown per post rather than as one global list — every scheduled post belongs to a draft that is already in that list, so nothing became harder to find, and a post with a pending send now carries a ⏰ chip. What stays in Export is sending, and re-sending: with the blog page already live, the Publish button reads **Update**, because rewriting the page is the export, not the management of it. A Telegram post can't be edited after sending, and the hint under the button says so instead of pretending otherwise.

**One publish button.** Setting a time no longer reveals a second Schedule button competing with Publish — it changes what Publish does. The quick presets and the datetime field stayed; the list of what's already scheduled went with the rest of the management.

**Languages became checkboxes**, one Telegram message per ticked language, sent one after another so a rate limit part-way through leaves what already went out visibly sent. Unticking the last language is refused rather than quietly meaning "publish nothing".

**Layout**: channels folded into the Telegram destination behind a disclosure — they only ever meant Telegram, and connecting a channel is rare next to picking one. Invitations and Watermark became sections of their own instead of blocks nested inside the blog destination; who may read a post is not a property of publishing it. The form choice is a dropdown with an explicit "no form", matching what the Posts Manager already had, and every explanatory line became a bubble with an icon so advice stops reading as body text.

**`.zip` export.** New `GET /api/drafts/{id}/export-zip`: a page per language plus the media they reference, rendered with `"."` as the media base so each asset resolves to `./media/...` inside the archive. It replaces the per-language `.html` download, which produced a page whose images all pointed back at blog.mooexe.dev — a saved copy that worked only while the blog was up.

**A green confirmation with the post's links**, held ten seconds. Inside the modal rather than over the page, since the window stays open after publishing and the message is the answer to the button that was just pressed.

## 2026-07-27 (latest), Phase 9e — FI3 closed

The three items left in the Posts Manager group.

**Tags and folders became shared components** (FI3.2/FI3.3). There were three takes on "pick a tag" and three on "pick a folder" — the editor's popovers, the new-draft dialog's pill rows, the posts manager's text-field-plus-pills — so the ask was less about looks than about the same thing being reachable everywhere. `TagPickerComponent` and `FolderPickerComponent` now serve all four screens. Both carry an `inline` mode: an `app-popover` nested inside `app-modal` doesn't position, which is exactly why the new-draft dialog grew its own pill rows in the first place, and inline keeps one implementation rather than forking around that.

The lists behind them moved into `FoldersService` and `TagUsageService`, which buys two things the copies couldn't: a folder created in the editor shows up in the drafts table without a reload, and **creating, renaming and deleting folders now works from anywhere** instead of only from the `/drafts` filter menu — that menu went back to being a pure filter. The picker loads its list on init rather than on first open, because the trigger displays the folder's *name*; loading on open is precisely what made a filed draft read as unfiled until clicked (`IB6`).

**The "Reactions & comments" tab is gone** (FI3.5). `CommentsComponent` takes an `onlyDraftId` and renders under the selected post, dropping the group title and the cross-post totals when scoped — both only mean something with several posts on screen. It's one instance filtered client-side, so switching posts costs no request. Old links to `?tab=feedback` resolve to the Posts tab instead of falling through, and the new-feedback badge moved onto that tab.

## 2026-07-27 (late), Phase 9e — second Input sweep

Marty rewrote `Input.md` again: ~60 items across 6 new features, 6 improvement groups and 3 bug groups, confirmed as not overlapping the earlier lists. Imported to `docs/BACKLOG.md` with a cost note per item, since several read as one line and are not.

**I was wrong about email being blocked.** I wrote that NF3 (email confirmation) couldn't be built because the Resend key 401s — taken from a `TASKS.md` note dated the previous day and not checked. Marty's dashboard shows the domain verified and `POST /emails` returning 200. Corrected; email is not a blocker for anything.

### Bug pass (DB2, DB3)

**The inverted column resize** (DB2.1) was real and mine: the handle sat on each column's *left* edge while the drag maths grew the column as the pointer moved right, so every resize felt backwards. The handle belongs on the right edge, where the divider you drag rightward widens the column to its left — which is both what the maths does and what every other table does.

**Flag emoji were the wrong call** (DB3.1), and that call was also mine. I picked them for the language pickers (I1/I17) reasoning that a flag is recognisable to someone who can't read the current language; on Windows that is simply false, since it ships no regional-indicator glyphs and renders the pair of letters instead. Two-letter codes now — what the editor's own content-language tabs already used, identical on every platform, and they scale to six languages where sourcing six flag SVGs would not.

Also: default sort is creation date (DB2.2) — the one order that doesn't reshuffle under you the way "updated" does; the fixed columns widened so the 1fr Title column stops hogging the row (DB2.3); and draft names are bounded to 1–64 characters in both the dialog and the topbar, checked on Enter too (DB2.6).

### NF2 — six content languages

RU, EN, DE, FR, ES, JA. The server turned out to need almost nothing: `DraftTranslation` was already keyed by language string and `ITranslationProvider.TranslateAsync` already took a target language, so expanding `Languages.TranslationLanguages` carried the whole backend.

The editor was the work. It had ~20 places hardcoded to English — a single `enMeta` signal, `enStale()`, `startEnVersion`, `deleteEnVersion`, `autoTranslateEn`, and literal `'en'` in save, load and export paths. Those became a `Record<string, TranslationMeta>` keyed by code, with the language passed as a parameter throughout. Tabs render one per language that exists plus a picker for the rest, and the export modal, blog badges and static-HTML links all loop over what exists instead of naming EN.

One deliberate simplification: the RU-side diff gutter compares against **one** translation's sync snapshot, since "what changed since translating" has no single answer once several translations exist. It follows whichever translation tab was opened last, defaulting to the first.

For the UI-language half, NF2 asked for the slots without the translations, so a locale with no dictionary falls back to English rather than shipping ~650 untranslated keys per language.

## 2026-07-27, Phase 9c (Input.md sweep) — bug pass

Marty's `Input.md` (32 items: 19 improvements, 9 bugs, 2 removals, 2 features) imported into `docs/BACKLOG.md` with a dedup verdict per item — five turned out to be duplicates of open `B`/`N` entries (`I14`≡`B15`, `I18`≡`B20`, `IB4`≡`B12`, `IB7`≡`B11`, `IF2`≡ backlog idea #12) and four are refinements of things that shipped in the previous two days. Scoped as Phase 9c in `docs/ROADMAP.md`, bugs first.

Seven of the nine bugs fixed. Three had a root cause that was not what the symptom suggested:

- **The folder label** (IB6) looked like state being lost on the way back from Settings; it was the folder list loading lazily on first opening the *picker*, while the *label* needed the same list to resolve a name. Any freshly-loaded draft therefore read as unfiled until you clicked the thing that would have told you otherwise. Loaded at editor init now, and an unresolved id shows `…` instead of claiming "no folder".
- **The diff gutter** (IB7, the never-shipped `B11`) was drawn from correct measurements in the wrong coordinate space: marker offsets came from `.ProseMirror`'s top but the bars are positioned inside `.sheet-wrap`, so every one of them sat exactly the sheet's 28px top padding too high — 40px with the ruler on, which is why it read as inconsistently above *or* below.
- **The dead profile button** (IB9) was reproducible by reading: the avatar was a real popover in the editor and a plain `<span>` everywhere else. It's now one shared `AccountMenuComponent` used by all four pages, which is also what gave `/posts` a logout — it had neither that nor a back link, so reaching it meant editing the URL to leave (IB8).

**The ruler is gone** (IB4/`B12`). It was never an overlay on the writing area: a 12px decorative strip rendered as a sibling *above* the sheet, which is exactly why it looked like it sat underneath. With no margins or tab stops in this editor for a ruler to control, Marty's "remove it if it can't be fixed" branch was taken outright rather than rebuilt.

Two translation misses from the ADR-050 sweep: the paragraph-format dropdown's trigger label (IB1 — the menu items were translated, but the label came from a function returning a raw English string that the active-state checks also matched against; it returns a block level now) and the whole re-translate flow (IB2 — dialog body, button, both tooltips, and the delete-translation confirm). IB2's other half was layout: the progress bar carried the sheet's max-width without `auto` side margins, pinning it to the far left of the column.

**IB3 (RU load marks EN stale) is only partly addressed and is not closed.** Two genuine defects on that path were found and fixed — `DraftsService.update()` discarded the server's `updatedAt`, so the client re-stamped the RU version from its own clock and any laptop-vs-Pi skew lit the stale dot by itself; and `enStale()` compared the two timestamps as raw strings, which flips on a trailing `Z` or a differing fractional-second precision. Both now use the server's value compared as instants. What is still unexplained is why an autosave fires at all about a second after a RU load: the timing matches the 1.2s debounce exactly, but `setContent` runs with `emitUpdate: false`, `resetHistory` goes through `view.updateState`, and no custom extension appends a transaction. Needs a live reproduction.

Not started: IB5 (blog comment form). `dotnet test` 278/278, `ng build` clean. Nothing here is live-verified in a browser yet.

### Admin panel — scoped, then Step 1 built (IF2)

Researched the code before writing anything; the scoping lives in `docs/admin-panel-scope.md`. Three findings shaped it:

- **No role concept existed at all** — not "unused", absent: `AddIdentityCore` is called without `.AddRoles(...)`, so `AspNetRoles`/`AspNetUserRoles` don't exist and there isn't one role check in the codebase.
- **Invite codes are a single config string**, and nothing records which code a user registered with. Creating codes needs a new entity; *attribution* needs new data and **cannot be backfilled** — the two existing accounts came in on the shared code and there is no record of it.
- **61 owner-filtered queries** across 8 endpoint files. The obvious implementation — "if admin, skip the filter" — would put a cross-tenant leak one missed call site away.

Marty's answers: bool not roles, no user deletion, no editing others' posts, attribution matters (so an admin will be able to set it by hand for the pre-existing accounts), and keep the config invite code as a fallback.

**Step 1 shipped**: `ApplicationUser.IsAdmin` with an additive migration; a config bootstrap from `Cedar:AdminEmail` that **grants only and never revokes**, so removing the setting can't silently lock the panel out; and a separate `AdminEndpoints` under `/api/admin` rather than any bypass in the existing endpoints — the security property is now one checkable sentence, "everything under `/api/admin` is admin-only, everything else stays owner-scoped". The check sits on the route **group**, so a route added later can't ship ungated, and it returns **404 rather than 403**: an admin panel that answers "wrong, but it exists" tells an ordinary account something it has no business knowing.

The page itself is the shell plus what's already knowable — headline counts and a user list with plan, Telegram link, content counts and join date, flagging lapsed plans where the stored tier and the effective one disagree. `/api/auth/me` gained `isAdmin` purely so the entry point can be hidden; that is convenience, not the gate.

**Nothing here is covered by automated tests** — the project has no HTTP-level integration tests, so the gate was verified by reading and needs a live check: a non-admin should get 404 from `/api/admin/users` and a redirect from `/admin`. And `Cedar:AdminEmail` has to be set on the Pi before the panel is reachable in production (`docs/integrations-setup.md` §3b).

### Cross-link wording (I15) and avatars (IF1) — Phase 9c closed

**Cross-links** are two profile fields now, falling back to the built-in text when blank. That is a deliberate deviation from the item, which asked for it at export time: this is branding that reads identically on every post, so retyping it at each export would be a chore rather than a choice. It lives in Settings → Profile and saves with the rest of the profile.

**B18 turned out to be already built.** The YouTube link text in Telegram has always fallen back to the node's caption — "Watch on YouTube" is only the default when the caption is empty. A second field would have meant the same thing twice, so the caption's placeholder now states its dual role instead.

**Avatars** reuse the ordinary asset upload rather than growing a second pipeline: the file goes through `POST /api/assets` with its existing type whitelist, storage quota and public `/media` serving, and `POST /api/auth/avatar` only records which uploaded image it is. That endpoint **rejects anything not starting `/media/`** — accepting an arbitrary URL would let a profile point the app's own chrome at someone else's server. Null keeps the initial-letter placeholder the app has always drawn.

With these, **every item from all three brainstorm lists and the Input sweep is closed**.

### Registration reported failure on every successful signup

Marty hit this creating an account with a fresh invite code: an error appeared, but the account existed and the code had been consumed. Not a double-submit — deterministic, and it had been true of every registration.

`AuthService.register` posts to `/api/auth/register`, then calls `refresh()` and decides success by whether `/api/auth/me` now returns a user. But the register endpoint never signed anyone in, so `/me` answered 401 and the client reported "Registration failed" while the server had done exactly what it was asked. Invite codes made it worse rather than causing it: seeing the error, the natural move is to try again, and on a single-use code the retry then genuinely fails — which is what it looked like from the outside.

Registration signs the new account in now, with the same `isPersistent` the login endpoint uses. That is the behaviour you'd expect anyway — you are logged in after signing up — and it makes the client's success check true instead of accidentally right.

### Admin panel Steps 4 and 5 — cross-owner posts and reporting

Step 4 is a read-only list of every post across owners: owner, state, views and comments, and links out to the live blog and Telegram post. Nothing on that tab writes — editing other people's content was ruled out during scoping and stayed out.

Step 5 is reporting on data that already existed: payments from the `Payment` table with a revenue total that counts **completed payments only** (a failed or pending row is not money), plus per-user storage and AI calls. No new collection was added for any of it.

The panel outgrew a single scroll at this point and gained a tab strip, matching the Posts Manager and Settings — the app's three secondary pages now navigate the same way rather than each inventing something. The admin entry point also joined the editor topbar next to Settings, shown only to admins.

**Marty live-verified the gate** on the Step-1/2 build: 404 from `/api/admin/users` for a signed-in non-admin, `/admin` redirects, self-targeting refused. That closes the one check the scoping doc flagged as impossible to automate here.

### Admin panel Step 3 — real invite codes

Registration checked one shared string from configuration; it now looks up a real `InviteCode` row first and falls back to `Cedar:InviteCode`, which stays deliberately, so a database problem can't lock registration out entirely. Codes carry a label, an optional expiry and an optional use cap, and a limited code's use is counted **after** the account is actually created — a failed registration shouldn't burn one.

Codes are **deactivated, never deleted**. Accounts point at the row through the new `ApplicationUser.InviteCodeId`, so deleting a code would silently erase the attribution of everyone who joined through it — the same reasoning that keeps user deletion out of the panel entirely.

Attribution can also be **set by hand**, which is the answer to the problem found during scoping: the two pre-existing accounts came in on the shared config code and there is no record of it, so it can never be recovered automatically. The audit entry says "set by hand" — an admin's assertion about history should not read the same as something the system observed.

The "is this code still usable" test briefly existed twice, in registration and in the panel's display flag. That's the shape of bug where the copy that drifts is the one guarding registration, so it moved into `CedarClerk.Core/InviteCodeRules.cs` with tests pinning the edges that actually matter: a cap of 5 admits exactly five accounts, and an expiry closes the code at the instant itself rather than a tick later. 308 tests green.

### Admin panel Step 2 — user management, with the audit log built in

Per-user actions on an expanded row: set plan tier and expiry, reset trial, lock/unlock, grant/revoke admin. Locking uses Identity's own `LockoutEnd`, so the ordinary sign-in path enforces it and there is no custom check to get wrong. A blank expiry on a paid tier is a manual grant that never expires — reusing the meaning `ApplicationUser` already documents rather than inventing a second convention for the same field.

**Self-targeting is refused server-side** for both lock and admin rights. There is exactly one admin; a self-lockout would have no second admin to undo it and the fix would be hand-editing the database on the Pi. The UI disables those buttons too, but only so the reason is visible — the refusal is on the server.

**The audit log was built now rather than deferred.** It was written up as "decide before Step 2"; the decision is that a log starting halfway through is missing precisely the changes anyone would later go looking for. New `AdminAuditEntry` table (nothing existing touched), written by every mutation, newest-first in the panel. Actor and target emails are denormalized deliberately: a log that stops making sense once the rows it points at change is not a log.

Still not included, per Marty's answers: deleting users (locking is the reversible equivalent) and editing other people's posts.

### Settings split (I12), zoom removed (IT1), toolbar customization kept (IT2)

**Zoom is gone** (IT1) — signal, both buttons, the `%` readout, the `--zoom` variable the sheet font size was multiplied by, and both dictionary keys. The Appearance panel's font-size slider covers what it was reaching for.

**Toolbar customization stays** (IT2, declined). It had also stopped being a standalone question: once I14 moved it into the editor's Appearance panel, deleting it would have gutted half of that panel rather than just removing a settings section.

**Settings split in two** (I12). I14 had already taken appearance and toolbar out, so the split landed as **Profile** — the profile card, header slots and social links, i.e. the author and what publishes under their name — and **Account** — language, plan, connected services. The account menu deep-links to the profile half, which is the "opened by clicking the user" part of the ask, while the topbar's Settings button still lands on the page generally.

Sections are guarded by tab individually rather than physically reordered. They were already in the right relative order within each tab, and moving large blocks with a script is precisely what silently deleted the Language section earlier today — not a mistake worth making twice in one day.

### Low-priority sweep (I3, I5, I6, I8, I13, I17) — and a regression caught

Six of the seven Low items.

**Toolbar tooltips now name their shortcut** (I3). Every combo was read off TipTap's actual key bindings in the installed packages rather than written from memory — a tooltip promising a shortcut that doesn't fire is worse than no tooltip — so buttons without a binding are deliberately left alone. "Mod" resolves to ⌘ or Ctrl the same way the binding does, and the `(Ctrl+Z)` that was hardcoded into the undo/redo dictionary strings came out, since it's supplied now.

**Table insert stopped being fixed** (I5) — it was 3×3, not the 3×2 the note said. The size lives in Appearance, bounded at 10×10 and clamped on read as well as on write, because the preference blob is editable through the API.

**Autofill on the private-post form** (I6). This page is public and unauthenticated, so there is nothing to prefill from server-side; what makes autofill work is naming the fields the way browsers and password managers expect, and `name` matters as much as `autocomplete` — a field with neither is invisible to most heuristics. The social field deliberately stays `type="text"`: `type="url"` would add browser validation stricter than the server's own rules and start rejecting a bare `@handle`.

**The stats slider became readable** (I8): 200px of track with six unlabelled 1px ticks marked something without saying what. It's 420px now, taller, and the notches carry their day counts — as click targets too, since a value worth marking is worth jumping to.

**Fullscreen** (I13) is real browser fullscreen rather than a CSS "hide the chrome" mode, kept in sync with a `fullscreenchange` listener because Esc leaves fullscreen without going through the button. **Flags on the settings language picker** (I17), beside the endonyms rather than replacing them — names stay in their own language, which is the one list nobody needs translated.

**Regression found and fixed while working on I17**: the settings page had *two* identical `<!-- APPEARANCE -->` comment lines, and the script that removed those sections for I14 matched the first one — silently taking the Language section with it. The language picker had been missing from Settings in the previous commit. Restored.

I15 is left: unlike the rest of this block it needs a stored setting and touches both renderers, and belongs with the open B18 (custom YouTube link text) — the same feature applied twice.

### Posts Manager restructure and three Appearance-panel bugs

Six items from Marty's live review of the previous deploy.

**Forms stopped being a property of a post.** The Forms tab used to make you pick a private post and then edit *that post's* form, which framed a form as belonging to a post; it doesn't. The tab is now purely a form authoring screen — a list of forms on the left, one editor on the right, no post mentioned anywhere — and what it authors are presets. A post picks one on the Posts tab, where the preset is copied onto it (N12's rule, unchanged: editing a form later can't rewrite a post that already used it). Presets are created immediately rather than held as a local draft, since a preset with no id has nowhere to save to.

The Posts tab gained the other half: a tag picker over the tags already in use instead of retyping them into a text field (the free-text input stays for tags that don't exist yet), and the form selector described above.

**Feedback is grouped per post** with a per-group "show all". A flat stream answered "what's new" but not "what happened to this post", which is the question the tab exists for. Reactions needed a server-side split to do this — `/api/comments` now returns `reactionsByDraft` alongside the running total — and a post with reactions but no comments still gets a row, because 20 likes and no comments is exactly as worth seeing.

**Three bugs in the day-old Appearance panel**, all found by Marty using it:

- **Line height did nothing.** `.sheet` carries the preference as `--sheet-line-height`, but `.tiptap` — the element the text is actually in — hardcoded `line-height: 1.6` and silently won that cascade. It inherits now, which is how font-size was already written, and why *that* slider worked.
- **Reordering groups within a toolbar row did nothing.** The layout model stored only which groups were in row 2, not their order, and the editor rendered them through a fixed chain of `@if` in hardcoded sequence — so dragging reordered a list nothing read. `ToolbarLayout` now carries both rows as ordered lists, the editor renders them by iterating that order, and a normalizer keeps stored layouts (which predate `row1Groups`) and any newly-added group from falling out of the toolbar.
- **The reset button sat under the debug-console tab**, which is fixed to the bottom-right. The panel's scroll column gained enough bottom padding to clear it.

Also removed the toolbar-customize button from the editor toolbar — it linked to `/settings#sec-toolbar`, an anchor that stopped existing when I14 moved that section into the panel.

### Audio clip names (I16) and the appearance panel (I14)

**I16 turned out not to need a migration.** The plan recorded for it assumed a name field on `Asset`; the actual mechanism is `InputMediaAudio.Title`, which is what Telegram labels the player with — without it the player falls back to the filename in the URL, i.e. the generated `asset_<guid>.mp3`. And the name belongs to the *insertion*, not the file: the same asset can legitimately be posted twice under different names. So it's a `title` attribute on the TipTap `audio` node, carried through `RichAudioBlock` into the Blocks renderer, with a second input in the node view above the caption (title names the file in Telegram's player, caption is body text under it — two things that both looked like "the label"). Blank stays null rather than becoming an empty title, which would label the clip `""`. The blog shows it too, since a bare `<audio>` element there is exactly as anonymous, and it escapes like all author text.

**Appearance and toolbar customization left the settings page** (I14/B15, raised three times across the brainstorms). They now live in a panel beside the writing sheet: collapsed it's a vertical handle, open it's a 268px column. Beside rather than over the sheet, deliberately — the entire point is watching the sheet change while dragging a slider, which an overlay would hide. Nothing had to be built to preview anything; the sheet *is* the preview.

Extracted rather than copied: `/settings` dropped both sections and carries a pointer to the editor instead, so each control still has exactly one home — the same rule I11 applied to navigation. Settings lost about 110 lines of TypeScript and 130 of template along with its drag-drop and toolbar imports. The button catalog became collapsible `<details>` groups, which a narrow column needs and a full-width settings card didn't.

That leaves I12 (splitting Settings) smaller than when it was written: appearance and toolbar are already out, so what remains to split is profile / header slots / social / billing / integrations.

### Middle-priority sweep (I1, I2, I4, I10, I11, I18, I19)

Six of the nine Middle items, all frontend.

**Navigation moved back into the topbar** (I11). Posts Manager and Settings had lived only inside the account popover since B22; they're real buttons next to Export now, styled the same but neutral so Export stays the only tinted control in the row. That reversal also settles B6 — "two entry points to Settings" — in favour of the topbar rather than the popover: the shared account menu takes `[showNav]="false"` on the editor, so no single screen offers two routes to the same page, while the other pages keep the popover links they rely on. The drafts button stopped being a hamburger, which reads as "menu" and said nothing about drafts (I18).

**A language picker on login and register** (I1), which was the one place the UI language couldn't be changed at all: the Settings picker needs an account, and picking a language is the first thing someone who can't read the form wants to do. Flags rather than language names — that's what a reader who doesn't speak the current language can actually recognise, which is also I17's point, delivered where it matters most. Registration pushes the choice onto the new profile so Settings opens already holding it, best-effort so a failure there can never block a signup.

**Paragraph numbers became legible** (I2): 10px in the faintest text colour halfway across the margin, now 12px in `--t2` in a gutter hugging the sheet's left edge, right-aligned so multi-digit numbers line up against the text. The "would be nice" half of that item shipped as well — a new appearance flag rules off each block. Per-block borders rather than a ruled-paper background, because a repeating gradient cannot stay aligned once line-height, headings and images vary.

**Reaction blocks stopped impersonating code blocks** (I4). The old solid-bar tinted panel is the visual language of a quote; it's now a dashed outline with a 💬 marker, distinct from both blockquote and `pre`. The marker is an emoji in CSS `content` deliberately — no text means nothing to translate.

**The drafts table got its width back** (I10): capped at 1080px, it left most of a wide monitor empty while Title — the column that actually needed room — was starved. Raised to 1600px rather than made fully fluid, since a row spanning a 4K display is unscannable, and N1's grid hands the extra space straight to Title.

**Form answers moved to the posts tab** (I19), where "what happened with this post" already lives. The forms tab keeps the form's definition — building it and reusing it as a preset — which is a different job, and now says where the answers went. This partly walks back N10's tab layout, which was flagged when the item was imported.

Still open in this block: I16 (custom audio clip names) needs a backend field and a migration rather than being a frontend change like the rest, and I12/I14 are held behind one design decision — see `TASKS.md`.

### Blog comments (IB5) and form presets (I9)

**The reply target that couldn't be cleared was a CSS bug, not a script bug.** `cancelReply()` was correct and wired correctly; `.comment-reply-indicator { display: flex }` simply overrides what the `[hidden]` attribute does, so the indicator stayed on screen whatever the script set. The same rule was quietly breaking a second thing nobody had reported: `.comment-load-more { display: block }` meant "show more comments" was offered even when there were none. Fixed once, globally, with `[hidden] { display: none !important }` in the blog stylesheet, so the next element scripted through `hidden` can't reintroduce it. This is the same shape as the paragraph-numbers bug from the day before — a stylesheet quietly defeating behaviour the code got right.

The comment form was three stacked full-width rows (name, textarea, a full-width Send slab) for what is a secondary element on the page; it now leads with the textarea and puts the optional name next to a normal-sized Send button on one row. A renderer test pins the class names the page script queries — nothing at build time connects the markup in Core to the script in `BlogEndpoints`, so a layout edit is exactly the change that could quietly break posting a comment.

**Form presets became independent (I9).** They were only reachable by first selecting a private post and opening its form, which contradicts what they are; they now live in their own block on the Forms tab, managed without any selection. Saving one still needs an open form to save *from*, and that half stays conditional with an explanation rather than a disabled control with no reason given.

The form editor also stopped saving silently on every keystroke — the real complaint behind "непонятно, форма запостилась или нет". Edits mark the form dirty and an explicit Save button with a saved/unsaved/saving state commits them. Navigating away doesn't discard: switching post or leaving the tab flushes first, the same guard the editor already uses when switching drafts. Enabling or deleting a form still commits immediately, because that's structural rather than an edit — it changes what an uninvited visitor of the post gets. Finally, the export modal's preset row used to disappear entirely when no preset existed, leaving no hint they exist; it now carries an empty state linking to `/posts?tab=forms`, and the manager honours that `tab` query param.

### Migration chain collapsed, and a guard so drift can't recur

Deploying the above surfaced real drift during the mandatory pre-deploy check: prod's `__EFMigrationsHistory` listed `AddDraftTranslationSourceSnapshot` and `AddBlogStatSnapshot`, but neither file existed in the repo any more — while their changes *had* survived in `CedarDbContextModelSnapshot.cs`. Production was fine (the columns and the table are physically there, verified directly rather than inferred from history rows), but the repo's migration set could no longer build the schema from scratch, so any fresh environment would have come up broken.

Marty asked whether migrations could be dropped entirely, being the only user. They can't: EF Core's only alternative is `EnsureCreated()`, which cannot alter an existing database, so every schema change would mean recreating `cedar.db` — and the data is not disposable (published blog posts have public URLs linked from Telegram, plus comments, reactions, form submissions and a real card payment). What *was* the actual problem — the ritual, and drift going unnoticed — got addressed instead:

- **`SchemaDriftGuardTests`** turns the "always migrate after an `Entities.cs` change" rule into a failing test, via EF 8's `Database.HasPendingModelChanges()`. Confirmed it genuinely fails (a property added without a migration turns it red) rather than being a test that can only pass.
- **The chain was collapsed to one `InitialCreate`**, on production this time, not just locally. Equivalence was established before touching anything: the new migration was applied to a scratch database and compared against prod by column set and index set — 27/27 tables with identical names/types/nullability, 40/40 identical indexes. Raw `.schema` text differs harmlessly and is the wrong thing to diff, because prod's tables grew through `ALTER TABLE ADD COLUMN` (appends columns, requires defaults) while a fresh `CREATE TABLE` uses model order. The collapse also absorbed the two orphaned migrations, so the drift is gone.

Executed as stop → back up → rewrite history to a single row → deploy → start, in that order, because a service started on the *old* binaries after the history edit would have tried to `CREATE TABLE` over live tables. Verified after: one history row, `PRAGMA integrity_check` ok, 2 users / 9 drafts / 2 channels / 17 comments / 24 reactions / 1 payment unchanged, zero migration statements in the log, and all three real blog posts plus an EN translation still serving 200. Procedure written up in `.claude/rules/ef-migrations.md`.

### Watermark on private posts (I7)

Specced by Marty mid-session, so it stopped being the blocked item it was imported as: heavy semi-transparent text tiled *over* the blog post, and in the editor nothing but a marker that one is set.

The overlay is a single tiling `background-image`, not N repeated elements — the post sheet's height depends on the post, and a tile covers any height without the renderer guessing how many copies to emit. The tile is an SVG carried as a **base64** data URI rather than percent-encoded XML: the payload is author-supplied text landing inside a CSS `url()`, and base64 removes every quote, paren and backslash from that context outright instead of relying on getting an escaping table right. The text is still XML-escaped inside the SVG, and `WatermarkRenderer` lives in Core with 11 unit tests asserting exactly that — including that hostile input can't break out of the `url()`.

Applied only when the post is private: the watermark exists to discourage redistribution of something handed out per invite, so it has no job on a public page. Fill is mid-grey at low opacity and deliberately not a theme colour — a data-URI SVG can't read the page's CSS variables, and grey is the one value that stays faint-but-legible on both the light and dark blog themes. New `Draft.WatermarkText` (migration `AddWatermarkText`, purely additive) and `POST /api/drafts/{id}/watermark`, its own endpoint in the same one-concern-each style as `/tags`, `/folder` and `/registration-form`. Capped at 60 characters, because a long watermark tiles into unreadable mush.

Drive-by: the state strip's "Private" chip was still hardcoded English.

`dotnet test` 289/289. Not live-verified.

## 2026-07-26, Phase 9 (brainstorm sweep)
Imported `_Documents_/CedarClerk/Brainstorm_Features.md` (27 items with Marty's own priorities) into `docs/BACKLOG.md` and opened Phase 9 in `docs/ROADMAP.md`, executing High → Medium → Low with one commit per item.

High items done so far:
- **B22 topbar layout** — brand/divider/drafts/title/save-state left, Export + theme + profile right; stats/comments moved into the account popover. Reversed part of the same day's earlier topbar work (`.cedar` download went back into Export, import onto `/drafts`) — B22 was the newer instruction.
- **B21** — channels menu moved out of the topbar into the top of the Export window.
- **B5 Export redesign** — a checkbox per destination gating its settings, one Publish button firing every ticked destination in sequence, file list now shows count + total size.
- **B24** — `/drafts` table scrolls horizontally again; `overflow:hidden` (there only to clip rounded corners) had been cutting the fixed-width column grid off on iPad.
- **B25** — draft state strip above the language tabs: private/public, LIVE, links to the live blog/Telegram post.
- **B14 auto-translate fix** — root cause was that Re-translate only rendered while `enStale()` was true, and that flag clears itself as soon as the EN version is touched, leaving delete as the only action. It's now always offered, with the same progress bar + cancel as first-time auto-translate.
- **B3 registration form for private posts** (ADR-042) — biggest item so far. An uninvited visitor of a private post now gets a per-post configurable form (name/nickname/email/social + custom text/choice questions) instead of a 404, and is let in on submit. **This deliberately supersedes part of ADR-041**: a private post with a form is "locked", not "hidden". With no form configured the original indistinguishable-from-404 behaviour is unchanged. Parsing and rendering live in Core (unit-tested, and the tests assert escaping of author-authored labels — the one new injection surface); submissions land in a new `PostRegistration` table; the public endpoint carries the first rate limit in the blog endpoints (3 per visitor per post per 24h). Owner configures the form and reads submissions in the Export modal.

- **B23 activity column on `/drafts`** (ADR-043) — blog views and reactions per draft, each with a `+N` chip for what arrived since the previous session. The delta needed somewhere to measure from: new `DraftStatSeen` table, one row per (owner, draft), holding both a baseline and the counters at the last page load — the baseline only rolls forward when 30+ minutes have passed since the previous load, so a reload doesn't wipe the "while I was away" numbers and they're identical on laptop and phone. **The sparkline from the brainstorm was dropped**: nothing snapshots per-draft stats over time, and history can't be backfilled, so it stays blocked on the same data-collection layer as Channel Analysis.

- **Interface language, mechanism only** (B26, ADR-044) — `LocaleService` + typed `en.ts`/`ru.ts` dictionaries (a missing key is a build error, not a runtime blank), `ApplicationUser.UiLanguage` + its own `POST /api/auth/ui-language`, picker card in Settings, `localStorage` used only as a first-paint cache with the profile as the source of truth. **Login, register and `/drafts` are translated; everything else is still English** — see `TASKS.md`.
- **Export window pass** (N4 + N5 + N13 from the rewritten brainstorm, ADR-045) — the modal goes full-width (1180px, auto-fit column grid instead of one long scroll), a Telegram target is picked by clicking a connected channel instead of typing an id, and an unticked destination folds down to its header. The connect-by-@username field survives behind a disclosure link rather than being deleted: the discovered-chats list is empty for an account with no linked Telegram, which would otherwise leave no way to add a channel at all. Cost: the `anyComponentStyle` error budget went 25kB→32kB, `editor.component.css` was already at 24.3kB.

- **Posts Manager** (N7, ADR-046) — new `/posts` page with four tabs: posts, reactions & comments, stats, forms. `/comments` and `/stats` stopped being pages of their own: their components are reused as tab bodies with the page chrome stripped out, and both routes redirect. The posts tab does metadata-only edits (title, tags, folder, private, archive, delete) plus links out to the live blog/Telegram post — a rename re-sends the draft's own body untouched, because the save endpoint takes title and body together. The forms tab lists private posts and their submissions read-only; editing, per-question breakdowns and the pie chart are the next item. No backend changes — every action uses endpoints that already existed.

- **Forms tab + presets** (N10, N12, ADR-047) — the registration-form editor moved out of the export modal into the Posts Manager, gained a multiple-choice question type (checkboxes; the answer travels as a JSON array inside the existing string map, so no stored row is invalidated), and submissions now show real question labels instead of raw keys. Each closed question gets a distribution pie with a legend carrying label/count/percent; a question with one distinct answer is rendered as a line of text instead, and a seventh option folds into "Other". Chart colours are new `--series-1..6` tokens, picked separately for light and dark and validated for colourblind separation and contrast. Presets (`FormPreset` + `/api/form-presets`) are managed in the Forms tab and applied as chips in the export modal at publish time — copied onto the post, never linked, so editing a preset can't rewrite a post that already used it.

- **Low-priority sweep + the paragraph-number bug** (N1, N3, N8, N9, B12 — ADR-049). `/drafts` columns sort and resize (state in `localStorage`, Title absorbs the slack so the table can't start scrolling again). New comments and reactions are highlighted until hovered: one `FeedbackSeenAt` watermark per account, moved by hovering rather than by opening the page, flushed once on leave. Round count badges on the Posts Manager tab and the editor's account menu, fed by a dedicated count endpoint. The stats range became a 7-day–6-month slider with magnets at 7/14/30/60/90/180, fetching on release. **Paragraph numbers now actually render**: the CSS was right but sat in a component stylesheet, and Angular's encapsulation means such a rule can never match ProseMirror-created paragraphs — moved to the global sheet where the rest of the TipTap styling already lives.

- **UI translation finished** (B26, ADR-050) — the remaining screens went onto `t()`: Posts Manager with its stats and comments tabs, Settings, the editor (toolbar tooltips, export modal, AI dialogs, new-draft dialog) and the debug console. The cycling "Translating… / Compressing large photos… / Almost done…" status lists became dictionary arrays indexed the same way, so a language may use a different number of steps. Brand names, plan tiers, language endonyms and the Free-tier attribution line stay untranslated — that last one is published content, not chrome. Hit the `t`-shadowing trap a second time (`@for (t of tagList())` in the editor); loop variables named `t` are renamed to `tag` everywhere now. Still English: server `{ error }` bodies and the legal pages.

## 2026-07-26, drafts UI restructure (uncommitted)
Five requests from Marty after using the deployed private-posts work:
- **New Draft dialog** gained a "Private post" checkbox and a target-folder pill row. Both are applied as follow-up calls right after creation (the create endpoint takes neither) and deliberately **not** saved into `newDraftDefaultsJson` alongside languages/tags/template — they're per-draft intent, not a preference to repeat every time. The folder picker is a pill row rather than the editor's folder popover, because a nested `app-popover` inside `app-modal` fights the modal's own fixed positioning.
- **`/drafts` shows a private flag** — a lock icon inside the Title cell (both table and grid views), which needed `IsPrivate` added to the drafts-list DTO; it previously only existed on the single-draft endpoint.
- **The editor's drafts popover is gone.** The hamburger button now links straight to `/drafts`. Removed with it: the in-topbar draft switcher, its per-draft delete (and the delete-confirm modal it was the only trigger for — `/drafts` has its own), plus the now-orphaned `.drafts-popover`/`.draft-item`/`.draft-info`/`.hint` CSS.
- **`.cedar` import/export moved into the topbar** as icon buttons. Import errors had nowhere to render once the popover was gone, so they now surface as a dismissible toast reusing the existing `.ai-toast` placement. The download button is hidden below 768px — the topbar mobile-overflow fix from 25.07.2026 leaves no room, and the same action already exists in the Export modal, which is reachable on mobile.
- **Markdown (`.zip`) import moved to `/drafts`** — it lived *only* in the removed popover, so leaving it there would have made the feature unreachable. `/drafts` had no import UI at all before this.
- **`/drafts` is now the landing screen**: login and the `''`/`**` route fallbacks all point at it instead of `/editor`, and its back-to-editor button is gone (nothing to go back to). **Registration still lands on `/editor`** — a brand-new account has no drafts to choose between, and the editor auto-creates the first one, so bouncing through an empty list would just add a click.
- **Debug console hidden on public routes** — it was mounted unconditionally in the root shell, so it floated over the login/register forms. Now gated behind `App.showDebugConsole()`, which tracks `NavigationEnd` against a `PUBLIC_ROUTES` list. Scoped to all four no-account-required routes (`/login`, `/register`, `/terms`, `/privacy`) rather than just the two Marty named — the console reports the signed-in owner's own API traffic, so it's equally meaningless on the legal pages.

## 2026-07-26, deploy follow-ups (uncommitted)
- Deployed the Folders/notifications/private-posts work to production (both migrations applied cleanly, health + blog + RSS all 200). Marty confirmed everything works **except email delivery**.
- **Email delivery broken — bad API key, not a code bug**: `GET https://api.resend.com/domains` from the Pi returns **401** with the configured key. The key was issued while the `noreply.mooexe.dev` domain existed; that domain was later deleted and replaced with `mooexe.dev`, which appears to have invalidated it. Needs a freshly generated Resend API key — the env var wiring itself is correct (`Cedar__Email__ResendApiKey` present and intact on the Pi, `FromAddress` already updated to `Cedar Clerk <noreply@mooexe.dev>` on the newly verified domain).
- **Privacy can now be set before publishing** (Marty's request after first real use) — the "Private post" toggle used to live inside the Export modal's "already published" branch, so a post could only be gated *after* going live. Moved it out: the toggle applies at any time, while the invite list (which needs a post URL) shows a hint until the first publish. See ADR-041's amendment, `docs/DECISIONS.md`.

## 2026-07-26, continued (uncommitted)
- **BUG**: opening a draft sometimes immediately flagged the EN translation as stale ("Pay attention") even though nothing had been edited. Root cause: TipTap 3's `setContent()` defaults to `emitUpdate: true`, so every one of 8 programmatic content-load call sites (draft open, language switch, AI-edit/auto-translate apply, new draft) fired the same autosave path as a real keystroke, silently bumping `Draft.UpdatedAt` and tripping the `ruUpdatedAt > enMeta.updatedAt` staleness check. Fixed by passing `{ emitUpdate: false }` at all 8 sites.
- **Folders** (first item picked from the "Cedar Clerk 0.9.0" backlog dump, idea #19) — a real `Folder` entity, one folder per draft (unlike `Tags`, which stay flat/multi-valued/unmanaged). Full CRUD (`FolderEndpoints.cs`), a filter + manage popover and per-row assignment on `/drafts` (table and grid views), and a lighter assign-only selector in the editor next to the tag row. Deleting a folder unassigns its drafts rather than deleting them. See ADR-039, `docs/DECISIONS.md`. Committed (`ce49650`, "Drafrs folders") and deployed to production the same session — health check + migration (`AddFolders`) applied cleanly. **Still not click-through-verified in a browser.**
- **Engagement notifications** (second item picked, idea #18) — opt-in DM via the bot when a new comment/reply or new "like" reaction lands on the owner's blog posts (not dislikes, not un-likes). New `ApplicationUser.NotifyOnEngagement` toggle in Settings → Integrations, only shown once Telegram is linked. Reuses the plain-text DM mechanism already proven in `BillingEndpoints.cs` — no new bot infrastructure. See ADR-040, `docs/DECISIONS.md`. **Not yet live-verified against a real Telegram DM.**
- **Private posts + first email infrastructure** (third item picked, idea #20.1/20.2; 20.3/polls stays deferred) — the project had zero email-sending capability, so this shipped in two parts: (1) `ResendEmailProvider` (`CedarClerk.Server/Email/`), Cedar Clerk's first outbound email, config'd via `Cedar:Email:ResendApiKey`/`FromAddress` (`docs/integrations-setup.md` §3 — **needs Marty to create a Resend account and verify the domain via Cloudflare DNS** before real delivery works); (2) `Draft.IsPrivate` + `PostInvite` (email + token per invited reader), gated centrally via `BlogEndpoints.HasPrivateAccess` at all 4 slug-lookup call sites (page render, annotations, reactions, comments), long-lived access cookie, unauthorized visitors get an indistinguishable-from-404 response, private posts excluded from the homepage list and RSS feed. The invite link is always shown/copyable in the Export modal even if the email itself fails to send. See ADR-041, `docs/DECISIONS.md`. **Not yet live-verified** (needs the Resend setup first for the email half; the link-copy fallback can be checked without it).

## 2026-07-26 (uncommitted)
- **Phase 8 (v0.8.0) closed** — finished the 3 remaining steps found half-done/not-started during the 25.07.2026 docs audit:
  - Step 6 (tags → Telegram): `PostEndpoints.BuildHashtagLine` appends a trailing `#tag1 #tag2` line to every Telegram export, relying on Telegram's native hashtag auto-linking. See ADR-036. **Not yet verified live against `@testingandfun`** — deferred by Marty's choice this session.
  - Step 7 (comments improvements): one level of comment replies (`Comment.ParentCommentId`, migration `AddCommentParentId`), the channel owner's own comments highlighted (whole-article comment box only), the owner's display name reserved against impersonation (409 on collision, no reservation table), and the post's publish time shown alongside each comment's write time. All in the vanilla-JS blog comment widget (`BlogEndpoints.cs`), not Angular. See ADR-037. **Not yet verified live in a browser** — deferred by Marty's choice this session.
  - Step 8 (AI progress bar): replaced the flat elapsed-second counter with an asymptotic pseudo-progress estimate (`pseudo-progress.util.ts`, capped at 90% until the real response lands) for AI-edit and auto-translate — real token streaming was investigated and scoped out (neither AI provider streams today; would need new backend SSE infrastructure for a proxy metric, not a true percentage, either way). Per Marty's ask on top of that: elapsed time still shown alongside the %, a 3-minute client-side timeout, and a Cancel button that actually aborts the in-flight request (required converting `DraftsService.autoTranslate`/`aiEdit` from `firstValueFrom`-wrapped Promises to raw, cancellable Observables). See ADR-038.
- Docs audit found and fixed further drift while closing this phase: `docs/PRD.md`'s "Open requirements — Phase 8" section still listed Steps 1–5 (RSS, legal pages, header slots, signature monetization, blog bugfixes) as open even though `docs/ROADMAP.md` already showed them done — folded into "Shipped requirements" properly, and the section removed now that the whole phase is closed.
- **BUG**: opening a draft (or switching language, or applying an AI-edit/auto-translate result) sometimes immediately flagged the EN translation as stale ("needs attention"), even when nothing had actually been edited. Root cause: TipTap 3's `editor.commands.setContent()` defaults to `emitUpdate: true`, so every one of the 8 programmatic content-load call sites in `editor.component.ts` fired the same `onUpdate` → `markDirty()` → 1.2s-debounced autosave path as a real keystroke — silently re-PUTting the unchanged RU content and bumping `Draft.UpdatedAt` to "now," which made `enStale()`'s `ruUpdatedAt > enMeta.updatedAt` comparison trip on load. Fixed by passing `{ emitUpdate: false }` at all 8 call sites (draft open, language switch both directions, AI-edit/auto-translate result apply, new draft, start-EN-version) — `onUpdate` still fires normally for actual user keystrokes, which go through ProseMirror transactions, not `setContent`.

## 2026-07-25 (uncommitted)
- Docs reorg: pulled the "Backlog ideas"/"Deferred"/"Tech debt"/"Open questions" tables out of `docs/ROADMAP.md` into a new `docs/BACKLOG.md` — Marty wanted one place that shows only not-yet-started work, without phase-status noise. Added the 10 ideas from Marty's `/remote-control` dump (loading indicators for import/export, tag popup everywhere, blog card tag display, admin role + user-management page, glossary/terms feature, AI popover relocation, more social integrations, etc.) with accuracy notes against current code (e.g. session-cookie auto-login already exists via ASP.NET Identity's persistent cookie — needs Marty to clarify what's actually broken before scoping).
- New `docs/UI-INVENTORY.md`: per-UI-element documentation convention (location/type/purpose/loading-state) plus a retroactive audit, starting with the full `editor.component` breakdown (~25 elements) and `shared/` components.
- Fixed 4 bugs from the same dump, verified live in `ng serve`/`dotnet run` by Marty:
  - Blog `ViewCount` was double-counting when a visitor switched RU↔EN on a post (each switch is a full page reload back into `RenderPostAsync`). Now gated by a short-lived per-post cookie (`BlogEndpoints.cs`, `Consts.General.ViewedCookiePrefix`).
  - Toolbar popup menus (Paragraph/table/formula/AI dropdowns) had stopped rendering, and the Export modal was pinned near the top of the screen instead of centered — both traced to the same cause: the "Cedar Aero" glass redesign put `backdrop-filter` directly on `.toolbar`/`.topbar`, which (per spec, like `transform`/`filter`) makes that element a containing block for its `position: fixed` descendants, so `app-popover` panels and the `app-modal` overlay were positioning/clipping against the 44–58px topbar/toolbar box instead of the viewport. Fixed by moving the glass blur onto a `::before` pseudo-element (keeps the visual effect, doesn't create the containing block) and additionally relocating the Export `<app-modal>` out of `<header class="topbar">` in `editor.component.html` so it isn't a header descendant at all.
  - Horizontal page-level scroll on iPad/iPhone widths — `.toolbar` is a flex item with default `min-width: auto`, so once its button row needed more space than the viewport it widened `.app`/`body` instead of scrolling internally via its own `overflow-x: auto`. Fixed with `min-width: 0; width: 100%` on `.toolbar`, plus `overflow-x: hidden` on `html, body` in `styles.scss` as a general safety net.
  - Drive-by, found during live verification: the floating debug-console tab (`app-debug-console`, mounted globally, `position: fixed; bottom: 0`) was sitting directly on top of the editor's status bar (word/char count, sync indicator) in the bottom-right corner. Gave the closed tab a 27px bottom margin (matching `.status-bar`'s height) so it clears the status bar; the open panel still goes flush to the bottom as before.

## 2026-07-16 (uncommitted)
- Fixed Telegram posts rendering garbled after Bot API bumped to **10.2** (14.07.2026): `Telegram.Bot` NuGet upgraded `22.10.1`→`22.10.2`; Telegram send path switched from `Markdown`/`Html` strings to `InputRichMessage.Blocks` via a new `CedarToTelegramBlocksRenderer` (Core) + mapping layer in `PostEndpoints` — the only combination that reliably embeds media with a real, natively-styled caption, verified live against `@testingandfun`. `CedarToTelegramMarkdownRenderer`/`CedarToTelegramHtmlRenderer` kept but no longer used for sending. Full story: ADR-018 in `docs/DECISIONS.md`, operational summary in `.claude/rules/telegram-bot.md`.
- Follow-up fix, same day: first real post after deploying the above hit a Cloudflare 502. Root-caused against a real prod draft (read-only DB pull, replayed locally): empty `carousel`/`collage` nodes (`images: []`, an editor artifact) produced a zero-item `InputRichBlockSlideshow`/`Collage`, which Telegram rejects with `RICH_MESSAGE_CONTENT_REQUIRED`. `CedarToTelegramBlocksRenderer` now drops these nodes instead of emitting them. A second, unrelated red herring in the same draft (one image asset failing with `wrong type of the web page content` despite being genuinely reachable) turned out to be Telegram caching an earlier failed fetch from mid-session testing, not a code defect — see ADR-019 in `docs/DECISIONS.md`.

## 2026-07-15
- `d9e56ae` "Fixes", `6065cd9` "Re-translate button" — fixed a `deploy.ps1` path-duplication bug (see `TASKS.md`); replaced the last `window.confirm()` in the re-translate flow with a styled confirm modal, matching the pattern already used for AI-edit (see ADR entries in `docs/DECISIONS.md` for the AI-edit gating this touches). Verified during this session that LLM buttons (translate/fix-errors/"schizo-izer") were already fully implemented — the backlog docs just hadn't been updated to reflect it.
- Phase 8 (v0.8.0) planned (not implemented): header slot system, signature monetization, legal pages, blog polish/bugfixes, comments improvements, tags, RSS, AI progress bar. See `docs/ROADMAP.md`.
- Documentation source-of-truth established: `CLAUDE.md` trimmed to an index, `docs/*.md` populated, `.claude/rules/*.md` created, `Plans/` folded into `docs/ROADMAP.md`+`docs/DECISIONS.md` and archived.

## 2026-07-13
- `39e08d2` "AI stuff and bug fixes" — AI-edit and related fixes (see Phase 4/6 LLM-buttons entries in `docs/ROADMAP.md`).

## 2026-07-11
- `788d421` "Refactoring, Payment processing" — billing model expanded from a single Pro tier to three tiers (Pro/Pro Plus/Trial); PayPal went from a stub to a full Orders API v2 integration; new `PlanLimitations`/`SubscriptionPlanHelper` (Core) + `SubscriptionPlan` (Server); Stripe Customer Portal added; migration history collapsed to a single `InitialCreate`. See ADR-012/ADR-013/ADR-015 in `docs/DECISIONS.md`. `dotnet test` 162/162, `ng build --configuration production` clean at the time.

## 2026-07-10
- `bcdacc9` "Lots of new features including subscription, tags and telegram login widget support" — Telegram account linking (HMAC-verified widget), bot chat auto-discovery, bilingual RU/EN drafts, blog tags + monthly timeline, post signatures. See Phase 6 in `docs/ROADMAP.md`.
- `d734c3b` "Refactorring", `a3dc7c0` "Fav icon", `d232ff3` "Fix" — follow-up fixes and polish on the above.

## 2026-07-08
- `709e048` "Added stats feature" — `ChannelStatSnapshot` + daily Quartz snapshot job + sparkline UI.
- `88394c8` "Frontend update", `9726eb6` "Draft export support added" — `.cedar` zip-container export/import (`CedarPackage`), see ADR-006 in `docs/DECISIONS.md`.
- `796d19c` "Added reactions and comments" — anchor-based blog reactions (like/dislike, `VisitorHash`-scoped) and comments, editor-side management panel.
- Same-day: the "Cabin" UI/UX redesign (design tokens, dark theme, new topbar/toolbar/status bar) — see Phase 4 in `docs/ROADMAP.md` for the full breakdown and ADR-011 in `docs/DECISIONS.md` for what was deliberately rejected (live preview bubble, right "Publish" panel).

## 2026-07-07
- `caeb543` "Media support", `65ed405` "Absorb cedarclerk-web into the main repo", `70d249e` "UI fixes", `7ae3319` "More media support added", `8f3249e` "Server improvement. Added channel endpoints and scheduled posts support", `12957f6` "Bug fixes", `c1de5a6` "Rights fix" — channel management (`ChannelEndpoints`), Quartz.NET scheduled publishing, media upload pipeline, ownership/rights fixes.
- `38fee62`/`113bdd9`/`ceddaa5` "Editor UI overhaul Phase 1–3a" — popovers, icons, EN strings, Markdown export format + Export popover, spoiler/links/emoji/date-time/toggle/collage TipTap extensions.
- `fad95fc` "Fix .gitignore case collision that excluded CedarClerk.Server/Data/*.cs" — a `.gitignore` pattern was accidentally matching source files, not just build output.
- `226504a` "UI redesign", `6a09719` "Version changed", `d875999` "Markdown support added", `1f409cc` "UI Improvements" — the Telegram-HTML-vs-Markdown renderer question was resolved in favor of an HTML-only canonical renderer (see ADR-007 in `docs/DECISIONS.md`); `CedarToTelegramMarkdownRenderer` remains as an export-format option.

## 2026-07-06
- `f5dc539` "Bug fixes. Added deploy script" — `Scripts/deploy.ps1` (build → publish → scp → restart → health check).
- `0b5d785` "Added basic API, tests and telegram bot support" — first working `TelegramBotService`, first xUnit tests, base REST API.

## 2026-07-05
- `ecf1942` "added gitignore and first api command", `1fcfb79` "Created solution and projects", `6ace957` "Init commit" — project scaffolding: the `CedarClerk.Server`/`CedarClerk.Core`/`CedarClerk.Tests` solution, initial `.gitignore`.
