using CedarClerk.Cli.Rendering;

namespace CedarClerk.Cli.Tests;

public class SparklineTests
{
    [Fact]
    public void Empty_series_draws_nothing()
    {
        Assert.Equal("", Sparkline.Render(Array.Empty<double>(), 10, GraphStyle.Braille));
        Assert.Equal("", Sparkline.Render(Array.Empty<double>(), 10, GraphStyle.Block));
        Assert.Equal("", Sparkline.Render(Array.Empty<double>(), 10, GraphStyle.Ascii));
    }

    [Fact]
    public void Zero_width_draws_nothing_rather_than_dividing_by_it()
    {
        Assert.Equal("", Sparkline.Render(new double[] { 1, 2, 3 }, 0, GraphStyle.Braille));
    }

    [Fact]
    public void All_zeroes_draw_a_baseline_not_an_empty_strip()
    {
        // The distinction this protects: "measured, and it was zero" against "no data". A server at
        // 0% and a server that answered nothing must not look the same.
        var braille = Sparkline.Render(new double[] { 0, 0, 0, 0 }, 2, GraphStyle.Braille);

        Assert.Equal(2, braille.Length);
        Assert.All(braille, character => Assert.NotEqual('⠀', character));
    }

    [Fact]
    public void A_single_value_renders_one_column()
    {
        Assert.Single(Sparkline.Render(new double[] { 42 }, 10, GraphStyle.Braille));
        Assert.Single(Sparkline.Render(new double[] { 42 }, 10, GraphStyle.Block));
    }

    [Fact]
    public void A_constant_series_renders_flat()
    {
        // The scale is zero-based, so a steady 7 reads as "7 out of 7" and fills the cell. What
        // matters is that it is flat and identical across the strip: a shape, not noise.
        var blocks = Sparkline.Render(new double[] { 7, 7, 7, 7 }, 4, GraphStyle.Block);

        Assert.Equal(4, blocks.Length);
        Assert.All(blocks, character => Assert.Equal(blocks[0], character));
    }

    [Fact]
    public void A_zero_width_range_does_not_divide_by_zero()
    {
        // min == max is the degenerate case the guard exists for; it must render, not throw.
        var blocks = Sparkline.Render(new double[] { 5, 5 }, 2, GraphStyle.Block, min: 5, max: 5);
        Assert.Equal(2, blocks.Length);
    }

    [Fact]
    public void A_rising_series_rises()
    {
        var blocks = Sparkline.Render(new double[] { 0, 25, 50, 75, 100 }, 5, GraphStyle.Block);

        for (var i = 1; i < blocks.Length; i++)
            Assert.True(blocks[i] >= blocks[i - 1], $"step {i} went down");
    }

    [Fact]
    public void NaN_is_a_gap_and_not_a_zero()
    {
        var blocks = Sparkline.Render(new[] { 10d, double.NaN, 10d }, 3, GraphStyle.Block);

        Assert.Equal(' ', blocks[1]);
        Assert.NotEqual(' ', blocks[0]);
    }

    [Fact]
    public void Infinity_is_treated_as_a_gap_too()
    {
        var blocks = Sparkline.Render(new[] { 1d, double.PositiveInfinity, 1d }, 3, GraphStyle.Block);
        Assert.Equal(' ', blocks[1]);
    }

    [Fact]
    public void Negative_values_are_included_in_the_scale_rather_than_clipped_away()
    {
        var blocks = Sparkline.Render(new double[] { -10, 0, 10 }, 3, GraphStyle.Block);

        Assert.Equal(3, blocks.Length);
        Assert.True(blocks[2] > blocks[0]);
    }

    [Fact]
    public void Braille_packs_two_samples_into_every_cell()
    {
        var text = Sparkline.Render(new double[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 40, GraphStyle.Braille);
        Assert.Equal(4, text.Length);
    }

    [Fact]
    public void Braille_output_stays_inside_the_braille_block()
    {
        var text = Sparkline.Render(new double[] { 3, 9, 1, 7, 5, 5, 2, 8 }, 4, GraphStyle.Braille);
        Assert.All(text, character => Assert.InRange(character, '⠀', '⣿'));
    }

    [Fact]
    public void Ascii_uses_only_characters_a_dumb_terminal_can_print()
    {
        var text = Sparkline.Render(new double[] { 0, 20, 40, 60, 80, 100 }, 6, GraphStyle.Ascii);
        Assert.All(text, character => Assert.InRange(character, ' ', '~'));
    }

    [Fact]
    public void More_samples_than_cells_are_averaged_rather_than_dropped()
    {
        // A spike that lands in a discarded slot would vanish from the graph that exists to show it.
        var values = new double[100];
        values[50] = 1000;

        var resampled = Sparkline.Resample(values, 10);

        Assert.Equal(10, resampled.Count);
        Assert.True(resampled.Sum() > 0, "the spike disappeared");
    }

    [Fact]
    public void Fewer_samples_than_cells_are_left_alone()
    {
        var values = new double[] { 1, 2, 3 };
        Assert.Same(values, Sparkline.Resample(values, 50));
    }

    [Fact]
    public void A_bucket_of_nothing_but_gaps_stays_a_gap()
    {
        var values = new[] { double.NaN, double.NaN, 5d, 5d };
        var resampled = Sparkline.Resample(values, 2);

        Assert.True(double.IsNaN(resampled[0]));
        Assert.Equal(5, resampled[1]);
    }
}
