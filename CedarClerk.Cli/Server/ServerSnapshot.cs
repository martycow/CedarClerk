using CedarClerk.Cli.Parsing;

namespace CedarClerk.Cli.Server;

public sealed record ServerSnapshot
{
    public bool Reachable { get; init; }
    public string Error { get; init; } = "";

    public HealthReport Health { get; init; } = HealthReport.Down;
    public ServiceState Service { get; init; } = ServiceState.Unknown;
    public bool TunnelActive { get; init; }

    public DiskUsage Disk { get; init; } = DiskUsage.Unknown;
    public MemoryUsage Memory { get; init; } = MemoryUsage.Unknown;

    public IReadOnlyDictionary<string, long> DataSizes { get; init; } = new Dictionary<string, long>();
    public long DatabaseBytes { get; init; }
    public long WalBytes { get; init; }

    public DateTimeOffset? LastDeployUtc { get; init; }
    public DateTimeOffset? LastBackupUtc { get; init; }
    public bool HasBackupSchedule { get; init; }

    public IReadOnlyList<double> CpuHistory { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> MemoryHistory { get; init; } = Array.Empty<double>();

    // The build that is answering versus the build this working copy would produce. They disagree
    // routinely — that is the single most useful line on the status screen.
    public string LocalVersion { get; init; } = "";

    public bool VersionMatches =>
        Health.Answered && LocalVersion.Length > 0 && Health.Version == LocalVersion;

    public long DataTotalBytes => DataSizes.Values.Sum() + DatabaseBytes + WalBytes;
}
