using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// The setup wizard, and the same screen that runs on a first launch.
//
// It stores paths and host names only. Nothing here is or becomes a secret: authentication is
// whatever ssh already does, which is why the config file can sit in %APPDATA% unencrypted and be
// pasted into a bug report without a second thought (ADR-118).
public sealed class ConfigCommand : AsyncCommand<CedarSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, CedarSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(CedarSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        var config = session.Config;

        if (session.Json)
        {
            session.WriteJson(new { path = ConfigStore.Path_, config });
            return 0;
        }

        session.Console.Write(Ui.Rule(session.Glyphs,
            $"[grey]{Markup.Escape(ConfigStore.Path_)}[/]", session.Console.Profile.Width));

        config.Host = session.Console.Prompt(
            new TextPrompt<string>("ssh [grey]user@host[/]").DefaultValue(config.Host));

        config.IdentityFile = session.Console.Prompt(
            new TextPrompt<string>("ssh key [grey](blank = let ssh decide)[/]")
                .DefaultValue(config.IdentityFile)
                .AllowEmpty()
                .Validate(path => path.Length == 0 || File.Exists(path)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]no file there[/]")));

        config.RepoRoot = session.Console.Prompt(
            new TextPrompt<string>("repository")
                .DefaultValue(config.RepoRoot.Length > 0 ? config.RepoRoot : ConfigStore.FindRepoRoot() ?? "")
                // The solution file, not a script: since ADR-119 the pipelines are the tool, so what
                // a configured repository has to contain is the repository itself.
                .Validate(path => File.Exists(Path.Combine(path, "CedarClerk.sln"))
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]no CedarClerk.sln under that path[/]")));

        config.RemoteRoot = session.Console.Prompt(
            new TextPrompt<string>("remote root").DefaultValue(config.RemoteRoot));

        config.HealthUrl = session.Console.Prompt(
            new TextPrompt<string>("health URL")
                .DefaultValue(config.HealthUrl)
                .Validate(url => Uri.TryCreate(url, UriKind.Absolute, out _)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]not a URL[/]")));

        ConfigStore.Save(config);
        session.Console.MarkupLine($"[{Palette.Hex(Palette.Ok)}]{session.Glyphs.Ok} saved[/] [grey]{Markup.Escape(ConfigStore.Path_)}[/]");

        // Verified rather than assumed: a wizard that accepts an unreachable host has told you
        // nothing, and the failure would surface later as a confusing "the server did not answer".
        if (session.Console.Confirm("Check the connection now?"))
        {
            var probe = await session.Console
                .Status()
                .Spinner(session.Glyphs.Spinner)
                .SpinnerStyle(new Style(Palette.Accent))
                .StartAsync($"ssh {config.Host}…",
                    _ => session.Runner.RunRemoteAsync("echo ok; hostname", cancellationToken));

            if (probe.Ok && probe.StdOut.Contains("ok"))
                session.Console.MarkupLine(
                    $"[{Palette.Hex(Palette.Ok)}]{session.Glyphs.Ok} reachable[/] [grey]{Markup.Escape(probe.StdOut.Split('\n').Last(l => l.Trim().Length > 0).Trim())}[/]");
            else
            {
                session.Problem("could not reach it.");
                session.Note(probe.StdErr.Trim().Length > 0 ? probe.StdErr.Trim() : probe.StdOut.Trim());
                return 1;
            }
        }

        return 0;
    }
}
