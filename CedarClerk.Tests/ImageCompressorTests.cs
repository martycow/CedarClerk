using CedarClerk.Server;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CedarClerk.Tests;

public class ImageCompressorTests
{
    // Random noise compresses far worse than a real photo at any given JPEG quality, so it's a
    // reliable stress case for "does this actually get under budget" without needing a fixture file.
    private static byte[] MakeLargeNoiseJpeg(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        var rng = new Random(42);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgb24((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
            }
        });

        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 100 });
        return ms.ToArray();
    }

    [Fact]
    public void Compresses_an_oversized_photo_under_the_target_size()
    {
        var original = MakeLargeNoiseJpeg(4000, 3000);
        var targetMaxBytes = 4L * 1024 * 1024;
        Assert.True(original.Length > targetMaxBytes, "test fixture should start out over budget");

        var compressed = ImageCompressor.TryCompressJpeg(original, targetMaxBytes);

        Assert.NotNull(compressed);
        Assert.True(compressed!.Length < original.Length);
        Assert.True(compressed.Length <= targetMaxBytes);

        // Still a valid, decodable image afterwards, not corrupted output.
        using var decoded = Image.Load(compressed);
        Assert.True(decoded.Width <= 2560);
        Assert.True(decoded.Height <= 2560);
    }

    [Fact]
    public void Returns_null_for_unparseable_input_instead_of_throwing()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5 };
        var result = ImageCompressor.TryCompressJpeg(garbage, 4L * 1024 * 1024);
        Assert.Null(result);
    }
}
