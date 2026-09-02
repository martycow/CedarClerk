using System.Text.Json;
using System.Text.Json.Serialization;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>One string in both of the landing's languages. Either half may be missing.</summary>
public record LandingText(string? En, string? Ru)
{
    public static readonly LandingText Empty = new(null, null);

    /// <summary>
    /// The asked-for language, falling back to the other one rather than to nothing: a maintainer
    /// who filled in only English meant the Russian reader to see something, not a hole.
    /// </summary>
    public string Pick(bool ru)
    {
        var first = ru ? Ru : En;
        var second = ru ? En : Ru;
        return (string.IsNullOrWhiteSpace(first) ? second : first)?.Trim() ?? "";
    }

    /// <summary>A question about the value, not a third half of it — and the stored JSON is a
    /// record of what was written, not of what was derived from it.</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(En) && string.IsNullOrWhiteSpace(Ru);
}

/// <summary>A screenshot on the page: a file the admin uploaded, plus what it is a picture of.</summary>
public record LandingShot(string File, LandingText Caption);

/// <summary>One column of the roadmap board. <paramref name="Mark"/> is done / doing / next.</summary>
public record LandingRoadmapColumn(LandingText Title, string Mark, List<LandingText> Items);

public record LandingStoryStep(LandingText When, LandingText Title, LandingText Text);

/// <summary>
/// Everything the landing draws, with the maintainer's edits already folded into the defaults
/// (ADR-215).
///
/// The split is deliberate. Numbers, plans and the feature list are <b>code</b> — they are claims
/// the product has to keep, and <see cref="PlanLimitations"/> moving without this page moving is
/// how a pricing page starts lying. The headline, the note in the margin, the screenshots, the
/// roadmap and the story are <b>data</b> — no test can check them, and they are the half that has
/// to sound like a person.
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
    public const string FallbackShot = "/landing-blog.png";

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
        Kicker = Fill(row?.KickerEn, row?.KickerRu, Defaults.Kicker),
        HeroTitle = Fill(row?.HeroTitleEn, row?.HeroTitleRu, Defaults.HeroTitle),
        HeroSub = Fill(row?.HeroSubEn, row?.HeroSubRu, Defaults.HeroSub),
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

    public static string Serialize<T>(IEnumerable<T> items) => JsonSerializer.Serialize(items, Json);

    /// <summary>
    /// The copy that is not the maintainer's to write, in one place so the render method reads as
    /// layout. English and Russian only: the landing is the front door, and the nine content
    /// languages are what a post can be written in, not what this page is translated into.
    /// </summary>
    public static class Defaults
    {
        public static readonly LandingText Kicker = new("publishing for independent makers", "публикация для независимых авторов");

        public static readonly LandingText HeroTitle = new(
            "Build in public.<br>Keep your own home.<br>Get discovered.",
            "Делайте открыто.<br>Храните у себя.<br>Находите читателей.");

        public static readonly LandingText HeroSub = new(
            "Write a personal blog or connect every devlog to a project. Publish to your own site, "
            + $"Telegram, X, Bluesky and Discord in {Languages.ContentLanguages.Count} languages — and join Discovery when you choose.",
            "Ведите личный блог или связывайте каждый девлог с проектом. Публикуйте на своём сайте, "
            + $"в Telegram, X, Bluesky и Discord на {Languages.ContentLanguages.Count} языках — и включайте Discovery, когда решите.");
    }
}
