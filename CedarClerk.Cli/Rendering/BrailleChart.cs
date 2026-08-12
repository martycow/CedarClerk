using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Rendering;

// A real line chart, which Spectre.Console does not ship — so it is a Renderable of our own, with
// Render(RenderOptions, int) emitting Segments, exactly as its built-in widgets do. That is what
// lets it sit inside a Panel or a Layout and be measured like anything else.
//
// Vertical resolution is 4 dots per row, so a 4-row chart resolves 16 levels where a bar chart of
// the same height resolves 4. On a series that lives between 5% and 9% CPU — which is what this
// droplet actually does — that is the difference between a readable line and a flat strip.
public sealed class BrailleChart : Renderable
{
    private const char BrailleBase = '⠀';

    private static readonly int[,] Dots =
    {
        { 0x01, 0x08 },
        { 0x02, 0x10 },
        { 0x04, 0x20 },
        { 0x40, 0x80 }
    };

    public IReadOnlyList<double> Values { get; init; } = Array.Empty<double>();
    public int Rows { get; init; } = 4;
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string LeftLabel { get; init; } = "";
    public string RightLabel { get; init; } = "";
    public string Unit { get; init; } = "";
    public Color Line { get; init; } = Palette.Accent;
    public Color Axis { get; init; } = Palette.Muted;
    public GraphStyle Style { get; init; } = GraphStyle.Braille;

    protected override Measurement Measure(RenderOptions options, int maxWidth) =>
        new(20, maxWidth);

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var segments = new List<Segment>();
        var rows = Math.Max(1, Rows);

        var (low, high) = Bounds();
        var gutter = Math.Max(FormatTick(high).Length, FormatTick(low).Length) + 1;
        var width = Math.Max(4, maxWidth - gutter - 1);

        if (Values.Count == 0)
        {
            segments.Add(new Segment("no samples".PadRight(Math.Min(maxWidth, 10)), new Style(Palette.Muted)));
            segments.Add(Segment.LineBreak);
            return segments;
        }

        var grid = Plot(width, rows, low, high);

        for (var row = 0; row < rows; row++)
        {
            var label = row == 0 ? FormatTick(high) : row == rows - 1 ? FormatTick(low) : "";
            segments.Add(new Segment(label.PadLeft(gutter - 1) + " ", new Style(Axis)));

            // Brighter towards the top: the eye reads the peak of the line first, which is the part
            // that matters when the question is "did it spike".
            var shade = Palette.Shade(Line, row, rows);
            segments.Add(new Segment(new string(grid[row]), new Style(shade)));
            segments.Add(Segment.LineBreak);
        }

        if (LeftLabel.Length > 0 || RightLabel.Length > 0)
        {
            var room = Math.Max(0, width - LeftLabel.Length - RightLabel.Length);
            segments.Add(new Segment(new string(' ', gutter) + LeftLabel + new string(' ', room) + RightLabel,
                new Style(Axis)));
            segments.Add(Segment.LineBreak);
        }

        return segments;
    }

    private char[][] Plot(int width, int rows, double low, double high)
    {
        var perCell = Style == GraphStyle.Braille ? 2 : 1;
        var levels = Style == GraphStyle.Braille ? rows * 4 : rows;
        var series = Sparkline.Resample(Values, width * perCell);

        var masks = new int[rows, width];

        for (var i = 0; i < series.Count; i++)
        {
            var value = series[i];
            if (double.IsNaN(value) || double.IsInfinity(value)) continue;

            var cell = i / perCell;
            var column = perCell == 2 ? i % 2 : 0;
            if (cell >= width) break;

            var span = high - low;
            var fraction = span <= 0 ? 0 : (value - low) / span;
            var height = Math.Clamp((int)Math.Ceiling(Math.Clamp(fraction, 0, 1) * levels), 1, levels);

            for (var filled = 0; filled < height; filled++)
            {
                var fromTop = levels - 1 - filled;
                if (Style == GraphStyle.Braille)
                    masks[fromTop / 4, cell] |= Dots[fromTop % 4, column];
                else
                    masks[fromTop, cell] = 1;
            }
        }

        var grid = new char[rows][];
        for (var row = 0; row < rows; row++)
        {
            grid[row] = new char[width];
            for (var cell = 0; cell < width; cell++)
            {
                grid[row][cell] = Style switch
                {
                    GraphStyle.Braille => (char)(BrailleBase | masks[row, cell]),
                    GraphStyle.Block => masks[row, cell] == 0 ? ' ' : '█',
                    _ => masks[row, cell] == 0 ? ' ' : '#'
                };
            }
        }
        return grid;
    }

    private (double Low, double High) Bounds()
    {
        var real = Values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();
        if (real.Length == 0) return (Min ?? 0, Max ?? 1);
        var low = Min ?? Math.Min(0, real.Min());
        // A little headroom when the top is not fixed, so a steady series sits below the ceiling
        // instead of filling the panel solid. The top tick prints the scale, so nothing is implied
        // that the axis does not state.
        var high = Max ?? real.Max() * 1.15;
        if (high <= low) high = low + 1;
        return (low, high);
    }

    private string FormatTick(double value) =>
        value >= 100 ? $"{value:0}{Unit}" : $"{value:0.#}{Unit}";
}
