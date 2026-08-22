namespace CedarClerk.Core;

/// <summary>
/// The stroke glyphs the public pages draw, on the design system's 16px grid (ADR-179 clause 6).
///
/// Inline SVG rather than the app's generated Phosphor set: <c>assets/cedar-icons.js</c> is bundled
/// with the Angular app and the blog host never loads it, and nine glyphs do not pay for a second
/// delivery mechanism. They are here rather than in the server so the renderer and the endpoints
/// draw one eye, not two.
/// </summary>
public static class BlogIcons
{
    private const string Open =
        "<svg class=\"gl\" width=\"15\" height=\"15\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" "
        + "stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">";

    private static string Glyph(string body) => Open + body + "</svg>";

    public static readonly string Eye = Glyph(
        "<path d=\"M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>");

    public static readonly string ThumbUp = Glyph(
        "<path d=\"M7 21V10l4.2-7a2.4 2.4 0 0 1 2.4 2.9L12.8 10H19a2 2 0 0 1 2 2.4l-1.3 6.2A2 2 0 0 1 17.7 21H7Z\"/>"
        + "<path d=\"M7 10H3.6v11H7\"/>");

    public static readonly string ThumbDown = Glyph(
        "<path d=\"M17 3v11l-4.2 7a2.4 2.4 0 0 1-2.4-2.9l.8-4.1H5a2 2 0 0 1-2-2.4l1.3-6.2A2 2 0 0 1 6.3 3H17Z\"/>"
        + "<path d=\"M17 14h3.4V3H17\"/>");

    public static readonly string Chat = Glyph(
        "<path d=\"M21 14.5a3 3 0 0 1-3 3H8.6L4 21V5.5a3 3 0 0 1 3-3h11a3 3 0 0 1 3 3Z\"/>");

    public static readonly string Lock = Glyph(
        "<rect x=\"4\" y=\"10\" width=\"16\" height=\"11\" rx=\"2\"/><path d=\"M8 10V7a4 4 0 0 1 8 0v3\"/>");

    public static readonly string List = Glyph("<path d=\"M4 6h16M4 12h16M4 18h16\"/>");

    public static readonly string ArrowUp = Glyph("<path d=\"M12 20V5M5 12l7-7 7 7\"/>");

    public static readonly string ArrowLeft = Glyph("<path d=\"M20 12H5M12 19l-7-7 7-7\"/>");

    public static readonly string Sun = Glyph(
        "<circle cx=\"12\" cy=\"12\" r=\"4.2\"/>"
        + "<path d=\"M12 2.2v2M12 19.8v2M4.4 4.4l1.4 1.4M18.2 18.2l1.4 1.4M2.2 12h2M19.8 12h2M4.4 19.6l1.4-1.4M18.2 5.8l1.4-1.4\"/>");

    public static readonly string Moon = Glyph(
        "<path d=\"M20.4 14.6A8.6 8.6 0 0 1 9.4 3.6a8.6 8.6 0 1 0 11 11Z\"/>");
}
