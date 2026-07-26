# UI Inventory

Per-element inventory of the frontend UI — what exists, where it lives, what it does, and whether it needs (and has) a loading indicator. Complements `docs/DESIGN.md` (which covers tokens/CSS patterns, not individual elements). Update this file whenever a UI element is added, removed, or meaningfully changed — that's the point of it: a future session should be able to scan a page's table and answer "does this popup need a loading indicator, and does it have one?" without re-reading the component.

## Format

One table per page/component. Columns:

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
| Export button + Export modal | topbar `.export-trigger` button → `app-modal` (moved out of `<header>` 25.07.2026, see Bug 3 below) | button + modal | Channels (moved here from the topbar, B21), a checkbox per destination gating its settings (B5), blog publish/unpublish, Telegram schedule, privacy + invite list, registration-form editor and submissions (B3), `.cedar`/static-HTML export, per-draft asset list with total size. **One Publish button** at the bottom fires every ticked destination | Needed & present — `exporting()`/`blogBusy()`/`scheduling()`/`publishingAll()` each show a spin icon + `.inline-progress` indeterminate bar | By far the largest modal in the app and still growing; was mis-positioned near the top of the screen until the Bug 3 fix (25.07.2026) |
| Theme toggle | topbar `.theme-toggle` button | button | Switch light/dark theme | N/A — instant, `ThemeService` | |
| Account popover | topbar, avatar/email trigger | popover | Show account email, link to Settings, log out | N/A — instant actions | |
| Block-type dropdown | toolbar row 1, `.block-dropdown` | dropdown | Paragraph / Heading 1–6 | N/A | One of the popups broken by the Bug 2 `backdrop-filter` regression (fixed 25.07.2026) |
| Undo/Redo | toolbar row 1 | button | TipTap history | N/A | |
| Text group (`tplText`) | toolbar, movable via Settings → Toolbar | buttons | Bold/italic/underline/strike/spoiler | N/A | |
| Insert group (`tplInsert`) | toolbar, movable | dropdown/popovers | Insert modal (link/YouTube/email/phone/mention), emoji popover, date/time popover, footnote popover | N/A — instant inserts | |
| Lists group (`tplLists`) | toolbar, movable | buttons | Bullet/numbered/task list, indent/outdent | N/A | |
| Code group (`tplCode`) | toolbar, movable | buttons | Inline code, code block | N/A | |
| Media group (`tplMedia`) | toolbar, movable | buttons + file pickers | Image/video/GIF/audio/carousel/collage upload, YouTube insert | Needed & present — see Upload-progress panel below | |
| Blocks group (`tplBlocks`) | toolbar, movable | buttons/popovers | Table insert/row/col ops, formula (inline/block), blockquote, toggle block, table of contents, divider, annotation anchor | N/A | |
| AI actions popover | toolbar, `.ai-chip` | popover | Fix errors / "schizo-izer" rewrite (Pro Plus gated) | Present but weak — only an elapsed-time counter, no real progress bar | Tracked as Phase 8 Step 8 in `docs/ROADMAP.md`; overlaps Backlog #6 and #14 (move AI features elsewhere) |
| "Customize toolbar" link | toolbar row 1, far right | link | Jumps to Settings → Toolbar customization | N/A | |
| Upload-progress panel | editor sheet area | panel | Per-file upload progress bars for media inserts | Present | |
| AI-confirm modal | `app-modal`, `cancelAiConfirm()` | modal | Confirm before running an AI edit (replaces old `window.confirm()`) | See AI actions popover above | |
| New-draft dialog | `app-modal`, `closeNewDraftDialog()` | modal | Title, languages, tags, template, target folder, and a "private" checkbox for a new draft. Folder/private are applied as follow-up calls right after creation (the create endpoint doesn't take them) and, unlike languages/tags/template, are **not** remembered in `newDraftDefaultsJson` — they're per-draft intent, not a preference | Needed & present — `draftsBusy()` drives the "Creating…" button state | Folder shown as a pill row, not the editor's folder popover: a nested `app-popover` inside `app-modal` fights the modal's own fixed positioning |
| Insert modal | `app-modal`, `closeInsertModal()` | modal | Link/YouTube/email/phone/mention insert form | N/A | |
| ~~Delete-draft confirm~~ | — | — | **Removed 26.07.2026** along with the drafts popover that was its only trigger — deletion now lives solely on `/drafts` (which has its own confirm modal) | — | |
| Re-translate confirm | `app-modal`, `cancelTranslateConfirm()` | modal | Confirm overwriting an existing EN translation | N/A | |
| AI success toast | editor page | toast | Confirms an AI op finished | N/A | |
| Lang tabs (RU/EN) | editor sheet header | tab | Switch which language's content is being edited; flushes autosave on switch | N/A | Re-translate/delete-EN buttons and a stale-translation indicator live alongside |
| English-empty-state panel | editor sheet (EN tab, no content yet) | panel | Offers auto-translate / copy-from-RU / start-empty | Present — has its own progress bar during auto-translate | |
| Tag row | below editor header | chips + popover | Shows/removes tags on the draft; "+ Tag" popover has a tag-cloud (most-used/other) + new-tag input | N/A | See Backlog #7 (spread this pattern elsewhere) and #8 (blog card truncation) |
| Editor sheet | `.sheet`, TipTap host | panel | The actual rich-text canvas; ruler + RU-diff gutter markers alongside | N/A | |
| Status bar | bottom of `.app`, `.status-bar` | panel | Zoom controls, word/char count, sync indicator (saved/saving/error+retry) | The sync indicator itself *is* a loading/status indicator | Was overlapped by the debug-console tab until the fix below (25.07.2026) |

---

## Shared components (`cedarclerk-web/src/app/shared/`)

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| `app-modal` | `modal.component.ts` | modal shell | Generic overlay + card used by every dialog in the app | N/A (shell only — content decides) | `.modal-overlay` uses `position: fixed; inset: 0` — must not be nested inside an ancestor with `backdrop-filter`/`transform`/`filter` (see Bug 3, 25.07.2026) |
| `app-popover` | `popover.component.ts` | popover shell | Generic trigger+panel popover used by every dropdown/popover in the app | N/A | Panel uses `position: fixed` deliberately, to escape `overflow` clipping on the trigger's ancestors — same containing-block caveat as `app-modal` (see Bug 2, 25.07.2026) |
| `app-debug-console` | `debug-console.component.ts`, mounted in the root app shell behind `showDebugConsole()` | floating tab + panel | Dev tool: inspect in-flight/failed API requests without SSH-ing into the Pi | N/A (dev tool) | Fixed `bottom:0` overlay, always on top (`z-index:200`); closed-tab sits 27px above the true bottom edge (`margin-bottom`) so it clears the editor's status bar (fixed 25.07.2026). **Hidden on public routes** (`/login`, `/register`, `/terms`, `/privacy`) since 26.07.2026 — it reports the signed-in owner's own API traffic, so it's meaningless (and visually intrusive) on pages reachable without an account (`app.ts`'s `PUBLIC_ROUTES`) |
| `cedar-logo` | `cedar-logo.component.ts` | decorative | Logo SVG, used in topbar/auth pages | N/A | |
| `legal-page` | `legal-page.component.ts` | layout wrapper | Shared frame (logo/title/back-link/prose styling) for Terms/Privacy | N/A | |

---

## `drafts.component` (`cedarclerk-web/src/app/pages/drafts.component.{ts,html}`)

Full-page drafts grid/table — the compact editor drafts popover's bigger sibling.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| ~~Back-to-editor link~~ | — | — | **Removed 26.07.2026** — `/drafts` became the post-login landing screen, so there is nothing to go "back" to. The editor is reached by opening a draft or "New draft" | — | |
| Theme toggle | `:11` | button | Light/dark switch | N/A | Same pattern on every page's header |
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
| Anchor chip nav | `:18-26` | chip-row | Jumps to each section | N/A | 7 chips |
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
| Telegram link/unlink row | `:412-441` | panel | `linkTelegram()` / inline unlink confirm | Needed & present — `telegramBusy()` ("Waiting for Telegram…") | Unlink uses an inline "Sure?" chip, not a modal |
| Bot status row | `:445-459` | panel | Reachable/unreachable status dot + external link | N/A | Read-only |
| Channels row | `:461-472` | panel | Connected-channel summary, links to editor | N/A | Actual channel CRUD lives in the editor's Channels popover |

**Known gap**: nearly every "instant save, no Save button" preference control in Appearance/Toolbar has no loading indicator at all — only an error message on failure, nothing while the request is in flight. Worth fixing alongside Backlog idea #6 (loading indicators for long operations) if that gets scoped.

## `stats.component` (`cedarclerk-web/src/app/pages/stats.component.{ts,html}`)

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Channel tabs (Blog + per-channel) | `:22-30` | tab | `selectBlog()`/`selectChannel(id)` switches data source | **Needed but missing** — no busy signal on the fetch, only page-level `loading()` on initial load | Rapid re-clicking could race — no guard |
| Date-range tabs (30/90/180d) | `:34-36` | tab | `selectRange(days)` re-fetches at new range | Same gap as above | |
| Metric stat cards (Subscribers/Views/Likes/Comments) | `:41-84` | panel | Current value + week-over-week delta | Needed & present — page-level `loading()` gates first render | Blog view omits Subscribers |
| Line/area chart + hover tooltip | `:53-79` | panel/popover | SVG sparkline, crosshair tooltip on hover | N/A | Falls back to "Not enough history yet" under 2 snapshots |

## `comments.component` (`cedarclerk-web/src/app/pages/comments.component.{ts,html}`)

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
