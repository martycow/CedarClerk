# UI Inventory

Per-element inventory of the frontend UI — what exists, where it lives, what it does, and whether it needs (and has) a loading indicator. Complements `docs/DESIGN.md` (which covers tokens/CSS patterns, not individual elements). Update this file whenever a UI element is added, removed, or meaningfully changed — that's the point of it: a future session should be able to scan a page's table and answer "does this popup need a loading indicator, and does it have one?" without re-reading the component.

## Format

Two layers, added 30.07.2026 (Phase 10, ADR-070): the **per-element tables** below — one per page/component, six columns, unchanged — and, at the end of the file, a **verification map** with one row per route/surface plus an icon inventory. They are separate on purpose: "does this popup have a loading indicator" and "has anyone opened this screen" change at different times and for different reasons.

Per-element table columns:

| Column | Meaning |
|---|---|
| Element | Short name |
| Location | File + selector/anchor to find it |
| Type | `button` / `modal` / `popover` / `panel` / `toast` / `tab` / `dropdown` |
| Purpose | What it does and why it exists |
| Loading state | Whether a long-running action here needs a loading indicator, and whether one exists today |
| Notes | Anything else worth knowing (gating, known issues) |

---

## `editor.component` (`cedarclerk-web/src/app/pages/editor.component.{ts,html,css}`)

The main writing surface — by far the most complex page. Topbar + two toolbar rows + editor sheet + status bar, plus several modals/popovers layered on top.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Drafts link | topbar hamburger icon, `routerLink="/drafts"` | link | Opens the full `/drafts` page. **Replaced the old drafts popover** (26.07.2026) — the in-topbar draft list/switcher and its per-draft delete are gone; `/drafts` is the single place for browsing, deleting and organizing drafts | N/A | |
| Import `.cedar` | topbar, `.icon-btn` + hidden `#cedarInput` | button | Import a `.cedar` package as a new draft; moved here from the removed popover | Needed & present — spin icon via `importingCedar()`; errors surface as a dismissible toast (`.ai-toast.error`) since the topbar button has no room for text | Markdown (`.zip`) import moved to `/drafts` instead |
| Download `.cedar` | topbar, `.icon-btn.cedar-download` | link | Downloads the open draft as `.cedar`; only rendered when a draft is open | N/A | Hidden below 768px — the same action exists in the Export modal, which is reachable on mobile |
| Channels popover | topbar, right side | popover | Connect/select Telegram channels the bot knows about; per-channel sparkline stats | Not verified whether connect-flow has a spinner | Follow up if revisited |
| Export button + Export modal | topbar `.export-trigger` button → `app-modal` | button + modal | **Restructured 07.08.2026 (ADR-096)** into three numbered steps — **1 Version** (language checkboxes for the whole window, plus a one-line note naming the languages with no version yet), **2 Where to publish** (`.dest-card` per destination: Blog, Telegram, and one per connected short-post network; an unconnected network keeps its card, un-tickable, linking to Settings → Integrations), **3 Settings for each destination** (a panel per ticked destination only). Blog panel: live URL + per-language badges, privacy/listed/copy-protection, registration form. Telegram panel: capability warnings, **a channel picker per ticked version** (`.channel-lang-row`, ADR-098 — one row each, language badge shown only when more than one version is ticked, and a one-line note naming the versions still without a channel), thread offer with its part list, photo compression. X/Bluesky panel: **language pills** choosing which versions this account posts (`.lang-pick-row`, ADR-100, shown only when more than one version is ticked) plus a two-way **mode toggle** (`.mode-toggle`) — announcement+link (per-language text field with the network's own counter) or the whole post as a thread (part count, and credit cost on X) — never both sets of fields at once; the thread half is disabled while a send time is set. Then **4 When to send** (ADR-099 — the schedule moved out of the Telegram panel into its own step and applies to every ticked network; rendered only when a schedulable destination is ticked, with quick presets, a `datetime-local` field, the other-timezones hint, and notes that threads are not scheduled and the blog publishes immediately), **Private access** (invitations + watermark, shown whenever the post is private, not gated behind ticking the blog) and **Files** (`.cedar`/`.zip`/per-draft asset list). **One Publish button** fires every ticked destination, X and Bluesky included — their own per-section Publish buttons are gone. Connecting anything moved to Settings (ADR-095) | Needed & present — `blogBusy()`/`exporting()`/`publishingAll()` spin icons + `.inline-progress`; the publish checklist below is the real progress surface | Was a two-row column layout (`.export-row-primary`/`.export-row-utility`) with the language buried inside Telegram and two competing publish models |
| Publish progress checklist | `editor.component.html`, `.publish-run` inside its own `app-modal` (opened by `publishAllConfirmed()`) | modal | Watches a publication like a test run (01.08.2026): one row per phase (save / blog / each Telegram language / schedule / each short-post network per language, ADR-096) with waiting → spinner → ✓/✗ statuses; a thread (T-106) unfolds into numbered part chips (`.pr-part`) that fill green as parts send; an error is pinned to the failed step and it is the **first** failed part's error — the root cause, not the "held back" cascade; success rows carry the post link; summary line at the bottom | IS the loading state — live statuses fed by `awaitJobs()`'s per-poll callback | Stacks over the Export modal; closing it cancels nothing (the queue owns the jobs, T-090). Escape closes both stacked modals — pre-existing `app-modal` behavior |
| Theme toggle | topbar `.theme-toggle` button | button | Switch light/dark theme | N/A — instant, `ThemeService` | |
| Account popover | topbar, avatar/email trigger | popover | Show account email, link to Settings, log out | N/A — instant actions | |
| Block-type dropdown | toolbar row 1, `.block-dropdown` | dropdown | Paragraph / Heading 1–6 | N/A | One of the popups broken by the Bug 2 `backdrop-filter` regression (fixed 25.07.2026) |
| Undo/Redo | toolbar row 1 | button | TipTap history | N/A | |
| Text group (`tplText`) | toolbar, movable via Settings → Toolbar | buttons | Bold/italic/underline/strike/spoiler | N/A | |
| Insert group (`tplInsert`) | toolbar, movable | dropdown/popovers | Insert modal (link/YouTube/email/phone/mention), emoji popover, date/time popover, footnote popover | N/A — instant inserts | Emoji popover gained Flags (50 country/generic flags) + Flag sequences (01.08.2026) — multi-emoji colour runs (⚪️🔴⚪️, 🤍💙❤️, …) for flags Unicode never encoded, rendered as wide pills (`.emoji-grid-wide`). Windows caveat (same fact as DB3.1): country flags render as letter pairs in the editor on Windows — no regional-indicator glyphs in Segoe — but correctly in Telegram and on readers' phones; the sequences render everywhere |
| Lists group (`tplLists`) | toolbar, movable | buttons | Bullet/numbered/task list, indent/outdent | N/A | |
| Code group (`tplCode`) | toolbar, movable | buttons | Inline code, code block | N/A | |
| Media group (`tplMedia`) | toolbar, movable | buttons + file pickers | Image/video/GIF/audio/carousel/collage upload, YouTube insert | Needed & present — see Upload-progress panel below | |
| Blocks group (`tplBlocks`) | toolbar, movable | buttons/popovers | Table insert/row/col ops, formula (inline/block), blockquote, toggle block, table of contents, divider, annotation anchor | N/A | |
| AI actions popover | toolbar, `.ai-chip` | popover | Fix errors / "schizo-izer" rewrite (Pro Plus gated) | Present but weak — only an elapsed-time counter, no real progress bar | Tracked as Phase 8 Step 8 in `docs/ROADMAP.md`; overlaps Backlog #6 and #14 (move AI features elsewhere) |
| "Customize toolbar" link | toolbar row 1, far right | link | Jumps to Settings → Toolbar customization | N/A | |
| Upload-progress panel | editor sheet area | panel | Per-file upload progress bars for media inserts | Present | |
| AI-confirm modal | `app-modal`, `cancelAiConfirm()` | modal | Confirm before running an AI edit (replaces old `window.confirm()`) | See AI actions popover above | |
| New-draft dialog | `app-modal`, `closeNewDraftDialog()` | modal | Title, languages, tags, template, target folder, and a "private" checkbox for a new draft. Folder/private are applied as follow-up calls right after creation (the create endpoint doesn't take them) and, unlike languages/tags/template, are **not** remembered in `newDraftDefaultsJson` — they're per-draft intent, not a preference | Needed & present — `draftsBusy()` drives the "Creating…" button state | Tags and folder are `app-tag-picker`/`app-folder-picker` with `[inline]="true"` (FI3.3, 27.07.2026) — inline because a nested `app-popover` inside `app-modal` fights the modal's own fixed positioning, which is why this dialog used to carry its own pill rows |
| Insert modal | `app-modal`, `closeInsertModal()` | modal | Link/YouTube/email/phone/mention insert form | N/A | |
| ~~Delete-draft confirm~~ | — | — | **Removed 26.07.2026** along with the drafts popover that was its only trigger — deletion now lives solely on `/drafts` (which has its own confirm modal) | — | |
| Re-translate confirm | `app-modal`, `cancelTranslateConfirm()` | modal | Confirm overwriting an existing EN translation | N/A | |
| AI success toast | editor page | toast | Confirms an AI op finished | N/A | |
| Lang tabs (RU/EN) | editor sheet header | tab | Switch which language's content is being edited; flushes autosave on switch | N/A | Re-translate/delete-EN buttons and a stale-translation indicator live alongside |
| English-empty-state panel | editor sheet (EN tab, no content yet) | panel | Offers auto-translate / copy-from-RU / start-empty | Present — has its own progress bar during auto-translate | |
| Tag row | below editor header, `app-tag-picker` | chips + popover | Shows/removes tags on the draft; "+ Tag" popover has a tag-cloud (most-used/other) + new-tag input. A `[manage]` mode (used on `/drafts`) adds rename/delete across every draft — idea #3, 27.07.2026 | N/A | Shared component since FI3.2 (27.07.2026) — same control on `/posts` and in the new-draft dialog; the cloud is fed by `TagUsageService`. See Backlog #8 (blog card truncation) |
| Folder button | beside the tag row, `app-folder-picker` `[compact]` | button + popover | Files the draft; also creates, renames and deletes folders | N/A | Shared component since FI3.3 (27.07.2026), backed by `FoldersService`. Loads its list on init, not on open — the trigger shows the folder's *name*, and load-on-open is what caused IB6 |
| Editor sheet | `.sheet`, TipTap host | panel | The actual rich-text canvas; ruler + RU-diff gutter markers alongside | N/A | |
| Status bar | bottom of `.app`, `.status-bar` | panel | Zoom controls, word/char count, sync indicator (saved/saving/error+retry), debug-console toggle, fullscreen toggle | The sync indicator itself *is* a loading/status indicator | The debug console's fixed strip still covered this bar after the 25.07.2026 margin fix and swallowed clicks on the fullscreen button; fixed 27.07.2026 by making that strip `pointer-events: none` and hosting the console's toggle in this bar |

---

## Shared components (`cedarclerk-web/src/app/shared/`)

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| `app-modal` | `modal.component.ts` | modal shell | Generic overlay + card used by every dialog in the app | N/A (shell only — content decides) | `.modal-overlay` uses `position: fixed; inset: 0` — must not be nested inside an ancestor with `backdrop-filter`/`transform`/`filter` (see Bug 3, 25.07.2026) |
| `app-popover` | `popover.component.ts` | popover shell | Generic trigger+panel popover used by every dropdown/popover in the app | N/A | Panel uses `position: fixed` deliberately, to escape `overflow` clipping on the trigger's ancestors — same containing-block caveat as `app-modal` (see Bug 2, 25.07.2026) |
| `app-debug-console` | `debug-console.component.ts`, mounted in the root app shell behind `showDebugConsole()` | floating tab + panel | Dev tool: inspect in-flight/failed API requests without SSH-ing into the Pi | N/A (dev tool) | Fixed `bottom:0` overlay, always on top (`z-index:200`), `pointer-events: none` on the host so only the panel/tab take clicks. Open state lives in `DebugLogService.open`; `DebugLogService.hostBarHeight` (set by the editor to its status-bar height, 0 below the 768px breakpoint) both lifts the panel to sit on top of that bar and suppresses the floating tab — in the editor the toggle is a status-bar button instead, and the panel animates open by height (27.07.2026). Pages with no status bar keep the floating tab. **Hidden on public routes** (`/login`, `/register`, `/terms`, `/privacy`) since 26.07.2026 — it reports the signed-in owner's own API traffic, so it's meaningless (and visually intrusive) on pages reachable without an account (`app.ts`'s `PUBLIC_ROUTES`) |
| `cedar-logo` | `cedar-logo.component.ts` | decorative | Logo SVG, used in topbar/auth pages | N/A | |
| `legal-page` | `legal-page.component.ts` | layout wrapper | Shared frame (logo/title/back-link/prose styling) for Terms/Privacy | N/A | |
| `app-page-header` | `page-header.component.{ts,html,css}` | header | **Added 27.07.2026 (header/nav redesign, ADR-052)**. Glass header shared by `/posts`, `/glossary`, `/settings`, `/admin` and `/drafts` — back-to-editor (hidden on `/drafts` via `[showBack]="false"`), logo, breadcrumb, a nav row (Posts/Glossary/Settings/Admin, current page filled `--accent`), theme toggle, `app-account-menu`. Replaces four near-identical header blocks (Posts/Admin already had the glass fill, Glossary/Settings didn't) plus `/drafts`'s own copy. The editor topbar keeps its own markup (draft title instead of a breadcrumb) but now shares the same nav-row visual pattern | N/A | `page` input selects the breadcrumb text and which nav button gets the active fill; admin button only renders for `auth.isAdmin()` |
| `app-account-menu` | `account-menu.component.ts` | popover | Avatar trigger → profile link, admin shortcut (if applicable), logout | N/A | **`showNav` input removed 27.07.2026** — every screen now carries the nav row itself via `app-page-header`/the editor topbar, so this popover no longer duplicates Posts/Glossary/Settings links on any page |

---

## `drafts.component` (`cedarclerk-web/src/app/pages/drafts.component.{ts,html}`)

Full-page drafts grid/table — the compact editor drafts popover's bigger sibling.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Header (back link, nav row, theme toggle, account menu) | `<app-page-header page="drafts" [showBack]="false">` | header | **27.07.2026 (ADR-052)**: moved into the shared `app-page-header` (see Shared components). Back-to-editor stays hidden (nothing to go "back" to — the editor is reached by opening a draft or "New draft"), but Posts/Glossary/Settings/Admin nav buttons are now present here too, which they weren't before | N/A | |
| Search input | `:27` | input | Client-side filter by title/tag | N/A | Plain string, not a signal |
| View toggle (table/grid) | `:29-34` | tab | Switches `view()` layout | N/A | |
| New draft button | `:36` | button | Nav to `/editor?new=1` | N/A | |
| Import Markdown (`.zip`) | toolbar, `.btn-ghost` + hidden `#markdownInput` | button | Import a Notion-shaped Markdown zip as a new draft. **Moved here 26.07.2026** from the editor's removed drafts popover | Needed & present — spin icon via `importingMarkdown()`; unmatched-image warnings and errors render as inline `.channel-error` lines under the toolbar | On success with no warnings it navigates straight into the new draft; with warnings it stays put so the message is readable |
| Filter tabs (All/Drafts/Scheduled/Published/Needs attention/Archived) | `:40-45` | tab | Sets `filter()`, live counts via `filterCount()` | N/A | |
| Draft rows (table/grid) | `:56-113` | panel | Click opens draft; status badge, lang badges, folder, tags, updated date, and a 🔒 lock icon on private drafts | Needed & present — page-level `loading()` | The private lock sits inside the Title cell in both views rather than as its own column — no `grid-template-columns` change needed, and it works identically in the grid cards |
| Activity cell (per row) | table + grid card, `.activity-cell` | panel | Blog views and reactions (likes + dislikes combined), each with a `+N` accent chip for what accumulated since the previous session (B23, ADR-043). Renders `—` for drafts that were never blog-published | Needed & present — page-level `loading()` | The delta comes from the server (`DraftStatSeen` baseline, 30-min session gap), not from `localStorage`, so it matches across devices. No sparkline: no per-draft stats history exists to draw one from |
| Archive/unarchive button (per row) | `:75-79`, `:101-103` | button | `toggleArchive()` | Needed & present — `busyId()===d.id` spins the icon (table view only; grid view swaps icon without spinning — minor inconsistency) | |
| Delete button (per row) | `:80-82`, `:104-106` | button | Opens delete-confirm modal | Needed & present — disabled while `busyId()` set | |
| Delete confirm modal | `:118-126` | modal | Cancel/Delete via `confirmDelete()` | Needed & present | |
| Error banner | `:48` | toast (inline) | Surfaces list/archive/delete failures | N/A | Not dismissible |

## `settings.component` (`cedarclerk-web/src/app/pages/settings.component.{ts,html}`)

Profile, Appearance, Toolbar customization, Header slots, Social links, Subscription, Integrations — one long page with anchor-nav.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Header | `<app-page-header page="settings">` | header | **27.07.2026 (ADR-052)**: moved into the shared `app-page-header` — gained the glass material it didn't have before (was a solid `--surface` fill) and a Posts/Glossary/Settings/Admin nav row | N/A | See Shared components |
| Anchor chip nav | `:18-26` | chip-row | Jumps to each section | N/A | 8 chips (Credits added 05.08.2026) |
| Post signature + URL fields | `:48-56` | panel + button | Pro-gated custom signature | Needed & present — `signatureBusy()`/`signatureSaved()` | Free users see static attribution instead |
| Theme mode toggle | `:78-79` | tab | Which palette is being edited (local UI state) | N/A | |
| Accent preset swatches | `:84-89` | chip-row | `pickAccentPreset()`, saves instantly | **Needed but missing** — fire-and-forget save, only `appearanceError()` on failure | "Applies instantly" by design, but a failed save is silent otherwise |
| Sheet width / Typeface toggles | `:98-112` | tab | Instant-save prefs | **Needed but missing** (same gap as above) | |
| Font size / line height sliders | `:117-126` | slider | Instant-save on every drag tick, no debounce | **Needed but missing** | |
| Appearance checkboxes (ruler/paragraph numbers/word count/focus mode/sheet flush) | `:130-134` | chip-row | 5 instant-save booleans | **Needed but missing** | |
| Toolbar preset toggle (Minimal/Standard/Everything) | `:146-149` | tab | Instant-save | **Needed but missing** — `toolbarError()` shown, no busy state | |
| Toolbar row 1/2 drag lists | `:157-174` | panel (drag-drop) | CDK drag-drop moves groups between rows | **Needed but missing** | |
| Toolbar group/button visibility checkboxes | `:183-188` | chip-row | Show/hide groups or individual buttons | **Needed but missing** | |
| Reset-to-Standard button | `:197` | button | `pickToolbarPreset('standard')` | **Needed but missing** | |
| Header slot selects (1/2/3) + author/URL/location inputs | `:208-254` | dropdown + panel | Assigns metadata fields to subtitle slots; slot 3 Pro-gated | Saved via explicit Save button below | |
| Save header slots button | `:258-260` | button | `saveProfile()` | Needed & present — `profileBusy()`/`profileSaved()` | |
| Social link inputs (Twitter/Instagram/Facebook/YouTube/GitHub) | `:274-292` | panel + button | Informational-only URL fields | Needed & present — `socialBusy()`/`socialSaved()` | |
| Plan banner + Manage billing link | `:310-323` | panel/button | Opens Stripe portal | Needed & present — `billingBusy()` | Only shown if Stripe customer linked |
| Plan cards (Free/Pro/Pro Plus) + trial link | `:333-365` | panel | `pickPlan()`, 7-day trial start | N/A | |
| Pay-method radios (Stripe/PayPal/Telegram Stars) | `:372-397` | chip-row | Selects payment provider; disabled per-method if unconfigured/unlinked | N/A | Good gating with explanatory tooltips |
| Confirm upgrade/pay button | `:399-402` | button | Redirects to hosted checkout / sends Stars invoice | Needed & present — `billingBusy()` | |
| Credits balance banner | `sec-credits` | panel | ADR-092 — wallet balance (`GET /api/billing/credits`) | N/A | **Added 05.08.2026** |
| Credit pack cards (10/50/100) | `sec-credits` | panel | `pickPack()` → pay-method step (Stripe/Stars, no PayPal yet) | N/A | Reuses `plan-card`/`pay-method` markup and gating |
| Buy credits button | `sec-credits` | button | Stripe hosted checkout redirect / Stars invoice to bot chat | Needed & present — `creditsBusy()` | `creditsError()` inline on failure |
| Credit ledger list | `sec-credits` | panel | Last 50 wallet movements, reason + signed delta | N/A | Empty state when no activity |
| Telegram link/unlink row | `:412-441` | panel | `linkTelegram()` / inline unlink confirm | Needed & present — `telegramBusy()` ("Waiting for Telegram…") | Unlink uses an inline "Sure?" chip, not a modal |
| Bot status row | `:445-459` | panel | Reachable/unreachable status dot + external link | N/A | Read-only |
| Telegram channels block | `sec-integrations`, `.integration-block` | panel | **Rewritten 07.08.2026 (ADR-095)** — the connected list with per-row Disconnect, the chats the bot is already in with per-row Connect, a Refresh button (`my_chat_member` cache, there is no "list my chats" call), and an `@name`/id field behind a disclosure. Was a count with "manage in editor →" | Needed & present — `channelBusy()` on every button, `knownChatsRefreshing()` spins Refresh | The editor's Export window now only *picks* from this list |
| Bluesky block | `sec-integrations`, `.integration-block` | panel | **Added 07.08.2026 (ADR-095)** — handle + app-password connect (verified server-side before it is stored), connected handle with last-published date, Disconnect | Needed & present — `blueskyBusy()` | Moved out of the Export modal |
| X block | `sec-integrations`, `.integration-block` | panel | **Added 07.08.2026 (ADR-095)** — one Connect button (OAuth round trip to x.com, back to `/settings?tab=account&x=connected`), the `?x=` outcome banner, credit balance with a jump to the Credits section, Disconnect | Needed & present — `xBusy()`; busy deliberately stays set on success, the page is navigating away | Moved out of the Export modal |
| Planned networks strip | `sec-integrations`, `.integration-block.dashed` | chip-row | Threads/Facebook/Medium/Patreon/Notion/Google Docs, named rather than hidden | N/A | Moved out of the Export modal's "coming soon" disclosure |

**Known gap**: nearly every "instant save, no Save button" preference control in Appearance/Toolbar has no loading indicator at all — only an error message on failure, nothing while the request is in flight. Worth fixing alongside Backlog idea #6 (loading indicators for long operations) if that gets scoped.

## `posts-manager.component` (`cedarclerk-web/src/app/pages/posts-manager.component.{ts,html}`)

The `/posts` page (N7, ADR-046). `/comments` and `/stats` now redirect here; the two components below are rendered as its tab bodies with their own page headers removed.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Header | `<app-page-header page="posts">` | header | **27.07.2026 (ADR-052)**: moved into the shared `app-page-header` — same glass material it already had, but now with a Posts/Glossary/Settings/Admin nav row (was reachable only via the account popover before) | N/A | See Shared components |
| Tab strip (Posts · Stats · Forms) | `.manager-tabs` | tab | `setTab()`; switching to Forms loads submissions for the selected post | N/A | "Reactions & comments" removed by FI3.5 (27.07.2026) — feedback renders under the selected post instead, and the new-feedback badge moved onto Posts. `?tab=feedback` still resolves, to Posts. No fixed widths and `overflow-wrap: anywhere` — the labels get translated eventually and must survive long-word languages (B26) |
| Post list (left) | `.post-list` | panel | Selects the post the detail pane acts on; LIVE/Archived chips | Needed & present — page-level `loading()` | Also used, filtered to private posts only, by the Forms tab |
| Detail pane — title/tags + Save | `.post-detail` | input + button | Metadata-only edits | Needed & present — `renaming()` spins the icon, `busy()` disables | A rename re-sends the draft's own `cedarJson` unchanged (the save endpoint takes title+body together) |
| Detail pane — folder | `app-folder-picker` | button + popover | `assignFolder()`, applies immediately; folders can also be created/renamed/deleted here | **Needed but missing** — no busy state on the assignment | Shared picker since FI3.3 (27.07.2026); same instant-save-no-indicator gap as the Appearance controls above |
| Detail pane — article title | `.detail-field`, `editArticleTitle` | input | The reader-facing headline, separate from the draft's name above it (idea #4). Empty means "same as the name" | Saved with the rest of the metadata by the Save button | Added 27.07.2026 |
| Forms tab — preset editor | `.form-block` ×3 + `.question-card` | panel | Identity (name, language, intro), built-in fields, questions | Present — explicit Save with a saved/unsaved/saving state | Rebuilt for FI4.2 (27.07.2026) out of one flat stack of inputs; the language selector is FI4.1 |
| Detail pane — feedback | `app-comments [onlyDraftId]` | panel | This post's reactions and comments, replacing the removed tab (FI3.5) | Needed & present — the component's own `loading()` | One instance filtered client-side, so switching posts costs no request |
| Detail pane — private / archive / open in editor / delete | `.detail-actions` | button | Metadata actions; delete goes through a confirm modal | Needed & present — all disabled while `busy()` | |
| Detail pane — copy protection | `.detail-field` checkbox, `toggleDisableCopy()` | checkbox | Blocks selection/copy/context menu on the rendered blog page (ADR-063); private posts only, mirrored in the editor's Export modal | Needed & present — disabled while `busy()` | Added 29.07.2026 |
| Detail pane — Blog / Telegram links | `.detail-links` | link | Opens the published post where it lives | N/A | Rendered only when the draft actually has a slug / message id |
| Forms tab — form editor | `.form-editor` | panel | Toggle the form on/off, intro text, built-in field flags, questions (Text / One of / Several), delete form. **Moved here from the export modal** (N10, ADR-047) | Needed & present — `regBusy()` disables while a save is in flight | Every edit persists the whole blob; an unlabelled question isn't saved until it has a label |
| Forms tab — presets | `.preset-pill`, `.preset-add` | button + input | Save the current form as a named preset, apply or delete one (N12) | Needed & present — `regBusy()` | Applying copies the definition onto the post; the same presets appear as chips in the export modal at publish time |
| Forms tab — distribution charts | `.chart-block`, `.pie`, `.legend` | panel | Answer distribution per closed question | N/A — derived from already-loaded submissions | Pie only for `choice`/`multi`; single-answer questions render as one line of text; 7th option folds into "Other"; `--series-1..6` tokens, legend carries label+count+percent so identity isn't colour-alone |
| Forms tab — submissions list | `.registration-row` inside `.registration-item` | panel | Who submitted a private post's registration form, when, and what they answered | Needed & present — `registrationsLoading()` | Answers now resolve to question labels, falling back to the raw key for questions deleted after the fact |
| Forms tab — delete submission | `.registration-remove` trash per row + Delete in the submission modal → confirm `app-modal` | button + modal | Removes one submission (01.08.2026 — the owner's own test answers polluted the distribution charts). Charts recompute from the shortened list on their own. **Also revokes that reader's access** — the row carries the ADR-084 grant — which the confirm body says out loud | Needed & present — `registrationDeleting()` disables the confirm button with a spinner | Same confirm pattern as delete-post/delete-preset; `$event.stopPropagation()` keeps the trash from also opening the row's modal |

## `stats.component` (`cedarclerk-web/src/app/pages/stats.component.{ts,html}`) — now the Posts Manager's Stats tab

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Channel tabs (Blog + per-channel) | `:22-30` | tab | `selectBlog()`/`selectChannel(id)` switches data source | **Needed but missing** — no busy signal on the fetch, only page-level `loading()` on initial load | Rapid re-clicking could race — no guard |
| Date-range tabs (30/90/180d) | `:34-36` | tab | `selectRange(days)` re-fetches at new range | Same gap as above | |
| Metric stat cards (Subscribers/Views/Likes/Comments) | `:41-84` | panel | Current value + week-over-week delta | Needed & present — page-level `loading()` gates first render | Blog view omits Subscribers |
| Line/area chart + hover tooltip | `:53-79` | panel/popover | SVG sparkline, crosshair tooltip on hover | N/A | Falls back to "Not enough history yet" under 2 snapshots |
| Audience breakdown cards (Views by country / by reader language) | `:93-146` | panel | Views summed over the selected range, split by `CF-IPCountry` and `Accept-Language` (ADR-097) | Needed & present — page-level `loading()`, same fetch as the cards above | Blog tab only (Telegram reports no geography). Flag emoji from the alpha-2 code, names via `Intl.DisplayNames` in the UI language; `??` renders as 🌐 "Unknown" |
| Show all / Collapse (per breakdown) | `:113-117`, `:137-141` | button | Folds the long tail past the first 8 rows | N/A | Purely client-side — the endpoint returns every bucket |

## `comments.component` (`cedarclerk-web/src/app/pages/comments.component.{ts,html}`) — embedded under the selected post in the Posts Manager (FI3.5); `onlyDraftId` scopes it, unscoped still renders every post

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Reactions summary bar | `:18-21` | panel | Static 👍/👎 totals across all drafts | N/A | Not filterable/clickable |
| Comment cards list | `:26-38` | panel | Draft title, in-text-vs-whole-article tag, author, timestamp, text | Needed & present — page-level `loading()` | No pagination — `listAll()` returns everything at once |
| Delete comment button (per card) | `:32-34` | button | `deleteComment(id)` | **Needed but missing** — no per-row busy/disabled state | **Known issue**: `comments.component.ts:43-46` has no try/catch around the delete call — a failed request throws unhandled and the item silently isn't removed, no error shown. Worth a quick fix (wrap in try/catch + `httpErrorMessage()`, matching the pattern already used elsewhere e.g. `settings.component.ts`'s `saveProfile()`) — flagged here, not fixed as part of this pass since it wasn't part of the original bug list |

## `login.component` / `register.component` (`cedarclerk-web/src/app/pages/{login,register}.component.{ts,html}`)

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Theme toggle | both, `:2-4` | button | No shared header bar on auth pages | N/A | |
| Email/password inputs | login `:15-19`; register `:15-23` (+ invite code) | input | Credentials | N/A | Register's Enter-to-submit is only wired on the invite-code field, not email/password — minor inconsistency |
| Log in / Create account button | login `:22-24`; register `:30-32` | button | Submits, navigates to `/editor` on success | Needed & present — `busy()` disables + shows "…" | |
| Error message | login `:26`; register `:26-28` | toast (inline) | Login: generic "Invalid email or password" (doesn't distinguish network vs auth failure). Register: server-provided, more specific | N/A | |
| Register/Log in cross-link | login `:28`; register `:34` | button (link) | Nav between the two | N/A | Login page notes "invite required" |
| Terms/Privacy links | register `:35` | button (link) | Nav to `/terms`/`/privacy` | N/A | Required by consent copy but not gated by a checkbox |

## `privacy.component` / `terms.component`

Thin wrappers (10 lines each) around `shared/legal-page.component`, passing only `title`/`updated` inputs. Content is 100% static prose with `[bracketed]` placeholders (see `docs/ROADMAP.md` Phase 8 Step 3) — no interactive elements, nothing to inventory beyond the shared `legal-page` shell already covered above.

---

## `glossary.component` (`cedarclerk-web/src/app/pages/glossary.component.{ts,html,css}`)

Idea #11. Terms the owner defines once, found and explained on the published blog. Reached via the nav row on every screen's header (account-menu link removed 27.07.2026, ADR-052).

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Header | `<app-page-header page="glossary">` | header | **27.07.2026 (ADR-052)**: moved into the shared `app-page-header` — gained the glass material it didn't have before (was a solid `--surface` fill) and a Posts/Glossary/Settings/Admin nav row | N/A | See Shared components |
| Language tabs | `.lang-tabs` | tab | Terms are listed — and matched — per content language | N/A | A term only ever marks text in its own language: a Russian description under an English article would be worse than no tooltip |
| Translate all (button) | `.lang-tabs` `.btn-ghost` | button | ADR-062: opens the whole-language translate modal | N/A | Only rendered when the selected language has at least one term |
| Translate all modal | `app-modal`, `.lang-check-list` | modal | ADR-062: checkboxes for the other content languages; one `/translate-all` call per checked language, sequentially — each call covers every term of the selected language for one AI-quota call | Present — same `translatingLang()` spinner/disable pattern as the per-term modal, whose signals it reuses | Stops on the first failed language and leaves the rest checked for a retry |
| New term / edit form | `.term-form` | panel | Term, language, description, other spellings, optional image | Present — `busy()` on Save, `uploading()` on the image picker | One form for both create and edit; a separate create dialog would be the same six fields twice |
| Term list | `.term-card` | panel | Every term in the selected language, with its aliases and description | Present — page-level `loading()` | Per-card actions are icon buttons (pencil/languages/trash) since 29.07.2026 — edit was a text button before |
| Auto-translate modal | `app-modal`, `.lang-check-list` | modal | ADR-061: checkboxes for the other content languages; one `/translate` call per checked language, sequentially | Present — `translatingLang()` spins next to the language in flight and on the confirm button; checkboxes disabled while running | Stops on the first failed language and leaves the rest checked for a retry; aliases are deliberately not translated |
| Delete confirm | `app-modal` | modal | Deleting a term stops its tooltip appearing on published posts | N/A | |
| Profile tab — Save | `.profile-save-bar` | button | One Save for identity, header slots, social links and cross-links | Present — `profileBusy()` | Single on purpose (28.07.2026): they all write through one request, and the per-section buttons that preceded it nulled the sections they did not send |
| Gate language switcher (blog) | `.reg-langs`, `RegistrationFormHtml` | link row | Switches the private post's registration form between the languages it has one for | N/A | Only rendered with more than one; the reader has no other route to their language, since the post body is behind the form |
| Glossary tooltip (blog) | `.glossary-term` / `.glossary-pop`, `BlogEndpoints.ShellTemplate` | popover | Shows the description (and image) on hover, focus or tap | N/A | Rendered by `GlossaryScanner`, first occurrence per page only, never inside code or a link. The script writes the description with `textContent`, never `innerHTML` |

---

# Phase 10 additions (30.07.2026)

Everything below was added when Phase 10 (ADR-070) audited this file against the Angular routing config and the real components. **The per-element tables above are unchanged and keep their original six columns** — verification status is tracked in the map below rather than by widening ten tables, so the two can be updated independently.

## Verification map — one row per surface

The `Verified` column is filled in by Phase 10 Block D. `smoke` means a Playwright scenario covers it (`cedarclerk-web/e2e/`, run via `Scripts/e2e.ps1`); `hand` means someone clicked through it in a browser; a defect ID points at a `docs/BACKLOG.md` row.

| Route / surface | Component | States to check | Verified | Defects |
|---|---|---|---|---|
| `/login` | `login.component` | error, server-unreachable + retry, already-signed-in redirect | smoke + hand | — |
| `/register` | `register.component` | invalid invite, duplicate email, guest-guard redirect | hand | — |
| `/drafts` | `drafts.component` | empty, loading, import progress, import error, table vs grid, long titles, horizontal overflow | smoke + hand (empty, table, grid, folder menu, tag manage, long title) | — |
| `/editor` | `editor.component` | no draft, save states (saved/saving/dirty/error), refused save, offline retry, upload progress, long title | smoke + hand (export modal both destinations, version history, translate-all, emoji, paragraph marks, appearance, save-guard dialog) | T-093, T-095, T-096, T-097, T-098 |
| `/posts` | `posts-manager.component` | empty, no forms, submissions, chip overflow, scheduled toggle | smoke + hand (posts, selected post, forms empty, forms editor with all four field types, stats) | — |
| `/settings` | `settings.component` | Profile vs Account tab, Pro-gated fields, save failure | smoke + hand (both tabs) | — |
| `/glossary` | `glossary.component` | empty, per-language empty, translate-all failure mid-run | smoke + hand (empty, one term) | — |
| `/admin` | `admin.component` | non-admin gate, empty audit, audit paging, self-targeting refusal | smoke + hand (all four tabs) | — |
| `/terms`, `/privacy` | `legal-page.component` | — | hand (terms) | T-052 (placeholder text) |
| Blog index | `BlogEndpoints.RenderIndexAsync` | no posts, timeline, tag filter, semi-public lock | smoke + hand (timeline, semi-public lock with excerpt withheld) | T-094, T-099 |
| Blog post | `BlogEndpoints.RenderPostAsync` | not-translated notice, TOC, watermark, copy protection, floating nav, glossary tooltip, poll | smoke + hand (rendered post) | T-094 |
| Registration gate | `CedarToBlogHtmlRenderer.RegistrationFormHtml` | required-field validation, language switcher, consent, static block, long answer | smoke + hand (all four field types on a real gate) | T-099 |
| `/rss.xml` | `BlogEndpoints.RenderRssAsync` | empty feed, escaping | smoke | — |

Screenshots behind the `hand` marks are reproducible with `AUDIT=1 npx playwright test 99-audit` (output in `.e2e-audit/`, gitignored) — captured through Playwright rather than the browser extension, which proved unreliable here: screenshots timing out, zoom returning the wrong region, and keystrokes never reaching the TipTap surface.

**Still not verified by anything**, and each needs a person or a device rather than a script: flush-on-hide on a real iPhone; incremental re-translation preserving manual corrections (needs a provider key and a Pro Plus account); the uk/be/ka capability refusal; the Posts Manager submission modal and "mark all as read" (need a real submission); tag rename/delete; audit paging past the first page; the glossary tooltip on a published post; per-language cross-links; and whether the Russian wording reads well — which is Marty's call, not a script's.

## `admin.component` (`cedarclerk-web/src/app/pages/admin.component.{ts,html,css}`)

IF2, built 27.07.2026 in five steps (`docs/admin-panel-scope.md`). Reached from the nav row, gated by `adminGuard`; a non-admin gets a redirect and the API answers 404 rather than 403 — an account must not learn that an endpoint it may not use exists.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Summary cards | `.summary-grid` / `.summary-card` | panel | Seven counts: users, paid, published/drafts, comments, reactions, channels, storage | Page-level `loading()` only | Not a dashboard — no history, no deltas; the data-collection layer that would allow them doesn't exist (see the Channel Analysis dependency in `docs/ROADMAP.md`) |
| Tab strip | `.admin-tabs` | tab | Users · Invites · Posts · Reports | N/A | Four tabs, unlike the Posts Manager's own strip — different component, same visual role, and one of the cross-screen inconsistencies the 28.07 design handoff flags |
| User card | `.user-card`, expands on click | panel | Email, plan chip, admin/locked/lapsed chips, meta | N/A | Expansion is click-anywhere; the action row stops propagation so a button press doesn't collapse the card |
| Plan + expiry | `.action-row`, date input + Save | button | Set tier and expiry; blank means forever | Present — `busy()` disables Save | Free has no expiry; the hint line says which rule applies |
| Reset trial | `.action-row` `.btn-ghost` | button | Clears `TrialUsedAt` so the 7-day trial can be bought again | Present — `busy()` | Disabled when the account never used a trial |
| Lock / unlock, grant / revoke admin | `.action-row` `.btn-ghost` | button | Account state changes | Present — `busy()` | **Self-targeting is refused server-side**, not merely hidden — the button is also disabled via `isSelf(u)` |
| Credits | `.action-group` — balance, amount, note, Add / Take back | button | Moves an account's credit balance in either direction | Present — `busy()` | **Added 11.08.2026.** Self-targeting is deliberately *allowed* here, unlike lock/admin: a balance is not a privilege, and testing a paid post needs credits on the testing account. Take back is disabled at zero and refused server-side below it; the note goes to the audit log, the movement to the ledger |
| Invite attribution | `.action-group` | panel | Assign an invite code to an account that predates code tracking | Present — `busy()` | Manual attribution exists because accounts older than IF2 step 3 have no code to point at |
| Invite creation | `.invite-new` | panel | New code + label | Present — `busy()` | Codes are deactivated, never deleted — deleting one would silently orphan the accounts attributed to it |
| Audit log | `.audit-list` / `.audit-row` | panel | Append-only record of every admin action | Present — `auditLoadingMore()` on the Load-more button | Paging added after `docs/admin-panel-scope.md` flagged its absence; **retention is deliberately absent** — the log is append-only on purpose |
| Load more (audit) | `.btn-ghost` under the list | button | `?skip=` paging, `hasMore` drives visibility | Present | |

## Blog surfaces (server-rendered — `CedarClerk.Server/BlogEndpoints.cs`, `CedarClerk.Core/CedarToBlogHtmlRenderer.cs`)

Not Angular: these are strings built on the server and host-routed by `Program.cs` `MapWhen` on `Host.Host`. They were missing from this file entirely, which matters because roughly half of what a *reader* ever sees lives here — and because the redesign has to touch two style systems, not one.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Site header | `RenderHeader`, `.site-header-inner` | panel | Channel identity (avatar, name, `@username` + subscriber count), then **RSS** (`.rss-btn` → `/rss.xml`, added 09.08.2026), **Open in Telegram** and the theme toggle | N/A — server-rendered | The RSS button is styled secondary against the filled Telegram one: two filled buttons read as two main actions. The feed itself has existed since ADR-024, reachable only via `<link rel="alternate">` in `<head>` |
| Post list / timeline | `RenderIndexAsync`, `.post-list` / `.timeline-item` | panel | Reverse-chronological cards with a vertical chronology line | N/A — server-rendered | A private-but-listed post (`IsListedWhilePrivate`) shows a lock and **withholds its excerpt**, deliberately |
| Post header slots | `.post-header-slots`, `HeaderSlotRenderer` | panel | Up to three configured values under the title; slot 3 is Pro | N/A | Clamped server-side for a downgraded account, not just hidden in Settings |
| Table of contents | `<nav class="toc">` | panel | Generated from the document's headings at render time | N/A | Same heading ids the Telegram anchor blocks use |
| Not-translated notice | `.not-translated-notice` | panel | Shown when the requested language has no translation row | N/A | Renders the original rather than mislabelling the page as the requested language |
| Reactions | `.annotation-controls` `.react-btn` | button | Anonymous like/dislike, per anchor and whole-article | Optimistic — the count updates locally, no spinner | Deduped by salted `VisitorHash`; raw IPs are never stored (ADR-016) |
| Comment box | `.comment-box`, `form.comment-form` | panel | Anonymous comment, optional name, one level of replies | No spinner on submit | `[hidden]` is forced globally in the blog stylesheet after a CSS rule silently overrode it twice (IB5) |
| Registration gate | `.reg-gate` / `.reg-card`, `RegistrationFormHtml` | panel | Shown instead of a 404 on a private post that has a form | Submit disables while the fetch is in flight | Answers in the reader's `?lang=`, falling back to the post's primary — so an English reader of a Russian post sees Russian chrome and untranslated author questions, by design |
| Watermark | `.watermark-overlay`, `WatermarkRenderer` | panel | Tiled text over a private post | N/A | One tiling `background-image` (base64 SVG data URI), not N repeated elements |
| Copy protection | `user-select:none` + copy/contextmenu block | — | Deterrent on private posts (ADR-063) | N/A | Deterrent, not protection — the wording in the UI says so |
| Floating nav | `.floating-nav` | panel | Back-to-menu and back-to-top, after 400px of scroll | N/A | Glyphs (`☰`, `↑`), not icon-set icons — see the icon inventory below |
| Poll | `poll` node, `PollVote` | panel | Blog-only vote, results shown after voting (ADR-055) | N/A | No Telegram surface at all, by decision |
| Glossary tooltip | `.glossary-term` / `.glossary-pop` | popover | Hover/focus/tap explanation, first occurrence per page | N/A | Description written with `textContent`, never `innerHTML` |
| RSS | `RenderRssAsync`, `/rss.xml` | — | Latest 30 published posts | N/A | Auto-discovery `<link>` in every page head |

## Surfaces added in 0.9.16–0.9.17 (30.07.2026) — absent from this file before

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Save-stopped dialog | `editor.component.html` `app-modal`, `saveRefusal()` | modal | The 409 from a refused save — shrink or stale. Buttons are "Restore stored" and "Save anyway" | N/A — the decision *is* the point | ADR-066. Nothing is lost while it is open: the editor holds the new text and the server the old one |
| Version history | `app-modal`, `revisionsOpen()`, opened by the `◷` button beside the language tabs | modal | List, preview, diff against any other version or against what is stored, restore | Present — `revisionBusy()` | ADR-067. A restore records what it replaced first, so it is itself undoable |
| Publish-diff confirmation | `app-modal`, `updateConfirm()` | modal | Every already-live language with its own diff, before one click republishes them all | N/A | ADR-065. Server-enforced by fingerprint — the client-side-only version it replaced was decoration |
| Translate-all | `app-modal`, `translateAllOpen()` | modal | Checkbox per language, sequential calls, cost shown up front | Present — per-language spinner | Whatever stays ticked after a failure is what is left to do |
| Appearance status line | Appearance modal, where the Apply button was | panel | saved / saving / could not save | Is itself the loading state | ADR-069. The button was real but read as decoration next to a live preview |
| Long-answer field | `.reg-textarea`, `data-question` | — | Multi-line answer on a registration form (T-031) | N/A | Same `data-question` contract as the single-line input |
| Static block | `.reg-static` | — | Text and/or image the reader only reads (T-032) | N/A | Carries **no** `data-question` — a block with one would submit an empty answer for a question nobody was asked |
| Consent field | `.reg-consent`, `data-question-consent` | — | Statement plus a required checkbox (ADR-060) | N/A | Separate attribute because the script reads `.checked`, not `.value` |
| Guest guard | `core/guest.guard.ts` | — | A live session is redirected away from `/login` and `/register` | N/A | v0.9.17. A server that doesn't answer falls through to the login page — unknown is not the same as proven |

## Icon inventory

**Superseded 01.08.2026 by `/dev/icons`** — the live inventory is now generated from the call sites by `npm run icons:generate` (`tools/generate-icon-usage.mjs` → `icon-usage.generated.ts`, ADR-075) and rendered on that page: every icon, what it means here, where it is drawn, and any meaning drawn twice. Prefer it over the table below, which is a snapshot of the pre-migration state kept for the record.

Numbers as of 01.08.2026: **80 icons, 206 call sites, 0 unused, 0 bound at runtime.** All 58 icon-only controls now carry an `aria-label` beside their `title` (T-080), and the four screens are asserted for it by `e2e/12-a11y.spec.ts`. No meaning is drawn with two icons; `x` (12 labels), `trash` (5) and `plus` (5) remain overloaded, which is what universal actions do.

The 30.07.2026 snapshot, measured against the templates before T-079/T-080, retained because it is what the two tasks were scoped from:

| Fact | Number | What it means for Phase 11 |
|---|---|---|
| Distinct icons in use | 79 | All from `@lucide/angular` (ISC). Q-12 decides whether this set stays |
| Total icon usages in templates | 203 | |
| Icon controls with no visible text, `title` **or** `aria-label` | **0** | Better than assumed — every icon control carries a `title`. The gap is not "unlabelled": `title` never appears on touch, so on iPad/iPhone (T-034) those controls are unlabelled in practice |
| `aria-label` attributes in the whole app | **1** | Labelling rests entirely on `title`, which browsers also expose as a last-resort accessible name — adequate on a desktop, absent on mobile |
| Non-set glyphs used as icons | 10 kinds | `☾`/`☀` (theme toggle, 5 files), `👍`/`👎` (comments), `✦` (AI), `◷` (version history), `⤢` (reset column widths), `★`, `¶` (paragraph marks), `⏰` (scheduled), plus `☰`/`↑` on the blog's floating nav. Each renders in the OS emoji font and so follows neither the icon set's weight nor its colour — the same class of problem as the flag emoji DB3.1 already removed for not existing on Windows |
| `.icon` size definitions | **9 files, three different values** — 15px (×6), 18px (glossary), 20px (folder-picker, tag-picker) | Angular view encapsulation makes each component redefine the class; nothing keeps them equal, and they already are not. This is the concrete case for `--icon-sm/md/lg` |
| `.icon-sm` size definitions | 10 files: 14px (×9), 13px (comments) | Same cause |
| `.icon-xs` size definitions | 7 files, 12px everywhere | Consistent today — by luck, not by construction |

Icons appearing in more than one component (`Trash2`, `X`, `Plus`, `Pencil`, `RefreshCw`, `Archive`/`ArchiveRestore`, `Folder`, `Settings`, `Newspaper`, `BookMarked`, `ShieldCheck`, `Terminal`, `Send`, `List`/`LayoutGrid`) are used **consistently** — the same glyph means the same thing on every screen, and no duplicate meanings were found. `RefreshCw` carries two roles (busy spinner with `.spin`, refresh action without), which is conventional and not worth splitting.

## Development surfaces (`/dev/*`, authGuard'd, lazy)

Neither is a product screen: both are excluded from localization on purpose, and neither is in the initial bundle (ADR-076).

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Styleguide | `pages/styleguide.component.{ts,html,css}` → `/dev/styleguide` | page | Every token and control state on one screen, with live theme and density toggles (T-078) | n/a — no async | Captured by the audit run into `.e2e-audit/70…73`, all four theme×density combinations |
| Pseudo-locale toggle | `styleguide.component.html`, `.sg-controls` | button | Switches the whole app in this browser onto the inflated long-word strings (T-051) | n/a | Per-browser (`localStorage: cedar-pseudo`), never reaches the profile. `?pseudo=1` on any URL does the same |
| Icon inventory | `pages/icons.component.{ts,html,css}` → `/dev/icons` | page | What each icon means in this app, from the generated call-site table: duplicates of meaning, overloaded glyphs, unused icons, full grid with search and weight/size toggles (T-080) | n/a — generated data, no fetch | Regenerate with `npm run icons:generate` after moving icons, or it describes the previous commit. Captured as `.e2e-audit/74` |

## Screens and flows never opened in a browser

Cross-referenced with `TASKS.md`. Note that smoke coverage is a different claim from "a person looked at it and it was right" — it says the flow works, not that it reads well.

- **Covered mechanically as of 30.07.2026** (Phase 10 Block B): save guards (refusal and restore), version history, the private-post gate end to end, blog reactions and comments, the admin gate, the Posts Manager list, the drafts round-trip.
- **Still seen by nobody**: flush-on-hide on a real iPhone (cannot be staged on a desktop — the 29.07 incident was iOS), incremental re-translation preserving manual corrections, the uk/be/ka provider refusal, the translate-all modal, the two new form field types on a real gate, the Posts Manager submission modal and "mark all as read", the Appearance panel's autosave, the FI2 export rebuild, the FI3 pickers and folder delete, the FI4 forms editor and per-language gate, tag rename/delete, article title, audit paging, the emoji panel, the paragraph-mark toggle, the glossary tooltip on a real published post, per-language cross-links, a semi-public post on the blog index, and Phase 8's Steps 6 and 7 (tags in the Telegram export, comment replies/highlight/name reservation).
