using CedarClerk.Core;

namespace CedarClerk.Tests;

// ADR-239 clause 10. The phone the editor draws is only trustworthy if this projection says
// exactly what the wire would carry: the same parts, the same counts, the same drops.
public class TelegramPreviewProjectionTests
{
    private static readonly PublishCapabilities Telegram = new()
    {
        Network = PublishNetworks.Telegram,
        MaxCharacters = Consts.Telegram.MaxPostChars,
        ThreadPartCharacters = Consts.Telegram.ThreadPartChars,
        MaxMediaItems = 10,
    };

    private static string Doc(params string[] nodes) =>
        $$"""{"type":"doc","content":[{{string.Join(",", nodes)}}]}""";

    private static string Paragraph(string text) =>
        $$"""{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}""";

    private static string Heading(string text) =>
        $$"""{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"{{text}}"}]}""";

    private static string Image(string src, string? caption = null) =>
        caption is null
            ? $$$"""{"type":"image","attrs":{"src":"{{{src}}}"}}"""
            : $$$"""{"type":"image","attrs":{"src":"{{{src}}}","caption":"{{{caption}}}"}}""";

    private static string Carousel(params string[] images) =>
        $$$"""{"type":"carousel","attrs":{"images":[{{{string.Join(",", images.Select(i => $"\"{i}\""))}}}]}}""";

    private static string ListItem(string text) =>
        $$"""{"type":"listItem","content":[{{Paragraph(text)}}]}""";

    private static string BulletList(params string[] items) =>
        $$"""{"type":"bulletList","content":[{{string.Join(",", items.Select(ListItem))}}]}""";

    private static TelegramPreview Project(string cedarJson, IReadOnlyList<(string Text, string Url)>? buttons = null,
        PublishCapabilities? capabilities = null)
    {
        var caps = capabilities ?? Telegram;
        var parts = TelegramThreadSplitter.Split(CedarToTelegramBlocksRenderer.Render(cedarJson), caps);
        return TelegramPreviewProjection.Project("ru", parts, buttons ?? [], caps);
    }

    [Fact]
    public void A_text_only_document_is_one_message_of_paragraphs()
    {
        var preview = Project(Doc(Paragraph("Привет"), Paragraph("Мир")));

        Assert.Equal(1, preview.MessageCount);
        var message = Assert.Single(preview.Messages);
        Assert.Equal(0, message.Index);
        Assert.Equal(ThreadCutReason.End, message.CutReason);
        Assert.Equal("Привет", message.StartsWith);
        Assert.Collection(message.Blocks,
            b => { Assert.Equal("paragraph", b.Kind); Assert.Equal("Привет", b.Text); Assert.Empty(b.Urls); Assert.Null(b.Caption); },
            b => { Assert.Equal("paragraph", b.Kind); Assert.Equal("Мир", b.Text); });
        Assert.Equal(9, message.Characters);
        Assert.Equal(9, preview.Characters);
        Assert.Equal(Consts.Telegram.ThreadPartChars, preview.MaxCharactersPerMessage);
        Assert.Equal(10, preview.MaxMediaPerMessage);
        Assert.Empty(preview.Buttons);
    }

    [Fact]
    public void A_photo_carries_its_relative_url_and_caption()
    {
        var preview = Project(Doc(Image("/media/a.jpg", "Кадр с койотом")));

        var block = Assert.Single(Assert.Single(preview.Messages).Blocks);
        Assert.Equal("photo", block.Kind);
        Assert.Equal("", block.Text);
        Assert.Equal(["/media/a.jpg"], block.Urls);
        Assert.Equal("Кадр с койотом", block.Caption);
        Assert.Equal(1, preview.Messages[0].MediaCount);
    }

    [Fact]
    public void A_document_over_the_part_budget_splits_on_size()
    {
        var text = new string('x', 2000);
        var preview = Project(Doc(Paragraph(text), Paragraph(text)));

        Assert.Equal(2, preview.MessageCount);
        Assert.Equal(ThreadCutReason.Size, preview.Messages[0].CutReason);
        Assert.Equal(ThreadCutReason.End, preview.Messages[1].CutReason);
        Assert.Equal([0, 1], preview.Messages.Select(m => m.Index));
        Assert.Equal(4000, preview.Characters);
    }

    [Fact]
    public void A_heading_cut_lands_where_the_splitter_puts_it()
    {
        var preview = Project(Doc(Heading("Первая"), Paragraph(new string('a', 2000)), Heading("Вторая"), Paragraph("b")));

        Assert.Equal(2, preview.MessageCount);
        Assert.Equal(ThreadCutReason.Heading, preview.Messages[0].CutReason);
        Assert.Equal("Вторая", preview.Messages[1].StartsWith);
        Assert.Equal("heading", preview.Messages[1].Blocks[0].Kind);
    }

    [Fact]
    public void A_media_cut_lands_where_the_splitter_puts_it()
    {
        var preview = Project(Doc(Enumerable.Range(0, 12).Select(i => Image($"/media/{i}.jpg")).ToArray()));

        Assert.Equal(2, preview.MessageCount);
        Assert.Equal(ThreadCutReason.Media, preview.Messages[0].CutReason);
        Assert.Equal(10, preview.Messages[0].MediaCount);
        Assert.Equal(2, preview.Messages[1].MediaCount);
    }

    [Fact]
    public void An_empty_carousel_is_dropped_and_a_full_one_lists_every_url()
    {
        var preview = Project(Doc(Carousel(), Paragraph("текст"), Carousel("/media/1.jpg", "/media/2.jpg")));

        var blocks = Assert.Single(preview.Messages).Blocks;
        Assert.Collection(blocks,
            b => Assert.Equal("paragraph", b.Kind),
            b => { Assert.Equal("slideshow", b.Kind); Assert.Equal(["/media/1.jpg", "/media/2.jpg"], b.Urls); Assert.Null(b.Caption); });
    }

    [Fact]
    public void Anchor_blocks_are_not_projected()
    {
        var preview = Project(Doc(Heading("Заголовок"), Paragraph("текст")));

        Assert.Equal(["heading", "paragraph"], preview.Messages[0].Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void List_items_are_joined_with_newlines()
    {
        var preview = Project(Doc(BulletList("один", "два", "три")));

        var block = Assert.Single(preview.Messages[0].Blocks);
        Assert.Equal("list", block.Kind);
        Assert.Equal("один\nдва\nтри", block.Text);
    }

    [Fact]
    public void Buttons_ride_at_the_top_level_and_appear_once()
    {
        var preview = Project(Doc(Paragraph(new string('x', 2000)), Paragraph(new string('y', 2000))),
            buttons: [("Play", "https://example.com/play"), ("Wishlist", "https://example.com/wish")]);

        Assert.Equal(2, preview.MessageCount);
        Assert.Collection(preview.Buttons,
            b => { Assert.Equal("Play", b.Text); Assert.Equal("https://example.com/play", b.Url); },
            b => { Assert.Equal("Wishlist", b.Text); Assert.Equal("https://example.com/wish", b.Url); });
    }

    [Fact]
    public void Characters_are_the_splitters_numbers()
    {
        var cedarJson = Doc(Heading("Глава"), Paragraph(new string('a', 1500)), Image("/media/a.jpg", "подпись"),
            Paragraph(new string('b', 2500)), BulletList("раз", "два"));
        var parts = TelegramThreadSplitter.Split(CedarToTelegramBlocksRenderer.Render(cedarJson), Telegram);

        var preview = TelegramPreviewProjection.Project("ru", parts, [], Telegram);

        Assert.Equal(parts.Count, preview.MessageCount);
        Assert.Equal(parts.Select(p => p.Characters), preview.Messages.Select(m => m.Characters));
        Assert.Equal(parts.Select(p => p.MediaCount), preview.Messages.Select(m => m.MediaCount));
        Assert.Equal(parts.Sum(p => p.Characters), preview.Characters);
    }

    [Fact]
    public void An_empty_document_is_zero_messages()
    {
        var preview = Project(Doc());

        Assert.Equal(0, preview.MessageCount);
        Assert.Empty(preview.Messages);
        Assert.Equal(0, preview.Characters);
    }
}
