namespace CedarClerk.Core;

// Framework-agnostic tree mirroring Telegram Bot API 10.2's structured Rich Message model
// (InputRichBlock*/RichText*) without CedarClerk.Core taking a dependency on Telegram.Bot.
// CedarClerk.Server maps this onto the real wire types (see PostEndpoints.ToInputRichBlock).

public abstract record RichRun;
public sealed record RichRunText(string Text) : RichRun;
public sealed record RichRunBold(RichRun Inner) : RichRun;
public sealed record RichRunItalic(RichRun Inner) : RichRun;
public sealed record RichRunUnderline(RichRun Inner) : RichRun;
public sealed record RichRunStrike(RichRun Inner) : RichRun;
public sealed record RichRunCode(RichRun Inner) : RichRun;
public sealed record RichRunSpoiler(RichRun Inner) : RichRun;
public sealed record RichRunLink(RichRun Inner, string Url) : RichRun;
public sealed record RichRunDateTime(string FallbackText, long UnixSeconds, string Format) : RichRun;
public sealed record RichRunMath(string Latex) : RichRun;
public sealed record RichRunSequence(IReadOnlyList<RichRun> Runs) : RichRun;

public abstract record CedarRichBlock;
public sealed record RichParagraphBlock(RichRun Text) : CedarRichBlock;
public sealed record RichHeadingBlock(int Level, RichRun Text) : CedarRichBlock;
public sealed record RichListBlock(IReadOnlyList<RichListItem> Items) : CedarRichBlock;
public sealed record RichListItem(IReadOnlyList<CedarRichBlock> Blocks, bool HasCheckbox, bool IsChecked, int? OrderValue);
public sealed record RichCodeBlock(string? Language, string Code) : CedarRichBlock;
public sealed record RichQuoteBlock(IReadOnlyList<CedarRichBlock> Blocks) : CedarRichBlock;
public sealed record RichDividerBlock : CedarRichBlock;
public sealed record RichPhotoBlock(string Url, RichRun? Caption) : CedarRichBlock;
public sealed record RichVideoBlock(string Url, RichRun? Caption) : CedarRichBlock;
public sealed record RichAudioBlock(string Url, RichRun? Caption) : CedarRichBlock;
public sealed record RichSlideshowBlock(IReadOnlyList<string> Urls) : CedarRichBlock;
public sealed record RichCollageBlock(IReadOnlyList<string> Urls) : CedarRichBlock;
public sealed record RichTableBlock(IReadOnlyList<IReadOnlyList<RichTableCell>> Rows) : CedarRichBlock;
public sealed record RichTableCell(RichRun Text, bool IsHeader, int? Colspan, int? Rowspan);
public sealed record RichMathBlock(string Latex) : CedarRichBlock;
public sealed record RichDetailsBlock(RichRun Summary, IReadOnlyList<CedarRichBlock> Blocks, bool IsOpen) : CedarRichBlock;
public sealed record RichFooterBlock(RichRun Text) : CedarRichBlock;
