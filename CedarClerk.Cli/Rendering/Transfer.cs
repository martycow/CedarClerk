using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Rendering;

// An upload in progress: a bar, its numbers, and a braille chart of the throughput.
//
// The chart is not decoration. The failure this transfer path exists for is a connection that goes
// quiet and is then reset (ADR-113), and "quiet" has a shape — the line sags for several seconds
// before the drop, which an averaged number cannot show.
public sealed class Transfer
{
    // Two minutes of history at four samples a second, which is longer than any stall worth seeing
    // and short enough that the line does not become a smear.
    private const int MaxSamples = 480;
    private const double SampleSeconds = 0.25;

    private readonly List<double> _throughput = new();
    private long _lastBytes;
    private TimeSpan _lastAt = TimeSpan.Zero;

    public IReadOnlyList<double> Throughput => _throughput;

    // Instantaneous rate between two observations rather than the running average: the average is
    // what the summary line reports, and averaging twice would flatten out the very dip this is for.
    public void Observe(long sent, TimeSpan elapsed)
    {
        var since = elapsed - _lastAt;
        if (since.TotalSeconds < SampleSeconds) return;

        _throughput.Add((sent - _lastBytes) / since.TotalSeconds);
        if (_throughput.Count > MaxSamples) _throughput.RemoveAt(0);

        _lastBytes = sent;
        _lastAt = elapsed;
    }

    // Resuming continues one transfer, so the bar keeps counting from the byte already on the far
    // side; the clock and the rate start again, because they describe this attempt and not the last.
    public void Resumed(long offset, TimeSpan elapsed)
    {
        _lastBytes = offset;
        _lastAt = elapsed;
    }

    public IRenderable Render(Glyphs glyphs, long sent, long total, TimeSpan elapsed, int width)
    {
        var fraction = total <= 0 ? 0 : Math.Clamp((double)sent / total, 0, 1);
        var average = elapsed.TotalSeconds <= 0.05 ? 0 : sent / elapsed.TotalSeconds;
        var recent = _throughput.Count == 0 ? average : _throughput.TakeLast(4).Average();

        var eta = recent > 1024 && sent < total
            ? Format.Duration(TimeSpan.FromSeconds((total - sent) / recent))
            : "--";

        var rows = new List<IRenderable>
        {
            new Markup(
                $"{Bar(glyphs, fraction, 24)}  " +
                $"[{Palette.Hex(Palette.Text)}]{fraction * 100,4:0}%[/]  " +
                $"[grey]{Format.Size(sent)} / {Format.Size(total)}[/]  " +
                $"[{Palette.Hex(Palette.Accent)}]{Format.Size(recent)}/s[/]  " +
                $"[grey35]ETA {eta}[/]")
        };

        if (_throughput.Count >= 2)
            rows.Add(new BrailleChart
            {
                Values = _throughput,
                Rows = 3,
                Min = 0,
                Line = Palette.AccentSoft,
                Style = glyphs.GraphStyle,
                LeftLabel = "throughput",
                RightLabel = $"avg {Format.Size(average)}/s",
                Unit = ""
            });

        return new Rows(rows);
    }

    private static string Bar(Glyphs glyphs, double fraction, int width)
    {
        var filled = (int)Math.Round(width * Math.Clamp(fraction, 0, 1));
        return $"[{Palette.Hex(Palette.Accent)}]{Repeat(glyphs.Full, filled)}[/]" +
               $"[grey35]{Repeat(glyphs.Empty, width - filled)}[/]";
    }

    private static string Repeat(string glyph, int count) =>
        count <= 0 ? "" : string.Concat(Enumerable.Repeat(glyph, count));
}
