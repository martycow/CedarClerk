using System.Text;

namespace CedarClerk.Core;

// RFC 4180 with a BOM: Excel reads a bare UTF-8 file as the local code page and turns a Cyrillic
// channel name into mojibake, while every other reader ignores the mark.
public static class StatSeriesCsv
{
    public const string Bom = "﻿";

    public static string Write(StatSeries series)
    {
        var sb = new StringBuilder(Bom);

        var header = new List<string> { "day" };
        var columns = new List<(int Series, string Metric)>();
        for (var s = 0; s < series.Series.Count; s++)
        {
            var item = series.Series[s];
            foreach (var metric in item.Tracked)
            {
                if (item.Values.GetValueOrDefault(metric) is null) continue;
                header.Add(Quote($"{item.Name} {metric}"));
                columns.Add((s, metric));
            }
        }
        sb.Append(string.Join(',', header)).Append("\r\n");

        for (var i = 0; i < series.Days.Count; i++)
        {
            sb.Append(series.Days[i].ToString("yyyy-MM-dd"));
            foreach (var (s, metric) in columns)
                sb.Append(',').Append(series.Series[s].Values[metric]![i]);
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
