namespace CedarClerk.Core;

public sealed record HeaderSlotContext(
    string? AuthorDisplayName, string? ProfileUrl, string? ProfileLocation,
    DateTime? PublishedAt, int CharacterCount, int WordCount);

public sealed record HeaderSlotValue(string Text, string? LinkUrl);

public static class HeaderSlotRenderer
{
    public static HeaderSlotValue? Render(HeaderSlotType type, HeaderSlotContext ctx) => type switch
    {
        HeaderSlotType.AuthorSignature => ctx.AuthorDisplayName is { Length: > 0 } n ? new(n, null) : null,
        HeaderSlotType.Url => ctx.ProfileUrl is { Length: > 0 } u ? new(u, u) : null,
        HeaderSlotType.MapLocation => ctx.ProfileLocation is { Length: > 0 } loc
            ? new(loc, $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(loc)}")
            : null,
        HeaderSlotType.PublishedDate => ctx.PublishedAt is { } d ? new(d.ToString("d MMM yyyy"), null) : null,
        HeaderSlotType.Length => new($"{ctx.CharacterCount} characters", null),
        HeaderSlotType.TimeToRead => new($"{Math.Max(1, (int)Math.Ceiling(ctx.WordCount / 200.0))} min read", null),
        _ => null,
    };
}
