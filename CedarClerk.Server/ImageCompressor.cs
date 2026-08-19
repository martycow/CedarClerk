using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace CedarClerk.Server;

// Produces a Telegram-safe JPEG derivative for large camera photos — Telegram rejects a photo
// fetched by URL above a certain size (see Consts.FileSizes.TelegramSafeImageBytes and the ADR
// in docs/DECISIONS.md). Resizes down first (camera-native resolution is far more than any chat
// app displays usefully), then reduces JPEG quality if still over budget after that.
public static class ImageCompressor
{
    private const int MaxLongEdge = 2560;
    private static readonly int[] QualitySteps = [85, 75, 65, 50];

    public static byte[]? TryCompressJpeg(byte[] original, long targetMaxBytes, ILogger? logger = null)
    {
        try
        {
            using var image = Image.Load(original);
            // ImageSharp copies source EXIF (incl. GPS) into the output by default — the derivative
            // is what actually reaches Telegram (ADR-130).
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;
            if (image.Width > MaxLongEdge || image.Height > MaxLongEdge)
            {
                var ratio = (double)MaxLongEdge / Math.Max(image.Width, image.Height);
                image.Mutate(x => x.Resize((int)(image.Width * ratio), (int)(image.Height * ratio)));
            }

            byte[] best = [];
            foreach (var quality in QualitySteps)
            {
                using var ms = new MemoryStream();
                image.Save(ms, new JpegEncoder { Quality = quality });
                best = ms.ToArray();
                if (best.Length <= targetMaxBytes)
                    break;
            }
            return best;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to compress image for Telegram");
            return null;
        }
    }
}
