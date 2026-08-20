---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: поэлементный инвентарь фронтенд-UI
guard: UiInventoryDriftTests + PreToolUse-хук ui-inventory-reminder
---

# UI Inventory

Per-element inventory of the frontend UI — what exists, where it lives, what it does, and whether it needs (and has) a loading indicator. Complements `docs/design/DESIGN.md` (which covers tokens/CSS patterns, not individual elements). Update this file whenever a UI element is added, removed, or meaningfully changed — that's the point of it: a future session should be able to scan a page's table and answer "does this popup need a loading indicator, and does it have one?" without re-reading the component.

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

The main writing surface — by far the most complex page. Topbar + two toolbar rows + editor sheet, plus several modals/popovers layered on top. The topbar carries only what belongs to the open draft — its title, its save state and Export; brand, navigation, theme, Appearance and the account belong to the shell rail (ADR-139), and the way back to the draft list is the Text hook.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Import `.cedar` | topbar, `.icon-btn` + hidden `#cedarInput` | button | Import a `.cedar` package as a new draft; moved here from the removed popover | Needed & present — spin icon via `importingCedar()`; errors surface as a dismissible toast (`.ai-toast.error`) since the topbar button has no room for text | Markdown (`.zip`) import moved to `/drafts` instead |
| Download `.cedar` | topbar, `.icon-btn.cedar-download` | link | Downloads the open draft as `.cedar`; only rendered when a draft is open | N/A | Hidden below 768px — the same action exists in the Export modal, which is reachable on mobile |
| Channels popover | topbar, right side | popover | Connect/select Telegram channels the bot knows about; per-channel sparkline stats | Not verified whether connect-flow has a spinner | Follow up if revisited |
| Export button + Export modal | topbar `.export-trigger` button → `app-modal` | button + modal | **Restructured 07.08.2026 (ADR-096)** into three numbered steps — **1 Version** (language checkboxes for the whole window, plus a one-line note naming the languages with no version yet), **2 Where to publish** (`.dest-card` per destination: Blog, Telegram, and one per connected short-post network; an unconnected network keeps its card, un-tickable, linking to Settings → Integrations), **3 Settings for each destination** (a panel per ticked destination only). Blog panel: live URL + per-language badges, privacy/listed/copy-protection, registration form. Telegram panel: capability warnings, **a channel picker per ticked version** (`.channel-lang-row`, ADR-098 — one row each, language badge shown only when more than one version is ticked, and a one-line note naming the versions still without a channel), thread offer with its part list, photo compression. X/Bluesky/Discord panel: **language pills** choosing which versions this account posts (`.lang-pick-row`, ADR-100, shown only when more than one version is ticked) plus a two-way **mode toggle** (`.mode-toggle`) — announcement+link (per-language text field with the network's own counter) or the whole post as a thread (part count, and credit cost on X) — never both sets of fields at once; the thread half is disabled while a send time is set, and for Discord the whole toggle is absent (webhooks have no threads, ADR-131 — the panel is link-mode only). Then **4 When to send** (ADR-099 — the schedule moved out of the Telegram panel into its own step and applies to every ticked network; rendered only when a schedulable destination is ticked, with quick presets, a `datetime-local` field, the other-timezones hint, and notes that threads are not scheduled and the blog publishes immediately), **Private access** (invitations + watermark, shown whenever the post is private, not gated behind ticking the blog) and **Files** (`.cedar`/`.zip`/per-draft asset list). **One Publish button** fires every ticked destination, X and Bluesky included — their own per-section Publish buttons are gone. Connecting anything moved to Settings (ADR-095) | Needed & present — `blogBusy()`/`exporting()`/`publishingAll()` spin icons + `.inline-progress`; the publish checklist below is the real progress surface | Was a two-row column layout (`.export-row-primary`/`.export-row-utility`) with the language buried inside Telegram and two competing publish models |
| Publish progress checklist | `editor.component.html`, `.publish-run` inside its own `app-modal` (opened by `publishAllConfirmed()`) | modal | Watches a publication like a test run (01.08.2026): one row per phase (save / blog / each Telegram language / schedule / each short-post network per language, ADR-096) with waiting → spinner → ✓/✗ statuses; a thread (T-106) unfolds into numbered part chips (`.pr-part`) that fill green as parts send; an error is pinned to the failed step and it is the **first** failed part's error — the root cause, not the "held back" cascade; success rows carry the post link; summary line at the bottom | IS the loading state — live statuses fed by `awaitJobs()`'s per-poll callback | Stacks over the Export modal; closing it cancels nothing (the queue owns the jobs, T-090). Escape closes both stacked modals — pre-existing `app-modal` behavior |
| Block-type dropdown | toolbar row 1, `.block-dropdown` | dropdown | Paragraph / Heading 1–6 | N/A | One of the popups broken by the Bug 2 `backdrop-filter` regression (fixed 25.07.2026) |
| Undo/Redo | toolbar row 1 | button | TipTap history | N/A | |
| Text group (`tplText`) | toolbar, movable via Settings → Toolbar | buttons | Bold/italic/underline/strike/spoiler | N/A | |
| Insert group (`tplInsert`) | toolbar, movable | dropdown/popovers | Insert modal (link/YouTube/email/phone/mention), emoji popover, date/time popover, footnote popover | N/A — instant inserts | Emoji popover gained Flags (50 country/generic flags) + Flag sequences (01.08.2026) — multi-emoji colour runs (⚪️🔴⚪️, 🤍💙❤️, …) for flags Unicode never encoded, rendered as wide pills (`.emoji-grid-wide`). Windows caveat (same fact as DB3.1): country flags render as letter pairs in the editor on Windows — no regional-indicator glyphs in Segoe — but correctly in Telegram and on readers' phones; the sequences render everywhere |
| Lists group (`tplLists`) | toolbar, movable | buttons | Bullet/numbered/task list, indent/outdent | N/A | |
| Code group (`tplCode`) | toolbar, movable | buttons | Inline code, code block | N/A | |
| Media group (`tplMedia`) | toolbar, movable | buttons + file pickers | Image/video/GIF/audio/carousel/collage upload, YouTube insert | Needed & present — see Upload-progress panel below | |
| Blocks group (`tplBlocks`) | toolbar, movable | buttons/popovers | Table insert/row/col ops, formula (inline/block), blockquote, toggle block, table of contents, divider, annotation anchor | N/A | |
| AI actions popover | toolbar, `.ai-chip` | popover | Fix errors / "schizo-izer" rewrite (Pro Plus gated) | Present — asymptotic pseudo-progress % + elapsed time, 3-min timeout, real Cancel (ADR-038, `pseudo-progress.util.ts`; this cell said "elapsed-time only" until 18.08 — stale since 26.07) | What the popover *is* remains Q-7 (T-048); overlaps Backlog #6 and #14 (move AI features elsewhere) |
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
| Series button | beside the folder button, `app-series-picker` `[compact]` | button + popover | Assigns the draft to a series (ADR-125); also creates, renames and deletes series | N/A | Mirror of the folder picker, backed by `SeriesService`. Attaching appends to the series' end (`SeriesOrder = max+1`); renaming keeps the slug — the URL is a promise readers hold |
| Tree breadcrumbs | `.doc-crumbs` before the tag row | link row | ADR-128 — the open document's ancestor chain; a crumb opens that document directly (`openDraft`) | N/A | Built from the already-loaded drafts list, no extra request; hidden for root documents |
| Wiki-link chip | `.wikilink-chip` in the sheet (`wikilink` node view, `tiptap-extensions/wikilink-node.ts`) | inline chip | ADR-128 — an inline atom `{draftId, label}`; click opens the target document | N/A | Linked by id, so renaming the target never breaks the link; the label is a title snapshot from insert time |
| Wiki-link suggester | `.wiki-suggest`, trigger `[[` (`@tiptap/suggestion`) | popover | Filters the loaded drafts by title as you type; ↑/↓/Enter/Escape, click inserts | N/A | Hand-rolled fixed-position popover off `props.clientRect`; client-side filter only until T-176 brings server search |
| Backlinks chip | `.backlinks-chip` beside the series picker | button + popover | ADR-128 — «← N» opens the list of documents whose wikilinks point here; a row opens that document | N/A — chip hidden until the list arrives | Links are diff-synced server-side on every save of the primary language; the chip refreshes on draft open |
| Library button | toolbar Media group, after Collage | button + modal | ADR-127 — opens `app-media-picker` to insert an already-uploaded file without re-uploading | N/A | Toolbar id `library` (hide/show in Settings → Toolbar like its siblings); hidden in the Minimal preset |
| Paste/drop upload | `editorProps.handlePaste` / `handleDrop` on the sheet | behavior | ADR-127 — a screenshot pasted from the clipboard or files dropped onto the sheet upload through the same progress panel and insert as media; drop lands at the drop point | Present — the existing `.uploads` progress panel | Claimed only for the whitelisted upload types; plain text/HTML paste keeps default handling |
| Editor sheet | `.sheet`, TipTap host | panel | The actual rich-text canvas; ruler + RU-diff gutter markers alongside | N/A | |
| Invisibles toggle | toolbar row 1, after the closing `.sep` | button | Toggles `.show-invisibles` on the sheet so paragraph ends are drawn; display only, the document is untouched | N/A | Was a status-bar button until that bar dissolved (ADR-153); a toggle is a control and the rule below takes none, so it came to the tool strip |
| Fullscreen toggle | toolbar row 1, end of the strip | button | Real browser fullscreen rather than a CSS chrome-hiding mode, so Esc exits it | N/A | Moved off the status bar with the invisibles toggle |
| Retry save | toolbar row 1, `.tb-retry`, rendered only while `saveState() === 'error'` | button | The manual save that is left once autosave's widening backoff has given up | IS the error state — the button exists only in it | The one red button on the strip. Kept rather than dropped when the status bar dissolved: without it a draft whose retries are spent has no way to be saved (ADR-153) |

---

## Shared components (`cedarclerk-web/src/app/shared/`)

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| `app-modal` | `modal.component.ts` | modal shell | Generic overlay + card used by every dialog in the app | N/A (shell only — content decides) | `.modal-overlay` uses `position: fixed; inset: 0` — must not be nested inside an ancestor with `backdrop-filter`/`transform`/`filter` (see Bug 3, 25.07.2026) |
| `app-popover` | `popover.component.ts` | popover shell | Generic trigger+panel popover used by every dropdown/popover in the app | N/A | Panel uses `position: fixed` deliberately, to escape `overflow` clipping on the trigger's ancestors — same containing-block caveat as `app-modal` (see Bug 2, 25.07.2026) |
| `app-debug-console` | `debug-console.component.ts`, mounted in `app-bench-shell` | drawer body | Dev tool: inspect in-flight/failed API requests without SSH-ing into the server | N/A (dev tool) | Rows and a clear button, nothing else — the strip, the lip, the shut/open slide and the summary belong to `app-bench-drawer` (ADR-153). Open state lives in `DebugLogService.open` because the drawer is a controlled component; the lip is the only thing that opens it, so no page carries a toggle. Rows are built only while open. Mounted in `app-bench-shell`, so it is absent from `/login`, `/register`, `/terms` and `/privacy` by the shape of the route tree rather than by a URL list — it reports the signed-in owner's own API traffic, and there is no session to have on those four |
| `cedar-logo` | `cedar-logo.component.ts` | decorative | Logo SVG, used in topbar/auth pages | N/A | |
| `legal-page` | `legal-page.component.ts` | layout wrapper | Shared frame (logo/title/back-link/prose styling) for Terms/Privacy | N/A | |
| `app-account-menu` | `account-menu.component.ts` | popover | Avatar trigger → profile link, admin shortcut (if applicable), logout | N/A | Mounted once, in the rail's `[account]` slot (ADR-139 clause 5). Carries no navigation and no email beside the avatar — the wall does the first, and the popover itself shows the address |
| `app-appearance-panel` | `appearance-panel.component.{ts,html}`, mounted in `app-bench-shell` | modal | Theme, accent, sheet width, typeface, type scale, default table size, the five display checkboxes, and toolbar customization | **Needed but missing** on every control in it — see the known gap under the Settings table | Hoisted out of `editor.component` into the shell (ADR-151 clause 2): its trigger is an entry in the rail's dots menu, and a trigger in shared chrome cannot open a modal parented to one page. The control rows are tabled under `settings.component`, where the panel is reached from as well |
| `app-media-picker` | `media-picker.component.{ts,html,css}` | modal | ADR-127 — pick an already-uploaded file to insert into the editor: search, type chips, tile grid, paging | Present — `loading()` | Deliberately NOT a reuse of the `/media` page (no delete/usage here); emits `picked` and the editor maps content type → node (`image/gif` → `<video>`, same quirk as upload) |

---

## Bench kit (`cedarclerk-web/src/app/bench/`)

The design-system components (ADR-136…ADR-154), one folder per design-system group, no barrel. Every one is proven on `/dev/styleguide` first, which shows each in every variant, size and state on both surfaces. The `chrome/` group is past that point: `app-bench-shell` and the five pieces it assembles wrap every authenticated screen, so the styleguide is their second call site rather than their only one. Every component that is a surface spells `data-surface` (ADR-138) — chrome parts take 30px / 13–11px, paper parts 44px / 14px and up. The two brass pieces spell neither: hardware sits *on* a surface and is not one.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| `app-button` | `bench/forms/button.component.ts` | button | The one button: `pine` primary, `paper` secondary, `rail` on chrome, `danger` destructive; sizes `md`/`sm` | N/A — the caller owns busy state | Surface is read off the **variant**, not the size: `rail` is the only chrome one, so `sm` on paper tightens padding and can never take a control under the 44px floor. `danger` is a dashed-underlined word, never a filled red button. Every `box-shadow` is written `:not(:focus-visible)` so the ADR-140 halo is not out-specified |
| `app-input` | `bench/forms/input.component.ts` | field | Text/email/password/search/number/date/url/tel field with optional label and hint; `serif` for long form, `dense` for an inspector row | N/A | Full `ControlValueAccessor`, so `[(ngModel)]` and reactive forms both bind it; `type="number"` emits `number \| null`, matching Angular's own accessor. Generates its own id when the caller supplies none, so `<label for>` is always bound. **No error state** — the design system's field has none either, so a validating form has nowhere to put the message |
| `app-stamp-badge` | `bench/display/stamp-badge.component.ts` | status | What state a thing is **in**: tones `pine`/`brass`/`rust`/`ink` | N/A | Never acts — no output, no role, no tab stop, so it cannot quietly become a button. Each tone sits on a wash token mixed from its own ink, never a tint hand-mixed in the rule (ADR-145): `pine`→`--asoft`, `rust`→`--danger-soft`, `brass`→`--brass-soft`. `ink` takes `--t2` on no wash at all, because a stamp carries a word and content does not sit on `--t3` |
| `app-resin-drop` | `bench/display/resin-drop.component.ts` | status | The **only** save indicator in the app: `forming` (unsaved) → `set` (saved) | It *is* the indicator | No spinner, no toast, no busy state — the union is two values — and never on a button. `role="status"`, which is what removes the toast. Both animations are measured in `calc(var(--motion-slow) * N)` so the global reduced-motion collapse reaches them |
| `app-leaf-tag` | `bench/display/leaf-tag.component.ts` | tag / filter | Labels a thing (a topic) and filters a thing (a stats source); `active`/`idle`/`dried`, optional series `swatch`, optional remove | N/A | `dried` means no data or switched off, **never** an error — the component reaches for no `--danger` at all. `swatch` takes a token reference, never a literal (ADR-146). `removeLabel` has no default: the consumer owns the translation |
| `app-paper-card` | `bench/display/paper-card.component.ts` | surface | The sheet ink lies on; `deckled` tears the bottom edge, `bright` for a sheet being written on, `interactive` adds the lift | N/A | Never glassy, never blurred, never a large radius; the whole hover language is a 2–3px lift. A clipped box casts no box-shadow, so the drop is a `filter` on a wrapper. Takes a `[pin]` slot for `app-brass-pin` |
| `app-task-tag` | `bench/display/task-tag.component.ts` | tag | A task anywhere in the product: priority chip, due date, optional stamp, hangs from a hook | N/A | Only the **date** turns rust when overdue — never the tag, never the rail. `done` fades to 62% rather than striking the text through. Slots: `[hook]`, `[stamp]` |
| `app-spec-row` | `bench/worktop/spec-row.component.ts` | inspector row | One ruled line of a spec sheet: label in sans, value in mono, `field` for editable, `warn` for missing/wrong | N/A | Every row declares `scope` (`selection`\|`document`), because the inspector may never describe both subjects at once — that makes a mixed sheet one query instead of a convention nobody can check |
| `app-bench-shell` | `bench/chrome/bench-shell.component.ts` | shell | The parent route every authenticated screen renders inside: hook rail, rail header, the paper ground the page stands on, and the drawer + rule along the bottom. Owns the route-prefix table that lights a hook and the crumb table that names the screen | N/A — the child route owns its own | Declares `data-surface="paper"` on the ground, which is what ADR-138 item 5's touch carve-out keys on. `/login`, `/register`, `/terms` and `/privacy` are outside it (ADR-139 clause 2). No width breakpoint of its own — narrow screens are commissioned, not invented (ADR-147) |
| `app-rail-header` | `bench/chrome/rail-header.component.ts` | header (chrome) | The one sign board: brand, version, project switcher tile, breadcrumb, then the screen's save state and primary action, the dots menu and the account | N/A | Ink on the rail is always `--rail-ink`; the soft cream is spent on the crumb separator and nothing that is read. The `[menu]` panel is `[hidden]`, never `@if` — the controls in it own state. Replaces `app-page-header` and the editor topbar's nav row. The dots menu holds the theme toggle and the Appearance trigger (ADR-151), Glossary, Admin and `/dev/*` |
| `app-hook-rail` | `bench/chrome/hook-rail.component.ts` | navigation (chrome) | The tool wall: how you move between screens. Hub, Text, Board, Assets, Metrics, and Settings anchored at the bottom | N/A | Every hook is an `<a routerLink>`, so middle-click works; the current one is marked three ways and only one of them is colour. 5–7 tools, warned in dev mode past 7. Hub and Board render only for `auth.indieDev()` |
| `app-shelf-panel` | `bench/chrome/shelf-panel.component.ts` | panel (chrome header + paper sheet) | A board on the wall carrying one sheet; the panel's own commands live in its `[actions]` slot and nowhere else | N/A — the sheet's content owns it | Refuses to nest inside another panel (throws), because one board carries one sheet |
| `app-index-tabs` | `bench/chrome/index-tabs.component.ts` | tabs (chrome) | Switches what a panel or drawer **shows**; a badge counts what is behind each tile | N/A | Never navigation — a tile is a `<button>` and the only thing that leaves the component is an id, so it cannot become a router link. Exports `indexTabBadgeLabel`, where `app-count-badge`'s nothing-at-zero and `99+` rules now live |
| `app-bench-drawer` | `bench/chrome/bench-drawer.component.ts` | drawer (chrome) | The 32px lip under the bench that pulls out to 172px of ruled paper: journal, checks, build output | N/A | The lip **is** the toggle, and the summary on it is what justifies leaving it shut. Open is a height, not a mount — the journal stays in the tree and carries `inert` while closed |
| `app-ruler-bar` | `bench/chrome/ruler-bar.component.ts` | status strip (chrome) | The milled rule along the bottom edge: read-only counts and state words, fed by `RulerService` | N/A | Readouts are strings, not projected content — an `ng-content` here would be an invitation to hang a build button off the rule. Every numeral is mono and tabular. It is what the editor's status bar became: the word count, the character count and the sync word are published here by `editor.component` (behind the same `showWordCount` preference for the two counts, ADR-153), and the bar is blank on every screen that publishes nothing yet |
| `app-worktop` | `bench/worktop/worktop.component.ts` | surface | The bench top a screen's work lies on: pencil grid under the content, a lamp wash from one corner, a dashed edge strip carrying two chalked labels | N/A | One per screen, warned in dev mode on the second. The frame is chrome; the body restates `data-surface="paper"` |
| `app-brass-pin` / `app-brass-hook` | `bench/scenery/{brass-pin,brass-hook}.component.ts` | decorative hardware | The pin holds down what **lies** on a surface; the hook carries what **hangs** | N/A | Deliberately not interchangeable — different sizes, different APIs — because swapping them breaks the workshop's physical logic. Neither takes a colour or a size input: brass is hardware, not a palette. The pin is `aria-hidden` until given a `label`; the hook is never named |

---

## `drafts.component` (`cedarclerk-web/src/app/pages/drafts.component.{ts,html}`)

Full-page drafts grid/table — the compact editor drafts popover's bigger sibling.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Search input | `.search-input` | input | Client-side filter by title/tag | N/A | Plain string, not a signal. *(This section's bare `:NN` line anchors had drifted ~30–60 lines by 18.08.2026 — replaced with selectors, which the same audit showed do not rot)* |
| View toggle (table/grid/tree) | `.view-toggle` | tab | Switches `view()` layout | N/A | Third state `tree` added by ADR-128 (icon `tree-structure`) |
| Tree view | `.drafts-tree`, `.tree-row` | panel | ADR-128 — the document hierarchy: indent by depth, expand/collapse carets (client-side `collapsed` Set), click opens the draft | N/A — page-level `loading()` | Status tabs and the folder filter step aside in this view — a filtered-out parent would hide its visible subtree. Search stays and keeps ancestor paths. Per-row: ↑/↓ glyph buttons reorder among siblings, the `tree-structure` popover moves under a new parent («Верхний уровень» = root); server enforces the cycle guard and depth ≤ 10 |
| New draft button | toolbar `.btn-accent` | button | Nav to `/editor?new=1` | N/A | |
| Import Markdown (`.zip`) | toolbar, `.btn-ghost` + hidden `#markdownInput` | button | Import a Notion-shaped Markdown zip as a new draft. **Moved here 26.07.2026** from the editor's removed drafts popover | Needed & present — spin icon via `importingMarkdown()`; unmatched-image warnings and errors render as inline `.error-box` lines under the toolbar | On success with no warnings it navigates straight into the new draft; with warnings it stays put so the message is readable |
| Filter tabs (All/Drafts/Scheduled/Published/Needs attention/Archived) | `.filter-tabs` | tab | Sets `filter()`, live counts via `filterCount()` | N/A | |
| Column sort + width resize | `.col-head` buttons + `.col-resize` handles in the table header; `.col-reset` (⤢) resets widths | button | Sort by any column with direction marks (`sortBy()`/`sortMark()`); drag column widths (`startColResize()`); both persisted in `localStorage` | N/A | N1/ADR-049 (27.07.2026); **row added only 18.08.2026** — the audit's one stale finding in a 10-row sample. Title keeps the leftover space so a resize can't reintroduce the horizontal scroll B24 fixed |
| Draft rows (table/grid) | `.drafts-table` rows / grid cards | panel | Click opens draft; status badge, lang badges, folder, tags, updated date, and a 🔒 lock icon on private drafts | Needed & present — page-level `loading()` | The private lock sits inside the Title cell in both views rather than as its own column — no `grid-template-columns` change needed, and it works identically in the grid cards |
| Activity cell (per row) | table + grid card, `.activity-cell` | panel | Blog views and reactions (likes + dislikes combined), each with a `+N` accent chip for what accumulated since the previous session (B23, ADR-043). Renders `—` for drafts that were never blog-published | Needed & present — page-level `loading()` | The delta comes from the server (`DraftStatSeen` baseline, 30-min session gap), not from `localStorage`, so it matches across devices. No sparkline: no per-draft stats history exists to draw one from |
| Archive/unarchive button (per row) | per-row icon button, table + grid | button | `toggleArchive()` | Needed & present — `busyId()===d.id` spins the icon (table view only; grid view swaps icon without spinning — minor inconsistency) | |
| Delete button (per row) | per-row icon button, table + grid | button | Opens delete-confirm modal | Needed & present — disabled while `busyId()` set | |
| Delete confirm modal | `app-modal` → `confirmDelete()` | modal | Cancel/Delete | Needed & present | |
| Error banner | `.error-box` under the toolbar | toast (inline) | Surfaces list/archive/delete failures | N/A | Not dismissible |

## `settings.component` (`cedarclerk-web/src/app/pages/settings.component.{ts,html}`)

One long page with anchor-nav. The sections, by the `id` the nav jumps to — **a new control belongs in one of these, and adding an eighth place for an existing concern is the mistake `.claude/rules/ui-changes.md` exists to stop**: `sec-profile` (profile + signature), `sec-language` (interface language), `sec-header-slots`, `sec-social-links`, `sec-subscription` (plan, payment method), `sec-credits`, `sec-cross-links`, `sec-integrations` (Telegram, Bluesky, X, planned networks). Appearance and toolbar customization sit in `app-appearance-panel`, which the shell mounts (ADR-151) rather than this page — the rows below table its controls because Settings is the other place it is opened from.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Anchor chip nav | `:18-26` | chip-row | Jumps to each section | N/A | 8 chips (Credits added 05.08.2026) |
| Post signature + URL fields | `:48-56` | panel + button | Pro-gated custom signature | Needed & present — `signatureBusy()`/`signatureSaved()` | Free users see static attribution instead |
| Theme mode toggle (Light / Dark) | Appearance modal, top of the panel (`appearance-panel.component.html:7-13`) | tab | Sets `data-theme` on `<html>` — device-local via `ThemeService`, localStorage `cedar-theme`; the accent swatches below edit whichever theme it leaves you on | N/A — instant | The only styling axis — one look, nothing else to select (ADR-136) |
| Accent preset swatches | Appearance modal (`appearance-panel.component.html:15-21`) | chip-row | `pickAccentPreset()`, saves instantly | **Needed but missing** — fire-and-forget save, only `appearanceError()` on failure | "Applies instantly" by design, but a failed save is silent otherwise. Each preset carries a day tone and a darker night one (ADR-141) and the swatch paints the active theme's |
| Sheet width / Typeface toggles | Appearance modal (`appearance-panel.component.html:24-43`) | tab | Instant-save prefs | **Needed but missing** (same gap as above) | |
| Font size / line height sliders | Appearance modal (`appearance-panel.component.html:45-57`) | slider | Instant-save on every drag tick, no debounce | **Needed but missing** | |
| Default table size (rows × cols) | Appearance modal (`appearance-panel.component.html:60-69`) | number inputs | The size Insert → Table starts at | **Needed but missing** (same gap as above) | Clamped to `MAX_TABLE_SIZE` (10) whatever is typed — a Telegram Blocks message has to carry every cell |
| Appearance checkboxes (ruler/paragraph numbers/word count/focus mode/sheet flush) | Appearance modal (`appearance-panel.component.html:71-77`) | chip-row | 5 instant-save booleans | **Needed but missing** | |
| Toolbar preset toggle (Minimal/Standard/Everything) | Appearance modal (`appearance-panel.component.html:91-98`) | tab | Instant-save | **Needed but missing** — `toolbarError()` shown, no busy state | |
| Toolbar row 1/2 drag lists | Appearance modal (`appearance-panel.component.html:101-121`) | panel (drag-drop) | CDK drag-drop moves groups between rows | **Needed but missing** | |
| Toolbar group/button visibility checkboxes | Appearance modal (`appearance-panel.component.html:123-139`) | chip-row | Show/hide groups or individual buttons | **Needed but missing** | |
| Reset-to-Standard button | Appearance modal (`appearance-panel.component.html:141`) | button | `pickToolbarPreset('standard')` | **Needed but missing** | |
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
| Discord block | `sec-integrations`, `.integration-block` | panel | **Added 18.08.2026 (ADR-131, T-161)** — a webhook-URL field + Connect (verified against Discord before it is stored encrypted; the URL is the credential and is never shown back), connected webhook name with last-published date, Disconnect | Needed & present — `discordBusy()` | The hint explains where Discord issues a webhook (channel settings → Integrations) |
| Planned networks strip | `sec-integrations`, `.integration-block.dashed` | chip-row | Threads/Facebook/Medium/Patreon/Notion/Google Docs, named rather than hidden | N/A | Moved out of the Export modal's "coming soon" disclosure |

**Known gap**: nearly every "instant save, no Save button" preference control in Appearance/Toolbar has no loading indicator at all — only an error message on failure, nothing while the request is in flight. Worth fixing alongside Backlog idea #6 (loading indicators for long operations) if that gets scoped.

## `posts-manager.component` (`cedarclerk-web/src/app/pages/posts-manager.component.{ts,html}`)

The `/posts` page (N7, ADR-046). `/comments` and `/stats` now redirect here; the two components below are rendered as its tab bodies with their own page headers removed.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
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
| Range slider (7…180 days) | `stats.component.html:20-41` | slider | Free range magnetised to 7/14/30/60/90/180; the label follows the drag, the fetch fires on release (N9, ADR-049). The six notches carry readable day counts that are also click targets (I8) — unlabelled 1px ticks marked something without saying what | Same gap as above | Replaced the fixed 30/90/180 tabs. The kit's five fixed period segments are deliberately not ported (ADR-149) |
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

Thin wrappers around `shared/legal-page.component`, passing only `title`/`updated`. Static prose, no interactive elements — nothing to inventory beyond the shared shell above. **Filled in 13.08.2026 (`T-052`)**: every `[bracketed]` blank is gone, both carry `updated="12 August 2026"`, and the "draft template" banner was removed with them. The blog footer links here from every page, which is what made finishing them urgent.

---

## `glossary.component` (`cedarclerk-web/src/app/pages/glossary.component.{ts,html,css}`)

Idea #11. Terms the owner defines once, found and explained on the published blog. Reached from the rail's dots menu (ADR-151) — it was the nav row on the shared header until that header was retired with `app-page-header`.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
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

## `media-library.component` (`cedarclerk-web/src/app/pages/media-library.component.{ts,html,css}`)

T-177 (ADR-127) — the owner-wide library of uploaded files at `/library` (NOT `/media` — that prefix belongs to the uploaded files' own URLs, and the dev proxy forwards it wholesale to the backend), reached from the Assets hook on the rail (icon `images`, ADR-139), which is what the shared nav row became. Everything here IS uploaded bytes (unlike project-assets, which indexes paths on a machine); the anatomy deliberately mirrors project-assets so the two screens read as siblings.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Header + usage bar | `.media-toolbar`, `.usage-bar` | header | Title, «занято X из Y» line and a thin quota bar fed by the same `GET /api/assets` response | N/A — arrives with the list | No separate `/usage` call by design |
| Type chips | `.filter-tabs` | tab | All / Images / Videos / Audio with counts | N/A | Counts come from the **unfiltered** set — chips must not shrink while a filter is on |
| Search | `.search-input` | input | Filename search, debounced 250ms | N/A | Server-side `Contains` on `FileName` |
| Grid / list toggle | `.view-toggle` | button | Same control as project-assets; remembered in `localStorage` (`cedar.mediaView`) | N/A | |
| Tile / row | `.tile` / `.list-row` | button | Opens the asset modal; images preview from `/media/{path}` directly, video/audio show a kind icon | N/A — `loading()` covers the page | `thumbFailed` falls back to the honest «no preview» label, not a broken-image icon |
| Pager | `.pager` | button | skip/take paging, PAGE_SIZE 60 | N/A | |
| Asset modal | `app-modal`, `.asset-detail` | modal | Preview (img/video/audio), meta line, insert hint, and Delete | Present — `busy()` on Delete | Delete of a referenced file answers **409** and the modal lists the referencing posts (`.used-by`) instead of a bare refusal — ADR-127 |

---

# Phase 10 additions (30.07.2026)

Everything below was added when Phase 10 (ADR-070) audited this file against the Angular routing config and the real components. **The per-element tables above are unchanged and keep their original six columns** — verification status is tracked in the map below rather than by widening ten tables, so the two can be updated independently.

## Verification map — one row per surface

The `Verified` column is filled in by Phase 10 Block D. `smoke` means a Playwright scenario covers it (`cedarclerk-web/e2e/`, run via `Scripts/e2e.ps1`); `hand` means someone clicked through it in a browser; a defect ID points at a `docs/tasks/BACKLOG.md` row.

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
| `/projects/:id/planner` | `project-planner.component` | empty planner, empty sprint, collapsed vs expanded, load error, overdue inside a sprint | hand + API (11.08.2026) | Not yet in the smoke suite |
| `/projects/:id/tasks` | `project-tasks.component` | empty board, empty column, filtered-empty, load error, action error, overdue | hand + API (11.08.2026) | Both views and the card modal exercised against a running server; not yet in the smoke suite |
| `/terms`, `/privacy` | `legal-page.component` | — | hand (terms) | T-052 (placeholder text) |
| Blog index | `BlogEndpoints.RenderIndexAsync` | no posts, timeline, tag filter, semi-public lock | smoke + hand (timeline, semi-public lock with excerpt withheld) | T-094, T-099 |
| Blog post | `BlogEndpoints.RenderPostAsync` | not-translated notice, TOC, watermark, copy protection, floating nav, glossary tooltip, poll | smoke + hand (rendered post) | T-094 |
| Registration gate | `CedarToBlogHtmlRenderer.RegistrationFormHtml` | required-field validation, language switcher, consent, static block, long answer | smoke + hand (all four field types on a real gate) | T-099 |
| `/rss.xml` | `BlogEndpoints.RenderRssAsync` | empty feed, escaping | smoke | — |

Screenshots behind the `hand` marks are reproducible with `AUDIT=1 npx playwright test 99-audit` (output in `.e2e-audit/`, gitignored) — captured through Playwright rather than the browser extension, which proved unreliable here: screenshots timing out, zoom returning the wrong region, and keystrokes never reaching the TipTap surface.

**Still not verified by anything**, and each needs a person or a device rather than a script: flush-on-hide on a real iPhone; incremental re-translation preserving manual corrections (needs a provider key and a Pro Plus account); the uk/be/ka capability refusal; the Posts Manager submission modal and "mark all as read" (need a real submission); tag rename/delete; audit paging past the first page; the glossary tooltip on a published post; per-language cross-links; and whether the Russian wording reads well — which is Marty's call, not a script's.

### Added 11.08.2026 — where a post went (ADR-110)

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Published-elsewhere rows | `posts-manager.component.html`, inside the Destinations group, `@for (post of publishedElsewhere(d))` | panel | One row per network a post reached (X, Bluesky), with the link, the language, when it went and "thread of N messages" | None — folded into the page load, and a failure leaves the list empty rather than breaking the page | Read from `GET /api/publish/published`, which reads the publish queue. Before this the only place a published URL ever appeared was the editor's progress checklist, and closing it lost the link |
| Publish success toast | `editor.component.html`, `.publish-toast` + `.toast-close` | toast | The links a publish produced | IS the result | **Stopped expiring 11.08.2026** — it sits under the progress checklist, so a ten-second life meant it was gone before anything uncovered it. Dismissed by its own × |
| Thread part chips | `editor.component.html`, `.pr-parts` / `.pr-part` | panel | One numbered chip per thread message, filling in as parts send | IS the loading state | Repainted every poll on **every** network since 11.08.2026 — the short-post path never passed the progress callback, so an X thread showed 25 static chips |

## `project-planner.component` (`cedarclerk-web/src/app/pages/project-planner.component.{ts,html,css}`)

T-124, built 11.08.2026 from `docs/design_handoff_indiedev_core_loop` §8. Compact density; reached from the dashboard's sprint card or the board's toolbar, gated by `indieDevGuard`.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Toolbar | `.planner-toolbar` | panel | Title, "N sprints · M open tasks", link to the board, "+ New sprint" | Page-level `loading()` | Same anatomy as the board and the asset screen |
| Sprint card | `.sprint-card` | panel | One per sprint: number, name, dates, state badge, overdue note, progress bar, task rows | `busy()` on its controls | Order is current → planned → No sprint → finished |
| Devlog draft button | `.sprint-head`, `.btn-ghost.small` beside the edit pencil | button | **Added 18.08.2026 (ADR-132, T-158)** — assembles the sprint's finished tasks, releases in its window and still-open tasks into a post draft and opens the editor on it | `busy()` disables it | Same generator flow as the builds screen's "Changelog" |

### Added 18.08.2026 — onboarding (ADR-133, T-160), on `projects.component`

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Example-project button | `/projects` empty state, `.btn-ghost` under "New project" | button | Creates "Cedar Quest" — a filled full-game project (tasks across the board, a current sprint, a released build, a devlog written from them) and opens its dashboard | `saving()` disables it | On demand, never seeded silently; the hint under it says deleting it is fine. Starter documents of ordinary new projects also gained per-type skeletons (server-side, `StarterTemplates`) |
| State badge | `.status-badge` in the head | — | Current / Planned / Finished | N/A | **Derived from the dates on every read** (ADR-111), never stored |
| Overdue note | `.overdue-note` | — | "N tasks overdue" inside this sprint | N/A | Said about the tasks — a sprint is never itself overdue, and the card never turns red |
| Progress bar | `.sprint-bar` / `.sprint-fill` | — | Share of the sprint's tasks that are done | N/A | Absent when the sprint holds no tasks: an empty bar would read as 0% of something |
| "No sprint" group | `.sprint-card.unplanned` | panel | Tasks planned into nothing yet | N/A | Dashed border, no fill — a pile rather than a plan. Sits between planned and finished on purpose |
| Collapsed summary | `.collapsed-line` | button | "N tasks completed · collapsed" | N/A | **Only when everything in the sprint is done.** One unfinished task keeps the card open — collapsing it would hide the case worth seeing |
| Sprint dialog | `app-modal` `[width]="480"` | modal | Name, start, end; Delete on the left when editing | Present — `busy()` | Deleting says the tasks stay. Refuses an end before the start, client and server both |
| Sprint filter chips | `project-tasks.component.html`, after the state chips, separated by `.chip-divider` | button | All / S1 / S2 … / No sprint, with counts | N/A | Rendered only once sprints exist. Combines with the state filters rather than replacing them |
| Sprint chip on a card | `project-tasks.component.html`, `.sprint-chip` | — | `S14`, mono, with the sprint's name as its tooltip | N/A | The number is stored (ADR-111), not parsed out of the name |
| Sprint picker | task card modal, `.field select` | dropdown | Moves a task between sprints, or out of all of them | Present — `busy()` | Saved on change rather than with the text fields: it is a move, not an edit |
| Dashboard sprint card | `project.component.html`, `.rail-card` (second) | panel | The sprint covering today: number, name, progress, end date, overdue line | Page-level | Says "No sprint covers today" when none does — a fact about the calendar, not a missing feature |

## `admin.component` (`cedarclerk-web/src/app/pages/admin.component.{ts,html,css}`)

IF2, built 27.07.2026 in five steps (scoping doc absorbed into ADR-122, 18.08.2026). Reached from the rail's dots menu (ADR-151), gated by `adminGuard`; a non-admin gets a redirect and the API answers 404 rather than 403 — an account must not learn that an endpoint it may not use exists.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Summary cards | `.summary-grid` / `.summary-card` | panel | Seven counts: users, paid, published/drafts, comments, reactions, channels, storage | Page-level `loading()` only | Not a dashboard — no history, no deltas; the data-collection layer that would allow them doesn't exist (see the Channel Analysis dependency in `docs/tasks/ROADMAP.md`) |
| Tab strip | `.admin-tabs` | tab | Users · Invites · Posts · Reports | N/A | Four tabs, unlike the Posts Manager's own strip — different component, same visual role, and one of the cross-screen inconsistencies the 28.07 design handoff flags |
| User card | `.user-card`, expands on click | panel | Email, plan chip, admin/locked/lapsed chips, meta | N/A | Expansion is click-anywhere; the action row stops propagation so a button press doesn't collapse the card |
| Plan + expiry | `.action-row`, date input + Save | button | Set tier and expiry; blank means forever | Present — `busy()` disables Save | Free has no expiry; the hint line says which rule applies |
| Reset trial | `.action-row` `.btn-ghost` | button | Clears `TrialUsedAt` so the 7-day trial can be bought again | Present — `busy()` | Disabled when the account never used a trial |
| Lock / unlock, grant / revoke admin | `.action-row` `.btn-ghost` | button | Account state changes | Present — `busy()` | **Self-targeting is refused server-side**, not merely hidden — the button is also disabled via `isSelf(u)` |
| Credits | `.action-group` — balance, amount, note, Add / Take back | button | Moves an account's credit balance in either direction | Present — `busy()` | **Added 11.08.2026.** Self-targeting is deliberately *allowed* here, unlike lock/admin: a balance is not a privilege, and testing a paid post needs credits on the testing account. Take back is disabled at zero and refused server-side below it; the note goes to the audit log, the movement to the ledger |
| Invite attribution | `.action-group` | panel | Assign an invite code to an account that predates code tracking | Present — `busy()` | Manual attribution exists because accounts older than IF2 step 3 have no code to point at |
| Invite creation | `.invite-new` | panel | New code + label | Present — `busy()` | Codes are deactivated, never deleted — deleting one would silently orphan the accounts attributed to it |
| Audit log | `.audit-list` / `.audit-row` | panel | Append-only record of every admin action | Present — `auditLoadingMore()` on the Load-more button | Paging added after the scoping doc flagged its absence; **retention is deliberately absent** — the log is append-only on purpose (ADR-122) |
| Load more (audit) | `.btn-ghost` under the list | button | `?skip=` paging, `hasMore` drives visibility | Present | |

## Blog surfaces (server-rendered — `CedarClerk.Server/BlogEndpoints.cs`, `CedarClerk.Core/CedarToBlogHtmlRenderer.cs`)

Not Angular: these are strings built on the server and host-routed by `Program.cs` `MapWhen` on `Host.Host`. They were missing from this file entirely, which matters because roughly half of what a *reader* ever sees lives here — and because the redesign has to touch two style systems, not one.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Site header | `RenderHeader`, `.site-header-inner` | panel | Channel identity (avatar, name, `@username` + subscriber count), then **RSS** (`.rss-btn` → `/rss.xml`, added 09.08.2026), **Open in Telegram** and the theme toggle | N/A — server-rendered | The RSS button is styled secondary against the filled Telegram one: two filled buttons read as two main actions. The feed itself has existed since ADR-024, reachable only via `<link rel="alternate">` in `<head>` |
| Post list / timeline | `RenderIndexAsync`, `.post-list` / `.timeline-item` | panel | Reverse-chronological cards with a vertical chronology line | N/A — server-rendered | A private-but-listed post (`IsListedWhilePrivate`) shows a lock and **withholds its excerpt**, deliberately |
| Series page | `RenderSeriesAsync`, `/series/{slug}`, `.series-head` | panel | All visible parts of one series, numbered «Часть N», with description and count | N/A — server-rendered | Membership filter is exactly the index visibility rule, so the numbering never betrays a hidden part (ADR-125) |
| Series line + prev/next | `.series-line` above the post title, `.series-nav` below the body | panel | «Часть N из M» linking to the series page; neighbouring parts in `(SeriesOrder, BlogPublishedAt)` order | N/A — server-rendered | Both computed over *visible* members only — a stranger's numbering and neighbours skip unlisted-private parts (ADR-125) |
| Showcase page | `RenderShowcaseAsync`, `/games/{slug}`, `.showcase-head` / `.roadmap-list` | panel | **Added 18.08.2026 (ADR-134, T-159)** — a project's public game page: cover, description, store-link pills, the devlog feed (index visibility rule verbatim), and the roadmap of opted-in tasks (title + status only) | N/A — server-rendered | Exists only while `Project.ShowcaseSlug` is set and the project is not archived; store links are parsed `Label\|URL` lines, anything else skipped |
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
| Image viewer | `.lightbox` (built by the shell script), `img.zoomable` | overlay | Click any picture in the post to see it at ~94% of the viewport without leaving the page (Marty, 11.08.2026). Arrows/←→ walk **every** image in the post, so a collage or carousel is one gallery; the `figcaption` and an `N / M` counter show under it; click anywhere, Escape or the × closes | N/A — the images are already on the page | The zoom cursor is added by the script, so it cannot promise a click that JavaScript is not there to handle. The overlay lives on `<body>`, which is why the copy-protection guard had to move to a document-level listener — otherwise an enlarged picture on a private post was right-clickable |
| RSS | `RenderRssAsync`, `/rss.xml` | — | Latest 30 published posts | N/A | Auto-discovery `<link>` in every page head |
| Site footer | `ShellTemplate`, `.site-footer-inner` | panel | **Rebuilt 13.08.2026** into three groups on one line: `.footer-brand` (cedar mark + "Made with Cedar Clerk"), `.footer-links` (Terms · Privacy · Status) and `.footer-badge` (DigitalOcean referral). Was the made-with line centred with the badge centred on a second row | N/A — server-rendered | Terms and Privacy point at the **app host**, not the blog: one copy of a legal page for both hosts. Status points at `status.mooexe.dev`, deliberately off-box — it is reachable exactly when the blog is not. Under 700px the three groups stack and re-centre, because a stacked row pushed to the edges reads as ragged. The badge is dimmed to `opacity: .72` (full on hover): it ships as a white plate and was otherwise the brightest thing on a dark page |

## Landing (server-rendered — `CedarClerk.Server/LandingEndpoints.cs`, `/` for signed-out visitors on the app host)

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Hero | `.hero` | panel | **Reworked 18.08.2026 (ADR-135, T-154)** — devlog-first: "Build your game. Grow your audience. One tool.", English by default (Russian only when the browser asks first) | N/A — server-rendered | The four feature cards re-slanted to the same story: work → story, six languages, every network, a public home |
| Waitlist form | `.waitlist`, `#waitlist-form` | form | **Added 18.08.2026 (ADR-135)** — the primary CTA while registration is invite-only: email → `POST /api/waitlist`, inline JS swaps the form for a done-line | The submit button is the state | Honeypot field instead of a captcha; a repeat signup answers OK — the visitor's goal is to be on the list, and they are |
| Pricing cards | `.plans` | panel | Prices and limits read from `PlanLimitations`/`Consts.Plans` at render | N/A | Cannot drift from the code that enforces them — the reason the page is server-rendered |

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

## `project-tasks.component` (`cedarclerk-web/src/app/pages/project-tasks.component.{ts,html,css}`)

T-123, built 11.08.2026 from `docs/design_handoff_indiedev_core_loop` §3-4. Compact density on the page root; reached from the project dashboard's "Board →" link, gated by `indieDevGuard` (with the module off the route redirects to `/drafts` — the URL is not wrong, the feature is not installed).

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Toolbar | `.tasks-toolbar` | panel | Title, "N open · M in total", overdue count, search, view toggle, "+ New task" | Page-level `loading()` | Same anatomy as `/drafts` and the asset screen — the three module screens have to read as one product |
| View toggle | `.view-toggle` | tab | Board ↔ list. Both are required by the design; neither is a fallback | N/A | Persisted in `localStorage` (`cedar.taskView`), like the drafts view |
| Filter chips | `.filter-row` `.chip` | button | All / Open / Overdue, with counts | N/A | The overdue chip only renders when something is overdue — a permanent zero would read as a broken filter |
| Board column | `.column` (×4) | panel | One per status, on `--alt` with `--sheet` cards; header carries the count and a "+" that creates straight into that column | N/A | Horizontal scroll below 900px: a 60px-wide kanban column is not a column |
| Task card | `.task-card` | button | Title, priority chip, due date, assignee, link chips. Click opens the card modal | N/A | Only the **date** goes red when overdue — never the card, never the column |
| Link chip | `.link-chip` | — | Icon + label of a linked document/asset/task | N/A | A link whose target is gone renders italic "no longer exists" rather than an empty chip |
| List view | `.tasks-table` | panel | Task / Status / Prio / Due / Links, sortable headers | N/A | Undated tasks sort last in **both** directions — "no deadline" is not a late deadline |
| Task card modal | `app-modal` `[width]="560"`, `.task-modal` | modal | Title, status, priority, due, assignee, description, links, timestamps | Present — `busy()` on every control | Centered modal, the design's chosen variant. Opened via the `?task=` query parameter, which is what makes a task linkable from the dashboard |
| Description field | `.field textarea` | — | Plain text, deliberately not a rich editor | N/A | ADR-106: a task that needs tables or media is really a document. The empty state says exactly that |
| Public-roadmap toggle | task card modal, `.roadmap-toggle` | — | **Added 18.08.2026 (ADR-134, T-159)** — opt-in onto the project's public showcase roadmap; title and status only ever leave | Present — `busy()` | Saved on change like the sprint picker above it, not with the Save button |
| Link picker | `.link-picker` | panel | Documents of this project not already linked | Present — `busy()` | Documents only for now; asset↔task links exist in the API and are drawn, but are added from the asset screen |
| Card footer | `.modal-foot-row` | button | Delete · Archive · Save · Mark done / Reopen | Present — `busy()` | Delete asks for confirmation and says archiving is the softer option; the ghost button only turns red on hover |
| New-task modal | `app-modal` `[width]="480"` | modal | Title, status, priority. Enter creates | Present — `busy()` | Opens pre-set to the column its "+" was pressed in |

## `project-assets.component` (`cedarclerk-web/src/app/pages/project-assets.component.{ts,html,css}`)

T-122, reshaped by **ADR-117** on 12.08.2026 when the index moved to the cloud and indexing became a push from the desktop agent. Compact density on the page root; reached from the project dashboard, gated by `indieDevGuard`.

**The question this screen has to keep answering: is this a file or a fingerprint of one?** The index opens from anywhere, so most of the time the bytes are on another machine. `isLocal()` compares the project's `sourceMachine.id` against `cedarDesktop.machine()`, and defaults to **false** — a browser has no bridge, so it never claims otherwise.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Toolbar | `.assets-toolbar` | panel | Title, "N files indexed", root path, last-indexed time, not-found count, search, view toggle | Page-level `loading()` | Same anatomy as `/drafts` and the task board |
| Machine note | `.fingerprint-note` | — | "files on MARTY-PC" beside the root path, with a tooltip explaining what is here instead | N/A | Only when `!isLocal()`. Without it a bare path reads as "on this computer" wherever it is shown |
| Re-index / Change folder | `.btn-ghost` in the toolbar | button | Re-scan the recorded folder; pick a new one | `scanning()` disables both | Re-index needs `isLocal()` — from elsewhere the path names a folder this machine cannot see. Change folder only needs a bridge, since it picks a new one |
| Scan banner | `.scan-banner` | panel | Two phases: index (counting → indexing → sweeping) and previews, each with its own counts; previews also show megabytes | Is itself the loading state | The progress **bar** covers the index phase only. One bar across both would crawl then leap — the phases differ by orders of magnitude per item, and the preview pass has no honest denominator until the sweep has run |
| Cancel | `.btn-ghost` in the banner | button | Stops the walk and the upload | N/A | Everything already uploaded stays. The preview pass resumes from `thumbs/pending`, so cancelling costs nothing but time |
| Pick-a-folder card | `.pick-card` | panel | Empty state. One button: the OS folder picker | `scanning()` | The typed-path field is **gone** (ADR-117): a path the page invented would have to be granted by the page, and the grant exists precisely so disk access starts with a human gesture. Recent folders are listed as history, not as buttons |
| Web-only notice | `.web-only` + link to `/downloads/latest` | — | In a browser: indexing needs the app, and what is indexed still shows here | N/A | Two facts in one sentence, because "you can't do this" alone reads as a broken screen |
| Filter chips | `.filter-tabs` | button | All / per-kind / not-found, with counts from the **unfiltered** set | N/A | No "Music" chip: a track cannot be told from ambience by extension (ADR-107) |
| Grid tile | `.tile` | button | Preview or icon, filename, size · dimensions · date | N/A | Striped ground, not blank — a plain rectangle reads as an image that failed |
| Fingerprint chip | `.fingerprint-chip` | — | "fingerprint" over the top-right of a tile, tooltip naming the machine | N/A | Deliberately quiet (`--t2`, not `--danger`): this is the normal case, not a warning, and the not-found state owns the alarming colour |
| Preview states | `.preview-label` | — | Three: stored, coming, impossible | N/A | Without the middle one a freshly indexed folder in a browser looks identical to one full of formats nothing can decode |
| List view | `.asset-table` | panel | File / Type / Details / Status | N/A | The status column has three values too — not found, on disk, fingerprint. "On disk" is a claim about *this* disk and would be false from a browser |
| Asset modal | `app-modal` `[width]="780"` | modal | Preview, fields, machine, linked documents | Present — `busy()` | Says outright what is here when the file is elsewhere |
| Reveal / Re-index file | `.modal-actions` | button | Explorer highlight; re-stat this one file | `busy()` | **Hidden**, not disabled, unless `isLocal()`: a greyed button invites a hunt for the reason it is grey. Re-index is two hops now — the agent looks, the page pushes |
| Link picker | `.link-option` | panel | Documents of this project not already linked | `busy()` | "Linked documents", never "Used in": an indexed file lives outside Cedar Clerk, so a reference to it can only be stated (T-141) |

## `projects.component` (`cedarclerk-web/src/app/pages/projects.component.{ts,html,css}`)

Added 18.08.2026, closing the gap this file had carried since T-120 shipped (10.08). The projects **list** at `/projects`, compact density, `indieDevGuard`; built from the Claude Design core-loop package.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Toolbar | `.projects-toolbar` | panel | Title, "N projects, M active" subtitle, search, New project | Page-level `loading()` | Same anatomy as `/drafts` and the other module screens |
| Filter tabs | `.filter-tabs` | tab | All / Active / Archived with counts from the unfiltered set | N/A | |
| Project row | `.projects-row` | button | Cover (or initials), name, document / open-task / asset counts, last activity, state badge | N/A | Whole row opens the dashboard; keyboard-reachable (`tabindex` + Enter). Tasks column is the **open** count on purpose. **Project type is not shown here** — that is `T-139` |
| Empty state | `.empty-state` | panel | Controller icon, explanation, New project | N/A | |
| Create-project modal | `app-modal` `[width]="560"` | modal | Project type picker (four types, each naming its starter document) + name | `saving()` disables submit | Types are ADR-103 as amended by the design package; Enter in the name field submits |

## `project.component` (`cedarclerk-web/src/app/pages/project.component.{ts,html,css}`)

Added 18.08.2026, same gap. The project **dashboard** at `/projects/:id`, comfortable density (no `data-density` attribute), `indieDevGuard`.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Project head | `.project-head` | panel | Cover, name, type icon + name, active/archived, document count, description | Page-level `loading()` | The **only** screen showing project type (`T-139`) |
| Head actions | `.btn-ghost`/`.btn-accent` in `.project-head` | button | Assets, Builds, Settings, New document | N/A | Builds link deliberately replaces a rail card — a project with no versions should not carry an empty one (T-126 note in the template) |
| Featured type cards | `.type-card` | panel | Per featured document type: count + recent documents, each opening the editor | N/A | `@empty` renders "none yet" |
| Tile row | `.tile-row` / `.type-tile` | panel | Remaining document types: count + latest document | N/A | |
| "Up next" rail card | `.rail-card` (first) | panel | Server-sorted next tasks: title, priority, due date; link to the board | Page-level | Overdue reddens the **date only**, never the row (T-123). Empty text distinguishes "no tasks at all" from "nothing up next" |
| Sprint rail card | `.rail-card` (second) | panel | Current sprint, progress bar, end date | Page-level | Row recorded 11.08.2026 under the `project-planner.component` section — one row per element, it stays there |
| New-document modal | `app-modal` `[width]="640"` | modal | Grid of document types, one click creates and opens | `busy()` disables cards | Note under the actions says where the document lands |
| Project-settings modal | `app-modal` `[width]="520"` | modal | Name, description, archive/unarchive, delete | `busy()` | Delete is two-click (button arms, hint appears) — the confirm pattern `project-builds`' delete lacks |
| Showcase block | project-settings modal, `.checkbox-row` + slug/links fields | panel | **Added 18.08.2026 (ADR-134, T-159)** — the public game page's switch: toggle, slug (server slugifies, globally unique), store links `Label\|URL` per line, and the live URL once it exists | `busy()` on Save | Saves with the modal's one Save button; turning it off clears the slug but keeps the links |

## `project-builds.component` (`cedarclerk-web/src/app/pages/project-builds.component.{ts,html,css}`)

Added to this file 12.08.2026, by the guard test rather than by anyone noticing — it had shipped with no row at all. Compact density on the page root, reached from the project dashboard, `indieDevGuard`. A build is a version of the game; the screen exists to answer "what went into 0.3.1" and to turn that answer into a changelog document.

| Element | Location | Type | Purpose | Loading state | Notes |
|---|---|---|---|---|---|
| Toolbar | `.builds-toolbar` | panel | Title, "N builds, M unreleased", link to the task board, New build | Page-level `loading()` | Same anatomy as the asset index and the task board |
| Build card | `.build-card` | panel | One per build: version, released/unreleased badge, release date, done/total counts | N/A | Ordering and counts come with the list; nothing is computed in the template |
| Make changelog | `.btn-ghost.small` in `.build-head` | button | Creates a changelog document from the build's done tasks | `busy()` disables it | The one action that leaves this screen for the editor |
| Edit build | `.icon-btn` in `.build-head` | button | Opens the same modal as New build, pre-filled | `busy()` | |
| Linked document chips | `.build-docs` / `.doc-chip` | chip-row | Documents attached to the build; opens each in the editor | N/A | |
| Task rows | `.task-row` | panel | Tasks assigned to the build, with their status badge; opens the task | N/A | `@empty` renders "no tasks" rather than an empty card |
| Empty state | `.empty-state` | panel | Rocket icon, explanation, New build | N/A | |
| Build modal | `app-modal` `[width]="480"` | modal | Version, release date (with hint), notes; Delete on the left when editing | `busy()` on Save and Delete | `Enter` in the version field saves. Delete has no confirm step — worth a look, every other destructive control in the app has one |
| Error lines | `.error-line` | toast (inline) | `loadError()` above the list, `actionError()` in the toolbar and again in the modal | N/A | |

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
| Styleguide | `pages/styleguide.component.{ts,html,css}` → `/dev/styleguide` | page | Every token and every bench primitive on one screen, in both themes, both densities and on both surfaces (T-078, T-215) | n/a — no async | The kit's only call site: each primitive appears in every variant and size with its states, and the focus ring is proved on paper, on the rail, and on the two surfaces ADR-140 accepts as exceptions. The page draws **no control of its own** — `styleguide.component.spec.ts` fails when a native button or input appears outside a bench component. Captured by the audit run into `.e2e-audit/70…73`, all four theme×density combinations |
| Surface toggle | `styleguide.component.html`, `.sg-controls` | button | Flips the demo bays between `data-surface="paper"` and `"chrome"` — the same control on wood and on paper (ADR-138) | n/a | Sets the attribute on the bays and on nothing else: a surface is an element's material, not a screen's mode. The inspector bay is chrome wherever it stands and deliberately does not follow |
| Pseudo-locale toggle | `styleguide.component.html`, `.sg-controls` | button | Switches the whole app in this browser onto the inflated long-word strings (T-051) | n/a | Per-browser (`localStorage: cedar-pseudo`), never reaches the profile. `?pseudo=1` on any URL does the same |
| Icon inventory | `pages/icons.component.{ts,html,css}` → `/dev/icons` | page | What each icon means in this app, from the generated call-site table: duplicates of meaning, overloaded glyphs, unused icons, full grid with search and weight/size toggles (T-080) | n/a — generated data, no fetch | Regenerate with `npm run icons:generate` after moving icons, or it describes the previous commit. Captured as `.e2e-audit/74` |

## Screens and flows never opened in a browser

Cross-referenced with `TASKS.md`. Note that smoke coverage is a different claim from "a person looked at it and it was right" — it says the flow works, not that it reads well.

- **Covered mechanically as of 30.07.2026** (Phase 10 Block B): save guards (refusal and restore), version history, the private-post gate end to end, blog reactions and comments, the admin gate, the Posts Manager list, the drafts round-trip.
- **Still seen by nobody**: flush-on-hide on a real iPhone (cannot be staged on a desktop — the 29.07 incident was iOS), incremental re-translation preserving manual corrections, the uk/be/ka provider refusal, the translate-all modal, the two new form field types on a real gate, the Posts Manager submission modal and "mark all as read", the Appearance panel's autosave, the FI2 export rebuild, the FI3 pickers and folder delete, the FI4 forms editor and per-language gate, tag rename/delete, article title, audit paging, the emoji panel, the paragraph-mark toggle, the glossary tooltip on a real published post, per-language cross-links, a semi-public post on the blog index, and Phase 8's Steps 6 and 7 (tags in the Telegram export, comment replies/highlight/name reservation).
