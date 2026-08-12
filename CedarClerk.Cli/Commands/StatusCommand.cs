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

        session.Console.Write(new StatusView(session.Glyphs, session.Config.Host).Render(snapshot, now, session.Console.Profile.Width));
        return snapshot.Reachable ? 0 : 1;
    }
}
