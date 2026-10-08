namespace CedarClerk.Core;

/// <param name="Kind">One of <see cref="TelegramPreviewProjection.Kinds"/>.</param>
/// <param name="Text">Plain text by <see cref="TelegramThreadSplitter.PlainText"/>'s rules; empty for media.</param>
/// <param name="Urls">Media only: one entry for photo/video/audio, one per image for slideshow/collage.</param>
/// <param name="Items">Lists only: the items with their marker, so the phone draws bullets, numbers or checkboxes (ADR-313).</param>
public sealed record TelegramPreviewBlock(string Kind, string Text, IReadOnlyList<string> Urls, string? Caption,
    IReadOnlyList<TelegramPreviewListItem>? Items = null);

/// <param name="Order">The ordinal for a numbered list; null for a bullet or a checkbox.</param>
public sealed record TelegramPreviewListItem(string Text, int? Order, bool HasCheckbox, bool IsChecked);

public sealed record TelegramPreviewMessage(
    int Index,
    IReadOnlyList<TelegramPreviewBlock> Blocks,
    int Characters,
    int MediaCount,
    string CutReason,
    string StartsWith);

public sealed record TelegramPreviewButton(string Text, string Url);

/// <param name="Buttons">The CTA row Telegram attaches to the last message.</param>
/// <param name="FoldAfterCharacters">
/// Past this many characters the Telegram client hides a channel post behind "Show more"
/// (ADR-086). The phone draws a marker there inside the message; it is not a message boundary.
/// </param>
public sealed record TelegramPreview(
    string Language,
    int MessageCount,
    int Characters,
    int MaxCharactersPerMessage,
    int MaxMediaPerMessage,
    IReadOnlyList<TelegramPreviewMessage> Messages,
    IReadOnlyList<TelegramPreviewButton> Buttons,
    int FoldAfterCharacters = Consts.Telegram.ThreadPartChars);

// ADR-239 clause 10 — what Telegram would receive, as plain text over the same parts the send path
// computes, so the phone the editor draws cannot drift from the wire. Nothing here sends.
public static class TelegramPreviewProjection
{
    public static class Kinds
    {
        public const string Paragraph = "paragraph";
        public const string Heading = "heading";
        public const string List = "list";
        public const string Code = "code";
        public const string Quote = "quote";
        public const string Divider = "divider";
        public const string Table = "table";
        public const string Math = "math";
        public const string Details = "details";
        public const string Footer = "footer";
        public const string Photo = "photo";
        public const string Video = "video";
        public const string Audio = "audio";
        public const string Slideshow = "slideshow";
        public const string Collage = "collage";
    }

    public static TelegramPreview Project(
        string language,
        IReadOnlyList<ThreadPart> parts,
        IReadOnlyList<(string Text, string Url)> buttons,
        PublishCapabilities capabilities,
        bool thread = false)
    {
        var messages = parts
            .Select((p, i) => new TelegramPreviewMessage(
                i,
                p.Blocks.Select(ProjectBlock).OfType<TelegramPreviewBlock>().ToList(),
                p.Characters,
                p.MediaCount,
                p.CutReason,
                p.StartsWith ?? ""))
            .ToList();

        return new TelegramPreview(
            language,
            messages.Count,
            messages.Sum(m => m.Characters),
            // ADR-313 — one message is counted against the message limit; only a thread is counted
            // against the per-part budget.
            thread
                ? capabilities.ThreadPartCharacters ?? capabilities.MaxCharacters ?? Consts.Telegram.ThreadPartChars
                : capabilities.MaxCharacters ?? Consts.Telegram.MaxPostChars,
            Math.Max(1, capabilities.MaxMediaItems),
            messages,
            buttons.Select(b => new TelegramPreviewButton(b.Text, b.Url)).ToList());
    }

    private static TelegramPreviewBlock? ProjectBlock(CedarRichBlock block) => block switch
    {
        RichParagraphBlock p => Text(Kinds.Paragraph, PlainText(p.Text)),
        RichHeadingBlock h => Text(Kinds.Heading, PlainText(h.Text)),
        RichListBlock l => new TelegramPreviewBlock(
            Kinds.List,
            Lines(l.Items.Select(i => Lines(i.Blocks.Select(InnerText)))),
            [],
            null,
            l.Items.Select(i => new TelegramPreviewListItem(
                Lines(i.Blocks.Select(InnerText)), i.OrderValue, i.HasCheckbox, i.IsChecked)).ToList()),
        RichCodeBlock c => Text(Kinds.Code, c.Code),
        RichQuoteBlock q => Text(Kinds.Quote, Lines(q.Blocks.Select(InnerText))),
        RichExpandableQuoteBlock eq => Text(Kinds.Quote, Lines(eq.Blocks.Select(InnerText))),
        RichDividerBlock => Text(Kinds.Divider, ""),
        RichTableBlock t => Text(Kinds.Table, Lines(t.Rows.Select(r => string.Join(" | ", r.Select(c => PlainText(c.Text)))))),
        RichMathBlock m => Text(Kinds.Math, m.Latex),
        RichDetailsBlock d => Text(Kinds.Details, Lines([PlainText(d.Summary), .. d.Blocks.Select(InnerText)])),
        RichFooterBlock f => Text(Kinds.Footer, PlainText(f.Text)),
        RichPhotoBlock p => Media(Kinds.Photo, [p.Url], p.Caption),
        RichVideoBlock v => Media(Kinds.Video, [v.Url], v.Caption),
        RichAudioBlock a => Media(Kinds.Audio, [a.Url], a.Caption),
        RichSlideshowBlock s => Media(Kinds.Slideshow, s.Urls, null),
        RichCollageBlock c => Media(Kinds.Collage, c.Urls, null),
        // Invisible in-message jump target for TOC links; nothing to draw on the phone.
        RichAnchorBlock => null,
        _ => null,
    };

    private static string InnerText(CedarRichBlock block) =>
        ProjectBlock(block) is { } inner ? (inner.Text.Length > 0 ? inner.Text : inner.Caption ?? "") : "";

    private static TelegramPreviewBlock Text(string kind, string text) => new(kind, text, [], null);

    private static TelegramPreviewBlock Media(string kind, IReadOnlyList<string> urls, RichRun? caption) =>
        new(kind, "", urls, caption is null ? null : PlainText(caption));

    private static string PlainText(RichRun? run) => TelegramThreadSplitter.PlainText(run);

    private static string Lines(IEnumerable<string> lines) => string.Join("\n", lines);
}
