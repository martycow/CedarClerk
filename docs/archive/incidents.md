---
owner: marty
last_verified: 2026-09-01
source_of_truth_for: index of recorded incidents — where each narrative lives and what guards it today
guard: none
---

# Incident index

Every incident this project has recorded, in one place. **This is an index, not a retelling** — the
full account of each one already exists somewhere, and the link is the point. Each entry gives the
date, the shape of the failure in a sentence, where the narrative lives, and what stands in the way
of a repeat today.

Entries are grouped by the surface that failed. Dates are the day the failure happened, not the day
the fix shipped.

## Text and data loss

- **29.07.2026 — a published post's text was wiped by its own autosave.** Deleting a table empties
  the document for an instant; the 1.2s autosave honestly saved that empty document, the server
  accepted it without a word, and closing the tab on iOS killed the timer carrying the restored text.
  Three independent gaps, each of which had to hold, and none did.
  **Narrative**: `docs/adr/ADR-066.md` (the saving half), `docs/adr/ADR-065.md` (the publishing half),
  `docs/adr/ADR-067.md` (recovery); `docs/tasks/CHANGELOG.md` 2026-07-30 (v0.9.16) and the
  2026-07-30 audit entry. Backlog history: `T-060`, `T-018`.
  **Guards today**: `CedarClerk.Core/ShrinkGuard.cs` refuses a save that leaves 20% or less of a
  document that had 200+ visible characters, and the refusal dialog offers "restore stored"; a pending
  save is flushed on `pagehide`/`visibilitychange` with `keepalive`; a save may carry
  `expectedUpdatedAt` and gets a `409` instead of overwriting; both publish endpoints require the
  fingerprint of the version the author was shown; version history is readable, diffable and
  restorable. `ShrinkGuardTests` pins the thresholds — changing one silently brings the incident back.
  Still unverified by hand: flush-on-hide on a real iPhone (`docs/tech/QA.md` §Editor and saving).

- **30.07.2026 — the guard that was declared and never ran.** Found by an adversarial audit of
  uncommitted work before it was committed, not by a failure: `ConfirmedFingerprint` was dead code, so
  the publish-confirmation modal was decoration, and the blog path — the exact path of the 29.07 wipe —
  skipped the guard entirely. The same audit found `DraftRevision` rows growing without a ceiling and
  leaving full copies of deleted private posts in production and in all fourteen backup generations.
  **Narrative**: `docs/tasks/CHANGELOG.md` 2026-07-30 (the audit entry); `docs/adr/ADR-065.md`.
  **Guards today**: the guard is server-enforced; revisions are written only on real change, pruned to
  50 saves per draft+language, and deleted with their draft or translation.

- **01.08.2026 — a private post was readable in one browser and forgeable in any.** A reader filled in
  a private post's form in Telegram's in-app browser, opened the post in Chrome and was asked to
  register again — the grant lived nowhere but in one browser. Reading the code for it found the other
  half: the access cookie's value was the string `"1"`, so anyone who knew a draft's id could write it.
  **Narrative**: `docs/adr/ADR-084.md`; `docs/tasks/CHANGELOG.md` (the T-064/T-023 entry).
  **Guard today**: the cookie carries a DataProtection-signed value, and registration mints a
  per-reader token that comes back in the redirect — a link the reader can send themselves, revocable
  without locking out everyone else.

## Telegram

- **13.07, 26.07 and 09.08.2026 — three `409 Conflict`s on the production bot token.** The Bot API
  allows one long-poller per token; a local dev server starting its own `TelegramBotService` knocked
  production's off. The rule was known each time and not acted on.
  **Narrative**: `.claude/rules/telegram-bot.md` §"A local launch has exactly two safe forms"; the
  26.07 mechanism is also in `docs/adr/ADR-121.md`.
  **Guards today**: exactly two safe launch forms, and the startup line `Cedar:BotToken not set — bot
  is disabled` is the only accepted proof. Two traps are written down because they defeated earlier
  attempts: `--no-launch-profile` is load-bearing (`launchSettings.json` pins `Development`, which
  loads the real token), and `$env:Cedar__BotToken = ''` *deletes* the variable on Windows rather than
  blanking it — which is why the desktop shell passes a single space instead.

- **16.07.2026, then 01.08.2026 — Telegram's per-URL negative cache.** A genuinely reachable,
  correctly served image kept failing with `wrong type of the web page content`: Telegram had cached an
  earlier failed fetch of that exact URL. The first time it was judged not worth a code change; the
  second time it killed part 3 of a twelve-part thread, holding back every later part.
  **Narrative**: `.claude/rules/telegram-bot.md`; `docs/adr/ADR-087.md`; `docs/tasks/CHANGELOG.md`
  (2026-08-01, the threaded publish).
  **Guard today**: every media URL from our own `/media/` gets a per-send `?v=<stamp>`
  (`TelegramPublishTarget.StampUrl`, narrowed by ADR-088 and ADR-091), so a cached failure cannot
  outlive its incident. External URLs are never stamped — `img.youtube.com` answers 404 to unknown
  query strings.

- **01.08.2026 — Telegram's fetcher timed out pulling many media at once from the origin.**
  `failed to get HTTP URL content`, which is not flood control (that answers 429). The host was a
  Raspberry Pi behind a home upstream, so the timeout itself may no longer reproduce.
  **Narrative**: `.claude/rules/telegram-bot.md`; ADR-088/ADR-089.
  **Guard today**: own media is pre-uploaded once to a storage chat and sent by cached `file_id`
  rather than fetched by Telegram at all. `Cedar:Telegram:MediaDelivery=url` reverts the target without
  a redeploy.

- **01.08.2026 — a thread's first part collapsed behind "Show more".** The splitter budgeted against
  `MaxPostChars = 32,768` — the most a message *can* carry — so a 6,412-character part was legal and
  unreadable, while 1,787 rendered in full.
  **Narrative**: `docs/adr/ADR-086.md`.
  **Guard today**: `PublishCapabilities.ThreadPartCharacters`, with Telegram at
  `Consts.Telegram.ThreadPartChars = 3,000` — a named constant precisely so the next test-channel
  session can tune it in one place.

- **01.08.2026 — the publish request that never came back.** A post with 15 MB of audio and 15 MB of
  video returned Cloudflare's 502. Nothing had failed: publishing was synchronous inside one HTTP
  request, and Telegram was downloading every media URL from our own origin while `SendRichMessage`
  blocked. The proxy gave up; the publish may still have been in flight.
  **Narrative**: `docs/adr/ADR-080.md`.
  **Guards today**: a non-blocking `slow-media` warning before the send, and publishing has since
  become a queued job (`PublishJob`, `Bot/RunPublishJobsJob.cs`) rather than a request the author waits
  on.

## Production, deploy and backups

- **11.08.2026 — the move to DigitalOcean left production with no database backup.** The Pi's nightly
  `sqlite3 .backup` and its microSD copies did not travel: no crontab, no `~/bin`, no `/mnt/backup` on
  the new host. What remained was DigitalOcean's weekly whole-machine image — the loss window went from
  a day to a week, a restore meant the whole machine, and the only copy now lived in the same account
  as the original.
  **Narrative**: `.claude/rules/production-environment.md` §Backups; `docs/tasks/CHANGELOG.md`
  2026-08-11 and 2026-08-12. (The migration checklist that also covered it was deleted in the
  24.08.2026 archive clear-out — see the docs entry below.)
  **Guards today**: `~/bin/backup.sh` from crontab at 03:30 UTC with fourteen dated copies and a
  healthchecks.io ping so a *silent* failure raises an alert; `Scripts/server/backup.sh` is the
  versioned source of it; an off-box `rclone` copy to Cloudflare R2 — a second account, not a second
  disk — with `--backup-dir` instead of a bare sync and its own separate healthcheck. Restores are
  tested on a monthly cadence (`docs/product/BUSINESS.md` §5).

- **12.08.2026 — a backup that would have reported itself as missing, then as empty.** Two defects in
  one directory. The script wrote to `~/backups` while `cedar status` and `cedar backup verify` read
  `{RemoteDataDir}/backups`, so the tool said "no local copy" over a directory that had one. Moving it
  surfaced the second: `cedar` counted copies with a bare `*` glob, and cron appends `backup.log` to
  that same directory *after* the copy — from the first scheduled run onward the newest "backup" would
  have been a 0-byte log. A third would have killed it outright: the cron line's `>> …/backups/backup.log`
  is opened by the shell before the script runs, so the script's own `mkdir -p` was too late and cron
  would never have started it at all.
  **Narrative**: `.claude/rules/production-environment.md` §Backups; `docs/tasks/CHANGELOG.md`
  2026-08-12.
  **Guards today**: one destination in both places (`/home/martycow/cedarclerk/data/backups`), the glob
  is `cedar-*.db.gz`, and the healthcheck is what would have noticed the cron failure.

- **11.08–12.08.2026 — the systemd unit was not `enabled` for the first day after the move.** A
  DigitalOcean maintenance reboot would have left the site down until someone noticed.
  **Narrative**: `.claude/rules/production-environment.md` (`T-143`).
  **Guard today**: the unit is enabled; the fact is written into the production rule so a future host
  move has it on the checklist.

- **Recurring, through 11.08.2026 — `scp -r` left production half-copied, twice.**
  `client_loop: send disconnect: Connection reset` near the finish line, and every time *after* the
  service had already been stopped: 50 files of 174 in `app/`, `wwwroot` missing, `/api/health`
  answering 502, and a re-run starting the same 50 MB from scratch because `scp` cannot resume.
  **Narrative**: `docs/adr/ADR-113.md`; `docs/adr/ADR-118.md` (why the CLI calls the scripts rather
  than repeating them).
  **Guard today**: one `tar.gz` with a resumable byte position and a checksum verified on the far side;
  everything slow happens while the old version still serves; the service stops only for two renames;
  `app.prev` stays for `--rollback`.

- **12.08.2026 — the first real desktop update quit with "exit code 1".** Nothing was wrong with the
  update. `taskkill /f` gives the process it kills an exit code of 1, and the server child's `exit`
  handler reported "the local server stopped unexpectedly" in a modal error box — which blocked the
  main process mid-hand-over, because an update stops the server while the window is still open.
  **Narrative**: `docs/tasks/CHANGELOG.md` 2026-08-12 (the desktop update entry); `docs/adr/ADR-116.md`.
  **Guards today**: an intentional stop detaches the crash reporter before killing — a shutdown we
  asked for cannot be a failure — and `%APPDATA%\CedarClerk\update.log` exists, because a packaged app
  has no console and this had to be reasoned out from first principles instead of read.

- **01.09.2026 — two consecutive deploys called themselves 0.20.0.** The health check matches the
  version string, so it proved nothing; what actually proved the swap was `GET /api/teams` answering
  401 instead of 404.
  **Narrative**: `docs/tasks/TASKS.md` §Notes — the only record so far.
  **Guard today**: none automated. Bump `Consts.CurrentVersion` before a deploy, or the blind spot
  returns.

## Database schema

- **Date not recorded — `SQLite Error 'no such column'` on every authorized request.** A property was
  renamed with no matching migration. It broke *every* authorized request rather than one screen,
  because ASP.NET Identity's security-stamp validation touches `AspNetUsers` on each one.
  **Narrative**: `.claude/rules/ef-migrations.md`.
  **Guards today**: `SchemaDriftGuardTests` (added 27.07.2026) calls EF's `HasPendingModelChanges()`
  and fails `dotnet test` when `Entities.cs` has moved without a migration — verified to actually go
  red. Column renames are hand-edited to `RenameColumn`, because EF's default diff generates Drop+Add
  and loses the data.

- **27.07.2026 — the repo could no longer build its own schema from scratch.** Production had applied
  `AddDraftTranslationSourceSnapshot` and `AddBlogStatSnapshot`, but both migration files were missing
  from the repo while their changes survived in the model snapshot. Production itself was fine; a fresh
  database was not buildable.
  **Narrative**: `.claude/rules/ef-migrations.md` §"Collapsing the migration chain".
  **Guard today**: the migration-chain collapse absorbed both, and step 1 of that procedure — compare a
  scratch database against production *by column set and index set*, never by raw `.schema` text — is
  what found them.

## UI, design and tooling

- **Date not recorded — the debug console covered the editor's status bar and swallowed its clicks.**
  A viewport-pinned full-width strip overlaying the page, taking the clicks meant for the fullscreen
  button underneath.
  **Narrative**: `docs/adr/ADR-153.md` (context paragraph and decision 6); referenced again in
  `docs/adr/ADR-174.md`.
  **Guard today**: the strip reserves its own height (`--bench-bottom-h`) instead of covering content,
  and the editor opens it from a button in its own status bar.

- **Date not recorded — the contrast checker measured the light block twice.** `tools/check-contrast.mjs`
  and `tools/generate-design-tokens.mjs` both find the first `:root` string in `styles.scss` and
  brace-match from there, so a mention of `:root` inside a *comment* above the base block silently
  pointed the dark check at the light one.
  **Narrative**: `docs/adr/ADR-136.md`, `docs/adr/ADR-137.md` §4, `docs/design/UI-V2-PLAN.md`.
  **Guard today**: nothing may spell `:root` above the base block, and no material may be declared
  above it — `DesignTokenDriftTests` matches the first `--<token>:` occurrence in the file.

- **During the Cedar Bench port, twice — `icon-usage.generated.ts` shipped stale.** A generated file
  that is committed says a stale answer is impossible *as long as the script is re-run*, and nothing
  re-ran it or noticed that it had not been.
  **Narrative**: `docs/adr/ADR-173.md` (20.08.2026).
  **Guard today**: the Icon inventory phase of `cedar test` fails when the committed file disagrees
  with the call sites.

- **07.08.2026 — connection controls were built in the wrong place three times, then rebuilt.**
  Connecting a Telegram channel, a Bluesky app password and the X OAuth button all grew up inside the
  Export modal, while the Settings → Integrations panel that owns integrations by name went stale and
  showed neither Bluesky nor X. The rebuild existed only because nobody looked at where the Telegram
  connection already lived.
  **Narrative**: `docs/adr/ADR-095.md`; `.claude/rules/ui-changes.md`; the `sec-integrations` rows in
  `docs/design/UI-INVENTORY.md` record both the move and the reason.
  **Guards today**: read `docs/design/UI-INVENTORY.md` before adding any control, and update it in the
  same commit as the code; `UiInventoryDriftTests` catches the coarse case — a screen or `sec-*`
  section with no mention at all.

- **31.07.2026 — a codemod's regex was bounded in appearance only.**
  `import\s*\{[\s\S]*?\}\s*from '@lucide/angular';` is lazy but still *starts* at the first `import {`
  in the file, so it swallowed every import statement above the Lucide one: fourteen files lost their
  entire import section. A follow-up check `from '.*icon\.component'` matched `brand-icon.component`
  and judged a file to already have the import it was missing.
  **Narrative**: `docs/tasks/CHANGELOG.md` 2026-07-31 (T-079, the Phosphor migration).
  **Guard today**: none automated — the compiler caught it and `git checkout` undid it. The lesson is
  written where it happened: `[^{}]*` cannot cross another import's braces, and a pattern that is
  merely plausible is not a check.

- **Date not recorded — `npm` launched by bare name could not find its own entry point.** `npm.cmd`
  locates its JavaScript through `%~dp0`, and a batch file started by bare name through `CreateProcess`
  gets `%0` without a directory. The symptom, `Cannot find module <cwd>\node_modules\npm\bin\npm-cli.js`,
  reads like a broken project and is a broken launch. The fix had its own trap: Node ships both `npm`
  (a shell script) and `npm.cmd` in one directory, and resolving the bare name first finds the file
  `CreateProcess` cannot run.
  **Narrative**: `docs/tasks/CHANGELOG.md` (the CLI port entry, 2026-08-12).
  **Guard today**: npm is started by full path, `.cmd` explicitly — and the comment naming this
  incident is one of the few kept in the CLI, because it *is* the code's justification.

## Documentation

- **24.08.2026 — the archive was cleared and left dangling references.** Three files under
  `docs/archive/` (the DigitalOcean migration checklist, ROADMAP phases 0–10, the docs audit) were
  deleted under the commit message "Removed outdated docs", and the references to them in
  `docs/DOCS-FLOW.md` and `.claude/rules/production-environment.md` were left pointing at nothing.
  **Narrative**: `docs/DOCS-FLOW.md` §Known weak spots and §"Prod move to DigitalOcean".
  **Guard today**: `DocsFlowGraphTests.Every_mapped_path_exists` — which caught it only later, because
  the test was not run between commits. If a file under `archive/` has gone missing, check
  `git log --diff-filter=D -- docs/archive/` before fixing the references.

- **18.08.2026 — four reference docs were never on the map.** `STACK`, `BUSINESS`, `MULTITENANCY` and
  `integrations-setup` existed and `docs/DOCS-FLOW.md` claimed to map every living doc. The claim held
  on discipline alone, and discipline had already failed.
  **Narrative**: `docs/DOCS-FLOW.md` §File placement; the comment at the top of
  `CedarClerk.Tests/DocsFlowGraphTests.cs`.
  **Guard today**: `DocsFlowGraphTests.Every_live_doc_is_on_the_map` — a new doc gets a node in the
  same commit or `dotnet test` goes red.

## Found by audit, not by failure

Holes that were real and reachable, but with no recorded exploitation. They belong here because the
guards that closed them are the same kind, and because "nobody used it" is not a reason to forget it.

- **31.08.2026 — the shared bot token made every attribution a security boundary.** One token sits in
  every account's channels and private chats, so "which account does this update belong to" is a
  security question. Four holes: connecting a channel checked only that the *bot* was an admin, not the
  caller, so any account could claim any channel the bot was in; comment counting could be inflated by
  a stranger forwarding a post into their own chat with the bot; media and `file_id` resolution ran
  under a platform context with the tenant filter off, so a draft could name another account's file and
  mutate its row; and `refresh-known-chats` walked the global table for any authenticated user.
  **Narrative**: `.claude/rules/telegram-bot.md` §"The bot is shared" (`T-359`).
  **Guards today**: `BotChatAccess.IsAdminOrCreator` on the caller; comment counting requires a
  `Supergroup` and Telegram's own `IsAutomaticForward`; an explicit `a.OwnerId == request.OwnerId` on
  the queue's asset queries; `refresh-known-chats` bounded to the caller's admin chats. A Telegram user
  id maps to one account by a unique filtered index.

- **13.08.2026 — the storage quotas were set from generosity rather than from hardware.** Pro promised
  8 GB per account against a droplet with ~40 GB free in total: five paying users filling their quota
  would take the server down, before the deploy needs room for a second copy of the app and the backups
  need room for fourteen days of database.
  **Narrative**: `docs/tasks/CHANGELOG.md` 2026-08-13; `docs/product/MULTITENANCY.md` §1;
  `docs/adr/ADR-129.md`.
  **Guard today, half of it**: quotas are cut to what the machine can honour. The second half — moving
  media off the disk into object storage, so the disk stops being the ceiling — is still open as the
  `T-172` remainder and is a gate in front of public registration: migrating someone else's files is
  worse than migrating your own.

## Outside the software

- **Date not recorded — a Zoom recorder's SD card was formatted prematurely.** An irreversible action
  taken without stopping to confirm it first.
  **Narrative**: `.claude/rules/destructive-operations.md` — the rule is the record.
  **Guard today**: for anything hard to reverse — dropping a table, `rm -rf`, `mkfs`, force-push,
  deleting drafts or media, formatting a disk or card — explain what is about to happen, then **stop
  and wait for explicit confirmation**. It is why that rule is a separate file rather than a line in a
  larger one.
