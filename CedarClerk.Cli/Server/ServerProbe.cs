using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Parsing;

namespace CedarClerk.Cli.Server;

// Everything the status screen needs, in one ssh round trip.
//
// One trip rather than ten because latency to fra1 dominates: ten sequential logins would take
// several seconds and make `watch` feel broken. The script echoes section markers and the parsing
// happens here, which is also what makes the whole thing testable against captured text.
public sealed class ServerProbe
{
    private readonly ICommandRunner _runner;
    private readonly IHealthProbe _health;
    private readonly CliConfig _config;

    public ServerProbe(ICommandRunner runner, IHealthProbe health, CliConfig config)
    {
        _runner = runner;
        _health = health;
        _config = config;
    }

    // The one place the backup directory is spelled out for `status`, `backup verify` and the deploy
    // preflight. The script's DEST on the droplet and this path are one fact in two places, and they
    // disagreed for a day once — a third copy of the path would be a third way to disagree. Copies
    // are counted by name, never by a bare glob: backup.log shares the directory and is touched after
    // every run, so a bare glob reports the log as the newest backup.
    public static string BackupCopies(string dataDir) => $"{dataDir}/backups/cedar-*.db.gz";

    public static string NewestBackup(string dataDir) =>
        $"$(ls -1t {BackupCopies(dataDir)} 2>/dev/null | head -1)";

    public string BuildScript()
    {
        var root = _config.RemoteRoot;
        var data = _config.RemoteDataDir;

        // Single quotes only: the whole thing travels as one argv through ssh, and a double quote
        // in here would have to survive Windows argument quoting on the way out.
        return string.Join('\n',
            "echo '=== service ==='",
            $"systemctl show {CliConsts.ServiceName} -p ActiveState,SubState,UnitFileState,ActiveEnterTimestamp,MainPID,NRestarts,MemoryCurrent 2>/dev/null",
            "echo '=== tunnel ==='",
            $"systemctl is-active {CliConsts.TunnelServiceName} 2>/dev/null",
            "echo '=== df ==='",
            $"df -Pk {root} 2>/dev/null",
            "echo '=== free ==='",
            "free -b 2>/dev/null",
            "echo '=== du ==='",
            $"du -sb {data}/media {data}/thumbs {data}/downloads {data}/dataprotection-keys 2>/dev/null",
            "echo '=== db ==='",
            $"stat -c '%s %n' {data}/cedar.db {data}/cedar.db-wal 2>/dev/null",
            "echo '=== deploy ==='",
            $"stat -c '%Y %n' {root}/app 2>/dev/null",
            "echo '=== backup ==='",
            $"ls -1t {BackupCopies(data)} 2>/dev/null | head -1",
            $"stat -c '%Y %n' {NewestBackup(data)} 2>/dev/null",
            "echo '=== cron ==='",
            "crontab -l 2>/dev/null | grep -cv '^#' || true",
            "echo '=== cpu ==='",
            "sar -u -f /var/log/sysstat/sa$(date -u -d yesterday +%d) 2>/dev/null",
            "sar -u 2>/dev/null",
            "echo '=== mem ==='",
            "sar -r -f /var/log/sysstat/sa$(date -u -d yesterday +%d) 2>/dev/null",
            "sar -r 2>/dev/null",
            "echo '=== end ==='");
    }

    public async Task<ServerSnapshot> TakeAsync(int historyHours, CancellationToken ct)
    {
        var remote = await _runner.RunRemoteAsync(BuildScript(), ct);
        var health = await _health.GetAsync(_config.HealthUrl, ct);
        return Interpret(remote, health, historyHours);
    }

    public ServerSnapshot Interpret(CommandResult remote, HealthReport health, int historyHours)
    {
        var sections = Split(remote.StdOut);

        // A failed login yields an empty body; saying "unreachable" is more honest than drawing a
        // dashboard of zeroes, which looks exactly like a healthy idle machine.
        var reachable = sections.ContainsKey("end");

        var du = SystemFacts.ParseDu(Section(sections, "du"));
        var files = SystemFacts.ParseStatSizes(Section(sections, "db"));

        var cpu = SarParser.ParseCpuBusy(Section(sections, "cpu"));
        var memory = SarParser.Parse(Section(sections, "mem"), "%memused");

        return new ServerSnapshot
        {
            Reachable = reachable,
            Error = reachable ? "" : (remote.StdErr.Trim().Length > 0 ? remote.StdErr.Trim() : remote.StdOut.Trim()),
            Health = health,
            Service = SystemFacts.ParseSystemctlShow(Section(sections, "service")),
            TunnelActive = Section(sections, "tunnel").Trim().StartsWith("active", StringComparison.OrdinalIgnoreCase),
            Disk = SystemFacts.ParseDf(Section(sections, "df")),
            Memory = SystemFacts.ParseFree(Section(sections, "free")),
            DataSizes = Named(du),
            DatabaseBytes = files.FirstOrDefault(f => f.Key.EndsWith("cedar.db", StringComparison.Ordinal)).Value,
            WalBytes = files.FirstOrDefault(f => f.Key.EndsWith("-wal", StringComparison.Ordinal)).Value,
            LastDeployUtc = FirstEpoch(Section(sections, "deploy")),
            LastBackupUtc = FirstEpoch(Section(sections, "backup")),
            HasBackupSchedule = ParseCronCount(Section(sections, "cron")) > 0,
            CpuHistory = Tail(cpu, historyHours),
            MemoryHistory = Tail(memory, historyHours),
            LocalVersion = CedarClerk.Core.Consts.CurrentVersion
        };
    }

    // sysstat samples every 10 minutes, so an hour is six points (ADR-118).
    private static IReadOnlyList<double> Tail(IReadOnlyList<SarSample> samples, int hours)
    {
        var wanted = Math.Max(1, hours * 6);
        return samples.Select(s => s.Value).TakeLast(wanted).ToList();
    }

    private static Dictionary<string, long> Named(Dictionary<string, long> byPath)
    {
        var named = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (path, bytes) in byPath)
        {
            var name = path.TrimEnd('/');
            name = name[(name.LastIndexOf('/') + 1)..];
            if (name.Length > 0) named[name] = bytes;
        }
        return named;
    }

    private static DateTimeOffset? FirstEpoch(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var fields = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            if (long.TryParse(fields[0], out var epoch) && epoch > 0)
                return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }
        return null;
    }

    private static int ParseCronCount(string text) =>
        int.TryParse(text.Trim(), out var count) ? count : 0;

    private static string Section(Dictionary<string, string> sections, string name) =>
        sections.TryGetValue(name, out var body) ? body : "";

    internal static Dictionary<string, string> Split(string output)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = "";
        var body = new List<string>();

        foreach (var raw in (output ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith("=== ", StringComparison.Ordinal) && trimmed.EndsWith(" ===", StringComparison.Ordinal))
            {
                if (current.Length > 0) sections[current] = string.Join('\n', body);
                current = trimmed[4..^4].Trim();
                body.Clear();
                continue;
            }
            if (current.Length > 0) body.Add(line);
        }
        if (current.Length > 0) sections[current] = string.Join('\n', body);
        return sections;
    }
}
