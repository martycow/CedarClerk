using System.Globalization;
using CedarClerk.Core;
using CedarClerk.Localization;

namespace CedarClerk.Tests;

// T-301 / ADR-218 — the kind-specific half of an item. A payload is stored and echoed whole, so
// what is pinned here is the refusals: silently dropping a field the board did not recognise would
// look, to the author, exactly like the board eating their work.
public class CanvasPayloadTests
{
    private const string Image = """{"url":"/media/asset_1.jpg","naturalWidth":1920,"naturalHeight":1080,"alt":""}""";
    private const string Note = """{"text":"hello","align":"left"}""";
    private const string Frame = """{"title":"Mood"}""";
    private const string Link = """{"url":"https://example.test/x","title":"","favicon":""}""";

    [Theory]
    [InlineData(CanvasItemKinds.Image, Image)]
    [InlineData(CanvasItemKinds.Note, Note)]
    [InlineData(CanvasItemKinds.Frame, Frame)]
    [InlineData(CanvasItemKinds.Link, Link)]
    public void Each_kind_accepts_its_own_shape(string kind, string json)
    {
        Assert.Null(CanvasPayload.Validate(kind, json));
    }

    [Theory]
    [InlineData(CanvasItemKinds.Image, """{"url":"/media/a.jpg","zoom":2}""")]
    [InlineData(CanvasItemKinds.Note, """{"text":"hi","colour":"red"}""")]
    [InlineData(CanvasItemKinds.Frame, """{"title":"m","locked":true}""")]
    [InlineData(CanvasItemKinds.Link, """{"url":"https://a.test","preview":"x"}""")]
    public void An_unknown_property_is_rejected(string kind, string json)
    {
        Assert.Equal(ErrorMessages.CanvasPayloadInvalid, CanvasPayload.Validate(kind, json));
    }

    [Theory]
    [InlineData("""{"url":"https://elsewhere.test/a.jpg"}""")]
    [InlineData("""{"url":"../media/a.jpg"}""")]
    [InlineData("""{"url":""}""")]
    public void An_image_url_outside_media_is_rejected(string json)
    {
        Assert.Equal(ErrorMessages.CanvasPayloadInvalid, CanvasPayload.Validate(CanvasItemKinds.Image, json));
    }

    [Theory]
    [InlineData("""{"url":"javascript:alert(1)"}""")]
    [InlineData("""{"url":"ftp://files.test/a"}""")]
    [InlineData("""{"url":"/media/a.jpg"}""")]
    public void A_link_url_that_is_not_http_is_rejected(string json)
    {
        Assert.Equal(ErrorMessages.CanvasPayloadInvalid, CanvasPayload.Validate(CanvasItemKinds.Link, json));
    }

    [Fact]
    public void A_payload_over_the_limit_is_rejected()
    {
        var json = $$"""{"title":"{{new string('x', Consts.Canvas.PayloadMaxChars)}}"}""";
        Assert.Equal(ErrorMessages.CanvasPayloadTooLarge(Consts.Canvas.PayloadMaxChars),
            CanvasPayload.Validate(CanvasItemKinds.Frame, json));
    }

    [Fact]
    public void A_note_longer_than_the_text_limit_is_rejected()
    {
        // Under the payload ceiling and still refused: the two limits mean different things, and a
        // note is bounded by how much of it a person could read, not by how much JSON fits.
        var text = new string('n', Consts.Canvas.NoteTextMax + 1);
        Assert.True(text.Length + 20 < Consts.Canvas.PayloadMaxChars);
        Assert.Equal(ErrorMessages.CanvasPayloadTooLarge(Consts.Canvas.NoteTextMax),
            CanvasPayload.Validate(CanvasItemKinds.Note, $$"""{"text":"{{text}}"}"""));
    }

    [Fact]
    public void An_unknown_kind_names_itself_in_the_refusal()
    {
        Assert.Equal(ErrorMessages.UnknownCanvasItemKind("sticker"),
            CanvasPayload.Validate("sticker", "{}"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("")]
    public void Anything_that_is_not_a_json_object_is_rejected(string json)
    {
        Assert.Equal(ErrorMessages.CanvasPayloadInvalid, CanvasPayload.Validate(CanvasItemKinds.Note, json));
    }

    [Fact]
    public void A_note_align_the_board_cannot_draw_is_rejected()
    {
        Assert.Null(CanvasPayload.Validate(CanvasItemKinds.Note, """{"text":"a","align":"center"}"""));
        Assert.Equal(ErrorMessages.CanvasPayloadInvalid,
            CanvasPayload.Validate(CanvasItemKinds.Note, """{"text":"a","align":"justify"}"""));
    }

    [Fact]
    public void A_wrongly_typed_property_is_rejected_rather_than_coerced()
    {
        Assert.Equal(ErrorMessages.CanvasPayloadInvalid,
            CanvasPayload.Validate(CanvasItemKinds.Image, """{"url":"/media/a.jpg","naturalWidth":"1920"}"""));
        Assert.Equal(ErrorMessages.CanvasPayloadInvalid,
            CanvasPayload.Validate(CanvasItemKinds.Note, """{"text":7}"""));
    }

    [Fact]
    public void Every_refusal_it_can_give_is_translated()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            Assert.Equal("Содержимое этого объекта доска не понимает.",
                CanvasPayload.Validate(CanvasItemKinds.Note, "[]"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
