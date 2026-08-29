using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// Drives FillQueueSlotsJob.FillAsync directly against an in-memory database — the acceptance
// criteria for Wave 2 item 10: rows appear for open occurrences, exhausted or expired drafts are
// never picked, and running the job twice never doubles a fill.
public class QueueSlotFillTests
{
    private const string Owner = "owner-a";
    private const string Other = "owner-b";

    private static readonly DateTime Now = new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc); // a Monday

    private static Microsoft.Data.Sqlite.SqliteConnection SharedDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var seed = Open(connection);
        seed.Database.EnsureCreated();
        return connection;
    }

    private static CedarDbContext Open(Microsoft.Data.Sqlite.SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());

    private static (Microsoft.Data.Sqlite.SqliteConnection Connection, Guid TargetId) Seeded()
    {
        var connection = SharedDatabase();
        using var db = Open(connection);
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "a@x.test", Email = "a@x.test" });
        db.Users.Add(new ApplicationUser { Id = Other, UserName = "b@x.test", Email = "b@x.test" });
        var target = new PublishTarget
        {
            OwnerId = Owner, Network = PublishNetworks.Telegram, DisplayName = "Chan", RemoteId = "-100777",
        };
        db.PublishTargets.Add(target);
        db.SaveChanges();
        return (connection, target.Id);
    }

    private static QueueSlot Slot(Guid targetId, int dayOfWeek = 2 /* Tuesday */, int minutes = 9 * 60, string category = "") =>
        new() { OwnerId = Owner, TargetId = targetId, Name = "morning", Category = category, DayOfWeek = dayOfWeek, TimeUtcMinutes = minutes };

    private static Draft Evergreen(string title, string category = "", int sendCount = 0,
        int? maxSends = null, DateTime? until = null, string owner = Owner) => new()
    {
        OwnerId = owner, Title = title, IsEvergreen = true, EvergreenCategory = category,
        EvergreenSendCount = sendCount, EvergreenMaxSends = maxSends, EvergreenUntil = until,
        PrimaryLanguage = "en",
    };

    [Fact]
    public void Occurrences_lands_on_the_right_weekday_inside_the_window()
    {
        var slot = Slot(Guid.NewGuid(), dayOfWeek: 2, minutes: 9 * 60);
        var occurrences = FillQueueSlotsJob.Occurrences(slot, Now + FillQueueSlotsJob.MinLead, Now + FillQueueSlotsJob.Horizon);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal(DayOfWeek.Tuesday, occurrence.DayOfWeek);
        Assert.Equal(new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), occurrence);
    }

    [Fact]
    public void Occurrence_closer_than_the_lead_is_not_offered()
    {
        // Monday 10:00 now; a Monday 11:00 slot is inside the 2h lead and must wait for next week —
        // which is outside the 7-day horizon, so nothing is offered at all.
        var slot = Slot(Guid.NewGuid(), dayOfWeek: 1, minutes: 11 * 60);
        Assert.Empty(FillQueueSlotsJob.Occurrences(slot, Now + FillQueueSlotsJob.MinLead, Now + FillQueueSlotsJob.Horizon));
    }

    [Fact]
    public async Task Fills_an_open_occurrence_with_a_scheduled_post_row()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        db.QueueSlots.Add(Slot(targetId));
        db.Drafts.Add(Evergreen("post"));
        await db.SaveChangesAsync();

        var created = await FillQueueSlotsJob.FillAsync(db, Now);

        Assert.Equal(1, created);
        var post = Assert.Single(db.ScheduledPosts.ToList());
        Assert.Equal("Pending", post.Status);
        Assert.Equal(targetId, post.TargetId);
        Assert.Equal(PublishNetworks.Telegram, post.Network);
        Assert.Equal("-100777", post.ChatId);
        Assert.Equal("en", post.Language);
        Assert.Equal(Owner, post.OwnerId);
        Assert.NotNull(post.SlotId);
        Assert.Equal(new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), post.ScheduledAtUtc);
    }

    [Fact]
    public async Task Running_twice_never_double_fills()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        db.QueueSlots.Add(Slot(targetId));
        db.Drafts.Add(Evergreen("post"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await FillQueueSlotsJob.FillAsync(db, Now));
        Assert.Equal(0, await FillQueueSlotsJob.FillAsync(db, Now));
        Assert.Single(db.ScheduledPosts.ToList());
    }

    [Fact]
    public async Task A_dragged_slot_ticket_still_occupies_its_week()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        db.QueueSlots.Add(Slot(targetId));
        db.Drafts.Add(Evergreen("post"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await FillQueueSlotsJob.FillAsync(db, Now));
        var post = db.ScheduledPosts.Single();

        // The calendar drag: SlotId survives, only the timestamp moves — forward two days...
        var (status, _) = await ScheduledPostEndpoints.RescheduleAsync(db, Owner, post.Id, post.ScheduledAtUtc.AddDays(2));
        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(0, await FillQueueSlotsJob.FillAsync(db, Now));

        // ...and back a day (still the same ISO week). The vacated timestamp is not an opening.
        (status, _) = await ScheduledPostEndpoints.RescheduleAsync(db, Owner, post.Id, new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc));
        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(0, await FillQueueSlotsJob.FillAsync(db, Now));

        Assert.Single(db.ScheduledPosts.ToList());
    }

    [Fact]
    public async Task A_failed_slot_send_frees_the_week_for_another_pick()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        var slot = Slot(targetId);
        var draft = Evergreen("post");
        db.QueueSlots.Add(slot);
        db.Drafts.Add(draft);
        db.ScheduledPosts.Add(new ScheduledPost
        {
            OwnerId = Owner, DraftId = draft.Id, TargetId = targetId, SlotId = slot.Id,
            ScheduledAtUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), Status = "Failed",
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, await FillQueueSlotsJob.FillAsync(db, Now));
    }

    [Fact]
    public async Task A_settled_occurrence_is_not_refilled()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        var slot = Slot(targetId);
        var draft = Evergreen("post");
        db.QueueSlots.Add(slot);
        db.Drafts.Add(draft);
        // The occurrence already went out (whatever its outcome) — a Sent row occupies it forever.
        db.ScheduledPosts.Add(new ScheduledPost
        {
            OwnerId = Owner, DraftId = draft.Id, TargetId = targetId, SlotId = slot.Id,
            ScheduledAtUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), Status = "Sent",
        });
        await db.SaveChangesAsync();

        Assert.Equal(0, await FillQueueSlotsJob.FillAsync(db, Now));
    }

    [Fact]
    public async Task Exhausted_and_expired_drafts_are_never_picked()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        db.QueueSlots.Add(Slot(targetId));
        db.Drafts.Add(Evergreen("used up", sendCount: 2, maxSends: 2));
        db.Drafts.Add(Evergreen("expired", until: Now.AddDays(-1)));
        db.Drafts.Add(new Draft { OwnerId = Owner, Title = "template", IsEvergreen = true, IsTemplate = true });
        db.Drafts.Add(new Draft { OwnerId = Owner, Title = "design doc", IsEvergreen = true, DocumentType = DocumentTypes.Design });
        db.Drafts.Add(Evergreen("someone else's", owner: Other));
        await db.SaveChangesAsync();

        Assert.Equal(0, await FillQueueSlotsJob.FillAsync(db, Now));
        Assert.Empty(db.ScheduledPosts.ToList());
    }

    [Fact]
    public async Task Least_sent_draft_wins_and_categories_gate_the_pool()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        db.QueueSlots.Add(Slot(targetId, category: "promo"));
        db.Drafts.Add(Evergreen("wrong category", category: "recap", sendCount: 0));
        db.Drafts.Add(Evergreen("sent often", category: "promo", sendCount: 5));
        db.Drafts.Add(Evergreen("fresh promo", category: "promo", sendCount: 1));
        await db.SaveChangesAsync();

        Assert.Equal(1, await FillQueueSlotsJob.FillAsync(db, Now));
        var post = Assert.Single(db.ScheduledPosts.ToList());
        var picked = db.Drafts.First(d => d.Id == post.DraftId);
        Assert.Equal("fresh promo", picked.Title);
    }

    [Fact]
    public async Task One_draft_is_never_pending_twice_for_one_target()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        // Two slots on the same destination, one eligible draft: the second occurrence stays open.
        db.QueueSlots.Add(Slot(targetId, dayOfWeek: 2));
        db.QueueSlots.Add(Slot(targetId, dayOfWeek: 4));
        db.Drafts.Add(Evergreen("only one"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await FillQueueSlotsJob.FillAsync(db, Now));
        Assert.Single(db.ScheduledPosts.ToList());
    }

    [Fact]
    public async Task Inactive_slots_and_inactive_targets_fill_nothing()
    {
        var (connection, targetId) = Seeded();
        using var db = Open(connection);
        var inactive = Slot(targetId);
        inactive.IsActive = false;
        db.QueueSlots.Add(inactive);

        var deadTarget = new PublishTarget
        {
            OwnerId = Owner, Network = PublishNetworks.Telegram, DisplayName = "Gone", RemoteId = "-1", IsActive = false,
        };
        db.PublishTargets.Add(deadTarget);
        db.QueueSlots.Add(Slot(deadTarget.Id, dayOfWeek: 3));
        db.Drafts.Add(Evergreen("ready"));
        await db.SaveChangesAsync();

        Assert.Equal(0, await FillQueueSlotsJob.FillAsync(db, Now));
    }
}
