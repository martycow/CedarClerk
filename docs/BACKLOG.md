# Backlog

The **only** place to look for open, not-yet-started ideas/features/tech-debt — deliberately separate from `docs/ROADMAP.md`, which mixes this in with phase status and shipped work. If it's not started and not scheduled into a phase, it lives here; once it gets scoped into a phase, move the entry into `docs/ROADMAP.md` and delete it from here.

Migrated from `docs/ROADMAP.md` (16.07.2026 origin dates preserved) on 25.07.2026 — content unchanged in the move, only relocated.

## Ideas (not yet scoped into a phase)

| # | What | Notes |
|---|---|---|
| 1 | Cross-posting: split a post into parts and publish to Twitter/Bluesky | **Core positioning axis now, not a peripheral nice-to-have** (ADR-021, `docs/DECISIONS.md` — Cedar Clerk is a write-once-publish-everywhere tool, Telegram/blog/Twitter-Bluesky are co-equal destinations). Still not scoped into a phase. Needs an export setting to control social-preview (OpenGraph/card) visibility for those platforms — character-limit-aware splitting, not a straight dump of the Telegram render |
| 2 | Email confirmation on registration | Currently invite-code gated only, no email verification step at all — needs a provider decision (SMTP vs a transactional-email service) before scoping |
| ~~3~~ | ~~Dedicated tag-management UI~~ | **Done 27.07.2026** — `PUT /api/drafts/tags` (rename across every draft, merging rather than duplicating when the target already exists) and `DELETE /api/drafts/tags/{tag}`. Surfaced as a `[manage]` mode on the shared `TagPickerComponent`, reachable from `/drafts`. Propagation to the blog is free: it reads `Draft.Tags` directly and keeps no copy |
| ~~4~~ | ~~Separate "draft name" from "article title"~~ | **Done 27.07.2026** — `Draft.ArticleTitle` (migration `AddArticleTitle`, nullable, null means "same as the name"), edited in the Posts Manager and used by the blog page, the post cards, RSS and both file exports. The per-language half turned out to already exist: `DraftTranslation.Title` has always been that language's own article title, so only the primary language needed a field |
| 5 | Founder / Lifetime plan via a designated invite code | Low cost, reuses existing invite-code registration infra — see ADR-022, `docs/DECISIONS.md`. Grants a permanent Pro tier (not Pro Plus, no AI); no new schema needed (`ApplicationUser.PlanExpiresAt = null` on a paid tier already means "never expires"). Open: the founder code's actual value |

### Idea dump 25.07.2026 (from Marty, via `/remote-control`)

| # | What | Notes |
|---|---|---|
| 6 | Loading indicator for import/export and other long operations | Broader than the existing AI-op indicator (Phase 8 Step 8 in `docs/ROADMAP.md` — currently just an elapsed-time counter, no real progress bar). Needs coverage on `.cedar`/Markdown import, blog export, and any other long-running action that currently gives no feedback |
| ~~7~~ | ~~Tag picker/creator popup everywhere tags are used~~ | **Done 27.07.2026 (FI3.2)** — extracted as `TagPickerComponent`, used by the editor, the new-draft dialog and the posts manager. The decision it asked for was taken: spread the UI, leave `Draft.Tags` a flat string; idea #3's rename/delete works fine without normalizing |
| ~~8~~ | ~~Show all tags on a blog post card~~ | **Done 27.07.2026** — the card loops over every tag instead of emitting `tags[0]` |
| 9 | Store `session_id` in a cookie for auto-login | **Likely already implemented** — ASP.NET Identity already sets a persistent auth cookie today (`isPersistent: true`, `AuthEndpoints.cs:61-71`); `auth.service.ts` relies on the browser cookie jar, no manual token handling. Confirm with Marty what's actually failing (session too short-lived? doesn't survive a browser restart?) before treating this as new work |
| 10 | Email confirmation at registration | Duplicate of idea #2 above — do not scope twice, just link back to #2 |
| ~~11~~ | ~~Glossary of terms~~ | **Done 27.07.2026** — `GlossaryTerm` (migration `AddGlossaryTerms`), a `/glossary` page, and `GlossaryScanner` in Core marking terms in the rendered blog HTML with a hover/tap tooltip. **Scoped deliberately narrower than the original line in two places**: the scan runs at render time on the blog only (Marty's ask was "при публикации"), not as an inline TipTap decoration in the editor; and there is no auto-detect-before-posting pass. See the ADR in `docs/DECISIONS.md` |
| ~~12~~ | ~~Admin role + a dedicated user-management page (mini CRM)~~ | **Done 27.07.2026** — all five steps of `docs/admin-panel-scope.md`; the audit log gained paging on 27.07.2026, which was the last open gap. Original note follows. No role/admin concept existed at all — `ApplicationUser : IdentityUser` has no `IsAdmin`/role field, `Program.cs` calls `AddIdentityCore` without `.AddRoles(...)`, and `AuthEndpoints.cs` has zero role checks. Needs: role concept on `ApplicationUser` (or ASP.NET Identity roles), a migration, admin-only endpoints, and a new page listing/managing users |
| ~~13~~ | ~~Appearance settings: a small live preview~~ | **Done 27.07.2026 (I14)** — the appearance panel moved into the editor beside the sheet, so the sheet *is* the preview and nothing extra had to be built |
| 14 | Move AI features into a separate popup/menu | An `.ai-chip` AI popover already exists in the toolbar today. Clarify with Marty what's actually wanted — a different location for the same popover, or a fundamentally different UI pattern (e.g. a floating button off the toolbar entirely) |
| 15 | Start adding integration buttons for other social networks | Today's "Integrations" section in Settings (`settings.component.html:408-467`) only has Telegram. Overlaps with idea #1 above (Twitter/Bluesky cross-posting, already an ADR-021 positioning decision) — treat as the same initiative, don't scope as a separate item |

### Idea dump 25.07.2026 (from Marty, "Cedar Clerk 0.9.0" feature list)

Raw list, not yet scoped into phase steps — logged here per Marty's call, not started as `Phase 9` in `docs/ROADMAP.md` yet.

| # | What | Notes |
|---|---|---|
| 16 | X/Twitter, Bluesky, Threads, Instagram integration | Same initiative as idea #1 (cross-posting, ADR-021) and idea #15 (social integration buttons) — this is the concrete platform list for that already-decided direction, not a new idea. Threads/X/Bluesky/Facebook/Medium/Patreon/Notion/Google Docs already have "Coming soon" placeholder rows in the Export modal's mock list (`editor.component.html`, `export-mock-list`); Instagram is new, not in that mock list yet |
| 17 | Export menu per social network (except Telegram): preview + thread-splitting for X/Threads/Bluesky | The actual mechanism for #16 — character-limit-aware splitting into a thread, plus a preview before sending. Depends on #16 (need the integrations themselves first) |
| ~~18~~ | ~~Notifications about comments/likes via the Telegram bot~~ | **Done 26.07.2026** — opt-in toggle in Settings → Integrations, DM on new comments/replies and new "like" reactions only (not dislikes, not un-likes). See ADR-040, `docs/DECISIONS.md`. Not yet live-verified against a real Telegram DM |
| ~~19~~ | ~~Folders / drafts grouping~~ | **Done 26.07.2026** — real `Folder` entity, one folder per draft, full CRUD (create/rename/delete), filter + per-row assignment on `/drafts`, lighter assign-only selector in the editor. See ADR-039, `docs/DECISIONS.md`. Not yet live-verified in a browser |
| ~~20.1/20.2~~ 20.3 | ~~Private posts: require registration to view (20.1), access management — who can view (20.2)~~ / must support polls (20.3) | **20.1/20.2 done 26.07.2026** — email invite list per post (`PostInvite`), link-based access via a long-lived cookie, gated at all 4 places a private post could be reached by slug. Required building the project's first real email infrastructure (Resend) as a prerequisite — see ADR-041, `docs/DECISIONS.md`, and `docs/integrations-setup.md` §3 for the manual setup Marty still needs to do (Resend account + domain verification) before email delivery actually works; the link itself is always shown/copyable regardless. **20.3 still open and still depends on #22** (polls) existing first — don't scope it standalone |
| 21 | Optional registration on the blog site — to comment, reserve a display name, prevent impersonation; possibly a "verified" badge | Blog comments today are anonymous, `VisitorHash`-scoped (IP-based, no accounts) with post-hoc moderation via deletion (ADR-016) — this is a fundamentally different model (real visitor identity) and would sit alongside, not replace, the anonymous path. The "verified" badge sub-idea has no defined meaning yet (verified how — email? something else?) |
| 22 | Polls, forms, questionnaires | New content-block type, would need: TipTap node + renderer support across all three surfaces (Telegram Blocks, blog HTML, `.cedar`), response storage, and a results view. Referenced as a dependency by idea #20.3 |

**Open dependency note**: idea #20.3 (private posts must support polls) needs #22 (polls) built first — don't scope the "private" half before polls exist as a content type.

### Input sweep v2 — 27.07.2026 (late), current (from `_Documents_/CedarClerk/Input.md`)

**Marty rewrote `Input.md` again** after the first sweep closed. Confirmed by him: these do **not** overlap the earlier lists. ~60 items across 6 new features, 6 improvement groups and 3 bug groups. Numbered `NF*`, `FI*`, `DB*` — the source file's own numbering.

Scoped as Phase 9e in `docs/ROADMAP.md`. Recorded here with what each actually costs, since several are much larger than one line suggests.

**New features**

| # | Pri | What | Verdict / what it really costs |
|---|---|---|---|
| NF3 | High | Email confirmation on registration, required at login | **Was already idea #2/#10** in this file. **Blocked in practice**: the Resend API key configured on the Pi 401s (issued for a since-deleted domain — see `TASKS.md`), so invite emails don't send today either. Confirmation mail would fail the same way. Needs a working key before it can be built, or it ships broken |
| NF1 | Medium | Post templates — a preset authored in the editor, named like a draft but not a post | Needs a real separation between "draft" and "template" (Marty says so himself). Cheapest honest shape: a flag on `Draft` plus filtering, rather than a parallel entity — the editor, autosave and export all already work on `Draft` |
| NF4 | Medium | OAuth sign-in: Google, Apple, Meta, Telegram | Telegram is already done (HMAC link, ADR-009) but as *linking*, not *sign-in* — turning it into a login path is its own change. The other three need real provider registrations, secrets on the Pi, and a decision about account merging when an OAuth email matches an existing password account |
| NF5 | Medium | Polls inside a post | **This is idea #22**, and idea #20.3 depends on it. Marty's suggestion to build it on the existing form-preset entity is a genuinely good reuse — the question/choice model is already there. Still needs a TipTap node, renderers for all three surfaces, response storage and a results view. Telegram has native polls but **not inside `sendRichMessage` Blocks**, so the Telegram surface likely degrades to a link |
| NF6 | Low | Embed a pay-any-amount form mid-post on the blog | Stripe/PayPal exist for subscriptions only. A public, unauthenticated, arbitrary-amount payment on a blog page is a different flow with its own fraud surface |
| NF2 | Low | Add ES/FR/DE — UI localization now, translation later | Two separate axes that this item merges: **UI** language (`en.ts`/`ru.ts`, typed so a new locale must implement every key) and **content** language (`DraftTranslation`, the editor's RU/EN tabs, `?lang=` on the blog, auto-translate). The UI half is mechanical; the content half touches the editor's whole language model, which currently assumes exactly two |

**Improvement groups** — each is many sub-items; full text in `Input.md`.

| # | Pri | What | Notes |
|---|---|---|---|
| FI2 | High | Export window UX, 11 sub-items | The unifying principle Marty states is worth keeping: **"Export manages ONLY export"**. Unpublish, scheduled-post management and post-publication editing all move to the Posts Manager. Also: RU/EN as checkboxes, a form-preset dropdown for private posts, publish/schedule as one button, and a success toast with links |
| FI3 | High | Posts Manager UX, 11 sub-items | Includes **removing the "Reactions & comments" tab** and folding it into Posts — compatible with the per-post grouping just built, it moves where that grouping lives. Also a manual blog URL, search, status indicators, and a better toolbar icon (the current one is a chart) |
| FI4 | High | Forms manager: per-language presets; the layout "rябит" | Per-language presets interact with NF2's content-language axis — worth doing after that decision, not before |
| FI6 | Medium | Account settings, 5 sub-items | **Blocked indefinitely**: sub-items 1/3/4/5's text was lost when `Input.md` was overwritten before this session and neither Marty nor this file recorded them — only sub-item 2 (the deferred pricing restructure) survives. Needs re-specifying from scratch before any code |
| ~~FI1~~ | ~~Low~~ | ~~Appearance panel UX, 7 sub-items~~ | **Done 28.07.2026** — see `docs/ROADMAP.md` Phase 9e and ADR-053 |
| ~~FI5~~ | ~~Low~~ | ~~Profile settings: real social icons, more slot types, multi-language signatures~~ | **Done 28.07.2026** — see `docs/ROADMAP.md` Phase 9e and ADR-054 |

**Bugs**

| # | Pri | What | Notes |
|---|---|---|---|
| DB1 | Medium | iPad layout — nothing should overflow the screen | Recurring theme (`B24`, `B7`, the mobile items). Needs a device pass, not a guess |
| DB3 | Low | **Flag emoji don't render on desktop browsers** | **This invalidates a choice I made**: `I1`/`I17` used flag emoji, reasoning that a flag is recognisable to someone who can't read the language. On Windows that is simply false — it does not ship regional-indicator glyphs, so they render as letter pairs. Needs a different visual (inline SVG, or language codes) |
| DB2 | Low | Drafts table, 7 sub-items | Includes a real regression: **column resize behaves inverted** (`N1`). Plus a default sort, a narrower Title default, status indicators, name validation (1–64 chars), and the new-draft dialog opening *before* navigation |

**Cross-cutting decisions needed before building**

1. **FI6.2 — collapse the tiers to one paid plan?** Contradicts ADR-012/013/014 and changes billing, limits and the admin panel. Decide first.
2. **NF2 — how many content languages, really?** The editor's two-tab model, `Languages.cs`, auto-translate and the blog's `?lang=` all assume two. FI4 and FI5 both wait on this.
3. **NF3 — the Resend key** must work before email confirmation is worth building.

### Input sweep — 27.07.2026, current (from `_Documents_/CedarClerk/Input.md`)

**A third list from Marty**, this time out of `Input.md` rather than `Brainstorm_Features.md` — 19 improvements, 9 bugs, 2 removals, 2 features. Same rule as before: it **adds to** v1/v2, it doesn't cancel them. Numbered `I1`…`I19` (improvements), `IB1`…`IB9` (bugs), `IT1`/`IT2` (removals), `IF1`/`IF2` (features) — plain numbers and `B`/`N` prefixes are already taken.

Scoped into `docs/ROADMAP.md` Phase 9c; this table is the full text plus the dedup verdict for each item.

**Bugs** — these come first in Phase 9c, before any improvement.

| # | Priority | Tag | What | Verdict / overlap |
|---|---|---|---|---|
| IB3 | High | Translation | Opening the RU version makes the EN version go Dirty about a second later, as if EN lags behind RU | New. Distinct from `B14` (which was about the re-translate button being hidden) — this is the stale flag firing on a pure read |
| IB4 | High | Workspace | The ruler is only visible along the top and sits *under* the writing area. If it can't be fixed, remove it | **Duplicate of `B12`**'s first half (the paragraph-number half of B12 shipped 27.07.2026). Removal is now an explicitly sanctioned outcome, which it wasn't in B12 |
| IB6 | High | Workspace | Return to the editor from Settings and the post reads as "outside any folder" — clicking the folder picker shows the correct one | New. Almost certainly the folder signal not being re-read on route re-entry, not a persistence bug |
| IB7 | High | Diff gutter | Diff gutter on the right is still drawn above or below the line it belongs to | **Duplicate of `B11`** (Medium, never shipped). "Still" confirms the gutter was never fixed; priority rises to High |
| IB8 | High | Posts Manager | No way back to the editor from the Posts Manager — no back button, no logout, only editing the URL | New. Real trap: `/posts` was built (N7) with its page chrome stripped, and nothing replaced the topbar |
| IB1 | Medium | Headers | The paragraph-format dropdown still says "Paragraph" in English in the RU UI | New, and a straight miss from ADR-050's translation sweep |
| IB2 | Medium | Re-translate dialog | Everything in the dialog is Russian except the description itself; the progress bar slides far left out of the working area when re-translating | New. Two defects in one row — one translation miss, one layout bug |
| IB5 | Medium | Comments | Blog comment section misbehaves: picking who to reply to can't be undone, and the form is bulky | New. Post-dates `ADR-037` (replies), so it's feedback on shipped work |
| IB9 | Medium | Profile | On some pages the profile button does nothing — doesn't open | New. Needs the page list narrowed down before it's fixable |

**Improvements**

| # | Priority | Tag | What | Verdict / overlap |
|---|---|---|---|---|
| I7 | High | Private Posts | Configurable watermark on private posts | **Specced 27.07.2026, no longer open-ended**: text, in a very heavy semi-transparent face, **tiled over** the post on the blog (above the content, not behind it). In the editor it is *not* rendered — the draft just carries a marker icon saying a watermark is set |
| I9 | High | Forms | Form presets are a standalone entity; at publish time you pick one. With no preset, a button leads to the preset-creation page. The form page needs a Save button at the bottom so it's clear whether it saved | **Extends the shipped `N10`/`N12`** (ADR-047). Presets already exist and are already copied-not-linked; what's missing is the empty-state route and the explicit Save |
| I1 | Middle | Login Page | Language picker on login/registration (two flags at the bottom). After registering, Settings should already hold the language chosen at signup | New. `B26`/ADR-044 shipped the picker in Settings only; `UiLanguage` already exists on the user, so signup just needs to carry it |
| I2 | Medium | Workspace | Line numbers are too small and barely visible; they should hug the left edge with a small gap, VS Code style. Optionally also show horizontal line rules | Refines the paragraph-number feature that shipped 27.07.2026 (`B12` second half) |
| I4 | Medium | Reactions | A reaction/comment block currently looks exactly like a code block — needs its own visual treatment | New |
| I10 | Middle | Drafts Sheet | The drafts table can be wider; it should adapt to the screen width | Related to `B24` (horizontal scroll) and `N1` (resizable columns), but neither made the table fluid — the grid is still fixed-width |
| I11 | Middle | UI | Move the Posts Manager and Settings entry points into the top bar as real buttons, like Export | **Reverses part of `B22`** (which moved them into the account popover) and resolves `B6` (two entry points to Settings) in the opposite direction. Newer instruction wins, same as B22 over the earlier topbar work |
| I12 | Middle | Settings | Split the settings page: profile settings (opened from the user menu), appearance settings, and the rest | New. Interacts with `I14`/`B15` — decide the split before moving appearance out |
| I14 | Middle | Customization | Move appearance and toolbar settings into a right-hand panel in the editor so the effect is visible, or at least show a preview inside Settings | **Duplicate of `B15`** + idea #13. Third time it's been raised |
| I16 | Middle | Audio Insert | Custom name for an inserted audio clip — Telegram currently shows `asset_<...>.mp3` | New |
| I18 | Middle | UI | Better icon for the drafts-table button — the current one doesn't read as anything | **Duplicate of `B20`** |
| I19 | Middle | Posts Manager | Move the form-response statistics into the posts tab, where it fits better | New, and it partly walks back `N10`'s tab layout |
| I3 | Low | Toolbar | Show the keyboard shortcut in the tooltip where one exists; possibly a customizable shortcut map | New |
| I5 | Low | Insert Table | Table insert is hard-coded to 3×2 — let the default size be configured within sane bounds | New |
| I6 | Low | Forms | Autofill email/name/nickname in the private-post viewing form | New. Note this is a public, unauthenticated page — autofill can only mean browser autocomplete attributes, not server-side prefill |
| I8 | Low | Stats | Make the time-range slider bigger both ways; the notches are too close together to read | **Refines the just-shipped `N9`** (27.07.2026, ADR-049) |
| I13 | Low | UI | Fullscreen toggle button | New |
| I15 | Low | Signature | Custom link text for the cross-link between the Telegram post and the blog post | New, and the same shape as the open `B18` (custom YouTube link text) — worth doing together |
| I17 | Low | Localization | Flag icons instead of language names | New. Pairs with `I1` |

**Removals** — Marty asking for features to be deleted, not built.

| # | Priority | Tag | What | Verdict |
|---|---|---|---|---|
| IT1 | Low | Status Bar | Delete editor zoom entirely — it doesn't work and seems pointless | New. Verify it's genuinely broken before deleting, then remove the control and its state |
| IT2 | Low | Settings Page | Delete toolbar customization — looks useful, in practice just clutter | New, and it removes a chunk of `ADR-035`. Interacts with `I12`/`I14`: don't design the settings split around a section that's about to go |

**Features**

| # | Priority | Tag | What | Verdict / overlap |
|---|---|---|---|---|
| IF2 | High | Admin Panel | Admin panel page: manage posts and users, create invite codes, see which user came in on which invite, activate/deactivate a subscription, and much more — "the more functions the better" | **Same initiative as idea #12** above. **Scoped 27.07.2026 — see `docs/admin-panel-scope.md`** for the verified state of the code, the three decisions it needs, a 5-step build order and 5 open questions. Headline findings: no role concept exists at all; invite codes are one config string, so attribution needs new data and **cannot be backfilled** for existing accounts; and cross-owner access must be a separate `/api/admin` endpoint set rather than a bypass flag threaded through the 61 owner-scoped queries |
| IF1 | Low | Profile | Avatar upload | New. `AssetEndpoints` + the storage quota already exist, so this is mostly profile plumbing |

### Brainstorm v2 — 26.07.2026, current (from `_Documents_/CedarClerk/Brainstorm_Features.md`)

**Late on 26.07.2026 Marty emptied the brainstorm file and wrote 13 new items into it.** This is an *addition*, confirmed with him directly: v1 was already recorded here, so he started the source file over rather than appending. **Nothing in v1 is cancelled** — both lists are live, v2 just holds the newer thinking. Numbered `N1`…`N13` here — the source file numbers them 1…13, and plain numbers would collide with the idea list above.

| # | Priority | Tag | What | Notes / overlap |
|---|---|---|---|---|
| N2 | High | New Draft Window | The new-draft dialog **must** have a tag selector | Real gap, verified in code: the dialog only has a free-text `comma, separated, tags` input (`editor.component.html:769`); the tag-cloud picker with usage counts exists solely in the editor's own tag row (`:931-950`) |
| N4 | High | Export Window | Remove the channel text inputs — channels are chosen by clicking only | Was v1's `B4`; the "small channel icons" half of B4 is **not** in v2 |
| N5 | High | Export Window | An unticked destination shrinks or disables its whole settings block so it stops drawing attention | Extends the shipped B5 redesign (checkbox per destination), doesn't replace it |
| ~~N6~~ | High | Post Questionnaire | ~~Validate the registration-form inputs~~ | **Already done** — `RegistrationFieldValidator.IsValidName` (Core) enforced server-side in `BlogEndpoints.PostRegistrationAsync`, applied whenever a name is given rather than only when required. Verified 27.07.2026; this row had been stale |
| N7 | High | Status Page | **New "Posts Manager" page** — comments, likes, stats and private-post form management all move onto it. Sections: post management (pick a post, minimal edits), reactions & comments, statistics, forms (private posts only) | The big one. Absorbs today's `/stats` and `/comments` pages |
| N10 | High | Status Page | **Forms tab** — private posts only. Edit/delete a form, see who answered what and when, add a multiple-choice field type with a pie chart of the answer distribution | Depends on N7's page existing. Heaviest single item in the list |
| N12 | High | Status Page | **Form presets** — build a preset of questions once, pick which one to use before publishing a post | Depends on N10 |
| N13 | High | Export Window | Make the export window much wider — near the full available area. It's the most important window after the writing area | Interacts with N4/N5: worth doing the three as one pass over that modal |
| ~~N11~~ | Medium | Notifications | ~~Telegram DM when someone fills in a registration form~~ | **Already done** — same opt-in plumbing as comments/likes, and a failed DM never turns a successful registration into an error. Verified 27.07.2026; this row had been stale |
| N1 | Low | Drafts Page | Sort the table by the chosen column; resizable columns | |
| N3 | Low | UI | Small round count badges (e.g. "10 new comments") for things worth attention | Pairs naturally with N8 and with B23's since-last-session delta, which already computes "what's new" server-side |
| N8 | Low | Status Page | Comments/reactions tab: highlight new entries, mark them seen on hover | Same "seen" problem B23 solved for `/drafts` — reuse the `DraftStatSeen` idea rather than inventing a second one |
| N9 | Low | Status Page | Custom stats range: 7 days to 6 months, with magnetic notches at 7/14/30/60/90 days | Supersedes v1's `B1` (which said 1–180 days, notches at 14/30/90/180) |

**Grouping note**: N7 + N10 + N12 (+ N8, N9) are not five independent items — they all build one new Posts Manager page. Scheduling them apart would mean building that page's shell three times.

**Not in v2 but still live**: `B26` (interface language) is half-shipped — mechanism + 3 screens done, the rest of the UI still English. It dropped out of the rewritten file, but the work exists in the code and is tracked in `TASKS.md`; don't treat its absence here as cancellation.

**Still open from v1** (unchanged priorities, just not the current focus): `B10`, `B11`, `B12`, `B19` (Medium) and the whole Low block — `B2`, `B6`, `B7`, `B8`, `B9`, `B13`, `B15`, `B16`, `B17`, `B18`, `B20`, `B27`. Two of them deserve calling out: `B26` (interface language) is **half-shipped** — mechanism plus login/register/`/drafts`, the rest of the UI still English, tracked in `TASKS.md`; and `B12`'s "paragraph numbers don't render despite the setting being on" is a **bug**, not a feature request.

### Brainstorm v1 — 26.07.2026, still live

27 items with Marty's own High/Medium/Low priorities, executed as Phase 9 (see `docs/ROADMAP.md`). Shipped so far: `B3`, `B5`, `B14`, `B21`, `B22`, `B23`, `B24`, `B25`; `B26` half. The rest are open — see the note above. Numbering is the brainstorm's own (`B1`…`B27`).

| # | Priority | Tag | What |
|---|---|---|---|
| B3 | High | Private Posts | Visiting a private post without a token shows a **customizable registration form** (name, nickname, email, a social link) with an optional Google-Forms-style questionnaire attached (e.g. gaming experience, favourite genre) |
| B5 | High | Export Window | Redesign: a checkbox per destination (blog, Telegram, Twitter…), its settings unfold when ticked; a separate files/statistics section (incl. total size); **one** Publish button that posts everywhere configured at once |
| B14 | High | Translation | Auto-translate flow is broken: editing RU lights the EN badge, but switching to EN clears the dot after a second and only offers "delete translation". Needs a proper re-translate button plus the same progress indicator auto-translate has |
| B21 | High | Export Window | The channels menu moves out of the topbar into the top of the Export window |
| B22 | High | Topbar | Left→right: logo+name, divider, drafts button, draft-title field, Saved/Unsaved indicator. Right: Export, theme toggle, profile. **Download .cedar moves into Export; Import moves to the drafts page.** Stats/comments buttons move next to the settings button |
| B23 | High | Drafts Page | New column with view/reaction counts, including the delta since the previous session — possibly a small sparkline |
| B24 | High | Drafts Page | The table is wide on iPad but can't be scrolled horizontally |
| B25 | High | Workspace | Show the current draft's state somewhere: private or not, published or not; when published, a LIVE marker with links to the blog/Telegram post |
| B26 | High | Localization | Language picker in settings (RU/EN for now) — design for languages with very long words (German) not breaking the layout |
| B4 | Medium | Export Window | Remove free-text channel entry — pick only from configured channels; add small channel icons |
| B10 | Medium | UI | All popups behave identically: dimmed backdrop, closable only via the ✕ (or an action that implies closing, e.g. Publish) |
| B11 | Medium | Diff gutters | Diff markers sit above/below the changed lines instead of level with them; should highlight the whole changed region, not draw a thin bar |
| B12 | Medium | Workspace | The ruler is pointless as-is — it should overlay the writing area; paragraph numbers don't render despite the setting being on |
| B19 | Medium | Insert Window | Remaining Insert-group buttons (date, footnote, emoji) should all become popups, like the link insert |
| ~~B1~~ | Low | Stats Page | ~~Custom stats range~~ — superseded by `N9` and shipped with it (27.07.2026), then widened by `I8`. **Nothing in the backlog touches the stats page any more** |
| B2 | Low | UI | Load small channel icons in the channel list and stats window |
| B6 | Low | UI | Two entry points to settings today (account popover + toolbar) — keep only the toolbar one, change its icon to a gear |
| B7 | Low | UI | On iPad the account email overflows the screen edge — shift it left |
| B8 | Low | UI | The YouTube button is the only coloured one; make it monochrome like the rest |
| ~~B9~~ | Low | Insert Window | ~~Emoji panel overflows and has only 40 emoji~~ — **done 27.07.2026**: four captioned groups (~120 emoji), the popover scrolls instead of growing. Deliberately a hand-picked set, not a full Unicode table — that needs search, and search needs names in six UI languages |
| ~~B13~~ | Low | Workspace | ~~Toggle to reveal where content actually is~~ — **done 27.07.2026**, in the status bar. Paragraph marks only: in a contenteditable, spaces and tabs can't be drawn without either inserting characters that would reach the exported text or fighting the browser's whitespace handling |
| B15 | Low | Customization | Move Appearance + toolbar settings out of the settings page into a right-hand panel in the editor, so changes are visible live |
| B16 | Low | Customization | Custom accent colour picker; writing-area presets per target (Telegram, iPhone, iPad, Blog…) |
| ~~B17~~ | Low | Signature | ~~Make the end-of-post signature bold in Telegram~~ — **done 27.07.2026**. A linked signature is bolded *inside* the link, since Telegram renders bold within a link but not a link within bold |
| B18 | Low | YouTube | Let the author set the link text shown in Telegram (currently a fixed "Watch on YouTube") |
| B20 | Low | UI | Better icon for the drafts-list button |
| B27 | Low | AI | One AI button opening a popup with the operation choice + progress indicator; window locks during the run but the run can be cancelled (closing the window) |

**Conflict noted at import**: B22 reverses part of the 26.07.2026 topbar restructure done earlier the same day — `.cedar` download/import had just been moved *into* the topbar; B22 sends download to Export and import to the drafts page. B22 wins (it's the newer instruction).

| # | What | Why deferred |
|---|---|---|
| 1 | Pro Plus signature tier (rich links etc.) | Three signature tiers before a user base exists adds complexity without benefit |
| 2 | Emoji as a Header Slot type | Unclear value; manual emoji input breaks the automatic-slot model |
| 3 | Comment translation via AI | Blocked on a not-yet-built AI-credit metering system — unbounded per-comment API cost risk otherwise |
| 4 | General "redesign" | Needs to be broken into concrete pain points first, not scheduled as a monolithic item |
| 5 | Text alignment in the editor | Needs evaluation against Telegram HTML export limits before it can be scoped |

## Tech debt (acknowledged, non-blocking)

| # | What | Deadline / trigger |
|---|---|---|
| 1 | OS migration: Bullseye → a fresh 64-bit Raspberry Pi OS (gets arm64 + a newer .NET). Bullseye security support ends ~August 2026 | Separate session; the Pi also runs Freenove electronics projects, so timing needs to be coordinated with Marty |
| 2 | ~~SSH keys instead of password auth~~ | ✅ done 06.07.2026 (ed25519, passwordless deploy) |
| 3 | Cloud backup duplication (rclone → Google Drive/Dropbox) | Now relevant — the database holds real user data |
| 4 | .NET 8 EOL November 2026 → runtime upgrade alongside the OS migration (#1) | Bundled with #1 |

## Open questions (need Marty)

- Public name for the shared Cedar Clerk bot
- Domain strategy — direction resolved (ADR-020: hybrid, separate dedicated domain for tenant blogs, not `mooexe.dev` subdomains), but exact domain name, whether `blog.mooexe.dev` migrates, and subdomain-vs-path scheme for tenants are still open
- "Progressive reveal" visual effect for published posts — what exactly is wanted, since `SendRichMessageDraft` can't do it for channels (see `.claude/rules/telegram-bot.md`)
- Idea #9 (session cookie) — is the existing persistent-cookie behavior actually broken, or is this asking for something else?
- Idea #14 (AI features popup) — what specifically is wrong with the current `.ai-chip` toolbar popover?
- ~~`I7` (private-post watermark) — what should it actually be?~~ — answered 27.07.2026: tiled heavy semi-transparent text laid **over** the blog post; the editor only shows a marker icon. Still undecided and worth asking when it's built: is the text fixed per post, or per viewer (burning in the viewer's email would make it leak-traceable)?
- ~~`IB9` (profile button dead) — on which pages?~~ — resolved by reading the code 27.07.2026: `/drafts`, `/settings` and `/posts`, where the avatar was an inert `<span>`. Fixed
- `IB3` (RU load marks EN stale) — needs a live reproduction: what fires an autosave ~1.2s after a RU version loads? Nothing in the load path explains it
- `IT1` (delete zoom) — confirm zoom is genuinely broken and not just unused, before deleting the control
- `I12` vs `I14`/`B15` vs `IT2` — the settings page is being split, partly moved into the editor, and partly deleted, all at once. Needs one decision about the end state rather than three separate passes

~~Lifetime-deal pricing — yes/no~~ — resolved: yes, via the Founder/Lifetime invite-code plan (ADR-022, `docs/DECISIONS.md`, Idea #5).
