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

    private static string OrderedList(params string[] items) =>
        $$"""{"type":"orderedList","content":[{{string.Join(",", items.Select(ListItem))}}]}""";

    private static string TaskItem(string text, bool done) =>
        $$"""{"type":"taskItem","attrs":{"checked":{{(done ? "true" : "false")}}},"content":[{{Paragraph(text)}}]}""";

    private static string TaskList(params string[] items) =>
        $$"""{"type":"taskList","content":[{{string.Join(",", items)}}]}""";

    // Most tests here are about how a thread is cut, so the helper threads unless told otherwise;
    // `thread: false` is the send path's default and what the Preview tab shows (ADR-313).
    private static TelegramPreview Project(string cedarJson, IReadOnlyList<(string Text, string Url)>? buttons = null,
        PublishCapabilities? capabilities = null, bool thread = true)
    {
        var caps = capabilities ?? Telegram;
        var blocks = CedarToTelegramBlocksRenderer.Render(cedarJson);
        var parts = thread ? TelegramThreadSplitter.Split(blocks, caps) : TelegramThreadSplitter.Whole(blocks.ToList());
        return TelegramPreviewProjection.Project("ru", parts, buttons ?? [], caps, thread);
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

    // ADR-313 / #3 — without a thread the send path sends one message, so the preview shows one.
    [Fact]
    public void Without_a_thread_a_long_document_is_still_one_message_counted_against_the_message_limit()
    {
        var text = new string('x', 2000);
        var preview = Project(Doc(Paragraph(text), Paragraph(text)), thread: false);

        var message = Assert.Single(preview.Messages);
        Assert.Equal(1, preview.MessageCount);
        Assert.Equal(4000, message.Characters);
        Assert.Equal(2, message.Blocks.Count);
        Assert.Equal(ThreadCutReason.End, message.CutReason);
        Assert.Equal(Consts.Telegram.MaxPostChars, preview.MaxCharactersPerMessage);
        Assert.Equal(Consts.Telegram.ThreadPartChars, preview.FoldAfterCharacters);
    }

    [Fact]
    public void Without_a_thread_more_media_than_one_part_takes_stays_in_the_one_message()
    {
        var preview = Project(Doc(Enumerable.Range(0, 12).Select(i => Image($"/media/{i}.jpg")).ToArray()), thread: false);

        var message = Assert.Single(preview.Messages);
        Assert.Equal(12, message.MediaCount);
    }

    [Fact]
    public void A_thread_is_still_counted_against_the_part_budget()
    {
        var preview = Project(Doc(Paragraph(new string('x', 2000)), Paragraph(new string('y', 2000))), thread: true);

        Assert.Equal(2, preview.MessageCount);
        Assert.Equal(Consts.Telegram.ThreadPartChars, preview.MaxCharactersPerMessage);
    }

    [Fact]
    public void Whole_of_nothing_is_no_messages()
    {
        Assert.Empty(TelegramThreadSplitter.Whole([]));
        Assert.Equal(0, Project(Doc(), thread: false).MessageCount);
    }

    // #7 — a list reaches the phone with its markers, not as unmarked lines.
    [Fact]
    public void A_bullet_list_carries_its_items_without_ordinals_or_checkboxes()
    {
        var block = Assert.Single(Project(Doc(BulletList("один", "два")), thread: false).Messages[0].Blocks);

        Assert.Equal("list", block.Kind);
        Assert.Collection(block.Items!,
            i => { Assert.Equal("один", i.Text); Assert.Null(i.Order); Assert.False(i.HasCheckbox); },
            i => { Assert.Equal("два", i.Text); Assert.Null(i.Order); Assert.False(i.HasCheckbox); });
    }

    [Fact]
    public void An_ordered_list_carries_its_ordinals()
    {
        var block = Assert.Single(Project(Doc(OrderedList("a", "b", "c")), thread: false).Messages[0].Blocks);

        Assert.Equal([1, 2, 3], block.Items!.Select(i => i.Order));
    }

    [Fact]
    public void A_task_list_carries_its_checkbox_state()
    {
        var block = Assert.Single(Project(Doc(TaskList(TaskItem("сделано", true), TaskItem("нет", false))), thread: false).Messages[0].Blocks);

        Assert.Collection(block.Items!,
            i => { Assert.True(i.HasCheckbox); Assert.True(i.IsChecked); Assert.Equal("сделано", i.Text); },
            i => { Assert.True(i.HasCheckbox); Assert.False(i.IsChecked); });
    }

    [Fact]
    public void Blocks_that_are_not_lists_carry_no_items()
    {
        var block = Assert.Single(Project(Doc(Paragraph("текст")), thread: false).Messages[0].Blocks);

        Assert.Null(block.Items);
    }

    // ADR-313 — the kitchen-sink post: one document with every block the preview has to draw. A block
    // type the projection drops, or a list that loses its marker, fails here instead of in front of a
    // reader. The marks (bold, italic, link) are flattened by design (ADR-239 clause 10) — the text
    // must survive that.
    [Fact]
    public void A_post_with_every_block_loses_none_of_them_and_keeps_its_text()
    {
        const string marked = """
            {"type":"paragraph","content":[
              {"type":"text","text":"жирный ","marks":[{"type":"bold"}]},
              {"type":"text","text":"курсив ","marks":[{"type":"italic"}]},
              {"type":"text","text":"ссылка","marks":[{"type":"link","attrs":{"href":"https://example.com"}}]}]}
            """;
        const string quote = """{"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"цитата"}]}]}""";
        const string code = """{"type":"codeBlock","content":[{"type":"text","text":"var x = 1;"}]}""";
        const string rule = """{"type":"horizontalRule"}""";
        const string video = """{"type":"video","attrs":{"src":"/media/v.mp4"}}""";
        const string audio = """{"type":"audio","attrs":{"src":"/media/a.mp3"}}""";

        var cedarJson = Doc(
            Heading("Глава"), marked, BulletList("раз", "два"), OrderedList("первый", "второй"),
            TaskList(TaskItem("готово", true), TaskItem("нет", false)),
            quote, code, rule,
            Image("/media/p.jpg", "подпись"), Carousel("/media/1.jpg", "/media/2.jpg", "/media/3.jpg", "/media/4.jpg", "/media/5.jpg", "/media/6.jpg"),
            video, audio);

        var rendered = CedarToTelegramBlocksRenderer.Render(cedarJson);
        var preview = Project(cedarJson, thread: false);

        var blocks = Assert.Single(preview.Messages).Blocks;
        // Nothing visible is dropped on the way from the renderer to the phone. A RichAnchorBlock is an
        // invisible jump target emitted before each heading (TOC links), so it has no phone counterpart.
        Assert.Equal(rendered.Count(b => b is not RichAnchorBlock), blocks.Count);
        Assert.Equal(
            ["heading", "paragraph", "list", "list", "list", "quote", "code", "divider", "photo", "slideshow", "video", "audio"],
            blocks.Select(b => b.Kind));
        Assert.Contains("жирный курсив ссылка", blocks[1].Text);
        Assert.Equal(6, blocks.Single(b => b.Kind == "slideshow").Urls.Count);

        // The three lists keep their three kinds of marker.
        var lists = blocks.Where(b => b.Kind == "list").ToList();
        Assert.All(lists, l => Assert.NotEmpty(l.Items!));
        Assert.Null(lists[0].Items![0].Order);
        Assert.False(lists[0].Items![0].HasCheckbox);
        Assert.Equal(1, lists[1].Items![0].Order);
        Assert.True(lists[2].Items![0].HasCheckbox);
        Assert.True(lists[2].Items![0].IsChecked);
    }
}
