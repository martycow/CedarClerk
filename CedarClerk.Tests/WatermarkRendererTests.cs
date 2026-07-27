using System.Text;
using CedarClerk.Core;

namespace CedarClerk.Tests;

public class WatermarkRendererTests
{
    private static string DecodeSvg(string css)
    {
        var start = css.IndexOf("base64,", StringComparison.Ordinal) + "base64,".Length;
        var end = css.IndexOf(')', start);
        return Encoding.UTF8.GetString(Convert.FromBase64String(css[start..end]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_watermark_renders_nothing(string? text)
    {
        Assert.Equal("", WatermarkRenderer.BackgroundCss(text));
        Assert.Equal("", WatermarkRenderer.OverlayHtml(text));
    }

    [Fact]
    public void Overlay_carries_the_tile_and_is_hidden_from_assistive_tech()
    {
        var html = WatermarkRenderer.OverlayHtml("CONFIDENTIAL");
        Assert.Contains("class=\"watermark-overlay\"", html);
        Assert.Contains("aria-hidden=\"true\"", html);
        Assert.Contains("background-image:url(data:image/svg+xml;base64,", html);
    }

    [Fact]
    public void Text_reaches_the_svg()
    {
        var svg = DecodeSvg(WatermarkRenderer.BackgroundCss("CONFIDENTIAL"));
        Assert.Contains(">CONFIDENTIAL</text>", svg);
    }

    [Fact]
    public void Cyrillic_survives_the_roundtrip()
    {
        var svg = DecodeSvg(WatermarkRenderer.BackgroundCss("Не распространять"));
        Assert.Contains(">Не распространять</text>", svg);
    }

    // The escaping invariant (.claude/rules/renderers.md): author text must never be able to
    // close the element it sits in, nor break out of the CSS url() the tile is carried in.
    [Fact]
    public void Markup_in_the_text_is_escaped_not_emitted()
    {
        var css = WatermarkRenderer.BackgroundCss("</text><script>alert(1)</script>");
        var svg = DecodeSvg(css);
        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&lt;/text&gt;&lt;script&gt;", svg);
    }

    [Fact]
    public void Ampersands_and_quotes_are_escaped()
    {
        var svg = DecodeSvg(WatermarkRenderer.BackgroundCss("A & \"B\" & 'C'"));
        Assert.Contains("A &amp; &quot;B&quot; &amp; &apos;C&apos;", svg);
    }

    // Base64 is what makes this true regardless of the input — no quote, paren or backslash from
    // author text can ever appear inside the url().
    [Fact]
    public void Url_payload_stays_base64_even_for_hostile_text()
    {
        var css = WatermarkRenderer.BackgroundCss("\");background:red;x:url(\"");
        var start = css.IndexOf("base64,", StringComparison.Ordinal) + "base64,".Length;
        var payload = css[start..css.IndexOf(')', start)];
        Assert.Matches("^[A-Za-z0-9+/=]+$", payload);
    }

    [Fact]
    public void Overlong_text_is_clamped_to_the_configured_maximum()
    {
        var svg = DecodeSvg(WatermarkRenderer.BackgroundCss(new string('x', Consts.Watermark.MaxLength + 40)));
        Assert.Contains(new string('x', Consts.Watermark.MaxLength) + "</text>", svg);
    }

    [Fact]
    public void Tile_widens_with_longer_text_so_glyphs_are_not_clipped()
    {
        var shortCss = WatermarkRenderer.BackgroundCss("AB");
        var longCss = WatermarkRenderer.BackgroundCss(new string('W', Consts.Watermark.MaxLength));
        Assert.Contains("background-size:300px", shortCss);
        Assert.DoesNotContain("background-size:300px", longCss);
    }
}
