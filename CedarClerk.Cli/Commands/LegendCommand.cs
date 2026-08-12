using CedarClerk.Cli.Rendering;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// The glossary on its own. It reads nothing and reaches nowhere - which is why it is the one command
// that works with no configuration, no server and no network.
public sealed class LegendCommand : AsyncCommand<CedarSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, CedarSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    public static Task<int> RunAsync(CedarSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        session.Console.Write(LegendView.Render(session.Glyphs, session.Console.Profile.Width));
        return Task.FromResult(0);
    }
}
