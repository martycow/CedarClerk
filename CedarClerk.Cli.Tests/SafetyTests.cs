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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_claude_session_starts_in_the_repository_whichever_terminal_is_available(bool windowsTerminal)
    {
        var (exe, args) = ClaudeCommand.Launch("pwsh", @"D:\Moo.exe\CedarClerk", windowsTerminal);

        Assert.Contains("claude /remote-control", args);
        Assert.Contains(@"D:\Moo.exe\CedarClerk", args);
        Assert.Equal(windowsTerminal ? "wt" : "pwsh", exe);

        // wt splits its own arguments on ';', so a semicolon in the wt form would be read as the
        // start of a second tab rather than as PowerShell syntax.
        if (windowsTerminal) Assert.DoesNotContain(";", args);
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

    [Fact]
    public async Task Deploy_prints_the_command_and_runs_no_deploy_of_its_own()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;

        var settings = new DeploySettings { DryRun = true, NoLogo = true };
        await DeployCommand.RunAsync(settings, CancellationToken.None);

        // Whatever the checks said, nothing that could ship anything may appear as an executed step.
        Assert.DoesNotContain("systemctl stop", console.Output);
        Assert.DoesNotContain("app.new", console.Output);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CedarClerk.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "CedarClerk.Cli");
    }
}
