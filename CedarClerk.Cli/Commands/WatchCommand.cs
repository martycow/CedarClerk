using System.ComponentModel;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Commands;

public sealed class WatchSettings : StatusSettings
{
    [CommandOption("-i|--interval <SECONDS>")]
    [Description("Seconds between refreshes.")]
    public int Interval { get; init; } = 20;
}

// The status screen inside Live. It is the same StatusView, so the two can never disagree.
public sealed class WatchCommand : AsyncCommand<WatchSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, WatchSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(WatchSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        if (session.Json)
        {
            session.Problem("watch is a live screen; use 'status --json' for machine-readable output");
            return 2;
        }

        var view = new StatusView(session.Glyphs, session.Config.Host);
        using var cancellation = new CancellationTokenSource();

        // Ctrl+C must leave a usable terminal behind: cancel the loop, let Live restore the cursor,
        // and return normally instead of tearing the process down mid-frame.
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.Interval, 3, 3600));
        IRenderable current = new Markup("[grey]reading the droplet…[/]");

        try
        {
            await session.Console.Live(current).StartAsync(async ctx =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var snapshot = await session.Probe.TakeAsync(settings.Hours, cancellation.Token);
                    ctx.UpdateTarget(new Rows(
                        view.Render(snapshot, DateTimeOffset.UtcNow, session.Console.Profile.Width),
                        new Markup($"[grey35]refreshing every {interval.TotalSeconds:0}s · Ctrl+C to stop[/]")));
                    ctx.Refresh();

                    await Task.Delay(interval, cancellation.Token);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // The documented way out of this command, not a failure.
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            session.Console.Cursor.Show();
        }

        session.Note("stopped.");
        return 0;
    }
}
