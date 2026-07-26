using CedarClerk.Core;

namespace CedarClerk.Tests;

public class RegistrationFormDefinitionTests
{
    [Fact]
    public void Null_or_blank_json_means_no_form()
    {
        Assert.Null(RegistrationFormDefinition.Parse(null));
        Assert.Null(RegistrationFormDefinition.Parse("   "));
    }

    [Fact]
    public void Malformed_json_falls_back_to_the_default_form()
    {
        // A corrupt blob must never take a published post down — it degrades to name+email.
        Assert.Equal(RegistrationFormDefinition.Default, RegistrationFormDefinition.Parse("{not json"));
        Assert.Equal(RegistrationFormDefinition.Default, RegistrationFormDefinition.Parse("[1,2,3]"));
    }

    [Fact]
    public void Parses_field_flags_and_intro()
    {
        var form = RegistrationFormDefinition.Parse(
            """{"intro":"Hi","requireName":true,"requireNickname":true,"requireEmail":false,"requireSocial":true}""")!;

        Assert.Equal("Hi", form.Intro);
        Assert.True(form.RequireName);
        Assert.True(form.RequireNickname);
        Assert.False(form.RequireEmail);
        Assert.True(form.RequireSocial);
        Assert.Empty(form.Questions);
    }

    [Fact]
    public void Parses_text_and_choice_questions()
    {
        var form = RegistrationFormDefinition.Parse("""
            {"questions":[
                {"id":"exp","label":"Your experience","type":"text","required":true},
                {"id":"genre","label":"Favourite genre","type":"choice","options":["RPG","Sim"]}
            ]}
            """)!;

        Assert.Equal(2, form.Questions.Count);
        // Compared field-by-field rather than as a whole record: the record's generated equality
        // compares the Options list by reference, so [] never equals an empty List<string>.
        Assert.Equal("exp", form.Questions[0].Id);
        Assert.Equal("Your experience", form.Questions[0].Label);
        Assert.Equal(RegistrationQuestionType.Text, form.Questions[0].Type);
        Assert.True(form.Questions[0].Required);
        Assert.Empty(form.Questions[0].Options);
        Assert.Equal(RegistrationQuestionType.Choice, form.Questions[1].Type);
        Assert.Equal(["RPG", "Sim"], form.Questions[1].Options);
        Assert.False(form.Questions[1].Required);
    }

    [Fact]
    public void Choice_without_options_degrades_to_text()
    {
        var form = RegistrationFormDefinition.Parse("""{"questions":[{"id":"q","label":"L","type":"choice"}]}""")!;
        Assert.Equal(RegistrationQuestionType.Text, form.Questions[0].Type);
    }

    [Fact]
    public void Unlabelled_questions_are_dropped_and_missing_ids_generated()
    {
        var form = RegistrationFormDefinition.Parse("""
            {"questions":[{"label":""},{"label":"Real one"},{"id":null,"label":"Another"}]}
            """)!;

        Assert.Equal(2, form.Questions.Count);
        Assert.Equal("q1", form.Questions[0].Id);
        Assert.Equal("q2", form.Questions[1].Id);
    }
}

public class RegistrationFormHtmlTests
{
    [Fact]
    public void Renders_only_the_enabled_fields()
    {
        var form = new RegistrationFormDefinition(null, RequireName: true, RequireNickname: false,
            RequireEmail: true, RequireSocial: false, []);

        var html = CedarToBlogHtmlRenderer.RegistrationFormHtml(form, "My post");

        Assert.Contains("data-field=\"name\"", html);
        Assert.Contains("data-field=\"email\"", html);
        Assert.DoesNotContain("data-field=\"nickname\"", html);
        Assert.DoesNotContain("data-field=\"social\"", html);
    }

    [Fact]
    public void Renders_choice_question_as_select_with_options()
    {
        var form = new RegistrationFormDefinition(null, false, false, false, false,
            [new RegistrationQuestion("genre", "Genre", RegistrationQuestionType.Choice, ["RPG", "Sim"], false)]);

        var html = CedarToBlogHtmlRenderer.RegistrationFormHtml(form, "T");

        Assert.Contains("<select class=\"reg-input\" data-question=\"genre\"", html);
        Assert.Contains("<option value=\"RPG\">RPG</option>", html);
    }

    [Fact]
    public void Escapes_author_authored_text()
    {
        // Intro, labels and options are owner input rendered into a public page — the one real
        // injection surface this form introduces.
        var form = new RegistrationFormDefinition("<script>x</script>", false, false, false, false,
            [new RegistrationQuestion("q", "<b>Label</b>", RegistrationQuestionType.Choice, ["\"opt\""], false)]);

        var html = CedarToBlogHtmlRenderer.RegistrationFormHtml(form, "<h1>Title</h1>");

        Assert.DoesNotContain("<script>x</script>", html);
        Assert.DoesNotContain("<b>Label</b>", html);
        Assert.DoesNotContain("<h1>Title</h1>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&quot;opt&quot;", html);
    }

    [Fact]
    public void Marks_required_questions()
    {
        var form = new RegistrationFormDefinition(null, false, false, false, false,
            [new RegistrationQuestion("q", "Needed", RegistrationQuestionType.Text, [], true)]);

        var html = CedarToBlogHtmlRenderer.RegistrationFormHtml(form, "T");

        Assert.Contains("Needed *", html);
        Assert.Contains("maxlength=\"200\" required", html);
    }
}
