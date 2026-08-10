using CedarClerk.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CedarClerk.Server.Modules.IndieDev;

/// <summary>
/// Where a project's generated thumbnails live (T-140): under <c>CEDAR_DATA_DIR</c>, beside the
/// database — **never** beside the source file. Cedar Clerk does not write into the folder it is
/// indexing: that folder is someone's game project, usually under version control, and dropping
/// files into it would be both a surprise and a diff.
/// </summary>
public record ThumbnailPaths(string Dir)
{
    public string For(Guid assetId) => Path.Combine(Dir, assetId.ToString("N") + ".jpg");
}

/// <summary>
/// Reads what a file's own header says, and makes thumbnails on request (T-140).
///
/// Both halves are header-or-nothing by design: the index can hold a hundred thousand files, and a
/// scan that fully decoded each one would take minutes for information almost none of them will be
/// asked about.
/// </summary>
public static class AssetMetadata
{
    /// <summary>The long edge of a generated thumbnail. A grid tile is ~180px wide; this covers 2×.</summary>
    private const int ThumbnailLongEdge = 360;
    private const int ThumbnailQuality = 78;

    /// <summary>Enough for a WAV's RIFF header and its fmt/data chunk descriptors.</summary>
    private const int WavHeaderBytes = 4096;

    /// <summary>
    /// Fills in width/height for an image and duration/sample-rate for a WAV. Returns false when it
    /// learned nothing — a caller uses that only to avoid a pointless write.
    ///
    /// Never throws: an unreadable or malformed file is a normal thing to meet in a folder of tens
    /// of thousands, and one of them must not end a scan.
    /// </summary>
    public static bool TryRead(string fullPath, string kind, AssetEntry into)
    {
        try
        {
            switch (kind)
            {
                case AssetKinds.Image:
                    // Identify reads the header only — it does not decode pixels, which is what
                    // makes this affordable during a scan. It throws for formats it does not know
                    // (PSD, EXR, Aseprite), and the catch below turns that into "no dimensions"
                    // rather than into a failed scan.
                    var info = Image.Identify(fullPath);
                    into.Width = info.Width;
                    into.Height = info.Height;
                    return true;

                case AssetKinds.Model:
                    // A .blend carries its own preview, and its size is the one real measurement
                    // available without opening Blender.
                    if (!AssetKinds.HasEmbeddedPreview(fullPath)) return false;
                    if (ReadBlendPreview(fullPath) is not { } preview) return false;
                    into.Width = preview.Width;
                    into.Height = preview.Height;
                    return true;

                case AssetKinds.Audio:
                    if (!fullPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return false;
                    using (var stream = File.OpenRead(fullPath))
                    {
                        Span<byte> buffer = stackalloc byte[WavHeaderBytes];
                        var read = stream.ReadAtLeast(buffer, WavHeaderBytes, throwOnEndOfStream: false);
                        if (WavHeader.TryRead(buffer[..read]) is not { } wav) return false;
                        into.DurationMs = wav.DurationMs;
                        into.SampleRate = wav.SampleRate;
                        return true;
                    }

                default:
                    return false;
            }
        }
        catch (Exception)
        {
            // A .png that is really a text file, a file being written to right now, a permission
            // that changed since the walk. None of it is worth a log line per file.
            return false;
        }
    }

    /// <summary>
    /// Produces a JPEG thumbnail, or null when this file cannot have one. Called on demand from the
    /// thumbnail endpoint, never from the scan.
    /// </summary>
    public static bool TryWriteThumbnail(string fullPath, string destination, ILogger? logger = null)
    {
        try
        {
            using var image = AssetKinds.HasEmbeddedPreview(fullPath)
                ? LoadBlendPreview(fullPath)
                : Image.Load(fullPath);
            if (image is null) return false;
            var longEdge = Math.Max(image.Width, image.Height);
            if (longEdge > ThumbnailLongEdge)
            {
                var ratio = (double)ThumbnailLongEdge / longEdge;
                image.Mutate(x => x.Resize((int)(image.Width * ratio), (int)(image.Height * ratio)));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // Written to a temporary name and moved into place: two requests for the same missing
            // thumbnail arrive together often (a grid asks for a whole screenful at once), and a
            // half-written JPEG served to the second one would be a broken image forever after.
            var temporary = destination + ".tmp";
            using (var output = File.Create(temporary))
                image.SaveAsJpeg(output, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = ThumbnailQuality });
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            logger?.LogDebug(e, "No thumbnail for {Path}", fullPath);
            return false;
        }
    }

    /// <summary>
    /// Whether this exact file can have a thumbnail. Per file rather than per kind, because "image"
    /// covers both a PNG and a Photoshop document — one decodes here and the other does not — and
    /// because a .blend is a model that nonetheless has a picture inside it.
    /// </summary>
    public static bool CanHaveThumbnail(string path) => AssetKinds.CanPreview(path);

    /// <summary>
    /// Blender's own preview, as an image. Only the front of the file is read: the preview block
    /// sits near the start, and a scene file can be hundreds of megabytes.
    /// </summary>
    private static Image? LoadBlendPreview(string fullPath)
    {
        if (ReadBlendPreview(fullPath) is not { } preview) return null;
        // Blender stores the rows bottom-up, the way OpenGL hands them over. Flipped here, or every
        // 3D thumbnail in the grid would be upside down.
        var image = Image.LoadPixelData<Rgba32>(preview.Rgba, preview.Width, preview.Height);
        image.Mutate(x => x.Flip(FlipMode.Vertical));
        return image;
    }

    private static BlendThumbnail.Preview? ReadBlendPreview(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var window = (int)Math.Min(BlendThumbnail.ReadWindowBytes, stream.Length);
            var buffer = new byte[window];
            var read = stream.ReadAtLeast(buffer, window, throwOnEndOfStream: false);
            return BlendThumbnail.TryRead(buffer.AsSpan(0, read));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
