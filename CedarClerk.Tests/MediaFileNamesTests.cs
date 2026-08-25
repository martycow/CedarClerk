using CedarClerk.Server;

namespace CedarClerk.Tests;

// The parser is the only thing standing between /media and a database round-trip per guessed name,
// so what it refuses matters more than what it accepts.
public class MediaFileNamesTests
{
    private static readonly Guid Id = Guid.Parse("2f1a6c3e-9b47-4d21-8a55-0e7c1d3b6f90");

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    [InlineData(".mp4")]
    public void An_asset_original_is_recognised(string ext)
    {
        Assert.True(MediaFileNames.TryParse($"asset_{Id}{ext}", out var reference));
        Assert.Equal(new MediaRef(MediaRefKind.AssetOriginal, Id), reference);
    }

    [Fact]
    public void The_telegram_derivative_resolves_to_the_same_asset()
    {
        Assert.True(MediaFileNames.TryParse($"asset_{Id}_tg.jpg", out var reference));
        Assert.Equal(MediaRefKind.AssetDerivative, reference.Kind);
        Assert.Equal(Id, reference.Id);
    }

    [Fact]
    public void A_channel_avatar_is_recognised()
    {
        Assert.True(MediaFileNames.TryParse($"channels/{Id}.jpg", out var reference));
        Assert.Equal(new MediaRef(MediaRefKind.ChannelAvatar, Id), reference);
    }

    [Fact]
    public void An_uppercase_guid_is_the_same_file()
    {
        Assert.True(MediaFileNames.TryParse($"asset_{Id.ToString().ToUpperInvariant()}.jpg", out var reference));
        Assert.Equal(Id, reference.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../cedar.db")]
    [InlineData("..\\cedar.db")]
    [InlineData("channels/../../cedar.db")]
    [InlineData("a/b/c.jpg")]
    [InlineData("/asset_2f1a6c3e-9b47-4d21-8a55-0e7c1d3b6f90.jpg")]
    [InlineData("asset_notaguid.jpg")]
    [InlineData("asset_2f1a6c3e-9b47-4d21-8a55-0e7c1d3b6f90")]
    [InlineData("asset_.jpg")]
    [InlineData("channels/x.jpg")]
    [InlineData("channels/2f1a6c3e-9b47-4d21-8a55-0e7c1d3b6f90.png")]
    [InlineData("thumbs/2f1a6c3e-9b47-4d21-8a55-0e7c1d3b6f90.jpg")]
    [InlineData("dataprotection-keys/key.xml")]
    [InlineData("cedar.db")]
    public void Anything_not_ours_is_refused(string path)
    {
        Assert.False(MediaFileNames.TryParse(path, out _));
    }
}
