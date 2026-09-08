using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-331 — a preset's config is free-form JSON on the way in and a normalised record on the way out.
// These are the cases that decide what survives that trip, which is the whole of the validation:
// the endpoint re-serialises from the record, so anything dropped here can never reach the database.
public class ProjectPresetConfigTests
{
    [Fact]
    public void An_unknown_project_type_falls_back_rather_than_being_stored()
    {
        var config = ProjectPresetConfig.Parse("""{"projectType":"spaceship"}""");

        Assert.Equal(ProjectPresetConfig.Default.ProjectType, config.ProjectType);
    }

    [Fact]
    public void A_known_project_type_survives_and_a_retired_one_falls_back()
    {
        Assert.Equal(ProjectTypes.Blog, ProjectPresetConfig.Parse("""{"projectType":"blog"}""").ProjectType);
        Assert.Equal(ProjectTypes.Vault, ProjectPresetConfig.Parse("""{"projectType":"vault"}""").ProjectType);
        // "jam" left the offer with ADR-293; a preset still naming it starts the default project instead.
        Assert.Equal(ProjectPresetConfig.Default.ProjectType, ProjectPresetConfig.Parse("""{"projectType":"jam"}""").ProjectType);
    }

    [Fact]
    public void An_unknown_document_type_becomes_null_and_the_type_decides_instead()
    {
        var config = ProjectPresetConfig.Parse("""{"projectType":"blog","documentType":"invoice"}""");

        Assert.Null(config.DocumentType);
        // Null is not "no document" — it is "whatever this project type already implies".
        Assert.Equal(ProjectTypes.StarterDocumentType(ProjectTypes.Blog), config.EffectiveDocumentType);
    }

    [Fact]
    public void A_named_document_type_wins_over_the_project_types_own()
    {
        var config = ProjectPresetConfig.Parse("""{"projectType":"blog","documentType":"design"}""");

        Assert.Equal(DocumentTypes.Design, config.EffectiveDocumentType);
    }

    [Fact]
    public void Blank_text_becomes_null_rather_than_an_empty_title()
    {
        var config = ProjectPresetConfig.Parse("""{"documentTitle":"   ","description":"  "}""");

        Assert.Null(config.DocumentTitle);
        Assert.Equal("", config.Description);
    }

    [Fact]
    public void Overlong_text_is_cut_to_its_limit()
    {
        var title = new string('x', ProjectPresetConfig.TitleMaxChars + 50);
        var description = new string('y', ProjectPresetConfig.DescriptionMaxChars + 50);

        var config = ProjectPresetConfig.Parse($$"""{"documentTitle":"{{title}}","description":"{{description}}"}""");

        Assert.Equal(ProjectPresetConfig.TitleMaxChars, config.DocumentTitle!.Length);
        Assert.Equal(ProjectPresetConfig.DescriptionMaxChars, config.Description.Length);
    }

    [Fact]
    public void Broken_or_missing_json_is_the_default_and_never_an_exception()
    {
        Assert.Equal(ProjectPresetConfig.Default, ProjectPresetConfig.Parse(null));
        Assert.Equal(ProjectPresetConfig.Default, ProjectPresetConfig.Parse(""));
        Assert.Equal(ProjectPresetConfig.Default, ProjectPresetConfig.Parse("{not json"));
    }

    [Fact]
    public void A_config_survives_a_round_trip_through_its_own_json()
    {
        var config = ProjectPresetConfig.Parse(
            """{"projectType":"product","documentType":"note","documentTitle":"Read me","description":"A thing"}""");

        Assert.Equal(config, ProjectPresetConfig.Parse(config.ToJson()));
    }
}

public class ExportPresetConfigTests
{
    [Fact]
    public void Unknown_destinations_are_dropped_and_known_ones_keep_their_order()
    {
        var config = ExportPresetConfig.Parse("""{"destinations":["telegram","myspace","blog"]}""");

        Assert.Equal(["telegram", ExportDestinations.Blog], config.Destinations);
    }

    [Fact]
    public void A_destination_repeated_is_stored_once()
    {
        var config = ExportPresetConfig.Parse("""{"destinations":["blog","blog","BLOG"]}""");

        Assert.Equal(["blog"], config.Destinations);
    }

    [Fact]
    public void The_blog_is_a_destination_and_so_is_every_publish_network()
    {
        Assert.True(ExportDestinations.IsKnown(ExportDestinations.Blog));
        foreach (var network in PublishNetworks.All) Assert.True(ExportDestinations.IsKnown(network));
        Assert.False(ExportDestinations.IsKnown("myspace"));
    }

    [Fact]
    public void Unknown_languages_are_dropped()
    {
        var config = ExportPresetConfig.Parse("""{"languages":["en","klingon","ru"]}""");

        Assert.Equal(["en", "ru"], config.Languages);
    }

    [Fact]
    public void An_empty_preset_is_a_valid_one()
    {
        var config = ExportPresetConfig.Parse("{}");

        // Empty means "leave the ticks alone", never "publish nowhere" — the client reads it that
        // way and the record has to be able to hold it.
        Assert.Empty(config.Destinations);
        Assert.Empty(config.Languages);
    }

    [Fact]
    public void Broken_or_missing_json_is_the_default_and_never_an_exception()
    {
        Assert.Equal(ExportPresetConfig.Default, ExportPresetConfig.Parse(null));
        Assert.Equal(ExportPresetConfig.Default, ExportPresetConfig.Parse("{not json"));
    }

    [Fact]
    public void A_config_survives_a_round_trip_through_its_own_json()
    {
        var config = ExportPresetConfig.Parse("""{"destinations":["blog","telegram"],"languages":["en"]}""");
        var again = ExportPresetConfig.Parse(config.ToJson());

        Assert.Equal(config.Destinations, again.Destinations);
        Assert.Equal(config.Languages, again.Languages);
    }
}

public class PresetKindsTests
{
    [Fact]
    public void The_three_kinds_are_known_and_nothing_else_is()
    {
        Assert.True(PresetKinds.IsKnown(PresetKinds.Document));
        Assert.True(PresetKinds.IsKnown(PresetKinds.Project));
        Assert.True(PresetKinds.IsKnown(PresetKinds.Export));
        Assert.False(PresetKinds.IsKnown("channel"));
        Assert.False(PresetKinds.IsKnown(null));
    }
}
