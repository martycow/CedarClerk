using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// T-108 (ADR-084). A registration row is the reader's grant; revoking it must close the post to
// the link that row minted, and restoring must open it again, with the row itself untouched.
public class RegistrationRevokeTests
{
    private const string Owner = "o1";
    private const string Token = "reader-token";

    private static (Draft Draft, PostRegistration Registration) Seed(CedarDbContext db)
    {
        db.WithOwner(Owner);
        var draft = new Draft
        {
            Title = "Secret devlog",
            CedarJson = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Behind the gate."}]}]}""",
            OwnerId = Owner,
            BlogSlug = "secret",
            IsBlogPublished = true,
            IsPrivate = true,
        };
        var registration = new PostRegistration { OwnerId = Owner, DraftId = draft.Id, Email = "reader@x.test", AccessToken = Token };
        db.Drafts.Add(draft);
        db.PostRegistrations.Add(registration);
        db.SaveChanges();
        return (draft, registration);
    }

    private static async Task<(int Status, string Body)> Open(CedarDbContext db)
    {
        var ctx = BlogTestHost.Request("GET", "/secret", db, $"?access={Token}");
        await BlogEndpoints.HandleRequest(ctx);
        return (ctx.Response.StatusCode, BlogTestHost.Body(ctx));
    }

    [Fact]
    public async Task Revoking_closes_the_link_and_restoring_reopens_it()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var (draft, registration) = Seed(db);

        var before = await Open(db);
        Assert.Equal(StatusCodes.Status200OK, before.Status);
        Assert.Contains("Behind the gate.", before.Body);

        var revoked = await DraftEndpoints.SetRegistrationRevokedAsync(db, Owner, draft.Id, registration.Id, true);
        Assert.True(revoked!.IsRevoked);
        Assert.Equal(StatusCodes.Status404NotFound, (await Open(db)).Status);
        Assert.NotNull(db.PostRegistrations.SingleOrDefault(r => r.Id == registration.Id));

        var restored = await DraftEndpoints.SetRegistrationRevokedAsync(db, Owner, draft.Id, registration.Id, false);
        Assert.False(restored!.IsRevoked);
        Assert.Equal(StatusCodes.Status200OK, (await Open(db)).Status);
    }

    [Fact]
    public async Task Revoking_twice_is_the_same_as_once()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var (draft, registration) = Seed(db);

        await DraftEndpoints.SetRegistrationRevokedAsync(db, Owner, draft.Id, registration.Id, true);
        var again = await DraftEndpoints.SetRegistrationRevokedAsync(db, Owner, draft.Id, registration.Id, true);

        Assert.Equal(registration.Id, again!.Id);
        Assert.True(again.IsRevoked);
    }

    [Fact]
    public async Task Only_the_drafts_owner_may_revoke_and_only_that_drafts_rows()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var (draft, registration) = Seed(db);

        Assert.Null(await DraftEndpoints.SetRegistrationRevokedAsync(db, "someone-else", draft.Id, registration.Id, true));
        Assert.Null(await DraftEndpoints.SetRegistrationRevokedAsync(db, Owner, Guid.NewGuid(), registration.Id, true));
        Assert.Null(await DraftEndpoints.SetRegistrationRevokedAsync(db, Owner, draft.Id, Guid.NewGuid(), true));
        Assert.False(db.PostRegistrations.Single(r => r.Id == registration.Id).IsRevoked);
    }
}
