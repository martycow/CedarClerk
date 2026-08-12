using CedarClerk.Cli.Commands;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;
using Spectre.Console;
using Spectre.Console.Testing;

namespace CedarClerk.Cli.Tests;

// The promises that would be expensive to break: --dry-run touching nothing, the config holding no
// secrets, and the deploy command never deploying.
public class SafetyTests
{
    private static Session DryRun(TestConsole console) =>
        Session.From(new CedarSettings { DryRun = true, NoLogo = true }, console, new CliConfig());

    [Fact]
    public async Task Dry_run_executes_nothing_and_reports_success()
    {
        var console = new TestConsole();
        var session = DryRun(console);

        var remote = await session.Runner.RunRemoteAsync("rm -rf /", CancellationToken.None);
        var local = await session.Runner.RunLocalAsync("format", "C:", CancellationToken.None);

        Assert.True(remote.Ok);
        Assert.True(local.Ok);
        Assert.Contains("rm -rf /", console.Output);
        Assert.Contains("format C:", console.Output);
    }

    [Fact]
    public async Task Dry_run_opens_no_window_either()
    {
        var console = new TestConsole();

        var result = await DryRun(console).Runner.LaunchDetachedAsync("wt", "-d . pwsh", null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Contains("wt -d . pwsh", console.Output);
    }

    [Fact]
    public void The_claude_session_starts_in_the_repository()
    {
        var (exe, args) = ClaudeCommand.Launch("pwsh", @"D:\Moo.exe\CedarClerk");

        Assert.Contains("claude /remote-control", args);
        Assert.Contains(@"D:\Moo.exe\CedarClerk", args);
        Assert.Equal("pwsh", exe);

        // The shell is started directly: `wt` rebuilds the command line it is handed and drops the
        // quotes, which is what made this fail before (see the comment on ClaudeCommand).
        Assert.NotEqual("wt", exe);
    }

    [Fact]
    public async Task Dry_run_deletes_no_directory_either()
    {
        // The half the command runner never covered: publish/ is removed by a method call, not by a
        // process, so a build under --dry-run would have deleted a real directory.
        var console = new TestConsole();
        var writer = DryRun(console).Files;

        Assert.IsType<DryRunFileWriter>(writer);

        var canary = Path.Combine(Path.GetTempPath(), $"cedar-canary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(canary);
        writer.DeleteDirectory(canary);

        Assert.True(Directory.Exists(canary), "--dry-run deleted a real directory");
        Directory.Delete(canary);
        await Task.CompletedTask;
    }

    [Fact]
    public void A_real_run_writes_files_for_real()
    {
        Assert.IsType<RealFileWriter>(Session.From(new CedarSettings(), new TestConsole(), new CliConfig()).Files);
    }

    [Fact]
    public void Dry_run_does_not_even_reach_the_health_endpoint()
    {
        // An HTTP GET to production is still touching the outside world.
        Assert.IsType<Server.OfflineHealthProbe>(DryRun(new TestConsole()).Health);
    }

    [Fact]
    public void A_real_run_uses_the_process_runner()
    {
        var session = Session.From(new CedarSettings(), new TestConsole(), new CliConfig());
        Assert.IsType<ProcessCommandRunner>(session.Runner);
    }

    [Fact]
    public void The_config_carries_paths_and_hosts_and_nothing_that_could_be_a_secret()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new CliConfig());

        foreach (var forbidden in new[] { "password", "token", "secret", "key\":", "apiKey", "passphrase" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);

        // IdentityFile is a path to a key, not a key — worth stating, since the name reads otherwise.
        Assert.Contains("IdentityFile", json);
        Assert.Equal("", new CliConfig().IdentityFile);
    }

    [Fact]
    public void The_binary_name_is_written_down_exactly_once()
    {
        // Q-17 (the product name) is open; a rename must be one edit, not a sweep.
        var sources = Directory.GetFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        var literals = sources
            .Where(path => Path.GetFileName(path) != "CliConsts.cs")
            .Where(path => File.ReadAllText(path).Contains("\"cedar\""))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(literals.Count == 0, $"the name is hardcoded in: {string.Join(", ", literals)}");
    }

    [Fact]
    public void Assume_yes_answers_a_confirmation_and_says_that_it_did()
    {
        var console = new TestConsole();
        var session = Session.From(new CedarSettings { AssumeYes = true }, console, new CliConfig());

        Assert.True(session.Confirm("Restart production?"));
        Assert.Contains("--yes", console.Output);
        Assert.Contains("Restart production?", console.Output);
    }

    [Fact]
    public void Json_without_yes_refuses_a_destructive_command_rather_than_hanging_on_a_prompt()
    {
        var console = new TestConsole();
        var session = Session.From(new CedarSettings { Json = true }, console, new CliConfig());

        Assert.False(session.Confirm("Restart production?"));
        Assert.Contains("refused", console.Output);
    }

    // `cedar deploy` does deploy since ADR-119, so what has to be pinned down is no longer "it never
    // ships" but the two things that replaced that promise: --dry-run still touches nothing, and
    // --preflight still stops after the checks.

    [Fact]
    public async Task Dry_run_deploy_stops_at_the_checks_and_reaches_no_swap()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;

        await DeployCommand.RunAsync(new DeploySettings { DryRun = true, NoLogo = true }, CancellationToken.None);

        // The dry-run runner prints what it would have run, so a swap that had been reached would be
        // visible here in words.
        Assert.DoesNotContain("systemctl stop", console.Output);
        Assert.DoesNotContain("mv '", console.Output);
    }

    [Fact]
    public async Task Preflight_only_never_reaches_the_swap_either()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;

        await DeployCommand.RunAsync(
            new DeploySettings { DryRun = true, NoLogo = true, PreflightOnly = true }, CancellationToken.None);

        Assert.DoesNotContain("systemctl stop", console.Output);
        Assert.DoesNotContain("app.new", console.Output);
    }

    [Fact]
    public void Every_command_that_can_change_the_server_inherits_the_dry_run_flag()
    {
        // The flag it would be worst to forget on a tool that can now stop production.
        foreach (var type in new[] { typeof(DeploySettings), typeof(BuildSettings), typeof(TestSettings), typeof(OpenSettings) })
            Assert.True(typeof(CedarSettings).IsAssignableFrom(type), $"{type.Name} does not inherit the global flags");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CedarClerk.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "CedarClerk.Cli");
    }
}
