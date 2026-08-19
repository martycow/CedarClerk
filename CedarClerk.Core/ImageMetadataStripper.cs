using System.Text;

namespace CedarClerk.Core;

// Removes EXIF/XMP/IPTC (GPS lives there) from uploaded images without re-encoding pixels (ADR-130).
// Anything it cannot parse comes back unchanged — a corrupted image would be worse than the leak.
public static class ImageMetadataStripper
{
    public static byte[] Strip(byte[] bytes, string? contentType) => contentType switch
    {
        "image/jpeg" => StripJpeg(bytes),
        "image/png" => StripPng(bytes),
        "image/webp" => StripWebp(bytes),
        _ => bytes,
    };

    private static byte[] StripJpeg(byte[] b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return b;

        var output = new MemoryStream(b.Length);
        output.Write(b, 0, 2);
        var i = 2;
        while (i + 2 <= b.Length)
        {
            if (b[i] != 0xFF) return b;
            var marker = b[i + 1];

            if (marker == 0xFF) // fill byte before a marker
            {
                i += 1;
                continue;
            }
            if (marker == 0xDA) // start of scan — entropy-coded data through EOI, verbatim
            {
                output.Write(b, i, b.Length - i);
                return output.ToArray();
            }
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9)) // standalone, no length word
            {
                output.Write(b, i, 2);
                i += 2;
                continue;
            }
            if (i + 4 > b.Length) return b;

            var len = (b[i + 2] << 8) | b[i + 3];
            if (len < 2 || i + 2 + len > b.Length) return b;

            // APP1 carries EXIF and XMP, APP13 carries IPTC; APP0/APP2/APP14 stay — decoders need them.
            var drop = marker is 0xE1 or 0xED or 0xFE;
            if (!drop) output.Write(b, i, 2 + len);
            i += 2 + len;
        }
        return b;
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] StripPng(byte[] b)
    {
        if (b.Length < 8 || !b.AsSpan(0, 8).SequenceEqual(PngSignature)) return b;

        var output = new MemoryStream(b.Length);
        output.Write(b, 0, 8);
        var i = 8;
        while (i + 12 <= b.Length)
        {
            var len = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
            if (len < 0 || i + 12 + len > b.Length) return b;

            var type = Encoding.ASCII.GetString(b, i + 4, 4);
            var drop = type is "eXIf" or "tEXt" or "zTXt" or "iTXt";
            if (!drop) output.Write(b, i, 12 + len);
            i += 12 + len;
            if (type == "IEND") return output.ToArray();
        }
        return b;
    }

    private static byte[] StripWebp(byte[] b)
    {
        if (b.Length < 12
            || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F'
            || b[8] != 'W' || b[9] != 'E' || b[10] != 'B' || b[11] != 'P') return b;

        var output = new MemoryStream(b.Length);
        output.Write(b, 0, 12);
        long vp8xFlagsAt = -1;
        var i = 12;
        while (i + 8 <= b.Length)
        {
            var fourCc = Encoding.ASCII.GetString(b, i, 4);
            var len = b[i + 4] | (b[i + 5] << 8) | (b[i + 6] << 16) | (b[i + 7] << 24);
            var padded = len + (len & 1); // RIFF chunks are word-aligned
            if (len < 0 || i + 8 + len > b.Length) return b;
            if (i + 8 + padded > b.Length) padded = len; // tolerate a missing final pad byte

            if (fourCc is "EXIF" or "XMP ")
            {
                i += 8 + padded;
                continue;
            }
            if (fourCc == "VP8X") vp8xFlagsAt = output.Position + 8;
            output.Write(b, i, 8 + padded);
            i += 8 + padded;
        }

        var result = output.ToArray();
        var riffSize = result.Length - 8;
        result[4] = (byte)riffSize;
        result[5] = (byte)(riffSize >> 8);
        result[6] = (byte)(riffSize >> 16);
        result[7] = (byte)(riffSize >> 24);
        if (vp8xFlagsAt >= 0 && vp8xFlagsAt < result.Length)
            result[vp8xFlagsAt] &= unchecked((byte)~0x0C); // EXIF and XMP presence flags
        return result;
    }
}
