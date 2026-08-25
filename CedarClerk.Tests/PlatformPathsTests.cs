using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// PlatformPaths is the one place where a request is allowed to read across owners, so widening it
// by accident is the highest-cost mistake in the tenancy code. The prefix-lookalike cases below
// are the point: a substring match would hand /api/administrators the same exemption as /api/admin.
public class PlatformPathsTests
{
    [Theory]
    [InlineData("/api/admin")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/users/123/delete")]
    [InlineData("/API/Admin/users")]
    [InlineData("/api/Billing/Stripe/Webhook")]
    [InlineData("/api/billing/stripe/webhook")]
    [InlineData("/api/billing/paypal/capture")]
    [InlineData("/api/drafts/import-markdown-local")]
    public void Reads_across_owners(string path) =>
        Assert.True(PlatformPaths.IsPlatform(new PathString(path)), path);

    [Theory]
    [InlineData("/")]
    [InlineData("/api/drafts")]
    [InlineData("/api/drafts/123")]
    [InlineData("/api/drafts/import-markdown")]
    [InlineData("/api/assets")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/billing")]
    [InlineData("/api/billing/stripe")]
    [InlineData("/api/billing/stripe/webhooks")]
    [InlineData("/api/adminx")]
    [InlineData("/api/administrator")]
    [InlineData("/api/administrators")]
    [InlineData("/media/asset_x.png")]
    public void Stays_inside_one_owner(string path) =>
        Assert.False(PlatformPaths.IsPlatform(new PathString(path)), path);

    [Fact]
    public void An_empty_path_is_not_platform() =>
        Assert.False(PlatformPaths.IsPlatform(PathString.Empty));

    // A page reading across owners would be a cross-tenant HTML surface — the thing the subdomain
    // work exists to remove.
    [Fact]
    public void Every_platform_prefix_is_an_api_path() =>
        Assert.All(PlatformPaths.Prefixes, prefix => Assert.StartsWith("/api/", prefix));

    // One prefix covering another means somebody widened the gate and the narrower entry is now
    // decoration.
    [Fact]
    public void No_platform_prefix_contains_another()
    {
        foreach (var prefix in PlatformPaths.Prefixes)
        {
            var covered = PlatformPaths.Prefixes
                .Where(other => other != prefix)
                .Where(other => new PathString(other).StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.True(covered.Length == 0, $"{prefix} already covers {string.Join(", ", covered)}.");
        }
    }
}
