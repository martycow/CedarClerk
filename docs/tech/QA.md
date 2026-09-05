---
owner: marty
last_verified: 2026-09-01
source_of_truth_for: the permanent verification checklist — what is re-checked whenever a surface changes
guard: none
---

# Verifying Cedar Clerk

**This file holds what gets re-checked whenever a surface changes. `docs/tasks/TASKS.md` holds only
what is being checked right now.** A row that names a task id and a date is a moment and belongs on
the board; a row that names a behaviour is permanent and belongs here. When a sprint's eye-check is
finished, its tick disappears from TASKS and the behaviour stays in this file for the next time
someone touches that surface.

Verification debt is the project's standing risk: the code is covered by tests and most of it has
never been opened by a person. A green `dotnet test` says the shapes are right, not that the app
works.

## What a machine already answers

`cedar test` runs **six phases by default and seven with `--smoke`**, each with its own verdict:

| Phase | What it proves |
|---|---|
| Backend (`dotnet test`) | Endpoints, renderers, guards, and the drift guards below |
| Frontend units (vitest) | Component and service logic in isolation |
| Icon inventory | `icon-usage.generated.ts` matches the call sites in `src/app` |
| Contrast contract | Every token pair clears its ratio in both themes |
| Density contract | No control drops below its touch/size floor |
| Frontend build (`ng build`) | The front end compiles for production and stays under its bundle ceiling — the same command the deploy runs (ADR-265) |
| Smoke (Playwright, isolated database) — **`--smoke` only** | The critical paths end to end against a scratch `CEDAR_DATA_DIR` |

`TestPipeline.Phases()` appends the smoke phase only under `options.Smoke`, so a bare `cedar test`
never runs it. Anyone who has only ever typed the bare command has a whole suite they have never
triggered — run `cedar test --smoke` before a deploy that touches a critical path.

Three drift guards inside the backend phase fail the build rather than waiting to be remembered:
`SchemaDriftGuardTests` (`Entities.cs` moved without a migration), `UiInventoryDriftTests` (a screen
or `sec-*` settings section absent from `docs/design/UI-INVENTORY.md`), `DocsFlowGraphTests` (a doc
absent from `docs/DOCS-FLOW.md`, or a mapped path that no longer exists).

The build phase names no tests, so the grid draws nothing for it and its row reads "no results
parsed" — the verdict is its exit code, as for every phase. `--smoke` does not replace it: the smoke
harness starts the Angular dev server, which tolerates exactly what the production compiler rejects.

What none of it answers: how anything looks, whether a real provider accepts our payload, whether a
flow makes sense to a person, and anything that needs a second machine, a phone, or real money.
Those are the checks below.

## Before you check anything

- **Two safe forms for a local launch, and no third** — `.claude/rules/telegram-bot.md`. If the bot is
  not part of the check: `ASPNETCORE_ENVIRONMENT=LocalNoBot`, `--no-launch-profile`, and read the
  startup log for `Cedar:BotToken not set — bot is disabled` before doing anything else. That line is
  the only proof. If the bot *is* part of the check, stop `cedarclerk` on the droplet first and start
  it again after — and remember that stopping it also kills `/media/*`.
- **Telegram checks go to `@testingandfun`.** Never post to the real channel without explicit
  permission.
- **Some checks need configuration that is the maintainer's to create** — Google OAuth client and
  `@BotFather /setdomain`, the PostHog project key, Resend, provider keys. Each is marked below;
  `docs/for_user/integrations-setup.md` says where each one comes from.
- **A few checks cost real money or real Stars.** Those are the maintainer's alone and are marked.

---

## Editor and saving

This is the most incident-hardened surface in the project (ADR-065/066/067, after the 29.07 wipe —
`docs/archive/incidents.md`). Re-check all of it whenever autosave, the save endpoints, or
`ShrinkGuard` are touched.

- **Save guards.** Delete nearly all of a document that had real text in it. The save is refused with
  a dialog, not applied; the dialog's second button is "restore stored" and it brings the stored
  version back. Then make a heavy but honest edit (a table rewritten into paragraphs) — it must go
  through, because the measure is extracted text, not JSON length.
- **Restore from history.** Open the history modal, read a version, diff two, restore one. The restore
  button is disabled on the version that *is* what is stored, rather than writing a no-op.
- **flush-on-hide on a real iPhone.** Type, then close the tab. The pending save survives. Desktop
  proves nothing here — Safari kills a tab differently, and the 29.07 incident was on iOS.
- **Paste and drop.** A screenshot straight from the clipboard (Win+Shift+S → Ctrl+V) and a multi-file
  drag & drop: the progress panel appears and the files land at the drop point, not at the end.
- **The `[[` trigger on a Russian layout.** `[` needs the Latin layout — watch what the suggester
  actually does when the keyboard is Russian. If it cannot be reached at all from Russian, that is a
  defect and needs an alternative (a button or a command), not a workaround in the tester's head.
- **The tree.** Move a document via "Move under…", move it up and down among siblings, open a document
  from the editor breadcrumbs, and confirm the backlinks chip counts correctly after saving a `[[`
  link.
- **Sprint → devlog.** On a real sprint with done and open tasks and a released build, the "Devlog
  draft" button on the planner card assembles the three sections in the document's language and opens
  the editor.
- **Incremental re-translation.** Manual corrections survive a re-translation of a changed document.
- **The glossary tooltip on a live published post**, and per-language cross-links.

## Media and assets

- **Media library round trip.** Insert from the library, publish, and confirm the media renders on the
  blog *and* in Telegram. Delete an asset that a post uses — `409` naming the post titles. Delete a
  free one — the file and its `_tg` derivative both disappear from disk.
- **The asset window on cover and gallery.** The project logo and the showcase gallery both open the
  picker, uploading through it works, and the gallery appends rather than eating a hand-typed URL.
- **EXIF stripping.** Upload a phone photo that has GPS, download it back from `/media/`, and confirm
  the coordinates are gone in any EXIF viewer. Check the Telegram derivative too.
- **Assets follow their project.** A file used by a document belongs to that document's project, and
  the project's Assets screen separates Uploaded from On disk.

## Blog and showcase

- **A series of three posts.** `/series/{slug}` lists them, prev/next work from a post, "Part N of M"
  is right, and a private unlisted post does not shift a stranger's numbering.
- **OG previews.** Check through @WebpageBot or opengraph.xyz with a `?v=` on the URL — Telegram caches
  the old scrape. A public post gives the full card with image and description; a semi-public one
  gives title plus fallback; a private post and the gate give nothing. `/og-default.png` serves from
  the blog host.
- **The private-post gate, across browsers.** Fill the form in one browser, then open the same post in
  a second browser through the link the reader was given — it opens. A third browser with neither the
  link nor a cookie meets the gate. Hand-writing an access cookie proves nothing.
- **Showcase page.** Enable the public page in project settings, mark two tasks public: `/games/{slug}`
  shows the cover, the links, the devlog feed (private-listed posts locked, unlisted absent) and the
  roadmap. Archiving the project 404s the page.
- **Landing and waitlist.** From an incognito visit: the EN devlog-first page (an RU browser gets RU),
  the waitlist form accepts an address and swaps to the done-line, and the row lands in
  `WaitlistEntries` (`cedar db`).
- **Blog reactions and comments**, and a semi-public post on the blog index.

## Publishing

- **The publish guard.** Preview a target that has been published before, then change the document and
  confirm: publishing answers `409` with a fresh diff instead of overwriting, and the modal re-opens on
  that diff. Every language being published is shown, not one of them. Both Telegram and the blog.
- **A slow publish.** A post carrying tens of megabytes of media — watch that the author is told what
  is happening rather than being handed a proxy's 502.
- **Discord webhook.** Connect a real channel webhook in Settings → Integrations and publish a post
  with an image: the announcement lands, the blog link unfurls into a card with the OG image, and
  there is no `@everyone` ping even when the text contains one.
- **Bluesky with an image.** `uploadBlob` against real bsky.social. Maintainer only — list building and
  text are unit-tested, the upload is not.
- **X and Bluesky connection lives in Settings → Integrations only.** The Publish / Export tab selects from
  what is connected and offers a "Connect →" link where nothing is; it never grows a form of its own
  (ADR-095).

## Telegram

Read `.claude/rules/telegram-bot.md` before any of this — it holds the wire-level constraints these
checks are shaped by.

- **A real send to `@testingandfun`.** Local render is an approximation; only a send is a preview.
  Confirm media embeds with a natively-styled caption (muted, small, tight under the media), not as
  ordinary body text.
- **Own media goes by `file_id`.** A local file is pre-uploaded once to the storage chat and cached on
  the Asset; a second publish of the same file does not re-upload. With no linked Telegram, or after a
  failed upload, that file falls back to a stamped URL and the publish still completes.
- **A twelve-part thread.** Every part arrives; no part is held back by a poisoned media URL. External
  URLs (YouTube thumbnails) are never stamped, our own `/media/` URLs always are.
- **An empty media group.** Insert a carousel and delete all its images: the publish succeeds with that
  node dropped, rather than getting `RICH_MESSAGE_CONTENT_REQUIRED`.
- **Attribution is a security boundary** (the bot is shared across every account). Connecting a channel
  is refused unless the *caller's* linked Telegram is an admin of it; comment counting rejects anything
  that is not a reply to Telegram's own automatic forward in the linked discussion supergroup;
  `refresh-known-chats` touches only the caller's admin chats.
- **Stars payment by a third party.** Pay a credits invoice from a Telegram account that is not the
  payload account's: the credits land on the right account, and the reply says nothing about that
  account's balance or expiry. Maintainer only — it costs real Stars.
- **Tags in the Telegram export**, comment replies, highlight and name reservation.

## Billing and plans

- **AI credits end to end.** On Pro with 0 credits: an AI translate answers the top-up message (402);
  buying a pack unblocks it and the ledger shows the `ai` charge. A Pro+ payment lands 30 credits
  (`proplus-monthly` in the ledger); a trial payment lands none. The 21st call in a day answers 429
  whatever the balance.
- **Language paywall.** On Free the editor's `+` menu locks everything but EN/JA, the glossary form's
  locked options are disabled *and* labeled, and a direct API `PUT` of a new RU translation answers
  403 — an existing RU translation stays editable. On Pro everything unlocks.
- **Plan locks by eye.** On Free: silver locks on the signature field and Save and on slot 3; gold
  locks on every AI control (settings translate buttons, editor retranslate / translate-all /
  auto-translate, right-click AI entries, both glossary translate buttons, the form preset language
  chip). Every locked button is inert. On Pro the signature unlocks while AI stays gold; on Pro+
  nothing wears a lock.
- **Payment providers.** Each active processor takes a real payment and the credits or plan land.
  Maintainer only.

## Auth and onboarding

- **Sign in with Google.** Needs the Google OAuth client (`integrations-setup.md` §3d). Four runs, and
  the third is the one that matters: (1) a brand-new account signs up — the callback lands on
  `/auth/complete`, a wrong invite code is refused there, a right one creates the account and the blog
  address is the name that was typed; (2) that same account signs in again and goes straight in, no
  completion screen; (3) an account that already exists **with a password** presses Google on the same
  address — it must land on `/login` saying the address is taken, and the link is attached only after
  the password is accepted, never before; (4) unlinking the only way in is refused in Settings.
- **Sign in with Telegram.** Needs `@BotFather /setdomain`. An account with a linked Telegram signs in
  in one click; a Telegram nobody has linked answers "no account" rather than creating one.
- **A Telegram id belongs to one account.** Linking an id another account already holds is refused.
- **Onboarding door.** On a fresh account: register lands on `/onboarding`, guarded routes bounce there
  until a display name is saved, the blog address is shown read-only (shown, never assigned), and
  `returnUrl` survives the detour. An existing account with no display name goes through it once.
- **Example project.** "Create an example project" from an empty `/projects` builds Cedar Quest whole —
  board, sprint, build, devlog — and a new ordinary project's starter document opens with its skeleton
  in the UI language.
- **Sessions do not drop.** A failed `/api/auth/me` (network blip, 5xx, a deploy-window 502) shows a
  retry, not a password prompt; only a 401 clears the session.
- **Settings tabs.** Four tabs; the X OAuth callback lands on Integrations, the X credits note crosses
  to Billing → credits, and the account menu still opens Account.

## Collaboration — teams, shared projects, canvas

- **Teams end to end.** Make a team, invite a real address, accept from a second account, hand a
  project to the team, and confirm the second account reaches its canvas and nothing else. Then
  restrict them (the board goes read-only), ban them (the project disappears from their hub and the
  address cannot be re-invited), lift the ban and confirm the role they had comes back. A per-project
  viewer on a project whose team makes them an editor stays a viewer.
- **The invited stranger's way in.** Needs Resend configured. Invite an address with no account, open
  the mailed link in a private window, follow Register from the login bounce: the invite-code field is
  gone, the account is created, and the invitation opens straight after. This is also the only run that
  renders `EmailTexts.ProjectInviteBody` and `TeamInviteBody` — a run against an unconfigured mailer
  takes the designed fallback and proves nothing about the templates.
- **Shared with me.** As a member, the hub's fourth tile lists the project, its card opens the canvas,
  and the count is right after a second invitation. As an owner, the tile is empty and says so.
- **The reference board with two real people.** Two browsers on two machines — a Playwright driver on
  one machine adding a note is what let the image hole through review. Drop an image and pick a library
  image, draw a frame around them, add a link, resize, and fit-to-content on a board holding one item.
  Then the failure half: kill the server under an open board and watch the offline overlay and Retry,
  demote a live editor to viewer, and delete a board someone else is drawing on.
- **A shared board's pictures are readable by the board's people** and by nobody else.

## Stats and analytics

- **Analytics end to end.** Needs the PostHog key in the systemd drop-in (`integrations-setup.md` §3c).
  In a private window on `/welcome`: the banner appears and — **before pressing anything** — the
  Network tab shows no request to `eu.i.posthog.com` at all. Decline makes it go away and keeps it away
  on reload; clearing `cedar_consent` brings it back; Accept loads `/static/array.js` and a `$pageview`
  lands in PostHog → Activity. Then the same banner on `/login` (it is outside the shell). Register a
  throwaway account and watch `signup_started` and `signup_completed` arrive under the same person —
  the identify is the join, and if it is broken the funnel silently reads as two people. Last: publish
  something and confirm `post_published` **and** `post_published_first` both arrive, then publish again
  and confirm only the first one does.
- **Stats sparklines.** On a source with a real series the line matches the chart's shape; under three
  readings there is no line; a flat series draws on the middle, not the floor.
- **The stats board.** No source selected, one source versus several, a dried leaf, the table view, and
  the axis shortening when a young channel is ticked.
- **Telegram engagement.** Reactions and comments both arrive on `ChannelPost` and sum nightly; views
  are absent for a Telegram source and say so rather than reading zero.

## UI, presets and the shell

- **Project and export presets.** A project preset appears in New project and builds what it describes
  (type, first document, its title, the description), and editing the fields after picking it still
  wins. An export preset fills the Publish / Export tab's rack in one pick, and a language the post does not
  have is ignored rather than ticked. New project offers Empty / Blog / Game / Product; Empty's first
  note has no skeleton, and an older jam or prototype project still names its type on the dashboard.
- **Post card thumbnails.** On an account with existing posts the first listing fills covers in (50 at a
  time), a post with no picture keeps its type icon, and adding or removing the first image changes the
  plate on the next listing.
- **The shell, by eye.** Walk every authenticated screen: which hook lights on which route and what the
  crumb says, the dots menu (theme, Appearance, Glossary, Admin, `/dev/*`), the drawer lip opening onto
  the journal, the ruler showing the editor's counts and blank everywhere else, and the four routes
  that stay outside the shell. Static checks cover the CSS; the route table and the crumbs are
  behaviour.
- **The palette, by eye, in both themes.** The state washes, the avatar initials in the editor and the
  admin list, the blog's channel avatar, and the focus ring on paper and on rail. The contrast phase
  measures ratios; it does not look at anything.
- **Both themes on the four doors** — login, register, terms, privacy — which declare their own surface
  outside the shell.
- **New UI goes where its concern already lives.** Before adding a control, grep
  `docs/design/UI-INVENTORY.md` for the area and put it in the panel that owns it; the row lands in the
  same commit as the code (`.claude/rules/ui-changes.md`).
- **Russian wording, screen by screen** — the UI is fully on `t()`, server errors are not.

## Desktop

The full risk list is `docs/tech/DESKTOP.md` §Risks. The checks that need a real machine:

- **The installer on a clean machine.** It builds and is verified on artifacts; it has never been run
  on a machine that does not already have the project.
- **`cedar build --installer` and `cedar deploy --desktop` end to end.** Not run since the pipeline
  moved to C#; installer build and Cloudflare distribution were verified only on the old pipeline.
- **After ADR-117**: cloud `/projects` without the price table, a scan of a real Unity or Blender
  folder, the orientation of a real `.blend` preview, cancelling mid-preview-pass and resuming, zero
  processes of either kind after closing the window (an orphan holds the SQLite WAL lock and the next
  launch finds a database it cannot open), and `curl` to the agent without a token → 401.
- **The startup log says `Cedar:BotToken not set — bot is disabled`.** A desktop build that polls would
  knock production's bot off its token.

## Deploy, backups and the CLI

- **`cedar restart`** — the CLI's only destructive command, and it drops the blog together with the app
  for a few seconds. Run it when that costs nothing, and treat it as a
  `.claude/rules/destructive-operations.md` event.
- **The deployed build is actually the new one.** A version-string match proves nothing when two builds
  carry the same number: check a behaviour only the new build has, and bump `Consts.CurrentVersion`
  before deploying so the cheap check works next time.
- **`cedar status` and `cedar backup verify` agree with the server.** The nightly copy's destination and
  the path the tool reads are one fact in two places; they have disagreed for a day before. Copies are
  counted by `cedar-*.db.gz`, never by a bare glob — `backup.log` shares the directory and is written
  after the copy.
- **A restore, monthly.** Download a dated copy, gunzip it over a scratch `cedar.db`, and open the app
  against it. Restore the media half from R2 the same way. A backup nobody has restored is a hope with
  a cron entry. Cadence: `docs/product/BUSINESS.md` §5.
- **Both healthchecks ping.** The on-droplet copy and the off-box copy have separate checks on purpose —
  sharing one would let either failure silence the other.
- **`Scripts/server/backup.sh` reaches the droplet only by hand.** `cedar deploy` replaces the app
  directory and nothing else, so a change in the repo is not a change in production until someone
  copies it across.

## Small things with no home

Once each, no rush: DeepL's uk/be/ka refusal with a clear message; the translate-all modal; tag rename
and delete; audit paging past page one; the emoji panel; the paragraph-mark toggle; the two form field
types on a real gate; the Posts Manager submission modal and "mark all as read"; the Appearance panel's
Apply-gated autosave; folder delete.
