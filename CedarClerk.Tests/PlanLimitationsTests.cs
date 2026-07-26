using CedarClerk.Core;

namespace CedarClerk.Tests;

public class PlanLimitationsTests
{
    [Theory]
    [InlineData(PlanTiers.Free, 0, true)]
    [InlineData(PlanTiers.Free, 1, false)]
    [InlineData(PlanTiers.Pro, 2, true)]
    [InlineData(PlanTiers.Pro, 3, false)]
    [InlineData(PlanTiers.ProPlus, 9, true)]
    [InlineData(PlanTiers.ProPlus, 10, false)]
    [InlineData(PlanTiers.Forever, 9, true)]
    public void Channel_limits_per_tier(PlanTiers tier, int currentCount, bool expected)
    {
        Assert.Equal(expected, PlanLimitations.CanConnectAnotherChannel(tier, currentCount));
    }

    [Theory]
    [InlineData(PlanTiers.Free, 200L * 1024 * 1024)]
    [InlineData(PlanTiers.Pro, 8L * 1024 * 1024 * 1024)]
    [InlineData(PlanTiers.ProPlus, 16L * 1024 * 1024 * 1024)]
    [InlineData(PlanTiers.Forever, 100L * 1024 * 1024 * 1024)]
    public void Storage_limits_per_tier(PlanTiers tier, long expectedBytes)
    {
        Assert.Equal(expectedBytes, PlanLimitations.StorageLimitBytes(tier));
        Assert.True(PlanLimitations.HasStorageRoom(tier, expectedBytes - 10, 10));
        Assert.False(PlanLimitations.HasStorageRoom(tier, expectedBytes - 10, 11));
    }

    [Theory]
    [InlineData(PlanTiers.Free, false)]
    [InlineData(PlanTiers.Pro, true)]
    [InlineData(PlanTiers.ProPlus, true)]
    [InlineData(PlanTiers.Forever, true)]
    public void Signature_is_pro_and_above(PlanTiers tier, bool expected)
    {
        Assert.Equal(expected, PlanLimitations.HasCustomSignature(tier));
    }

    [Fact]
    public void Free_tier_always_gets_the_fixed_attribution_regardless_of_stored_signature()
    {
        // Write-gated at the API level so this shouldn't normally happen, but a stale value
        // (e.g. left over from a downgrade) must never leak through for a Free account.
        var resolved = PlanLimitations.ResolveSignature(PlanTiers.Free, "Some leftover text", "https://example.com");
        Assert.Equal(Consts.Signatures.FreeAttributionText, resolved!.Text);
        Assert.Equal(Consts.URLs.MainHost, resolved.Href);
    }

    [Fact]
    public void Pro_with_blank_signature_gets_no_signature_at_all()
    {
        // The actual point of paying: removing the Free-tier attribution, not falling back to it.
        Assert.Null(PlanLimitations.ResolveSignature(PlanTiers.Pro, null, null));
        Assert.Null(PlanLimitations.ResolveSignature(PlanTiers.Pro, "   ", null));
    }

    [Fact]
    public void Pro_with_custom_signature_and_no_url_has_no_href()
    {
        var resolved = PlanLimitations.ResolveSignature(PlanTiers.Pro, "M.C. (c) 2026", null);
        Assert.Equal("M.C. (c) 2026", resolved!.Text);
        Assert.Null(resolved.Href);
    }

    [Fact]
    public void Pro_with_custom_signature_and_url_is_clickable()
    {
        var resolved = PlanLimitations.ResolveSignature(PlanTiers.ProPlus, "Visit my site", "https://example.com");
        Assert.Equal("Visit my site", resolved!.Text);
        Assert.Equal("https://example.com", resolved.Href);
    }

    [Theory]
    [InlineData(PlanTiers.Free, false)]
    [InlineData(PlanTiers.Pro, false)]
    [InlineData(PlanTiers.ProPlus, true)]
    [InlineData(PlanTiers.Forever, true)]
    public void Ai_features_are_pro_plus_and_above(PlanTiers tier, bool expected)
    {
        Assert.Equal(expected, PlanLimitations.HasAiFeatures(tier));
    }
}

public class SubscriptionPlanHelperTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(Consts.Plans.Pro, PlanTiers.Pro, 3)]
    [InlineData(Consts.Plans.ProPlus, PlanTiers.ProPlus, 6)]
    [InlineData(Consts.Plans.Trial, PlanTiers.ProPlus, 1)]
    public void Plan_metadata(string plan, PlanTiers tier, int usd)
    {
        Assert.True(SubscriptionPlanHelper.IsValid(plan));
        Assert.Equal(tier, SubscriptionPlanHelper.TierOf(plan));
        Assert.Equal(usd, SubscriptionPlanHelper.PriceUsd(plan));
    }

    [Fact]
    public void Unknown_plan_is_invalid()
    {
        Assert.False(SubscriptionPlanHelper.IsValid("enterprise"));
        Assert.Throws<ArgumentException>(() => SubscriptionPlanHelper.TierOf("enterprise"));
    }

    [Fact]
    public void Effective_paid_tier_with_future_expiry_stays()
    {
        Assert.Equal(PlanTiers.Pro, SubscriptionPlanHelper.CheckPlanExpiration(PlanTiers.Pro, Now.AddDays(5), Now));
    }

    [Fact]
    public void Effective_paid_tier_with_past_expiry_is_free()
    {
        Assert.Equal(PlanTiers.Free, SubscriptionPlanHelper.CheckPlanExpiration(PlanTiers.Pro, Now.AddMinutes(-1), Now));
        Assert.Equal(PlanTiers.Free, SubscriptionPlanHelper.CheckPlanExpiration(PlanTiers.ProPlus, Now.AddDays(-1), Now));
    }

    [Fact]
    public void Effective_null_expiry_means_manual_grant_never_expires()
    {
        Assert.Equal(PlanTiers.Pro, SubscriptionPlanHelper.CheckPlanExpiration(PlanTiers.Pro, null, Now));
    }

    [Fact]
    public void Effective_forever_and_free_ignore_expiry()
    {
        Assert.Equal(PlanTiers.Forever, SubscriptionPlanHelper.CheckPlanExpiration(PlanTiers.Forever, Now.AddDays(-100), Now));
        Assert.Equal(PlanTiers.Free, SubscriptionPlanHelper.CheckPlanExpiration(PlanTiers.Free, null, Now));
    }

    [Fact]
    public void NextExpiry_fresh_purchase_starts_from_now()
    {
        var expiry = SubscriptionPlanHelper.NextExpiry(PlanTiers.Free, null, PlanTiers.Pro, Now);
        Assert.Equal(Now + SubscriptionPlanHelper.SubscriptionPeriod + SubscriptionPlanHelper.Grace, expiry);
    }

    [Fact]
    public void NextExpiry_early_renewal_same_tier_extends_remaining_time()
    {
        var current = Now.AddDays(10);
        var expiry = SubscriptionPlanHelper.NextExpiry(PlanTiers.Pro, current, PlanTiers.Pro, Now);
        Assert.Equal(current + SubscriptionPlanHelper.SubscriptionPeriod + SubscriptionPlanHelper.Grace, expiry);
    }

    [Fact]
    public void NextExpiry_tier_change_starts_from_now()
    {
        var expiry = SubscriptionPlanHelper.NextExpiry(PlanTiers.Pro, Now.AddDays(10), PlanTiers.ProPlus, Now);
        Assert.Equal(Now + SubscriptionPlanHelper.SubscriptionPeriod + SubscriptionPlanHelper.Grace, expiry);
    }
}
