using System.ComponentModel;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

public class StatusSettings : CedarSettings
{
    [CommandOption("--hours <HOURS>")]
    [Description("How much history the graphs show. sysstat samples every 10 minutes.")]
    public int Hours { get; init; } = CliConsts.DefaultHistoryHours;

    [CommandOption("-l|--legend")]
    [Description("Print the glossary of the abbreviations underneath the dashboard.")]
    public bool Legend { get; init; }
}

public sealed class StatusCommand : AsyncCommand<StatusSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, StatusSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(StatusSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        var now = DateTimeOffset.UtcNow;

        var snapshot = await session.Console
            .Status()
            .Spinner(session.Glyphs.Spinner)
            .SpinnerStyle(new Style(Palette.Accent))
            .StartAsync("reading the droplet…", _ => session.Probe.TakeAsync(settings.Hours, cancellationToken));

        if (session.Json)
        {
            session.WriteJson(snapshot);
            return snapshot.Reachable ? 0 : 1;
        }

        var width = session.Console.Profile.Width;
        session.Console.Write(new StatusView(session.Glyphs, session.Config.Host).Render(snapshot, now, width));

        // The glossary sits under the dashboard when asked for, and otherwise leaves one grey line
        // saying where it is. Printing it every time would push the numbers off a short screen.
        session.Console.Write(settings.Legend
            ? LegendView.Render(session.Glyphs, width)
            : LegendView.Hint(session.Glyphs));

        return snapshot.Reachable ? 0 : 1;
    }
}
