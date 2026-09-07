using System.Text.Json.Serialization;

namespace CedarClerk.Localization;

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
