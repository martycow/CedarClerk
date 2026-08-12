using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// The one destructive command this stage ships.
//
// It exists because the droplet's sudoers grants NOPASSWD for exactly
// `systemctl start|stop|restart cedarclerk` and nothing else — the capability was already drawn by
// the server, so exposing it here widens nothing. It still shows what is running, asks with the
// default set to no, and checks health afterwards, because a restart drops every in-flight request
// and takes the blog with it (one process serves both).
public sealed class RestartCommand : AsyncCommand<CedarSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, CedarSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(CedarSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        var now = DateTimeOffset.UtcNow;

        var snapshot = await session.Console
            .Status()
            .Spinner(session.Glyphs.Spinner)
            .SpinnerStyle(new Style(Palette.Accent))
            .StartAsync("checking what is running…", _ => session.Probe.TakeAsync(1, cancellationToken));

        if (!snapshot.Reachable && !session.DryRun)
        {
            session.Problem($"cannot reach {session.Config.Host} - nothing was touched.");
            return 1;
        }

        session.Console.Write(new Panel(new Markup(string.Join('\n', new[]
        {
            $"[grey]service[/]  {Markup.Escape(snapshot.Service.ActiveState)} {Markup.Escape(snapshot.Service.SubState)}, " +
            $"up {Markup.Escape(Format.Age(snapshot.Service.SinceUtc, now).Replace(" ago", ""))}",
            $"[grey]version[/]  {(snapshot.Health.Answered ? "v" + snapshot.Health.Version : "not answering")}",
            $"[{Palette.Hex(Palette.Warn)}]{session.Glyphs.Warn} a restart drops every in-flight request, and the blog with it[/]",
            "[grey]  (the same Kestrel process serves cedarclerk.mooexe.dev and blog.mooexe.dev)[/]"
        })))
        {
            Border = session.Glyphs.Border,
            BorderStyle = new Style(Palette.Warn),
            Header = new PanelHeader($" [{Palette.Hex(Palette.Warn)}]restart {CliConsts.ServiceName}[/] "),
            Padding = new Padding(1, 0, 1, 0)
        });

        if (!session.Confirm($"Restart {CliConsts.ServiceName} on {session.Config.Host}?"))
        {
            session.Note("nothing was done.");
            return 1;
        }

        var result = await session.Runner.RunRemoteAsync(
            $"sudo systemctl restart {CliConsts.ServiceName} && systemctl is-active {CliConsts.ServiceName}",
            cancellationToken);

        if (!result.Ok && !session.DryRun)
        {
            session.Problem("the restart command failed.");
            session.Note(result.StdErr.Trim().Length > 0 ? result.StdErr.Trim() : result.StdOut.Trim());
            return 1;
        }

        if (session.DryRun) return 0;

        var health = await session.Console
            .Status()
            .Spinner(session.Glyphs.Spinner)
            .SpinnerStyle(new Style(Palette.Accent))
            .StartAsync("waiting for it to answer…", async _ =>
            {
                // Up to two minutes: a start that applies an EF migration has taken ~40s before now,
                // the same window deploy.ps1 allows for the same reason.
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    var report = await session.Health.GetAsync(session.Config.HealthUrl, cancellationToken);
                    if (report.Answered) return report;
                    await Task.Delay(3000);
                }
                return Parsing.HealthReport.Down;
            });

        if (!health.Answered)
        {
            session.Problem("restarted, but the server is not answering.");
            session.Note($"ssh {session.Config.Host} \"systemctl status {CliConsts.ServiceName}\"");
            return 1;
        }

        session.Console.MarkupLine(
            $"[{Palette.Hex(Palette.Ok)}]{session.Glyphs.Ok} back up[/] [grey]- v{health.Version}[/]");
        return 0;
    }
}
