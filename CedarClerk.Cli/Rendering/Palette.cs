using Spectre.Console;

namespace CedarClerk.Cli.Rendering;

// The colours, in one place, borrowed from the app's own warm-editorial token set (docs/design/DESIGN.md)
// so the console and the product look like the same thing.
//
// Spectre downgrades truecolor to whatever the terminal reports, so these are written as RGB once
// and degrade on their own rather than being chosen twice.
public static class Palette
{
    public static readonly Color Accent = new(201, 162, 39);       // --accent, warm amber
    public static readonly Color AccentSoft = new(160, 130, 45);
    public static readonly Color Ok = new(90, 150, 90);
    public static readonly Color Warn = new(200, 150, 40);
    public static readonly Color Danger = new(190, 70, 60);
    public static readonly Color Muted = new(130, 124, 110);
    public static readonly Color Faint = new(90, 86, 78);
    public static readonly Color Text = new(225, 220, 208);

    public static readonly Color[] Cedar =
    {
        new(46, 82, 54),
        new(62, 104, 62),
        new(84, 128, 72),
        new(112, 152, 84),
        new(146, 176, 100)
    };

    public static readonly Color[] Series =
    {
        new(201, 162, 39),
        new(112, 152, 84),
        new(150, 120, 170),
        new(90, 140, 170),
        new(190, 110, 80),
        new(130, 124, 110)
    };

    // A usage colour is never shown without its number beside it (ADR-118 decision 7); the colour is
    // the glance, the number is the answer.
    public static Color ForLoad(double fraction) =>
        fraction >= 0.90 ? Danger : fraction >= 0.75 ? Warn : Ok;

    // Row 0 is the top of a chart and gets the full colour; lower rows fade towards the background.
    public static Color Shade(Color colour, int row, int rows)
    {
        if (rows <= 1) return colour;
        var factor = 1.0 - 0.45 * row / Math.Max(1, rows - 1);
        return new Color(
            (byte)Math.Clamp(colour.R * factor, 0, 255),
            (byte)Math.Clamp(colour.G * factor, 0, 255),
            (byte)Math.Clamp(colour.B * factor, 0, 255));
    }

    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new Color(
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));
    }

    public static string Hex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
}
