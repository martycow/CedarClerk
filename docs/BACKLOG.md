# Backlog

The **only** place to look for open, not-yet-started ideas/features/tech-debt — deliberately separate from `docs/ROADMAP.md`, which mixes this in with phase status and shipped work. If it's not started and not scheduled into a phase, it lives here; once it gets scoped into a phase, move the entry into `docs/ROADMAP.md` and delete it from here.

Migrated from `docs/ROADMAP.md` (16.07.2026 origin dates preserved) on 25.07.2026 — content unchanged in the move, only relocated.

## Ideas (not yet scoped into a phase)

| # | What | Notes |
|---|---|---|
| 1 | Cross-posting: split a post into parts and publish to Twitter/Bluesky | **Core positioning axis now, not a peripheral nice-to-have** (ADR-021, `docs/DECISIONS.md` — Cedar Clerk is a write-once-publish-everywhere tool, Telegram/blog/Twitter-Bluesky are co-equal destinations). Still not scoped into a phase. Needs an export setting to control social-preview (OpenGraph/card) visibility for those platforms — character-limit-aware splitting, not a straight dump of the Telegram render |
| 2 | Email confirmation on registration | Currently invite-code gated only, no email verification step at all — needs a provider decision (SMTP vs a transactional-email service) before scoping |
| 3 | Dedicated tag-management UI | Tags are currently edited inline per-draft (`PUT /api/drafts/{id}/tags`, Phase 6); Marty wants a separate menu to manage the tag set directly, and edits there must propagate to the live blog, not just the editor |
| 4 | Separate "draft name" from "article title" | `Draft.Title` currently serves both roles — it's the name shown in the drafts list AND the rendered article/post title. Needs its own field, and needs to be per-language (an EN translation currently inherits the RU title verbatim instead of having its own) |
| 5 | Founder / Lifetime plan via a designated invite code | Low cost, reuses existing invite-code registration infra — see ADR-022, `docs/DECISIONS.md`. Grants a permanent Pro tier (not Pro Plus, no AI); no new schema needed (`ApplicationUser.PlanExpiresAt = null` on a paid tier already means "never expires"). Open: the founder code's actual value |

### Idea dump 25.07.2026 (from Marty, via `/remote-control`)

| # | What | Notes |
|---|---|---|
| 6 | Loading indicator for import/export and other long operations | Broader than the existing AI-op indicator (Phase 8 Step 8 in `docs/ROADMAP.md` — currently just an elapsed-time counter, no real progress bar). Needs coverage on `.cedar`/Markdown import, blog export, and any other long-running action that currently gives no feedback |
| 7 | Tag picker/creator popup everywhere tags are used | A tag-cloud picker popover already exists in the editor (`editor.component.html:821-855`), but `Tag` isn't a normalized entity — `Draft.Tags` is a flat string column (`Entities.cs:132`), parsed/joined client-side. Decide whether to spread the existing UI pattern to more places as-is, or normalize the schema first |
| 8 | Show all tags on a blog post card | **Confirmed 25.07.2026**: `BlogEndpoints.cs:598-599` (post-list card rendering) only emits `tags[0]` — truncates to a single tag per card even when a post has multiple. The single-post page's own tag row (`BlogEndpoints.cs:734-737`, `post-tags-row`) already shows all tags correctly — the fix is making the card use the same pattern instead of `tags[0]` |
| 9 | Store `session_id` in a cookie for auto-login | **Likely already implemented** — ASP.NET Identity already sets a persistent auth cookie today (`isPersistent: true`, `AuthEndpoints.cs:61-71`); `auth.service.ts` relies on the browser cookie jar, no manual token handling. Confirm with Marty what's actually failing (session too short-lived? doesn't survive a browser restart?) before treating this as new work |
| 10 | Email confirmation at registration | Duplicate of idea #2 above — do not scope twice, just link back to #2 |
| 11 | Glossary of terms: term list, highlight recognized terms in text, click-to-view description popup, term-creation menu, auto-detect terms before posting | Large multi-part feature: new `Term`/`Definition` entity, text-scan pass (on save and/or pre-publish), inline TipTap decoration for recognized terms, click handler + description popover, term-authoring UI. Needs its own scoping session, not a quick add |
| 12 | Admin role + a dedicated user-management page (mini CRM); mark `cedarworks@mooexe.dev` as the first admin | No role/admin concept exists today at all — `ApplicationUser : IdentityUser` has no `IsAdmin`/role field, `Program.cs` calls `AddIdentityCore` without `.AddRoles(...)`, and `AuthEndpoints.cs` has zero role checks. Needs: role concept on `ApplicationUser` (or ASP.NET Identity roles), a migration, admin-only endpoints, and a new page listing/managing users |
| 13 | Appearance settings: a small live preview of how the editor/text will look | `settings.component.ts` already stores appearance preferences (`appearance.service.ts`), but there's no embedded preview pane reflecting the current selection live |
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

~~Lifetime-deal pricing — yes/no~~ — resolved: yes, via the Founder/Lifetime invite-code plan (ADR-022, `docs/DECISIONS.md`, Idea #5).
