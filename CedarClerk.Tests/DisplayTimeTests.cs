using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Server;

namespace CedarClerk.Tests;

/// <summary>
/// ADR-115 — the server keeps UTC, the page shows Pacific. The cases that matter are the two sides
/// of a daylight-saving change (a fixed -8 would be an hour wrong for most of the year, which is
/// the reason a named zone was chosen) and the kind-less values SQLite hands back.
/// </summary>
public class DisplayTimeTests
{
    [Fact]
    public void Summer_instant_is_seven_hours_back_and_named_PDT()
    {
        var utc = new DateTime(2026, 8, 11, 21, 5, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 8, 11, 14, 5, 0), DisplayTime.ToZone(utc));
        Assert.Equal("PDT", DisplayTime.Abbreviation(utc));
    }

    [Fact]
    public void Winter_instant_is_eight_hours_back_and_named_PST()
    {
        var utc = new DateTime(2026, 1, 15, 21, 5, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 1, 15, 13, 5, 0), DisplayTime.ToZone(utc));
        Assert.Equal("PST", DisplayTime.Abbreviation(utc));
    }

    [Fact]
    public void Unspecified_kind_is_read_as_UTC_because_that_is_what_SQLite_returns()
    {
        var fromDatabase = new DateTime(2026, 8, 11, 21, 5, 0, DateTimeKind.Unspecified);

        Assert.Equal(DisplayTime.ToZone(DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc)),
                     DisplayTime.ToZone(fromDatabase));
    }

    [Fact]
    public void A_post_published_just_after_midnight_UTC_belongs_to_the_previous_day_here()
    {
        // The case that would put a post in September's group under an August date.
        var utc = new DateTime(2026, 9, 1, 0, 30, 0, DateTimeKind.Utc);
        var local = DisplayTime.ToZone(utc);

        Assert.Equal(8, local.Month);
        Assert.Equal(31, local.Day);
        Assert.Equal("2026-08", local.ToString("yyyy-MM"));
    }

    [Fact]
    public void Blog_date_and_time_carries_the_zone_name()
    {
        var utc = new DateTime(2026, 8, 11, 21, 5, 0, DateTimeKind.Utc);

        Assert.Equal("11 августа 2026, 14:05 PDT", BlogDateFormatter.DateTimeLocal(utc, "ru"));
        Assert.Equal("11 August 2026, 14:05 PDT", BlogDateFormatter.DateTimeLocal(utc, "en"));
    }

    [Fact]
    public void Blog_date_alone_is_converted_but_not_labelled()
    {
        // 03:00 UTC is still the previous evening here — the date itself has to move with it.
        var utc = new DateTime(2026, 8, 12, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal("11 August 2026", BlogDateFormatter.DateLocal(utc, "en"));
        Assert.Equal("August 2026", BlogDateFormatter.MonthHeadingLocal(utc, "en"));
    }

    [Fact]
    public void The_pure_formatters_still_format_exactly_what_they_are_given()
    {
        // The conversion lives in the Local wrappers on purpose: these stay testable as formatters.
        var wallClock = new DateTime(2026, 8, 11, 21, 5, 0);

        Assert.Equal("11 August 2026, 21:05", BlogDateFormatter.DateTimeShort(wallClock, "en"));
    }
}

public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new NullableUtcDateTimeConverter());
        return options;
    }

    [Fact]
    public void A_kindless_value_is_written_as_UTC_with_a_Z()
    {
        // Exactly what EF hands back from SQLite, and exactly what used to reach the browser without
        // a suffix — which a browser reads as local time.
        var fromDatabase = new DateTime(2026, 8, 11, 21, 5, 0, DateTimeKind.Unspecified);

        Assert.Equal("\"2026-08-11T21:05:00.0000000Z\"", JsonSerializer.Serialize(fromDatabase, Options));
    }

    [Fact]
    public void A_utc_value_is_written_unchanged()
    {
        var utc = new DateTime(2026, 8, 11, 21, 5, 0, DateTimeKind.Utc);

        Assert.Equal("\"2026-08-11T21:05:00.0000000Z\"", JsonSerializer.Serialize(utc, Options));
    }

    [Fact]
    public void Null_stays_null()
    {
        Assert.Equal("null", JsonSerializer.Serialize((DateTime?)null, Options));
    }

    [Fact]
    public void Reading_a_Z_value_yields_the_same_instant_in_UTC()
    {
        var parsed = JsonSerializer.Deserialize<DateTime>("\"2026-08-11T21:05:00Z\"", Options);

        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        Assert.Equal(new DateTime(2026, 8, 11, 21, 5, 0, DateTimeKind.Utc), parsed);
    }

    [Fact]
    public void Reading_an_offset_value_is_converted_rather_than_truncated()
    {
        var parsed = JsonSerializer.Deserialize<DateTime>("\"2026-08-11T14:05:00-07:00\"", Options);

        Assert.Equal(new DateTime(2026, 8, 11, 21, 5, 0, DateTimeKind.Utc), parsed);
    }
}
