using System.Text;
using CedarClerk.Core;

namespace CedarClerk.Tests;

public class ImageMetadataStripperTests
{
    private static byte[] JpegSegment(byte marker, byte[] payload)
    {
        var len = payload.Length + 2;
        return [0xFF, marker, (byte)(len >> 8), (byte)len, .. payload];
    }

    private static byte[] SampleJpeg(out byte[] scan)
    {
        scan = [0xFF, 0xDA, 0x00, 0x04, 0x01, 0x02, 0xAA, 0xBB, 0xFF, 0xD9];
        return
        [
            0xFF, 0xD8,
            .. JpegSegment(0xE0, Encoding.ASCII.GetBytes("JFIF\0")),
            .. JpegSegment(0xE1, Encoding.ASCII.GetBytes("Exif\0\0GPSDATA")),
            .. JpegSegment(0xE2, Encoding.ASCII.GetBytes("ICC_PROFILE\0")),
            .. JpegSegment(0xED, Encoding.ASCII.GetBytes("Photoshop 3.0")),
            .. JpegSegment(0xFE, Encoding.ASCII.GetBytes("a comment")),
            .. JpegSegment(0xDB, [0x00, 0x01]),
            .. scan,
        ];
    }

    [Fact]
    public void Jpeg_drops_exif_iptc_and_comment_segments()
    {
        var stripped = ImageMetadataStripper.Strip(SampleJpeg(out _), "image/jpeg");

        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(stripped));
        Assert.DoesNotContain("Photoshop", Encoding.ASCII.GetString(stripped));
        Assert.DoesNotContain("a comment", Encoding.ASCII.GetString(stripped));
    }

    [Fact]
    public void Jpeg_keeps_jfif_icc_tables_and_scan_data()
    {
        var stripped = ImageMetadataStripper.Strip(SampleJpeg(out var scan), "image/jpeg");

        var text = Encoding.ASCII.GetString(stripped);
        Assert.Contains("JFIF", text);
        Assert.Contains("ICC_PROFILE", text);
        Assert.Equal(0xDB, stripped[Find(stripped, [0xFF, 0xDB]) + 1]);
        Assert.EndsWith(Convert.ToHexString(scan), Convert.ToHexString(stripped));
        Assert.Equal([0xFF, 0xD8], stripped[..2]);
    }

    [Fact]
    public void Jpeg_with_fill_bytes_before_marker_still_parses()
    {
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, // fill byte
            .. JpegSegment(0xE1, Encoding.ASCII.GetBytes("Exif\0\0")),
            0xFF, 0xDA, 0x00, 0x02, 0x00, 0xFF, 0xD9,
        ];
        var stripped = ImageMetadataStripper.Strip(jpeg, "image/jpeg");
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(stripped));
    }

    [Fact]
    public void Truncated_jpeg_comes_back_unchanged()
    {
        byte[] broken = [0xFF, 0xD8, 0xFF, 0xE1, 0x40, 0x00, 0x01];
        Assert.Same(broken, ImageMetadataStripper.Strip(broken, "image/jpeg"));
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var len = data.Length;
        return
        [
            (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len,
            .. Encoding.ASCII.GetBytes(type), .. data, 0, 0, 0, 0, // stripper does not read the CRC
        ];
    }

    private static readonly byte[] PngSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Png_drops_exif_and_text_chunks_keeps_pixels()
    {
        byte[] png =
        [
            .. PngSig,
            .. PngChunk("IHDR", new byte[13]),
            .. PngChunk("eXIf", Encoding.ASCII.GetBytes("GPSDATA")),
            .. PngChunk("tEXt", Encoding.ASCII.GetBytes("Author\0me")),
            .. PngChunk("iTXt", Encoding.ASCII.GetBytes("XML:com.adobe.xmp\0\0\0\0\0xmp")),
            .. PngChunk("IDAT", [1, 2, 3]),
            .. PngChunk("IEND", []),
        ];
        var stripped = ImageMetadataStripper.Strip(png, "image/png");

        var text = Encoding.ASCII.GetString(stripped);
        Assert.DoesNotContain("eXIf", text);
        Assert.DoesNotContain("tEXt", text);
        Assert.DoesNotContain("iTXt", text);
        Assert.Contains("IHDR", text);
        Assert.Contains("IDAT", text);
        Assert.Contains("IEND", text);
    }

    [Fact]
    public void Png_with_bad_signature_comes_back_unchanged()
    {
        byte[] notPng = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        Assert.Same(notPng, ImageMetadataStripper.Strip(notPng, "image/png"));
    }

    private static byte[] WebpChunk(string fourCc, byte[] data)
    {
        var len = data.Length;
        byte[] chunk =
        [
            .. Encoding.ASCII.GetBytes(fourCc),
            (byte)len, (byte)(len >> 8), (byte)(len >> 16), (byte)(len >> 24),
            .. data,
        ];
        return (len & 1) == 1 ? [.. chunk, 0] : chunk;
    }

    [Fact]
    public void Webp_drops_exif_and_xmp_patches_riff_size_and_vp8x_flags()
    {
        byte[] vp8x = [0x0C, 0, 0, 0, 1, 0, 0, 1, 0, 0]; // EXIF|XMP flags set
        byte[] body =
        [
            .. WebpChunk("VP8X", vp8x),
            .. WebpChunk("EXIF", Encoding.ASCII.GetBytes("GPSDATA")),
            .. WebpChunk("XMP ", Encoding.ASCII.GetBytes("<xmp/>")),
            .. WebpChunk("VP8 ", [9, 9, 9, 9]),
        ];
        var size = body.Length + 4;
        byte[] webp =
        [
            .. Encoding.ASCII.GetBytes("RIFF"),
            (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24),
            .. Encoding.ASCII.GetBytes("WEBP"),
            .. body,
        ];

        var stripped = ImageMetadataStripper.Strip(webp, "image/webp");

        var text = Encoding.ASCII.GetString(stripped);
        Assert.DoesNotContain("EXIF", text);
        Assert.DoesNotContain("GPSDATA", text);
        Assert.DoesNotContain("XMP ", text);
        Assert.Contains("VP8X", text);
        Assert.Contains("VP8 ", text);

        var riffSize = stripped[4] | (stripped[5] << 8) | (stripped[6] << 16) | (stripped[7] << 24);
        Assert.Equal(stripped.Length - 8, riffSize);

        var flagsAt = Find(stripped, Encoding.ASCII.GetBytes("VP8X")) + 8;
        Assert.Equal(0, stripped[flagsAt] & 0x0C);
    }

    [Fact]
    public void Gif_and_unknown_types_pass_through()
    {
        byte[] gif = Encoding.ASCII.GetBytes("GIF89a...");
        Assert.Same(gif, ImageMetadataStripper.Strip(gif, "image/gif"));
        Assert.Same(gif, ImageMetadataStripper.Strip(gif, null));
    }

    private static int Find(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        return -1;
    }
}
