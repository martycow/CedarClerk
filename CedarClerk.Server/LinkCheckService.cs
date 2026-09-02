using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Server;

public sealed record DeadLink(string Url, string Status);

/// <summary>
/// Best-effort dead-link probe for the pre-publish checklist (T-238). Warnings only, never a
/// blocker: every probe is capped at five seconds, probes run in parallel, and a transport
/// failure is reported as "unreachable" rather than thrown — a misbehaving site on the other end
/// of a link must never delay or fail a publish.
///
/// The probe runs from the droplet, where Kestrel listens on loopback and nothing else is meant
/// to be reachable — so any URL whose host lands in a loopback/private/link-local range is
/// refused before a request is built, reported as "blocked". The address check happens on what
/// the host resolves to, not on how it is spelled.
/// </summary>
public class LinkCheckService(HttpClient http, Func<string, CancellationToken, Task<IPAddress[]>> resolveHost)
{
    [ActivatorUtilitiesConstructor]
    public LinkCheckService(HttpClient http) : this(http, Dns.GetHostAddressesAsync) { }

    public const int MaxLinks = 10;
    public const string Unreachable = "unreachable";
    public const string Blocked = "blocked";
    public static readonly TimeSpan PerLinkTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The links that answered 4xx/5xx, were refused, or did not answer at all. Healthy links are omitted.</summary>
    public async Task<IReadOnlyList<DeadLink>> CheckAsync(IReadOnlyCollection<string> urls, CancellationToken ct = default)
    {
        var probes = urls.Distinct().Take(MaxLinks).Select(url => ProbeAsync(url, ct));
        var results = await Task.WhenAll(probes);
        return results.OfType<DeadLink>().ToList();
    }

    private async Task<DeadLink?> ProbeAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new DeadLink(url, Unreachable);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PerLinkTimeout);

            if (await PointsInsideAsync(uri, cts.Token))
                return new DeadLink(url, Blocked);

            using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, uri),
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            // Plenty of servers refuse HEAD outright while serving the page fine.
            if (head.StatusCode != HttpStatusCode.MethodNotAllowed)
                return Verdict(url, head);

            using var get = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri),
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return Verdict(url, get);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new DeadLink(url, Unreachable);
        }
    }

    private static DeadLink? Verdict(string url, HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        return status >= 400 ? new DeadLink(url, status.ToString()) : null;
    }

    private async Task<bool> PointsInsideAsync(Uri uri, CancellationToken ct)
    {
        if (IPAddress.TryParse(uri.IdnHost, out var literal))
            return IsInternal(literal);

        var addresses = await resolveHost(uri.IdnHost, ct);
        return addresses.Any(IsInternal);
    }

    private static bool IsInternal(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 0                              // 0.0.0.0/8 — "this host" on Linux
                || b[0] == 10                             // 10/8
                || (b[0] == 172 && (b[1] & 0xF0) == 16)   // 172.16/12
                || (b[0] == 192 && b[1] == 168)           // 192.168/16
                || (b[0] == 169 && b[1] == 254);          // 169.254/16 link-local
        }

        return ip.IsIPv6LinkLocal                          // fe80::/10
            || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;   // fc00::/7 unique-local
    }
}
