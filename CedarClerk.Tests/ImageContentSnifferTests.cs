using CedarClerk.Core;

namespace CedarClerk.Tests;

public class ImageContentSnifferTests
{
    [Fact]
    public void Detects_jpeg_from_magic_bytes()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
        Assert.Equal("image/jpeg", ImageContentSniffer.DetectContentType(bytes));
    }

    [Fact]
    public void Detects_png_from_magic_bytes()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
        Assert.Equal("image/png", ImageContentSniffer.DetectContentType(bytes));
    }

    [Fact]
    public void Detects_gif_from_magic_bytes()
    {
        byte[] bytes = "GIF89a"u8.ToArray();
        Assert.Equal("image/gif", ImageContentSniffer.DetectContentType(bytes));
    }

    [Fact]
    public void Detects_webp_from_magic_bytes()
    {
        byte[] bytes = [.."RIFF"u8.ToArray(), 0, 0, 0, 0, .."WEBP"u8.ToArray()];
        Assert.Equal("image/webp", ImageContentSniffer.DetectContentType(bytes));
    }

    [Fact]
    public void Renamed_non_image_file_is_not_detected_as_image()
    {
        // e.g. an .exe or script renamed to look like a .jpg — must not pass the whitelist
        byte[] bytes = "MZ\x90\x00"u8.ToArray();
        Assert.Null(ImageContentSniffer.DetectContentType(bytes));
    }

    [Fact]
    public void Empty_bytes_returns_null()
    {
        Assert.Null(ImageContentSniffer.DetectContentType(ReadOnlySpan<byte>.Empty));
    }
}
