using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;

namespace CedarClerk.Cli.Tests;

// 13.08.2026: a deploy hung forever on `stat -c %s` during Upload. The remote command had finished
// and left no process behind; the client sat at 0% CPU because ssh had inherited the console's stdin
// and will not exit until that reaches EOF. `-n` is the fix, and the one call that genuinely feeds
// ssh a file must not have it — hence a test for both halves rather than for the flag's presence.
public class SshArgumentTests
{
    private static ProcessCommandRunner Runner() =>
        new(new CliConfig { Host = "martycow@periwinkle.mooexe.dev" });

    [Fact]
    public void An_ordinary_remote_command_takes_its_stdin_from_nowhere()
    {
        Assert.Contains(" -n ", Runner().SshArgs("stat -c %s /tmp/x"));
    }

    [Fact]
    public void The_upload_keeps_its_stdin()
    {
        // This is the transfer path: -n here would send an empty file and report success.
        Assert.DoesNotContain(" -n ", Runner().SshArgs("cat >> '/tmp/x'", stdinIsUsed: true));
    }

    [Fact]
    public void Batch_mode_and_the_keepalive_survive_either_way()
    {
        foreach (var args in new[] { Runner().SshArgs("uptime"), Runner().SshArgs("cat", stdinIsUsed: true) })
        {
            Assert.Contains("BatchMode=yes", args);
            Assert.Contains("ServerAliveInterval=15", args);
        }
    }
}
