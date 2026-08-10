using System.Buffers.Binary;

namespace CedarClerk.Core;

/// <summary>
/// Pulls the preview image Blender saves inside a <c>.blend</c> file (T-140, Marty 10.08.2026).
///
/// Why this is worth a parser: a `.blend` is the single most important file in a lot of game
/// projects, and it is the one file no image library can open. Blender itself writes a small RGBA
/// preview into the file (Preferences → Save &amp; Load → Save Preview Images, on by default), so
/// the thumbnail is already there — it just has to be found.
///
/// Pure and read-only: it walks block headers and copies one array of pixels out. It does not
/// understand a `.blend` beyond that, and does not try to.
/// </summary>
public static class BlendThumbnail
{
    public record Preview(int Width, int Height, byte[] Rgba);

    private static ReadOnlySpan<byte> Magic => "BLENDER"u8;
    private static ReadOnlySpan<byte> TestBlock => "TEST"u8;

    /// <summary>A preview larger than this is not one — it is a misread length.</summary>
    private const int MaxDimension = 4096;

    /// <summary>
    /// Returns the embedded preview, or null when there isn't one this can read: a compressed file,
    /// a file saved without previews, a truncated download, or anything that is not a .blend.
    /// Null is a normal answer, and the caller shows "no preview" for it.
    /// </summary>
    public static Preview? TryRead(ReadOnlySpan<byte> bytes)
    {
        // Header: "BLENDER" + pointer-size char + endianness char + 3 version chars.
        if (bytes.Length < 12 || !bytes[..7].SequenceEqual(Magic))
        {
            // Blender 3.0+ can save zstd-compressed (and older versions gzip-compressed) files. The
            // preview is in there, but reaching it means shipping a decompressor for a thumbnail —
            // so this says no rather than half-doing it.
            return null;
        }

        var pointerSize = bytes[7] switch { (byte)'_' => 4, (byte)'-' => 8, _ => 0 };
        if (pointerSize == 0) return null;

        var littleEndian = bytes[8] == (byte)'v';
        // Every .blend written this century is little-endian; big-endian support would be untested
        // code guarding against a file nobody has.
        if (!littleEndian) return null;

        // Block header: 4-byte code, 4-byte length, pointer, 4-byte SDNA index, 4-byte count.
        var headerSize = 4 + 4 + pointerSize + 4 + 4;
        var offset = 12;

        while (offset + headerSize <= bytes.Length)
        {
            var code = bytes.Slice(offset, 4);
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 4, 4));
            if (length < 0) return null;

            var data = offset + headerSize;

            if (code.SequenceEqual(TestBlock))
            {
                if (data + 8 > bytes.Length) return null;
                var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(data, 4));
                var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(data + 4, 4));
                if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension) return null;

                var pixels = (long)width * height * 4;
                if (data + 8 + pixels > bytes.Length) return null;

                return new Preview(width, height, bytes.Slice(data + 8, (int)pixels).ToArray());
            }

            // "ENDB" closes the file; anything after it is not a block.
            if (code.SequenceEqual("ENDB"u8)) return null;

            var next = (long)data + length;
            if (next <= offset) return null;   // a zero or negative stride would spin here forever
            offset = (int)Math.Min(next, bytes.Length);
        }

        return null;
    }

    /// <summary>
    /// How much of the file has to be read to find the preview. Blender writes the TEST block near
    /// the front, so a fixed window beats reading a 300 MB scene into memory; a file whose preview
    /// sits further in simply reports none.
    /// </summary>
    public const int ReadWindowBytes = 1024 * 1024;
}
