using CedarClerk.Core;

namespace CedarClerk.Tests;

// This predicate decides whether a stranger may create an account, so its edges are worth
// pinning: off-by-one on the use cap, and the exact moment an expiry takes effect.
public class InviteCodeRulesTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Active_code_with_no_limits_is_usable()
    {
        Assert.True(InviteCodeRules.IsUsable(true, null, null, uses: 999, Now));
    }

    [Fact]
    public void Deactivated_code_is_never_usable()
    {
        Assert.False(InviteCodeRules.IsUsable(false, null, null, uses: 0, Now));
    }

    [Theory]
    [InlineData(-1, false)] // expired an hour ago
    [InlineData(0, false)]  // expires exactly now — already over
    [InlineData(1, true)]   // an hour left
    public void Expiry_takes_effect_at_the_instant_itself(int hoursFromNow, bool expected)
    {
        var expiry = Now.AddHours(hoursFromNow);
        Assert.Equal(expected, InviteCodeRules.IsUsable(true, expiry, null, uses: 0, Now));
    }

    [Theory]
    [InlineData(4, true)]   // one left
    [InlineData(5, false)]  // cap reached — a cap of 5 admits exactly five accounts
    [InlineData(6, false)]  // somehow over; still closed
    public void Use_cap_admits_exactly_max_uses_accounts(int uses, bool expected)
    {
        Assert.Equal(expected, InviteCodeRules.IsUsable(true, null, maxUses: 5, uses, Now));
    }

    [Fact]
    public void Null_max_uses_means_unlimited()
    {
        Assert.True(InviteCodeRules.IsUsable(true, null, maxUses: null, uses: 10_000, Now));
    }

    // Any single failing condition closes the code, regardless of the others.
    [Fact]
    public void Expired_code_stays_closed_even_under_its_cap()
    {
        Assert.False(InviteCodeRules.IsUsable(true, Now.AddDays(-1), maxUses: 100, uses: 0, Now));
    }
}
