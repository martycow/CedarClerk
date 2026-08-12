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
/// What a file's own header said. Null fields mean "not read" or "this kind does not say" — an image
/// has no duration, and only WAV reports one at all.
/// </summary>
public record AssetHeader(int? Width, int? Height, int? DurationMs, int? SampleRate);

/// <summary>
/// Reads what a file's own header says, and renders thumbnails (T-140).
///
/// Both halves are header-or-nothing by design: the index can hold a hundred thousand files, and a
/// scan that fully decoded each one would take minutes for information almost none of them will be
/// asked about.
///
/// **Called only in agent mode since ADR-117.** The hosted server never sees an indexed file's bytes,
/// so it neither reads headers nor renders previews — it stores what the agent sent. The two members
/// production still uses are <see cref="CanHaveThumbnail"/> and <see cref="ThumbnailPaths"/>.
/// </summary>
public static class AssetMetadata
{
    /// <summary>The long edge of a generated thumbnail. A grid tile is ~180px wide; this covers 2×.</summary>
    private const int ThumbnailLongEdge = 360;
    private const int ThumbnailQuality = 78;

    /// <summary>Enough for a WAV's RIFF header and its fmt/data chunk descriptors.</summary>
    private const int WavHeaderBytes = 4096;

    /// <summary>
    /// Width/height for an image, duration/sample-rate for a WAV. Null when the file's header said
    /// nothing — which a caller stamps anyway, or it would reopen that file on every scan forever.
    ///
    /// Never throws: an unreadable or malformed file is a normal thing to meet in a folder of tens
    /// of thousands, and one of them must not end a scan.
    /// </summary>
    public static AssetHeader? TryRead(string fullPath, string kind)
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
                    return new AssetHeader(info.Width, info.Height, null, null);

                case AssetKinds.Model:
                    // A .blend carries its own preview, and its size is the one real measurement
                    // available without opening Blender.
                    if (!AssetKinds.HasEmbeddedPreview(fullPath)) return null;
                    if (ReadBlendPreview(fullPath) is not { } preview) return null;
                    return new AssetHeader(preview.Width, preview.Height, null, null);

                case AssetKinds.Audio:
                    if (!fullPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return null;
                    using (var stream = File.OpenRead(fullPath))
                    {
                        Span<byte> buffer = stackalloc byte[WavHeaderBytes];
                        var read = stream.ReadAtLeast(buffer, WavHeaderBytes, throwOnEndOfStream: false);
                        if (WavHeader.TryRead(buffer[..read]) is not { } wav) return null;
                        return new AssetHeader(null, null, wav.DurationMs, wav.SampleRate);
                    }

                default:
                    return null;
            }
        }
        catch (Exception)
        {
            // A .png that is really a text file, a file being written to right now, a permission
            // that changed since the walk. None of it is worth a log line per file.
            return null;
        }
    }

    /// <summary>
    /// Renders a JPEG thumbnail into memory, or null when this file cannot have one.
    ///
    /// **In memory rather than to a path since ADR-117**: the agent hands the bytes back over the
    /// loopback and never writes anything — the machine being scanned is someone's game project,
    /// usually under version control, and the agent's whole contract is that it only reads.
    /// The bytes are small by construction (a 360px JPEG at quality 78), so holding one is cheaper
    /// than the temp file it replaces.
    /// </summary>
    public static byte[]? TryRenderThumbnail(string fullPath, ILogger? logger = null)
    {
        try
        {
            using var image = AssetKinds.HasEmbeddedPreview(fullPath)
                ? LoadBlendPreview(fullPath)
                : Image.Load(fullPath);
            if (image is null) return null;
            var longEdge = Math.Max(image.Width, image.Height);
            if (longEdge > ThumbnailLongEdge)
            {
                var ratio = (double)ThumbnailLongEdge / longEdge;
                image.Mutate(x => x.Resize((int)(image.Width * ratio), (int)(image.Height * ratio)));
            }

            using var output = new MemoryStream();
            image.SaveAsJpeg(output, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = ThumbnailQuality });
            return output.ToArray();
        }
        catch (Exception e)
        {
            logger?.LogDebug(e, "No thumbnail for {Path}", fullPath);
            return null;
        }
    }

    /// <summary>
    /// Stores a thumbnail the agent uploaded. Written to a temporary name and moved into place: a
    /// grid asks for a whole screenful of thumbnails at once, and a half-written JPEG served to one
    /// of those requests would be a broken image forever after.
    /// </summary>
    public static void SaveThumbnail(string destination, byte[] jpeg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp";
        File.WriteAllBytes(temporary, jpeg);
        File.Move(temporary, destination, overwrite: true);
    }

    /// <summary>
    /// Whether these bytes really are a JPEG. Checked because the upload endpoint would otherwise be
    /// a way to store arbitrary bytes on the server under a name the grid then serves as an image.
    /// </summary>
    public static bool LooksLikeJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[^2] == 0xFF && bytes[^1] == 0xD9;

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
