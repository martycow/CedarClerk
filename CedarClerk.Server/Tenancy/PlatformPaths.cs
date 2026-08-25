namespace CedarClerk.Server.Tenancy;

/// <summary>
/// The request paths that legitimately read across owners, as data rather than as a habit spread
/// over the endpoint files.
///
/// The mode has to be chosen before <c>UseAuthentication</c>: Identity's security-stamp check
/// queries AspNetUsers on every authorized request, and that first query is what compiles the
/// model. Anything deciding later would be told it is too late.
/// </summary>
public static class PlatformPaths
{
    /// <summary>The gate itself, readable so a test can hold it to its own shape.</summary>
    public static readonly IReadOnlyList<string> Prefixes =
    [
        // ADR-122 — every cross-owner read in the app lives behind this one gate.
        "/api/admin",
        // Provider callbacks. A webhook is a request from Stripe or PayPal about somebody's
        // payment; there is no signed-in user to take a tenant from, and the row it settles
        // belongs to whichever account bought something.
        "/api/billing/stripe/webhook",
        "/api/billing/paypal/capture",
        // ADR-058 — the local-only import bypass names its owner by email in the body, which is
        // only readable after the request is already under way.
        "/api/drafts/import-markdown-local",
    ];

    public static bool IsPlatform(PathString path) =>
        Prefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
