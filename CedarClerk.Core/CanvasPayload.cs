using System.Text.Json;
using CedarClerk.Localization;

namespace CedarClerk.Core;

/// <summary>
/// The kind-specific half of a canvas item. Geometry is columns; everything that differs between an
/// image and a note is this one JSON string, replaced whole rather than patched field by field.
/// </summary>
public static class CanvasPayload
{
    /// <summary>An ErrorMessages sentence, or null when the payload is acceptable for that kind.</summary>
    public static string? Validate(string? kind, string? json)
    {
        if (!CanvasItemKinds.IsKnown(kind)) return ErrorMessages.UnknownCanvasItemKind(kind ?? "");
        if (string.IsNullOrWhiteSpace(json)) return ErrorMessages.CanvasPayloadInvalid;
        if (json.Length > Consts.Canvas.PayloadMaxChars)
            return ErrorMessages.CanvasPayloadTooLarge(Consts.Canvas.PayloadMaxChars);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return ErrorMessages.CanvasPayloadInvalid;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ErrorMessages.CanvasPayloadInvalid;

            return kind switch
            {
                CanvasItemKinds.Image => Image(root),
                CanvasItemKinds.Note => Note(root),
                CanvasItemKinds.Frame => Frame(root),
                CanvasItemKinds.Link => Link(root),
                _ => ErrorMessages.UnknownCanvasItemKind(kind ?? ""),
            };
        }
    }

    private static string? Image(JsonElement root)
    {
        var unknown = OnlyKnown(root, "url", "naturalWidth", "naturalHeight", "alt");
        if (unknown is not null) return unknown;

        var url = Text(root, "url");
        if (url is null) return ErrorMessages.CanvasPayloadInvalid;
        // Own uploads only: an outside address is what the link kind is for, and it would also make
        // a board silently fetch from wherever the author pasted from.
        if (!url.StartsWith("/media/", StringComparison.Ordinal)) return ErrorMessages.CanvasPayloadInvalid;

        if (!Number(root, "naturalWidth") || !Number(root, "naturalHeight")) return ErrorMessages.CanvasPayloadInvalid;
        if (root.TryGetProperty("alt", out var alt) && alt.ValueKind != JsonValueKind.String)
            return ErrorMessages.CanvasPayloadInvalid;

        return null;
    }

    private static string? Note(JsonElement root)
    {
        var unknown = OnlyKnown(root, "text", "align");
        if (unknown is not null) return unknown;

        var text = Text(root, "text");
        if (text is null) return ErrorMessages.CanvasPayloadInvalid;
        if (text.Length > Consts.Canvas.NoteTextMax)
            return ErrorMessages.CanvasPayloadTooLarge(Consts.Canvas.NoteTextMax);

        if (root.TryGetProperty("align", out var align))
        {
            if (align.ValueKind != JsonValueKind.String) return ErrorMessages.CanvasPayloadInvalid;
            if (align.GetString() is not ("left" or "center")) return ErrorMessages.CanvasPayloadInvalid;
        }

        return null;
    }

    private static string? Frame(JsonElement root)
    {
        var unknown = OnlyKnown(root, "title");
        if (unknown is not null) return unknown;
        return Text(root, "title") is null ? ErrorMessages.CanvasPayloadInvalid : null;
    }

    private static string? Link(JsonElement root)
    {
        var unknown = OnlyKnown(root, "url", "title", "favicon");
        if (unknown is not null) return unknown;

        var url = Text(root, "url");
        if (url is null) return ErrorMessages.CanvasPayloadInvalid;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return ErrorMessages.CanvasPayloadInvalid;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return ErrorMessages.CanvasPayloadInvalid;

        if (Text(root, "title") is null) return ErrorMessages.CanvasPayloadInvalid;
        if (Text(root, "favicon") is null) return ErrorMessages.CanvasPayloadInvalid;

        return null;
    }

    /// <summary>
    /// A property nobody recognises is refused rather than dropped: a payload is stored and echoed
    /// whole, so silently losing a field would look like the board eating the author's work.
    /// </summary>
    private static string? OnlyKnown(JsonElement root, params string[] allowed)
    {
        foreach (var property in root.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                return ErrorMessages.CanvasPayloadInvalid;
        return null;
    }

    /// <summary>The property's string, "" when absent, null when present but not a string.</summary>
    private static string? Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : null;
    }

    private static bool Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return true;
        return value.ValueKind == JsonValueKind.Number;
    }
}
