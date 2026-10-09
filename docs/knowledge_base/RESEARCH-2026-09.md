---
owner: marty
last_verified: 2026-10-09
source_of_truth_for: the API/terms research behind backlog rows T-127, T-169, T-170, T-171, T-443
guard: none
---

# API research, September 2026

Four research-only backlog rows, one section each. Every fact carries the URL it came from and the
date it was read; where a page refused an automated fetch, that is stated rather than guessed
around. Each section ends with a **Verdict** — what a scoping row on `docs/tasks/BACKLOG.md` would
say, or "drop". This file does not edit the board.

Cross-cutting rule inherited from ADR-223: no session puppeteering, no scraping, no "unofficial
API" against an account that owns a game. It shapes three of the four verdicts below.

---

## T-127 — Write APIs: IndieDB and LinkedIn

### IndieDB (ModDB)

- **No public API, read or write.** The ModDB/IndieDB team's API effort became **mod.io**, an
  in-game mod-management API and a separate company since February 2019
  (indiedb.com/company/modio/news/modio-is-now-independent, read 05.09.2026). It manages mods
  inside games; it has nothing to do with publishing news on IndieDB.
- The only machine-readable surface is **outbound RSS 2.0** (`www.indiedb.com/rss`,
  `rss.indiedb.com/articles`, `rss.indiedb.com/headlines`; read 05.09.2026). Read-only.
- The one community library, `moddb` on PyPI, says of itself: "This library is not endorsed by
  the ModDB team and remains a scraper" (moddb.readthedocs.io, read 05.09.2026). That is the
  ADR-223 category of tool, not a connector.
- **Unverified:** `moddb.com/terms-of-use`, `indiedb.com/terms-of-use` and the 2016 forum thread
  "Is there a ModDB API?" all answered HTTP 403 to a non-browser client on 05.09.2026. The
  scraping clause was not read; the site's refusal of automated clients is itself the answer to
  "would a scraper be welcome".

**Verdict (IndieDB):** same class as Steam and itch — a **copy target**, never a connector. Scoping
row: "IndieDB copy target — render the article body as HTML for IndieDB's news editor (its tag
allowlist to be checked by hand before the renderer is written; `CedarToItchHtmlRenderer` is the
sibling to start from), preview + clipboard card in the export rack. #integrations P3, S."

### LinkedIn

Two doors, and they answer different questions.

| | Share on LinkedIn (self-serve) | Community Management API (vetted) |
|---|---|---|
| Permission | `w_member_social` — "Post, comment and like posts on behalf of an authenticated member" | `w_organization_social` (pages), `r_member_social` (read, "approved users only") |
| Access | Open Permission, "available to all developers … via self-service" — add the product in the Developer Portal, no review (learn.microsoft.com/en-us/linkedin/shared/authentication/getting-access, read 05.09.2026) | "only available to registered legal organizations for commercial use cases only"; verified business email, legal name, registered address, privacy policy, app verified by the LinkedIn Page; Development tier first, Standard tier after a screencast review (…/marketing/community-management-app-review, read 05.09.2026) |
| Target | The member's **personal profile** — exactly the single-user case | Organization pages, comment reading, analytics |

What a personal-profile post costs, from the primary pages (all read 05.09.2026):

- **Endpoint.** `POST https://api.linkedin.com/rest/posts` with headers `Linkedin-Version: YYYYMM`
  and `X-Restli-Protocol-Version: 2.0.0`. "The Posts API replaces the ugcPosts API"; a person URN
  as `author` is supported ("finding by both person and organization authors" since version
  202301). The Share on LinkedIn guide still shows the legacy `/v2/ugcPosts`; the versioned path is
  the one to build on (…/marketing/community-management/shares/posts-api and
  …/contentapi-migration-guide).
- **Content.** Text, article, image, video, multi-image, poll. Article posts do **no URL scraping**
  — title, description and a thumbnail uploaded through the Images API must be supplied.
- **Rate limits.** Share on LinkedIn: **150 requests per member per day, 100,000 per application**
  (…/consumer/integrations/self-serve/share-on-linkedin). Limits generally are "not published" and
  are read off the app's Analytics tab; 429 on breach (…/shared/api-guide/concepts/rate-limits).
- **Token lifetime.** "all access tokens are issued with a 60-day lifespan"; refresh means
  re-running the OAuth flow (screen bypassed while the member is logged in and the token unexpired);
  "Programmatic refresh tokens are available for a limited set of partners"
  (…/shared/authentication/authorization-code-flow). A self-serve app therefore asks every user to
  re-authorize every 60 days — the connector needs a visible "reconnect" state, as X does.
- **The clause that matters.** LinkedIn API Terms of Use §3.1, item 26 on the "Don'ts" list:
  *"Use the Content or the APIs to automate posting on the LinkedIn Services."* And §4.1: *"You must
  not capture, copy, cache, or store any Content … except to the extent expressly permitted"*
  (linkedin.com/legal/l/api-terms-of-use, read 05.09.2026). A scheduled queue is automated posting
  by any plain reading; a user pressing Publish in the editor and the app forwarding it once is
  what the Share on LinkedIn product exists for. This is a reading of the terms, not legal advice;
  the schedulers that do queue to LinkedIn are vetted Marketing partners.

**Verdict (LinkedIn):** a connector is technically cheap — `IPublishTarget`, credential encryption
and an OAuth target (X) already exist — and the honest shape is narrow. Scoping row: "LinkedIn
connector, personal profile, **post-now only** (no scheduled send: API Terms §3.1(26)), Share on
LinkedIn self-serve, `/rest/posts`, text + article (own thumbnail) + image, 60-day re-auth with a
reconnect state in Settings → Integrations. #integrations P3, M." If post-now-only is not worth a
Settings block, the fallback is a copy target like IndieDB.

---

## T-169 — IGDB autofill for projects

**Source caveat.** `api-docs.igdb.com` answers HTTP 403 to non-browser clients (05.09.2026). Its
text was read through mirrors that quote it (publicapi.dev/igdb-com-api; the api-evangelist rules
file on GitHub) and the Twitch developer forum. `legal.twitch.com/legal/developer-agreement/` is
JS-rendered and yielded no text — its caching and attribution clauses are **unverified** here.

- **Keys.** A Twitch developer application (Twitch account, 2FA) gives a Client ID and Secret;
  a `client_credentials` token from `https://id.twitch.tv/oauth2/token` (~60-day lifetime) goes as
  `Authorization: Bearer` with the `Client-ID` header on every call. Server-side only — the secret
  lives in the systemd drop-in like every other provider key.
- **Rate limit.** "4 requests per second" per Client-ID and "up to 8 open requests"; 429 beyond.
  Irrelevant at Cedar Clerk's scale — one search per project created.
- **Terms.** The docs' own sentence: *"The API is free for non-commercial usage under the terms of
  the Twitch Developer Service Agreement"*; commercial needs go to `partner@igdb.com` (docs text via
  the mirrors above). An IGDB staff answer on the Twitch forum (discuss.dev.twitch.com, thread
  23567, 06.01.2020): *"When it comes to commercial use, you should be fine using our API as such
  currently"* — older and softer than the docs sentence, which governs. **Cedar Clerk is a paid
  SaaS, so this is a commercial use: one email to partner@igdb.com before the feature ships**,
  asking two things — commercial use as a project-creation helper, and storing cover art.
- **Caching / storing.** The docs-derived guidance is to cache reference data and "avoid bulk
  re-publishing the dataset". Storing the handful of fields a user picked into their own Project
  is not re-publishing; copying the cover image into our media store is the part to ask about.
  Safe default: link the cover by URL, do not copy the bytes.
- **Images.** `https://images.igdb.com/igdb/image/upload/t_{size}/{image_id}.jpg`, sizes such as
  `cover_big`, `720p`, `1080p`; `cover.image_id`, `width`, `height` on the Cover object.
- **Fields an autofill would pull** (Game endpoint, per the community schema mirror, read
  05.09.2026): `name`, `slug`, `summary`, `storyline`, `cover.image_id`, `first_release_date`,
  `genres`, `platforms`, `themes`, `game_modes`, `involved_companies` (developer/publisher),
  `websites` (official, Steam, itch, Discord, X, YouTube), `external_games` (store ids),
  `screenshots`, `videos` (YouTube ids), `url`, `game_type`, `game_status`.
- **Fit with the data model.** `Project` has `Name`, `Description`, `CoverUrl` — name, summary and
  the cover URL land directly. Release date, platforms and store links have no columns; either
  they become `EntityLink` rows / a tags string, or the first version drops them. Attribution: a
  "Data from IGDB" line on the create dialog costs nothing and pre-empts the question.

**Verdict:** buildable and small. Scoping row: "IGDB autofill in Create Project — server-side proxy
`GET /api/igdb/search?q=`, pick a result → Name, Description, CoverUrl (linked, not copied), store
links as EntityLinks; Twitch keys in the drop-in; 'Data from IGDB' attribution; **blocked on
partner@igdb.com confirming commercial use + cover storage**. Control goes into the existing
create-project dialog (UI-INVENTORY rule). #phase13 P3, S–M."

---

## T-170 — Telegram: auto-comment after N reactions / after a timer

Half of this row is already shipped, which the row does not know.

- **Reading reaction counters — done (ADR-205).** `TelegramBotService` names
  `UpdateType.MessageReactionCount` in `allowed_updates`; `TelegramEngagement.ApplyReactionsAsync`
  writes the tally onto `ChannelPost.ReactionCount`. Bot API: *"The bot must be an administrator in
  the chat and must explicitly specify "message_reaction_count" in the list of allowed_updates to
  receive these updates. The updates are grouped and can be sent with delay up to a few minutes"*
  (core.telegram.org/bots/api, Bot API 10.3 of 24.08.2026, read 05.09.2026). Nothing can be
  fetched — the count is push-only, so "after N reactions" fires within minutes of N, not at N.
- **Commenting on a channel post — possible, through the discussion group.** A channel comment is a
  reply in the linked supergroup to the post's automatic forward: Bot API `Message.is_automatic_forward`
  — *"True, if the message is a channel post that was automatically forwarded to the connected
  discussion group"*; `ReplyParameters.message_id` — *"Identifier of the message that will be
  replied to in the current chat"*. `message_thread_id` is not the tool: it is *"for forum
  supergroups and private chats of bots with forum topic mode enabled only"* (Bot API text as
  shipped in Telegram.Bot 22.10.3 XML docs). So the call is `sendMessage(chat_id = discussion
  group, reply_parameters.message_id = the automatic forward's id, text)`. ADR-205's comment
  counter already receives that forward (`ForwardOrigin` → channel + message id → `ChannelPost`);
  what is missing is **persisting the forward's message id** on `ChannelPost` — today it is read
  and discarded.
- **Preconditions the owner meets or the feature is off.** The channel must have a linked
  discussion group and the bot must be a member of it — the same membership ADR-205 already needs
  for comment counts; without it the screen says "no group", it does not invent a number. The
  comment is **signed by the bot**, not by the channel or the author — Telegram gives a bot no way
  to post as the channel. That is a product fact to show on the settings block, not a bug.
- **Shared-bot security (telegram-bot.md, T-359).** The update carries chat id + message id and is
  resolved to a `ChannelPost` through `Channels.TelegramChatId`, so the trigger is owner-scoped by
  construction. Two accounts may claim one channel, so the send is deduplicated **per
  `ChannelPost`** (a `FirstCommentSentAt` column), never per Channel row. The comment text comes
  from the owner's draft only; nothing from the update is ever echoed into the group.
- **Triggers.** "After N reactions": a threshold check inside `ApplyReactionsAsync` when the total
  crosses N and `FirstCommentSentAt` is null. "After T minutes": a Quartz job at publish + T
  (Quartz is already the scheduler). "Immediately": send right after the forward arrives.

**Verdict:** buildable, one to two sessions. Scoping row: "Telegram first comment — per-post text +
rule (immediately / after N reactions / after T minutes) in the export modal's Telegram step;
reply to the automatic forward in the discussion group; `ChannelPost.DiscussionMessageId` +
`FirstCommentSentAt` (migration); Quartz job for the timer; signed by the bot, off without a
group. #telegram #growth P3, M."

---

## T-171 — Game events and jams database

Which sources are machine-readable (all read 05.09.2026):

| Source | What exists | Machine-readable? | Terms |
|---|---|---|---|
| itch.io jams | `itch.io/jams`, `/jams/upcoming` (~4 pages of upcoming jams) | **No.** The server-side API has 16 endpoints (profile, games, download keys, purchases, collections, wharf) and no jam endpoint (itch.io/docs/api/serverside). GitHub issue itchio/itch.io#872 "API for game jams" has been open since 10.08.2018 with no staff reply. The `.xml` RSS trick the API overview offers for browse pages does not apply: `/jams/upcoming.xml` returns the HTML page | itch.io/docs/legal/terms has no scraping clause; ADR-223 still says no |
| Game Jolt jams | `gamejolt.com/games/jams` | **No.** JS-rendered app; the Game API (gamejolt.com/game-api/doc) covers scores, trophies and data storage for games, not jams | — |
| indiegamejams.com | "the most complete game jam calendar on the web", community-filled | **Not documented.** No iCal/API found; the calendar page was not reachable at a stable URL. letsmakeagame.net's calendar page simply points here and to itch | no license stated |
| Steam fests | partner.steamgames.com/doc/marketing/upcoming_events — **public, no login** | HTML only, no feed. Lists Next Fest **19–26 Oct 2026, 22 Feb–1 Mar 2027, 14–21 Jun 2027**, themed fests through June 2027, seasonal sales | Steamworks docs page; reading it by hand is what it is for |
| Conferences | GDC 2027 **1–5 Mar** (gdconf.com); gamescom 2027 **23–29 Aug**; PAX West 2026 **4–7 Sep**; PAX East 2027 **22–25 Apr** (paxsite.com); Global Game Jam (globalgamejam.org/calendar) | HTML only; dates announced once a year | — |

So nothing upstream is a feed. The honest design is a **hand-curated seed**, which is also what the
product needs: IndieViral's 750 rows are mostly itch jams (426), the long tail nobody plans a
content calendar around. The anchor set an indie actually schedules posts against is ~30 events a
year — three Next Fests, the seasonal sales, the genre fests that match the project, GDC, gamescom,
the PAXes, Global Game Jam, Ludum Dare, GMTK Jam.

- **Seed:** ~30 rows, one JSON file in the repo (Name, Kind, StartsAt, EndsAt, Url, Tags), loaded
  into an `IndustryEvent` table on startup; about two hours once.
- **Refresh:** by hand, monthly, ~30 minutes: Steam's upcoming-events page, the three conference
  sites, itch's upcoming list for the two or three jams worth naming. Weekly is not needed — the
  sources change a few times a year.
- **User's own events** (their jam, their launch) fit the same table, which is what makes the row
  worth building: the calendar from Wave 2 (ADR-226) shows both.
- **Not automated:** a Quartz job diffing Steam's HTML would work today and break silently on the
  next redesign; it is also scraping in the ADR-223 sense even though it reads a public doc page.

**Verdict:** scoping row: "Industry events seed — ~30 hand-curated anchor events as JSON, loaded
into `IndustryEvent`, plus user-added events; shown in the calendar; refreshed by hand monthly;
itch jams out of scope until issue #872 gets an API. #growth #calendar P3, S."

---

## T-443 — Miro: importing and exporting reference boards

Asked on 09.10.2026: can a reference board (ADR-218) go to Miro and come back.

### What Miro offers

- **REST API v2 reads and writes board items.** Item types: card, sticky note, text, shape,
  connector, app card, document, embed, frame, image (developers.miro.com/docs/rest-api-reference-guide,
  read 09.10.2026). `GET /v2/boards/{board_id}/items` lists them with an optional `type` filter.
- **Scopes:** `boards:read` reads boards, members and items; `boards:write` creates, updates and
  deletes them. Both are available on every Miro plan (developers.miro.com/reference/scopes, read
  09.10.2026). `boards:export` (PDF with comments) is Enterprise only and not needed here.
- **Rate limit:** 100,000 credits per minute per application; a call costs 50, 100, 500 or 2,000
  credits by level, which is 2,000 down to 50 calls a minute (developers.miro.com/reference/rate-limiting,
  read 09.10.2026). A 2,000-item board is well inside that either way.
- **Images are created from a URL** Miro fetches (`imageUrl`, same reference guide). Our board images
  live behind the media gate (ADR-219), so an export has to hand Miro a URL it can read.
- **Connectors join two items**; both ends must be items, never a free point
  (developers.miro.com/docs/work-with-connectors, read 09.10.2026).
- **Unverified:** the help-centre page on manual export (PDF, image, CSV; a "board backup" `.rtb`
  file on paid plans) answered HTTP 403 to an automated client on 09.10.2026; the per-item field
  schema (position, geometry, parent) and the experimental bulk-create endpoint's limits were not
  read from the reference. A `.rtb` backup is not a documented format and is not a route.

### What it means for our board

- Our model has four kinds — note, image, frame, link — with a position, a size, a rotation and a
  palette-token colour (`CanvasItem`, `CedarClerk.Core/CanvasPayload.cs`).
- **Export is the easy half:** note → sticky note, frame → frame, image → image, link → a card or
  text with the URL. Lossless enough to be honest about. It needs the user's OAuth grant
  (`boards:write`), and images need a short-lived readable URL.
- **Import is lossy by construction:** shapes, connectors, cards, text styling, embeds and documents
  have nothing to land in. A connector-heavy Miro board arrives as a pile of notes. Images must be
  downloaded and uploaded into the owner's library first (the `/media/` rule), and writes go through
  the hub's validation, 100 items a call, 2,000 a board.
- No scraping and no file-format reverse engineering (ADR-223); the API with the user's own grant is
  the only route.

**Verdict:** feasible, official, and a real connector's worth of work (OAuth app, token storage, two
mappers, an image bridge) for a one-time migration. Scoping row: "Miro export for a reference board —
OAuth (`boards:write`), map note/frame/image/link, images through a short-lived signed URL; import
only if the board model first grows connectors and shapes. #canvas #integrations P3, L." Cheaper
first step if the need is just "show this board to someone on Miro": a PNG export of the board,
which Miro accepts as an ordinary image.
