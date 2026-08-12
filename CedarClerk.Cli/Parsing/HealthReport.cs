using System.Text.Json;

namespace CedarClerk.Cli.Parsing;

public sealed record HealthReport(string Version, string Environment, bool OpenRegistration, DateTimeOffset? TimeUtc)
{
    public bool Answered => Version.Length > 0;
    public static readonly HealthReport Down = new("", "", false, null);
}

// /api/health is the one thing that says which build is actually serving, as opposed to which one is
// on disk. Parsed leniently: a field added to the endpoint later must not make this read as "down".
public static class HealthParser
{
    public static HealthReport Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return HealthReport.Down;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return HealthReport.Down;

            return new HealthReport(
                String(root, "version"),
                String(root, "env"),
                root.TryGetProperty("openRegistration", out var open) && open.ValueKind == JsonValueKind.True,
                root.TryGetProperty("timeUtc", out var time) && time.TryGetDateTimeOffset(out var stamp)
                    ? stamp
                    : null);
        }
        catch (JsonException)
        {
            // Cloudflare answers HTML when the origin is down; that is a real answer about the world,
            // and it means the same thing as no answer at all.
            return HealthReport.Down;
        }
    }

    private static string String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
