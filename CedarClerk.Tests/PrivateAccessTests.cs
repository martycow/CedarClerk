using CedarClerk.Server;
using Microsoft.AspNetCore.DataProtection;

namespace CedarClerk.Tests;

// T-023/T-064. The cookie this replaces said "1" — presence alone, forgeable by anyone who knew a
// draft's id — and it lived only in the browser that filled in the form, which is why a reader who
// switched from Telegram's in-app browser to Chrome was asked to register twice.
public class PrivateAccessTests
{
    private static PrivateAccess Make(string appName = "cedar-test") =>
        new(DataProtectionProvider.Create(appName));

    [Fact]
    public void A_grant_is_valid_for_the_post_it_was_issued_for()
    {
        var access = Make();
        var draft = Guid.NewGuid();
        var token = PrivateAccess.NewToken();

        Assert.True(access.IsValid(access.Grant(draft, token), draft, out var read));
        Assert.Equal(token, read);
    }

    // The hole this closes: a cookie that proves nothing lets anyone who knows an id read a
    // private post.
    [Fact]
    public void A_hand_written_cookie_grants_nothing()
    {
        var draft = Guid.NewGuid();

        Assert.False(Make().IsValid("1", draft, out _));
        Assert.False(Make().IsValid(draft.ToString("N") + ":anything", draft, out _));
    }

    [Fact]
    public void A_grant_for_one_post_does_not_open_another()
    {
        var access = Make();
        var grant = access.Grant(Guid.NewGuid(), PrivateAccess.NewToken());

        Assert.False(access.IsValid(grant, Guid.NewGuid(), out _));
    }

    [Fact]
    public void A_grant_from_another_server_is_refused()
    {
        var draft = Guid.NewGuid();
        var elsewhere = Make("someone-elses-server").Grant(draft, PrivateAccess.NewToken());

        Assert.False(Make().IsValid(elsewhere, draft, out _));
    }

    [Fact]
    public void Tokens_are_url_safe_and_not_guessable_by_shape()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => PrivateAccess.NewToken()).ToList();

        Assert.Equal(50, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.Matches("^[A-Za-z0-9_-]+$", t));
        Assert.All(tokens, t => Assert.True(t.Length >= 30));
    }
}
