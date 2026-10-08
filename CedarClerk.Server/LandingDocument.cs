using System.Text.Json;
using System.Text.RegularExpressions;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;
using TextMap = System.Collections.Generic.Dictionary<string, string>;

namespace CedarClerk.Server;

public sealed class LandingFeature
{
    public string Id { get; set; } = "";
    public string Icon { get; set; } = "";
    public TextMap Title { get; set; } = [];
    public TextMap Body { get; set; } = [];
    public string? Shot { get; set; }
}

public sealed class LandingItem
{
    public Dictionary<string, TextMap> Text { get; set; } = [];
    public string? Icon { get; set; }
    public string? File { get; set; }
    public string? Mark { get; set; }
    public List<TextMap> Entries { get; set; } = [];
}

public sealed class LandingBlock
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Style { get; set; }
    public bool Hidden { get; set; }
    public Dictionary<string, TextMap> Text { get; set; } = [];
    public List<LandingItem> Items { get; set; } = [];
    public Dictionary<string, bool> Options { get; set; } = [];
    public string? Url { get; set; }
    public string? Image { get; set; }
}

public sealed class LandingSection
{
    public string Id { get; set; } = "";
    public string Layout { get; set; } = LandingLayouts.Stack;
    public string? Anchor { get; set; }
    public bool Hidden { get; set; }
    public TextMap Nav { get; set; } = [];
    public TextMap Label { get; set; } = [];
    public List<LandingBlock> Blocks { get; set; } = [];
}

public static class LandingLayouts
{
    public const string Stack = "stack";
    public const string Hero = "hero";
    public const string Split = "split";
    public const string Flow = "flow";
    public const string Band = "band";

    public static readonly IReadOnlyList<string> All = [Stack, Hero, Split, Flow, Band];
}

public sealed record LandingStyleSpec(
    string Id, string[] Fields, string[] ItemFields,
    bool Icons = false, bool Files = false, bool Marks = false, bool Entries = false,
    bool Image = false, bool Url = false);

public sealed record LandingBlockSpec(
    string Type, LandingStyleSpec[] Styles, string[] Required, string[] RequiredItem, string[] Options,
    bool Single = false);

/// <summary>The eleven block types of ADR-323 §2, with the fields each style draws.</summary>
public static class LandingBlocks
{
    public const string Text = "text";
    public const string Subscribe = "subscribe";
    public const string Hint = "hint";
    public const string Screenshot = "screenshot";
    public const string Timeline = "timeline";
    public const string Social = "social";
    public const string Discovery = "discovery";
    public const string Tools = "tools";
    public const string Pricing = "pricing";
    public const string Faq = "faq";
    public const string Download = "download";

    public const string Default = "default";
    public const string ShowNotes = "showNotes";
    public const string ShowComparison = "showComparison";

    public static readonly IReadOnlyList<LandingBlockSpec> All =
    [
        new(Text,
        [
            new("hero", ["kicker", "title", "body"], []),
            new("heading", ["title", "body"], []),
            new("rule", ["title", "meta"], []),
            new("copy", ["title", "body", "linkLabel"], [], Url: true),
            new("plain", ["title", "body"], []),
        ], ["title"], [], []),
        new(Subscribe,
        [
            new("form", ["button", "hint", "proof", "note"], []),
            new("button", ["button"], []),
        ], ["button"], [], []),
        new(Hint,
        [
            new("steps", [], ["title", "text"]),
            new("cards", [], ["title", "text"], Icons: true),
        ], [], ["title"], []),
        new(Screenshot,
        [
            new("hero", [], ["caption"], Files: true),
            new("gallery", [], ["caption"], Files: true),
        ], [], [], []),
        new(Timeline,
        [
            new("milestones", [], ["when", "title", "text"], Image: true),
            new("board", [], ["title"], Marks: true, Entries: true),
        ], [], ["title"], []),
        new(Social, [new(Default, ["lead"], [])], ["lead"], [], []),
        new(Discovery, [new(Default, [], [])], [], [], []),
        new(Tools, [new(Default, ["title"], [])], ["title"], [], [], Single: true),
        new(Pricing, [new(Default, [], [])], [], [], [ShowNotes, ShowComparison]),
        new(Faq, [new(Default, ["title"], ["question", "answer"])], ["title"], ["question", "answer"], []),
        new(Download, [new(Default, ["title", "body", "meta", "button"], [])], ["title", "button"], [], []),
    ];

    public static LandingBlockSpec? Find(string? type) => All.FirstOrDefault(s => s.Type == type);

    public static LandingStyleSpec StyleOf(LandingBlockSpec spec, string? style) =>
        spec.Styles.FirstOrDefault(s => s.Id == style) ?? spec.Styles[0];
}

/// <summary>
/// The landing as ADR-323 stores it: an ordered list of sections, each a layout over an ordered
/// list of typed blocks, plus the feature list the Tools block reads.
/// </summary>
public sealed partial class LandingDocument
{
    public const int MaxBytes = 256 * 1024;
    public const int MaxSections = 30;
    public const int MaxBlocks = 12;
    public const int MaxItems = 40;
    public const int MaxEntries = 60;
    public const int MaxFeatures = 40;
    public const int MaxTextLength = 2000;

    public static readonly IReadOnlyList<string> RequiredLanguages = [Localization.Languages.English, Localization.Languages.Russian];
    public static readonly IReadOnlyList<string> Marks = ["done", "doing", "next"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Element ids the page's own chrome and scripts already use; a section anchor equal to one
    // would be a second element with that id.
    private static readonly HashSet<string> ReservedAnchors =
    [
        "waitlist", "waitlist-form", "waitlist-note", "waitlist-dialog", "waitlist-dialog-title",
        "waitlist-dialog-content", "waitlist-modal-form", "waitlist-modal-note", "shots", "discover",
        "tools-title", "consent", "consent-yes", "consent-no", "readingbtn", "readingmenu",
    ];

    public List<string> Languages { get; set; } = [.. RequiredLanguages];
    public string? ShowcaseBlog { get; set; }
    public List<LandingFeature> Features { get; set; } = [];
    public List<LandingSection> Sections { get; set; } = [];

    public static async Task<LandingDocument> LoadAsync(CedarDbContext db, IConfiguration cfg)
    {
        var row = await db.LandingSettings.AsNoTracking().FirstOrDefaultAsync();
        return FromRow(row, cfg[Consts.General.ShowcaseBlogCfg]);
    }

    /// <summary>
    /// The stored document, or the legacy columns read as one. A row nobody has saved since the
    /// block model arrived holds no document, and the page it draws must be the page it drew.
    /// </summary>
    public static LandingDocument FromRow(LandingSettings? row, string? configuredShowcase) =>
        Parse(row?.DocumentJson) ?? FromLegacy(row, configuredShowcase);

    public static bool IsStored(LandingSettings? row) => Parse(row?.DocumentJson) is not null;

    public static LandingDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var doc = JsonSerializer.Deserialize<LandingDocument>(json, Json);
            return doc?.Normalize();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// The asked-for language, then English, then Russian, then whatever is written: a language the
    /// admin added but has not finished shows the page's floor language rather than a hole.
    /// </summary>
    public static string Pick(TextMap? map, string language)
    {
        if (map is null) return "";
        foreach (var code in new[] { language, Localization.Languages.English, Localization.Languages.Russian })
            if (map.TryGetValue(code, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return map.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
    }

    public static bool IsBlank(TextMap? map) => map is null || map.Values.All(string.IsNullOrWhiteSpace);

    /// <summary>
    /// Nulls a client may send become empty collections, text is trimmed and blank halves are
    /// dropped, so everything after this reads the document without asking what is missing.
    /// </summary>
    public LandingDocument Normalize()
    {
        Languages = (Languages ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();
        foreach (var required in RequiredLanguages)
            if (!Languages.Contains(required)) Languages.Add(required);
        ShowcaseBlog = Blank(ShowcaseBlog);
        Features ??= [];
        Sections ??= [];
        foreach (var feature in Features)
        {
            feature.Id = (feature.Id ?? "").Trim();
            feature.Icon = (feature.Icon ?? "").Trim();
            feature.Title = Clean(feature.Title);
            feature.Body = Clean(feature.Body);
            feature.Shot = Blank(feature.Shot);
        }
        foreach (var section in Sections)
        {
            section.Id = (section.Id ?? "").Trim();
            section.Layout = (section.Layout ?? "").Trim();
            section.Anchor = Blank(section.Anchor);
            section.Nav = Clean(section.Nav);
            section.Label = Clean(section.Label);
            section.Blocks ??= [];
            foreach (var block in section.Blocks)
            {
                block.Id = (block.Id ?? "").Trim();
                block.Type = (block.Type ?? "").Trim();
                block.Style = Blank(block.Style) ?? LandingBlocks.Find(block.Type)?.Styles[0].Id;
                block.Text = Clean(block.Text);
                block.Options ??= [];
                block.Url = Blank(block.Url);
                block.Image = Blank(block.Image);
                block.Items ??= [];
                foreach (var item in block.Items)
                {
                    item.Text = Clean(item.Text);
                    item.Icon = Blank(item.Icon);
                    item.File = Blank(item.File);
                    item.Mark = Blank(item.Mark);
                    item.Entries = (item.Entries ?? []).Select(Clean).Where(e => e.Count > 0).ToList();
                }
            }
        }
        return this;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TextMap Clean(TextMap? map) =>
        (map ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToDictionary(p => p.Key, p => p.Value.Trim());

    private static Dictionary<string, TextMap> Clean(Dictionary<string, TextMap>? fields) =>
        (fields ?? []).Select(p => (p.Key, Value: Clean(p.Value))).Where(p => p.Value.Count > 0)
            .ToDictionary(p => p.Key, p => p.Value);

    // ---------- validation ----------

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$")]
    private static partial Regex AnchorPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,119}$")]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?(:\d{1,5})?(/[A-Za-z0-9._~/-]*)?$")]
    private static partial Regex HostPattern();

    /// <summary>
    /// An image a block may point at: a file uploaded to the landing directory, or a path on this
    /// site. Never another origin, a protocol-relative address or a scheme.
    /// </summary>
    public static bool IsSafeImage(string? value) =>
        value is not null && (FileNamePattern().IsMatch(value) || IsSitePath(value));

    /// <summary>A link a block may carry: https, a path on this site, or an anchor on the page.</summary>
    public static bool IsSafeLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        if (value.StartsWith('#')) return AnchorPattern().IsMatch(value[1..]);
        if (value.StartsWith('/')) return IsSitePath(value);
        return value.StartsWith("https://", StringComparison.Ordinal)
               && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
               && uri.Host.Length > 0 && string.IsNullOrEmpty(uri.UserInfo);
    }

    public static bool IsSafeHost(string? value) => value is not null && HostPattern().IsMatch(value);

    private static bool IsSitePath(string value) =>
        value.Length is > 1 and <= 300 && value[0] == '/' && value[1] != '/'
        && !value.Contains('\\') && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));

    /// <summary>The first thing wrong with the document, in the caller's language, or null.</summary>
    public string? Validate()
    {
        if (Serialize().Length > MaxBytes) return ErrorMessages.LandingDocumentTooLarge(MaxBytes / 1024);

        if (Languages.Count != Languages.Distinct().Count()) return ErrorMessages.LandingIdInvalid("languages");
        foreach (var language in Languages)
            if (!Localization.Languages.IsUiLanguage(language))
                return ErrorMessages.LandingValueUnknown("languages", language);

        if (ShowcaseBlog is not null && !IsSafeHost(ShowcaseBlog)) return ErrorMessages.LandingUrlInvalid("showcaseBlog");

        if (Features.Count > MaxFeatures) return ErrorMessages.LandingTooMany("features", MaxFeatures);
        var featureIds = new HashSet<string>();
        for (var i = 0; i < Features.Count; i++)
        {
            var feature = Features[i];
            var at = $"features[{i + 1}]";
            if (!IdPattern().IsMatch(feature.Id) || !featureIds.Add(feature.Id)) return ErrorMessages.LandingIdInvalid(at);
            if (Icons.Markup(feature.Icon).Length == 0) return ErrorMessages.LandingValueUnknown($"{at}.icon", feature.Icon);
            if (feature.Shot is not null && !IsSafeImage(feature.Shot)) return ErrorMessages.LandingUrlInvalid($"{at}.shot");
            if (CheckText(feature.Title, $"{at}.title", required: true) is { } titleError) return titleError;
            if (CheckText(feature.Body, $"{at}.body", required: true) is { } bodyError) return bodyError;
        }

        if (Sections.Count > MaxSections) return ErrorMessages.LandingTooMany("sections", MaxSections);
        var sectionIds = new HashSet<string>();
        var blockIds = new HashSet<string>();
        var anchors = new HashSet<string>();
        var singles = new HashSet<string>();
        var forms = 0;
        for (var i = 0; i < Sections.Count; i++)
        {
            var section = Sections[i];
            var at = $"sections[{i + 1}]";
            if (!IdPattern().IsMatch(section.Id) || !sectionIds.Add(section.Id)) return ErrorMessages.LandingIdInvalid(at);
            if (!LandingLayouts.All.Contains(section.Layout)) return ErrorMessages.LandingValueUnknown($"{at}.layout", section.Layout);
            if (section.Anchor is not null
                && (!AnchorPattern().IsMatch(section.Anchor) || ReservedAnchors.Contains(section.Anchor)
                    || section.Anchor.StartsWith("tool-", StringComparison.Ordinal) || !anchors.Add(section.Anchor)))
                return ErrorMessages.LandingIdInvalid($"{at}.anchor");
            if (CheckText(section.Nav, $"{at}.nav", required: false) is { } navError) return navError;
            if (CheckText(section.Label, $"{at}.label", required: false) is { } labelError) return labelError;
            if (section.Blocks.Count > MaxBlocks) return ErrorMessages.LandingTooMany($"{at}.blocks", MaxBlocks);

            for (var j = 0; j < section.Blocks.Count; j++)
            {
                var block = section.Blocks[j];
                var where = $"{at}.blocks[{j + 1}]";
                if (!IdPattern().IsMatch(block.Id) || !blockIds.Add(block.Id)) return ErrorMessages.LandingIdInvalid(where);
                if (LandingBlocks.Find(block.Type) is not { } spec) return ErrorMessages.LandingValueUnknown($"{where}.type", block.Type);
                if (spec.Styles.All(s => s.Id != block.Style)) return ErrorMessages.LandingValueUnknown($"{where}.style", block.Style ?? "");
                var style = LandingBlocks.StyleOf(spec, block.Style);
                if (spec.Single && !singles.Add(spec.Type)) return ErrorMessages.LandingTooMany($"{where}.type", 1);
                if (block is { Type: LandingBlocks.Subscribe, Style: "form" } && ++forms > 1)
                    return ErrorMessages.LandingTooMany($"{where}.style", 1);

                foreach (var key in block.Text.Keys)
                    if (!style.Fields.Contains(key)) return ErrorMessages.LandingValueUnknown($"{where}.text", key);
                foreach (var field in style.Fields)
                    if (CheckText(block.Text.GetValueOrDefault(field), $"{where}.text.{field}", spec.Required.Contains(field)) is { } error)
                        return error;
                foreach (var key in block.Options.Keys)
                    if (!spec.Options.Contains(key)) return ErrorMessages.LandingValueUnknown($"{where}.options", key);

                if (block.Url is not null && (!style.Url || !IsSafeLink(block.Url))) return ErrorMessages.LandingUrlInvalid($"{where}.url");
                if (block.Image is not null && (!style.Image || !IsSafeImage(block.Image))) return ErrorMessages.LandingUrlInvalid($"{where}.image");

                if (block.Items.Count > MaxItems) return ErrorMessages.LandingTooMany($"{where}.items", MaxItems);
                if (block.Items.Count > 0 && style.ItemFields.Length == 0 && !style.Files)
                    return ErrorMessages.LandingValueUnknown(where, "items");
                for (var k = 0; k < block.Items.Count; k++)
                {
                    var item = block.Items[k];
                    var here = $"{where}.items[{k + 1}]";
                    foreach (var key in item.Text.Keys)
                        if (!style.ItemFields.Contains(key)) return ErrorMessages.LandingValueUnknown($"{here}.text", key);
                    foreach (var field in style.ItemFields)
                        if (CheckText(item.Text.GetValueOrDefault(field), $"{here}.text.{field}", spec.RequiredItem.Contains(field)) is { } error)
                            return error;
                    if (style.Icons ? Icons.Markup(item.Icon ?? "").Length == 0 : item.Icon is not null)
                        return ErrorMessages.LandingValueUnknown($"{here}.icon", item.Icon ?? "");
                    if (style.Files ? !IsSafeImage(item.File) : item.File is not null)
                        return ErrorMessages.LandingUrlInvalid($"{here}.file");
                    if (style.Marks ? !Marks.Contains(item.Mark) : item.Mark is not null)
                        return ErrorMessages.LandingValueUnknown($"{here}.mark", item.Mark ?? "");
                    if (item.Entries.Count > (style.Entries ? MaxEntries : 0))
                        return ErrorMessages.LandingTooMany($"{here}.entries", style.Entries ? MaxEntries : 0);
                    for (var n = 0; n < item.Entries.Count; n++)
                        if (CheckText(item.Entries[n], $"{here}.entries[{n + 1}]", required: true) is { } error)
                            return error;
                }
            }
        }
        return null;
    }

    private string? CheckText(TextMap? map, string at, bool required)
    {
        map ??= [];
        foreach (var (language, value) in map)
        {
            if (!Languages.Contains(language)) return ErrorMessages.LandingValueUnknown(at, language);
            if (value.Length > MaxTextLength) return ErrorMessages.LandingTextTooLong(at, MaxTextLength);
        }
        if (!required && map.Count == 0) return null;
        foreach (var language in RequiredLanguages)
            if (!map.ContainsKey(language)) return ErrorMessages.LandingTextRequired(at, language.ToUpperInvariant());
        return null;
    }

    // ---------- the legacy row, read as a document (ADR-323 §5) ----------

    private const int CountSentinel = 918273645;
    public const string LanguagesToken = "{languages}";
    public const string NetworksToken = "{networks}";

    /// <summary>
    /// The fixed columns, section flags and code defaults of the pre-block landing, as the document
    /// that draws the same page. Deterministic and never written back on its own: the row keeps
    /// reading this way until an admin saves, and the columns it reads stay as they were.
    /// </summary>
    public static LandingDocument FromLegacy(LandingSettings? row, string? configuredShowcase)
    {
        var c = LandingContent.From(row, configuredShowcase);
        TextMap Copy(string key) => Map(c.Editorial[key]);
        var id = 0;
        LandingBlock Block(string type, string? style = null, bool hidden = false) => new()
        {
            Id = $"b{++id}", Type = type, Hidden = hidden,
            Style = style ?? LandingBlocks.Find(type)!.Styles[0].Id,
        };
        LandingBlock With(LandingBlock block, params (string Key, TextMap Value)[] text)
        {
            foreach (var (key, value) in text)
                if (value.Count > 0) block.Text[key] = value;
            return block;
        }
        LandingItem Item(params (string Key, TextMap Value)[] text)
        {
            var item = new LandingItem();
            foreach (var (key, value) in text)
                if (value.Count > 0) item.Text[key] = value;
            return item;
        }
        LandingItem Shot(LandingShot shot)
        {
            var item = Item(("caption", Map(shot.Caption)));
            item.File = shot.File;
            return item;
        }

        var hero = With(Block(LandingBlocks.Text, "hero"),
            ("kicker", Map(c.Kicker)), ("title", Map(c.HeroTitle)), ("body", Map(c.HeroSub)));
        var form = With(Block(LandingBlocks.Subscribe, "form"),
            ("button", Map(LandingTexts.JoinTheWaitlist)), ("hint", Map(LandingTexts.WaitlistHint)),
            ("proof", Map(c.Proof)), ("note", Map(c.Note)));
        var heroShot = Block(LandingBlocks.Screenshot, "hero", hidden: !c.ShowShots);
        heroShot.Items.Add(Shot(c.Hero));

        var steps = Block(LandingBlocks.Hint, "steps");
        foreach (var key in new[] { "write", "channels", "publish" })
            steps.Items.Add(Item(("title", Copy(key + "Title")), ("text", Copy(key + "Body"))));
        var social = With(Block(LandingBlocks.Social), ("lead", Map(LandingTexts.OnePostEveryAddress)));

        var examples = With(Block(LandingBlocks.Text, "copy"),
            ("title", Copy("examplesTitle")), ("body", Copy("examplesBody")), ("linkLabel", Copy("examplesLink")));
        examples.Url = c.ShowcaseBlog is null ? null : "https://" + c.ShowcaseBlog;
        var gallery = Block(LandingBlocks.Screenshot, "gallery", hidden: !c.ShowShots);
        gallery.Items.AddRange((c.Gallery.Count > 0 ? c.Gallery : [new LandingShot("/landing-blog.png", LandingTexts.ViewScreen)])
            .Select(Shot));

        var benefits = Block(LandingBlocks.Hint, "cards");
        foreach (var (icon, title, text) in new[]
                 {
                     ("translate", Counted(LandingTexts.LanguagesTitle, LanguagesToken), Map(LandingTexts.TranslationDescription)),
                     ("timer", Map(LandingTexts.Scheduler), Map(LandingTexts.SchedulerDescription)),
                     ("download-simple", Map(LandingTexts.TheTextsStayYours), Map(LandingTexts.ExportDescription)),
                 })
        {
            var item = Item(("title", title), ("text", text));
            item.Icon = icon;
            benefits.Items.Add(item);
        }
        var tools = With(Block(LandingBlocks.Tools), ("title", Map(LandingTexts.AllTools)));

        var pricingHead = With(Block(LandingBlocks.Text, "heading"),
            ("title", Copy("pricingTitle")), ("body", Copy("pricingBody")));
        var pricing = Block(LandingBlocks.Pricing);
        pricing.Options[LandingBlocks.ShowNotes] = true;
        pricing.Options[LandingBlocks.ShowComparison] = true;

        var faq = With(Block(LandingBlocks.Faq), ("title", Copy("faqTitle")));
        for (var i = 1; i <= 3; i++)
            faq.Items.Add(Item(("question", Copy($"faq{i}Question")), ("answer", Copy($"faq{i}Answer"))));

        var roadmapHead = With(Block(LandingBlocks.Text, "rule"),
            ("title", Map("Roadmap", "Roadmap")), ("meta", Map(LandingTexts.DoneInProgressNext)));
        var roadmap = Block(LandingBlocks.Timeline, "board");
        foreach (var column in c.Roadmap)
        {
            var item = Item(("title", Map(column.Title)));
            item.Mark = Marks.Contains(column.Mark) ? column.Mark : "next";
            item.Entries = (column.Items ?? []).Select(Map).ToList();
            roadmap.Items.Add(item);
        }

        var storyHead = With(Block(LandingBlocks.Text, "rule"),
            ("title", Map(LandingTexts.WhyThisExists)), ("meta", Map(LandingTexts.BrieflyByMilestones)));
        var story = Block(LandingBlocks.Timeline, "milestones");
        story.Image = c.Hero.File;
        foreach (var step in c.Story)
            story.Items.Add(Item(("when", Map(step.When)), ("title", Map(step.Title)), ("text", Map(step.Text))));

        var downloadHead = With(Block(LandingBlocks.Text, "rule"),
            ("title", Map(LandingTexts.TheDesktopApp)), ("meta", Map("Windows", "Windows")));
        var download = With(Block(LandingBlocks.Download),
            ("title", Map(LandingTexts.TheSameBenchInItsOwnWindow)), ("body", Map(LandingTexts.DesktopDescription)),
            ("meta", Map(LandingTexts.KeepsItselfUpdatedWithEveryRelease)), ("button", Map(LandingTexts.DownloadForWindows)));

        var closing = With(Block(LandingBlocks.Text, "plain"),
            ("title", Copy("closingTitle")), ("body", Copy("closingBody")));
        var seat = With(Block(LandingBlocks.Subscribe, "button"), ("button", Map(LandingTexts.SaveMySeat)));

        return new LandingDocument
        {
            ShowcaseBlog = Blank(row?.ShowcaseBlog),
            Features = LegacyFeatures(),
            Sections =
            [
                new() { Id = "hero", Layout = LandingLayouts.Hero, Blocks = [hero, form, heroShot] },
                new()
                {
                    Id = "features", Layout = LandingLayouts.Stack, Anchor = "features", Hidden = !c.ShowFeatures,
                    Nav = Map(LandingTexts.HowItWorks), Label = Copy("workflowTitle"), Blocks = [steps, social],
                },
                new()
                {
                    Id = "examples", Layout = LandingLayouts.Split, Anchor = "examples",
                    Hidden = !c.ShowShots && c.ShowcaseBlog is null,
                    Nav = Map(LandingTexts.Examples), Blocks = [examples, gallery],
                },
                new() { Id = "discovery", Layout = LandingLayouts.Flow, Blocks = [Block(LandingBlocks.Discovery)] },
                new() { Id = "tools", Layout = LandingLayouts.Flow, Hidden = !c.ShowFeatures, Blocks = [benefits, tools] },
                new()
                {
                    Id = "pricing", Layout = LandingLayouts.Stack, Anchor = "pricing", Hidden = !c.ShowPricing,
                    Nav = Map(LandingTexts.Pricing), Blocks = [pricingHead, pricing],
                },
                new() { Id = "faq", Layout = LandingLayouts.Flow, Anchor = "faq", Blocks = [faq] },
                new()
                {
                    Id = "roadmap", Layout = LandingLayouts.Stack, Anchor = "roadmap",
                    Hidden = !c.ShowRoadmap || c.Roadmap.Count == 0, Blocks = [roadmapHead, roadmap],
                },
                new()
                {
                    Id = "story", Layout = LandingLayouts.Stack, Anchor = "story",
                    Hidden = !c.ShowStory || c.Story.Count == 0, Blocks = [storyHead, story],
                },
                new()
                {
                    Id = "download", Layout = LandingLayouts.Stack, Anchor = "download", Hidden = !c.ShowDownload,
                    Blocks = [downloadHead, download],
                },
                new() { Id = "closing", Layout = LandingLayouts.Band, Blocks = [closing, seat] },
            ],
        };
    }

    private static List<LandingFeature> LegacyFeatures()
    {
        static LandingFeature Feature(string id, string icon, TextMap title, TextMap body, string screen) => new()
        {
            Id = id, Icon = icon, Title = title, Body = body, Shot = $"/assets/review/{screen}.png",
        };
        return
        [
            Feature("projects", "folder-open", Map(LandingTexts.ProjectsTool), Map(LandingTexts.ProjectsToolBody), "project"),
            Feature("glossary", "book-open", Map(LandingTexts.GlossaryTool), Map(LandingTexts.GlossaryToolBody), "glossary"),
            Feature("boards", "images", Map(LandingTexts.CanvasTool), Map(LandingTexts.CanvasToolBody), "canvas"),
            Feature("presets", "layout", Map(LandingTexts.PresetsTool), Map(LandingTexts.PresetsToolBody), "presets"),
            Feature("editor", "pencil-simple", Map(LandingTexts.BlockEditor), Map(LandingTexts.EditorDescription), "editor"),
            Feature("telegram", "paper-plane-tilt", Map(LandingTexts.PublishingToTelegram), Map(LandingTexts.TelegramDescription), "publishing"),
            Feature("networks", "tree-structure", Counted(LandingTexts.NetworksTitle, NetworksToken), Map(LandingTexts.NetworksDescription), "publishing"),
            Feature("blog", "newspaper", Map(LandingTexts.BlogOnYourOwnSubdomain), Map(LandingTexts.BlogDescription), "publishing"),
            Feature("languages", "translate", Counted(LandingTexts.LanguagesTitle, LanguagesToken), Map(LandingTexts.TranslationDescription), "editor"),
            Feature("comments", "chat-teardrop-dots", Map(LandingTexts.CommentsAndReactions), Map(LandingTexts.DiscussionDescription), "posts"),
            Feature("tasks", "kanban", Map(LandingTexts.TasksAndSprints), Map(LandingTexts.TasksDescription), "tasks"),
            Feature("assets", "images", Map(LandingTexts.AnAssetIndex), Map(LandingTexts.AssetsDescription), "library"),
            Feature("builds", "cube", Map(LandingTexts.Builds), Map(LandingTexts.BuildsDescription), "builds"),
            Feature("scheduler", "timer", Map(LandingTexts.Scheduler), Map(LandingTexts.SchedulerDescription), "calendar"),
            Feature("appearance", "eye", Map(LandingTexts.DayAndNight), Map(LandingTexts.AppearanceDescription), "appearance"),
            Feature("export", "download-simple", Map(LandingTexts.TheTextsStayYours), Map(LandingTexts.ExportDescription), "publishing"),
        ];
    }

    // A half the admin left blank is filled with the other one: that is what the page already
    // shows for it, and it makes the converted document satisfy "RU and EN are required".
    private static TextMap Map(LandingText? text) => Map(text?.En, text?.Ru);

    private static TextMap Map(Func<bool, string> text) => Map(text(false), text(true));

    private static TextMap Map(string? en, string? ru)
    {
        en = Blank(en);
        ru = Blank(ru);
        if (en is null && ru is null) return [];
        return new TextMap { [Localization.Languages.English] = en ?? ru!, [Localization.Languages.Russian] = ru ?? en! };
    }

    // Counts stay derived (ADR-323 §6): the stored title carries a token where the code's own
    // sentence carried the number.
    private static TextMap Counted(Func<bool, int, string> text, string token) => Map(
        text(false, CountSentinel).Replace(CountSentinel.ToString(), token),
        text(true, CountSentinel).Replace(CountSentinel.ToString(), token));
}
