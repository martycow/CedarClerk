namespace CedarClerk.Core;

/// <summary>Why a part ended where it did — shown in the preview, so a cut is never a surprise.</summary>
public static class ThreadCutReason
{
    /// <summary>The next block opened a section, and a section boundary is where a reader would break anyway.</summary>
    public const string Heading = "heading";

    /// <summary>The next block would not fit in the character budget.</summary>
    public const string Size = "size";

    /// <summary>The next media item would not fit — a network takes only so many per message.</summary>
    public const string Media = "media";

    /// <summary>The last part. Nothing was cut here; the document simply ended.</summary>
    public const string End = "end";
}

/// <param name="StartsWith">
/// The heading this part opens with, or its first words — the only thing that makes a numbered
/// part recognisable in a preview list.
/// </param>
public sealed record ThreadPart(
    IReadOnlyList<CedarRichBlock> Blocks,
    int Characters,
    int MediaCount,
    string? StartsWith,
    string CutReason);

/// <summary>
/// Splits a document that does not fit one message into a thread of messages (T-106).
///
/// The rules, in the order they matter:
///
/// 1. **A cut is placed at a heading whenever one is near.** A message that ends mid-section reads
///    like a transmission error; one that ends where a section ends reads like a chapter. The
///    splitter therefore takes a heading boundary as soon as the part is <see cref="ComfortableFill"/>
///    full, rather than filling every message to the brim.
/// 2. **A block is never split.** A table, a code block, a list or a media group crosses no
///    boundary — half a table in one message and half in the next is worse than a shorter message.
/// 3. **Both limits are honoured** — characters and media count. A document can exceed either one
///    on its own, and a hundred images will cut a thread long before its text does.
///
/// Nothing here decides *whether* to split: that is the author's call (T-106's UI), and this only
/// answers what the split would look like.
/// </summary>
public static class TelegramThreadSplitter
{
    /// <summary>
    /// How full a part must be before a heading is worth cutting at. Below this a heading is
    /// simply the next section of a still-short message; above it, ending here is a favour.
    /// </summary>
    public const double ComfortableFill = 0.6;

    /// <summary>Longest a preview label runs before it stops being a label.</summary>
    private const int LabelLength = 60;

    public static IReadOnlyList<ThreadPart> Split(IEnumerable<CedarRichBlock> blocks, PublishCapabilities capabilities)
    {
        var maxChars = capabilities.MaxCharacters ?? int.MaxValue;
        var maxMedia = Math.Max(1, capabilities.MaxMediaItems);

        var parts = new List<ThreadPart>();
        var current = new List<CedarRichBlock>();
        var chars = 0;
        var media = 0;

        void Close(string reason)
        {
            if (current.Count == 0) return;
            parts.Add(new ThreadPart(current.ToList(), chars, media, Label(current[0]), reason));
            current.Clear();
            chars = 0;
            media = 0;
        }

        foreach (var block in blocks)
        {
            var blockChars = Characters(block);
            var blockMedia = MediaCount(block);

            // A heading that arrives once the part is comfortably full ends it here rather than
            // three paragraphs later. This is the rule that turns "eight slices" into "eight
            // sections" and is the whole reason the preview is readable.
            if (current.Count > 0 && block is RichHeadingBlock && chars >= maxChars * ComfortableFill)
                Close(ThreadCutReason.Heading);

            if (current.Count > 0 && chars + blockChars > maxChars)
                Close(ThreadCutReason.Size);

            if (current.Count > 0 && media + blockMedia > maxMedia)
                Close(ThreadCutReason.Media);

            current.Add(block);
            chars += blockChars;
            media += blockMedia;
        }

        Close(ThreadCutReason.End);

        // A single block bigger than the whole budget still ships as its own part: refusing here
        // would leave the author with a document that cannot be published at all and no reason why.
        return parts;
    }

    /// <summary>Characters a block contributes — what the network counts, not what it renders.</summary>
    public static int Characters(CedarRichBlock block) => block switch
    {
        RichParagraphBlock p => Length(p.Text),
        RichHeadingBlock h => Length(h.Text),
        RichCodeBlock c => c.Code.Length,
        RichQuoteBlock q => q.Blocks.Sum(Characters),
        RichListBlock l => l.Items.Sum(i => i.Blocks.Sum(Characters)),
        RichDetailsBlock d => Length(d.Summary) + d.Blocks.Sum(Characters),
        RichTableBlock t => t.Rows.Sum(r => r.Sum(c => Length(c.Text))),
        RichMathBlock m => m.Latex.Length,
        RichFooterBlock f => Length(f.Text),
        RichPhotoBlock p => Length(p.Caption),
        RichVideoBlock v => Length(v.Caption),
        RichAudioBlock a => Length(a.Caption),
        _ => 0,
    };

    public static int MediaCount(CedarRichBlock block) => block switch
    {
        RichPhotoBlock or RichVideoBlock or RichAudioBlock => 1,
        RichSlideshowBlock s => s.Urls.Count,
        RichCollageBlock c => c.Urls.Count,
        RichQuoteBlock q => q.Blocks.Sum(MediaCount),
        RichDetailsBlock d => d.Blocks.Sum(MediaCount),
        RichListBlock l => l.Items.Sum(i => i.Blocks.Sum(MediaCount)),
        _ => 0,
    };

    private static int Length(RichRun? run) => PlainText(run).Length;

    private static string Label(CedarRichBlock block)
    {
        var text = block switch
        {
            RichHeadingBlock h => PlainText(h.Text),
            RichParagraphBlock p => PlainText(p.Text),
            RichQuoteBlock q => q.Blocks.Count > 0 ? Label(q.Blocks[0]) : "",
            RichPhotoBlock or RichSlideshowBlock or RichCollageBlock => "",
            _ => "",
        };
        text = text.Trim();
        return text.Length <= LabelLength ? text : text[..(LabelLength - 1)].TrimEnd() + "…";
    }

    private static string PlainText(RichRun? run) => run switch
    {
        null => "",
        RichRunText t => t.Text,
        RichRunBold b => PlainText(b.Inner),
        RichRunItalic i => PlainText(i.Inner),
        RichRunUnderline u => PlainText(u.Inner),
        RichRunStrike s => PlainText(s.Inner),
        RichRunCode c => PlainText(c.Inner),
        RichRunSpoiler sp => PlainText(sp.Inner),
        RichRunLink l => PlainText(l.Inner),
        RichRunAnchorLink al => PlainText(al.Inner),
        RichRunDateTime dt => dt.FallbackText,
        RichRunMath m => m.Latex,
        RichRunSequence seq => string.Concat(seq.Runs.Select(PlainText)),
        _ => "",
    };
}
