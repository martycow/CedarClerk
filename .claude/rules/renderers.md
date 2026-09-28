# Renderer invariants

`CedarClerk.Core` turns one TipTap JSON document into every output (`docs/tech/ARCHITECTURE.md` §"One document, many renderers").

| Renderer | Status | Tests |
|---|---|---|
| `CedarToTelegramBlocksRenderer` | canonical for Telegram (`telegram-bot.md`) | `BlocksRendererTests` |
| `CedarToBlogHtmlRenderer` | live | `BlogHtmlRendererTests` |
| `CedarToItchHtmlRenderer` | live | `ItchHtmlRendererTests` |
| `CedarToSteamBbcodeRenderer` | live | `SteamBbcodeRendererTests` |
| `HeaderSlotRenderer`, `WatermarkRenderer` | live | own `*Tests` |
| `CedarToTelegramHtmlRenderer`, `…MarkdownRenderer` | legacy, not used for sending | `UnitTest1`, `MarkdownRendererTests` |

Invariants:
1. **User text is always escaped** (`< > &`, plus the target's own syntax) — the only barrier against injection.
2. **Every inline mark and block type has a unit test.**
3. **`dotnet test` before any deploy that touches Core**.
