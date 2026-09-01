using CedarClerk.Core;
using PostHog;

namespace CedarClerk.Server.Analytics;

/// <summary>
/// The one way product events reach the provider (ADR-236). Call sites name an event from
/// <see cref="Consts.Analytics.Events"/> and pass the owner; everything else — whether analytics is
/// configured at all, and what happens when the provider is unreachable — is settled here.
///
/// Nothing on this type throws. It sits inside billing, publishing and registration, and a metric
/// is never worth failing a payment or losing a published post over.
/// </summary>
public class ProductAnalytics(
    IPostHogClient? client,
    IConfiguration cfg,
    ILogger<ProductAnalytics> logger)
{
    /// <summary>
    /// What the configuration asked for, deliberately not "is there a client object": the two are
    /// the same in production, and keeping them apart is what lets the swallow-everything guarantee
    /// below be tested at all.
    /// </summary>
    public bool IsEnabled { get; } =
        cfg.GetValue(Consts.Analytics.EnabledCfg, false)
        && !string.IsNullOrWhiteSpace(cfg[Consts.Analytics.ProjectKeyCfg]);

    /// <summary>
    /// Records one event against an owner. Properties are the event's own dimensions — never an
    /// email, a name or anything else that identifies the person to a third party (ADR-236): the
    /// owner id is an opaque key here and stays one.
    /// </summary>
    public void Track(string? ownerId, string eventName, Dictionary<string, object>? properties = null)
    {
        if (!IsEnabled || string.IsNullOrEmpty(ownerId)) return;

        try
        {
            Send(ownerId, eventName, properties ?? []);
        }
        catch (Exception ex)
        {
            // Debug, not Warning: this runs on every publish and payment, and the service logs every
            // EF statement already (~1.5M lines a day). An analytics outage must not become the
            // loudest thing in the journal.
            logger.LogDebug(ex, "Analytics event {Event} was not recorded", eventName);
        }
    }

    /// <summary>The one call into the provider, overridable so the swallow above can be proven.</summary>
    protected virtual void Send(string ownerId, string eventName, Dictionary<string, object> properties) =>
        client?.Capture(ownerId, eventName, properties);
}
