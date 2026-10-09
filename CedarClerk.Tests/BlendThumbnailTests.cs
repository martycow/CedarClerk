using System.Text;
using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-140 (Marty, 10.08.2026 — ".blend has to work"). A .blend is the file a game project is actually
// built in and the one no image library opens; Blender writes a preview inside it, so the thumbnail
// is a parsing problem rather than a rendering one. What is pinned here is that the parser reads a
// real layout and refuses everything it is not sure about — a wrong answer here is a wrong picture
// next to somebody's model.
public class BlendThumbnailTests
{
    /// <summary>
    /// A minimal but structurally real .blend: the 12-byte header, an unrelated block to walk past,
    /// then the TEST block holding width, height and RGBA pixels.
    /// </summary>
    private static byte[] Blend(int width, int height, char pointerSize = '_', char endian = 'v',
        bool withTest = true, bool endBlockFirst = false)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("BLENDER"u8.ToArray());
        w.Write((byte)pointerSize);
        w.Write((byte)endian);
        w.Write("403"u8.ToArray());   // version

        var pointerBytes = pointerSize == '-' ? 8 : 4;

        void Block(string code, byte[] data)
        {
            w.Write(Encoding.ASCII.GetBytes(code));
            w.Write(data.Length);
            w.Write(new byte[pointerBytes]);
            w.Write(0);   // SDNA index
            w.Write(1);   // count
            w.Write(data);
        }

        if (endBlockFirst) Block("ENDB", []);

        // Something else first, so the walk has to step over a block to reach the preview.
        Block("REND", new byte[24]);

        if (withTest)
        {
            using var payload = new MemoryStream();
            using var pw = new BinaryWriter(payload);
            pw.Write(width);
            pw.Write(height);
            pw.Write(new byte[width * height * 4]);
            pw.Flush();
            Block("TEST", payload.ToArray());
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>The Blender 5 layout: a 17-byte header and block headers with 64-bit lengths.</summary>
    private static byte[] Blend5(int width, int height, string header = "BLENDER17-01v0501")
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write(Encoding.ASCII.GetBytes(header));

        void Block(string code, byte[] data)
        {
            w.Write(Encoding.ASCII.GetBytes(code));
            w.Write(0);                 // SDNA index
            w.Write(0L);                // old pointer
            w.Write((long)data.Length);
            w.Write(1L);                // count
            w.Write(data);
        }

        Block("REND", new byte[264]);
        using var payload = new MemoryStream();
        using var pw = new BinaryWriter(payload);
        pw.Write(width);
        pw.Write(height);
        pw.Write(new byte[width * height * 4]);
        pw.Flush();
        Block("TEST", payload.ToArray());

        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Reads_the_preview_from_a_Blender_5_file()
    {
        var preview = BlendThumbnail.TryRead(Blend5(128, 50));

        Assert.NotNull(preview);
        Assert.Equal((128, 50, 128 * 50 * 4), (preview!.Width, preview.Height, preview.Rgba.Length));
    }

    [Fact]
    public void Refuses_a_file_format_version_it_has_never_seen()
    {
        Assert.Null(BlendThumbnail.TryRead(Blend5(128, 50, "BLENDER17-02v0600")));
        Assert.Null(BlendThumbnail.TryRead("BLENDER17-01v05"u8));
    }

    [Fact]
    public void Reads_the_preview_Blender_saved_inside_the_file()
    {
        var preview = BlendThumbnail.TryRead(Blend(128, 96));

        Assert.NotNull(preview);
        Assert.Equal(128, preview!.Width);
        Assert.Equal(96, preview.Height);
        Assert.Equal(128 * 96 * 4, preview.Rgba.Length);
    }

    [Fact]
    public void Works_for_64_bit_files_too()
    {
        // '-' means 8-byte pointers, which changes the block header's size. Getting this wrong
        // would misread every block after the first on a 64-bit save — i.e. on every modern file.
        var preview = BlendThumbnail.TryRead(Blend(64, 64, pointerSize: '-'));
        Assert.Equal(64, preview!.Width);
    }

    [Fact]
    public void A_file_saved_without_previews_reports_none()
    {
        Assert.Null(BlendThumbnail.TryRead(Blend(64, 64, withTest: false)));
    }

    [Fact]
    public void Stops_at_the_end_marker_instead_of_reading_past_it()
    {
        Assert.Null(BlendThumbnail.TryRead(Blend(64, 64, endBlockFirst: true)));
    }

    [Fact]
    public void A_compressed_blend_reports_none_rather_than_nonsense()
    {
        // Blender 3.0+ can save zstd-compressed; older ones gzip. The preview is in there, but
        // reaching it means shipping a decompressor — so this must say no, not guess.
        Assert.Null(BlendThumbnail.TryRead(new byte[] { 0x28, 0xB5, 0x2F, 0xFD, 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.Null(BlendThumbnail.TryRead(new byte[] { 0x1F, 0x8B, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void Anything_too_short_is_none(byte[] bytes) => Assert.Null(BlendThumbnail.TryRead(bytes));

    [Fact]
    public void A_truncated_file_does_not_read_past_its_end()
    {
        // The header promises more pixels than the file holds — the classic way a parser reads
        // somebody else's memory.
        var bytes = Blend(128, 128);
        Assert.Null(BlendThumbnail.TryRead(bytes.AsSpan(0, bytes.Length / 2)));
    }

    [Fact]
    public void An_absurd_preview_size_is_refused()
    {
        var bytes = Blend(2, 2);
        // Rewrite the recorded width to something no preview is, the way a misread offset would.
        var testAt = bytes.AsSpan().IndexOf("TEST"u8);
        Assert.True(testAt > 0);
        var widthAt = testAt + 4 + 4 + 4 + 4 + 4;   // code, len, pointer(4), sdna, count
        BitConverter.GetBytes(1_000_000).CopyTo(bytes, widthAt);
        Assert.Null(BlendThumbnail.TryRead(bytes));
    }

    [Fact]
    public void Blend_files_are_models_that_can_still_be_previewed()
    {
        Assert.Equal(AssetKinds.Model, AssetKinds.FromPath("scenes/ferry_terminal.blend"));
        // Blender's own rolling backup — the same file, and worth indexing for the same reason.
        Assert.Equal(AssetKinds.Model, AssetKinds.FromPath("scenes/ferry_terminal.blend1"));
        Assert.True(AssetKinds.CanPreview("scenes/ferry_terminal.blend"));
        Assert.True(AssetKinds.HasEmbeddedPreview("a.blend1"));
        // An .fbx is a model with nothing to show, and must not be asked for a thumbnail.
        Assert.False(AssetKinds.CanPreview("props/kiosk.fbx"));
    }
}
