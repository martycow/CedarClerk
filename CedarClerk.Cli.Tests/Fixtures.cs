namespace CedarClerk.Cli.Tests;

// Real output, captured from the production droplet on 12.08.2026 while ADR-118 was being written.
//
// Fixtures rather than a live server on purpose: a parser test that talks to fra1 fails when the
// network is down, passes when the format has silently changed, and cannot be run on a plane. These
// strings are the contract, and when a tool's output really does change, updating them is the
// deliberate act of accepting the new format.
public static class Fixtures
{
    public const string Df = """
        Filesystem     1024-blocks    Used Available Capacity Mounted on
        /dev/vda1         49691512 4878876  44796252      10% /
        """;

    public const string Free = """
                       total        used        free      shared  buff/cache   available
        Mem:      2063581184   596004864   108216320    75739136  1636683776  1467576320
        Swap:              0           0           0
        """;

    public const string SystemctlShow = """
        MainPID=49780
        NRestarts=0
        ExecMainStartTimestamp=Wed 2026-08-12 08:06:57 UTC
        ActiveEnterTimestamp=Wed 2026-08-12 08:06:57 UTC
        MemoryCurrent=186736640
        ActiveState=active
        SubState=running
        UnitFileState=disabled
        """;

    public const string SarCpu = """
        Linux 6.8.0-84-generic (cedarclerk-ubuntu-s-1vcpu-2gb-fra1) 	08/12/26 	_x86_64_	(1 CPU)

        07:20:00        CPU     %user     %nice   %system   %iowait    %steal     %idle
        07:30:00        all      5.68      0.00      1.40      0.19      0.82     91.90
        07:40:02        all      5.89      0.00      1.47      0.18      1.10     91.36
        07:50:09        all      5.55      0.00      1.38      0.14      0.72     92.22
        08:00:15        all      5.82      0.00      1.44      0.13      0.72     91.89
        08:10:16        all      8.80      0.00      2.94      0.24      1.02     87.01
        Average:        all      6.29      0.43      1.64      0.23      0.69     90.71
        """;

    // The reason SarParser reads by column name: kbavail sits between kbmemfree and kbmemused, and a
    // sysstat without it would shift %memused two places left.
    public const string SarMemory = """
        Linux 6.8.0-84-generic (cedarclerk-ubuntu-s-1vcpu-2gb-fra1) 	08/12/26 	_x86_64_	(1 CPU)

        07:50:09    kbmemfree   kbavail kbmemused  %memused kbbuffers  kbcached  kbcommit   %commit  kbactive   kbinact   kbdirty
        08:00:15       125484   1491696    206352     10.24     18796   1447144    750968     37.26    526524   1081688      1036
        08:10:16        98544   1515876    189340      9.40     19512   1490020    721152     35.79    561728   1073660      1244
        Average:       150542   1481329    231246     11.47     35039   1459203    789451     39.17    729541    928690      3632
        """;

    public const string Du = """
        980371270	/home/martycow/cedarclerk/data/media
        3769524	/home/martycow/cedarclerk/data/thumbs
        249200571	/home/martycow/cedarclerk/data/downloads
        """;

    public const string Health =
        """{"name":"CedarClerk.Server","env":"Production","version":"0.11.0","openRegistration":false,"timeUtc":"2026-08-12T08:17:45.0498426Z","status":"I'm fine, thanks."}""";

    // Note the hint block: journalctl prints it without -q, and it reads like a permission failure.
    public const string Journal = """
        Hint: You are currently not seeing messages from other users and the system.
              Users in groups 'adm', 'systemd-journal' can see all messages.
        Aug 12 08:17:44 cedarclerk-ubuntu-s-1vcpu-2gb-fra1 dotnet[49780]: info: Microsoft.EntityFrameworkCore.Database.Command[20101]
        Aug 12 08:17:44 cedarclerk-ubuntu-s-1vcpu-2gb-fra1 dotnet[49780]:       Executed DbCommand (0ms)
        Aug 12 08:17:44 cedarclerk-ubuntu-s-1vcpu-2gb-fra1 dotnet[49780]:       SELECT "c"."MemberCount"
        Aug 12 08:17:45 cedarclerk-ubuntu-s-1vcpu-2gb-fra1 dotnet[49780]: warn: CedarClerk.Server.Bot[0]
        Aug 12 08:17:45 cedarclerk-ubuntu-s-1vcpu-2gb-fra1 dotnet[49780]:       something to look at
        """;

    public static string Probe() => string.Join('\n',
        "=== service ===", SystemctlShow,
        "=== tunnel ===", "active",
        "=== df ===", Df,
        "=== free ===", Free,
        "=== du ===", Du,
        "=== db ===", "15011840 /home/martycow/cedarclerk/data/cedar.db",
                      "2896392 /home/martycow/cedarclerk/data/cedar.db-wal",
        "=== deploy ===", "1755000379 /home/martycow/cedarclerk/app",
        "=== backup ===", "",
        "=== cron ===", "0",
        "=== cpu ===", SarCpu,
        "=== mem ===", SarMemory,
        "=== end ===");
}
