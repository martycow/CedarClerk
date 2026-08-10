using System.Buffers.Binary;

namespace CedarClerk.Core;

/// <summary>
/// What a RIFF/WAVE header says about a sound file (T-140). Pure and header-only: it reads the
/// first few dozen bytes and never decodes a sample.
///
/// **WAV alone, deliberately.** MP3, OGG and FLAC each need a real parser (frame scanning, VBR
/// headers, metadata blocks), and a wrong duration is worse than none — an author who sees "2:14"
/// has no reason to doubt it. WAV is also what a game project's source audio actually is; the
/// compressed formats are usually exports. Everything else reports nothing, and the UI shows
/// nothing rather than a guess.
/// </summary>
public static class WavHeader
{
    public record Info(int Channels, int SampleRate, int BitsPerSample, int DurationMs);

    // "RIFF" + size + "WAVE" is 12 bytes; the smallest useful chunk header is another 8.
    private const int MinimumLength = 20;

    /// <summary>
    /// Reads the format and duration, or null when the bytes are not a WAV this can be sure about.
    /// Null is the honest answer for a truncated file, an unusual codec, or anything else.
    /// </summary>
    public static Info? TryRead(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < MinimumLength) return null;
        if (!bytes[..4].SequenceEqual("RIFF"u8)) return null;
        if (!bytes[8..12].SequenceEqual("WAVE"u8)) return null;

        int channels = 0, sampleRate = 0, bitsPerSample = 0, byteRate = 0;
        long dataBytes = -1;

        // Chunks run back to back after the 12-byte RIFF header: 4-byte id, 4-byte little-endian
        // size, then the payload padded to an even length.
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = bytes.Slice(offset, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            var payload = offset + 8;

            if (id.SequenceEqual("fmt "u8) && payload + 16 <= bytes.Length)
            {
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(payload + 2, 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(payload + 4, 4));
                byteRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(payload + 8, 4));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(payload + 14, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                // The size field is what matters, not the payload — which is exactly why only the
                // header has to be read for a file that may be hundreds of megabytes.
                dataBytes = size;
                break;
            }

            // Guard against a corrupt size that would loop forever or run backwards.
            if (size > int.MaxValue) return null;
            var next = payload + (long)size + (size % 2);
            if (next <= offset) return null;
            offset = (int)Math.Min(next, bytes.Length);
        }

        if (channels <= 0 || sampleRate <= 0 || byteRate <= 0 || dataBytes < 0) return null;

        var durationMs = (int)(dataBytes * 1000 / byteRate);
        return new Info(channels, sampleRate, bitsPerSample, durationMs);
    }

    /// <summary>m:ss, the way a sound file's length is written everywhere else.</summary>
    public static string FormatDuration(int durationMs)
    {
        var total = (int)Math.Round(durationMs / 1000.0);
        return $"{total / 60}:{total % 60:D2}";
    }
}
