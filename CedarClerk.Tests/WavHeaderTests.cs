using System.Buffers.Binary;
using System.Text;
using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-140. The duration this produces is shown to an author as fact ("2:14"), so the thing worth
// pinning is that it stays silent whenever it is not sure.
public class WavHeaderTests
{
    /// <summary>A minimal but real RIFF/WAVE header — fmt chunk, then a data chunk of the given size.</summary>
    private static byte[] Wav(int sampleRate, short channels, short bits, int dataBytes, bool withExtraChunk = false)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        var byteRate = sampleRate * channels * bits / 8;

        w.Write("RIFF"u8.ToArray());
        w.Write(0);                       // size; readers here never use it
        w.Write("WAVE"u8.ToArray());

        if (withExtraChunk)
        {
            // Real files carry LIST/INFO and similar before fmt — the reader has to walk past them.
            w.Write("LIST"u8.ToArray());
            w.Write(4);
            w.Write("INFO"u8.ToArray());
        }

        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);                // PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)(channels * bits / 8));
        w.Write(bits);

        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Reads_format_and_duration_from_the_header_alone()
    {
        // 48 kHz, stereo, 16-bit → 192000 bytes per second. 2:14 = 134s.
        var info = WavHeader.TryRead(Wav(48000, 2, 16, 134 * 192000));

        Assert.NotNull(info);
        Assert.Equal(48000, info!.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(16, info.BitsPerSample);
        Assert.Equal(134_000, info.DurationMs);
        Assert.Equal("2:14", WavHeader.FormatDuration(info.DurationMs));
    }

    [Fact]
    public void Walks_past_chunks_it_does_not_understand()
    {
        var info = WavHeader.TryRead(Wav(44100, 1, 16, 44100 * 2));
        Assert.Equal(1000, info!.DurationMs);

        var withList = WavHeader.TryRead(Wav(44100, 1, 16, 44100 * 2, withExtraChunk: true));
        Assert.Equal(1000, withList!.DurationMs);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void Too_short_is_null(byte[] bytes) => Assert.Null(WavHeader.TryRead(bytes));

    [Fact]
    public void Anything_that_is_not_a_wav_is_null()
    {
        // A PNG. The indexer offers this reader every file whose extension says audio, and an
        // extension is only a claim.
        var png = new byte[64];
        png[0] = 0x89; png[1] = (byte)'P'; png[2] = (byte)'N'; png[3] = (byte)'G';
        Assert.Null(WavHeader.TryRead(png));

        // RIFF, but AVI rather than WAVE.
        var avi = Wav(44100, 2, 16, 1000);
        "AVI "u8.CopyTo(avi.AsSpan(8));
        Assert.Null(WavHeader.TryRead(avi));
    }

    [Fact]
    public void A_header_with_no_data_chunk_is_null_rather_than_zero()
    {
        // Truncated right after fmt. Reporting 0:00 would be a claim about a file nobody measured.
        var truncated = Wav(44100, 2, 16, 1000)[..36];
        Assert.Null(WavHeader.TryRead(truncated));
    }

    [Fact]
    public void A_corrupt_chunk_size_does_not_hang()
    {
        // A size that runs past the buffer, or backwards, used to be a way to loop forever.
        var bytes = Wav(44100, 2, 16, 1000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), uint.MaxValue);
        Assert.Null(WavHeader.TryRead(bytes));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(999, "0:01")]
    [InlineData(61_000, "1:01")]
    [InlineData(3_600_000, "60:00")]
    public void Duration_reads_as_minutes_and_seconds(int ms, string expected) =>
        Assert.Equal(expected, WavHeader.FormatDuration(ms));
}
