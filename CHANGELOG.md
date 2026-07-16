# Changelog

Human-readable, grouped by session/date, derived from `git log` (33 commits, `6ace957`→`6065cd9`) and the richer context already captured in `docs/ROADMAP.md`/`docs/DECISIONS.md`. Not a raw commit dump — see `git log` directly for that.

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
