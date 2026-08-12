using CedarClerk.Cli.Parsing;

namespace CedarClerk.Cli.Server;

// /api/health is fetched over the public URL rather than from inside the droplet on purpose: it is
// the only check that also proves the Cloudflare tunnel is carrying traffic. It is HTTP rather than
// a process, so it gets its own swappable seam — otherwise --dry-run would still reach the network.
public interface IHealthProbe
{
    Task<HealthReport> GetAsync(string url, CancellationToken ct);
}

public sealed class HttpHealthProbe : IHealthProbe, IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<HealthReport> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            return HealthParser.Parse(await _client.GetStringAsync(url, ct));
        }
        catch (Exception)
        {
            // Down, unreachable, timed out and TLS-refused all mean the same thing to a person
            // looking at a dashboard: it is not answering.
            return HealthReport.Down;
        }
    }

    public void Dispose() => _client.Dispose();
}

public sealed class OfflineHealthProbe : IHealthProbe
{
    private readonly HealthReport _answer;

    public OfflineHealthProbe(HealthReport? answer = null) => _answer = answer ?? HealthReport.Down;

    public Task<HealthReport> GetAsync(string url, CancellationToken ct) => Task.FromResult(_answer);
}
