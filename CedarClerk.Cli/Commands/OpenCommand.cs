using System.ComponentModel;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

public sealed class OpenSettings : CedarSettings
{
    [CommandArgument(0, "[where]")]
    [Description("browser, desktop, blog or local.")]
    public string Where { get; init; } = "browser";
}

// Opening the product itself, from the console that manages it (Marty, 12.08.2026).
//
// It is a small thing and it closes a real gap: everything else here answers questions about Cedar
// Clerk - is it up, what is it running, what do the logs say - and the obvious next move, looking at
// it, meant leaving for a browser and typing a URL from memory. Four destinations, one of which is
// deliberately not production.
//
// The desktop shell is a different question from the site, because there are two of it: an installed
// copy, and the working copy in this repository. Installed first - that is the one Marty actually
// uses - and the repository build as the fallback, which is the one that exists five minutes after a
// change. Neither is guessed at silently: what was opened is printed.
public sealed class OpenCommand : AsyncCommand<OpenSettings>
{
    private const string LocalUrl = "http://localhost:8080";

    protected override Task<int> ExecuteAsync(CommandContext context, OpenSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    public static Task<int> RunAsync(CedarSettings settings, CancellationToken cancellationToken) =>
        RunAsync(new OpenSettings
        {
            DryRun = settings.DryRun, Json = settings.Json, NoUnicode = settings.NoUnicode,
            AssumeYes = settings.AssumeYes, NoLogo = settings.NoLogo, Where = "browser"
        }, cancellationToken);

    public static async Task<int> RunAsync(OpenSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);

        return settings.Where.Trim().ToLowerInvariant() switch
        {
            "desktop" or "app" or "shell" => await DesktopAsync(session, cancellationToken),
            "blog" => await UrlAsync(session, BlogUrl(session.Config), "the blog", cancellationToken),
            "local" or "dev" => await UrlAsync(session, LocalUrl, "the local dev server", cancellationToken),
            _ => await UrlAsync(session, session.Config.PublicBaseUrl, "production", cancellationToken)
        };
    }

    // blog.<domain>, derived rather than stored: the blog is host-routed inside the same Kestrel
    // process, so it is always the same domain with one label in front (Program.cs MapWhen).
    public static string BlogUrl(CliConfig config)
    {
        try
        {
            var uri = new Uri(config.PublicBaseUrl);
            var host = uri.Host.StartsWith("blog.", StringComparison.OrdinalIgnoreCase) ? uri.Host : "blog." + uri.Host;
            return $"{uri.Scheme}://{host}";
        }
        catch (UriFormatException)
        {
            return config.PublicBaseUrl;
        }
    }

    private static async Task<int> UrlAsync(Session session, string url, string what, CancellationToken cancellationToken)
    {
        session.Console.MarkupLine(
            $"[{Palette.Hex(Palette.Accent)}]{session.Glyphs.Arrow}[/] [grey]opening {what}:[/] " +
            $"[{Palette.Hex(Palette.Text)}]{Markup.Escape(url)}[/]");

        // The URL is the executable as far as ShellExecute is concerned, which is what makes this the
        // default browser rather than a browser this tool had to be told about.
        var result = await session.Runner.LaunchDetachedAsync(url, "", null, cancellationToken);
        if (result.Ok) return 0;

        session.Problem($"could not open it: {result.StdErr.Trim()}");
        return 1;
    }

    private static async Task<int> DesktopAsync(Session session, CancellationToken cancellationToken)
    {
        var installed = InstalledShell();
        if (installed is not null)
        {
            session.Console.MarkupLine(
                $"[{Palette.Hex(Palette.Accent)}]{session.Glyphs.Arrow}[/] [grey]opening the installed shell:[/] " +
                $"[{Palette.Hex(Palette.Muted)}]{Markup.Escape(installed)}[/]");
            var opened = await session.Runner.LaunchDetachedAsync(installed, "", Path.GetDirectoryName(installed), cancellationToken);
            if (opened.Ok) return 0;
            session.Note("the installed copy would not start - falling back to the working copy.");
        }

        if (!session.RequireRepo()) return 2;

        // npm start runs start.js, which is what knows how to launch Electron against the sidecar.
        // Anything else here would be a second, quietly diverging copy of that knowledge.
        if (!Directory.Exists(Path.Combine(session.Config.DesktopDir, "node_modules")))
        {
            session.Problem("the desktop shell has not been built in this working copy.");
            session.Note($"{CliConsts.BinaryName} build      # Angular, server and the shell");
            return 1;
        }

        session.Console.MarkupLine(
            $"[{Palette.Hex(Palette.Accent)}]{session.Glyphs.Arrow}[/] [grey]npm start in[/] " +
            $"[{Palette.Hex(Palette.Muted)}]{Markup.Escape(session.Config.DesktopDir)}[/]");

        var shell = Shell.PowerShell();
        var result = await session.Runner.LaunchDetachedAsync(
            shell,
            $"-NoExit -Command \"Set-Location '{session.Config.DesktopDir.Replace("'", "''")}'; npm start\"",
            session.Config.DesktopDir,
            cancellationToken);

        if (result.Ok) return 0;
        session.Problem($"could not start it: {result.StdErr.Trim()}");
        return 1;
    }

    // electron-builder's nsis target installs under the product name, per user by default. Both
    // locations are checked because a per-machine install is one config line away and would
    // otherwise look like "not installed".
    public static string? InstalledShell()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } local
                ? Path.Combine(local, "Programs")
                : "",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var root in roots.Where(r => r.Length > 0))
        {
            var candidate = Path.Combine(root, CliConsts.DisplayName, CliConsts.DisplayName + ".exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
