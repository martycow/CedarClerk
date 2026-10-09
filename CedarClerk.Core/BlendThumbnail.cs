using System.Buffers.Binary;

namespace CedarClerk.Core;

// Pulls the RGBA preview Blender saves inside a .blend (T-140). Worth a parser because it is the
// most important file in a game project and the one no image library can open — Blender already
// wrote the thumbnail, it just has to be found. Read-only: walks block headers, copies out pixels.
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
        // Legacy header: "BLENDER" + pointer-size char + endianness char + 3 version chars.
        if (bytes.Length < 12 || !bytes[..7].SequenceEqual(Magic))
        {
            // Blender 3.0+ can save zstd-compressed (and older versions gzip-compressed) files. The
            // preview is in there, but reaching it means shipping a decompressor for a thumbnail —
            // so this says no rather than half-doing it.
            return null;
        }

        // Blender 5 writes "BLENDER17-01v0501": a two-digit header length, the format version, then
        // a four-digit release — and block headers with 64-bit lengths in a different field order.
        var large = bytes[7] is >= (byte)'0' and <= (byte)'9';
        int headerSize, offset, lengthAt;
        if (large)
        {
            if (bytes.Length < 17 || bytes[8] is < (byte)'0' or > (byte)'9' || bytes[9] != (byte)'-') return null;
            if (bytes[10] != (byte)'0' || bytes[11] != (byte)'1' || bytes[12] != (byte)'v') return null;
            offset = (bytes[7] - '0') * 10 + (bytes[8] - '0');
            if (offset < 17) return null;
            // 4-byte code, 4-byte SDNA index, 8-byte pointer, 8-byte length, 8-byte count.
            headerSize = 4 + 4 + 8 + 8 + 8;
            lengthAt = 16;
        }
        else
        {
            var pointerSize = bytes[7] switch { (byte)'_' => 4, (byte)'-' => 8, _ => 0 };
            if (pointerSize == 0) return null;

            // Every .blend written this century is little-endian; big-endian support would be untested
            // code guarding against a file nobody has.
            if (bytes[8] != (byte)'v') return null;

            // Block header: 4-byte code, 4-byte length, pointer, 4-byte SDNA index, 4-byte count.
            headerSize = 4 + 4 + pointerSize + 4 + 4;
            offset = 12;
            lengthAt = 4;
        }

        while (offset + headerSize <= bytes.Length)
        {
            var code = bytes.Slice(offset, 4);
            var length = large
                ? BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset + lengthAt, 8))
                : BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + lengthAt, 4));
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

            var next = data + length;
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
