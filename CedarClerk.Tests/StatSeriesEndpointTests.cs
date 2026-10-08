using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// T-242/T-243 at the endpoint level: every source is listed, only the selected ones are read,
// another owner's guid neither draws nor appears, an id of an unknown kind is refused, and the
// carry-forward alignment done in Core is what comes back. Owner o1's zone is UTC so the fixture
// days are the calendar days they look like.
public class StatSeriesEndpointTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly Guid _ownChannel;
    private readonly Guid _otherChannel;
    private readonly Guid _target;

    public StatSeriesEndpointTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddScoped<TenantProvider>();
        services.AddDbContext<CedarDbContext>(o => o.UseSqlite(_connection));
        _services = services.BuildServiceProvider();

        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1", Email = "o1@test.local", TimeZoneId = "UTC" });
        db.Users.Add(new ApplicationUser { Id = "o2", UserName = "o2", Email = "o2@test.local", TimeZoneId = "UTC" });

        var own = new Channel { OwnerId = "o1", Title = "Dev Diary", TelegramChatId = -1 };
        var other = new Channel { OwnerId = "o2", Title = "Stranger", TelegramChatId = -2 };
        db.Channels.AddRange(own, other);
        _ownChannel = own.Id;
        _otherChannel = other.Id;

        db.ChannelStatSnapshots.AddRange(
            Snap(own, 1, 100, 5, 1), Snap(own, 3, 110, 6, 1), Snap(own, 3, 112, 7, 2, hour: 20), Snap(own, 8, 120, 9, 2),
            Snap(other, 1, 999, 0, 0));
        db.ChannelPosts.Add(new ChannelPost { OwnerId = "o1", ChannelId = own.Id, DraftId = Guid.NewGuid(), TelegramMessageId = 1, PublishedAt = Day(4) });

        db.BlogStatSnapshots.AddRange(
            new BlogStatSnapshot { OwnerId = "o1", ViewCount = 10, LikeCount = 1, CommentCount = 0, TakenAt = Day(6) },
            new BlogStatSnapshot { OwnerId = "o1", ViewCount = 30, LikeCount = 2, CommentCount = 1, TakenAt = Day(10) });

        var target = new PublishTarget { OwnerId = "o1", Network = PublishNetworks.X, DisplayName = "@marty", RemoteId = "42" };
        db.PublishTargets.Add(target);
        _target = target.Id;
        db.PublishTargetStatSnapshots.Add(new PublishTargetStatSnapshot
        {
            OwnerId = "o1", TargetId = target.Id, Network = PublishNetworks.X, FollowerCount = 50, PostCount = 4, TakenAt = Day(9),
        });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private static DateTime Day(int day) => new(2026, 9, day, 4, 0, 0, DateTimeKind.Utc);
    private static DateOnly D(int day) => new(2026, 9, day);

    private static ChannelStatSnapshot Snap(Channel channel, int day, int members, int reactions, int comments, int hour = 4) => new()
    {
        OwnerId = channel.OwnerId, ChannelId = channel.Id, MemberCount = members,
        TelegramReactionCount = reactions, TelegramCommentCount = comments, TakenAt = Day(day).AddHours(hour - 4),
    };

    private async Task<StatSeriesEndpoints.BuildResult> BuildAsync(string uid, int days, params string[] sources)
    {
        // A platform scope on purpose: the endpoint's own OwnerId predicates are what keep the
        // stranger's rows out, and this is the test that would leak without them.
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        return await StatSeriesEndpoints.BuildAsync(db, uid, days, sources, Now);
    }

    [Fact]
    public async Task Available_lists_blog_then_channels_then_targets_with_first_days()
    {
        var result = await BuildAsync("o1", 30);

        Assert.Equal(["blog", $"channel:{_ownChannel}", $"target:{_target}"], result.Response.Available.Select(a => a.Id));
        Assert.Equal(["blog", "telegram", "x"], result.Response.Available.Select(a => a.Network));
        Assert.Equal(D(6), result.Response.Available[0].FirstDay);
        Assert.Equal(D(1), result.Response.Available[1].FirstDay);
        Assert.Equal(D(9), result.Response.Available[2].FirstDay);
        Assert.Equal([StatMetrics.MemberCount], result.Response.Available[2].Tracked);
        Assert.Empty(result.Response.Days);
        Assert.Empty(result.Response.Series);
        Assert.Equal("UTC", result.Response.Zone);
    }

    [Fact]
    public async Task A_channel_series_is_dense_carried_forward_with_delta_over_the_window()
    {
        var result = await BuildAsync("o1", 7, $"channel:{_ownChannel}");

        Assert.Equal(Enumerable.Range(4, 7).Select(D), result.Response.Days);
        var series = Assert.Single(result.Response.Series);
        Assert.Equal([112, 112, 112, 112, 120, 120, 120], series.Values[StatMetrics.MemberCount]!);
        Assert.Equal([7, 7, 7, 7, 9, 9, 9], series.Values[StatMetrics.LikeCount]!);
        Assert.Null(series.Values[StatMetrics.ViewCount]);
        Assert.Equal(120, series.Current[StatMetrics.MemberCount]);
        Assert.Equal(8, series.Delta[StatMetrics.MemberCount]);
        Assert.Equal([D(4)], series.PublishDays);
    }

    [Fact]
    public async Task A_project_keeps_the_readings_and_narrows_the_publish_days_to_its_documents()
    {
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var project = new Project { OwnerId = "o1", Name = "Game" };
        db.Projects.Add(project);
        var inside = new Draft { OwnerId = "o1", Title = "Inside", ProjectId = project.Id };
        db.Drafts.Add(inside);
        db.ChannelPosts.Add(new ChannelPost { OwnerId = "o1", ChannelId = _ownChannel, DraftId = inside.Id, TelegramMessageId = 2, PublishedAt = Day(8) });
        await db.SaveChangesAsync();

        var result = await StatSeriesEndpoints.BuildAsync(db, "o1", 7, [$"channel:{_ownChannel}"], Now, project.Id);

        var series = Assert.Single(result.Response.Series);
        Assert.Equal([112, 112, 112, 112, 120, 120, 120], series.Values[StatMetrics.MemberCount]!);
        Assert.Equal([D(8)], series.PublishDays);
    }

    [Fact]
    public async Task The_youngest_selected_source_shortens_the_window_for_every_series()
    {
        var result = await BuildAsync("o1", 30, "blog", $"channel:{_ownChannel}", $"target:{_target}");

        Assert.Equal([D(9), D(10)], result.Response.Days);
        Assert.Equal(["blog", $"channel:{_ownChannel}", $"target:{_target}"], result.Response.Series.Select(s => s.Id));
        Assert.Equal([10, 30], result.Response.Series[0].Values[StatMetrics.ViewCount]!);
        Assert.Equal([120, 120], result.Response.Series[1].Values[StatMetrics.MemberCount]!);
        Assert.Equal([50, 50], result.Response.Series[2].Values[StatMetrics.MemberCount]!);
        Assert.Null(result.Response.Series[2].Values[StatMetrics.LikeCount]);
    }

    [Fact]
    public async Task Another_owners_channel_is_neither_listed_nor_drawn()
    {
        var result = await BuildAsync("o1", 30, $"channel:{_otherChannel}", $"channel:{Guid.NewGuid()}");

        Assert.DoesNotContain(result.Response.Available, a => a.Id == $"channel:{_otherChannel}");
        Assert.Empty(result.Response.Series);
        Assert.Empty(result.Response.Days);
    }

    [Fact]
    public void An_id_of_an_unknown_kind_is_refused_and_a_well_formed_one_is_kept()
    {
        Assert.False(StatSeriesEndpoints.TryParseSources("blog,feed:123", out _));
        Assert.False(StatSeriesEndpoints.TryParseSources("channel:not-a-guid", out _));
        Assert.True(StatSeriesEndpoints.TryParseSources($" Blog , channel:{_ownChannel},blog", out var selected));
        Assert.Equal(["blog", $"channel:{_ownChannel}"], selected);
        Assert.True(StatSeriesEndpoints.TryParseSources(null, out var none));
        Assert.Empty(none);
    }

    [Fact]
    public async Task Days_are_clamped_and_the_audience_is_always_present()
    {
        var result = await BuildAsync("o1", 1);

        Assert.Equal(StatSeriesEndpoints.MinDays, result.Response.RequestedDays);
        Assert.NotNull(result.Response.Audience);
        Assert.Empty(result.Response.Audience.Countries);
    }

    [Fact]
    public async Task Csv_of_the_aligned_matrix_has_one_column_per_tracked_metric()
    {
        var result = await BuildAsync("o1", 30, "blog", $"target:{_target}");

        var lines = StatSeriesCsv.Write(result.Aligned)[StatSeriesCsv.Bom.Length..].Split("\r\n");
        Assert.Equal("day,Blog viewCount,Blog likeCount,Blog commentCount,@marty memberCount", lines[0]);
        Assert.Equal("2026-09-09,10,1,0,50", lines[1]);
        Assert.Equal("2026-09-10,30,2,1,50", lines[2]);
    }
}
