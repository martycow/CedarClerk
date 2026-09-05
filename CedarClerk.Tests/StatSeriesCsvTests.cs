using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-243. A spreadsheet is the consumer, so the file has to survive Excel: the BOM, CRLF, and a
// channel called Dev, "Diary" quoted the RFC 4180 way. Column order is the series order, then
// each series' tracked metrics, and an untracked metric has no column at all.
public class StatSeriesCsvTests
{
    private static DateOnly D(int day) => new(2026, 9, day);

    private static StatSeries Sample()
    {
        var blog = new SourceReadings("blog", "Blog", [StatMetrics.ViewCount, StatMetrics.LikeCount],
            [new StatReading(D(1), new Dictionary<string, int> { [StatMetrics.ViewCount] = 120, [StatMetrics.LikeCount] = 3 }),
             new StatReading(D(3), new Dictionary<string, int> { [StatMetrics.ViewCount] = 131, [StatMetrics.LikeCount] = 4 })], []);
        var channel = new SourceReadings("channel:1", "Dev, \"Diary\"", [StatMetrics.MemberCount],
            [new StatReading(D(2), new Dictionary<string, int> { [StatMetrics.MemberCount] = 40 })], []);
        return StatSeriesAligner.Align([blog, channel], D(1), D(3));
    }

    [Fact]
    public void Starts_with_a_bom_and_a_header_of_day_then_tracked_columns()
    {
        var csv = StatSeriesCsv.Write(Sample());

        Assert.StartsWith(StatSeriesCsv.Bom, csv);
        var lines = csv[StatSeriesCsv.Bom.Length..].Split("\r\n");
        Assert.Equal("day,Blog viewCount,Blog likeCount,\"Dev, \"\"Diary\"\" memberCount\"", lines[0]);
    }

    [Fact]
    public void Rows_are_carried_forward_integers_per_day_with_crlf_endings()
    {
        var csv = StatSeriesCsv.Write(Sample());

        var lines = csv[StatSeriesCsv.Bom.Length..].Split("\r\n");
        Assert.Equal("2026-09-01,120,3,40", lines[1]);
        Assert.Equal("2026-09-02,120,3,40", lines[2]);
        Assert.Equal("2026-09-03,131,4,40", lines[3]);
        Assert.Equal("", lines[4]);
        Assert.DoesNotContain("\n", csv.Replace("\r\n", ""));
    }

    [Fact]
    public void An_empty_selection_is_the_header_row_only()
    {
        Assert.Equal(StatSeriesCsv.Bom + "day\r\n", StatSeriesCsv.Write(StatSeries.Empty));
    }
}
