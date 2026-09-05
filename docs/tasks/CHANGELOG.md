# Changelog

## 2026-09-05 — Backlog sweep (ADR-260…288)

One session, many lanes, fifty-five board rows closed. Everything below is on `claude/backlog-sweep`,
verified by unit and end-to-end tests only — nobody has opened a screen — and not merged or deployed.
`Consts.CurrentVersion` is 0.22.0. Four migrations ride along, every one an added column or table:
`AddPublishJobSilentPin`, `AddPublishTargetStatSnapshot`, `AddProjectEngineAndPlatforms`,
`AddDraftLastTelegramSentAt`.

**The bench draws the whole form (ADR-260).** `app-select`, `app-textarea` and `app-checkbox` join
`app-input` in `bench/forms/`, `app-input` passes `maxlength` through and takes an accessible name
without a visible label, and `app-task-tag` preserves the query it sits beside — T-251, T-252, T-254.
With the controls in place the modal footers followed: the editor, Posts Manager, project builds and
the project hub now footer in `app-button` (T-263), and the four picker dialogs T-262 named turned out
to use it already. Seventy-three orphan dictionary keys are gone from both languages, ten kept as
computed lookups (T-264); the comments' thumbs-down is an icon and the admin star no longer exists
(T-269); the peer face reads `--avatar-size` instead of three literals (T-312); presence colours are
six on both sides of the hub (T-311). Drafts' card rows are laid out by kind (T-030), and the Posts
Manager, form picker, glossary and Settings fields that had only a placeholder for a name have one
(T-103, the editor's dialogs remain). Skeletons are one `app-skeleton` held for 300 ms with
`aria-busy` on the region, used in admin (ADR-286, T-184 keeps the Posts Manager and editor). A dialog
leaves through Escape, the ✕ or an action, and the scrim is not a button (ADR-285, T-043). The
router cross-fades except around the editor, and the blog carries a post's cover into its hero; both
stop under reduced motion (ADR-287, T-185). A custom accent is admitted at 3:1 on the paper and
`--accent-ink` is set for it; the four area presets — Telegram, iPhone, iPad, blog — are a macro over
the sheet controls (ADR-288, T-047).

**Build, test and the deploy gate.** `editor.component.css` is four files split by meaning and the
initial bundle ships only the active language, the Russian dictionary lazily (ADR-263, T-102,
T-363). `cedar test` runs `ng build` as its sixth phase, and the deploy preflight warns when the
newest `cedar-*.db.gz` is stale (ADR-265, T-362, T-195). The hanging-link test asserts the cap was
applied instead of measuring wall-clock, so it no longer races the suite (the flaky T-364).
`e2e/helpers.ts` derives the blog and landing origins from `E2E_BASE_URL` and `E2E_API_PORT` (T-266);
both canvas screens have page-level specs (T-308); the `SaveAsync` concurrency branch and
`CanvasHub.Leave` have tests through a fixture that can arrange the race (T-307). The smoke suite's
cold run on this branch passed — 60 passed, 18 audit-only skips — and the login spec repeated ten
times went 40/40, which closes T-317 and T-173.

**Publishing and Telegram.** `disable_notification` and pin-after-send travel through the queue into
`PublishJob`, so an immediate send honours the toggles the scheduled path already did (T-325). A
publish revision records the source document rather than the wire copy with Telegram's media paths,
so the next diff shows only what the author changed (ADR-269, T-104). `POST /api/posts/{id}/telegram-sync`
edits the one message the last send left and answers with Telegram's own verdict; `Draft.LastTelegramSentAt`
is what "edited since" is measured against, and a thread answers 409; the editor's Telegram state
row carries the Sync button (ADR-275/278 — T-180 keeps only the live check against a post older
than 48 h). The link probe checks every
redirect hop against the private ranges and dials only the address it resolved (ADR-268, T-324).
`article:modified_time` is clamped like the JSON-LD date (T-319); `GET …/preview-link` reads an
existing token back so the editor can show it in a later session (T-320); a post older than the
nightly snapshots says so once, on the chart (T-105).

**Stats.** X and Bluesky get `PublishTargetStatSnapshot` and `SnapshotPublishTargetStatsJob` at
04:10 UTC, and the Stats tab grows a leaf per target as soon as a row exists (ADR-273, T-241).
`GET /api/stats/series` answers every selected source over one aligned window with `available[]`
beside it, the window delta replaces Δ7d, and `series.csv` is the same query as a file (ADR-279
supersedes ADR-161 rule 5; T-242, T-243). The invite shelf draws the per-day join/leave bars from
the series it already had (ADR-271, T-327).

**Project hub, tree and forms.** `Project` carries an engine and target platforms as closed-vocabulary
keys, comma-joined in the column and refused by the API when unknown, and the hero tag fills in
(ADR-274/276, T-247). The hub's journal is a union of timestamps the module already writes, and the
publishing nudge is one date on that list plus a sentence and a number the hub names as the
account's (ADR-277/281, T-249, T-166). `ProjectSummary` carries the build count and latest version
(T-246); admin audit rows have a severity derived from the action (T-258). A tree node moves by its
grip on one flat drop list, depth read off sideways travel (ADR-283, T-201); the asset list view is
virtualised, the grid appends on scroll by decision (T-142). A registration can be revoked and
restored from the Forms tab (T-108).

**Canvas.** The board list asks `/access` instead of an owner-only endpoint (T-303); the batch cap has
its own sentence in `ErrorMessages` (T-306); the People panel is `shared/project-members-panel`
(T-309); `saving` counts writes in flight (T-310); the empty state and the rail no longer both offer
"New board" (T-313); the key list names every key the handler implements (T-314); the resend endpoint
is an addendum to ADR-217 (T-316).

**Errors in every UI language (ADR-284, T-194).** Fifty-eight interpolated server strings across
fourteen files moved into `ErrorMessages`, and the table now speaks all nine UI languages —
en/ru/de/fr/es/ja/uk/be/ka — from one partial file per language keyed by member name. The seven new
translations are first passes, not native-reviewed.

**Docs and research.** `docs/tech/SECURITY.md` is the threat model — assets, trust boundaries, a
STRIDE table with each mitigation cited, and sixteen gaps G1–G16 as candidate rows (T-190; the
triage is T-376). The four research rows went into `docs/knowledge_base/RESEARCH-2026-09.md`, each
section ending in a verdict: IndieDB has no write API and is a copy target; LinkedIn is a post-now-only
connector or a copy target; IGDB autofill is small but waits on one email to IGDB; the Telegram
first comment is one to two sessions; no upstream events feed exists, so a curated seed (T-127,
T-169, T-170, T-171 → T-380…T-384). T-275 was deleted from the board — it had been marked done in
its own text.

**Board bookkeeping.** New rows T-370…T-384 (the `npm audit` row was re-numbered from a duplicate
T-364), T-180/T-184/T-103 narrowed to what is left. A machine without the ASP.NET Core 8 shared
runtime needs `DOTNET_ROLL_FORWARD=Major` for `dotnet test` and `dotnet ef` — in `docs/tech/QA.md`.

## 2026-09-02 — Queryable collections and document-first editing (ADR-247…251)

The app now owns one headless collection contract instead of adopting a themed data grid: visible
queries and facets, keyboard-reachable sort headers with `aria-sort`, stable ID tie-breakers,
filtered empty states with one reset, and route-query restoration where a manager already owns its
view in the URL. Posts and Forms, Drafts, Media Library, Project Assets, Project Tasks, Teams,
statistics series and the table-shaped Admin reports use it. Server-paged Admin, Media Library and
Project Assets collections validate their criteria and filter and sort before pagination.

Posts Manager's list is a compact document-first shelf with reliable cover fallbacks, a title that
owns the flexible width, readable language and visibility metadata, one state stamp and selection
that survives a query change. Drafts' table titles are native links. These changes close T-271;
the remaining tree/card link decision stays in T-261.

Navigation has one door per destination. All projects and Manage teams live in the Project
switcher; Teams no longer reads as a Library item; Settings lives in the account popover; and the
sidebar has no duplicate persistent collapse command. The popover opens beside the rail, stays in
the viewport, supports native focus order and restores focus according to Escape or outside-press
intent. Settings is Profile, Preferences, Integrations and Billing. Preferences combines Interface
language with the only authenticated Appearance surface, which owns every persistent app and
writing-sheet visual choice. The Account-only-language tab and separate Appearance overlay are
retired. This closes T-267.

The Editor gives equal, readable weight to Write, Preview and Publish, removes the repeated title,
ellipsis and permanent Details button from its tall top band, and exposes History and a compact
optional inspector in the document frame. Normal, Wide and Full sheet measures now produce 760px,
960px and an uncapped working surface. At phone width the shell temporarily uses the rail without
changing the saved desktop choice; the document remains visible, the three workflow labels remain
whole and Settings tabs form a two-column grid. This scoped safety rule closes T-265 while the full
physical-device and responsive-design pass remains T-034/T-236/T-237.

Configured Website, GitHub, YouTube, Mastodon, Bluesky and itch.io links now appear in a safe,
tenant-scoped Author links disclosure on every public Blog. Hover, focus and press can reveal it;
Escape and outside press close it. Only absolute HTTP(S) URLs render. Browser captures cover the
rebuilt manager, project/account navigation, Preferences, all four Editor measures, public Author
links, and the 1180/820/390px layout. They are layout evidence, not physical-device or human
accessibility verification.

Validation: `cedar test --smoke` is green: 2,654 results, 2,636 passed, 18 skipped and 0 failed;
the 78 Playwright scenarios use an isolated database. The targeted visual capture passed 5/5, and
`cedar build --yes` produced the v0.21.0 server publish and Electron shell. The production build is
green at 836.16 kB under the 850 kB hard ceiling; its 700 kB bundle warning and the Editor
stylesheet's 34.24/34 kB warning remain visible as T-363. Nothing was deployed.

## 2026-09-02 — One UI across the eighteen-screen set (ADR-246)

The supplied 3440×1392 screenshots are now one acceptance set rather than a list of local CSS
patches. The sidebar has one persisted expanded/rail choice on every route, retains the same grouped
navigation and Project context in both modes, and no longer changes shape when the Editor opens.
Pages declare a shared focus, form, operational or showcase measure; bounded list and inspector
tracks give the working surface the remaining width, and the common responsive flow stacks them
before content clips. The Editor sheet includes its gutters in that width, and its Details inspector
keeps the shared two-column contract.

Lists, selected rows, pick-one controls and empty states now share visible and programmatic state.
Forms, Presets and Teams expose one creation path instead of duplicating a header action beside an
empty card. Calendar, Planner, Tasks, Dialogues, Assets, Settings and Posts use one vertical scroll
owner, wrap long metadata, and keep their actions inside their panels. Appearance, payment and
export choices expose `aria-pressed`, `aria-checked` or `aria-current` as appropriate. Modal,
popover, search, console, feedback and Project-switcher surfaces coordinate through one overlay
stack with focus trapping, focus restoration, an inert background and topmost-first Escape.

Discovery keeps its privacy and eligibility rules but presents a compact editorial stage: search,
lenses and Shuffle form one band; only sections with real material render; the honest zero state
explains how to publish into the commons instead of repeating empty feeds and zero-count categories.
Its short state now holds the footer at the viewport edge. The public Showcase has its own bounded
1440px Project composition: a cover-or-monogram hero, positive-only real-content facts, paper
section cards and deliberate About/Devlog empty states, with uncropped key art at every breakpoint.
The public Showcase and all authenticated screenshot surfaces were rendered at the source size;
the responsive pass also covered 1440×900 and 900×900, both sidebar modes, populated and empty
states, Editor history and Feedback. No visible document-level horizontal overflow remained.

Validation: `cedar test --smoke` is green with 2,591 results (1,947 backend, 568 frontend and 76
Playwright; 17 conditional skips), including icon, contrast and density contracts. `cedar build
--no-desktop` produced the Angular and portable server release. Nothing was deployed.

## 2026-09-02 — Project pages and account time (T-357, T-323, T-322)

Showcase is now an ordered, versioned page rather than a fixed game template. Its editor has a
block rail, live result and per-block inspector; owners can reorder, hide, add and remove the safe
Hero, About, Links, Trailer, Gallery, Downloads, Devlog, Follow and Roadmap sections. Pro Plus can
ask the existing background AI job pipeline to polish, shorten or propose three alternatives for
the selected prose, then review and apply the answer locally before the ordinary page Save. The
server normalizes the same contract, encodes every authored string and keeps old Projects on the
compatible default. Public pages and companion routes use canonical `/showcase/{slug}` URLs while
`/games/{slug}` remains readable. The press kit and Discovery vocabulary now describe Projects of
any kind. ADR-245; schema change `AddShowcaseBlocks`.

Every account now owns an IANA display timezone, stored in the profile and applied to authenticated
dates, Calendar wall-clock input, standalone exports, Blog pages, Showcases and header slots. UTC
remains the wire and database contract. Invalid zones are refused; missing legacy values fall back
to `America/Los_Angeles`; DST gaps are refused and repeated wall times choose the earlier instant.
ADR-244; schema change `AddUserTimeZone`.

Calendar's Week view is live over the same scheduled-post and queue-slot projection as Month. It
shows one seven-day row, moves by seven days, keeps Today and Schedule, and groups and edits wall
times in the account timezone without adding an endpoint.

## 2026-09-02 — Discovery for independent makers (T-369, ADR-243)

The public front door now speaks to independent makers of games, apps, tools, art, film, music,
hardware and personal Blogs. The landing keeps its waitlist and product proof, but adds a live
privacy-filtered Discovery preview and a direct public route. `/discovery` is a server-rendered
shuffled mix with search, Project/Blog lenses, a `#ScreenshotSaturday` stage, independent Blog and
Project Devlog sections, Project Showcase cards and fixed bilingual categories.

Consent is explicit at both levels. Settings → Profile adds an account opt-in that defaults off;
Discovery still admits only a live Showcase or a public Blog post, and never admits a private post
or a tenant-local `IsListedWhilePrivate` teaser. Showcase owns the Project category. Admin →
Discovery can pause the page or its three public sections and edit its bilingual introduction, but
cannot opt an author in. The schema change is `AddDiscovery`; eight focused backend privacy/type
tests and the frontend suite cover the new contract.

## 2026-09-02 — Publish / Export is the third tab

The editor's third tab is a real state: `?tab=publish` selects it, deep-links to it, and holds the
Export window's steps as a three-column workspace — the destination rack, the active destination's
settings under the version row, and a readiness review — with a stepper across the top that moves
focus to the region it names, and the file exports in a utility strip underneath. The modal and the
top-bar Publish split button are gone; the footer carries one primary action per state (Continue to
Preview · Continue to Publish · Publish to N destinations) with Back before it. A destination card is
two controls — a labelled checkbox and a settings button — instead of a checkbox inside a button.
Preview and Publish read their checks under one vocabulary (Blocking · Warnings · Ready · Needs setup ·
Unavailable), every state an icon with a word, a Fix only where a route or an action exists. The
publication checklist stays a modal over the workspace; Write stays mounted behind both tabs. The
tool strip's groups are History, Text, Insert, Lists, Media, Blocks and More, and Details opens
closed. ADR-242.

## 2026-09-02 — The sidebar carries every screen

An open project's sidebar no longer empties while the access answer is in flight: an own project
draws the owner's wall at once, and the hub draws the account-wide screens instead of nothing.
Glossary, Presets and Teams left the account menu for a fourth sidebar group, Library, and the
menu shrank to what belongs to the person — a three-tile Display row for appearance, theme and
fullscreen, the developer doors for an admin, the console and About as quiet rows. ADR-240.

X posts now carry up to four of the document's pictures, uploaded through the v2 media endpoint;
the connect flow asks for `media.write`, and an older connection says so on its row until it is
reconnected. A publish matrix — one row per kind of content, one column per destination, every
cell read off the network's capabilities, the document's own rows lit — sits under the Preview
tab's destinations and in the Publish window's *Where to publish* step. ADR-241.

Settings is a two-column paper page: a section index at the left that follows the scroll, one
reading column of cards that open on a serif title and a lead line, field rows with a label column,
integrations as rows with a brand mark and a status line. The editor fits a tablet in landscape:
the Preview tab folds to one column under 1180px (destinations strip, render, checks), the document
frame takes the page gutter, chips in the inspector stay chip-sized under a finger, the hub's
document rows keep their titles, and the document can no longer scroll under the fixed shell — the
iOS keyboard shift that pushed the top bar off the screen.

## 2026-09-01 — Editorial studio and readable screens

Posts Manager now opens a selected post as a read-first editorial studio: publication state travels
from Draft through Blog, Telegram, X and Bluesky, the current view total sits beside its nightly
trend, and the latest destination activity is visible before any settings. The existing metadata
controls stay in their disclosure groups, and outbound links stay in the inspector.

The editor's Preview tab renders X, Bluesky and Discord: the announcement with the blog link, or the
thread as its parts, projected on the server by the same builders and splitter the targets run
(`GET /api/drafts/{id}/preview/micro`). The checks column names the account, the length in the
network's own units, whether the text is the author's own or a teaser, the blog link and the
pictures the network would attach. Closes `T-367` from ADR-239.

The six supplied ultrawide surfaces now share a bounded working measure. Metrics gives its audience
rail and readouts more room; Projects uses a bounded card grid and a two-column New Project matrix;
the editor and Publish window have wider inspectors and destination settings; Settings is centred
instead of leaving an empty half-screen. The global page gutter now contracts with the viewport.

## 2026-09-01 — Inspectors that can be written to, and the facts they were missing (S-15, ADR-238)

Six board rows that were one complaint in different places: a shelf describing an object it cannot
change, or naming a property nobody stored. Reading the code first changed the sprint — **T-270 was
already shipped** (`DocumentType` is on both draft projections and the editor's Type row is a live
`<select>`), so it closes as already done rather than as this session's work.

**T-239 — `field` is a promise, and a row that hosts no control loses it.** `app-spec-row`'s sunken
paper box moved off `.text` onto `.value`, so a projected input or select *is* the field instead of
standing beside one. The media **source** row dropped it (the file itself cannot be typed over) and
so did the **link href** row (the link mark's own dialog owns it, and a second weaker editor is not
worth building). The **Location** row is a third the ADR's list did not name: `app-location-input`
draws its own bordered input with a profile chip stacked under it, so the row's box would have
wrapped a two-line composite in a one-line field. And the **slug row became a real edit** —
`POST /api/drafts/{id}/slug` and `DraftsService.setBlogSlug` have existed since FI3.4 and the port
simply stopped calling them. Every rule stays on the server, so the field shows the stored answer
back rather than the keystrokes and surfaces the refusal instead of inventing one. Alt text was
already editable before this sprint; it keeps `field`, and its input carries the global `.field`
opt-out class so one box is drawn rather than two.

**T-240 — one attribute, and the facts come from the server.** `assetId` (default null,
`data-asset-id`) on image, video and audio; a node without it renders, saves, exports and publishes
exactly as before, because every renderer reads named attributes and ignores the rest.
`GET /api/assets/meta?id=|path=` answers resolution, byte size and origin. **Nothing is
denormalised into the document and no column was added to `Asset`**: a resolution copied into a
document goes stale the moment a derivative is written, and `Image.Identify` reads a header rather
than an image with ImageSharp already a dependency. That is what removed a migration plus a backfill
pass over ~937 MB of existing media. The lookup resolves by id **or by media path**, so documents
written before today answer too — an id-only endpoint would have been true and useless.

**T-256 — `/drafts` gets a shelf, and a row keeps its one meaning.** A read-only inspector over the
picked document, selected by an explicit control in the existing `.row-actions` cluster and never by
the row's own click: a row is a door to the editor (ADR-163), and a click that sometimes opened and
sometimes selected would break the one interaction the board is for. Exclusive with the Folders
shelf, which is what the column falls back to (ADR-167), and withdrawn in tree view alongside the
other filters. No new endpoint — `DraftMeta` already carried every field it states, so picking a row
asks the server nothing.

**T-257 — the admin per-user form is a modal, by the maintainer's ruling.** The row was `#decision`,
and the choice was made over both the 340px shelf ADR-164 clause 5 refused and leaving the form
inline. A pure move: same handlers, same endpoints, the existing `app-modal` as the shell. Two
things came with it. The user card is now a real `<button>` — with nothing nested inside it, it is
keyboard-reachable and no click has to be stopped from propagating. And the delete confirmation
**closes** the user modal instead of stacking on it: `ModalComponent` binds
`document:keydown.escape` on every mounted instance, so two on screen would let one Escape dismiss
both, taking the destructive question away with the context that explains it.

**T-260 — glossary hits are recorded where the text changes and where the term changes.**
`GlossaryTermUsage` holds one row per (term, draft) pair and none at all for a pair at zero, and
`usedInDrafts` rides `GET /api/glossary`. Deliberately **not** `usedInPosts`: since ADR-102 a post
is one of six `DocumentType`s, a term appearing in a changelog and two design documents is used
three times, and `TERMINOLOGY.md` makes `Draft` the entity word. The scan runs on plain text through
the matcher `Mark` already uses and ignores `DraftGlossaryExclusion` — an exclusion is a publishing
decision, and a writer asking where a term is used wants the text, not the render. Beside the count
travels `shadowedByTermId`, because `CountHits` is a single non-overlapping pass: where two terms
spell the same thing only one is credited, and a bare `0` on the loser is indistinguishable from
"this term appears nowhere in your documents" — two readings that call for opposite actions. The
count is not zeroed for it, since a shadowed global term is still genuinely used everywhere the
other one does not reach. Migration `AddGlossaryTermUsage`, add-table-only, and the sprint's only
schema change.

Three things about how the ADR itself moved, because each reads as drift otherwise.

**The ADR said six write paths; the code has sixteen.** Clause 8 was written as a census of call
sites, and the C# lane's sweep found eleven more: template instantiation on both branches, both AI
background jobs, revision restore, `.cedar` and markdown import, project starter documents, preset
bodies, changelog-from-build and devlog-from-sprint. A count labelled "used in N" that is quietly a
different N is a lie rather than a delay, so the clause was rewritten as an invariant — *any
endpoint that changes a draft's stored text calls `SyncForDraftAsync` before it returns* — and
`GlossaryUsageInvariantTests` now enforces it, with a reasoned allow-list and a second test that
validates the allow-list itself. It was verified to go red twice: once by the lane that wrote it,
once independently.

**Clause 1 went stale the same way inside one session** and was rewritten as an invariant for the
same reason: `field` belongs on a row whose value container *is* the control, never on one that
paints its own face. The test a future row is held to is one question — strip the row's box, does
anything still draw an edge?

**The lead found an error in his own clause 13 and corrected it rather than blessing it.** "The
stricter of the two case flags" was wrong: a case-insensitive `"Unity"` and a case-sensitive
`"unity"` both match the text `unity`, so they genuinely compete there and the loser then shows the
bare `0` the field exists to prevent — the field would have stayed silent in exactly the case it was
added for. The rule is to compare case-insensitively **unless both terms are case-sensitive**, which
still answers *no collision* for `"IT"` against `"it"` when both are, since no string matches both.

Two documentation rows closed with the sprint. **T-187** — `docs/tech/QA.md`, the permanent
verification checklist: a row naming a behaviour is permanent and lives there, a row naming a task
id and a date is a moment and stays on the board. **T-198** — `docs/archive/incidents.md`, an index
of 24 incidents across 8 groups, linking the narratives that already existed in ADRs, the CHANGELOG
and the rule files rather than rewriting them.

Tests: **1759 `CedarClerk.Tests`** (+54) plus **127 `CedarClerk.Cli.Tests`** — 1886 backend in all,
and the CLI half was never counted in the numbers earlier entries report. 518 frontend (+17).
`cedar test` green across all five phases, every drift guard included; the icon inventory was
regenerated at 86 icons over 445 call sites. **Nothing has been opened by a person** — the S-15
eye-check is in `docs/tasks/TASKS.md`.

## 2026-09-01 — Sign in with Google and Telegram (T-003 part, ADR-237)

Two of T-003's four providers. **Apple is deliberately not among them** and now carries its costs on
the backlog row: a paid Developer Program membership, a `client_secret` that is a self-signed ES256
JWT Apple caps at six months, Private Relay addresses our Resend sender cannot reach without being
registered with Apple, and a name returned only on the first sign-in. Bundling that would have held
the two cheap providers behind it.

**The invite gate applies to a provider button exactly as it applies to the password form.**
Registration is invite-only, and a Google button that made accounts freely would have opened it to
anyone with a Google account — through a door nobody decided to open, leaving the `BUSINESS.md`
§1/§2 gates behind the fact. Signing in is free; signing up goes through the same check, which moved
out of the register handler into `AuthEndpoints.ResolveInviteAsync` so the two callers cannot drift
apart. Nine tests now hold it there.

**An address that already belongs to an account is never merged on the provider's word.** The
callback sends that person to `/login?external=link`, the password proves the account is theirs, and
the link is attached right after it is accepted — best-effort, because a failed link must not become
a failed login. `email_verified` is a claim about Google's world, not ours, and treating it as proof
of ownership over our account is the standard takeover path.

**Telegram signs in; it does not sign up.** It carries no email, and email is what invitations,
receipts and recovery run through — inventing an address to satisfy the model would put a fiction in
the one field that must be writable. An unknown Telegram gets a 404 saying so. `TelegramLoginVerifier`
is reused unchanged from `/telegram/link`.

New: `ExternalAuthEndpoints` (`/api/auth/external/*` — challenge, callback, complete, link, the
account's login list, unlink, and Telegram), `/auth/complete` for a Google sign-in with no account
behind it yet (account name and invite code, the same pair `/register` asks for, on the same fields),
a provider row on both doors, and a Google mark in `brand-icon`. **No migration** — `AspNetUserLogins`
has been in the schema since `InitialCreate`.

Two things kept from going wrong on the way: the external cookie is cleared **by name**, because a
bare `SignOutAsync()` clears every scheme including the application cookie and would drop the session
just created; and unlinking refuses to remove the last way into an account, which with no password
set would leave it reachable only by hand.

Configuration is two lines for Google (`Cedar:Auth:Google:ClientId`/`ClientSecret`) and, for
Telegram, no keys at all — but `@BotFather → /setdomain`, without which the widget silently refuses
to render. Both in `integrations-setup.md` §3d. Neither configured means neither button is drawn.

Tests: 1705 backend (+9), 501 frontend. **Nothing has been opened by a person.**

## 2026-09-01 — Analytics: PostHog behind a consent nobody assumed (T-153, ADR-236)

The provider question open since 13.08 is answered: **PostHog, EU cloud**. It was the one of four
candidates that computes funnels, cohorts and retention directly — three of the four `BUSINESS.md`
§4 metrics are those shapes — and free well past this install's volume. Self-hosting was refused on
the spot: the droplet is 1 vCPU / 2 GB with no swap, and a ClickHouse beside the app trades a few
dollars for an out-of-memory kill that takes the service with it.

**The nine dictionary events of `METRICS.md` §4 are wired at the point each happens.** Eight on the
server — `signup_completed` (carrying how the account got in), `draft_created` on the empty-handed
create only, `post_published`/`post_published_first` off `PublishJobRunner`'s success, the three plan
events across Stripe, PayPal and Stars, `credits_purchased` against the **owner** rather than the
payer (T-359 finding 7), and `ai_used` at `ChargeAiOrRefuseAsync`, the single gate every AI call
passes, carrying `outcome` so a refusal counts too. One on the client: `signup_started`, the only
funnel step the server cannot witness, because `signup_completed` is written when the account already
exists and the people who tried and were refused would otherwise be invisible.

`ProductAnalytics` is the only route to the provider and **never throws** — it sits inside the Stripe
webhook and the publish queue, and a metric is not worth a failed payment. Unconfigured it is a
no-op, so a local run and a self-hosted install carry no client at all. The names live in
`Consts.Analytics.Events`, which a test asserts against the METRICS list.

**Consent is asked, and nothing loads before the answer.** ADR-126 clause 4 allowed a banner only by
the maintainer's deliberate decision, and this is it: PostHog's own cookie is kept, and a banner with
Accept and Decline stands in front of it. The SPA imports the library dynamically and the landing
appends its script tag only after an accept, so an unasked visitor has no provider code running —
stronger than initialising it opted-out. One `cedar_consent` cookie is read by both surfaces, so
answering on either settles both. The landing decides in the browser rather than on the server
because it is served `Cache-Control: public, max-age=300`, and a server-rendered banner would hand
one visitor's answer to the next out of the edge cache.

`/privacy` gains §4 "Analytics cookies" (the sections below it shift down one) and is dated
1 September 2026: it names the provider, the region, the cookie and its year, and states the two
exclusions outright — post content is never sent, and **blog readers are never counted this way**,
whose statistics stay our own and cookie-free.

Configuration is three lines in the systemd drop-in (`Cedar:Analytics:Enabled`, `:ProjectKey`,
`:Host`), documented in `integrations-setup.md` §3c. The project key rides `/api/health`, which the
frontend already calls at startup — it is public by construction, and building it into the bundle
would leave a self-hosted install unable to turn it off.

Tests: 1696 backend (+8), 501 frontend (+5). **Nothing has been opened by a person** — the
eye-check is in `docs/tasks/TASKS.md`.

## 2026-09-01 — Sprint v0.2.0 closed: teams, three preset kinds, and the sprint's remainders

**Teams (T-358, ADR-235).** `Team` and `TeamMember` beside the per-project membership ADR-217 built,
with `Project.TeamId` pointing a project at one. A team always belongs to a user and there is no
transfer; there is no team admin, so inviting, roles, statuses and deletion are the owner's alone.
`ProjectAccessResolver` asks `ProjectMembers` first and falls through to `TeamMembers` only when
that misses, so a narrower per-project grant is never widened by a broader team one. A status is a
second column rather than two more roles: `restricted` resolves to a viewer everywhere the team
reaches, `banned` resolves to nothing and clears the pending token so the address cannot walk back
in through its own mail. Deleting a team takes it away from its projects and never deletes one. A
`/teams` screen off the tray (list, sheet, inspector — the Posts Manager's shape), a one-row team
picker in the project's own edit dialog, `/team-invite/:token` reading through the same accept
screen as a project invitation, and migration `AddTeams`.

Three sprint items closed on the way. **T-301**: the shell asks `GET /api/projects/:id/access`
before drawing its wall, so a member sees the canvas — what a membership actually opens — instead of
seven hooks that answered 404. **T-302**: "Shared" is a fourth tile on the project hub's own state
strip, listing `/api/projects/shared` (which now also answers the projects a team reaches); losing
the invitation mail no longer loses the project. **T-304**: a live, unspent invite token stands in
for the registration invite code, read out of the `returnUrl` by the register screen, which says the
field is unnecessary rather than leaving an invited stranger hunting for a code.

**Three preset kinds (T-331 remainder, ADR-234).** The preset API moved from named document fields
to a free-form `Config` the kind's own record parses — `ProjectPresetConfig` and `ExportPresetConfig`
join `DocumentPresetConfig` in Core, and the endpoint re-serialises from the record so nothing
unvalidated reaches the database. The kind is fixed at creation. A project preset supplies the type,
the first document and its title, and anything the New-project dialog states outright still wins over
it; an export preset names destinations and languages and deliberately no channel ids, because a
channel can be reconnected as a new row. The Preset Manager switches the three with an index strip;
project presets stand beside the four built-in types in the New-project dialog; an export preset
fills step 2 of the Export modal from its header. No migration — the table already had the columns.

**T-353 remainder — the asset window.** The project cover and the showcase gallery open the shared
`app-media-picker` (which uploads as well as picks, so the private file inputs are gone with nothing
lost); the gallery appends rather than replaces, so a URL typed by hand still stands. The landing
screenshots deliberately stay as they are: `/landing-media/` is an admin-level store outside any
owner's asset library, and routing it through a library the admin does not own would be the wrong
kind of unification.

**T-350 remainder — the language menu at the other call sites.** Settings' signature-language and
cross-links pickers, and the form-preset "add language" popover, are the one shared
`app-language-menu`; the menu gained a `triggerLabel` so an "add" control can say what it is for
instead of naming the current language. The Glossary keeps its index strip and gains the menu beside
it: the strip lists the languages that hold terms plus the primary and the selected one, the menu
reaches the rest — a tile carries a count and a menu row cannot, so the tabs stay where they earn
their place.

**T-337 remainder — Posts Manager.** The search field is the bench's own (`app-input` `dense`,
sticky at the top of the shelf's scroller), and a post card draws the document's first picture on the
same plate its type icon used, so a list of mixed posts keeps one left edge. The cover is cached on
the draft (`CoverImagePath`/`CoverImageScanned`, migration `DraftCoverImage`) and filled in by the
listing 50 documents at a time — a migration cannot parse a document's JSON, and permanently joining
every body into a projection that deliberately avoids them would be a bad trade for a thumbnail. The
inspector's density is deliberately unchanged: density is a property of a page, never of a component
(ADR-164 rule 6), and this page is a list beside reading matter.

**T-338 remainder — Stats.** Each readout tile carries a sparkline of the window it reports, drawn
from the series the chart already holds, so it costs no request and cannot disagree with the chart.
Below three readings it is omitted; a flat series draws on the middle line, because the tile answers
"which way is this going" and zero movement is a horizon, not a floor.

**T-361 remainder — the 1-credit sync paths.** Glossary translate and translate-all, the form-preset
translate and the profile translate hand the credit back when the provider fails on us, through
`SubscriptionPlan.RefundAiAndFailAsync` — one call that refunds and answers, so a site cannot refund
without failing or fail without refunding. A partial success (some glossary terms skipped) keeps its
charge, and the daily count stays spent, as with the background jobs.

**T-359 remainder — the bot audit's two low-risk findings.** *Finding 7*: an invoice link is
transferable, so the payer is not necessarily the payload's account. Paying for somebody else is a
gift and is honoured; the confirmation now drops the expiry date and the balance when the payer's
Telegram id does not match the account's. *Finding 6b*: the `BotKnownChatAdmin` cache is only
rewritten when the *bot's* membership changes, so a demoted admin kept a grant nothing revoked.
`BotKnownChat.AdminsSyncedAt` (migration `BotKnownChatAdminsSyncedAt`, backfilled from `LastSeenAt`,
which is exactly when the admin list last synced) bounds that to seven days for the `/known`
listing; the Refresh button ignores the TTL, because it re-reads the admin list from Telegram and is
the way an expired chat comes back.

Tests: 1688 backend (26 new — team access resolution and the two new preset configs), 496 frontend.
`UiInventoryDriftTests` and `SchemaDriftGuardTests` pass; `docs/design/UI-INVENTORY.md` carries the
Teams screen and the Preset Manager's three kinds. Nothing was deployed, merged or published.


## 2026-08-31 — Public blog reading and discovery redesign

ADR-231 simplifies the public blog while retaining Cedar Bench materials and both
themes. The index has a wider reading area, persistent mobile search, separate
language/topic controls, a collapsible topic panel, visible selected filters and
an explicit reset. Public cards can preview a local image; private cards expose
neither an image nor an excerpt. Card and search links retain the preview language.

The article uses an unframed paper sheet with a left-aligned title and quieter
metadata. Discussion follows the article before further reading and subscription.
Reading and ordering controls have 44px targets. The topic panel closes on Escape
or an outside click, and forms stack on phones. Comment submission prevents repeat
clicks, preserves text on failure and exposes its busy state. Reaction buttons and
comment inputs have accessible names. Navigation and subscription return paths
retain the reader's language. The shared token export includes existing type and
spacing scales; no new palette or application theme is introduced.

Validation: the CLI `TestPipeline` runs against this isolated worktree through a
per-run `CliConfig.RepoRoot`, leaving the installed CLI's repository setting alone.
Backend tests: 1659; CLI tests: 127; frontend tests: 496. Icon, contrast and density
contracts pass; contrast retains four previously accepted exceptions. New endpoint
coverage checks private previews, external thumbnail exclusion, translated cards
and Russian search navigation. Existing related/series privacy assertions retain
their exclusions while checking language-bearing links.

Visual evidence is partial: earlier local browser inspection covered the index,
article and a 390px phone layout with demo data. The Browser then returned
`ERR_BLOCKED_BY_CLIENT`, so the final layout, both-theme pass and full interaction
pass remain unverified. The local fixture uses a supplied screenshot to exercise
image layout; it is not a copy of production content. No mail, Telegram publication,
deployment, merge or PR was performed.

## 2026-08-31 — Sprint v0.2.0: credit refund when an AI job fails (T-361, most of it)

`AiJobService.Start` gained an `onFailure` callback that fires on any job failure (provider error,
cancel, timeout, throw); the two background jobs — document translate and AI-edit — pass one that
grants the credit back through a fresh tenant scope (`SubscriptionPlan.RefundAiAsync`, ledger
reason `ai-refund`, a fresh ref so it is a visible second movement, and the daily count stays spent
since the attempt still hit the provider). The charge is still taken up front, correct against
abuse; this only reverses it when the failure was ours. The cheap 1-credit sync paths (glossary /
form / profile translate) are left as the row's remainder. Backend 1653, green.

## 2026-08-31 — Sprint v0.2.0, batch fifteen: post cards read by shape

**T-337 (first pass)** — the Posts Manager list (already cards since 01.08) now carries the
document-type icon on a small tinted plate at the head of each card, so the list reads by shape as
well as by title. The rest of the "needs more visual" ask (a cover thumbnail, the inspector's
density, the bench search field) stays on the board for a live-render pass with Marty's eye.
Checks: frontend 496, density/contrast/icons green.

## 2026-08-31 — Sprint v0.2.0, batch fourteen: the Preset Manager (T-331 + T-355, ADR-233)

Document presets, end to end. A `Preset` table (migration `AddPresets`, `Kind`=document today,
project/export the same table later), CRUD at `/api/presets`, and a Preset Manager screen
`/presets` off the tray: each document preset bundles a base type (which built-in `DocumentTypes`
value it publishes as — never a new stored string, so publishability stays the contract) and a
heading skeleton. `POST /api/projects/:id/documents` takes an optional `presetId` and applies the
skeleton server-side once; the New-document dialog offers presets as dashed cards beside the
built-in types. This is what gives a Document Type real meaning (**T-355**, Marty's ruling): the
type is a nameable, editable starting point now, not a bare label. `DocumentPresetConfig` in Core
(shared by validation and skeleton generation) is unit-tested. Verified by capture in both themes.
Project and export presets stay on the board as the next slices of the same table. Backend 1657
(4 new), frontend 496, density/contrast/icons green.

## 2026-08-31 — Sprint v0.2.0, batch thirteen: the Stats zero-state

**T-338 (zero-state)** — the chart board on a fresh account read as dead: bare dashes and "nothing
to draw". It now shows a leaf centred on the graph paper with a title and one line that says the
numbers fill in from the first publish (or, when sources are off, to tap a leaf) — the screenshot-6
"looks poor and uninteresting" was the empty state, and this is what a new account meets. Richer
populated readouts (sparklines) stay on the board for when there is data to judge them against.
Two stats specs rebound to `.stats-empty`. Checks: frontend 496, density/contrast green.

## 2026-08-31 — Sprint v0.2.0, batch twelve: the scalable language menu

**T-350 (menu built)** — `app-language-menu`, a searchable popover over every content language
with its endonym, a Pro lock on the ones Free cannot reach and the caller-supplied has-content /
stale marks; it replaces the flat leaf-tag row that ran off the edge as languages piled up (the
screenshot-4 concern). The editor's add-translation control uses it now; the other call sites
(glossary tabs, settings signature pick, cross-links, form chips) stay a per-site swap on the
board. Checks: frontend 496, density/contrast/icons green.

## 2026-08-31 — Sprint v0.2.0, batch eleven: the feedback channel (T-191, ADR-232)

**T-191 (closed)** — feedback is a stored owner-scoped entity, not email: `FeedbackEntry`
(migration `AddFeedbackEntry`), `POST /api/feedback` (kind bug/idea/other + message + the path it
was sent from), an `app-feedback-panel` modal hoisted in the shell and opened from the tray so it
is reachable everywhere, and an admin Feedback tab that lists every account's entries with the
sender's email and a Mark-handled / Reopen toggle plus an "Unhandled only" filter (the tab badge
counts the unhandled). Deliberately not email — the maintainer can triage a list; the public blog
Report stays `mailto:` (T-360) since there is no account there to scope a row to. The i18n lives
under `feedbackForm` (the `feedback` key was already the comments/reactions feature's). ADR-232.
Checks: backend 1653, frontend 496, density/contrast/icons green.

## 2026-08-31 — Sprint v0.2.0, batch ten: the projects hub becomes cards

**T-332 (closed)** — `/projects` traded its cold row table for a responsive card grid: each card
leads with a 16:9 cover (an image when `coverUrl` is set — no screen sets one yet, T-353's
remainder — the wood-and-initials plate otherwise), the state stamp pinned in the cover corner,
then name, type and a mono strip of doc/open-task/asset counts and the last-activity date. A card
is still an `<a [routerLink]>`, so middle-click and copy-link survive; an archived card dims. The
summary shelf is unchanged. Verified by capture in both themes; the three renamed spec assertions
follow the `.card`/`.card-name` selectors. Checks: frontend 496, density/contrast green.

## 2026-08-31 — Sprint v0.2.0, batch nine: task board drag-drop, contrast sweep, AI audit

**T-354 (closed)** — the task board's four columns are one `cdkDropListGroup` and each card is a
`cdkDrag`; dropping a card into a different column calls `setStatus`, which reloads the board like
every other mutation. A same-column drop is ignored on purpose — the board keeps the server's
order (ADR-106), so reordering there would be a lie the next reload corrects. The card stays an
anchor, so click-to-open still works (CDK cancels the click only after a real drag). **T-334
(closed)** — the reported near-invisible-glyph class was the toolbar (fixed in batch five); a
contrast-census pass turned up no new instances, only the already-tracked drawer-lip decision row
and the intentional white-on-brand buttons (Telegram/X blue). **T-352 (closed, audit only)** — see
below. Checks: frontend 496, density/contrast green.

## 2026-08-31 — Sprint v0.2.0: T-352 AI-abuse audit closed (no code change)

Verified every path that reaches a translation or edit provider (`AuthEndpoints`, `DraftEndpoints`
×2, `FormPresetEndpoints`, `GlossaryEndpoints` ×2 — six sites, one per provider invocation) charges
through `SubscriptionPlan.ChargeAiOrRefuseAsync` **before** the provider is called, so a cancelled
or failed call cannot yield a free one; the 20/day ceiling stands on top as an abuse backstop; Free
has no AI at all (`HasAiFeatures` opens at Pro). The money-loss-by-abuse surface is closed — a user
can only spend credits they hold. Two notes, neither a hole: the "translate all profile texts"
batch is 1 credit for several short provider round-trips (intentional, cheap, the UI says "one AI
call"), and a failed background job does not refund the credit — the opposite of abuse, filed as
the fairness follow-up **T-361**.

## 2026-08-31 — Sprint v0.2.0, batch eight: the shared-bot audit closes four cross-tenant holes (T-359)

A `very thorough` read of every place the shared Telegram bot maps an update to an account
(TelegramBotService, ChannelEndpoints, TelegramEngagement, TelegramPublishTarget, the discovery
cache, Stars payments) found four *confirmed broken* boundaries — the bot sits in every user's
chats, so each is a security bug before open beta — plus verified-clean flows. Fixed:
- **Channel connect checked only the bot, not the caller** (worst): `POST /api/channels` now
  verifies the caller's linked Telegram is an admin/creator of the chat
  (`BotChatAccess.IsAdminOrCreator`); unlinked accounts cannot connect. Without it any signed-up
  stranger could claim any channel the bot was added to, then publish/pin/mint-invite/read stats.
- **Comment counting was forgeable from a private chat**: `TelegramEngagement.ApplyCommentAsync`
  now requires a `Supergroup` chat and that the replied-to message is Telegram's own automatic
  forward — three new unit tests (`TelegramEngagementTests`, the module had none).
- **Media resolved by filename with the tenant filter off in the queue**: both Asset queries in
  `TelegramPublishTarget` carry `OwnerId == request.OwnerId` now, so a draft can neither read
  another account's file bytes nor mutate its `TelegramFileId` row.
- **`refresh-known-chats` walked the global cache for any user**: bounded to the caller's own
  admin chats (a shared-token flood lever, and it latched others' rows on transient failure).
Two collisions confirmed settled (one Telegram id per account; one channel claimable by two, which
the caller-admin check stops a stranger from doing). The bot's shared-boundary rules are now in
`.claude/rules/telegram-bot.md`; low-risk remainders (transferable Stars invoice, stale-admin
discovery) stay on the board as T-359's tail. Checks: backend 1654 (4 new), suite green.

## 2026-08-31 — Sprint v0.2.0, batch seven: prose folds behind the (i)

**T-343 (closed)** — `shared/hint-dot.component.ts`: a small (i) that opens the explainer in a
clamped popover, accessible name "How this works". The standing paragraphs moved behind it:
Glossary's intro bubble, the Settings section explainers (Header slots, Cross-links,
Integrations, Credits — each heading carries its dot now), and the forms editor's language hint.
Conditional warnings deliberately kept visible — a hint that appears because something is wrong
is not decoration. **T-347 (closed)** — reviewed against the ask, the Appearance panel already
carries the interface-wide settings (theme, accent) ahead of the editor ones; what it lacked was
saying so — the panel now names its two halves, "Interface" and "Editor", with a rule between.
**T-346 (closed by its siblings)** — the profile screen's pass accumulated across the sprint:
T-344 moved the author fields under the avatar, T-343 folded its prose, T-349 dressed its locks,
T-345 unified its location control, T-348 gave it a clean four-tab home; the by-eye check rides
the existing TASKS rows. Checks: frontend 496, density/contrast green.

## 2026-08-31 — Sprint v0.2.0, batch six: the document's location, the last fly-out

**T-345 (closed)** — a location belongs to a document as well as to a profile (the trip case):
`Draft.LocationText` (migration `AddDraftLocation`), a `POST /{id}/location` endpoint on the
watermark's pattern, and the blog's MapLocation header slot reads the document's location first,
falling back to the profile — which is exactly what it always showed before, so nothing published
changes until someone types a trip. The control is one component now, `app-location-input`: a text
input with the profile's own location one press away (a flag chip, hidden while the field already
says it), commits on blur/Enter; Settings → Profile and the editor inspector's new Location row
both use it. **T-341 (closed)** — the last unclamped fly-out, the posts-manager forms shelf's
`.lang-add-menu`, moved onto `app-popover` (fixed + clamped, and no longer clipped by the shelf's
own overflow); its entries also wear the T-350 language locks now. The rail-header switcher and
the hook-rail tray stay as they are on purpose: both are anchored to fixed chrome corners and
cannot reach a screen edge until narrow layouts land (T-237). Checks: backend 1650 (drift guard
green with the migration), frontend 496.

## 2026-08-31 — Sprint v0.2.0, batch five: the live-render fixes

The first batch made with eyes on the running app: the isolated e2e stack (`Scripts/e2e.ps1`,
which now also seeds the admin's profile — T-328's guard would otherwise bounce the whole suite
to `/onboarding`) plus a throwaway Playwright capture spec, screenshots in both themes.
**T-339 (closed)** — the style guide's dark render was never a stray literal: the page lay
straight on the wall, and at night the wall is dark while every ink on the page is paper's, so
the headings measured near-invisible. The whole guide now lies on a paper sheet
(`--sheet`/`--tex-paper`/`--shadow-paper`), which is what its own tokens assumed all along —
verified fixed by re-capture. **T-340 (closed)** — the tool strip's forty icons rested in
`--rail-ink-soft`, the crumb separator's cream, and read as part of the wood; they take the full
rail ink now, plus one weight step for every glyph in a single rule (Phosphor draws fills, so
`stroke: currentColor` at 8/256 thickens the shape) instead of a forty-call-site `weight="bold"`
sweep. At 1440 the toolbar now drops its group captions — the adaptive fit (ADR-150) paying for
the wider wall. **T-335 (closed)** — `--bench-tool-w` 56 → 64: "Dialogues" no longer ellipsizes
and the tools stop reading cramped; the contextual hook sets were verified correct in the same
captures. Checks after: frontend 496, density/contrast green; before/after shots handed to Marty.

## 2026-08-31 — Sprint v0.2.0, batch four: 0.20.0, four presets, AI onto credits

Three rulings landed at once (Q-16: the version is **0.20.0** — reads as the wished-for "0.2.0",
still sorts after 0.17.x; presets: **Empty · Blog · Game · Product**; the pricing table approved
whole) and the batch encodes them. `Consts.CurrentVersion` → 0.20.0. **Presets** — the New-project
offer is the four; "fullgame" stays the stored key of Game (renaming a stored value is a data
migration for nothing visible), jam/prototype/released left the offer but stay recognized so old
rows still name themselves; Empty starts with a blank note (no skeleton — an Empty project
promises nothing), Product with the changelog skeleton; client type/icon/starter maps and both
dictionaries follow. **T-152 (the core)** — AI is paid in credits: `CreditPacks.AiTranslateCost`
(2, document translate/edit) and `AiSmallCost` (1, glossary/forms/profile),
`SubscriptionPlan.TryChargeAiAsync` = the 20/day ceiling kept as the abuse guard + a wallet charge
(upfront, a fresh guid per call — a reused ref would make later calls free), all six AI endpoints
switched via one `ChargeAiOrRefuseAsync` (403→429/402 with `NotEnoughCreditsForAi`);
`HasAiFeatures` opens at **Pro** now, Free has no AI at all, and **Pro+ receives 30 credits with
every paid subscription payment** (Stripe checkout + renewal, PayPal, Stars — idempotent by the
payment id; the $1 trial deliberately gets none). The client's AI locks turned silver (`pro`), and
the tier table lives in `docs/product/BUSINESS.md` §3 — the margin is capped from above now, 15
worst-case translations a month against the old potential 600. Checks: backend 1650, frontend
496, density/contrast/icons green. **T-351 (the surface)** — the credit chip on the rail before
the account menu: a mono number with a resin drop, a door to Settings → Billing, shown once
there is anything to watch (a paid plan or a non-zero balance) and refreshed per navigation
against a new `GET /api/billing/credits/balance` (the number alone — the wallet endpoint carries
a ledger the top bar has no business fetching). With the chip, the locks and the approved table,
**T-165 closes**: every paid feature is visible, marked and explained where it stands — the
HacknPlan "purple rule" the row asked for.

## 2026-08-31 — Sprint v0.2.0, batch three: the onboarding door, the language paywall, four settings tabs

Marty ruled T-329 (the blog address is shown, never assignable) and confirmed T-350's Free set, so
the batch had no open decisions. **T-328/T-329** — `/onboarding`, a fifth door outside the shell:
display name (required — it doubles as the "went through onboarding" mark, no separate flag),
main social/site URL, location, and the blog address as a read-only linked block. `authGuard`
bounces any named-less account there with `returnUrl`; `onboardingGuard` keeps named accounts out;
register's existing navigate-to-editor now lands on the door via the guard, and the e2e
`registerAccount` helper saves a profile right after registering so the suite never meets it.
**T-350 (the paywall half)** — `PlanLimitations.FreeContentLanguages = [en, ja]` and
`HasContentLanguage` (≥Pro passes everything): enforced where language versions are *created* —
a new `DraftTranslation` (existing rows stay editable whatever the plan) and a new glossary term —
each answering 403 `ErrorMessages.LanguageRequiresPro`. Deliberately ungated: a draft's own
primary language, so a Free author still writes in their language; the gate is on the
multi-language machinery. Client: the editor's add-language menu disables locked entries with a
silver lock, the glossary term form labels locked options; `AuthService.hasContentLanguage`
mirrors the server. The unified language menu stays on the board as the row's remainder.
**T-348** — Settings goes to four tabs: Profile, Account (UI language), Integrations, Billing
(subscription + credits). The X OAuth callback and the editor's three connect links land on
Integrations; the integrations-side credits note crosses tabs via `goToCredits()`. Checks:
backend 1648 (7 new `HasContentLanguage` cases), frontend 496, all green. **T-360** — a Report
link in the blog footer on every public page: `mailto:` the maintainer address already public on
Terms/Privacy, with the page URL appended client-side; no account needed to complain, and the
structured intake stays with T-191. One press-page test's "no contact ⇒ no `mailto:`" marker
became the row label, since the footer now carries a mailto everywhere.

## 2026-08-31 — Sprint v0.2.0, batch two: Pro+ by name, the plan locks, the popup clamps

**The tier is called Pro+ now** — every user-facing "Pro Plus" renamed across both dictionaries,
the settings plan card, the terms page, `ErrorMessages`, the Stripe/Stars product names and the
landing; the stored tier key `ProPlus` and `Consts.Plans` names are untouched, so nothing about
billing state changed shape. **T-349 (first half)** — `shared/plan-lock.component.ts`: the plan
lock every gated control wears, silver (`--lock-silver`) for Pro, gold (`--lock-gold`) for Pro+ —
two new one-set-for-both-themes tokens beside the avatars. Settings wears it on the signature
field and Save, both translate buttons and header slot 3, replacing the hand-drawn
`.pro-lock-badge`; the translate buttons' gate was also wrong — `hasProSignature()` (Pro), so a
Pro account clicked into the server's Pro+ refusal, which is the defect screenshot 3 reported —
they now gate on `hasAiPlan()` — the client half of `PlanLimitations.HasAiFeatures`, now a
computed on `AuthService`. The sweep then covered every AI surface the client draws (closes
T-349): the editor's retranslate, translate-all, empty-state auto-translate and both right-click
AI entries, the glossary's translate-all and per-term translate, and the form preset's
per-language translate chip — each disabled with a gold lock on a plan below Pro+. A by-eye pass
over the locks is on the TASKS checklist. **T-344** — the author-name, profile-URL and location
fields moved from Header slots into the Profile card under the avatar; Header slots keeps only the
three slot selects, exactly the split screenshot 5 asked for. The i18n keys stay under
`settings.headerSlots.*` until T-346 renames them with the profile redesign. **T-353 (the dead
end)** — `app-media-picker` uploads now: a pine Upload label in the modal footer (a file input
cannot be a button — the avatar picker's trade), multi-file, one uploaded file passes straight
through as the pick; the empty-state line says uploading here works. Canvas and editor share the
picker, so both get it at once; the one-off cover/gallery upload flows stay on the board. **T-341 (the reported cases)** — an audit found the popover clamp itself was the bug:
`app-popover` clamped the left edge against a literal 252 while real panels measure 278, so
inspector fly-outs hung off the right edge; it now re-clamps after render against the panel's
measured width and caps height at the viewport. The two genuinely unclamped menus — the sheet's
right-click term menu (raw pointer coords) and the `[[` wiki suggester (raw caret rect) — clamp
to the viewport now. Three lower-risk absolute panels stay on the board row. Checks: backend
1641, frontend 496, contrast/density green.

## 2026-08-31 — Sprint v0.2.0, batch one: the quick screenshot fixes

Six of the sprint's decision-free rows, straight off the annotated screenshots. **T-333** — the
example project is gone whole (the `/projects/example` endpoint, `createExample` on service and
component, the empty-state button and both i18n keys), "A project is a game" became the container
sentence in both languages, the hub's "one per game" note and the empty-state game-controller icon
went with it; test fixtures keep the name "Cedar Quest" — internal data, never shown. The
gamedev-flavoured new-project type blurbs stay until `T-331` rebuilds that dialog from presets.
**T-336** — Glossary's New term left the rail primary for a pine button on the `.gl-index` strip
beside Translate-all; the rail publishes nothing on that screen now. **T-339** (half) —
`/dev/styleguide` and `/dev/icons` moved from `authGuard` to `adminGuard` and the tray hides both
from non-admins; the dark-theme render stays open on the board. **T-330** — the About link left the
rail header; the way back to `/welcome` is a tree-evergreen button on the drawer lip, and the
landing header gained a paper "Log in" button beside the waitlist one. **T-334** (the reported
case) — icons on pine buttons now carry the same relief the label's text-shadow gives
(`drop-shadow` on `app-icon`), which is what made the + on New project fade by day; the sweep for
similar marks stays on the board. **T-342** — the personal placeholders are gone: signature,
author-name and location placeholders in both dictionaries, the register username `martycow`, the
admin landing field, and the register tagline no longer names Marty. Checks: backend 1641,
frontend 496, contrast/density/icons all green. UI-INVENTORY updated in the same commit.

## 2026-08-31 — Input sweep: the v0.2.0 open-beta sprint lands on the board

The third hand-run of the input-sweeper procedure (T-189). The 31.08 INPUT_PROMPT — ten annotated
screenshots of 0.17.0 in `docs/Cedar_Clerk_v0.2.0/` plus twenty-one thoughts — triaged against code
and board: 33 new rows `T-328`…`T-360` under "Sprint v0.2.0 — open-beta polish", `T-003`'s provider
list updated (Discord in, Meta out), and `Q-16` opened for the public version scheme (the sprint is
named v0.2.0 while `Consts.CurrentVersion` reads 0.17.0). Existing rows the sprint leans on stayed
where they were: T-152/T-153/T-165 (monetisation), T-172/T-190/T-293 (beta gates), T-191 (feedback),
T-301/T-302/T-304 (folded into T-358 Teams). The visual plan (six milestones, SEP–DEC) was built
with visualize-roadmap and handed to Marty; naming and pricing decisions stay his.

## 2026-08-30 — The dialogue tool: Yarn graphs, a .yarn export and an xlsx translation round trip (master, uncommitted)

A new gamedev-module tool (ADR-230), grown from "I need a localization framework and a dialogue
editor" for the Unity game: the framework answer is Unity Localization + Yarn Spinner in the
engine, and Cedar Clerk's part is where the dialogue gets written. `/projects/:id/dialogues` lists
a project's `DialogueScript`s; opening one lands on a node-graph editor — the canvas's div-world
surface at a smaller scale, nodes as paper cards, edges drawn as SVG from the bodies' own
`<<jump>>`/`[[link]]` text on every keystroke, never stored. Bodies are Yarn syntax in a mono
textarea; autosave stamps `#line:` ids server-side (`YarnDialogue` in Core, unit-tested).

Exports from the dock: a `.yarn` file with `position:` headers (Yarn Spinner's VS Code graph view
reads them back) and an xlsx sheet — `Id | Node | Character | Text` plus a column per language,
stored translations filled in — that imports back as an upsert per (line id, language), empty
cells skipped, unknown ids kept. One migration (`AddDialogueTool`, two new tables), one new server
dependency (ClosedXML 0.105.0, STACK.md updated), `dotnet test` 1640+127 green, icon usage
regenerated, density/contrast checks clean, UI-INVENTORY carries the two new screens.

## 2026-08-29 — Wave 2 "Rhythm": publishing learns to keep time (master, uncommitted, 0.17.0 — third session of the day)

The second wave of the competitor-research slate, same machinery as the morning: a frozen contract,
four lanes plus a tester, three defect-fix rounds. `Consts.CurrentVersion` still reads **0.17.0**
and everything sits uncommitted on `master` — nothing deployed. One migration (`AddWave2Rhythm`,
purely additive), one dependency bump (Telegram.Bot 22.10.2 → 22.10.3, Bot API 10.3 —
`telegram-bot.md` updated by the lane), and the same said-out-loud deviation a third time: the
contract stood in for ADRs during the work, and ADR-226, ADR-227, ADR-228 were written at session
close.

**The schedule becomes a place you can see** (closes `T-179`). `/calendar` hangs between Docs and
Metrics on the rail — the "Content Calendar" Claude Design canvas rebuilt on bench tokens: a
dark-wood board, paper day tickets with network dots, a brass ring on today. Dragging a pending
ticket to another day keeps its local wall-clock time and PATCHes the recomputed UTC. **Everything
on it renders in the browser timezone** — a deliberate divergence from ADR-115's fixed display
zone, matching the `datetime-local` pickers, and deliberately *not* an ADR yet: the divergence is
provisional until the per-user-timezone question is ruled on (`T-323`), and the ruling deserves one
ADR, not an ADR now and a reversal later. Week view is deferred (`T-322`).

**The queue fills itself** (ADR-228). `QueueSlot` names a weekly moment on one destination;
`FillQueueSlotsJob` (30 min) fills upcoming occurrences from the owner's evergreen pool —
`Draft.IsEvergreen` plus category/max-sends/until bounds, edited from the drafts page —
least-sent-first, as ordinary `ScheduledPost` rows. Occupancy is keyed by ISO week, not timestamp,
so a dragged ticket is never double-filled; a Failed send frees its week; `EvergreenSendCount`
counts only what actually went Sent.

**Telegram gets its 10.3 furniture.** Per-post CTA buttons — max 3, stored as `Draft.CtaButtonsJson`
and appended wire-level to the last part, never entering the document (ADR-226); expandable
blockquotes as a TipTap attribute with an editor toggle, collapsible natively on Telegram and plain
on the blog. The document block is deferred (`T-326`) — no attachment node exists to map from. Also
on the scheduled path: silent sends and pin-after-send, with auto-unpin of the previous pin
(`Channel.LastPinnedMessageId`) and a log-and-swallow rule — a pin failure never fails a publish.
Immediate sends don't bind the toggles yet (`T-325`). And a channel can carry its own signature trio
overriding the owner's (`PATCH /api/channels/{id}/signature`, free tier 403, blank text clears the
whole trio) — a client bug that would have wiped the stored translations on every save was caught
during shape verification, before it shipped.

**The export modal asks "is this ready?"** (closes `T-238`). A pre-publish checks shelf:
`POST /api/posts/preflight` finds empty language versions and dead links (`LinkCheckService`, 5s
cap, private-range SSRF guard — refused ranges report "blocked"; redirect-chasing and DNS-rebinding
hardening left open by decision, `T-324`), plus the existing validator's missing-alt-text pass.
Warnings never block, and the shelf says so.

**The stats learn where members come from** (ADR-227). Named invite links per channel,
`chat_member` ingestion into `ChannelMemberDaily` — daily aggregates only, never member identities,
the `BlogViewGeoDaily` stance on a third surface. The tester caught the first cut counting every
mute as churn: a restricted member with `IsMember` still set hasn't left. The shelf shows
joins/leaves/net per link plus the organic row; per-day bars stay on the board (`T-327`). Beside
it: tracked short links (`/l/{code}`, counters not visit logs, bots included and the UI says so)
with a copy affordance in the export modal and clicks in the post inspector; best-time hints
(`GET /api/channels/{id}/best-times`, only hours with ≥2 posts — no fake advice); publishing
streaks in ISO weeks with a streak card, and publish-event markers on the growth charts (closes
`T-244`, the streak half of `T-166`).

**A draft can start from somewhere.** Four EN+RU starter templates (weekly devlog, screenshot
Saturday, patch notes, postmortem) behind "New from template…", alongside the account's own
`IsTemplate` drafts; nothing is seeded until a pick happens.

**Verified.** Backend `dotnet test` **1619/1619** plus **127/127**; `npx ng test` **492/492**;
production build clean with the editor CSS back under its budget; every guard green; live e2e 6/0
on the touched specs. `UI-INVENTORY.md` rows for every new surface landed with the lanes.

## 2026-08-29 — Wave 1 "Reach": the blog learns to be found (master, uncommitted, 0.17.0 — second session of the day)

The day's second session, and this one is features. A five-agent competitor deep-research phase
(social schedulers, blog platforms, indie-gamedev marketing, Telegram tools) produced a two-wave
slate; Wave 1 — "Reach" — was frozen into a contract and built by four lanes plus a tester.
`Consts.CurrentVersion` still reads **0.17.0** and everything sits uncommitted on `master` — nothing
deployed. Two migrations (`AddWave1Reach` and the raw-SQL `AddDraftSearchFts`), one new dependency
(`SixLabors.ImageSharp.Drawing`), and the same deviation as this morning, said out loud again: the
contract stood in for ADRs during the work, and ADR-223, ADR-224, ADR-225 were written at session
close. The research itself also seeded `docs/product/COMPETITORS.md`, which closes `T-192`.

**The blog gets full-text search, and so does the app** (ADR-224, closes `T-176`). The `DraftSearch`
FTS5 table lives outside the EF model on purpose — a raw-SQL migration, triggers that catch
`ExecuteDelete`, a SaveChanges interceptor for tracked writes, a startup backfill for databases that
predate it — so `SchemaDriftGuardTests` never has to lie about a virtual table. On top of it: a
public `/search` page on the blog (result pages `noindex`), `GET /api/search/drafts`, and a Ctrl+K
overlay (`app-search-overlay`) in the bench shell. ADR-193 deleted the old title-only search for
exactly this gap; this is the other half of that decision arriving.

**A coverless post stops sharing as a blank card.** `/og/{slug}.png` renders a branded 1200×630 card
server-side — ImageSharp.Drawing, Literata and Source Sans woff2 embedded (latin + cyrillic), a disk
cache in `CEDAR_DATA_DIR/og-cache` with ETag/304 — and becomes the `og:image` fallback for posts
without a cover; the avatar fallback is gone. Judged below the ADR bar deliberately: a rendering
detail with a cache, no contested alternative, no reversal cost.

**A post ends with somewhere to go.** Up to three same-tag published posts as "read next" paper
cards at the article foot — filtered by the index visibility rule, so a private unlisted post is
never advertised there. And **the blog becomes legible to machines**: `sitemap.xml` and JSON-LD
Article markup, with `dateModified` clamped to never precede `datePublished`; the slugs `search`,
`subscribe` and `sitemap.xml` are reserved so no post can ever shadow the new pages.

**Steam and itch.io become copy targets** (ADR-223 — the T-318 half answered, T-127's Steam/itch
research resolved). `CedarToSteamBbcodeRenderer` and `CedarToItchHtmlRenderer` in Core, 86 tests
between them, `[noparse]` breakout protection and a URL scheme allowlist; the export modal's two
placeholder cards now show a rendered preview and a clipboard button off
`GET /api/drafts/{id}/export-text`. The clipboard is the whole design: no write API exists for
either storefront, and automation there is the researched anti-feature, not a gap.

**The press kit page nobody else builds** (closes `T-128`, design off the "Press Kit Page" Claude
Design canvas). `/games/{slug}/press` renders factsheet, description, screenshots, logos, videos and
contact from the showcase plus five optional press fields on `Project`, with a press-pack zip — a
zip the tester crashed once: it streamed synchronously into the response, which Kestrel refuses, and
it is assembled in a `MemoryStream` now. Edited from the showcase settings UI. presskit() is
abandoned and the niche is empty; this is a renderer over existing data, not a document to maintain.

**The blog gains subscribers, not a newsletter** (ADR-225). `BlogSubscriber` generalizes the
showcase follower machinery — double opt-in via `/subscribe*`, unsubscribe in every mail — and the
only thing that ever sends is the "Notify subscribers" toggle at a post's *first* blog publish,
through a durable `BlogNotifyJob` queue drained by a Quartz minute job. Never on re-publish, never
for a private post, and there is no broadcast composer, on purpose: margin and deliverability both
say notify-on-publish is the ceiling.

**A draft can be shown before it is anything.** `Draft.PreviewToken` — `POST`/`DELETE
/api/drafts/{id}/preview-link` from the editor, a public read-only `/preview/{token}` page,
`noindex`, revocable, no account needed. Typefully's most-loved feature, built as the reader access
token's shape applied one entity over — which is why it, too, stays below the ADR bar. What it ships
without: a `GET` to re-read an existing link, so the UI cannot show it next session (`T-320`).

**Verified, and what was deliberately not built.** Backend `dotnet test` **1460/1460** plus
**127/127**; `npx ng test` **486/486**; production build clean; every guard green — icons, contrast,
density, `UiInventoryDriftTests`, `ErrorMessageLocalizationTests`, `SchemaDriftGuardTests`,
`DocsFlowGraphTests`. `UI-INVENTORY.md` rows for the new blog surfaces landed with the session. The
anti-features held throughout: no Steam/itch auto-posting, no cross-network engagement inbox, no
bundled AI media generation, no MTProto analytics, no mass-DM — each refused in the research with a
reason, recorded in `docs/product/COMPETITORS.md`.

## 2026-08-29 — Fourteen maintainer items through one frozen contract (master, uncommitted, 0.17.0)

A batch session, not a feature one: fourteen of the maintainer's annotations, frozen into a contract
with per-item acceptance criteria, run as four parallel lanes plus a tester. `Consts.CurrentVersion`
reads **0.17.0** and every change sits uncommitted on `master` — nothing here has been deployed. One
deviation from the gate to say out loud: the contract stood in for ADRs during the work, and the three
decisions that deserved them — ADR-220, ADR-221, ADR-222 — were written at session close rather than
before the code.

**The nested tab-boards come apart.** The Media Library gained an `embedded` input: Assets embeds the
list and panel with no chrome of its own — one shelf-panel, the type strip welded above it, the view
toggle in the panel's actions — and its negative-margin hack gave way to the flex contract. Standalone,
the bucket and type strips merged onto one row, and Stats' metric and view strips did the same. No
screen stacks two index-tab rows over one panel any more, and the panel-in-panel throw never fires.

**Pages scroll inside their sheets.** The bench page contract — `:host{flex:1;min-height:0}` down the
chain — landed on Assets, Media Library, Projects, Boards and Settings, so a hundred rows scroll inside
the sheet while the header and strips stay put. Settings' two channel lists are bounded at 260px, the
drafts folder-list precedent.

**The Stats "Likes" hint stops reading vertically.** `.metric-note` carried `flex: 1; min-width: 0` and,
as the only shrinkable item on a wrapped strip line, collapsed to zero width — one character per line.
It is `flex: 0 1 auto; max-width: 48ch` now, and the comment on the rule says why it must never go back.

**The shell learns three small courtesies.** The project switcher keeps the open screen across a switch
— `/projects/A/assets` to project B lands on B's assets, `canvas/:boardId` folds to the boards list
(ADR-221). The logo is a link to the hub, labelled for a reader. And a signed-in author can see the
front door again: `/welcome` serves the landing regardless of the cookie, linked from beside the version
label with a plain href the SPA router cannot swallow (ADR-222).

**The desktop build gets a page.** A public `/download` outside the shell — title, the one honest
sentence about data living in the account, a button onto the existing `/downloads/latest` redirect —
and a `#download` section on the landing behind the new `LandingSettings.ShowDownload` column (migration
`AddLandingShowDownload`, admin Landing tab checkbox). The flag defaults **false** like Roadmap and
Story before it: the section is a promise the installer has to keep first, and it has still never run
on a clean machine (T-121). `app-button` gained an `href` input on the way.

**Two boards start looking like boards.** The tasks kanban's columns took the cork tone — lanes a paper
card reads as pinned to, plaque headers — and the project-boards row list became a paper card grid.
Tokens only, at page level; the kit was not touched.

**The blog warms up, in the one file allowed to.** ShellTemplate CSS in `BlogEndpoints.cs` only, never
`styles.scss`: a resin lamp glow, warmer post cards with an inline paper texture, softened radii, a
reading measure, warm link underlines — both themes in the same edit, no static asset the blog host
could 404 on. The export modal's `.export-section` joined the paper-texture rule alongside the surfaces
that already carried it.

**The export cards stop doing two things per click** (ADR-220). The card body — a `div[role=button]`
now, not a label — selects a destination for management and nothing else; the real checkbox toggles
inclusion and nothing else, propagation stopped, both in the tab order with visible focus. Selecting a
network to look at it no longer silently changes what the next send does. The Files step also gained
`export-global`: it had been auto-placed into the 280px column and squeezed.

**The wall gets forged hardware.** Per the maintainer's pick off the Claude Design canvas: the brass
hook is one continuous forged J under its screw head — gradient and a specular glint — and the tally
is a rotated work ticket with a punched hole, same resin ground and ink as the index-tab badge. The
wall's top padding stepped up so the taller peg stops clipping.

**Verified, with the numbers observed.** Backend `dotnet test` **1439/1439**; `npx ng test` **482/482**
(two jsdom-quirk specs fixed at the spec, not the code); production build clean; every guard green —
icons, contrast, density, `UiInventoryDriftTests`, `ErrorMessageLocalizationTests`,
`SchemaDriftGuardTests`, `DocsFlowGraphTests`. `UI-INVENTORY.md` rows were updated by each lane in the
same change. The e2e specs touching the export contract (`14-thread`, `99-audit`) were updated to the
two-affordance cards and pass live, 3/3.

**What the tester found under the harness, and what is not done.** The e2e harness had drifted from the
code since `6cfb2be` — registration now requires a username, and blogs are tenant-hosted, so
`blog.localhost` had to become `e2e-admin.localhost` under `Cedar:TenantHost` in `Scripts/e2e.ps1` and
`e2e/helpers.ts`. That migration is done; the **full suite has not been rerun on it** — only the three
touched specs ran — which is `T-317` on the board. And the export rack still draws five cards for
networks with no connector at all (Instagram, Threads, YouTube, Steam, itch.io) — selectable, managing
nothing; whether they survive the new card semantics is `T-318`.

## 2026-08-27 — A project gains other people, and the showcase gains a screen (branch `showcase_menu_and_layout`)

Two pieces of work in one session, and only the second is a feature. The first tidied what yesterday's
showcase had grown into; the second gave a project collaborators and a surface worth sharing with them.

**The showcase moved out of the project-settings modal onto `/projects/:id/showcase`**, with a Showcase
hook on the tool wall. The 26.08 session (`T-294`…`T-300`, ADR-216) turned that page into a site — slug,
store links, trailer, gallery, counters, followers, its own domain — and every one of those fields was
still edited behind a modal's scroll bar nobody opened. The project-settings modal now carries none of
it.

**Three layout defects, each invisible until named.** The tool wall centred its list, so every hook
shrank to the width of its own caption: eight tools, eight widths, and the tray button below wider than
any of them. The list stretches now, the tray takes the wall's own side padding, and the wall is cut
wide enough for the longest caption in either language. Checkboxes carried no `accent-color`, so the
export modal's language ticks rendered in the operating system's blue on a cream sheet — one rule in
`styles.scss` replaces five ad-hoc ones. And Settings' language rows sat 2px under their hint and 37px
above the section edge.

**A 401 now ends the session instead of being swallowed.** Anything but `/me`, login and register
answering 401 clears the session and sends the reader to the door carrying the URL they were on, so
signing in puts them back where they were. Before this the guards only asked on navigation: a session
that expired under an open screen turned every button on it into a silent failure. Drive-by:
`DOCS-FLOW.md` still named `docs/archive/roadmap-phases-0-13.md`, deleted two commits earlier, which had
left `DocsFlowGraphTests` red here and on `master`.

**Then the rest of the session: a project can have collaborators, and a reference board is the first
thing worth sharing with one** (ADR-217, ADR-218, ADR-219). `T-129` (a moodboard gallery) and `T-155`
(a Miro-like whiteboard) had stood on the board with a note that they were probably one thing and that
two must not be built. They are one thing, and this is it: a free canvas of notes, images, frames and
links under one pan/zoom transform, live over SignalR, with the people who can see it managed on the
screen that lists the boards. One migration, `ReferenceBoardAndMembers` — three tables (`CanvasBoards`,
`CanvasItems`, `ProjectMembers`), seven indexes, a pure create with nothing renamed, so none of
`ef-migrations.md`'s hand-edit rules apply.

**A membership opens exactly one door** (ADR-217). `ProjectMember` carries a role — editor or viewer —
and a single-use invitation token, and the only thing it grants is the canvas. `GET /api/projects/{id}`
stays owner-only, so a member on the board list reads the project's name off `/api/projects/shared`
rather than off the project. The tenancy story is untouched: `OwnerId` is still the tenant id (ADR-206),
the three new tables are filtered like every other table, and `TenantFilterGuardTests` never had to be
exempted for them.

**The board is one server-owned surface and the last writer wins** (ADR-218). Every item carries a
`Version`, and a client accepts an incoming item whose version is at least its own, so a refused write
and a dropped socket repair through the same path — re-`Join`, replace the state with the snapshot.
There are two write paths, not one: `DragItems` broadcasts at pointer speed and persists nothing,
`UpdateItems` persists once on pointer-up. That split is what makes a 1 vCPU droplet with no swap a
plausible host for this. The surface itself is DOM, not `<canvas>`: a `<div>` world layer under one CSS
transform, items positioned in world coordinates. A note stays selectable text, an `<img>` gets browser
decoding for free, and a screen reader walks the same tree as every other screen; the cost is DOM count,
paid for by keeping Angular out of the pointer path entirely.

**A picture on a shared board is readable by the board's people** (ADR-219) — and this is the thing the
design had wrong. ADR-218 was written claiming `/media/*` needed no new rule for a shared board. It needs
one. A canvas image is an ordinary library upload that no post claims, so the media door's three
questions — public, gated, yours — all answered no for every collaborator, and an owner-uploaded picture
was a 404 for everyone but the person who uploaded it. The door gained a fourth case: a cached
`assetId → projectIds` lookup over canvas items, consulted only after every cheaper answer has said no,
with `ProjectAccessResolver` deciding. Deliberately not folded into the visibility index (every blog page
would pay for it) and deliberately not a `MediaGrant` (a 15-minute lifetime blanks a long board session).
The two-browser run that should have caught it only ever added a note.

**Nineteen review findings; thirteen fixed, six left on the board.** Two of the fixes deviate from what
was prescribed, and both deviations are the interesting part. The version race in `CanvasWrites.Stamp` —
two concurrent writes reading the same version, incrementing to the same number and broadcasting two
different payloads that clients then settle differently — was closed with a striped per-board semaphore
rather than a transaction: EF's SQLite transaction is deferred, so two concurrent `BeginTransactionAsync`
calls both SELECT and the second gets `SQLITE_BUSY`, which turns the race into an error instead of
serialising it. And the shell borrowing another project's name for a shared one could not be fixed with
the proposed `if (!id || !name) return;` — that breaks the session memory before the project list
resolves, which `bench-shell.component.spec.ts` pins and which went red. The distinction the code could
not make was "name not known yet" versus "not this account's project"; it can now, and the test stayed
as it was.

Three of the thirteen were holes rather than defects: `CanvasHub.Leave` acted on the client-supplied
board id while deleting the connection's own record, which let a client shed its presence while staying
subscribed and made `RevokeAsync` — the only thing that evicts a removed collaborator — find nothing to
evict; a peer's drag ghost was cleared only by a terminating message, so a refused pointer-up left the
item painted at a position that existed nowhere, on everyone else's screen, indefinitely; and the
arrow-key nudge persisted per keydown, producing exactly the ~30 writes a second that `DragItems` exists
to prevent.

**Integration found the build red on arrival and fixed nine things on the way through.**
`CedarClerk.Core/CanvasPayload.cs` did not exist although `CanvasWrites.cs` referenced it — no lane owned
it — so it was written from scratch, and `CedarClerk.Core` gained a `ProjectReference` to
`CedarClerk.Localization` to reach `ErrorMessages` from it (a new edge in the project graph; no cycle,
Localization references nothing). `AccountDeletion` swept none of the three new tables, including the
easily-missed `ProjectMembers.Where(MemberUserId == ownerId)`; project delete swept none of them either.
`/invite/:token` and its page did not exist, so the link the People panel hands out landed on the `**`
redirect. `proxy.conf.json` had no `/hubs` entry, so `ng serve` could not reach the hub at all. And the
feature had no tests: nine backend suites and two frontend ones were written to ADR-218 §6.

**Verified, with the numbers observed.** Backend `dotnet test` **1312/1312**, up from 1226 at the start
of the session; CLI **127/127**; `npx ng build` clean; `npx ng test` **478 tests across 48 files**;
`npm run check:density` 0 failures over 9/9 rules; `npm run check:contrast` 0 failing pairs against the
four accepted exceptions (ADR-140/172). `SchemaDriftGuardTests`, `TenantFilterGuardTests`,
`UiInventoryDriftTests` and `ErrorMessageLocalizationTests` all green; no test was deleted or weakened.
Two checks went red during the work and were fixed at the code rather than at the check.

**The two-client run, asserted rather than eyeballed.** An isolated stack — its own environment, its own
data directory, the bot confirmed off by its startup line — with two Playwright browser contexts signed
in as two different accounts. A created a board and invited B, who opened `/invite/<token>`, read
"e2eadmin invited you to Canvas Drive. Can: Edit", joined, and landed on the board. Both hubs reached
`Live` and each saw the other's face on the tool strip. A added a note and typed into it; B received the
item and its text. A dragged it; B's copy moved from x 618 to x 834, matching A's settled position
exactly. A moved its pointer and B showed a named, coloured cursor in world coordinates. A demoted B to
viewer, and after a reload B got the read-only line and no add buttons.

**What is not done.** The shell has no notion of a shared project, so a member sees Hub, Docs, Board,
Planner, Builds, Assets, Showcase and Metrics on the tool wall and every one of them 404s (`T-301`) —
a real hole, and a shell change rather than a canvas one. There is no "Shared with me" screen:
`/api/projects/shared` answers and the i18n key is carried, but nothing renders either, so losing the
invitation mail loses the project (`T-302`). An invited address with no Cedar Clerk account cannot get
one, because registration is still gated by an invite code that a project invitation neither carries nor
mentions — the owner sees a 201 and a URL, the invitee sees a door they cannot pass (`T-304`). The accept
is not idempotent (`T-305`), the batch-cap error says something false (`T-306`), and the six
low-severity review findings are on the board as `T-310`…`T-315`. The People panel was not extracted as
the reusable component the contract named (`T-309`), and `POST …/members/{id}/resend` exists without
being recorded in ADR-217 (`T-316`).

**What is unverified, and it is more than usual.** `@microsoft/signalr` was **not** already installed as
the brief and the contract both stated — it was added this session, with sixteen transitive packages, and
it is the only direct dependency added anywhere. Two code paths were written from a read of the code with
no test behind them, because the seams to write one do not exist: `SaveAsync`'s concurrency branch and
`CanvasHub.Leave` (`T-307`). The two canvas screens have no page-level specs at all and are covered only
by the single browser run above (`T-308`). The invitation mail was never actually sent — Resend is
unconfigured in the isolated stack, which is the designed fallback (the URL comes back regardless), so
`EmailTexts.ProjectInviteBody`'s markup has never rendered. And nothing here has been near the droplet:
no version bump, no tag, no deploy, and the realtime path has only ever run on one laptop with two
browser contexts on it.

## 2026-08-26 — The game page becomes the game's site (branch `showcase_site`)

**Seven gaps between ADR-134's showcase and a page an indie developer would use instead of a site,
closed in one pass** (`T-294`…`T-300`, ADR-216). The page shipped on 18.08 had a cover, a description,
store links, a devlog feed and a roadmap — and no entrance, nothing to look at, no counters and no
way to follow it.

**Two ways in** (`T-294`). The blog index carries a Games strip above the post list, and a devlog post
carries a line back to the game it is about. Both use the showcase's own visibility rule, so neither
can link to a page that 404s, and a project without a public page is named nowhere.

**Something to look at** (`T-295`). A YouTube trailer through the same nocookie embed the blog
renderer emits, and a gallery of uploaded `/media/` images — at most twelve, parsed on save so a line
the page would skip never looks stored. ADR-134's exclusion of `AssetEntry` stands and is the reason
for the shape: an indexed file is a fingerprint of somebody's disk, with no bytes to serve.

**Counters** (`T-296`). One daily table, upserted the way `RecordViewGeoAsync` already upserts: a page
view deduplicated by the same 30-minute cookie a post view uses, and a store-link click counted by
routing the pill through `/games/{slug}/go/{i}` and answering 302. The owner reads both off the
project's shelf. Daily rows, so "how many opened the Steam link" is answerable and "who" is not.

**An address to follow it by** (`T-297`). Double opt-in throughout — the row is unconfirmed until the
mailed link is opened, every mail carries a per-row unsubscribe token, and unsubscribing deletes the
row rather than marking it. Publishing a devlog mails the confirmed followers **once**, on its first
publication: republishing to fix a typo is not news. A follower is an address and two tokens, not an
account, so the reader-identity question (`T-004`) stays open rather than answered by a side door.

**A feed per game** (`T-298`), announced from the page — a reader following one game no longer takes
every other project with it. **Public builds** (`T-299`): `Build.IsPublic` plus a link the author
hosts. Not bytes we take — storage is what registration already waits on (`T-172`), and a download
page that turned a 2 GB build into our disk problem would make that harder, not easier.

**A domain of the project's own** (`T-300`). A host that is neither the tenant domain nor the
application's resolves to a project, and the showcase answers at its root with every path losing the
`/games/{slug}` prefix. The lookup is cached in its own budget and only runs after the two known
hosts are ruled out; a miss is not a 404, because an unknown Host header is usually our own traffic
under a name nobody told the server about. **The DNS record and the certificate are still set by
hand** — the code answers, Cloudflare has to route.

`dotnet test` 1225/1226 (the one red is `DocsFlowGraphTests`, red on `master` too: DOCS-FLOW names
`docs/archive/roadmap-phases-0-13.md`, which is not on disk). Frontend 463/463, contrast and density
clean. Verified by running it against a scratch database: both addresses render, a click counts and
redirects, a view counts once per reader, the follow form writes an unconfirmed row, and the owner
reads the numbers back off the project.

**Two defects that run found**, both in the half no unit test had reached yet: the showcase path was
read off the project rather than off the request, so claiming a domain turned the *subdomain* page's
links root-relative; and the domain lookup ran on the request's own context, whose tenant is exactly
what the lookup is trying to find — it answered "no project" for every domain and failed closed.
Both now have tests.

## 2026-08-25 — Every account gets a subdomain, and the database stops answering questions nobody asked (0.15.0)

**Subdomain multitenancy, built over several sessions in seven phases, then a closeout round on the
defects the review found.** The app has had accounts and an `OwnerId` on every row since the
beginning, and neither meant anything: the queries were scoped by whatever `Where` the endpoint
remembered to write, `/media` was a flat directory served to anyone with a GUID, and `blog.mooexe.dev`
was the only blog there was. `name.cedarclerk.app` for every account needs all three to be wrong.

**The tenant is the account** (ADR-206). No `Tenant` table and no `TenantId` column — `OwnerId` *is*
the tenant id, and what the account gains is a name: `TenantUsername`, a DNS label under a filtered
unique index, required at registration, with one rule set in Core shared by the Host resolver and by
the registration form, so a name that can be typed as a subdomain is exactly a name that can be
registered. Eleven child tables that belonged to a draft or a channel and to nothing else gained
their own `OwnerId`, indexed and backfilled, because every endpoint reaches them by parent id and a
filter on the parent would never have run. Uniqueness that used to be global went per owner: two
accounts can both publish `/hello-world`, which is the point of giving each one a host.

**Two compiled models, not one filter with an off switch** (ADR-207). A tenant context and a
platform context key different EF models, so the owner predicate is absent from a platform query
rather than disabled inside it. The one-model alternative — `platform || OwnerId == id` —
parameterises a value that is constant for the life of the context, and SQLite plans that as a table
scan, retiring every owner-leading index in the schema on every query. **An unset tenant reads
nothing** (ADR-208): the strict default means a forgotten call site produces an empty list somebody
notices, never somebody else's rows, and the paths that legitimately span owners have to say so —
`PlatformPaths` as a prefix list for requests, `CreatePlatformScope()` for jobs, the bot and startup.
Writes are held to the same rule: `SaveChanges` refuses a new row with no owner instead of storing
one nobody can ever see again.

**The legacy blog host is an account too** (ADR-209), named by `Cedar:BlogOwner` and resolved rather
than special-cased — an owner that cannot be told is a 503, not a cacheable 404 that reads as a blog
taken down — and it keeps its own host in every URL the app hands out, because those links are
indexed and sit in channel history that cannot be edited. **A subdomain serves a blog and nothing
else** (ADR-210): routing's endpoint pick is dropped, so the API and the app shell are the 404 they
should be rather than a 401; a signed-in identity is dropped on any blog host and logged, because
the tenant there is the host's owner and every row that session wrote would land stamped with it;
the auth cookie is host-only by explicit setting, not by default. **A file inherits the audience of
the posts that publish it** (ADR-211) — a door in front of `/media`, not a re-layout of 489 files
whose paths are fingerprinted in revision bodies. **The host → owner answer is cached by expiry
alone** (ADR-212), misses included, which is what makes a blog page cost zero database commands warm
instead of a dozen. And **an account can now be deleted whole** (ADR-213), with the audit row
outliving it and the orphaned rows earlier incomplete deletes stranded counted before they are
purged.

**Verified, with the numbers that were actually observed.** Backend `dotnet test` **1170/1170**, 0
failed, 0 skipped, against a 1108 baseline; CLI tests 127/127; the Angular development build
succeeds. Guard suites run individually: SchemaDrift 1/1, UiInventoryDrift 2/2,
ErrorMessageLocalization 11/11, DocsFlowGraph 2/2, TenantIsolation 11/11, TenantFilterGuard 6/6,
PlatformPaths 24/24. Three migrations — `AddTenantUsername`, `AddOwnerIdToChildEntities`,
`PerOwnerBlogSlugs` — round-tripped on a scratch database, apply → revert → re-apply, each `Down` a
real inverse. A live three-tenant matrix on a local server with the bot confirmed off: `alpha`,
`bravo` and the legacy owner all holding the slug `shared-slug` at once, each host serving its own
post and 404ing the others', with drafts by id, RSS, the blog index, private gates, invite grants
and scraper user-agents all landing where they should.

**It is not deployable, and the version bump to 0.15.0 is not a release.** Two disclosure defects
survive the closeout round and one data defect is already written into other people's immutable
history: unpublishing a private post — or never publishing it — makes its media world-readable to
anyone holding the GUID (`T-284`); an arbitrary `Host:` header switches tenant narrowing off on
`/media`, which also leaves project attachments, avatars and covers ungated (`T-285`); and five
call sites still stamp the legacy blog host onto every owner's X, Bluesky and Discord cross-links,
so those URLs resolve to another account's post (`T-286`). The third has a compiler-enforced finish
line — `dotnet build -t:Rebuild` reports exactly five `CS0618` warnings today, one per unconverted
call site, and an incremental build reports zero, which is how they went unnoticed. Four smaller
findings are on the board as `T-287`…`T-292`. Nothing here has run on the droplet, and the platform
side of the tenant domain — registration, wildcard DNS, the tunnel — is still `Q-5` (`T-293`).

## 2026-08-24 — Telegram reports its own engagement; an uploaded file belongs to a project (0.14.2)

**Two reports, and neither was a bug — both were features that had never been built.**

**The bot was not picking up likes and comments from Telegram because it never had.** The numbers on
a channel's row were the blog's own reactions and comments, summed over every draft ever sent there
— ADR-025 said so in its own consequence and left `message_reaction_count` "unbuilt". ADR-205 builds
it, and splits the answer three ways because the Bot API helps three different amounts. **Views are
not available at all**: a channel post's view counter is not in the Bot API, only in the client
protocol under a user account, so a Telegram source no longer offers that metric and the strip says
why. **Reactions** arrive as `message_reaction_count`, which Telegram sends only to an administrator
bot that names the update in `allowed_updates` — it is left out of the default set, and the library's
event API has nowhere to name it, so the service moved to `StartReceiving` with `ReceiverOptions` and
dispatches messages itself. **Comments** are counted in the channel's linked discussion group, as
replies to the post's automatic forward. All of it is counted from updates and none of it can be
fetched, so history starts at this deploy and the number is a floor whenever the service has been
down — stated on the screen rather than left to be found.

**An uploaded file now belongs to a project, or to nobody** (ADR-204). Filing a post into a project
moved the document and left its pictures behind, because `Asset` had no project at all; and with the
indie module on, the Assets hook pointed at the folder index while `/library` — where every uploaded
file actually lives — had no entry point on any screen, so a glossary illustration was reachable by
nothing. `Asset` gains a nullable `ProjectId` where null is a real bucket rather than a gap, a file
follows the document that uses it (first project to claim it keeps it), and the backlog is swept by
a button that says how many moved, never silently. The library grew a project strip; the project's
Assets screen grew a source strip — **Uploaded** beside **On disk** — with the uploaded half rendered
by the library component scoped to that project rather than by a second copy of it.

Verification: `cedar test --smoke` all green — 1015 backend, 463 frontend, 74 smoke. Two additive
migrations, no column redefined: `AddAssetProject`, and `AddTelegramEngagementCounts`, which adds new
snapshot columns rather than changing what the two ADR-025 ones mean — that would have put a cliff in
the middle of an existing chart at the day the meaning changed.

## 2026-08-24 — the trim tier, panel-edge tabs and the blog's own language (0.14.1)

**A second annotated-screens round, and the answer to most of it was one missing box.** Every
"too tall" note pointed at the same thing: the port drew chips and band passengers at a paper
control's height. ADR-200 adds `--hit-trim` (24px) with two jurisdictions — chips, and anything
riding a chrome band — so filter leaves, index tiles, the compact field and a panel header's own
controls read at the mirror's proportions instead of at button scale. `tools/check-density.mjs`
scores the tier rather than trusting it.

**Index tiles are cut into the board they switch, never floating above it.** The Hub, the task
board, the Stats chart and the Posts Manager all had a step between the tiles and the panel; the
strips now sit on the edge, and the manager's three tabs stand on one board that holds whichever
body is lit. A panel's name and its counter share a baseline, so the number no longer sits below
the word it counts.

**Two decisions moved.** The fullscreen toggle left the rail for the drawer lip (ADR-201) — the
rail's right side is the save state and the screen's one primary action, and a window control
standing among them read as a third thing the page offered. The blog index carries a language pick
again (ADR-202), this time scoped to the index alone: it sets the index chrome and which
translation a card previews in, and a post page still has no such control. The reading size and
face controls now reach the card teaser, which is what made them look broken from the page they
open on.

**A post can be filed into a project** (ADR-203) from the Posts Manager's Placement group — the
API existed since the module landed and no screen ever called it, so every post written before it
belonged to nothing. **Telegram channel pictures are copied down and kept as files**, drawn in the
blog header and beside every connected channel in Settings; a fresh copy is taken nightly and
whenever Refresh is pressed.

Also: numeric dates read MM/DD/YYYY everywhere in the app (the blog keeps its readable form); the
audience shelf states the share of views that reported no reading language instead of ranking
"Unknown" among real ones; the account popover became an identity row plus three rows rather than
a centred address over two plaques; the tool wall drops project tools on the Hub, where no project
is open; the RSS button is resin; the blog's order control stands on one line with the tag leaves;
and the month rule no longer crosses the timeline spine.

**Three defects found on the way, none of them reported.** `app-input`'s dense mode had never once
been dense: the global field face sits at (0,6,1) and no component selector could beat it, so it
repainted every ported field — `.field` is now an exclusion and the component owns its face. A
small paper button kept its 30px box under a coarse pointer for the same reason, so the controls
that name their own box now name it as the floor carrier's fallback. And a dialog's action row
could sit under the drawer lip, which is what intercepted the click.

Verification: `cedar test --smoke` all green — 1015 backend, 462 frontend, 74 smoke. Five smoke
tests were red before this round on stale expectations (the landing route, two renamed labels, a
reworked dialog, a hook that is no longer on that wall) and were brought up to what ships.

## 2026-08-23 — annotated screens correction pass

**All 25 annotated Cedar Clerk screenshots were reconciled as one cross-product pass.** The shared
shell now keeps project tools contextual and account tools global, compact controls obey their
surface density, overlays stay inside the viewport, and project workflows gained Blog projects,
cover images and task attachments. Posts, registration, settings, statistics, glossary and credit
activity were corrected in the same pass rather than as isolated screenshot patches.

The Writer closes the deepest annotations: Structure has a selectable Document root; clicking the
worktop outside the sheet selects the Document; Undo/Redo is a labelled group; Inspector always
names Blog, Telegram, X, Bluesky and Discord; detected Glossary terms can be excluded for one
Document language without changing the global term; revision history shows readable unified
added/removed/context lines; long AI work owns a modal progress surface; and Export is a wooden
destination rack with configured, unconfigured and unsupported networks all visible. A migration
adds the owner-scoped `DraftGlossaryExclusion` rows used by blog rendering.

Verification: production build succeeded; Angular tests 462/462, backend tests 888/888, CLI tests
127/127. Public local pages were opened in the in-app browser; authenticated Writer click-through
remains pending a signed-in local browser session.

## 2026-08-23 — ADR-192 follow-up (ADR-193)

**Nine pieces of feedback on the just-shipped blog redesign, acted on the same day.** The site-wide
RU/EN cookie toggle is gone — its "RU does nothing" report turned out to be an explicit-link-always-
wins no-op rather than a real bug, and rather than fix the sync, the toggle itself is deleted: the
index now reads in English by default (with a translated preview per card when a post has an English
translation), and each post keeps exactly the language switch it already had. Search is deleted too —
it only ever matched titles and tags, never body text, which is why a real word from a post's body
came up empty. The plain sort link became a proper dropdown: newest/oldest, most/least popular (by
view count), and "posts available in language X." The index now paginates 10 at a time with a "Show
10 more" link instead of rendering the whole list every load. Smaller fixes: tag chips are noticeably
shorter, the copy-link button moved from the bottom of a post to the top, and the post reader is a bit
wider than the rest of the blog on desktop.

## 2026-08-23 — The blog reader moves to the wood board (ADR-192)

**Imported a Claude Design brief for the blog and built its real features.** The single-post reader
now sits on the same wood the header and footer already use, held by two brass pins — `.post-sheet`
itself keeps ADR-179's flat, unrotated paper untouched. Everything the brief's script assumed but the
blog didn't have got built for real rather than skipped: a `?q=` search and a newest/oldest sort on
the index (both server-side, round-tripping through the URL alongside the existing tag filter), a
site-wide RU/EN toggle in the header backed by a `cedar_blog_lang` cookie, blog-wide prev/next
neighbour cards below a post (shown only when it isn't in a series — a series already has its own
prev/next), a copy-link button beside the Telegram cross-link, and a Literata/Source Sans 3 face
toggle added to the existing reading menu.

## 2026-08-23 — Martian Mono is `--font-mono` (ADR-191)

**`--font-mono` gets a real face, product-wide.** It was `ui-monospace, Menlo, Consolas, monospace`
since the token's introduction — ADR-180 considered swapping it directly for Departure Mono and
rejected that (a pixel face off its 11px grid reads as broken in a 13px code block), giving Departure
Mono its own `--font-readout` role instead. Marty asked for Martian Mono specifically, and this time
chose the direct swap over a second role: dates, counts, tags, code blocks — everywhere `--font-mono`
is read, app and blog alike.

Self-hosted the same way the other four faces are (ADR-143): `@fontsource/martian-mono`, six files
(latin/cyrillic × 400/600/700, the three weights any call site actually uses), copied unhashed into
`assets/fonts` for the blog and landing page per ADR-178's mechanism, `DesignTokens.FontFaces` growing
six matching `@font-face` rules with the `index.css` unicode-ranges.

## 2026-08-22 — the readout face, and one menu for reading (branch `UI_V2`)

**Departure Mono is the readout face, bound to 11px (ADR-180).** Marty asked for it; the file was
read before anything was decided. v1.500, SIL OFL, one weight, and its `cmap` carries А, я, Ё, ё, Ж,
щ, №, the em dash and the middle dot — the Cyrillic requirement is met in the font, not in a release
note. 22 KB of woff2 covers every script it has, so there is no subset to split and no
`unicode-range` to get wrong.

Its README states the constraint that shaped the entry: *pixel-perfect at increments of 11px*. Off
that grid the strokes go soft and a reader sees a rendering fault, not a style. Measured against the
tree, **11 of the 53 sized `--font-mono` rules sit at 11px** — `--fs-ui` alone accounts for twenty of
the rest — so a blanket swap would have put the face off its grid in four call sites out of five.
It enters as `--font-readout` instead, a named role beside `--font-mono`, used only where the size
is already 11px: the carpenter's rule and its numerals, the chart's readout slip, the drawer's
journal summary, the version on the rail, the three counters, and on the blog the footer rule,
comment timestamps, the language stamp and the comment anchor.

Three things a size test alone would have swept in are deliberately out. `Input`'s chrome field is
11px mono and is a thing you *type into*. `.path` and `.tile-name` are 11px mono because machine text
is compared character by character — which a pixel face makes harder. And the chart's axis carries no
size of its own; it inherits whatever the page hands the SVG, and a pixel face at an unknown size is
what the rule refuses. `--text-readout` was **not** moved from 21px to 22px to make the big metric
on-grid: 21 is the kit's own number, the prototype is the law for this port, and one crisp numeral is
not worth deviating from it.

The file is committed rather than pulled from a package, and that is not a reversal of ADR-178:
Departure Mono has no npm package, so there is nothing for a copy to fall behind, and a build step
that downloads a font is the worse dependency. It sits at
`cedarclerk-web/public/assets/fonts/departure-mono-1.500.woff2` with the OFL text beside it, version
in the name so a bump shows in a diff.

**One menu for reading, replacing the theme toggle (ADR-181).** ADR-179 refused the tool's chrome on
the blog because *a reader has no commands* — and a control that changes how the page is read is not
one of those; the theme toggle had been sitting there for months proving it. The rule that decided
the shape is `.claude/rules/ui-changes.md`: there was no panel, only a lone button, so the button
became the panel rather than gaining a neighbour.

An `Aa` trigger opens a paper popover with two segmented rows. **Тема** now has the third state the
toggle could never express — day, night, and back to the system setting, which a two-way flip could
not return to once clicked. **Размер текста** has three steps resolving `--fs-read` to 16, 17 and
19px, and moves the reading column only: chrome, the rule, the leaves and every number stay where
they are, because a reader scaling an instrument face would break the surface split from outside.
Both are written on `<html>` and remembered in `localStorage`; the head script applies both before
the first paint. No face picker, for three reasons the ADR states — the first being that Literata at
the reading numbers is a measured decision, not a preference.

The header takes the page language now: `RenderHeader` had a channel and nothing else, so its
strings could only be English, while all nine of its call sites already held the language. The four
labels join the per-language chrome the gate and the back-link already use.

**Measured and looked at.** `cedar test` 1444 green — one real catch on the way: `rail-header`'s
*numbers are mono* asserts the token by name, so the swap failed it; the assertion now accepts either
face and says why, since `--font-readout` falls through to `--font-mono` behind it. Contrast gate
green with the four standing exceptions. Rendered on the isolated stack: the menu in Russian,
Belarusian, English and Japanese (Belarusian's «Сістэмная» is the longest of the nine and is what the
segmented row was widened for), the large text step against a real post, and the readout face on the
blog's rule and on the app's rail and ruler, Cyrillic included.

`BlogIcons.Sun` and `.Moon` were deleted with the toggle that used them.

## 2026-08-22 — the blog joins the bench (branch `UI_V2`)

The port had closed every screen of the app and left the one surface a stranger actually sees. The
blog already carried the bench *palette* — T-101's generator has fed it since ADR-071 principle 6 —
and nothing else: 12px card radii, pill chips, a sticky white header, `system-ui` under a token that
says *Source Sans 3*, and four emoji standing in for the counts.

**Materials now cross to the server (ADR-177).** `tools/contract-tokens.mjs` gained a second list
beside `CONTRACT`: `MATERIALS`, fifty names for what a surface is *made of* rather than what it is
*for*. Both are generated into `DesignTokens` — `MaterialsLight`/`MaterialsDark` beside
`Light`/`Dark` — and a material may be the gradient, texture URL or shadow it actually is, because
the gradient refusal walks `CONTRACT` and deliberately not this list. The off-contract check and the
stale check widen to the union, so a material renamed in `styles.scss` still fails the run by name.
A member with no call site is dead weight, not a reservation: the list was pruned to what the blog
paints, which is why `--pine` and `--wall-hi` are on it (a served derivation dereferences them) and
`--paper` and `--ink` are not (they are `--sheet` and `--text` under a second name).

**The blog is served the faces it names (ADR-178).** ADR-143 left this open and said why the obvious
repair fails — the bundler content-hashes the woff2, so a hand-written URL dies at the next build.
`angular.json` now copies twelve files out of `node_modules/@fontsource/*/files` into
`assets/fonts/` unhashed, and `DesignTokens.FontFaces` declares them. Vollkorn 600/700, Source Sans
3 400/600, Literata 400/600, latin and cyrillic each. Every rule carries an explicit `unicode-range`
taken from the packages' own `index.css`, because two subsets of one family and weight declared
without one do not compose — the later rule wins for the whole range.

**The blog is a sheet on the wall under a park-sign rail (ADR-179).** The ground is the plaster wall
under the lamp; the header is dark wood with cream lettering; the footer is the carpenter's rule,
milled brass with ticks and mono numerals. A post is `writer.html`'s sheet at the reading numbers —
paper stock with its noise, a square 3px cut, the two-layer sheet shadow, Vollkorn titles and
Literata body at `--fs-read`/`--lh-read`. Index cards are the same stock with the kit's 2–3px hover
lift, hung on brass pins along a pencil rule; each month opens with a stamp. Tags are leaves, the
roadmap's statuses are stamps, focus is the brass pair from ADR-140, and the `border-radius: 999px`
that ran through eleven classes is gone. The theme toggle is a paper button at the chrome box
(ADR-175) whose glyph is chosen by `data-theme` in CSS rather than by the click handler.

What the blog deliberately does **not** take: the hook rail, shelf panels, the drawer, index tabs
and the worktop grid. Every one is a place to put a command, and a reader has none.

**No emoji.** 👍 👎 💬 👁 🔒 and the `☰`/`↑` dingbats are `CedarClerk.Core.BlogIcons` — nine stroke
glyphs on the kit's 16px grid, inline in the markup because `assets/cedar-icons.js` ships with the
Angular bundle and the blog host never loads it. Every count beside them is mono.

**Two defects the port found and fixed on the way.** The page ground carries `--wood-ink`, not
`--text`: the wall darkens at night while paper stays cream (ADR-141), so a page-level `--text` was
invisible on the ground — every paper surface re-declares the paper ink for itself. And a rule drawn
*on* the sheet takes `--border`, never `--rule-ink`: the pencil turns cream at night because the
wall does, and cream on cream is not a line. It was the title divider, the `<hr>`, and three border
tops.

**Measured.** The contrast gate is green with the four standing ADR-140/172 exceptions and no new
ones; the census now reports nothing on the blog but the rail button, whose pair the table already
covers under wood — the same finding the app's own `.btn.rail` carries, and a limit of the census's
backdrop inference rather than a defect. `cedar test` 1444 green. Four page types were rendered in
both themes on an isolated stack (scratch database on 8090, `blog.localhost`, bot confirmed
disabled): index, post, series, gate, plus the post at phone width.

**Found in the app, not fixed here** (`T-275`): `styles.scss` has the clause-3 problem ADR-178
avoids server-side. Its sixteen `@fontsource` imports are per-subset sheets with no
`unicode-range`, latin first and cyrillic second, so the cyrillic rule shadows the latin one for the
whole range and Latin text in the app is drawn by the fallback stack while the token claims Vollkorn
and Literata. It is a decision rather than a one-liner — sixteen local `@font-face` blocks, or the
packages' `index.css` with the payload that implies.

## 2026-08-22 — the bench fidelity pass (branch `UI_V2`, one commit)

**What was audited.** Marty asked for the port to be checked against the design prototype, because
the app "must look as much as the design's prototype". A fleet audit captured 48 screenshots at
`1440x900` — both themes, prototype beside app, the app on an isolated stack (scratch database on
8090, `ng serve` on 4300, bot disabled) — and compared code and pixels. 399 raw findings merged to
294 real ones: **172 defects**, 98 deliberate divergences already bound to an ADR, 18 missing
features already on the board, 6 unsure. The defects were applied by file zone — tokens, bench
chrome, bench primitives, hub screens, writer and metrics, the other screens, then the
core/i18n/icon leftovers — and a tester and a regression lane closed what the fixers opened.

**Tokens** (`styles.scss`, both themes where the kit has both; `DesignTokens.generated.cs`
regenerated). `--fs-34` for the hero title. `--bench-cover-h` 296px, `--bench-dock-w-hub` 262px /
`-wide` 322px, `--bench-worktop-edge-h` 26px. Three bench durations — `--dur-tap` 150ms,
`--dur-control` 190ms, `--dur-move` 250ms, 1ms under reduced motion. `--rail-ink-dim`,
`--rail-btn-face` / `-hover`, `--tile-edge`; `--hook-face` is the kit's `rgba(0,0,0,.16)`.
`--radius-shelf` 5px; `--radius-lg` now resolves to `--radius-md` — nothing rounder than a plaque.
`--shadow-sheet`, `--shadow-tag`, `--shadow-worktop` and its inset. `--leaf-dried-bg` / `-edge` /
`-ink`. **Brass split by job**: `--brass-lo` is back to the kit's metal (`#8A6226` / night
`#7E5A20`), and a new `--brass-ink` (`#674A1C` / `#684A1A`) is what stamps and chart ticks write
with; `--brass-soft` mixes from the ink. `body` is set at `--fs-ui`; a global `.margin-note` rule is
the one place the hand-written face is allowed; `.tiptap h1`–`h3` are in `--font-display`.

**Kit.** The page ground is the plaster wall in both themes — night was light before (ADR-174: the
shell owns the viewport, `.body` scrolls, the page no longer scrolls as a document). The rail
header spans the full width with the hook rail below it; shelves stretch full height. Display-serif
headings: `--fs-34` on the hero, `--fs-27`/`--fs-19` on the sheet. Inspector groups are uppercase,
stamps are dashed, the settings save bar is in flow. Pickable leaf tags draw idle on the dried
stock and active green; a label leaf stays green (ADR-176). The doors' theme toggle is a paper face
at the chrome box, the rail tint having measured ~1.5:1 on the day wall (ADR-175).

**Screens.** The writer's toolbar moved into a compact strip inside the worktop and the title is
the sheet's first heading. The projects shelf carries its margin note; the stats tab has a
freshness line, a "Прочие" fold and a slug row in the inspector; planner and builds cards no longer
clip their labels; dates are `dd.MM` by UI language.

**Icons and i18n.** Six icons — `sun`, `moon`, `arrow-up`, `arrow-down`, `caret-left`,
`caret-right` — replace glyph characters; `npm run check:icons` is current at 82 icons and 305 call
sites. `shell.settings` has a short hook caption (`Настр.`), the full word on the tooltip.

**Regressions the tester found, all closed.** The day-theme toggle on the doors (R1), the RU/EN
leaves sharing one fill (R2), the save bar floating over the signature buttons once the shell took
the viewport (R4), the clipped settings caption (R6), the strip-meta gap (R7).

**Gates.** `cedar test` green: 1002 backend (guards included), 442 frontend units, contrast 0
failing with 4 accepted exceptions, density 0 failures, 237 files scanned. Smoke subset against the
isolated stack 26/29 — the three failures are `e2e/helpers.ts` hard-coding `BLOG_ORIGIN`/`BLOG_API`
on 8080, environmental (`T-266`). The installed `cedar` predates the icon phase: `.\Scripts\install-cli.ps1`
is due.

**What is left for Marty.** The eye-checks in `docs/tasks/TASKS.md` — the before/after shots live in
the session scratchpad only. Two decision rows on the board: a ruling on the six ADR-bound
divergences the audit could not touch (`T-273`), and whether the look-changing adaptations —
ADR-150's two-row strip, ADR-138's paper floors, ADR-160/168's tile wall, ADR-148/149/161's metrics
tab, ADR-159's writer chrome — stay or are reversed towards the prototype (`T-274`). Seven smaller
rows, `T-266`…`T-272`. `T-234` is still the gate.

## 2026-08-20 — the Cedar Bench port, end to end (branch `UI_V2`, Stages 0–6)

**This is the entry for the whole port.** The five below it are the sessions it ran in; this one is
what changed between 0.12.2 and whatever version Marty stamps on the merge. Nothing is deployed and
nothing is tagged — `Consts.CurrentVersion` still reads `0.12.2` on purpose, because the number and
the merge are his call (`T-234`).

**The app has one look now, and it is a workshop.** Cedar Bench — mirrored from Claude Design at
`.design-sync/ds-v2/` — stops being a second palette and becomes the only one. The skin mechanism it
replaces is gone entirely: no `data-skin`, no `Skin` union, no `setSkin`/`applySkin`, no `cedar-skin`
key, no Appearance control and no strings behind it in either language. `data-theme` is the one
styling axis left, joined by `data-surface`, which says whether a band is paper or chrome and carries
the density and touch floors with it. Thirty-eight ADRs (136–173) were written **before** the code
they govern, and the plan that framed them — `docs/design/UI-V2-PLAN.md` — settled ten questions
before a line moved.

**Every screen in the app was redrawn**, not restyled: twenty-one bench components under
`src/app/bench/`, a shell that wraps every authenticated route (tool wall, sign board, drawer, ruler),
and then the screens in four patterns — hub-like onto a worktop and a summary shelf, stats-like onto
one index strip plus one panel plus an inspector shelf, writer-like onto an index shelf plus one sheet
plus an exclusive-scope inspector, and the kanban onto the bench's materials with its own geometry
kept. Four doors — login, register, terms, privacy — sit outside the shell and declare their own
surface. Two detail modals and the drafts folder popover were retired on the way: a shelf beside the
list beats a card on top of it.

**What the port is really about is that the measurements were wrong, and now they are not.** The
contrast checker was rewritten first, before any repaint, and it immediately falsified three rounds of
its own green: it had been compositing nothing, so it scored the wrong pairs. Twenty-two rules each
hand-mixed a state tint out of an ink and whatever paper the component sat on — one visual idea
shipping as twenty-two ratios, the worst at 3.63 against a 4.5 floor. Night turned out to have been
copied rather than derived, with body text at 1.01:1 on the page ground. `--t3`, barred from carrying
content, was the only label of a button. `--alt` was ink on `--sheet` at 1.06:1. A drift guard that
existed to catch exactly this had been comparing ten hand-listed tokens, so the blog had been drawing
pre-derivation chart colours with nothing able to say so. Each of those is now a token, a pair in the
table, or a guard that walks everything instead of a list.

**The cleanup found the same thing at a smaller scale: the remembered numbers were wrong.** The
dead-CSS sweep was planned against forty-two files and deleted one selector fragment and four
dictionary strings — `.btn-accent` and `.btn-ghost` are live in fourteen blocks across eight page
stylesheets, and
`.icon-btn` had been gone for a while. What it found instead was eight modal buttons whose CSS went
with the port while their markup did not, so four dialogs currently footer in platform grey
(`T-262`). The smoke suite was worse: sixteen of fifty-seven tests red, and **not one of them red
because a behaviour had been lost** — fourteen were bound to class names the repaint moved, and two
had been red since ADR-135 rewrote the landing on `master`, unnoticed, because a suite red for one
reason hides every other reason it is red. It is rebound to roles and accessible names (ADR-171),
which is what a screen reader announces, and it caught the one genuine regression the port did cause:
at 390px the writer has no sheet to write on (`T-265`).

**Two things were measured for the first time while settling the branch.** The brass bar marking the
current tool on the hook rail had shipped through the entire port with nothing covering it; it reads
4.42 at night and 2.91 by day against the 3.0 a graphical object owes. It is now in the table and the
day figure is accepted with its reason (ADR-172) — reached by measuring rather than assuming, which
mattered, because the "raised sign tile" everyone would have named as the fallback cue turns out to be
relief and not colour at all: it scores 1.00 against the very wall it hangs on. The cue is the bold
caption, which passes. Mutating that new pair then exposed the exception mechanism itself as too
broad — `except` was a bare string consulted in both themes, so accepting a day shortfall bought
silence at night, where the same pair passes by a wide margin. Exceptions now name the theme they were
derived in, which tightens ADR-140's three as well as the new one.

**And the icon inventory stopped depending on memory.** `icon-usage.generated.ts` is committed,
describes our own call sites, and shipped stale twice during this port because a repaint moves call
sites and nothing re-ran the generator. It now has a `--check` mode that renders through the same code
path as the write, and `cedar test` has an Icon inventory phase beside the contrast and density
contracts (ADR-173) — the third member of a family the repository already believed in and had left
this one artifact out of.

**What this does not include.** Nobody has opened any of it. Every checker is green — backend,
frontend units, icons, contrast, density and the fifty-seven-test smoke suite — but the verification
map's marks are reset for every ported screen, which is now most of the app, and a measurement is not
a look. `styles/_forest.scss` is still there: 1 229 lines scoped under an attribute nothing sets, so
it compiles into the bundle and matches nothing, and deleting it is a destructive-operations event
waiting on Marty (`T-235`). The shell ships with no width breakpoint of its own, deliberately — the
design system states one viewport and inventing a ladder underneath it is what ADR-147 refused, so the
narrow screens wait on designs (`T-236`).

## 2026-08-20 — every remaining screen (branch `UI_V2`)

Stage 5 of the Cedar Bench port: the fourteen screens the three reference screens did not cover, in
four groups by pattern. Six ADRs (163–168) ahead of the code, no new bench components, ten new board
rows, one palette value re-derived, and the branch still unmerged with the version still untouched.
Every screen in the app is now drawn from the kit.

**The hub-like three.** `/projects`, the planner and the builds screen took a worktop, paper cards
and a summary shelf on the right. The question Stage 4 left open — whether the project index
survives the hub's switcher — is answered yes and written down: what the dock absorbed is the
*switch*, and the list keeps what a switcher refused, which is the create modal, the empty state with
the example project, and search over an unbounded list. Neither side grows the other's controls. Two
screens were navigating to `/editor?id=`, a parameter the editor does not read, so a devlog draft and
a document chip silently opened whichever draft was newest; both now pass `draft`. Rows and chips
became anchors, so a middle click opens a project, a task or a document in a new tab, and the builds
screen gained the priority and due date it was not drawing — the alternative is one object drawn two
ways one click apart.

**The stats-like four.** The media library, project assets, `/drafts` and `/admin` each became one
board: an index-tab strip above, one panel holding the list with its search and view controls in the
panel's own header, and a right column that is never empty. That shape is what retired two detail
**modals** — a shelf beside the list replaces a card on top of it, so the list stays readable while
you look at one file — and the drafts folder popover, which became a Folders shelf. The counts that
used to be hand-written spans are tab badges now, held to one rule: nothing at zero, `99+` past
ninety-nine. One count deliberately is not: the per-folder tally on `/drafts` sits on an unbounded,
user-created list, and an empty folder you just made has to stay visible to file into.

**The writer-like two.** The Posts Manager and the glossary became an index shelf, one sheet and an
inspector whose scope is exclusive — with a subject picked it describes the subject, without one it
describes the collection, and every row declares which, so a mixed sheet is a query rather than a
convention. Every outbound link left the manager's sheet for the inspector instead of being drawn in
both places. The manager's hand-rolled two-path SVG became `app-growth-chart`, three named series on
fixed slots with the metric buttons as the legend-filter. `/comments` was investigated and
deliberately **not** moved into the drawer: it has been a fragment under its own post since N7, and
the drawer is mounted once by the shell, so a Feedback tab there would follow you onto every screen
and would need chrome to reach into a page's selection.

**The board, and the four doors.** `project-tasks` is a kanban the kit does not draw, so it took the
bench's materials and kept its own geometry — four columns as shelf panels, cards as task tags, the
page title and its counts moving to the ruler. What a board *becomes* on the bench is now a written
brief rather than an assumption. Login, register, terms and privacy are the one place nothing above
them declares the surface, so they declare it themselves: the bench wall, one paper card, and the
theme toggle as a rail button because the rail's dots menu is not out there.

**What measuring found this time, and what it did not.** The census came back clean of the ports —
fourteen screens added no new ink-on-surface combination at all, because they took primitives instead
of page-local colour, which is the whole point of having primitives. It did surface one pair the
palette had never covered: `--danger` on `--asoft`, which the poll editor's remove glyph paints, at
4.46 against a 4.5 floor under the darkest accent preset. `--danger` took the smallest hue-preserving
step that clears and the pair joined the table, so the gate now measures it in both themes across all
five presets.

**Four defects fixed rather than boarded.** `app-input` and `app-button` declared their boolean
inputs without `booleanAttribute`, which made the bare-attribute form a compile error at every call
site and cost two agents a build. The `/stats` redirect was a *string*, which the router reads as a
path, so an old metrics bookmark resolved to the posts list and silently lost its `?tab=stats`; it is
a `UrlTree` now, with a test that fails if the query is dropped. `shared/count-badge.component.ts`
is deleted: its last consumer took its tally as a tab badge, so the hide-at-zero and `99+` rules live
in one function.

And the fourth was found by a guard failing on the palette change and then turning out to have been
blind: `DesignTokenDriftTests` compared ten hand-listed tokens against `styles.scss`, so when Stage 4
re-derived `--series-3`, `--series-4` and `--series-6` against graph paper, the generated C# copy the
blog and the landing page read was never regenerated and nothing said so. The blog has been drawing
the pre-derivation chart colours since. The generator has been re-run, and the guard now walks every
token it emits rather than a list somebody has to remember to extend — which is the drift its own
comment says it exists to catch.

**What is left.** Stage 6 is cleanup and ship. The ports found four gaps in the kit itself — the
field passes through no `autocomplete` and no `maxlength` and cannot be named without a visible
label, there is no select, textarea or checkbox at all, a button has no anchor form, and a task tag
cannot merge a query — and six more features the API cannot answer, on the board with the thirteen
from Stage 4. Nobody has *looked* at any of it: the verification map's marks are reset for every
ported screen, which is now most of the app.

## 2026-08-20 — the three reference screens (branch `UI_V2`)

Stage 4 of the Cedar Bench port: Stats, the hub and the writer, in that order of blast radius. Six
ADRs (157–162) ahead of the code, three new bench components, thirteen board rows for the features
the screens found the database cannot answer, and the branch still unmerged with the version still
untouched.

**Stats: the legend is the filter, and the chart may not overclaim.** Four 600x160 single-metric
sparklines became one `app-growth-chart` — every selected source on one axis, sharing one window.
The sources are the legend *and* the filter: multi-select leaf tags, one line each, never a sum,
because a draft published to several channels counts its full view total against each of them. The
four fixed stat cards became one readout per drawn source, and the audience grid moved out from
under the chart into a 320px right shelf that names whose readers it is counting rather than leaving
it to be assumed. Two controls the kit does not have were added because one chart needs them: a
metric strip, filtered to the metrics at least one source tracks, and a chart/table pair over the
same numbers. The window is now a function of the selection rather than a fixed period, which is new
behaviour and is stated in the panel's own counter. What the chart refuses to do is the point of
ADR-158: the readout slip, the crosshair dots, the live region and the emitted event are one
computed read out of the series, so no caller can state a number the curve does not draw; `openTail`
is a *required* input because a half-finished bucket plots low and reads as a collapse while
silently dropping a full one is the same lie mirrored; a series carries a fixed slot rather than an
array position, since deselecting a source reorders the array and index-keyed colour would repaint
the survivors; and every line carries its own name beside its own dot, because the six-colour
palette fails the CVD and normal-vision separation checks in both themes and a legend alone would be
the only thing telling two lines apart. Every number is also in the page as a hidden table, the open
bucket included and marked as open.

**The hub: a tile is a door, and the wall shows the modules the app has.** The project head became
an `app-worktop` with the last edit chalked on its edge; the `/projects` list was absorbed as the
left shelf's switcher, which leaves two entrances to the project list for one stage and is the
smallest wrong state available while the route tree belongs to `T-226`; the featured-type cards and
the tile row, which showed the same documents twice under two groupings, became four
`app-module-tile` plates and one documents panel. Four plates and not the kit's six: Documents is
the panel directly below rather than a door to the screen it stands on, and per-project metrics do
not exist because `Channel` carries `OwnerId` and no `ProjectId`. The kit's engine-and-platform tag
is not drawn for the same reason. A resume card sits above the wall, because the hub's job is to get
you back to work in one click.

**The writer keeps no chrome of its own.** The topbar is gone. Its save state and its one primary
action are *published* to the rail through `RailActionsService` — a signal holder of the same shape
and for the same reason as `RulerService`, carrying data and never a `TemplateRef`, so a page cannot
hang arbitrary markup on shared chrome, and `RailAction` is a single object rather than a list, which
makes a second rail button unrepresentable instead of merely discouraged. The horizontal meta strip
became a 340px inspector that describes either what is selected or the document and never both: the
panel's counter is the scope word, every row declares its own scope, and `core/selection-spec.ts` is
a pure function over a node's shape so the claim is checkable rather than conventional. The rows that
exist are the ones a TipTap node can answer — the kit's resolution, byte size and originating asset
are not on the node and are left out rather than drawn as empty values. The two user-ordered toolbar
rows became one strip that fits itself: `measureToolbar()` reads real widths in one layout pass and
decides how many groups stay on the first row, whether a second is needed and whether the captions
are drawn. Nothing is stored and nothing is a width breakpoint. That voided `ToolbarLayoutJson`, and
with it went `core/toolbar-layout.service.ts`, its injection in the auth guard,
`POST /api/auth/toolbar-layout`, the Appearance panel's whole toolbar half and four inventory rows.
A 224px structure shelf now walks the document, and it holds no selection of its own — the lit row is
derived from the caret and a click moves the caret, so neither direction can loop.

**Three new bench components.** `app-growth-chart`, whose refusals are above. `app-module-tile`,
whose API is closed on purpose: one number, no content slot, because a second number belongs on the
screen the tile opens and a projection slot is how the second one gets in. And `app-log-line`, the
only bench primitive whose surface is an input rather than a pinned value — a line paints no ground,
so the stock it lies on is named by whoever lays it there; both settings are declared so the density
lint scores both, and only the size moves.

**What the screens cost in colour.** The tool strip is the first band in the app to carry captions
and buttons at once, and the chart draws on ruled paper rather than flat paper. Fourteen pairs were
added — the strip's hover wash over the rail, brass on the rail and on a recessed chip, a stamped
chip against the wood, the six series and the brass event label on graph paper, and the two bar
fills against their tracks. Four values were re-derived by day with the hue kept and the smallest
step that clears taken: `--series-3`, `--series-4` and `--series-6` fell under 3:1 where the graph's
two rules cross and the ground is at its darkest, and `--brass-lo` fell under 4.5:1 there for the
same reason. Two call sites moved rather than their token — a group caption and the GIF button are
words, and the soft cream measures 3.35:1 on the rail, which is a glyph's floor and not a label's,
so both take the cream at full strength and the soft one keeps the crumb separator and the resting
tool glyph it was derived for. The one real defect was the retry button: rust ink laid straight on
the rail is 1.13:1, invisible, so it became a stamped chip on the opaque `--danger-soft` wash, which
is how the system already says something is wrong on chrome.

**What the settle left open.** Nobody has looked at the three screens; they are unit-tested and
measured, and their verification-map marks are reset rather than inherited. Thirteen features the
kit draws and the database cannot answer are on the board as `T-238`…`T-250` — the pre-publish check
system the writer's drawer tab and check count both wait on, X and Bluesky snapshots, a multi-source
stats endpoint, CSV export, axis event markers, a per-post breakdown, and six on the project side.
And one measured defect is named rather than patched: the drawer lip and the shelf-panel header both
write `--rail-ink` on `--shelf-frame`, whose light stop is `#B68B60`, so a lip title reads 2.51:1 by
day and its summary 1.79:1, and no flat ink clears that ramp end to end — which material those two
bands are made of is a design call, not something to settle from the outside.

## 2026-08-20 — what the browser said about Stage 3 (branch `UI_V2`)

Stage 3 was verified by rendering it rather than by reading it: two passes in real Chromium against
the built bundle, driving the keyboard and measuring resolved values. They found four defects no
static check could see, and a fifth was found by distrusting a green gate. Two ADRs (155, 156), the
branch still unmerged and the version still untouched.

**The touch floor is inherited, not selected (ADR-156).** `[data-surface="X"] button` matches at
every depth, and bench nests the two surfaces inside each other in both directions — a shelf sheet
inside chrome, a ruler on paper, a shelf panel on the worktop sheet. Where both selectors matched,
source order decided, so three of seven measured nestings stood at chrome's 30px while owing paper's
44. Swapping the blocks moves the same bug onto the other nesting; excluding the opposite surface's
descendants leaves a thrice-nested control at no floor at all, at 20px. The floor is carried by
`--hit-surface` now, a custom property being the one thing that resolves to the *nearest* declaring
ancestor at any depth in either direction, with per-member specificity unchanged. Four page
stylesheets carrying the same pinned-surface pattern with a bare `44px` moved onto it too.
`check-density.mjs` gained a seventh rule for it, checked red against the code that shipped the
defect and against both naive repairs, and `e2e/17-density.spec.ts` measures all seven nestings
under a coarse pointer.

**The focus ring stopped being re-declared.** ADR-140 says the ring is global and never
re-declared; eleven stylesheets disagreed. `outline: none` on the settings fields, the editor title
and the three pickers left the cream halo alone as the whole indicator at 1.02–1.24:1 — the exact
half-measure ADR-140 warns about. `/drafts` painted its ring `--accent`, which is a user preset and
so must never be half the floor. Four editor controls painted it `--abord`, accent-derived, and
measured 2.19–2.76 against a 3.0 floor in both themes. An Appearance slider suppressed outline and
box-shadow both and carried no indicator whatsoever. The only `outline` declarations left in the
front end are the global rule and the `.tiptap` exclusion. A component `box-shadow` on a focusable
control is now written `:not(:focus-visible)` throughout, including all four copies of the
view-toggle rule and a validity wash that out-specified the halo unconditionally.

**The rail got a tally (ADR-155).** Both unread-feedback badges died with `page-header`, leaving the
shell with no global signal and `bench-shell` never fetching the count at all. The Metrics hook now
carries a work ticket notched over its top corner — a tag on the hook, not a notification dot, the
wall being made of brass and sign tiles rather than phone chrome. It reuses `indexTabBadgeLabel` for
the hide-at-zero and 99+ rules rather than growing a third copy, takes the index tabs' own tokens so
chrome has one counter and not two dialects, and folds its label into the link's accessible name
instead of replacing it. The kit defines no badge for the rail; this is an extension and says so.

**The dots menu stopped lying.** Escape shut the panel and dropped focus to `<body>`; it returns
focus to the trigger now, and an Escape from elsewhere shuts the panel without reaching for focus. A
click on an entry that navigated left the panel standing; entries that act in place (theme,
Appearance) keep it open and the four that navigate close it. The project tile was a `<button>` with
`aria-haspopup="true"` that opened no popup and had no href — it is an `<a routerLink>` to the hub
now, so middle-click opens a tab and the ARIA describes what actually happens.

**A green gate hiding a false reason.** `check-contrast.mjs` accepted the ring's shortfall on
`--wood-hi` because "nothing focusable is placed on a bare frame". The drawer pull falsifies that: a
transparent button filling the lip, whose background is `--shelf-frame` itself. The ramp is a ring
surface now and the exception carries the real argument — walked end to end neither layer clears the
floor where the frame is light, 1.98 for the halo and 2.47 for the outline, and the 4.33:1 boundary
inside the band is what carries it, the same argument ADR-140 already rests cork and the ruler on;
at night the surface itself carries it at 4.96. ADR-140, ADR-138 and the styleguide's own rendered
caption were all still asserting the old reason and now say this one. The census run added no
failure the branch did not already have — its 42 sub-floor combinations are byte-identical to the
commit before these repairs — but three pairs were added for the tally, which it structurally cannot
reach: the resin is 90% opaque so its backdrop is part of its colour, and an absolutely positioned
ticket resolves to the page ground rather than to the hook it hangs on. Against the wall, a hook
tile and a hovered hook tile it holds 6.03–6.89.

**Green.** `check-contrast` 0 failing pairs with 3 printed exceptions, `check-density` 213 files and
0 failures with 9/9 rules measuring something, `ng build` clean, `ng test` 235 passed and 0 skipped,
`dotnet test` 883 + 127 passed.

## 2026-08-20 — the bench gets its shell (branch `UI_V2`, Stage 3)

Stage 3 of the Cedar Bench port: the chrome the app is assembled from, and the shell that holds it.
Eight more ADRs (147–154) landed with it, and the branch is still unmerged with the version
untouched.

**The shell.** Seven components under `bench/chrome/` — `app-bench-shell` and the
`app-rail-header`, `app-hook-rail`, `app-shelf-panel`, `app-index-tabs`, `app-bench-drawer` and
`app-ruler-bar` it assembles — plus one parent route in `app.routes.ts` wrapping every authenticated
child unchanged. `/login`, `/register`, `/terms` and `/privacy` stay outside it and keep their own
theme toggle, which is also what removes the debug console from them: by the shape of the route tree
rather than by a URL list (ADR-139). `shared/page-header.component.*` and the editor topbar's nav row
are gone — navigation is the hook rail, and the crumb, the save state and the account belong to the
rail header. The editor's status bar dissolved (ADR-153): its word, character and sync readouts are
published to the ruler through a new `RulerService`, while the invisibles and fullscreen toggles and
the error-state retry-save moved to the tool strip, since a rule takes no controls. The debug console
kept its rows, its service and its interceptor and lost its tab, its panel and its own animation to
the drawer; `hostBarHeight` retired with the bar it measured. The `@media (pointer: coarse)` rule is
scoped to paper with a chrome counterpart beside it, so a touch pointer can no longer push the ruler
to 44px or the rail past `--bench-rail-h` (ADR-138) — `npm run check:density` went from six failures
to eight rules that all measure something, the coarse-pointer one included.

**The decisions, and whose they are.** Four are Marty's: metrics stays a tab body inside the Posts
Manager instead of becoming a route again (ADR-148); the stats sources become the kit's
legend-filter while the app's 7–180-day slider stays, drawing one line per source and never their
sum, because ADR-025's per-channel attribution makes any cross-source total double-counted by
construction (ADR-149); the tool strip is adaptive, one row or two by measurement, group captions
the first thing it gives up, and toolbar customization deleted outright (ADR-150); and the narrow
screens are commissioned from Claude Design rather than invented here, the kit stating `1440x900`
and no second geometry (ADR-147). Four are the implementer's defaults, taken under a general
instruction to proceed and recorded as defaults rather than as answers Marty gave: the theme toggle
and the Appearance trigger behind the rail's dots menu, which also hoists the Appearance panel out
of `editor.component` into the shell (ADR-151); the accent picker surviving on ADR-141's five vetted
pairs (ADR-152); the console becoming a drawer and the status bar dissolving (ADR-153); and the
shell's content region declaring `data-surface="paper"` (ADR-154).

**What the shell cost in colour.** One real defect, and the census is what found it: the rail's dots
menu is a paper board pinned under a cream-inked rail, and it inherited that cream onto its own
`--sheet` surface, so every label in it sat at 1.12:1 — a menu that was there and could not be read.
It takes `--text` now, and its spec stopped asking that *every* colour on the rail be the cream and
started asking two things instead: a rule that declares a paper surface takes paper ink, and exactly
one rule may, so a second cannot arrive unnoticed. The cork sheet made `--wood-hi` a text background
for the first time — the wood block's own note allows only `--wood-ink` there — and the pair stood
at 4.477 by day against a 4.5 floor, so the ink took the smallest step that clears with its hue
kept, `#3B2A18`→`#3A2918` (ADR-074); being a contract token, it moved the generated copy in
`DesignTokens.generated.cs` with it. Two translucent faces that carry a caption became tokens,
`--hook-face` and `--tab-badge`, because a surface with text on it has to be nameable in the pair
table and an inline `color-mix` is not, and two rules had hidden a gradient in `background-image`
with no `background-color`, leaving the census to read the frame behind them rather than the tile
they paint. Six pairs were added; the two accepted `--glass` exceptions retired with `page-header`,
the surface that painted them. What the census still cannot reach is named rather than excluded — it
resolves a translucent backdrop only through CSS nesting, and these components write flat selectors,
so the hook's fill and the tab badge read against the page ground there; both are measured properly
by the pair table instead, at 9.29 and 6.28.

Stages 4 through 6 — the three reference screens, the remaining pages by pattern, cleanup and ship —
are `T-222`…`T-237` on the board. No decision waits on Marty any more; what waits on a deliverable is
`T-236`, the narrow-screen designs, whose brief is written as `docs/design/bench-responsive-prompt.md`
and which blocks `T-034` and `T-237`. The smoke suite is half-rebound (`T-233`): the shell-level
selectors are done and eleven spec files still bind page-body markup Stage 4 moves.
`styles/_forest.scss` is still neutralised rather than deleted, waiting on Marty as `T-235`.

## 2026-08-20 — Cedar Bench becomes the one look (branch `UI_V2`, Stages 0–2)

The design system mirrored from Claude Design into `.design-sync/ds-v2/` stops being a second
palette and becomes the app's only one. Planned before written: `docs/design/UI-V2-PLAN.md` settles
the ten questions the port turns on — theming attribute, token namespace, component layout,
half-pixels, night, density, icons, fonts — and eleven ADRs (136–146) landed ahead of the code they
govern. Three stages of six are done; the branch is not merged and the version is untouched.

**Stage 0 — the checks the rest of the port is measured by.** `check-contrast.mjs` was rewritten:
alpha is composited over the backdrop instead of discarded, a layered background resolves to every
colour it can paint, a gradient is walked along its whole ramp with a bisection to the exact
crossing where an ink passes through the surface's own luminance, the two-layer focus ring is scored
against every surface a control can sit on, and a token on the server-rendered contract list that
resolves to a gradient fails outright — that list is now one file, `tools/contract-tokens.mjs`,
read by the checker and the token generator alike. `npm run check:contrast:census` is a second mode
answering a different question: it walks what the app actually paints — component stylesheets, CSS
inside a `.ts` `styles:` array, and the CSS inside the C# raw strings of the blog, the landing page
and the draft preview — and reports every ink-on-surface no pair in the table covers.
`tools/check-density.mjs` enforces ADR-138's surface split behind `npm run check:density`, and
`cedar test` gained a Density phase, its place in the phase list pinned by `PipelineTests`.

**Stage 1 — the palette, and the end of the skin.** The bench values are written into the bare
`:root` and `:root[data-theme="dark"]` blocks of `styles.scss` — the only place a token is both
contrast-checked and emitted into `DesignTokens` for the blog and the landing page. Contract names
keep their spelling and stay flat colours; bench material names sit alongside them (ADR-137). Night
was re-derived against cream paper rather than copied: the design system's own night block puts body
text at 1.01:1 on the page ground (ADR-141). The skin mechanism is gone — no `data-skin`, no `Skin`
union, no `setSkin`/`applySkin`/`loadInitialSkin`, no `cedar-skin` key, no Appearance control and
no strings behind it in either language; `data-theme` is the only styling axis left (ADR-136).
`styles/_forest.scss` is neutralised rather than deleted — nothing writes the attribute its every
rule is scoped under — and the deletion of the file itself waits on Marty as `T-235`.

**What measuring found that remembering had not.** The checker had reported zero failures over three
rounds, and the census showed why: it was scoring the wrong pairs. `--accent` appears in 189
declarations and `--sheet` in 163, while several tokens the table did cover appear in none. Under
that, one visual idea had shipped as 22 ratios — 22 rules each hand-mixing a state tint out of an
ink and whatever paper the component sat on, the worst at 3.63 against a 4.5 floor. They became
`--ok-soft`/`--warn-soft`/`--danger-soft` on `--asoft`'s formula, and the state inks were
re-derived against the wash they are painted on rather than against bare paper (ADR-145) — which
also caught `--t3`, barred by ADR-074 from carrying content, as the only label of a button, and
`--alt` used as ink on `--sheet` at 1.06:1. Then the blind spot behind the census itself: a colour
bound with `[style.background]` never enters a stylesheet at all. Two components hashed an identity
into a six-colour array declared in their own `.ts` — the editor's channel list with a white ink
failing on three of the six (worst 2.26), the admin user list with a cream one failing on all six,
and one of the same fills shipped on the public blog's channel avatar at 2.92. The array became
`--avatar-1…6` plus `--avatar-ink` behind one hasher, hue kept and luminance moved until white
clears (ADR-146). Page roots and the server-rendered surfaces moved off the wall onto paper on the
way, and the five Appearance accent presets were re-derived as day/night pairs.

**Stage 2 — the primitives, and the page that proves them.** Ten components under
`cedarclerk-web/src/app/bench/`, in subfolders mirroring the design system one to one: `app-button`
and `app-input` in `forms/`, `app-stamp-badge`, `app-resin-drop`, `app-leaf-tag`, `app-paper-card`
and `app-task-tag` in `display/`, `app-spec-row` on the worktop, and `app-brass-pin` /
`app-brass-hook` in `scenery/`. The mirror is React and the app is Angular, so it was translated
rather than copied — children became `ng-content`, named slots `ng-content select`, handlers
`output()`, the rest `input()` signals — and the mirror's two escape hatches were dropped on the
way: the `style` prop, which would have re-seeded exactly the one-off values ADR-071 exists to
remove, and the `cb-` class names `Button.jsx` and `Input.jsx` inject into a global style element
at import, which became ordinary encapsulated component styles. `app-input` implements
`ControlValueAccessor`, because twenty templates bind `[(ngModel)]` and a plain `input()` signal
cannot serve them. Every component that is a surface spells `data-surface` (ADR-138); the two brass
pieces spell neither, hardware being something that sits *on* a surface rather than something that
is one. `/dev/styleguide` was rewritten as the kit's only call site, and its spec asserts the rule
the rewrite exists for — the page draws no control of its own, so what is on display is one
vocabulary instead of two.

**The colour the primitives cost.** They paint materials nothing had measured: leaf stock, the rail
as a button face, a wood plaque, a resin chip, and `--paper-bright` as a surface carrying a field's
ink rather than as a highlight. The census found one real failure among them — the brass stamp put
`--brass-lo` on a tint of itself, 3.87 by day and 3.42 at night, and the tint was a percentage
written in the rule, which is the twenty-third instance of the pattern ADR-145 was written to end.
It became `--brass-soft`, a token on the same formula as the other three washes, and the ink was
re-derived against it — day keeping its hue and taking the smallest step that clears, night
stepping down (`#8A6226`→`#7B5822`, `#7E5A20`→`#684A1A`), both still lighter than `--brass-edge`,
so the brass ramp keeps its order. `--paper-bright` joined the paper family rather than being
special-cased, which hands it the whole ink set, the hover washes and the five accent presets at
once. Fourteen pairs were added to `tools/check-contrast.mjs`, each ramp scored at both stops
rather than as one name; the census now reports **no** bench-sourced combination it cannot account
for, and the pair table is green with the six accepted exceptions it already carried.

Stages 3 through 6 — the chrome components and the shell, the three reference screens, the
remaining pages by pattern, cleanup and ship — are `T-216`…`T-234` on the board, none started, plus
`T-235`. `npm run check:density` stands at six failures, every one of them the unscoped
`@media (pointer: coarse)` rule `T-219` exists to carve out; it has been red since Stage 0 and no
bench component adds to it. Six decisions wait on Marty as `Q-19`…`Q-24`: whether `/stats` becomes
a route again, the shape of the stats filters, one editor tool strip against the configurable two
rows, where the theme and Appearance controls live in the new rail, responsive behaviour the design
system does not specify at all, and whether the accent picker survives now that pine is meant to be
the one colour that acts.

## 2026-08-19 — "do everything P1" (v0.12.2)

Marty's one-line directive, plus five decisions answered in the same message: T-164 (PRGE trip)
deleted from the board outright, Q-17 closed — the product stays **Cedar Clerk** with the focus in
a subtitle, Q-18 (NSFW) deleted, the T-172 quota figures confirmed, and T-147's R2 keys revealed to
have been live for a week.

The infrastructure half: storage quotas cut to what the disk can honor — Free 100 MB / Pro 1 GB /
Pro Plus 3 GB (ADR-129, one edit in `PlanLimitations`, every doc that quoted the old numbers
updated); the off-box backup verified against the actual R2 bucket (daily `cedar-*.db.gz` rows,
935 MB of media, both healthcheck URLs present, the installed script byte-identical to the repo
copy) and T-147 closed; and the first-ever backup restore performed (T-149) — downloaded, gunzipped,
integrity-checked, then served by a real local server: the blog rendered posts from the restored
copy and the three pending 0.12.1 migrations applied cleanly over production data, which doubles as
the pre-deploy migration rehearsal. T-175 landed as ADR-130: image metadata (EXIF/GPS, XMP, IPTC)
stripped losslessly at byte level on every media write path — upload, `.cedar` import, Markdown
import — and the two ImageSharp re-encode paths (Telegram derivative, asset thumbnails) stopped
copying source EXIF into their output. Old files already on the droplet are T-202.

The growth half — all five anchors from the competitor analysis, each with its ADR:

- **Discord** became the fourth publish network (T-161, ADR-131): a channel webhook pasted whole in
  Settings → Integrations, verified against Discord before being stored encrypted; the message is
  the ADR-077 short post whose blog link unfurls from the OG tags, `allowed_mentions` disarmed, no
  threads by design. Third micro-network in the export modal, brand icon and all.
- **Sprint → devlog draft** (T-158, ADR-132): the button on the planner card that assembles a
  sprint's finished tasks, its releases and its leftovers into a post skeleton — material, not
  prose; the story stays the author's. The changelog generator's JSON helpers became the shared
  `DocJson` on the way.
- **Onboarding** (T-160, ADR-133): starter documents are born with per-type skeletons in the UI
  language (GDD sections, jam plan, hypothesis note, changelog scaffold), and the empty `/projects`
  screen offers "Cedar Quest" — a filled example project (board, current sprint, released 0.1.0,
  a written devlog) created on demand, never seeded silently.
- **Public project showcase** (T-159, ADR-134): `/games/{slug}` on the blog host — cover, store
  links, the devlog feed under the blog index's exact visibility rule, and a roadmap of tasks the
  owner explicitly ticked (title + status only). Opt-in at every level; migration
  `AddProjectShowcase`.
- **The landing reworked** (T-154, ADR-135): devlog-first ("Build your game. Grow your audience.
  One tool."), English by default with Russian only on request, and the primary CTA is now a
  waitlist — `WaitlistEntry` + `POST /api/waitlist` with a honeypot and case-insensitive dedupe —
  because while registration is invite-only, "Create an account" leads to a wall. Verified live:
  EN/RU rendering, signup, dedupe, invalid address, honeypot.

Seven ADRs (129–135), three migrations, 1010 backend tests, frontend units and the contrast
contract all green. Remaining P1s on the board are the blocked/decision/device kind (T-088/Q-15,
T-034 device pass, T-036/Q-10, T-076 mockups, T-152 model revision, T-187/T-188 process rows).

## 2026-08-18 — the task board goes canonical (and English)

`/task-format all`, on Marty's word, plus two new standing rules from the same message.
The three task files became canonical Cowtext-board checklist lines — `- [ ] T-xxx Name —
description #tags P1..P3` — and English: TASKS.md got its Now / Waiting on Marty / Live
verification lanes (23 lines); BACKLOG's Russian tables became 92 checklist lines with
translated descriptions, every T-ref/ADR-ref/date preserved, priorities mapped High/Medium/Low →
P1/P2/P3, and the parser traps defused (a bare `P0` or `#word` in a description becomes a
priority/tag — "idea #5" is now "idea 5"). Dropped as already-recorded elsewhere: the retired
T-137 row and the answered Q-1/Q-13/Q-14 (their answers live in ROADMAP §Phase 12 / ADR-077 /
PRODUCT). The Questions and Deferred tables deliberately keep non-name headers so they stay
invisible to the board — they are not tasks. ROADMAP was only de-Russified; its history lines
were left byte-for-byte. T-173 is the one `[?]` — genuinely in testing.

The two rules, now in CLAUDE.md: **repository files are written in English** (chat stays
Russian), and **never write what changed and when** in comments or doc prose — Marty had been
deleting those notes himself (the ui-inventory hook header among them); history belongs to git
and this file. The freshest offenders were swept the same commit.

## 2026-08-18 — Cowtext skills adopted, TASKS.md moves in with its siblings

A survey of the Cowtext project's seven skills and two scripts, with Marty's ask to
borrow what fits. Borrowed and committed: **task-format** and **ultracode** (the
name-swapped copies already sitting untracked were adapted for real — task-format
now names the truth that the board is the Cowtext app watching this repo and that
all three task files live in `docs/tasks/`; ultracode lost the `tech-barn` lane,
gained Cedar's own lane cut (C# vs Angular/blog), points its record-keeping at
CHANGELOG/board/TERMINOLOGY instead of Cowtext's Status line, and promises never to
commit the Cowtext-managed `.claude/agents/`). Borrowed as *patterns* and written
fresh: **cedar-terminology** (module map + the canon terms — Blocks-is-canon,
DocumentLink vs EntityLink, semi-public, the silent-drop rule for new nodes — over
`docs/knowledge_base/TERMINOLOGY.md`) and **design-tokens** (the seven ADR-071 laws,
token families, the two style systems, the icon-map pipeline, the inventory-first
law). Skipped with reasons: art-direction/sound-design (pixel art and SFX — no such
surfaces here), manual-format (no test-manual practice; live-verify lives in
TASKS.md), `gen_sfx.py`/`gen_sprites.py` (game asset generators).

And `TASKS.md` left the repo root for **`docs/tasks/TASKS.md`** — Marty's call,
reversing the 18.08 morning decision to keep it in the root. BACKLOG and ROADMAP
were already there; the Cowtext board searches root → docs/ → docs/tasks/, so it
still finds everything — which is also why a stray root copy must never reappear
(it would shadow the real file). DOCS-FLOW (node, prose, placement row), CLAUDE.md
and AGENTS.md updated; the file gained its ADR-123 front-matter on the way in;
`DocsFlowGraphTests` green.

## 2026-08-18 — six board rows in one run (0.12.1)

Marty picked six tasks off the board — `T-186, T-174, T-178, T-193, T-177, T-181` — and the session
closed all six, five ADRs and fifteen commits deep, in the order that kept `BlogEndpoints.cs` warm:
blog fixes first, the L-sized tree/wiki-links last.

**T-186** (the bug): index cards wrote `RU` as every post's primary language; now the real
`PrimaryLanguage` leads the chip and the page's own `lang` follows the request. **T-174 / ADR-124**:
full OG/Twitter/canonical/hreflang meta on post pages and the index, built by a pure
`OgMetaBuilder` in Core — with a privacy policy that reader auth never widens: private posts emit
*zero* meta (the gate and the private-404 answer with the same semi-public-only helper, found
mid-build — crawlers never reach the render path), listed-private gets title+static fallback only,
public gets everything; a generated 1200×630 `/og-default.png` is the fallback card. **T-178 /
ADR-125**: series are an entity, not a tag — `/series/{slug}` landing, «Часть N из M» line and
prev/next on member posts, numbering computed over *visible* members only so a hidden part never
shifts a stranger's numbering; management mirrors folders end to end (endpoints, service latch,
picker in the editor strip; rename keeps the slug). **T-193 / ADR-126**: `docs/product/METRICS.md`
— the event dictionary that must exist before T-153 picks an analytics provider: what the database
already records (and is minable retroactively — activation is a join of `CreatedAt` ×
first-publish, no event needed), what does not exist until a provider ships, and the stable
`snake_case` names the provider will transport. **T-177 / ADR-127**: the media library — a `/library`
page (grid/list, type chips, search, quota bar; not `/media`, whose prefix the uploaded files
themselves own — the first e2e run caught the dev proxy swallowing the route), delete by on-demand scan over every document *and
every translation* (409 lists the referencing posts; the same sweep fixed the export-modal list
that only scanned the first translation), insert-from-library via a lightweight picker behind a new
toolbar button, and paste/drag&drop upload straight into the sheet — reusing
`CedarPackage.FindReferencedMediaPaths` as the one truth of «what references what». **T-181 /
ADR-128**, both halves: the document tree (`ParentDraftId`+`SiblingOrder`, cycle-guard, depth ≤10,
delete lifts children to the grandparent; a third `tree` view on /drafts with move-menu and ↑/↓;
breadcrumbs in the editor) and `[[`-wiki-links (`@tiptap/suggestion@3.27.2` verified multi-char,
inline atom `{draftId, label}`, `DocumentLink` diff-synced on every primary-language save,
backlinks chip «← N») — rendered as a link on the blog only when the target passes the index
visibility rule, as an escaped plain word everywhere else, with the label taught to
`PublishValidator`, `TipTapTextNodes` and `CedarPlainText` so teasers stop eating it silently.

Three migrations (`AddSeries`, `AddDocumentTree`, `AddDocumentLinks`), 964 backend tests green
(+13), 18 frontend. Deferred honestly to the board: media folders/tags/dedup (`T-200`), tree
drag&drop (`T-201`). Live-verify checklist in TASKS.md grew seven entries — OG previews through
Telegram's cache, `[[` on a Russian layout, the 409 titles, the series numbering with a hidden
part.

## 2026-08-18 — the map gets a guard, the words get a dictionary

Marty pasted an external DOCS-FLOW analysis — six P0 gaps, five P1 docs, structural scheme
complaints, three waves of agents — with the ultracode keyword. The evening went to verifying it
before obeying it, and the verification earned its keep: a 12-agent workflow checked every factual
claim against the repo with file:line evidence. Four claims **refuted** (the 0.10.8/0.10.9 «tag
divergence» — both are ancestors of master; «release process undocumented» — it lives in CLAUDE.md,
ARCHITECTURE, five ADRs and GitGuard's own tests, so RELEASE.md is rejected and the one real gap —
preflight not checking backup freshness — became `T-195`; «T-052 hard blocker» — closed five days
ago; «Clerk.com/AWS Cedar conflicts surfaced» — zero trace in the repo). The famous «~130
untranslated strings» is really **48** interpolated ones — plain literals have been guarded by a
red-capable test since 01.08 — and the analysis' own best idea was confirmed everywhere it counted:
no QA doc, no threat model, an empty knowledge base, and a scheme rule enforced by nothing.

So the rule got teeth: **`DocsFlowGraphTests`** now fails the build when a live doc is missing from
the DOCS-FLOW map or the map names a path that does not exist — proven red with an orphan file
first, the `SchemaDriftGuardTests` pattern applied to the docs themselves. **ADR-123** put
four-field front-matter (`owner`, `last_verified`, `source_of_truth_for`, `guard`) on all 17 living
docs — freshness readable without git log. The mermaid got its **edge legend** (truth-flow vs hard
gate vs reading-order, the three styles that used to read identically), dotted reading-order edges,
and the two nodes the analysis rightly missed: `production-environment.md` («истина о проде», named
so in prose and absent from the scheme — our own rule violated) and Terms+Privacy with the lawyer
gate. **`knowledge_base/TERMINOLOGY.md`** seeded the empty category: ~80 project terms in five
domains, each extracted from real code by parallel agents and merged — Draft to fingerprint,
ShrinkGuard to StageBoard. Two live contradictions found by the verifiers were fixed on the spot
(BUSINESS §4 vs T-153 on whether activation is computable today; a stale T-052 comment in
`AuthEndpoints.cs`).

The rest became **13 board rows** (`T-187…T-199`) rather than files: QA.md with its
verification-planner (High — the checklist's 8 entries / 19 items live in the fastest-rotting file
by the docs' own admission), Wave-1 agents (High, but **deliberately not created today** — the
analysis' own warning about nine drifting agent-files is taken at face value: one at a time, kill
the unused), FEEDBACK / COMPETITORS-import / event dictionary / threat model (Medium), and the Low
tail. 937 tests green, two of them new.

## 2026-08-18 — docs/ gets rooms

Marty's taxonomy, third docs session of the day: the root of `docs/` keeps only the high-level
files — `DOCS-FLOW.md` (the map), `DECISIONS.md` (the ADR index, whose path stays put because ~40
files, half of them code comments, point at it) and the untracked `INPUT_PROMPT.md` (his explicit
call: root) — and everything else moved into category folders:

- **`product/`** — PRODUCT, PRD, BUSINESS, MULTITENANCY, INDIEDEV (the module's product reference)
- **`tasks/`** — BACKLOG, ROADMAP (`TASKS.md` stays at the repo root, outside docs/)
- **`design/`** — DESIGN, UI-INVENTORY, indiedev-design-prompt
- **`tech/`** — ARCHITECTURE, DESKTOP
- **`adr/`** — stayed its own category rather than moving under `tech/`: ADRs are product decisions
  as often as technical ones (ADR-092 credits pricing, ADR-101 module-not-fork)
- **`knowledge_base/`** — STACK (Marty's own definition of the category names «технологии и стек»)
- **`fleet/`** — agent orchestration (Cowtext/FleetView); a README stub for now — agent definitions
  live in `.claude/agents/`, this folder is for docs about how the fleet works
- **`for_user/`** and **`archive/`** — as created earlier today; **`misc/`** appears with its first
  file rather than sitting empty

Thirteen files moved; every live reference followed them in the same commit — the sweep covered
CLAUDE.md/AGENTS.md/TASKS.md, the rules, the DOCS-FLOW scheme (all mermaid nodes re-pathed), code
comments, and three **functional** path consumers that a docs move could silently break:
`UiInventoryDriftTests` (builds the inventory path from components — went red on the first test run,
which is exactly the guard doing its job, then fixed), the `/tasks` skill's PowerShell script (held
the path with backslashes, so the forward-slash sweep missed it — caught by `git grep`, fixed, then
run to prove it), and the UI-inventory hook. Historical text — CHANGELOG, `docs/adr/*`,
`docs/archive/*` — keeps its old paths on purpose: those record what was true then. The taxonomy
itself, including the placement rule for future files, lives in `DOCS-FLOW.md` §Размещение, and the
scheme now carries every category. 935 tests green after the move.

**And the old inbox is retired.** Marty deleted the `_Documents_/CedarClerk/Input.md` row from
DOCS-FLOW's sources table by hand and asked for the rest: `docs/INPUT_PROMPT.md` is the **only**
inbox now. Every live doc was cleaned of Input.md as a living source — the mermaid node and its two
edges, the weak-spots bullet, CLAUDE.md's "how an item travels" line, the `.gitignore` comment —
while history (this file, the ADRs, the archive, ROADMAP's July entries) keeps its mentions as a
record of when Input.md was real. The two survivors in live files are deliberate: backlog `Q-9`
still explains that its spec was lost *to* an Input.md rewrite, now marked retired.

**Local paths left the documentation too.** Two of Marty's follow-ups: no absolute paths or drive
letters (three spots — the secrets rule's real folder layout, and a fictional `D:\Projects\MyGame`
example in DESKTOP.md/ADR-107 that became a drive-less `MyGame/Assets`), and then no `_Documents_`
folder paths at all (eleven spots): out-of-repo briefs, design handoffs and notes are now referenced
descriptively — "Marty's out-of-repo brief `Gamedev_Focused_Rework.md`" — with the location being
Marty's knowledge, not the documentation's. `%APPDATA%\…` and droplet paths stay (portable and
operational respectively); history keeps its old paths as a record; code test fixtures with `D:\`
are path-parsing test data, not documentation. The rule is written into `DOCS-FLOW.md` §Размещение.

## 2026-08-18 — the documentation gets a floor plan

The read-only audit (`docs/archive/AUDIT-DOCS-2026-08.md`) came back with five questions; Marty
answered all five, and the answers were executed the same evening. The audit's premise survived
contact with reality only partially — Q-1 turned out closed since 10.08, the "diverged tags" were
ancestors of master all along, and the four positioning files had already been reconciled by the
morning sweep — so what ran was the verified plan, not the original hypothesis.

**`docs/INPUT_PROMPT.md` is untracked again**, hours after being committed for the first time.
Marty's ruling: «это просто динамический файл с промптами… коммитить не надо». It is gitignored
now; a rewrite is detected by mtime against the last "Input sweep" entry in ROADMAP — exactly the
`Input.md` model, and DOCS-FLOW says so instead of praising git history it no longer has.

**The archive lives in the repo: `docs/archive/`.** Three residents: the DigitalOcean migration
journal (executed 11.08, live truth is `production-environment.md`), the audit itself, and the big
one — **Phases 0–10 cut whole out of `ROADMAP.md`** (371 lines, ~72 KB, 56% of the file; ROADMAP
went 128 KB → 52 KB). The live file keeps a one-line stub per archived phase; the one never-started
phase (7) keeps its open bullets in PRD. The cut honoured the audit's warning that the file is not
chronological — the boundary is lines, not phase numbers.

**`PRD.md` slimmed 12.7 KB → 4.7 KB** — Marty's call, earned by the file's drift record (statuses
corrected twice in eight days). What survives is what no other file holds: requirement-level
invariants, explicit non-requirements, the blocked list, Phase 7. Shipped enumerations now have one
owner: ROADMAP.

**`DECISIONS.md` split: 122 ADR files + an index.** `docs/adr/ADR-001.md … ADR-122.md`, plus
`ownership-audit.md` for the non-ADR table that lived between ADR-017 and ADR-018. The split is a
script over `^### ADR-\d+` with a safety net: reassembling the pieces reproduces the pre-split file
**byte-identically** (verified before anything was replaced). `DECISIONS.md` stays as the index and
front door, because ~40 files reference it by path — half of them code comments. New rule wording
in CLAUDE.md/AGENTS.md/DOCS-FLOW: a new decision = a new `docs/adr/` file + an index row, still
before the code.

**`docs/for_user/` is born** — Marty's placement rule: «все инструкции, мануалы и прочее, что важно
пользователю» live there. First resident: `integrations-setup.md` (the provider-key runbook); eight
live references updated, DECISIONS/CHANGELOG left alone as history. DOCS-FLOW gained a "Размещение
файлов" section carrying both rules, and the four reference docs its scheme never knew (STACK,
BUSINESS, MULTITENANCY, the runbook) are on the map now.

The audit's five live contradictions all fixed: CLAUDE.md's docs-map rows still describing the
desktop as a sidecar and the design prompt as a verbatim token copy; STACK's "931 тест" (a number
that had already rotted — replaced with "the count lives in `cedar test`"); UI-INVENTORY's AI
popover cell describing the pre-ADR-038 world; and the `/drafts` section's missing sort/resize row
— its bare `:NN` line anchors replaced with selectors, which the audit's own 10-row sample showed
are the anchors that do not rot. The merge hypothesis (PRODUCT+PRD+BUSINESS+INDIEDEV → one file)
was dropped on Marty's confirmation. 935 tests green after the shuffle.

## 2026-08-18 — the inbox sweep, and the docs stop lying

`docs/INPUT_PROMPT.md` appeared — an in-repo inbox («considered as a new prompt every time»), committed from now on so its rewrites live in git history, which `Input.md` never had. Its first sweep processed three embedded documents; the full disposition is ROADMAP's new "Input sweep" section. The 29.07 **Big Feature Scope** (~100 features) was triaged against the code: a good third already shipped, another third already on the board, ten rows genuinely new and worth tracking (`T-174…T-183` + `Q-18` NSFW), the rest deliberately not taken — the расфокус rule from the competitor analysis stands. The indie-dev brief turned out to be the same text that spawned Phase 13 — fully absorbed weeks ago, nothing to do. The **Visual Polish session brief** survives as three rows: its stop-gates describe a repository that no longer exists (tags 0.10.8/0.10.9, 442 tests), but its Task A is painfully real — **blog post pages emit no OG tags at all** (`T-174`, High: every post link shared to Telegram/X/Discord renders bare, which breaks acquisition channel №1), and photos go out with EXIF/GPS intact (`T-175`, High). Motion tokens it asked for already shipped in Phase 11 under other names; skeletons and view transitions became `T-184`/`T-185` (Low).

The bigger half of the day: three read-everything audits of `/docs`, then making the documentation match the code. What the docs got wrong, now fixed: **nine** content languages, not six; **Stripe proven with real money since 26.07**, not "waiting on keys"; Phase 13 listed as "open" in PRD with the desktop still described as a local sidecar; `integrations-setup.md` §2 carried **five invented config keys** (`Cedar__Translate__AnthropicApiKey` where the code reads `Cedar__Anthropic__ApiKey`, and so on) — following it verbatim leaves translation dead at 501 — plus an Opus default where the code defaults to Haiku; its UptimeRobot section was a to-do list for work Marty finished on the 13th; `DESIGN.md`'s `--fs-*`/`--icon-*` values were one step behind `styles.scss` since 01.08 — and the design prompt had copied exactly those stale values, so it now points at `styles.scss` instead of carrying a copy; `MULTITENANCY.md` recommended two different quota sets 74 lines apart.

Board hygiene: `T-109`/`T-110`/`T-111` (credit wallet, X connector, threads) had shipped weeks ago and still sat open — removed; duplicate ID `T-143` renumbered to `T-173`; `T-107`/`T-108` moved out of the questions table; the `T-018`/`T-060` remainders were pure live-verification and moved to `TASKS.md` — which was rewritten down to its actual short horizon from ~560 lines of July history. `docs/admin-panel-scope.md` deleted into **ADR-122** (its two living decisions — append-only audit log without retention, and the admin gate having no automated test — moved to DECISIONS). `UI-INVENTORY.md` gained its two missing screens (`projects`, `project`), closing a gap it had honestly recorded about itself.

Git hygiene on the way: `LIVE`/`LIVE-PREV` had leaked to origin **again** — deleted, rule reinforced in CLAUDE.md; `indiedev_module` is confirmed merged-and-deleted and every doc that called it a live branch updated. One real bug found while verifying: blog index cards hardcode "RU" as the primary-language label against ADR-064/065 (`T-186`). 935 tests green (808 + 127 CLI).

## 2026-08-18 — the workshop in the forest, and 0.12.0

Marty's hypothesis — «а что если Cedar Clerk выглядел бы совсем иначе» — went the whole way in one day: a design prompt, a Claude Design project ("Forest Workshop": nine screens, a materials sheet, ready-made SVG parts), and then the design implemented as a real, shipping **skin**. `v0.12.0` is on the droplet with it.

**The skin is a second styling axis, not a theme fork** (ADR-120): `data-skin="forest"` on `<html>`, orthogonal to `data-theme` — "workshop by day / at night" is the existing light/dark toggle wearing wood. The default skin carries no attribute at all, so it cannot change by a pixel; everything lives in one scoped partial (`_forest.scss`, ~1000 lines) plus a `Skin` toggle in the Appearance panel. What made it in: wood-rail chrome, park-sign buttons with brass inlay, pine-cone checkboxes, leaf tags, rubber-stamp statuses, carpenter's-rule scrollbars, the brass ruler under the editor, resin autosave, and the login page as a door into a misty pine forest. Night keeps paper cream and darkens only the wood — which is why the night block re-asserts every token the base dark theme flips. Found on the way and worth remembering: a header comment containing `:root[data-theme="dark"]` silently broke `check-contrast.mjs`'s block anchoring (the dark palette stopped being validated at all), and a design band SVG stretched with `slice` on an ultrawide crops to the bottom sliver — it is a seamless `repeat-x` tile now. Sounds stayed annotations, as the design itself insists.

**`cedar run`** (ADR-121) closes the local-check gap: build front and back, serve `publish/` — the artifact a deploy ships, not the dev servers — on `localhost:8080` against the dev database, open the browser, Ctrl+C stops it. The bot is forced off with a single-space `Cedar__BotToken` (an empty string would *delete* the variable on Windows and let an exported token through to a 409). A port that already answers gets a refusal naming its owner, never a kill.

**The first deploy of 0.12.0 taught the pipeline a lesson**: a `cedar run` left running holds `publish/`, and the build's `Directory.Delete` surfaced as a bare `Access to the path 'Anthropic.dll' is denied`. The pipeline now says what actually happened — publish/ is locked, a server is probably still running from it, stop `cedar run` and retry — with a test pinning the wording. Also learned: version tags live on GitHub too, so retagging a moved `0.12.0` needs `git push --force origin 0.12.0`.

Marty's competitor report («Cedar Clerk против всех» — Codecks, HacknPlan, Anchorpoint, IndieViral) was read against the code, the backlog and the live server, and turned into a prioritised plan. The report's central claim held up under verification: all four competitors look *inward* at production, none turns the work into publishable content — and that outward half is the part Cedar Clerk already runs in production.

**Fifteen backlog rows** (`T-158…T-172`). The ones that matter: the bridge from closed sprint/tasks/build to a devlog draft (`T-158` — the analysis' feature №1, cheap because `EntityLink` and the changelog generator already exist), the public project page on the blog domain (`T-159` — every free page advertises the service, and the "Published via Cedar Clerk" footer already ships per ADR-034), onboarding templates with a seeded project (`T-160` — activation is metric №1 and an empty screen kills it), Discord webhook publishing (`T-161` — days of work, and the main home of the audience), and quotas-vs-disk (`T-172` — promoted from `MULTITENANCY.md` §1 into a tracked registration blocker). Plus PRGE 2026 prep with a hard September deadline (`T-164` — go as a visitor with a laptop, not a booth), SEO comparison articles (`T-163`), paid-feature visibility in the UI (`T-165`), and three research rows (IGDB autofill, conditional first comment for Telegram, a game-events database).

**Three doc-vs-reality drifts found while verifying.** `T-154` claimed the site root greets strangers with a login form — the server-rendered landing has existed since `T-009` and answers live; the row now describes the real gap: RU-first generic-publisher positioning where an EN-first devlog-first pitch with a waitlist should be. `T-148` (external monitoring) had been done by Marty on 13.08 and was still open on the board — closed, status page verified live. And `docs/PRODUCT.md` promised 1/5GB storage while `PlanLimitations` grants 8/16 — corrected to the code, which is also exactly why `T-172` blocks registration.

**What was deliberately *not* added**, per the report's own warning about расфокус: time tracking, engine SDKs, a public API, Vision boards — the matrix of competitor features is a temptation, and the winning vertical is «написал → опубликовал везде → аудитория растёт».

## 2026-08-13 — the stack, the tenants, and nine rows in the backlog

Marty's answers to yesterday's business file turned into two more documents and a batch of backlog rows. One number came out of writing them that changes what "ready for users" means.

**Pro promises 8 GB of storage per account. The droplet has 40 GB free in total.** Five paying users, each filling their quota, take the server down — and that is before the deploy needs room for a second copy of the app and the backups need room for fourteen days of database. The quotas were set from generosity rather than from hardware, and `docs/MULTITENANCY.md` says what to do about it: cut them to something the machine can honour (one edit), then move media off the disk entirely into object storage, which is the only change that stops the disk being the ceiling. Do it **before** opening registration — migrating someone else's files is worse than migrating your own.

That document also answers the questions behind the question. Where tenant blogs live: subdomains on a dedicated domain, already decided in ADR-020, with the wildcard certificate and host-routing work that decision implies. What "self-hosted" would actually cost: a public repository, an installable image, and migrations that survive on a database nobody watches — in that order, with Docker first for our own sake (`T-151`) and the licence question last, where AGPL and MIT lead to genuinely different futures.

**`docs/STACK.md`** lists every dependency and service with what it costs and what breaks when it goes down. Fixed costs come to roughly $15/month; everything dangerous is variable, and the dangerous line is Anthropic. A smaller finding worth acting on eventually: Stripe's fee on a $3 subscription is about 13% of it, which is an argument for annual billing — one fee instead of twelve.

**Nine backlog rows** from Marty's list: restore-testing (`T-149`), environments (`T-150`), Docker (`T-151`), the monetisation rethink with all AI on credits (`T-152`), analytics (`T-153`), a landing page (`T-154`), a Miro-style board (`T-155`), a "game engine" redesign experiment (`T-156`), and a second security key (`T-157`). Two of them are notes rather than tasks: `T-155` probably absorbs the existing References board (`T-129`) rather than sitting beside it, and `T-157` exists because moving every account onto one YubiKey is a real improvement that also makes one object the single path to DigitalOcean, Cloudflare, Stripe and email.

Also updated: Stripe is now ticked as proven with real money on the launch checklist, and the blog footer's Status link points at `stats.uptimerobot.com` — a custom domain there is a paid feature, and the DNS record can wait for the plan.

## 2026-08-13 — the money side gets written down

Marty asked for the business to be tracked like the code is. It went into `docs/BUSINESS.md` rather than into memory, for the reason today keeps demonstrating: a checklist that references code and infrastructure drifts the moment either moves, and the repo is the only copy that moves with them.

Three findings came out of writing it, none of which were the point of the exercise.

**The restore has never been tested.** There are now three kinds of backup — nightly database, off-site copy, weekly machine image — and not one of them has been restored. A backup nobody has restored is a hope with a cron entry, and the check takes ten minutes.

**No real money has ever gone through.** All three payment providers are code-complete and none has processed a live payment. The first one should be Marty's own card on the $1 trial, because the first customer will not have access to the logs and will simply leave.

**Pro Plus may not be profitable, and it is arithmetic, not opinion.** $6/mo includes auto-translate — the most expensive call in the app, a whole document in one request — at up to 20 AI calls a day, which is 600 a month against $6 minus Stripe's cut. The file does not guess at the number; it says to read the real Anthropic bill and compare, and names the three ways out if it does not clear.

The rest is what a solo operator actually needs and no more: the ten gates before public registration opens (registration is closed today on purpose), the tax topics that get decided once and belong to an accountant rather than to a search engine, four metrics instead of twenty, weekly/monthly/quarterly checks tied to commands that exist, and where indie developers can actually be found. Plus the dangerous places already visible — chief among them that every key, backup and access path currently runs through one person and one laptop.

## 2026-08-13 — the blog footer grows up, and the legal pages stop being drafts

**The footer is three groups on one line now** — brand, links, badge — where it was a centred line with the DigitalOcean badge centred underneath it. Marty's ask was "not in the middle", and the reason it matters is what the position says: a fixed white plate in the centre of a footer reads as an advert placed there; the same plate at the edge reads as a credit. It is also dimmed to `opacity: .72` (full on hover), because it ships as white and was otherwise the brightest thing on a dark page — brighter than the post title. Below 700px the three groups stack and re-centre: a row pushed to the edges looks ragged once it becomes a column.

The links are **Terms · Privacy · Status**. Terms and Privacy point at the app host rather than a copy served by the blog — one legal page, two hosts. Status points off-box on purpose: it is the page a reader needs exactly when the blog cannot answer, so hosting it ourselves would defeat it.

**`T-052` is closed: every `[BRACKETED]` blank in Terms and Privacy is filled**, and the "draft template" banner is gone with them. It had been open since 31.07 because Marty did not know what to put in the blanks, which was the right instinct — most of them are decisions, not text. The decisions, from him: operator is Viacheslav Chudaev trading as Moo.exe, an individual in Oregon; jurisdiction Oregon, USA; contact `cedarworks@mooexe.dev`; minimum age 16; cancel any time with no refund of the unused period.

The age question deserved its answer rather than a number: **13 is the COPPA line** (below it, US law demands verifiable parental consent and much more), **16 is the GDPR line** (below it, consent must come from a parent for EU users), and 18 is where sites go when they want a clean contract with an adult. A public blog read from Europe makes 16 the quiet choice.

Everything else came out of the code and the infrastructure rather than out of a template: the active payment processors, Anthropic as the translation provider, `.cedar` and Markdown as the export mechanisms, and the **backup retention window stated in real numbers** — 14 days locally, 30 days off-site, four weeks of whole-machine images — which is only sayable at all because `T-071` and `T-147` happened first. A GDPR/CCPA rights paragraph was added. What is still true and now written down in `TASKS.md`: **no lawyer has read any of it**.

## 0.11.1 — 2026-08-13 — the blog answers HEAD, and the deploy stops hanging

**The first `cedar deploy` of the day hung on Upload and stayed there.** It looked like a slow transfer — 1m32s against 24.4 MB — and it was not a transfer at all: `staging/` on the droplet was empty, no `cat >>` process existed there, and the local `ssh` had burned **0.00 seconds of CPU** since starting. Nothing was moving in either direction.

The stuck command turned out to be `stat -c %s …/cedar-0.11.1.tar.gz` — the size probe that runs *before* the upload to find the resume offset. On the server that command had already finished and left no process; what survived was an `sshd: martycow@notty` session four minutes old with nothing running inside it.

**`ssh` was inheriting the console's stdin.** Without `-n` it forwards stdin to the far side and will not exit until that stream reaches EOF — which a console never does. Preflight survives this because nothing is competing for the console yet; the live progress panel is what makes the collision certain, so the hang lands on the first probe inside Upload, every time. `ConnectTimeout` cannot help: the connection is fine, it is the command that never returns.

Every remote command now passes `-n`. The one call that genuinely feeds ssh a file — the resumable upload — must not, and a test pins both halves, because `-n` on that path would send an empty file and report success. The same hazard one level down is closed too: local processes get a redirected stdin that is closed immediately, so a child that reads it gets an answer instead of waiting for one.

Nothing was half-deployed by the hang: production kept serving the old version throughout, and `staging/` was empty when it was interrupted. That is the shape ADR-113 was built for — everything slow happens before anything is switched.

The first monitor pointed at `blog.mooexe.dev` reported it down within minutes of being created, while the site opened normally in a browser. Both observations were correct: `curl` got 200, `curl -I` got 404.

`BlogEndpoints.HandleRequest` refused everything that was not a GET, and **UptimeRobot uses HEAD by default** — as does every other uptime service, most link checkers, and a fair number of crawlers deciding whether a page is worth fetching. A HEAD is a GET whose body is discarded, and Kestrel does the discarding, so the handler can render exactly as it would for a GET and let the server drop the bytes. Anything that is not a read still gets 404 rather than 405: a blog page is not a form, and saying "wrong method" invites a second guess.

What makes this worth a version rather than a line: a monitor that cries wolf on the day it is installed teaches you to ignore it, and the next alert would have been the real one. `BlogHeadRequestTests` pins it — verified red without the fix, in the same way `SchemaDriftGuardTests` was.

## 2026-08-13 — the backup leaves the droplet (T-147), and something starts watching

**The nightly backup script now lives in the repository.** It did not before: `~/bin/backup.sh` existed only on the droplet, unversioned, un-reviewed, and one `rm` away from being a thing nobody could reconstruct. `Scripts/server/backup.sh` is the source of truth; the copy that runs is installed by hand, because `cedar deploy` replaces the app directory and nothing else, and pretending otherwise would be worse than saying so.

**Off-box copying is wired but not switched on**, which is the honest state and not a half-measure — the half that needs an account and a key is Marty's, and the half that needs code is done. `rclone` v1.75 is installed at `~/bin/rclone` as a static binary, no sudo required. The script gained a second stage: `rclone copy` of the day's database into `db/` with a 30-day life there (longer than the 14 kept locally — the remote copy is precisely for when the local ones are gone), and `rclone sync` of `media/`.

Three decisions inside that are the whole substance of it.

**Cloudflare R2, not DigitalOcean Spaces.** Spaces is one checkbox and would have closed the "same disk" risk while leaving the one that actually ends a project: the copy would still live in the same DigitalOcean account as the droplet, where a billing lapse or a compromised login takes both. R2 is a different account, gives 10 GB free against our ~1 GB, and charges nothing for getting the data back — which is the exact moment you least want a bill.

**`--backup-dir`, not a bare `sync`.** A mirror propagates deletions, and to `sync` an accident, a rogue process and ransomware are indistinguishable from an author cleaning up. What sync would delete is moved into `media-removed/<date>` instead.

**Its own healthchecks check.** Sharing the database's check would let either failure silence the other; "the copy on the droplet failed" and "the copy off the droplet failed" want different reactions.

Two smaller things that would have bitten later. `rclone` is invoked by full path, because `~/bin` is not on cron's PATH — `command -v rclone` answers yes in an interactive shell and no at 03:30, which is the kind of difference that only shows up in a month-old log. And the healthchecks ping URL moved out of the script into `~/.config/cedar-backup.env` (mode 600): it is a secret in the sense that matters, since whoever holds it can silence the alarm.

**`T-148` opens: nothing watches production from outside.** The plan is written (`docs/integrations-setup.md` §6) and needs an account, so it is Marty's to run. One detail in it is worth repeating here: the API monitor checks for a keyword, not a 200. When the tunnel is down Cloudflare answers 200 with its own error page, so a status check that only reads the status code reports everything is fine at exactly the moment it is not.

## 2026-08-12 — the UI inventory gets teeth, and the comments get cut

Two of Marty's standing complaints, both about the same thing: things written down that nobody reads.

**Adding a UI element now goes through `docs/UI-INVENTORY.md`.** The complaint was concrete — X and Bluesky connection controls were built inside the Export modal and then rebuilt into Settings → Integrations (ADR-095), because nobody checked where the Telegram connection already lived. The inventory had described that panel the whole time. So the gap was never knowledge, it was that a 400-row document is only read by someone who already suspects it exists.

Three layers, because one would not hold. `.claude/rules/ui-changes.md` states the rule and the incident behind it. A `PreToolUse` hook fires on the first front-end edit of a session and puts the inventory's location into the context that is about to write the code — once per session, not per edit, since a reminder that repeats is a reminder that gets skimmed. And `UiInventoryDriftTests` fails `dotnet test` when a page component or a `sec-*` settings section exists with no mention in the inventory — the same shape as `SchemaDriftGuardTests`, which is the pattern that already works here: trust the test instead of remembering.

It went red on the first run, which is the point. `project-builds.component` — a whole screen — had shipped with no row at all, and six settings sections were described by name but not by the `id` the anchor nav jumps to. Both fixed; the guard is honest about what it cannot check, which is whether a row that exists is still *true*.

**Comments in `CedarClerk.Core` and `CedarClerk.Cli` were cut back.** Marty asked whether the volume was useful to Claude itself. It is not: a comment restating what the code does is read after the code and adds nothing, and it outlives the code it describes — `Logo.cs` carried a careful description of a subtitle that had changed months ago, which is also why a unit test was failing on `master`. What stays is a *why* that cannot be read off the code: an incident it prevents (npm's `%~dp0`, `scp` leaving production half-copied), an external constraint (X counts weighted units, Telegram collapses a post behind "Show more"), or a choice made against the obvious one (no `System.Formats.Tar`, no `CultureInfo` month names). Core went 18.9% → 13.5%, the CLI 13.2% → 11.9%, and the rule is written into `CLAUDE.md` so the next session does not re-inflate it. 925 tests still green.

**Commits lose their trailers.** `Co-Authored-By` and the `Claude-Session` link were a harness default, and `CLAUDE.md` had said "no body, no bullet list, no explanation" all along — a trailer is a body. Three or four words, nothing after them.

## 2026-08-12 — a state check: two tags, a dead hostname, and the backup that came back

Nothing here was planned work. It is what a "what is the state of git" question turned up, and the answer took four things with it.

**The working copy was sitting on `dev`, 28 commits behind `master`.** `dev` had no commits of its own — it was a stale pointer wholly contained in `master`, which meant every rule file, every document and `CLAUDE.md` itself were being read from before the CLI, the `LIVE` tag and the DigitalOcean move existed. That is the quiet failure mode of the branch rule: nothing is broken, nothing is dirty, and every fact in context is a month old. `dev` has been fast-forwarded onto `master`, and the first move of a session should be to look at which branch the files on disk belong to.

**`0.11.0` shipped without its tag.** The version bumped in `457d3f7` and twelve commits went on top of it; the last version tag was `0.10.9`. The tag is now on the commit that carries the bump, not on the tip — the rule is "bump the const and the tag together", so the tag belongs where the const changed. **And `LIVE` had been pushed to origin**, which `CLAUDE.md` explicitly says it never is. It is a local answer to "what is running here"; on a shared remote it is one machine's opinion presented as the project's. Deleted from origin, kept locally.

**`deploy.mooexe.dev` no longer resolves.** The droplet answers on `periwinkle.mooexe.dev` now, and the old name was still the default host compiled into `CliConsts`, plus seven documents. Anything still saying `deploy.` fails at DNS resolution rather than at login, which reads like a network problem instead of a stale name. `CHANGELOG.md` and `docs/DECISIONS.md` keep the old name on purpose: they record what was true then.

**`T-071` is closed, and `T-143` with it** — both by Marty on the server, which is the only place either could be done. The nightly `sqlite3 .backup` is back: fourteen dated copies, gzipped, with a healthchecks.io ping so that a *silent* failure raises an alert rather than nothing. Two things nearly made it a backup that exists only on paper. The cron line's `>> …/backups/backup.log` is opened by the shell before the script runs, so the script's own `mkdir -p` came too late — with no directory, cron would have failed to open the log and never started the script at all, every night, quietly. And the destination was `~/backups` while `cedar status` and `cedar backup verify` read `{RemoteDataDir}/backups`, so the tool reported "no local copy" over a directory that had one. Both point at `data/backups` now.

Moving it surfaced a second one immediately, which is the argument for verifying rather than declaring: `cedar` counted copies with `ls …/backups/*`, and cron writes `backup.log` into that same directory **after** the copy. From the first scheduled run onward the newest "backup" would have been a 0-byte log — a monitoring line that looks like a backup which ran and produced nothing. The glob is `cedar-*.db.gz` now, and the count went from a wrong 2 to a right 1 on a directory holding one copy and one log.

What is left of that theme is `T-147`: the nightly copy sits on the same disk as the database it copies, holds the database only — `media/` is 937 MB and rides in the weekly image alone — and that weekly image lives in the same DigitalOcean account as the droplet it protects.

**One red test, found by running them.** `LogoTests.The_subtitle_names_the_tool_and_says_it_is_a_console` asserted on `c e d a r` in the subtitle, which moved out of that line when the copyright moved in. It was failing on `master` — on the deployed commit — and would have blocked the next `cedar test` for reasons that had nothing to do with whatever change was being tested. The assertion now checks what the line actually says; the name is the wordmark drawn above it.

## 2026-08-12 — stage 2: the scripts became the wrapper (ADR-119)

ADR-118 ended on "stage 2 is a separate decision". This is it, with Marty's condition attached: everything must still build, test and deploy exactly as before.

**`Scripts/deploy.ps1`, `build.ps1`, `test.ps1` and `_git-guard.ps1` are gone.** The logic is `CedarClerk.Cli/Pipelines/`, and `cedar` is the only entrance.

That took two passes, and the second one is the interesting one. The first attempt kept the three scripts as thin wrappers calling `cedar`, on the reasoning that the names are written into CLAUDE.md, `docs/ARCHITECTURE.md`, four other documents and muscle memory. Marty read the result and asked the obvious question: are they really needed, there is no code in them. Checking answered it — **nothing automated ever called them**: no CI (there is none), not `e2e.ps1`, not npm, not electron-builder. Every one of the thirty hits across the repository was a mention in a comment or a document, which is text that gets edited by the same motion as everything else.

So "the name is in the documentation" was never an argument, because the documentation was being rewritten in that same session, and the muscle memory belongs to the one person who was asking for the scripts to go. What the wrappers would have cost is concrete: a second surface of flag names to keep in sync (`-SkipBuild` ↔ `--skip-build`, `-Ascii` ↔ `--no-unicode`), a recursion hazard that exists only because of them — an installed `cedar` from before this change calls the scripts back, which needed a guard built for a problem the wrappers themselves created — and two names for one thing in every sentence of documentation.

The one honest thing they offered was working on a machine where `cedar` is not installed yet. That did not need three fallbacks; it needed one true statement, which is now written at the top of `install-cli.ps1`: **that script is the first thing to run on a fresh clone**, and after it everything is `cedar`. If the tool itself is ever broken, the way round needs nothing from that folder either: `dotnet run --project CedarClerk.Cli -- deploy --preflight`.

Removing them found one live bug the tests had not: the config wizard validated a repository root by looking for `Scripts/deploy.ps1`, so it would have rejected this very repository. It checks for `CedarClerk.sln` now, and a test fails if any string literal in the tool names a script that does not exist — a comment mentioning a deleted script is history and is fine, a literal is a path the tool will follow.

**`cedar deploy` now deploys**, which reverses ADR-118 decision 2 together with its reason rather than in spite of it. That reason was specific: `deploy.ps1` had no safe stopping point before the swap, so taking it to the last step would have meant cutting the script open in the same session that changed the interface calling it — and then the first failed deploy would have been unanswerable, new logic or new wrapper. On stage 2 there is nothing to cut: the logic is rewritten whole, with its caller, which is exactly the "one thing at a time" the first stage was protecting. What survives from that decision is the half that was never about implementation — **a person starts a deploy**. The checks run, the version pair is printed, and it asks with the default set to no. `--preflight` keeps the old behaviour exactly.

**What deliberately did not move.** `e2e.ps1` is not an orchestrator but a test stand: it wipes a scratch data directory, starts the server without a bot token, registers an account, restarts to pick up the admin bootstrap, runs Playwright and kills the server whatever happens. That is process and environment lifecycle, which is what a shell script is for; `cedar test --smoke` calls it as a phase. `install-cli.ps1` stays for the reason above. And **`tar` stays an external program** — .NET 8 ships `System.Formats.Tar` and the temptation was real, but a Windows file has no unix mode for `TarWriter` to copy, so the server would unpack a tree of `000`-permission files: a deploy that passes every check and then does not start. Changing the packer in the same session as the orchestrator is also the exact mistake stage 1 spent itself avoiding. The bytes on the wire are yesterday's bytes.

Everything else moved verbatim, comments included, because in `deploy.ps1` the comments naming the incident *are* the code's justification: resumable upload because `scp` left production down twice, checksum on the far side because a truncated upload looks like a success, the service stopped only for two renames, `app.prev` kept for the rollback, and `.exe` → `.blockmap` → `latest.yml` in that order because that is what `electron-updater` reads. One thing was collapsed rather than copied: the two duplicate retry loops (one for the release tarball, one for the installer) became one. The separation existed so a change to the newer feature could not reach the path production rides on; the insurance that replaces it is a test that exercises the resume, the over-long remote file and the exhausted retries — which neither PowerShell copy ever had.

**Two bugs the move created and the tests did not catch, both found by running it.** `npm` has to be started by its **full path**: `npm.cmd` locates its own JavaScript through `%~dp0`, and a batch file launched by bare name through `CreateProcess` gets `%0` without a directory, so `cmd.exe` resolves it against the *working* directory instead. The symptom is `Cannot find module <cwd>\node_modules\npm\bin\npm-cli.js`, which reads like a broken project and is a broken launch. It never happened while npm was being started by PowerShell. Then the fix had its own trap: Node ships both `npm` (a shell script, for git-bash) and `npm.cmd` in the same directory, and resolving the bare name first finds the file `CreateProcess` cannot run — a thing that exists and refuses to start, which is worse than nothing found.

**A hole in `--dry-run` that stage 1 never had.** The promise is "touch nothing", and `ICommandRunner` covered every process and every ssh call — which was the whole story while the pipelines were wrappers. It stopped being the whole story the moment the build moved into C#: `Directory.Delete(publish)` is not a command, it is a method call, and it would have deleted a real directory during a run that had promised to change nothing. `IFileWriter` is the same arrangement as the runner rather than a new one — the implementation is swapped, so no pipeline contains an `if (dryRun)` branch. Reads are deliberately outside it: `File.Exists` changes nothing, and routing it through a seam would make every check unanswerable under `--dry-run` rather than merely inert.

**The screen.** All three pipelines run behind one `StageBoard`: the whole plan drawn before any of it happens, each step ticking over with its own clock, and the running step free to draw whatever it likes underneath itself. Drawing the plan up front is the test grid's field of empty cells again (ADR-118 decision 14) and it is there for the same reason — "seven steps, we are on three" is a different feeling from a line of text appearing every forty seconds with no idea how many are left. The upload gets a bar with speed and ETA and **a braille chart of the throughput it is actually getting**, from the same `BrailleChart` that draws CPU on the dashboard. That one is not decoration: the failure this transfer path exists for is a connection that goes quiet and is then reset, and "quiet" has a shape — the line sags for several seconds before the drop, which no single averaged number can show. The deploy ends on a flight recorder: where the time went, proportionally, so "why was that slow" is answered with "the Angular build was 70% of it" rather than a shrug.

**The logo says what it is now.** Marty's note: the splash opened with the product's wordmark and nothing else, which is a console claiming to be the product. A subtitle slides out from underneath after the letters land — `c e d a r · operations console`, the name letter-spaced so it reads as a mark, the description in plain lowercase one size down. It is kept inside the wordmark's own width, because every "does the logo fit" check measures the wordmark; it goes after the highlight sweep rather than with it, since the wordmark has to have finished being the thing on screen before something else qualifies it; and it drops the middle dot in ASCII mode with everything else.

**And the console can open the product it manages** (`cedar open`, plus an Open group in the menu): production, the blog, a local dev server, and the desktop shell. The desktop is the only interesting one, because there are two of it — the installed copy is preferred, since that is the one Marty actually uses, and the working copy in the repository is the fallback, since that is the one that exists five minutes after a change. Neither is guessed at silently: what was opened is printed.

`Pipelines/GitGuard.cs` is `_git-guard.ps1` dictated across, `LIVE`/`LIVE-PREV` and all. **120/120** on `CedarClerk.Cli.Tests` (32 new), and the full run: **923 backend, 18 frontend, contrast clean, exit 0**. The honest cost is written into ADR-119 rather than left implied — the riskiest script in the project no longer has a parallel copy to fall back to, the fallback is git, and the first real deploy on this code should be done with someone watching. What softens it is that the failure modes are unchanged by construction: nothing stops until the tarball is checksummed and unpacked, `app.prev` is there, the rollback works, and an interrupted upload still resumes from the byte it reached.

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
