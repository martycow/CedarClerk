using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CedarClerk.Server;

/// <summary>
/// Every <see cref="DateTime"/> leaves this server as UTC with a trailing Z (ADR-115).
///
/// SQLite has nowhere to store <see cref="DateTimeKind"/>, so EF hands back
/// <see cref="DateTimeKind.Unspecified"/> for values that are UTC in fact — everything in this
/// codebase is (<c>DateTime.UtcNow</c> everywhere, file times taken as <c>LastWriteTimeUtc</c>).
/// Serialized as-is, such a value prints without a suffix, and a browser reads an offset-less
/// timestamp as **local time** — which is why the app was showing UTC numbers labelled as local,
/// seven hours out, everywhere the one hand-rolled `utcDate()` helper had not been applied.
///
/// Fixing it here rather than at each call site is the point: a converter cannot be forgotten by
/// the next component.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToUtc(value).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

/// <summary>The nullable half — <see cref="UtcDateTimeConverter"/> does not cover <c>DateTime?</c> on its own.</summary>
public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.Parse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStringValue(UtcDateTimeConverter.ToUtc(value.Value)
            .ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
    }
}
