using System.Text;

namespace CedarClerk.Core;

// Watermark tiled over a private post's blog page (I7).
//
// The overlay is one CSS background-image that repeats, not N repeated elements: the sheet's
// height depends on the post, and a tiling background covers any height without the renderer
// having to guess how many copies to emit.
//
// The tile is an SVG carried as a base64 data URI. Base64 rather than percent-encoded XML on
// purpose — the payload is author-supplied text landing inside a CSS url(), and base64 removes
// every quote, paren and backslash from that context outright instead of relying on getting an
// escaping table right. The text is still XML-escaped inside the SVG itself, per the escaping
// invariant in .claude/rules/renderers.md.
public static class WatermarkRenderer
{
    private const int TileHeight = 170;
    private const int MinTileWidth = 300;
    private const int FontSize = 28;
    private const double RotationDegrees = -24;

    // Mid-grey at low opacity, deliberately not a theme colour: a data-URI SVG can't read the
    // page's CSS variables, and grey is the one value that stays legible-but-faint on both the
    // light and the dark blog theme.
    private const string Fill = "#808080";
    private const string FillOpacity = "0.16";

    /// <summary>
    /// The full overlay element, or an empty string when there is no watermark to draw.
    /// Caller places it inside a positioned container (the post sheet).
    /// </summary>
    public static string OverlayHtml(string? text)
    {
        var css = BackgroundCss(text);
        return css.Length == 0
            ? ""
            : $"<div class=\"watermark-overlay\" aria-hidden=\"true\" style=\"{css}\"></div>";
    }

    /// <summary>
    /// Just the <c>background-image</c>/<c>background-size</c> declarations for the tile, so the
    /// element itself can be styled in the page stylesheet. Empty when there's nothing to draw.
    /// </summary>
    public static string BackgroundCss(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "";
        if (trimmed.Length > Consts.Watermark.MaxLength)
            trimmed = trimmed[..Consts.Watermark.MaxLength];

        // Wider text needs a wider tile, otherwise the glyphs run past the tile edge and the
        // repeat visibly clips them mid-word.
        var width = Math.Max(MinTileWidth, trimmed.Length * FontSize * 2 / 3);
        var svg = BuildSvg(trimmed, width);
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        return $"background-image:url(data:image/svg+xml;base64,{data});background-size:{width}px {TileHeight}px";
    }

    private static string BuildSvg(string text, int width)
    {
        var safe = XmlEscape(text);
        var cx = width / 2;
        var cy = TileHeight / 2;
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{TileHeight}"><text x="{cx}" y="{cy}" transform="rotate({RotationDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)} {cx} {cy})" text-anchor="middle" dominant-baseline="middle" font-family="Helvetica, Arial, sans-serif" font-size="{FontSize}" font-weight="900" letter-spacing="2" fill="{Fill}" fill-opacity="{FillOpacity}">{safe}</text></svg>
            """;
    }

    // Not WebUtility.HtmlEncode: this is XML content, where the named entities that method emits
    // for non-ASCII (and its &#39; for the apostrophe) are unnecessary, and Cyrillic watermarks
    // are the expected case here — the UTF-8 bytes go into the base64 payload as-is.
    private static string XmlEscape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}
