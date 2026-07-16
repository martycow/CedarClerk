# Telegram bot rules

## 409 Conflict — only one process may *long-poll* the bot token
The Telegram Bot API allows exactly one process to long-poll (`getUpdates`) a given bot token at a time. `TelegramBotService : BackgroundService` (`CedarClerk.Server/Bot/TelegramBotService.cs`) polls in production on the Pi.

Before running the **full local dev server** with a real token (it starts its own `TelegramBotService` long-polling loop):
```
ssh martycow@raspberrypi.local "sudo systemctl stop cedarclerk"
```
...run/test locally, then:
```
ssh martycow@raspberrypi.local "sudo systemctl start cedarclerk"
```
Locally with no token configured, the bot is disabled by design — `TelegramBotService.IsRunning` returns false and `PostEndpoints`/export return 503 with a clear message instead of throwing. This lets local dev proceed without ever touching the Pi's bot process.

**This does NOT apply to a one-off `SendRichMessage`/`SendPhoto`/etc. call** (e.g. a diagnostic script that builds a `TelegramBotClient` and sends a single message) — the 409 is specific to concurrent `getUpdates`, not to ordinary send-type API calls. Don't stop the Pi service just to send a test message; stopping it also takes down `/media/*` (same process serves both — see below), which can actively break what you're trying to test.

## sendRichMessage — Bot API 10.2, Blocks is canonical (superseded 10.1 guidance below)
Bot API bumped to **10.2 on 14.07.2026**. Verified live against `@testingandfun` on 16.07.2026:
- **`InputRichMessage.Blocks` is the only mechanism that reliably embeds media with a real, natively-styled caption.** `CedarToTelegramBlocksRenderer` (Core) + the mapping in `PostEndpoints.ToInputRichBlock`/`ToRichText` (Server) is what actually gets used to send now — see ADR in `docs/DECISIONS.md`.
- `InputRichMessage.Markdown`/`.Html` + the `InputRichMessageMedia`/`tg://{kind}?id={Id}` reference mechanism (`InputRichMessageMedia.Id` docs literally describe this) is **accepted by Telegram without error but silently drops the media** — confirmed empirically, not just theorized. Don't reach for this combination expecting it to display an image.
- Within `Blocks`, media (`InputRichBlockPhoto`/`Video`/`Audio`) takes an `InputMediaPhoto`/`Video`/`Audio` object directly (no id-indirection) and a separate `Caption` (`RichBlockCaption`) that renders with real native caption styling (muted, small, tight under the media) — unlike a caption placed as plain text near a `Markdown`/`Html`-mode image, which is NOT styled and displays as ordinary body text since `InputMediaPhoto.Caption` is ignored outside of Blocks.
- `CedarToTelegramMarkdownRenderer`/`CedarToTelegramHtmlRenderer` are **kept but no longer used for sending** — see the "NOT USED" note at the top of each file before touching them.
- **Tag name lesson**: a photo block's tag is `<img>` (or, in Blocks, `InputRichBlockPhoto`) — **not** `<photo>`. An earlier commit renamed `<img>`→`<photo>` reacting to the same 10.2 change and got it backwards; `<photo>` is not a recognized tag and silently drops the media too, same as the Markdown/Html+id approach above.
- Media URLs must be publicly reachable — Telegram's servers download them server-side. `localhost` never works; see `Cedar:PublicBaseUrl` fallback in `PostEndpoints`. A stopped `cedarclerk` service on the Pi also kills `/media/*` (same process serves both) — Telegram's fetcher can hit a dead origin even when your own `curl` gets a lucky Cloudflare cache HIT, producing a confusing "wrong type of the web page content" error that looks like a Bot API problem but isn't.
- **`wrong type of the web page content` can also mean Telegram cached an earlier failed fetch of that exact URL** (verified 16.07.2026: a genuinely-reachable, correctly-served image kept failing until a `?cb=<timestamp>` cache-busting query string was appended to the same URL, after which it worked immediately). If a specific asset keeps failing for no visible reason while others work fine, suspect this before suspecting the renderer — it should self-resolve as Telegram's cache for that URL expires; a permanent code-level cache-buster is not warranted for one transient incident.
- **Empty media groups are rejected outright**: an `InputRichBlockSlideshow`/`InputRichBlockCollage` with zero child blocks (a `carousel`/`collage` TipTap node with an empty `images` array — a real editor artifact from repeated insert/delete) gets `Bad Request: RICH_MESSAGE_CONTENT_REQUIRED`. `CedarToTelegramBlocksRenderer` drops these nodes entirely rather than emit them (see ADR-019, `docs/DECISIONS.md`).
- **True preview only comes from sending to the test channel** `@testingandfun` ("Marty's Channel For Testing and Having Fun"). Local render is an approximation. **Never post to Dev Dairy Diary (the real channel) without explicit permission.**
- `SendRichMessageDraft` (ephemeral 30s preview, must be finalized with a normal `SendRichMessage`) only targets **private chats**, not channels — confirmed via Telegram.Bot XML docs. It cannot do a "progressive reveal" effect on a channel post; don't reach for it for that use case.
- `ExportRequest.Format`/`ScheduledPost.Format` (Markdown vs Html) are still accepted by the API/DB but **no longer change what actually gets sent** — `PublishAsync` always renders via Blocks now. The frontend's format selector is effectively vestigial; flagged as a cleanup candidate, not yet acted on.

## Chat/channel membership discovery
There is no "list my chats" Bot API call. The only way to learn the bot's channel/group memberships is listening for `my_chat_member` updates (`TelegramBotService.OnUpdateReceived`), which writes/updates `BotKnownChat` + `BotKnownChatAdmin`. `GET /api/channels/known` filters this cache by the current user's linked `TelegramUserId` — omitting that filter would leak every other user's discoverable channels, since the bot is shared across all accounts.
