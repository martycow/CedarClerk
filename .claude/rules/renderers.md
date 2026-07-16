# Renderer invariants

`CedarClerk.Core` holds the renderers that turn one TipTap JSON document into multiple outputs — this is the architectural core of the app (see `docs/ARCHITECTURE.md` §"One document, many renderers"). Current renderers: `CedarToTelegramHtmlRenderer`, `CedarToTelegramMarkdownRenderer`, `CedarToBlogHtmlRenderer`.

Invariants that must hold for every renderer:
1. **User text is always escaped** (`< > &`) before being placed into HTML or Telegram markup — this is the only thing standing between a post body and injection into the rendered output.
2. **Every inline mark and block type must have a unit test** in `CedarClerk.Tests` (see `MarkdownRendererTests.cs`, `BlogHtmlRendererTests.cs`, and the HTML renderer tests currently in `UnitTest1.cs`/`RendererTests` — inconsistently named, worth renaming to `HtmlRendererTests.cs` if touched).
3. **Run `dotnet test` before any deploy that touches `CedarClerk.Core`.** As of the last verified state (11.07.2026) the suite was 162/162 green.
4. Telegram HTML mode has real markup quirks (non-void tag closing, block tag support) — see `.claude/rules/telegram-bot.md` for the specifics, since they directly constrain what `CedarToTelegramHtmlRenderer` can emit.
