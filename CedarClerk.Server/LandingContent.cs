using System.Text.Json;
using System.Text.Json.Serialization;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>A screenshot on the page: a file the admin uploaded, plus what it is a picture of.</summary>
public record LandingShot(string File, LandingText Caption);

/// <summary>One column of the roadmap board. <paramref name="Mark"/> is done / doing / next.</summary>
public record LandingRoadmapColumn(LandingText Title, string Mark, List<LandingText> Items);

public record LandingStoryStep(LandingText When, LandingText Title, LandingText Text);

/// <summary>
/// The pre-block landing row with its code defaults folded in (ADR-215). Nothing draws from this
/// any more: <see cref="LandingDocument.FromLegacy"/> reads it to turn a row nobody has saved since
/// ADR-323 into the document that draws the same page.
/// </summary>
public sealed class LandingContent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public LandingText Kicker { get; private init; } = LandingText.Empty;
    public LandingText HeroTitle { get; private init; } = LandingText.Empty;
    public LandingText HeroSub { get; private init; } = LandingText.Empty;
    public LandingText Proof { get; private init; } = LandingText.Empty;
    public LandingText Note { get; private init; } = LandingText.Empty;
    public string? ShowcaseBlog { get; private init; }

    public bool ShowShots { get; private init; }
    public bool ShowFeatures { get; private init; }
    public bool ShowPricing { get; private init; }
    public bool ShowRoadmap { get; private init; }
    public bool ShowStory { get; private init; }
    public bool ShowDownload { get; private init; }

    public IReadOnlyList<LandingShot> Shots { get; private init; } = [];
    public IReadOnlyList<LandingRoadmapColumn> Roadmap { get; private init; } = [];
    public IReadOnlyList<LandingStoryStep> Story { get; private init; } = [];

    /// <summary>
    /// The screenshot the page falls back to when nothing has been uploaded. It ships in wwwroot,
    /// so a fresh install is never a landing page with an empty frame in the middle of it.
    /// </summary>
    public const string FallbackShot = "/assets/review/editor.png";

    /// <summary>
    /// The one place a stored file name becomes a URL. Uploads live in the data directory rather
    /// than in wwwroot, because a deploy replaces wwwroot and would take them with it.
    /// </summary>
    public const string ShotUrlPrefix = "/landing-media/";

    public static string ShotUrl(string file) =>
        file.StartsWith('/') ? file : ShotUrlPrefix + Uri.EscapeDataString(file);

    /// <summary>The hero image: the first uploaded screenshot, or the one that ships.</summary>
    public LandingShot Hero =>
        Shots.Count > 0 ? Shots[0] : new LandingShot(FallbackShot, LandingText.Empty);

    /// <summary>The rest of the gallery — the hero is already shown beside the form.</summary>
    public IReadOnlyList<LandingShot> Gallery => Shots.Count > 1 ? Shots.Skip(1).ToList() : [];

    public static async Task<LandingContent> LoadAsync(CedarDbContext db, IConfiguration cfg)
    {
        var row = await db.LandingSettings.AsNoTracking().FirstOrDefaultAsync();
        return From(row, cfg[Consts.General.ShowcaseBlogCfg]);
    }

    public static LandingContent From(LandingSettings? row, string? configuredShowcase) => new()
    {
        Editorial = ResolveEditorial(row?.EditorialJson),
        Kicker = Fill(row?.KickerEn, row?.KickerRu, LandingTexts.Kicker),
        HeroTitle = Fill(row?.HeroTitleEn, row?.HeroTitleRu, LandingTexts.HeroTitle),
        HeroSub = Fill(row?.HeroSubEn, row?.HeroSubRu, LandingTexts.HeroSub),
        // No default: this slot holds a number about other people, and an invented one makes
        // everything above it read as invented too. Empty until somebody has a true one.
        Proof = new LandingText(Blank(row?.ProofEn), Blank(row?.ProofRu)),
        Note = new LandingText(Blank(row?.NoteEn), Blank(row?.NoteRu)),
        ShowcaseBlog = Blank(row?.ShowcaseBlog) ?? Blank(configuredShowcase),
        ShowShots = row?.ShowShots ?? true,
        ShowFeatures = row?.ShowFeatures ?? true,
        ShowPricing = row?.ShowPricing ?? true,
        // Off until filled in, unlike the three above: a roadmap and a founding story are promises,
        // and the ones the mock arrived with were placeholder prose in somebody else's voice.
        ShowRoadmap = row?.ShowRoadmap ?? false,
        ShowStory = row?.ShowStory ?? false,
        // Off like the two above: a download section is a promise the build has to keep first.
        ShowDownload = row?.ShowDownload ?? false,
        Shots = Parse<LandingShot>(row?.ShotsJson),
        Roadmap = Parse<LandingRoadmapColumn>(row?.RoadmapJson),
        Story = Parse<LandingStoryStep>(row?.StoryJson),
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LandingText Fill(string? en, string? ru, LandingText fallback) =>
        new(Blank(en) ?? fallback.En, Blank(ru) ?? fallback.Ru);

    /// <summary>
    /// A malformed column is drawn as no column, never as a 500. This runs on the path a stranger
    /// hits first, and the stored JSON is only ever written by the admin tab — a parse failure here
    /// means a bad hand-edit, and the right answer to one is the rest of the page.
    /// </summary>
    private static IReadOnlyList<T> Parse<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public IReadOnlyDictionary<string, LandingText> Editorial { get; private init; } = new Dictionary<string, LandingText>();

    public string Copy(string key, bool ru) => Editorial[key].Pick(ru);

    public static Dictionary<string, LandingText> ReadEditorial(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, LandingText>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static Dictionary<string, LandingText> NormalizeEditorial(Dictionary<string, LandingText> overrides) =>
        LandingTexts.EditorialFields.ToDictionary(f => f.Key, f =>
            overrides.TryGetValue(f.Key, out var value) && value is not null
                ? new LandingText(Blank(value.En), Blank(value.Ru)) : LandingText.Empty);

    private static Dictionary<string, LandingText> ResolveEditorial(string? json)
    {
        var overrides = ReadEditorial(json);
        return LandingTexts.EditorialFields.ToDictionary(f => f.Key, f =>
        {
            overrides.TryGetValue(f.Key, out var value);
            return Fill(value?.En, value?.Ru, f.Default);
        });
    }

    public static string SerializeEditorial(Dictionary<string, LandingText> items) =>
        JsonSerializer.Serialize(NormalizeEditorial(items), Json);

    public static string Serialize<T>(IEnumerable<T> items) => JsonSerializer.Serialize(items, Json);

}
