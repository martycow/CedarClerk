using CedarClerk.Localization;
using CedarClerk.Core;
using CedarClerk.Server.Publishing;

namespace CedarClerk.Tests;

// The per-channel signature override, the silent flag and the pin decision are all pure
// functions on TelegramPublishTarget — deliberately, so they test without a TelegramBotClient
// (the 409 rule's testing shape: bot logic ships with unit tests only).
public class TelegramPinSignatureTests
{
    [Fact]
    public void Channel_signature_replaces_the_owner_trio_wholesale()
    {
        var (text, translations, url) = TelegramPublishTarget.PickSignatureSource(
            "Channel sig", """{"ru":"Канал"}""", "https://channel.example.com",
            "Owner sig", """{"ru":"Владелец"}""", "https://owner.example.com");

        Assert.Equal("Channel sig", text);
        Assert.Equal("""{"ru":"Канал"}""", translations);
        Assert.Equal("https://channel.example.com", url);
    }

    [Fact]
    public void Without_a_channel_signature_the_owner_trio_applies()
    {
        var (text, translations, url) = TelegramPublishTarget.PickSignatureSource(
            null, null, null,
            "Owner sig", """{"ru":"Владелец"}""", "https://owner.example.com");

        Assert.Equal("Owner sig", text);
        Assert.Equal("""{"ru":"Владелец"}""", translations);
        Assert.Equal("https://owner.example.com", url);
    }

    [Fact]
    public void Channel_override_never_borrows_the_owner_url_or_translations()
    {
        // A channel that sets only the text has decided against a link and against translations —
        // mixing in the owner's would sign the channel with half of somebody else's signature.
        var (text, translations, url) = TelegramPublishTarget.PickSignatureSource(
            "Channel sig", null, null,
            "Owner sig", """{"ru":"Владелец"}""", "https://owner.example.com");

        Assert.Equal("Channel sig", text);
        Assert.Null(translations);
        Assert.Null(url);
    }

    [Fact]
    public void Empty_channel_signature_still_overrides()
    {
        // Non-null is the switch: an empty string is "this channel signs with nothing", which
        // PlanLimitations turns into no signature for Pro and the fixed attribution for Free.
        var (text, _, _) = TelegramPublishTarget.PickSignatureSource(
            "", null, null,
            "Owner sig", null, "https://owner.example.com");

        Assert.Equal("", text);
        Assert.Null(PlanLimitations.ResolveSignature(PlanTiers.Pro, LocalizedTextMap.Pick(text, null, "en"), null));
    }

    [Fact]
    public void Channel_signature_localizes_through_the_same_pick()
    {
        // "ru" is LocalizedTextMap's primary language: it reads the primary column, every other
        // language reads the translations map — same rule as the owner-level signature.
        var (text, translations, _) = TelegramPublishTarget.PickSignatureSource(
            "Канал", """{"en":"Channel sig"}""", null,
            "Owner sig", null, null);

        Assert.Equal("Канал", LocalizedTextMap.Pick(text, translations, "ru"));
        Assert.Equal("Channel sig", LocalizedTextMap.Pick(text, translations, "en"));
    }

    [Theory]
    [InlineData(false, null, null, false)]
    [InlineData(true, null, null, true)]
    [InlineData(true, 0, 8, true)]
    [InlineData(false, 0, 8, false)]
    [InlineData(false, 1, 8, true)]
    [InlineData(true, 7, 8, true)]
    public void Silent_flag_and_later_thread_parts_both_silence(bool silent, int? partIndex, int? partCount, bool expected)
    {
        var part = partIndex is { } i ? new ThreadPartRef(i, partCount!.Value, null) : null;

        Assert.Equal(expected, TelegramPublishTarget.ShouldSilence(silent, part));
    }

    [Theory]
    [InlineData(false, null, null, false)]
    [InlineData(true, null, null, true)]
    [InlineData(true, 0, 8, true)]
    [InlineData(true, 1, 8, false)]
    [InlineData(true, 7, 8, false)]
    [InlineData(false, 0, 8, false)]
    public void Only_the_thread_root_gets_pinned(bool pinAfterSend, int? partIndex, int? partCount, bool expected)
    {
        var part = partIndex is { } i ? new ThreadPartRef(i, partCount!.Value, null) : null;

        Assert.Equal(expected, TelegramPublishTarget.ShouldPin(pinAfterSend, part));
    }
}
