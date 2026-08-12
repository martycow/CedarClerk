namespace CedarClerk.Cli.Rendering;

// A series drawn into one line of text, at three resolutions.
//
// Braille packs 2x4 subpixels into every cell (base U+2800), which is eight times the vertical
// detail of the block characters and four times the horizontal — on a 40-column strip that is the
// difference between a shape and a staircase. Blocks are the fallback for terminals whose font has
// no braille coverage, and the ASCII ramp is for --no-unicode.
//
// Two deliberate choices, both visible in the tests:
//   * A finite sample never renders as nothing. Zero draws the bottom row, not an empty cell, so a
//     flat-zero series reads as "measured, and it was zero" rather than "no data" — the two mean
//     very different things about a server and must not look identical.
//   * NaN is a gap, drawn as blank. sar leaves holes when the machine was off, and interpolating
//     across one would invent history.
public static class Sparkline
{
    private const char BrailleBase = '⠀';
    private static readonly char[] Blocks = "▁▂▃▄▅▆▇█".ToCharArray();
    private static readonly char[] Ascii = ".:-=+*#".ToCharArray();

    // [row, column] as in the braille dot layout: two columns of four dots per cell.
    private static readonly int[,] Dots =
    {
        { 0x01, 0x08 },
        { 0x02, 0x10 },
        { 0x04, 0x20 },
        { 0x40, 0x80 }
    };

    public static string Render(
        IReadOnlyList<double> values, int width, GraphStyle style, double? min = null, double? max = null)
    {
        if (values is null || values.Count == 0 || width <= 0) return "";

        var samplesPerCell = style == GraphStyle.Braille ? 2 : 1;
        var series = Resample(values, width * samplesPerCell);
        var (low, high) = Range(series, min, max);

        return style switch
        {
            GraphStyle.Braille => RenderBraille(series, low, high),
            GraphStyle.Block => RenderRamp(series, low, high, Blocks),
            _ => RenderRamp(series, low, high, Ascii)
        };
    }

    private static string RenderBraille(IReadOnlyList<double> series, double low, double high)
    {
        var cells = (series.Count + 1) / 2;
        var text = new char[cells];

        for (var cell = 0; cell < cells; cell++)
        {
            var mask = 0;
            for (var column = 0; column < 2; column++)
            {
                var index = cell * 2 + column;
                if (index >= series.Count) continue;

                var height = Height(series[index], low, high, 4);
                for (var filled = 0; filled < height; filled++)
                    mask |= Dots[3 - filled, column];
            }
            text[cell] = (char)(BrailleBase | mask);
        }

        return new string(text);
    }

    private static string RenderRamp(IReadOnlyList<double> series, double low, double high, char[] ramp)
    {
        var text = new char[series.Count];
        for (var i = 0; i < series.Count; i++)
        {
            if (double.IsNaN(series[i])) { text[i] = ' '; continue; }
            var height = Height(series[i], low, high, ramp.Length);
            text[i] = height == 0 ? ' ' : ramp[height - 1];
        }
        return new string(text);
    }

    // 0 only for a gap; any real value gets at least the bottom step.
    private static int Height(double value, double low, double high, int steps)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        var span = high - low;
        var fraction = span <= 0 ? 0 : (value - low) / span;
        var height = (int)Math.Ceiling(Math.Clamp(fraction, 0, 1) * steps);
        return Math.Clamp(height, 1, steps);
    }

    private static (double Low, double High) Range(IReadOnlyList<double> series, double? min, double? max)
    {
        var real = series.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();
        if (real.Length == 0) return (0, 1);

        var low = min ?? Math.Min(0, real.Min());
        var high = max ?? real.Max();
        // A constant series has no span to scale against; giving it one keeps it on the baseline
        // instead of dividing by zero and jumping to the top.
        if (high <= low) high = low + 1;
        return (low, high);
    }

    // Averaging into buckets rather than dropping samples: a spike that lands in a discarded slot
    // would vanish from a graph whose whole job is to show spikes. NaN is contagious within its
    // bucket only if the bucket holds nothing else.
    internal static IReadOnlyList<double> Resample(IReadOnlyList<double> values, int target)
    {
        if (target <= 0) return Array.Empty<double>();
        if (values.Count <= target) return values;

        var output = new double[target];
        for (var i = 0; i < target; i++)
        {
            var start = (int)((long)i * values.Count / target);
            var end = (int)((long)(i + 1) * values.Count / target);
            if (end <= start) end = start + 1;

            double sum = 0;
            var counted = 0;
            for (var j = start; j < end && j < values.Count; j++)
            {
                if (double.IsNaN(values[j]) || double.IsInfinity(values[j])) continue;
                sum += values[j];
                counted++;
            }
            output[i] = counted == 0 ? double.NaN : sum / counted;
        }
        return output;
    }
}
