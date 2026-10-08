# Telegram bot rules

Bot API **10.3** (24.08.2026), `Telegram.Bot` **22.10.3**. Production host: see `production-environment.md`.

## 1. One long-poller per token (409 Conflict)

`TelegramBotService` long-polls `getUpdates` in production. A second poller → 409 on both sides. It starts on every local launch, so this applies even to a plain `curl` check. (Three incidents: 13.07 / 26.07 / 09.08.2026.)

| Need the bot? | Do this |
|---|---|
| No (UI, HTTP) | `mkdir -p CedarClerk.Server/wwwroot && ASPNETCORE_ENVIRONMENT=LocalNoBot ASPNETCORE_URLS=http://localhost:8080 Cedar__Telegram__BotToken=' ' dotnet run --project CedarClerk.Server --no-launch-profile`. **Confirm `Cedar:BotToken not set — bot is disabled` in the log** before anything else. |
| Yes | `ssh martycow@periwinkle.mooexe.dev "sudo systemctl stop cedarclerk"` → run → `… start cedarclerk`. Stopping also kills `/media/*`. |

Traps:
- **`--no-launch-profile` is mandatory** — `launchSettings.json` pins `Development` in every profile, which loads the real token.
- **Key is `Cedar__Telegram__BotToken`** (`Consts.Telegram.BotTokenCfg`), not `Cedar__BotToken`. Whitespace = absent; an *empty* value removes the override and exposes the file's token.
- **`CedarClerk.Server/wwwroot` must exist** (`mkdir -p`), or startup throws `DirectoryNotFoundException`.
- One-off send calls (`SendRichMessage`, `SendPhoto`, …) don't conflict — don't stop prod for a test message.

## 2. Sending: `sendRichMessage` + Blocks only

Pipeline: `CedarToTelegramBlocksRenderer` (Core, → `RichBlockModel`) → `TelegramPublishTarget.ToInputRichBlock`/`ToRichText` (Server) → `SendRichMessage`.

- **Blocks is the only mode that shows media with a native caption** (`InputRichBlockPhoto/Video/Audio` + `RichBlockCaption`). Markdown/Html + `InputRichMessageMedia`/`tg://…?id=` is accepted and **silently drops the media**.
- Photo tag is `<img>` / `InputRichBlockPhoto`, **never `<photo>`** (silently dropped).
- 10.3 pieces in use: `InputRichBlockButtons` (CTA rows from `Draft.CtaButtonsJson`, wire-level only, never stored), `InputRichBlockExpandableBlockQuotation` (paragraph-only). **Not yet used:** `InputRichBlockDocument`, `RichMessageButton`, `rich_message` in `editEphemeralMessageText`.
- Empty `Slideshow`/`Collage` → `RICH_MESSAGE_CONTENT_REQUIRED`; the renderer drops them (ADR-019).
- `CedarToTelegramHtmlRenderer`/`MarkdownRenderer` are legacy, not used for sending.
- `SendRichMessageDraft` works only in **private chats** — no channel "progressive reveal".

## 3. Media delivery

- **Own media goes by `file_id`** (ADR-088/089): `SendRichMessage` is JSON-only, so a stream in Blocks → `media not found`. Each file is pre-uploaded once via `SendPhoto/Video/Audio` to the owner's private chat with the bot (self-deleting); `file_id` cached on `Asset.TelegramFileId` + `TelegramFileIdSourcePath`.
- Fallback per file → stamped URL. Global kill-switch: `Cedar:Telegram:MediaDelivery=url` in the systemd drop-in.
- URL media must be public (`Cedar:PublicBaseUrl`; `localhost` never works).
- `wrong type of the web page content` = dead origin (service stopped) **or** Telegram's negative cache of an earlier failed fetch. Own `/media/` URLs get a per-send `?v=<stamp>` (`StampUrl`, ADR-091); the stored document never does. **Never stamp external URLs** — `img.youtube.com` 404s on unknown query strings.

## 4. Shared bot = security boundary (T-359)

One token serves every account. Keep these closed:
- `POST /api/channels` checks the **caller** is admin/creator (`BotChatAccess.IsAdminOrCreator`); no linked Telegram → cannot connect.
- Comments count only in the discussion `Supergroup` and only as replies to an `IsAutomaticForward` (`TelegramEngagement.ApplyCommentAsync`).
- Asset queries in `TelegramPublishTarget` carry explicit `a.OwnerId == request.OwnerId` (queue runs with tenant filter off).
- `refresh-known-chats` walks only the caller's admin chats.
- Chat discovery: only via `my_chat_member` → `BotKnownChat`/`BotKnownChatAdmin`; `GET /api/channels/known` **must** filter by the caller's `TelegramUserId`.
- One Telegram user ↔ one account (unique filtered index). One channel ↔ many accounts is allowed.

## 5. Channels

- Preview = send to test channel **@testingandfun**. Local render is approximate.
- **Never post to Dev Dairy Diary without explicit permission.**
