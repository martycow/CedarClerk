using System.ComponentModel;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// The four flags every command carries. They are defined once and inherited so that a new command
// cannot accidentally ship without --dry-run — which, on a tool that can restart production, is the
// flag it would be worst to forget.
public class CedarSettings : CommandSettings
{
    [CommandOption("--dry-run")]
    [Description("Print every command that would run, change nothing, exit 0.")]
    public bool DryRun { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Answer every confirmation with yes. For scripts; never the default.")]
    public bool AssumeYes { get; init; }

    [CommandOption("--no-unicode")]
    [Description("ASCII glyphs and graphs, for terminals without Unicode coverage.")]
    public bool NoUnicode { get; init; }

    [CommandOption("--json")]
    [Description("Machine-readable output instead of graphics.")]
    public bool Json { get; init; }

    [CommandOption("--no-logo")]
    [Description("Skip the splash screen.")]
    public bool NoLogo { get; init; }

    // Added by ADR-119 so the shim scripts can still honour deploy.ps1's -CloudHost. It overrides the
    // stored config for one run and is never written back: a flag is an exception, and an exception
    // that quietly becomes the new default is how you end up deploying to the wrong machine twice.
    [CommandOption("--host")]
    [Description("Override the configured ssh host for this run only.")]
    public string Host { get; init; } = "";
}
