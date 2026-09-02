namespace CedarClerk.Core;

/// <param name="Length">In the network's own units — X's weighted characters, Bluesky's graphemes, Discord's chars.</param>
/// <param name="ImageUrls">Relative <c>/media/…</c> references the network would carry; empty where it attaches nothing.</param>
/// <param name="LinkUrl">The blog link inside <paramref name="Text"/>, when the post carries one.</param>
public sealed record MicroPreviewPost(int Index, string Text, int Length, IReadOnlyList<string> ImageUrls, string? LinkUrl);

/// <param name="Single">The announcement: the author's own text or the teaser, plus the blog link.</param>
/// <param name="Thread">The whole document as parts; empty where the network never threads.</param>
public sealed record MicroPreview(
    string Network,
    string Language,
    int MaxLength,
    bool HasAuthorText,
    bool SupportsThreads,
    string? BlogUrl,
    MicroPreviewPost Single,
    IReadOnlyList<MicroPreviewPost> Thread);

// ADR-239 clause 10, for the short-post networks — the same builders and the same splitter the
// targets run, so the card the editor draws cannot differ from the post that goes out. Nothing
// here sends.
public static class MicroPreviewProjection
{
    public static MicroPreview Project(string network, string language, string cedarJson, string? authorText,
        string? blogUrl, int linkReserve, PublishCapabilities capabilities)
    {
        var (measure, limit) = MicroThreadSplitter.ForNetwork(network);
        var images = capabilities.MaxMediaItems > 0 ? LocalImages(cedarJson, capabilities.MaxMediaItems) : [];

        var single = network switch
        {
            PublishNetworks.X => XPostBuilder.Build(authorText, cedarJson, blogUrl).Text,
            PublishNetworks.Bluesky => BlueskyPostBuilder.Build(authorText, cedarJson, blogUrl).Text,
            _ => DiscordPostBuilder.Build(authorText, cedarJson, blogUrl),
        };

        var thread = new List<MicroPreviewPost>();
        if (capabilities.SupportsThreads)
        {
            var parts = MicroThreadSplitter.Split(cedarJson, network, linkReserve);
            for (var i = 0; i < parts.Count; i++)
            {
                var last = i == parts.Count - 1;
                var text = last && blogUrl is not null ? $"{parts[i]}\n\n{blogUrl}" : parts[i];
                thread.Add(new MicroPreviewPost(i, text, measure(text), i == 0 ? images : [], last ? blogUrl : null));
            }
        }

        return new MicroPreview(
            network, language, limit,
            HasAuthorText: !string.IsNullOrWhiteSpace(authorText),
            SupportsThreads: capabilities.SupportsThreads,
            blogUrl,
            new MicroPreviewPost(0, single, measure(single), images, blogUrl is not null && single.Contains(blogUrl) ? blogUrl : null),
            thread);
    }

    /// <summary>The pictures a network would attach: the first N on this server's disk, in reading order.</summary>
    private static List<string> LocalImages(string cedarJson, int max) =>
        CedarImageRefs.Collect(cedarJson)
            .Where(r => CedarImageRefs.LocalFileName(r.Src) is not null)
            .Select(r => r.Src)
            .Take(max)
            .ToList();
}
