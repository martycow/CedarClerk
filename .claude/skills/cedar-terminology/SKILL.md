---
name: cedar-terminology
description: Canonical Cedar Clerk vocabulary — the module map and the terms every session must use consistently (Draft/CedarJson, renderers, publish pipeline, series, tree, links). Load before writing code, docs, or reports so names stay byte-exact. Full definitions live in docs/knowledge_base/TERMINOLOGY.md.
---

# Cedar Clerk canonical terminology

Source of truth: `docs/knowledge_base/TERMINOLOGY.md` (~80 terms, each backed by a
code reference). Use these names exactly; never invent synonyms for existing terms.
(Skill pattern borrowed from Cowtext's cowtext-terminology.)

## Module map

| Project | Owns |
|---|---|
| `CedarClerk.Core` | Document format + renderers, pure C#: `CedarToTelegramBlocksRenderer` (canon for sending), `CedarToBlogHtmlRenderer`, legacy HTML/MD renderers, `CedarPackage`, `PublishValidator`, `Consts` |
| `CedarClerk.Server` | Minimal APIs (`XxxEndpoints` static classes), EF Core + SQLite, Identity, Quartz, Telegram bot host, blog host (`BlogEndpoints.HandleRequest`), static Angular host |
| `CedarClerk.Localization` | `ErrorMessages` (RU/EN pairs via `Ru()`, per-request culture), `Languages` |
| MooTool (external) | Rust/Ratatui `cedar` — the build/test/deploy entry point (ADR-291); Cedar Clerk owns `cedar.json` |
| `cedarclerk-web` | Angular 21 standalone + signals, TipTap 3; i18n `en.ts` defines shape, `ru.ts` is `typeof en` |
| `Modules/IndieDev` (Server) + module pages (web) | Phase 13 behind `Cedar:Modules:IndieDev` (ADR-101) — Project, tasks, planner, asset index, builds |

## Canon terms (use exactly these)

- **Draft** — the central content entity: a TipTap document with autosave,
  revisions, translations, tags, folder; since ADR-102 any `DocumentType`
  (`post/design/script/plot/changelog/note`), only `post`/`changelog` publish.
- **CedarJson** — the canonical document: TipTap JSON in `Draft.CedarJson`,
  stored as-is, never rewritten per network.
- **One document, many renderers** — the core idea: every output renders from the
  same CedarJson; no surface keeps parallel hand-written content.
- **Renderer invariants** — user text always escaped (`< > &`); every node/mark
  has a unit test; a node missing from `TipTapTextNodes`/`CedarPlainText`/
  `PublishValidator` is *silently dropped* from teasers or refused at publish —
  new nodes must be added to all three (`.claude/rules/renderers.md`).
- **Blocks is canon** — Telegram sends via `InputRichMessage.Blocks`
  (`CedarToTelegramBlocksRenderer`); the HTML/MD renderers are kept but NOT used
  for sending (`.claude/rules/telegram-bot.md`).
- **PublishJob** — durable row, one publish into one target (one per *thread
  part*); `Pending→Running→Succeeded|Failed|Unknown`; Unknown never retries.
  **PublishTarget / PublishNetworks** — connected account / string network keys
  (`"telegram"`, `"bluesky"`, `"x"` — strings by ADR-078, never an enum).
- **Primary language / translation** — the document on Draft is the primary
  (`PrimaryLanguage`); `DraftTranslation` rows are the others; staleness =
  timestamp comparison; wiki-link sync and revisions follow the primary only.
- **Series** (ADR-125) — an entity, not a tag: per-owner slug, `/series/{slug}`,
  order `(SeriesOrder, BlogPublishedAt)`; «Часть N из M» counts *visible* members
  only. Rename keeps the slug.
- **Document tree** (ADR-128) — `ParentDraftId` + `SiblingOrder`, depth ≤ 10,
  cycle-guarded; deleting a node lifts children to the grandparent. **Folder ≠
  tree**: folder is a flat list filter, tree is structure.
- **DocumentLink vs EntityLink** (ADR-128) — DocumentLink is *derived* from
  wikilink nodes on save, lives outside projects; EntityLink is the module's
  *stated* relation and requires a ProjectId. Never merge the two.
- **wikilink** — inline atom `{draftId, label}`; linked by id (rename-safe),
  label is a title snapshot; blog links only targets visible on the index,
  everywhere else the label travels as escaped plain text.
- **Semi-public** — `IsPrivate && IsListedWhilePrivate`: listed on the index with
  a lock and no teaser, body behind the registration gate; OG meta =
  title + static fallback only (ADR-124).
- **VisitorHash** — salted hash instead of raw IPs (ADR-016); blog geo stats are
  aggregates (`BlogViewGeoDaily`), never a visit trail. No cookies.
- **LIVE / LIVE-PREV** — local-only git tags marking production; never pushed.
- **Asset / TelegramLocalPath / TelegramFileId** — an upload, its Telegram-safe
  JPEG derivative, and the cached bot-scoped file id (ADR-088/089). The library
  page is **`/library`** — `/media/*` is the files' own URL space.

## Byte-exact zones

Entity/property names in `Entities.cs` (migration immediately after any change —
SchemaDriftGuard); `ErrorMessages` member names; `Consts` members incl.
`CurrentVersion`; i18n keys (`ru.ts` must typecheck against `en.ts`);
`PublishNetworks`/`DocumentTypes` string values (they live in SQLite and the API);
TipTap node type names (they live in every stored document forever).
