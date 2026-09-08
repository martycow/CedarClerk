---
owner: marty
last_verified: 2026-09-05
source_of_truth_for: project terminology — what the words the code and docs use mean
guard: none
---

# Cedar Clerk Terminology

A glossary of project terms — things that can't be googled, because the meaning is local to this project.
Extracted from the code and the live docs on 18.08.2026 (five parallel passes + a merge); every term is
confirmed against a source. General technical words (SQLite, OAuth) are not included here. The reader-facing
post glossary is a different thing: it's a blog feature (`GlossaryTerm`), not this file.

## Document and publishing

| Term | Russian | Meaning | Source |
|---|---|---|---|
| Draft | черновик | The central content entity: a TipTap document with autosave, revisions, translations, tags and a folder; since ADR-102 — a document of any type inside a Project, not just a post | `Entities.cs`, ARCHITECTURE §Data model |
| CedarJson | — | The canonical internal document format — TipTap JSON in `Draft.CedarJson`; stored as-is and never rewritten for a specific network | `Entities.cs:256` |
| DocumentType | тип документа | A string on Draft (ADR-102): `post/design/script/plot/changelog/note`; default `post`, only `post` and `changelog` are publishable | `Core/DocumentTypes.cs` |
| One document, many renderers | «один документ, много рендереров» | The core of the architecture: one CedarJson is rendered into every output; no surface carries parallel hand-maintained content (the sole exception is override text) | ARCHITECTURE §Core idea |
| Renderer | рендерер | A pure C# class in Core that turns CedarJson into a destination format; the canonical one for Telegram is `CedarToTelegramBlocksRenderer`; invariants: escaping `< > &` + a test for every node | ARCHITECTURE:29-33, rules/renderers.md |
| copy target | копи-таргет | An export destination that renders to the clipboard instead of sending: the export card shows a rendered preview and a copy button (Steam BBCode, itch.io HTML), no connector and no credentials — automation on those storefronts is the anti-feature, not a gap | `Core/CedarToSteamBbcodeRenderer.cs`, ADR-223 |
| DraftSearch | — | The FTS5 virtual table behind full-text search — created by raw SQL (`AddDraftSearchFts`) and deliberately invisible to the EF model; kept in sync by triggers + a SaveChanges interceptor + a startup backfill | ADR-224 |
| preview link | превью-ссылка | A revocable read-only URL for an unpublished draft: `Draft.PreviewToken` → public `/preview/{token}`, noindex, no account needed; revoking makes the page a 404 | `Entities.cs`, CHANGELOG 29.08 |
| PublishTarget / IPublishTarget | таргет | Entity: the owner's connected account on one network ("where a post can go"); interface: name itself, describe its limits, send — and nothing more | `Publishing/IPublishTarget.cs`, `Entities.cs:843` |
| PublishNetworks | сеть | String keys for networks — `"telegram"`, `"bluesky"`, `"x"`; strings, not an enum: the value lives in SQLite, the API and Angular (ADR-078) | `Core/PublishCapabilities.cs:5-14` |
| PublishCapabilities | возможности сети | A network's limits as data, not behavior: MaxCharacters, MaxMediaItems, Supports*, DerivesShortPost — the editor reads them and warns before sending | `Core/PublishCapabilities.cs` |
| PublishJob | джоб очереди публикаций | A durable row for "one publish to one target" (ADR-081): Pending→Running→Succeeded\|Failed\|**Unknown**; Unknown (the process died mid-send) is never retried — a blind retry would turn one post into two | `Entities.cs:767+`, `Publishing/PublishJobRunner.cs` |
| PublishOutcome / Receipt | — | A send result in place of an exception: a Receipt (RemoteId; PublicUrl may be null) or a readable error + status code | `Publishing/IPublishTarget.cs:46,66` |
| Micro-thread | микроблог-тред | ADR-094: the whole document goes out as an X/Bluesky reply chain — split in the network's own units, numbered "N/M", with a blog link on the last part | `Core/MicroThreadSplitter.cs` |
| Thread part | часть треда | One PublishJob per **part**, not per thread (T-106): a thread that failed on part 4 resumes from part 4; ThreadPartRef carries ReplyTo and Root | `Entities.cs:779-787` |
| Override text | авторский текст под сеть | `DraftTargetText` per (draft, network, language): a cross-post is a standalone post, not an announcement (ADR-077); empty → a teaser, publishing is never blocked | `Entities.cs:818-835` |
| Teaser | тизер | An automatic short post with a link to the blog — the fallback when override text is empty; the DerivesShortPost capability turns "too long" from a rejection into information | `Entities.cs:823-825`, ADR-093 |
| PrimaryLanguage | основной язык | The draft's primary language (ADR-064): the document on the Draft itself is canonical and written in it; Russian by default | `Entities.cs:257-259` |
| DraftTranslation | перевод черновика | A version of the document in another language: its own Title and CedarJson at (DraftId, Language) | `Entities.cs:494-511` |
| SourceSnapshotJson | снапшот исходника перевода | A snapshot of the source CedarJson taken at the moment a translation was synced — gives a block-by-block "what changed since then" diff instead of a boolean "stale" flag | `Entities.cs:504-510` |
| DraftRevision | ревизия | An immutable history broken down by language: a row for every explicit save and every publish (Kind: save\|telegram\|blog\|restore) — an edit history and a safe diff before publishing. `save` rows are capped at the newest 50 per draft and language and at 90 days (pruned on the next save and by the nightly `PruneSaveRevisionsJob`); publish and restore rows are never pruned | `Entities.cs`, `DraftRevisionService.cs`, ADR-065 |
| ShrinkGuard | — | When a save requires explicit confirmation: an incident on 29.07 where autosave saved an instantaneously-empty editor; "suspicious" means there were ≥200 visible characters and ≤20% remain | `Core/ShrinkGuard.cs` |
| .cedar (CedarPackage) | формат .cedar | Document export/import: a zip container (analogous to .docx) with document.json + assets/; translations are deliberately not included in the export | `Core/CedarPackage.cs`, ARCHITECTURE:130-132 |
| assetId (node attribute) | ссылка на ассет | The one link from a media node (image/video/audio) to the `Asset` it was inserted from, `data-asset-id` in the DOM. The file's own facts — resolution, byte size, name — are never copied into the document: they belong to the file and go stale when its bytes change, so the inspector asks `GET /api/assets/meta` (by id, or by media path for documents older than the attribute) | ADR-238, `core/selection-spec.ts` |
| queue slot | слот очереди | `QueueSlot` — a weekly posting moment (UTC weekday + minute) on one destination; `FillQueueSlotsJob` keeps its upcoming occurrences filled from the evergreen pool, one occurrence per ISO week — which is why a dragged ticket is never double-filled | `Entities.cs`, ADR-228 |
| evergreen | эвергрин | `Draft.IsEvergreen` plus category/max-sends/until bounds: membership in the pool queue slots repost from, picked least-sent-first; `EvergreenSendCount` counts only sends that actually went out, never fills | `Entities.cs`, `Bot/FillQueueSlotsJob.cs` |
| CTA buttons | CTA-кнопки | Up to 3 URL buttons under a Telegram post — `Draft.CtaButtonsJson`, a per-post send setting appended at the wire level to the publication's last part; never a document node, never on the blog or in `.cedar` | ADR-226 |
| tracked link | трекинговая ссылка | `TrackedLink` — a `/l/{code}` short redirect with per-day click tallies; counters, not visit logs: nothing per-visitor is kept, bots count, and the UI says so | `Entities.cs`, `TrackedLinkEndpoints.cs` |
| member flow | приток и отток подписчиков | The invite-link analytics: joins/leaves per (channel, UTC day, invite link) from `chat_member` updates — `ChannelMemberDaily`, a daily aggregate that never stores who; a mute is not churn | ADR-227 |
| pre-publish checks | предпубликационные проверки | The export modal's checks shelf: `POST /api/posts/preflight` (empty language versions, dead links) plus the client's alt-text pass — warnings that never block a send. Not the same word's other meaning, `cedar deploy --preflight` | `PreflightEndpoints.cs`, CHANGELOG 29.08 |
| PublishTargetStatSnapshot | снапшот статистики таргета | The X/Bluesky counterpart of `ChannelStat`: one row per (PublishTarget, UTC day) with followers and the engagement sums the network's public API answers, written by `SnapshotPublishTargetStatsJob` at 04:10 UTC; the Stats tab grows a leaf per target the moment a row exists | `Entities.cs`, `Publishing/SnapshotPublishTargetStatsJob.cs`, ADR-273 |
| stat series | серия статистики | `GET /api/stats/series?days&sources` — every selected source over **one aligned window** (the same day axis for all of them, nulls where a source has no snapshot), plus `available[]`: every source the account has, in leaf-strip order, so the client never fans out per source; the window delta replaces Δ7d, and `series.csv` is the same query as a file | `StatSeriesEndpoints.cs`, ADR-279 |
| Telegram sync | синхронизация с Telegram | `POST /api/posts/{id}/telegram-sync` — edits the one channel message the last send left (`LastTelegramMessageId`) with the current document; "edited since" is measured against `Draft.LastTelegramSentAt`; single-message posts only (a thread answers 409), and Telegram's own verdict is the answer | `PostEndpoints.cs`, ADR-275, ADR-278 |

## Blog and readers

| Term | Russian | Meaning | Source |
|---|---|---|---|
| annotation | аннотация | A TipTap node marking a fragment of an article for anchored reactions/comments; null = "the whole article"; a blog-only concept | `Core/CedarToBlogHtmlRenderer.cs` |
| article title | читательский заголовок | `Draft.ArticleTitle`: the title shown to the reader, kept separate from the draft's working name ("devlog 14 (final final)" is a filename, not a title) | `Entities.cs` |
| private post | приватный пост | A post with `IsPrivate`, published to the blog but reachable only by invite/registration; to anyone else the page is indistinguishable from a 404 | `BlogEndpoints.cs` |
| semi-public post | полупубличный пост | `IsListedWhilePrivate`: a private post shown in the index as a card with a lock icon and no teaser — it changes "what gets advertised," not "what's readable" | `Entities.cs`, `BlogEndpoints.cs` |
| registration gate | регистрационный гейт | A form (B3) shown to an uninvited visitor of a private post instead of a 404; submitting it immediately sets a cookie — audience collection, not verification | `BlogEndpoints.cs`, `Core/RegistrationFormSet.cs` |
| form preset | пресет формы | A named, reusable form (N12): applying it **copies** JSON into the draft — editing the preset afterward does not change the live post | `Entities.cs` (FormPreset) |
| invite | инвайт на приватный пост | `PostInvite` — a row per invited email; the `?invite=` token issues a signed cookie; deleting the row revokes access immediately | `Entities.cs`, `BlogEndpoints.cs` |
| reader access token | токен доступа читателя | `PostRegistration.AccessToken` (T-064): an accessUrl generated after the form so access can carry over to another browser; revocation targets one reader | `Entities.cs` |
| revoked registration | отозванная регистрация | `PostRegistration.IsRevoked`: access has been revoked, the row stays for history | `Entities.cs` |
| watermark | водяной знак | `WatermarkText`, tiled across a private post's sheet (a base64 SVG tile in the CSS background): deterrence, not protection | `Core/WatermarkRenderer.cs` |
| copy protection | защита от копирования | `DisableCopy`: blocks selection/copy/context-menu on a private post's sheet; from the same deterrence family as the watermark | `BlogEndpoints.cs` |
| visitor hash | хеш посетителя | SHA-256 of the IP with a salt — an anonymous identity without storing raw IPs: reaction dedup (ADR-016), "one vote," form throttling | `BlogEndpoints.cs` |
| reaction | реакция | An anonymous like/dislike on an article or annotation, one per (draft, annotation, visitor); clicking again removes it | `Entities.cs` |
| poll vote | голос в опросе | Polls — a blog-only block (ADR-055): one vote per (poll, visitor), changing the answer updates the row | `Entities.cs`, `BlogEndpoints.cs` |
| view counter | счётчик просмотров | `ViewCount`: an atomic UPDATE plus a 30-minute cookie for dedup, shared across all languages of a post (ADR-023) | `BlogEndpoints.cs` |
| geo rollup | гео-сводка | `BlogViewGeoDaily`: a daily aggregate by (owner, day, country, language) from CF-IPCountry/Accept-Language; an aggregate, not a visit log (ADR-097) | `Entities.cs`, `Core/ReaderGeo.cs` |
| header slots | слоты шапки | Up to 3 configurable subheading elements (byline/URL/location/date/length/read time/word count/views); the third is Pro | `Core/HeaderSlotRenderer.cs` |
| cross-link | кросс-ссылка | Mutual links between a post's surfaces ("Watch on Telegram" ↔ "Read on the blog"), with owner-facing text that is localizable (I15) | `Consts.CrossLinks` |
| press kit page | пресс-кит | `/games/{slug}/press` — a press page rendered from the showcase plus five optional press fields, with a downloadable press-pack zip; self-updating because it is a renderer over existing data, not a maintained document | UI-INVENTORY §Blog surfaces, CHANGELOG 29.08 |
| user glossary | пользовательский глоссарий | `GlossaryTerm` — the owner's own terms (per-owner, per-language, with case-form aliases), highlighted in the blog with a tooltip; only on first occurrence | `Core/GlossaryScanner.cs` |
| GlossaryTermUsage | использование термина | One row per (term, draft) pair that actually matched, written when either half changes — a draft save rescans that draft, a term write rescans that term. A pair at zero has no row rather than a row holding `0`. The scan runs on plain text and ignores `DraftGlossaryExclusion`: an exclusion is a publishing decision, and "where is this term used" is a question about the text. The field is `usedInDrafts`, never `usedInPosts` — since ADR-102 a post is one `DocumentType` of six | `Entities.cs`, `GlossaryUsage.cs`, ADR-238 |
| shadowed term | перекрытый термин | Two terms of one owner and language whose spellings intersect compete for the same position, and `CountHits` credits exactly one — project-scoped before global (ADR-112), then `CreatedAt`, then `Id`. The loser carries `shadowedByTermId` naming the winner, because a bare `0` on it would read as "appears nowhere", and those two readings call for opposite actions. Two terms in *different* projects can never meet, so they never collide; spellings are compared case-insensitively unless **both** terms are case-sensitive | `GlossaryEndpoints.cs`, ADR-238 clause 13 |

## Tenancy and hosts

| Term | Russian | Meaning | Source |
|---|---|---|---|
| tenant | тенант | The account itself: there is no `Tenant` table and no `TenantId` column — `OwnerId` **is** the tenant id, and everything a tenant owns is everything carrying that id (ADR-206) | `Tenancy/TenantContext.cs`, ADR-206 |
| TenantUsername | имя тенанта | The account's DNS label (`marty` in `marty.cedarclerk.app`), lowercase, under a filtered unique index. Deliberately not `Username`: Identity owns `UserName`, and EF's differ matched the two case-insensitively. Required at registration, and there is no rename endpoint | `Entities.cs`, `Core/Usernames.cs` |
| tenant subdomain | поддомен тенанта | `<name>.<Cedar:TenantHost>` — one account's public blog and nothing else: routing's endpoint pick is dropped there, `/index.html` is 404, and a signed-in identity is discarded (ADR-210) | `Tenancy/TenantHost.cs`, `Program.cs` §TenantRouting |
| reserved subdomain | зарезервированный поддомен | Labels the platform keeps for itself (`www`, `api`, `admin`, `blog`, mail and DNS names): never assignable at registration, never resolved as a tenant | `Consts.ReservedSubdomains` |
| owner filter | фильтр владельца | The EF query filter `e.OwnerId == TenantId` on 34 entities. A tenant context and a platform context compile to **two different models**, so a platform query has no owner predicate in it rather than a disabled one (ADR-207) | `CedarDbContext.ApplyTenantFilters`, `Tenancy/TenantModelCacheKeyFactory.cs` |
| platform scope | платформенный скоуп | A scope that legitimately reads across owners — the admin panel, the publish queue, Quartz jobs, the bot, provider webhooks, startup — and has to declare itself: `PlatformPaths` for requests, `CreatePlatformScope()` off the request thread. **Unset is not platform**: it reads nothing (ADR-208) | `Tenancy/TenantProvider.cs`, `Tenancy/TenantScopes.cs`, `Tenancy/PlatformPaths.cs` |
| BlogSite | блог-сайт | Owner + host as one value, threaded explicitly through every blog render path in addition to the ambient filter, so a page's scoping can be tested rather than assumed | `Tenancy/BlogTenant.cs` |
| legacy blog host | легаси-хост блога | `blog.mooexe.dev` — one account's blog like any other, except its owner is looked up (`Cedar:BlogOwner`, a name) instead of read off the hostname. That owner keeps that host in every URL the app hands out (ADR-209) | `Tenancy/BlogTenant.cs` |
| media gate | гейт медиа | A file inherits the audience of the published posts that reference it: referenced by a public post it is public, referenced only by private ones it needs their grant. Keyed by asset, so a `_tg` derivative is no way around it; every refusal is a 404 (ADR-211) | `MediaAccess.cs` |
| orphan sweep | уборка сирот | `GET/POST /api/admin/maintenance/orphans` — rows whose parent is already gone, left by earlier incomplete deletes. Counted and purged by two separate calls, so the number can be looked at first | `OrphanSweep.cs`, ADR-213 |

## Plans and money

| Term | Russian | Meaning | Source |
|---|---|---|---|
| PlanTiers | уровни тарифа | A byte enum Free(0)/Pro(1)/ProPlus(2)/Forever(3); plan strings: pro $3, proplus $6, trial → tier ProPlus | `Core/PlanTiers.cs`, `Consts.Plans` |
| TTFP | время до первой публикации | Time to first publish — the median from `AspNetUsers.CreatedAt` to the first publish (the min over Succeeded `PublishJob` and `BlogPublishedAt`); an aid to the activation metric from BUSINESS §4 | METRICS §3.1 (ADR-126) |
| PlanLimitations | лимиты тарифов | The central gate by tier: 1/3/10 channels, signature from Pro, AI from ProPlus, 3 slots vs. 2, storage quotas | `Core/PlanLimitations.cs` |
| storage quota | квота хранилища | 100 MB / 1 GB / 3 GB / 100 GB (ADR-129); the residual risk is media sitting on the machine's disk until step 2 of T-172 | `PlanLimitations.cs`, MULTITENANCY §1 |
| Trial | триал за $1 | $1 for 7 days of ProPlus, once per account (`TrialUsedAt`); in the metrics it's an "intent filter" | `Consts.Plans`, BUSINESS §4 |
| Forever / Founder code | вечный тариф основателя | A permanent Pro plan (100 GB, no AI) granted through a separate founder invite with no payment flow; "forever" = PlanExpiresAt=null; given out at jams | ADR-022, BUSINESS §6 |
| Grace | грейс-период | 2 days on top of the 30-day subscription — so renewal-webhook lag doesn't "flicker" a user down to Free | `Core/SubscriptionPlanHelper.cs` |
| credit | кредит | An internal currency for usage that costs real money (an X post = 1 credit, cost basis ~$0.20); priced at ~2× cost | `Core/CreditPacks.cs` |
| credit wallet | кошелёк кредитов | A prepaid wallet (ADR-092): balance = SUM(Delta) over the ledger; topped up through the same payment flows as subscriptions | `CreditWallet.cs` |
| CreditEntry | леджер кредитов | A single movement (+purchase/−publish); a ledger, not a counter — the balance has an audit trail; a unique (Reason, Ref) pair gives idempotency | `Entities.cs` |
| CreditReasons | причины движения | The ledger's dictionary, shared with the UI: purchase / x-post / admin-grant | `Core/CreditPacks.cs` |
| credit pack | пакет кредитов | 10/$4, 50/$18, 100/$30 (Stars 200/900/1500⭐); payload "credits-{pack}:{user}" — the same {what}:{who} scheme as plans | `Core/CreditPacks.cs` |
| AiDailyLimit / AiUsage | дневная AI-квота | 20 calls/day per user; up to 600/month on the $6 ProPlus plan — the only line item able to make the plan unprofitable | `PlanLimitations.cs`, BUSINESS §3 |
| attribution signature | подпись-атрибуция | Free always gets "Published with Cedar Clerk" with a link (an upgrade hook); Pro+ replaces or removes it; the gate is centralized in `ResolveSignature` (ADR-034) | `PlanLimitations.cs` |
| InviteCode | инвайт-код | Real codes (IF2): Code/Label/MaxUses/ExpiresAt; deactivated, not deleted — deleting would erase attribution; falls back to config | `Entities.cs`, ADR-122 |
| Payment.ExternalId | дедуп платежа | The Stripe session / Stars charge / PayPal order id — protection against duplicates on repeated webhooks | `Entities.cs`, `BillingEndpoints.cs` |
| FreeChannelSwitchCooldown | кулдаун смены канала | On Free the channel can be switched no more than once every 7 days — otherwise one account could serve several channels in rotation | `PlanLimitations.cs` |
| activation | активация | Metric #1: the share of signups who published ≥1 post within their first week; low = an onboarding problem, too early to buy traffic | BUSINESS §4 |
| churn (отток) | — | Metric #3: paying users who didn't renew within a month; threshold 5%/month (nearby: #2 is Free→paid conversion, #4 is MRR excluding one-off payments) | BUSINESS §4 |

## Indie module and desktop

| Term | Russian | Meaning | Source |
|---|---|---|---|
| Project | проект | A game as a container for documents, tasks, sprints and an asset index; always holds ≥1 document, is archived rather than deleted | `Entities.IndieDev.cs` |
| ProjectType / CreatedFromPreset | тип проекта, пресет создания | The creation offer is empty/blog/fullgame/product/work/vault; the retired jam/prototype/released values were folded into fullgame/product by the ProjectModules migration. Stored on `Project.CreatedFromPreset`: it decides the starting document and the initial module rows at creation, then keeps only its icon and vocabulary (ADR-293) | `Core/ProjectTypes.cs` |
| ProjectModule | модуль проекта | One switch of one project — a `(ProjectId, ModuleKey, Enabled)` row per key of `ProjectModules.All` (documents, assets, site, posts, calendar, metrics, tasks, planner, builds, canvas, dialogues), created with the project, off rows included. Off hides a section and deletes nothing; `documents` never goes off; the API is `PUT /api/projects/{id}/modules` and the `modules` map on every project DTO. Navigation still ignores it until 14a | `Core/ProjectModules.cs`, `Entities.IndieDev.cs`, ADR-293 |
| GameTask | задача | A set of fixed fields, not a document (ADR-106 boundary): Description is plain text; named GameTask because Task is taken by async | `Entities.IndieDev.cs` |
| Sprint | спринт | A name + two dates + a non-reusable number from the project's counter (not MAX+1); no status column — planned/current/finished is derived from the dates (ADR-111) | `Entities.IndieDev.cs`, `Core/SprintStates.cs` |
| Build | билд | A record of a game version: number, notes, date, contents; an entity, not a tag, and knows nothing about git (ADR-112) | `Entities.IndieDev.cs` |
| changelog generator | генератор чейнджлога | `POST /api/builds/{id}/changelog`: assembles a changelog-type document from the build's notes and its closed tasks — an ordinary document from there on | `Modules/IndieDev/BuildEndpoints.cs` |
| EntityLink | связь сущностей | A manual link between two things in a project (a generalization of TaskLink, T-141); "used in" can't be detected — only stated by hand | `Entities.IndieDev.cs` |
| reference board (канвас) | референс-борда | `CanvasBoard` — an endless surface inside a project for images, notes, frames and links; the moodboard of T-129 and the whiteboard of T-155 turned out to be one screen (ADR-218) | `Entities.Canvas.cs`, INDIEDEV |
| canvas item | элемент борды | `CanvasItem` — one thing on a board: geometry and z-order are columns (a drag writes numbers, order sorts in SQL), everything kind-specific is one `Payload` JSON replaced whole | `Entities.Canvas.cs`, `Core/CanvasItemKinds.cs` |
| ProjectMember | участник проекта | A non-owner account with access to a project. `OwnerId` is the **project owner**, `MemberUserId` the invitee — otherwise either the owner can't see the row or the invitee's tenant owns it (ADR-217) | `Entities.Collab.cs`, ADR-217 |
| ProjectRoles | роль в проекте | `editor` writes, `viewer` reads; strings like TaskStatuses. **Owner is not a role** — it's identity, and the owner has no member row: a column that could say "owner" would let a row be edited into ownership | `Core/ProjectRoles.cs` |
| ProjectAccess | доступ к проекту | The answer to "may this person touch this project", resolved once per request or hub call through one platform-scope lookup, then everything runs in the owner's tenant scope. Null = not even readable | `Modules/IndieDev/ProjectAccess.cs` |
| invite token | токен приглашения | 32 random bytes mailed to an address and cleared on accept. **The token is the credential** — the accepting account's e-mail is deliberately not compared with the invited one (aliases, second accounts); the same model as ShowcaseFollower's tokens | `Entities.Collab.cs`, ADR-217 |
| last writer wins | побеждает последний | The board's conflict rule: every write from a permitted writer is accepted, `Version` is bumped server-side and broadcast. Not optimistic locking — the server never refuses a stale write; `Version` only lets a client drop an echo older than what it holds | ADR-218 |
| board group | группа борды | `canvas:{boardId}` — the SignalR group everything about one board is broadcast to; persisted changes go to the whole group including the sender, transient ones (cursor, selection, live drag) to the others | `Modules/IndieDev/CanvasHub.cs` |
| presence | присутствие | Who is on a board, their cursors and selections — in-process memory in the hub, never a row: meaningless a second after a disconnect, and a row per cursor move would be the heaviest write in the app. A second server instance would need a backplane | `Modules/IndieDev/CanvasHub.cs`, ADR-218 |
| project journal | журнал проекта | `GET /api/projects/{id}/activity` — a union of timestamps the module already writes, not a new log: `ActivityKinds` (document created/updated, blog published, task created/…) with one item shape `{at, kind, title, subtitle, href}`, drawn on the hub as log lines; the publishing nudge is one date on the same list | `Modules/IndieDev/ProjectEndpoints.cs`, ADR-277, ADR-281 |
| asset index | индекс ассетов | A cloud-side description of the project folder: a single root (`AssetRootPath`), scanned by the agent, in batches of 500; a missing file is flagged MissingSince, not deleted | `Entities.IndieDev.cs`, INDIEDEV |
| AssetEntry | строка индекса | One file: path + metadata + preview; the bytes are not copied. Deliberately not `Asset` — that's uploaded media with its own quota | `Entities.IndieDev.cs` |
| AssetKinds | вид ассета | A kind by extension (image/model/audio/…): opening tens of thousands of files isn't feasible; `SkippedDirectories` filters out engine caches; `.blend` has a built-in preview | `Core/AssetKinds.cs`, `Core/BlendThumbnail.cs` |
| filesystem agent (агент ФС) | агент файловой системы | The same `CedarClerk.Server.exe`, run with `Cedar:Agent:Enabled`: an early exit in Program.cs — no database, no Identity, only `/agent/*`; reads the disk, writes and launches nothing (ADR-117) | `Modules/Agent/`, DESKTOP |
| bearer token per launch | токен на запуск | The agent's first lock: 32 bytes per launch, passed via env, never written to disk; without the token everything is refused | `Modules/Agent/AgentEndpoints.cs` |
| granted roots | выданные корни | The second lock: folders granted by a human gesture (an OS dialog), compared by path with a trailing separator; these live in the shell, never fetched from the server | `Modules/Agent/AgentGrants.cs` |
| fingerprint | отпечаток | A preview + metadata standing in for a file from a different machine; the "Fingerprint" chip, "Show in Explorer" hidden; the normal screen state once the index is shared | DESKTOP §"File or fingerprint" |
| file or fingerprint (файл или отпечаток) | — | The asset screen's central question: `isLocal()` compares sourceMachine.id against the bridge's machine, defaulting to "false" — a browser has no bridge | DESKTOP, UI-INVENTORY |
| sourceMachine | машина-источник | A stable machine GUID from `machine.json` (the name updates, the id never does), stored in `Project.AssetRootMachineId/Name` | `CedarClerk.Desktop/main.js` |
| thumbs budget | бюджет превью | The ceiling on `thumbs/` volume in the cloud (2 GB per owner by default) — one of the numeric limits on the open write channel of the index | `Consts.cs:91`, DESKTOP |
| working material | рабочий материал | Non-publishable types (design/script/plot/note): rejected on the shared publish path for every network at once | `Core/DocumentTypes.cs` |
| DialogueScript | диалог | One Yarn dialogue: the whole node graph as `GraphJson` (`{id,title,x,y,body}`, bodies in Yarn syntax); edges are derived from the bodies' own jumps, never stored (ADR-230) | `Entities.IndieDev.cs` |
| line id (метка строки) | метка строки | The `#line:` tag stamped onto every localizable body line at save (`YarnDialogue.EnsureLineIds`) — the key the xlsx translation sheet trades in; existing tags are never rewritten | `Core/YarnDialogue.cs`, ADR-230 |
| DialogueLineTranslation | перевод строки | One translated line: (script, line id, language) → text, upserted by the xlsx import; empty cells skipped, unknown ids kept rather than refused | `Entities.IndieDev.cs`, ADR-230 |
| Cedar:Modules:IndieDev | флаг модуля | Turns the whole module on; turning it off returns the app to its pre-module state entirely — reversibility lives in the flag, not in a branch (ADR-101) | `Program.cs`, INDIEDEV |

## Operations and process

| Term | Russian | Meaning | Source |
|---|---|---|---|
| cedar CLI | операционная консоль | Native Rust/Ratatui operations console; the build/test/deploy entry point, installed from MooTool’s `modules/cedar` crate, with named JSON program profiles | AGENTS.md, ADR-252 |
| pipeline | пайплайн | Ordered build/test/deploy operations in MooTool’s `modules/cedar/src/operations.rs`, `deploy.rs` and the selected `cedar.json` profile | ADR-252 |
| preflight | префлайт | `cedar deploy --preflight`: runs every check (branch, tag, LIVE agreeing with the version, a remote probe) — and stops, touching nothing | CLAUDE.md |
| GitGuard | — | The pre-deploy checks: master only, not detached, a clean tree, a tag matching CurrentVersion; `test`/`build` deliberately do not apply them | `Pipelines/GitGuard.cs` |
| LIVE / LIVE-PREV | теги LIVE | Local-only tags (never pushed): LIVE = the commit in production, moved after a health check; the one it displaces stays as LIVE-PREV; `--rollback` moves it back | CLAUDE.md, ADR-118 |
| StageBoard | доска этапов | The long-running-operation screen in cedar: the whole step plan is drawn up front and checked off as it goes; the steps themselves know nothing about the board | `Pipelines/StageBoard.cs` |
| droplet / periwinkle | дроплет | Production: the DigitalOcean droplet `cedarclerk-periwinkle` (fra1, 1 vCPU / 2 GB, no swap, Ubuntu 24.04), which replaced the Pi on 11.08.2026 | rules/production-environment.md |
| Cloudflare Tunnel | туннель | The only way into production: cloudflared → Kestrel on loopback:8080; the droplet exposes nothing but SSH | rules/production-environment.md |
| healthchecks ping | пинг сторожа | A ping to healthchecks.io at the end of the nightly backup.sh — a silent backup failure raises an alert; the off-box half has its own check | rules/production-environment.md §Backups |
| off-box backup / R2 | внешний бэкап | The second half of backup.sh (T-147): rclone into Cloudflare R2 — deliberately not DO Spaces (so it doesn't live in the same account as the droplet); stays silent while there are no keys | rules/production-environment.md |
| smoke suite | smoke-сьют | Playwright via `Scripts/e2e.ps1`: the script owns the server process, a scratch database and the browser, with the bot off; `cedar test --smoke` | CLAUDE.md, ADR-070 |
| contrast check | контраст-чек | `check-contrast.mjs` inside `cedar test`: color pairs in both themes across every stylesheet under `src/` and the CSS the server writes from C#; a rule is judged in whichever palette its own selector chain's conditions carry, and the accent is run against every preset | ADR-137, ADR-152 |
| live-verify | живая верификация | Manual verification on top of the automated tests; the "not verified live" checklist lives in TASKS.md | ADR-070, DOCS-FLOW |
| pseudo-locale | псевдо-локаль | Padded-out strings standing in for translations to check layout (`?pseudo=1`); never lands in a profile | UI-INVENTORY |
| Cedar Bench | Cedar Bench | The app's single look: paper, wood, pine, brass. The values are both base blocks of `styles.scss`; a continuation of the "Cabin" set's direction, rewritten in place rather than added alongside it | ADR-136, ADR-137 |
| bench form controls | бенч-контролы формы | `app-input` / `app-select` / `app-textarea` / `app-checkbox` in `bench/forms/` — the kit's own form fields, so no screen dresses a native control by hand; `app-input` passes `maxlength` through and takes an accessible name without a visible label | `bench/forms/*.component.ts`, ADR-260 |
| area preset | пресет области | A named macro over the sheet controls — `telegram` / `iphone` / `ipad` / `blog` patch sheet width, font size and line height at once — applied from the Appearance panel; a shortcut, not a fourth measure | `core/area-presets.ts`, ADR-288 |
| custom accent / `--accent-ink` | свой акцент | A user-chosen accent hex, admitted only if it clears 3:1 on the paper (else the bench accent stands in); `--accent-ink` is the ink to paint over an accent fill, light or dark by the accent's own luminance — set today, consumed by nothing yet (T-373) | `core/appearance.service.ts`, `styles.scss`, ADR-288 |
| skeleton loader | скелетон | `app-skeleton` — one placeholder component (text/card/avatar/table-row) with a shimmer under the motion tokens, held for at least 300 ms so it never flashes, and `aria-busy` on the region it stands in for | `shared/skeleton.component.ts`, ADR-286 |
| tree drag grip | ручка перетаскивания | The handle on a `/drafts` tree row that starts a cdk drag: one flat drop list, the drop's depth read off sideways travel, so a node moves under a new parent without a nested drop-list per level | `pages/drafts.component.html`, ADR-283 |
| density mode | режим плотности | Two densities (ADR-071): comfortable by default, `[data-density="compact"]` on table-heavy screens; only spacing/sizes differ — never a color | DESIGN |
| surface class | класс поверхности | `data-surface` set to `chrome` or `paper` on an element — a second axis of density, not of palette: chrome has a minimum hit box of `--hit-chrome` (30px), paper has `--hit-target` (44px). Inherited via `--hit-surface`, never chosen by pinning a surface | ADR-138, ADR-156 |
| fidelity contract | контракт соответствия | A list of divergences between the port and the Cedar Bench prototype, checked against both code and pixels and broken down by file zone (tokens, chrome, primitives, screens); each row is either a defect for the fixer or an ADR-bound divergence the fixer must not touch | CHANGELOG 22.08, ADR-174…176 |
| brass ink | латунные чернила | `--brass-ink` — what a stamp or a tick graphic is drawn with; measured as a pair. Not to be confused with `--brass-lo` — the hook's metal, which nobody reads and no pair measures; `--brass-soft` is mixed from the ink | `styles.scss`, DESIGN §Color — dark |
| tally (work ticket) | тэлли, рабочий ярлык | The waiting-work count hung on a tool hook ("what is waiting behind that screen" — `HookRailItem.badge`), drawn as a tilted work ticket with a punched hole; shares the index-tab badge's resin ground and ink so chrome carries one badge dialect. Not a control — chrome's 30px hit floor deliberately does not apply | `hook-rail.component.ts` |
| pickable leaf | выбираемый лист | `app-leaf-tag` with `interactive`, not `dried`: a filter that can be clicked — at rest it sits on pale "dry" paper, active it turns green. A leaf-tag (not interactive) stays green in its single state | `leaf-tag.component.ts`, ADR-176 |
| margin note | заметка на полях | The global `.margin-note` rule — the only place the handwritten `--font-note` font is allowed: margin captions and empty states, `--fs-17`, `--t2`, with a tilt | `styles.scss`, DESIGN §Typography |
| ADR / index | ADR-лог | "ADR first, then code": the texts are one file per ADR in `docs/adr/`, `DECISIONS.md` is the index; reversing a decision is a new ADR on top, not an edit | DECISIONS, DOCS-FLOW |
| board (борда) | таск-борда | `docs/tasks/BACKLOG.md` — the only list of what's open; finished rows are deleted, the history lives in git | DOCS-FLOW |
| T-xxx / Q-xx | ID борды | Stable identifiers: T for tasks, Q for questions to Marty; the original numbering (B*, N*, I*…) is kept in parentheses in the descriptions | DOCS-FLOW |
| Input sweep | разбор инбокса | Reconciling the inbox's briefs against the code (they can be older than the code) → board/ROADMAP + an "Input sweep" entry in ROADMAP; the file's rewrite is dated by that entry | DOCS-FLOW |
| INPUT_PROMPT inbox | промпт-инбокс | `docs/INPUT_PROMPT.md` — the single inbox since 18.08.2026; deliberately not committed, Marty rewrites it wholesale | DOCS-FLOW |
