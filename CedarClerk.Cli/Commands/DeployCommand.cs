using System.ComponentModel;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

public sealed class DeploySettings : CedarSettings
{
    [CommandOption("--desktop")]
    [Description("Include the desktop installer in the command it prepares.")]
    public bool Desktop { get; init; }

    [CommandOption("--skip-build")]
    [Description("Prepare a -SkipBuild deploy, for continuing an interrupted upload.")]
    public bool SkipBuild { get; init; }
}

// Runs every check deploy.ps1 would run, shows what would change, and then stops and prints the
// command for Marty to run.
//
// It deliberately does not deploy (ADR-118 decision 2). deploy.ps1 has no safe stopping point
// before the swap, so "take it to the last step" would mean cutting the script apart — which is
// the one thing this stage is not allowed to do. What the CLI can do is make sure that when the
// command is run, nothing about it is a surprise.
public sealed class DeployCommand : AsyncCommand<DeploySettings>
{
    private sealed record Check(bool Ok, bool Fatal, string Label, string Detail);

    protected override Task<int> ExecuteAsync(CommandContext context, DeploySettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(DeploySettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        if (!session.RequireRepo()) return 2;

        var version = CedarClerk.Core.Consts.CurrentVersion;
        var checks = new List<Check>();

        // Under --dry-run git is not run either, so these come back empty. Saying so beats printing
        // a sentence with a hole where the branch name should be.
        var branch = (await Git(session, "rev-parse --abbrev-ref HEAD", cancellationToken)).Trim();
        checks.Add(new Check(branch == "master", true, "branch",
            branch switch
            {
                "master" => "master",
                "" => "not read (nothing was run)",
                _ => $"{branch} - deploy.ps1 refuses anything but master"
            }));

        var dirty = (await Git(session, "status --porcelain", cancellationToken)).Trim();
        var dirtyCount = dirty.Length == 0 ? 0 : dirty.Split('\n').Length;
        checks.Add(new Check(dirtyCount == 0, true, "tree",
            dirtyCount == 0 ? "clean" : $"{dirtyCount} uncommitted change(s) - what ships would match no commit"));

        var tags = (await Git(session, "tag --points-at HEAD", cancellationToken)).Split('\n').Select(t => t.Trim()).ToArray();
        checks.Add(new Check(tags.Contains(version), false, "tag",
            tags.Contains(version) ? $"HEAD is tagged {version}" : $"HEAD carries no '{version}' tag (a warning, not a stop)"));

        var snapshot = await session.Console
            .Status()
            .Spinner(session.Glyphs.Spinner)
            .SpinnerStyle(new Style(Palette.Accent))
            .StartAsync("checking the droplet…", _ => session.Probe.TakeAsync(1, cancellationToken));

        checks.Add(new Check(snapshot.Reachable, true, "ssh",
            snapshot.Reachable ? session.Config.Host : $"cannot reach {session.Config.Host}"));

        checks.Add(new Check(snapshot.Health.Answered, false, "live",
            snapshot.Health.Answered
                ? $"v{snapshot.Health.Version} answering"
                : "production is not answering - this deploy would bring it back"));

        checks.Add(await LiveTagCheck(session, snapshot.Health.Version, cancellationToken));

        // The swap keeps two copies of the app on disk at once (ADR-113).
        var freeMb = snapshot.Disk.FreeBytes / (1024 * 1024);
        checks.Add(new Check(freeMb > 400, false, "disk",
            $"{Format.Size(snapshot.Disk.FreeBytes)} free" + (freeMb > 400 ? "" : " - the swap keeps two copies of app/")));

        if (settings.Desktop)
        {
            var packageVersion = DesktopVersion(session);
            checks.Add(new Check(packageVersion == version, true, "desktop",
                packageVersion == version
                    ? $"shell says {packageVersion}"
                    : $"shell says {packageVersion}, this build is {version} - deploy.ps1 will stop and ask for a commit"));
        }

        if (session.Json)
        {
            session.WriteJson(new
            {
                version,
                live = snapshot.Health.Version,
                ready = checks.All(c => c.Ok || !c.Fatal),
                checks = checks.Select(c => new { c.Label, c.Ok, c.Fatal, c.Detail }),
                command = CommandLine(session, settings)
            });
            return checks.Any(c => !c.Ok && c.Fatal) ? 1 : 0;
        }

        Report(session, checks, snapshot.Health.Version, version);

        var blocked = checks.Any(c => !c.Ok && c.Fatal);
        session.Console.WriteLine();

        if (blocked)
        {
            session.Problem("not ready to deploy - the lines marked above would stop deploy.ps1 too.");
            return 1;
        }

        session.Console.Write(new Panel(new Markup(
                $"[{Palette.Hex(Palette.Text)}]{Markup.Escape(CommandLine(session, settings))}[/]"))
        {
            Border = session.Glyphs.Border,
            BorderStyle = new Style(Palette.Accent),
            Header = new PanelHeader($" [{Palette.Hex(Palette.Accent)}]run this yourself[/] "),
            Padding = new Padding(1, 0, 1, 0)
        });

        // Said every time, because the reason is a rule and not an implementation gap.
        session.Note($"{CliConsts.BinaryName} never starts a deploy: it stops production for the swap, and that is your call.");
        return 0;
    }

    private static void Report(Session session, List<Check> checks, string liveVersion, string localVersion)
    {
        // "v0.11.0 → v0.11.0" is not a deploy, it is a rebuild, and reading it as an upgrade is
        // exactly the confusion a version pair in the header is here to prevent.
        var versions = liveVersion == localVersion
            ? $"[{Palette.Hex(Palette.Ok)}]v{localVersion}[/] [grey]already live - this would ship the same version[/]"
            : $"[{Palette.Hex(Palette.Warn)}]{(liveVersion.Length > 0 ? "v" + liveVersion : "down")}[/] " +
              $"[grey]{session.Glyphs.Arrow}[/] [{Palette.Hex(Palette.Accent)}]v{localVersion}[/]";

        session.Console.Write(Ui.Rule(session.Glyphs,
            $"[grey]deploy preflight[/]  {versions}", session.Console.Profile.Width));

        foreach (var check in checks)
        {
            var (colour, mark) = check.Ok
                ? (Palette.Ok, session.Glyphs.Ok)
                : check.Fatal
                    ? (Palette.Danger, session.Glyphs.Bad)
                    : (Palette.Warn, session.Glyphs.Warn);

            session.Console.MarkupLine(
                $"  [{Palette.Hex(colour)}]{mark}[/] [grey]{check.Label,-8}[/] {Markup.Escape(check.Detail)}");
        }
    }

    // Where the LIVE tag points, and whether it still tells the truth (ADR-118 decision 12).
    //
    // "Only one commit carries LIVE" is free — a tag name resolves to exactly one object, so the
    // tag moves instead of accumulating. What is not free is the tag being stale, and the only way
    // to notice is to ask production what it is running and compare. Never fatal: a wrong tag is
    // bookkeeping, and the deploy this check would block is the thing that corrects it.
    private static async Task<Check> LiveTagCheck(Session session, string liveVersion, CancellationToken cancellationToken)
    {
        var live = (await Git(session, "rev-parse -q --verify refs/tags/LIVE^{commit}", cancellationToken)).Trim();
        if (live.Length == 0)
            return new Check(true, false, "LIVE", "no tag yet - a successful deploy will create it");

        var head = (await Git(session, "rev-parse -q --verify HEAD", cancellationToken)).Trim();
        var shortSha = live[..Math.Min(7, live.Length)];
        var tagged = await VersionAt(session, live, cancellationToken);

        if (live == head)
            return new Check(true, false, "LIVE", $"already on HEAD ({shortSha})");

        if (liveVersion.Length > 0 && tagged.Length > 0 && tagged != liveVersion)
            return new Check(false, false, "LIVE",
                $"{shortSha} says v{tagged}, production answers v{liveVersion} - stale, this deploy corrects it");

        return new Check(true, false, "LIVE", shortSha + (tagged.Length > 0 ? $" (v{tagged})" : ""));
    }

    // What CurrentVersion was at a commit — the version the tag claims is running.
    private static async Task<string> VersionAt(Session session, string commit, CancellationToken cancellationToken)
    {
        var consts = await Git(session, $"show {commit}:CedarClerk.Core/Consts.cs", cancellationToken);
        var match = System.Text.RegularExpressions.Regex.Match(consts, "CurrentVersion = \"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string CommandLine(Session session, DeploySettings settings)
    {
        var parts = new List<string> { ".\\Scripts\\deploy.ps1" };
        if (settings.SkipBuild) parts.Add("-SkipBuild");
        if (settings.Desktop) parts.Add("-Desktop");
        return string.Join(' ', parts);
    }

    private static async Task<string> Git(Session session, string arguments, CancellationToken cancellationToken)
    {
        // Read-only git, and it runs even under --dry-run: DryRunCommandRunner returns empty, which
        // shows up as a failed check rather than as a false clean bill of health.
        var result = await session.Runner.RunLocalAsync("git", $"-C \"{session.Config.RepoRoot}\" {arguments}", cancellationToken);
        return result.StdOut;
    }

    private static string DesktopVersion(Session session)
    {
        var path = Path.Combine(session.Config.RepoRoot, "CedarClerk.Desktop", "package.json");
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("version", out var value) ? value.GetString() ?? "?" : "?";
        }
        catch (Exception)
        {
            return "unreadable";
        }
    }
}
