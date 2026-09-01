using CedarClerk.Core;
using CedarClerk.Server.Analytics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CedarClerk.Tests;

// ADR-236 clause 3. Two properties matter here and neither is visible from a call site: an
// unconfigured install must record nothing rather than fail, and a provider that throws must not
// take a payment or a publish down with it.
public class ProductAnalyticsTests
{
    /// <summary>Records what would have gone to the provider, or fails on the way if asked to.</summary>
    private sealed class Recorder(IConfiguration cfg, bool @throw = false)
        : ProductAnalytics(null, cfg, NullLogger<ProductAnalytics>.Instance)
    {
        public readonly List<(string Owner, string Event, Dictionary<string, object> Properties)> Sent = [];

        protected override void Send(string ownerId, string eventName, Dictionary<string, object> properties)
        {
            if (@throw) throw new HttpRequestException("the provider is unreachable");
            Sent.Add((ownerId, eventName, properties));
        }
    }

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static IConfiguration Configured() => Config(
        (Consts.Analytics.EnabledCfg, "true"),
        (Consts.Analytics.ProjectKeyCfg, "phc_test"));

    [Fact]
    public void Without_configuration_nothing_is_recorded()
    {
        var analytics = new Recorder(Config());

        analytics.Track("owner-1", Consts.Analytics.Events.DraftCreated);

        Assert.False(analytics.IsEnabled);
        Assert.Empty(analytics.Sent);
    }

    [Fact]
    public void A_key_without_the_enabled_flag_stays_off()
    {
        var analytics = new Recorder(Config((Consts.Analytics.ProjectKeyCfg, "phc_test")));

        analytics.Track("owner-1", Consts.Analytics.Events.PostPublished);

        Assert.False(analytics.IsEnabled);
        Assert.Empty(analytics.Sent);
    }

    [Fact]
    public void The_enabled_flag_without_a_key_stays_off()
    {
        var analytics = new Recorder(Config((Consts.Analytics.EnabledCfg, "true")));

        analytics.Track("owner-1", Consts.Analytics.Events.PostPublished);

        Assert.False(analytics.IsEnabled);
        Assert.Empty(analytics.Sent);
    }

    [Fact]
    public void A_configured_provider_receives_the_event_against_the_owner()
    {
        var analytics = new Recorder(Configured());

        analytics.Track("owner-1", Consts.Analytics.Events.PlanPurchased,
            new() { ["plan"] = Consts.Plans.Pro });

        Assert.True(analytics.IsEnabled);
        var sent = Assert.Single(analytics.Sent);
        Assert.Equal("owner-1", sent.Owner);
        Assert.Equal("plan_purchased", sent.Event);
        Assert.Equal(Consts.Plans.Pro, sent.Properties["plan"]);
    }

    [Fact]
    public void An_event_with_no_properties_still_carries_a_map_rather_than_null()
    {
        var analytics = new Recorder(Configured());

        analytics.Track("owner-1", Consts.Analytics.Events.DraftCreated);

        Assert.Empty(Assert.Single(analytics.Sent).Properties);
    }

    [Fact]
    public void An_owner_that_is_not_known_records_nothing()
    {
        var analytics = new Recorder(Configured());

        analytics.Track(null, Consts.Analytics.Events.SignupCompleted);
        analytics.Track("", Consts.Analytics.Events.SignupCompleted);

        Assert.Empty(analytics.Sent);
    }

    [Fact]
    public void A_provider_that_throws_does_not_reach_the_caller()
    {
        var analytics = new Recorder(Configured(), @throw: true);

        // This sits inside the Stripe webhook and the publish queue. If it can throw, an analytics
        // outage becomes a failed payment.
        analytics.Track("owner-1", Consts.Analytics.Events.CreditsPurchased);
    }

    [Fact]
    public void Every_dictionary_name_is_snake_case_and_matches_METRICS()
    {
        // ADR-126 clause 2 — the names are a contract, and METRICS.md §4 is the list. A rename here
        // is a metric that quietly stops adding up, so the shape is asserted rather than trusted.
        string[] expected =
        [
            "signup_started", "signup_completed", "draft_created",
            "post_published", "post_published_first",
            "trial_started", "plan_purchased", "plan_renewed",
            "credits_purchased", "ai_used",
        ];

        var declared = typeof(Consts.Analytics.Events)
            .GetFields()
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(expected.Order(), declared.Order());
    }
}
