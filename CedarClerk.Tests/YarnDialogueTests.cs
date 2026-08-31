using CedarClerk.Core;

namespace CedarClerk.Tests;

public class YarnDialogueTests
{
    [Theory]
    [InlineData("Marty: Hello there!", true)]
    [InlineData("Just narration.", true)]
    [InlineData("-> Sure thing", true)]
    [InlineData("-> Sure thing <<if $brave>>", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("// a comment", false)]
    [InlineData("<<jump Barn>>", false)]
    [InlineData("<<set $met = true>>", false)]
    [InlineData("-> <<if $brave>>", false)]
    public void IsLocalizable_classifies_lines(string line, bool expected)
        => Assert.Equal(expected, YarnDialogue.IsLocalizable(line));

    [Fact]
    public void EnsureLineIds_tags_localizable_lines_only()
    {
        var body = "Marty: Hello!\n<<set $met = true>>\n-> Hi\n// note\n\nNarration.";
        var used = new HashSet<string>();

        var tagged = YarnDialogue.EnsureLineIds(body, used);

        var lines = tagged.Split('\n');
        Assert.Matches(@"#line:\w+$", lines[0]);
        Assert.Equal("<<set $met = true>>", lines[1]);
        Assert.Matches(@"#line:\w+$", lines[2]);
        Assert.Equal("// note", lines[3]);
        Assert.Equal("", lines[4]);
        Assert.Matches(@"#line:\w+$", lines[5]);
        Assert.Equal(3, used.Count);
    }

    [Fact]
    public void EnsureLineIds_keeps_existing_ids()
    {
        var body = "Marty: Hello! #line:abc12345";
        var used = new HashSet<string>();

        var tagged = YarnDialogue.EnsureLineIds(body, used);

        Assert.Equal(body, tagged);
        Assert.Equal(["abc12345"], used);
    }

    [Fact]
    public void EnsureLineIds_never_reuses_an_id_across_nodes()
    {
        var used = new HashSet<string>();
        var first = YarnDialogue.EnsureLineIds("One.", used);
        var second = YarnDialogue.EnsureLineIds("Two.", used);

        Assert.Equal(2, used.Count);
        Assert.NotEqual(TagOf(first), TagOf(second));

        static string TagOf(string body) => body.Split("#line:")[1];
    }

    [Fact]
    public void ExtractLines_reads_character_option_and_plain_text()
    {
        var node = new YarnDialogueNode("n1", "Start", 0, 0, string.Join('\n',
            "Marty: Hello there! #line:aaa11111",
            "Plain narration. #line:bbb22222",
            "-> Sure thing <<if $brave>> #line:ccc33333",
            "<<jump Barn>>",
            "Untagged line is skipped."));

        var lines = YarnDialogue.ExtractLines([node]);

        Assert.Equal(3, lines.Count);
        Assert.Equal(new YarnLine("aaa11111", "Start", "Marty", "Hello there!"), lines[0]);
        Assert.Equal(new YarnLine("bbb22222", "Start", "", "Plain narration."), lines[1]);
        Assert.Equal(new YarnLine("ccc33333", "Start", "", "Sure thing"), lines[2]);
    }

    [Fact]
    public void ExtractLines_keeps_markup_and_interpolation_in_text()
    {
        var node = new YarnDialogueNode("n1", "Start", 0, 0,
            "Marty: [wave]Hi {$name}![/wave] #line:aaa11111");

        var lines = YarnDialogue.ExtractLines([node]);

        Assert.Equal("[wave]Hi {$name}![/wave]", lines[0].Text);
    }

    [Fact]
    public void BuildYarnFile_emits_headers_body_and_terminators()
    {
        var yarn = YarnDialogue.BuildYarnFile([
            new YarnDialogueNode("n1", "Start", 80.7, 40.2, "Marty: Hello!\n<<jump Second Act>>"),
            new YarnDialogueNode("n2", "Second Act", 300, 40, "Done."),
        ]);

        Assert.Equal(string.Join('\n',
            "title: Start",
            "position: 80,40",
            "---",
            "Marty: Hello!",
            "<<jump Second Act>>",
            "===",
            "title: Second_Act",
            "position: 300,40",
            "---",
            "Done.",
            "===",
            ""), yarn);
    }

    [Theory]
    [InlineData("Second Act", "Second_Act")]
    [InlineData("3rd try!", "_3rd_try_")]
    [InlineData("  ", "Node")]
    [InlineData("Fine_Name9", "Fine_Name9")]
    public void SafeTitle_makes_yarn_identifiers(string title, string expected)
        => Assert.Equal(expected, YarnDialogue.SafeTitle(title));

    [Fact]
    public void Hash_inside_a_sentence_is_text_not_a_tag()
    {
        var node = new YarnDialogueNode("n1", "Start", 0, 0, "Marty: Issue #3 is fixed. #line:aaa11111");

        var lines = YarnDialogue.ExtractLines([node]);

        Assert.Equal("Issue #3 is fixed.", lines[0].Text);
    }
}
