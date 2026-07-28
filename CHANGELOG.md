# Changelog

Human-readable, grouped by session/date, derived from `git log` (33 commits, `6ace957`→`6065cd9`) and the richer context already captured in `docs/ROADMAP.md`/`docs/DECISIONS.md`. Not a raw commit dump — see `git log` directly for that.

## 2026-07-28 (latest) — Phase 9e continues: appearance, profile, polls, templates

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
