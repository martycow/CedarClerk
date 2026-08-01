using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

/// <summary>
/// What a network will do to a document that does not fit it (T-086). Codes, not sentences: the
/// wording belongs to the UI, which is localized, and Core is not — the same split the blog
/// renderer already makes by taking a language string instead of importing the dictionaries.
/// </summary>
public static class PublishIssueCodes
{
    public const string TooLong = "too-long";
    public const string TooManyMedia = "too-many-media";
    public const string ImageTooLarge = "image-too-large";
    public const string NoVideo = "no-video";
    public const string NoAudio = "no-audio";
    public const string NoTables = "no-tables";
    public const string NoMath = "no-math";
    public const string NoCodeBlocks = "no-code-blocks";
    public const string NoHeadings = "no-headings";
    public const string NoLists = "no-lists";
    public const string NoRichText = "no-rich-text";
    public const string SlowMedia = "slow-media";
}

/// <param name="Blocking">
/// True when the network would refuse the post outright; false when it would accept it and drop or
/// flatten something. The distinction is the whole point of showing this before the send: one is
/// "fix this", the other is "know this".
/// </param>
public sealed record PublishIssue(string Code, bool Blocking, long Actual = 0, long Limit = 0);

/// <summary>
/// Checks a document against a network's <see cref="PublishCapabilities"/> before anything is sent
/// (T-086, ADR-078). Pure and unit-tested, because the alternative — finding out from the network's
/// error message — is what publishing a table to Telegram did for two weeks.
/// </summary>
public static class PublishValidator
{
    /// <summary>Total bytes above which a send is worth warning about regardless of the per-file limit.</summary>
    public const long SlowMediaTotalBytes = 20L * 1024 * 1024;

    /// <param name="mediaBytes">
    /// Size of each referenced media path, where known. Telegram and Bluesky both fetch or upload
    /// the bytes at send time, so this is what decides whether a publish takes two seconds or two
    /// minutes — and a publish that outlives the browser's patience is the failure Marty hit
    /// (01.08.2026: a 15MB audio plus a 15MB video on a home connection).
    /// </param>
    public static IReadOnlyList<PublishIssue> Validate(
        string cedarJson,
        PublishCapabilities capabilities,
        IReadOnlyDictionary<string, long>? mediaBytes = null)
    {
        var issues = new List<PublishIssue>();
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(cedarJson);
        }
        catch (JsonException)
        {
            return issues;
        }
        if (doc is null) return issues;

        var stats = Collect(doc);

        if (capabilities.MaxCharacters is { } maxChars && stats.Characters > maxChars)
            issues.Add(new PublishIssue(PublishIssueCodes.TooLong, true, stats.Characters, maxChars));

        if (stats.MediaCount > capabilities.MaxMediaItems)
            issues.Add(new PublishIssue(PublishIssueCodes.TooManyMedia, true, stats.MediaCount, capabilities.MaxMediaItems));

        if (stats.HasVideo && !capabilities.SupportsVideo)
            issues.Add(new PublishIssue(PublishIssueCodes.NoVideo, false));
        if (stats.HasAudio && !capabilities.SupportsAudio)
            issues.Add(new PublishIssue(PublishIssueCodes.NoAudio, false));
        if (stats.HasTable && !capabilities.SupportsTables)
            issues.Add(new PublishIssue(PublishIssueCodes.NoTables, false));
        if (stats.HasMath && !capabilities.SupportsMath)
            issues.Add(new PublishIssue(PublishIssueCodes.NoMath, false));
        if (stats.HasCodeBlock && !capabilities.SupportsCodeBlocks)
            issues.Add(new PublishIssue(PublishIssueCodes.NoCodeBlocks, false));
        if (stats.HasHeading && !capabilities.SupportsHeadings)
            issues.Add(new PublishIssue(PublishIssueCodes.NoHeadings, false));
        if (stats.HasList && !capabilities.SupportsLists)
            issues.Add(new PublishIssue(PublishIssueCodes.NoLists, false));
        if (stats.HasFormatting && !capabilities.SupportsRichText)
            issues.Add(new PublishIssue(PublishIssueCodes.NoRichText, false));

        if (mediaBytes is { Count: > 0 })
        {
            var referenced = CedarPackage.FindReferencedMediaPaths(cedarJson);
            long total = 0;
            foreach (var path in referenced)
            {
                if (!mediaBytes.TryGetValue(path, out var bytes)) continue;
                total += bytes;
                if (capabilities.MaxImageBytes is { } maxImage && bytes > maxImage && IsImage(path))
                    issues.Add(new PublishIssue(PublishIssueCodes.ImageTooLarge, true, bytes, maxImage));
            }

            // Not blocking: it is within every limit and will go through. It is also the difference
            // between a publish that answers and one that outlives the proxy in front of it.
            if (total > SlowMediaTotalBytes)
                issues.Add(new PublishIssue(PublishIssueCodes.SlowMedia, false, total, SlowMediaTotalBytes));
        }

        return issues;
    }

    private static bool IsImage(string path) =>
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    private sealed class Stats
    {
        public long Characters;
        public int MediaCount;
        public bool HasVideo, HasAudio, HasTable, HasMath, HasCodeBlock, HasHeading, HasList, HasFormatting;
    }

    private static Stats Collect(JsonNode doc)
    {
        var stats = new Stats();
        Walk(doc, stats);
        return stats;
    }

    private static void Walk(JsonNode? node, Stats stats)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array) Walk(child, stats);
                return;

            case JsonObject obj:
                var type = (string?)obj["type"];
                switch (type)
                {
                    case "text":
                        stats.Characters += ((string?)obj["text"])?.Length ?? 0;
                        if (obj["marks"] is JsonArray { Count: > 0 }) stats.HasFormatting = true;
                        break;
                    case "image": stats.MediaCount++; break;
                    case "video": stats.MediaCount++; stats.HasVideo = true; break;
                    case "audio": stats.MediaCount++; stats.HasAudio = true; break;
                    // A carousel or collage is several media in one node — counted by its children,
                    // which is what the network counts too.
                    case "carousel":
                    case "collage":
                        stats.MediaCount += (obj["attrs"]?["images"] as JsonArray)?.Count ?? 0;
                        break;
                    case "table": stats.HasTable = true; break;
                    case "math":
                    case "mathBlock": stats.HasMath = true; break;
                    case "codeBlock": stats.HasCodeBlock = true; break;
                    case "heading": stats.HasHeading = true; break;
                    case "bulletList":
                    case "orderedList":
                    case "taskList": stats.HasList = true; break;
                }

                foreach (var (_, value) in obj) Walk(value, stats);
                return;
        }
    }
}
