using CedarClerk.Server;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// Wave 1 item 8 — the storage half of preview links: the token is unique across the whole table
// (it resolves a draft on its own), null means "no link" and any number of drafts may say so.
public class DraftPreviewTokenTests
{
    [Fact]
    public void Many_drafts_without_a_link_coexist()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "One" });
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "Two" });
        db.SaveChanges();

        Assert.Equal(2, db.Drafts.Count(d => d.PreviewToken == null));
    }

    [Fact]
    public void Two_drafts_cannot_share_one_token()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "One", PreviewToken = "same-token" });
        db.SaveChanges();

        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "Two", PreviewToken = "same-token" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void A_token_resolves_its_draft_and_revoking_stops_it()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var draft = new Draft { OwnerId = "o1", Title = "Shared", PreviewToken = PrivateAccess.NewToken() };
        db.Drafts.Add(draft);
        db.SaveChanges();

        Assert.Equal(draft.Id, db.Drafts.Single(d => d.PreviewToken == draft.PreviewToken).Id);

        draft.PreviewToken = null;
        db.SaveChanges();
        Assert.Empty(db.Drafts.Where(d => d.PreviewToken != null));
    }

    [Fact]
    public void New_tokens_are_long_url_safe_and_distinct()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => PrivateAccess.NewToken()).ToList();
        Assert.Equal(50, tokens.Distinct().Count());
        foreach (var token in tokens)
        {
            // 24 random bytes come out as 32 base64url characters.
            Assert.Equal(32, token.Length);
            Assert.Matches("^[A-Za-z0-9_-]+$", token);
        }
    }
}
