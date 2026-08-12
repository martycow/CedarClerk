using System.Text.RegularExpressions;
using CedarClerk.Cli.Execution;

namespace CedarClerk.Cli.Pipelines;

// Scripts/_git-guard.ps1, moved into C# (ADR-119 decision 6). It enforces Marty's branch rule
// (CLAUDE.md): master holds the latest stable version, every commit on it carries its version tag,
// and deploys run from master only. These methods answer and never exit — what a failed check costs
// belongs to the pipeline that asked, since "refused" and "warned" depend on which check it was.
public sealed class GitGuard
{
    private static readonly Regex VersionLiteral =
        new(@"CurrentVersion\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    private readonly ICommandRunner _runner;
    private readonly string _repoRoot;

    public GitGuard(ICommandRunner runner, string repoRoot)
    {
        _runner = runner;
        _repoRoot = repoRoot;
    }

    // Read-only git still runs under --dry-run, where the runner answers empty. That surfaces as a
    // failed check rather than as a clean bill of health invented out of nothing.
    public async Task<string> RunAsync(string arguments, CancellationToken ct)
    {
        var result = await _runner.RunLocalAsync("git", $"-C \"{_repoRoot}\" {arguments}", ct);
        return result.StdOut.Trim();
    }

    public Task<string> BranchAsync(CancellationToken ct) =>
        RunAsync("rev-parse --abbrev-ref HEAD", ct);

    public async Task<IReadOnlyList<string>> DirtyAsync(CancellationToken ct)
    {
        var porcelain = await RunAsync("status --porcelain", ct);
        return porcelain.Length == 0
            ? Array.Empty<string>()
            : porcelain.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    }

    public async Task<IReadOnlyList<string>> TagsAtHeadAsync(CancellationToken ct)
    {
        var tags = await RunAsync("tag --points-at HEAD", ct);
        return tags.Length == 0
            ? Array.Empty<string>()
            : tags.Split('\n').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
    }

    public async Task<string> HeadAsync(CancellationToken ct) =>
        await RunAsync("rev-parse -q --verify HEAD", ct);

    // Quoted, because the argument string is split on spaces before git ever sees it — an unquoted
    // "--format=%h %s" reaches git as two arguments and prints only the hash.
    public async Task<string> SubjectAsync(CancellationToken ct) =>
        await RunAsync("log -1 --format=\"%h %s\"", ct);

    // The commit a tag points at, or empty when the tag does not exist.
    public Task<string> TagCommitAsync(string tag, CancellationToken ct) =>
        RunAsync($"rev-parse -q --verify refs/tags/{tag}^{{commit}}", ct);

    // CurrentVersion as it was at a given commit — what the tag claims is running.
    public async Task<string> VersionAtAsync(string commit, CancellationToken ct)
    {
        var text = await RunAsync($"show {commit}:CedarClerk.Core/Consts.cs", ct);
        var match = VersionLiteral.Match(text);
        return match.Success ? match.Groups[1].Value : "";
    }

    public sealed record LiveMove(string Commit, string? Previous);

    // Moves LIVE onto HEAD, keeping the tag it replaces as LIVE-PREV (ADR-118 decision 12).
    //
    // Called only after production has answered with the new version. Tagging earlier would let LIVE
    // name a commit that never finished shipping, which is worse than no tag: a wrong answer to
    // "what is running" gets acted on, a missing one gets investigated.
    //
    // Never fatal. By the time this runs the site is already live, and failing a finished deploy over
    // bookkeeping would be the tail wagging the dog.
    public async Task<LiveMove?> SetLiveAsync(CancellationToken ct)
    {
        var head = await HeadAsync(ct);
        if (head.Length == 0) return null;

        var previous = await TagCommitAsync("LIVE", ct);

        // The outgoing LIVE becomes LIVE-PREV even when it is the same commit as HEAD: re-deploying
        // the same commit makes the server's app.prev that commit too, and a LIVE-PREV pointing
        // further back would describe a release the server can no longer roll back to.
        if (previous.Length > 0)
            await RunAsync($"tag -f LIVE-PREV {previous}", ct);
        else
            // No LIVE means nothing here knows what the server is replacing, so any LIVE-PREV lying
            // around is a guess. Removing it makes a later rollback say "I do not know" instead.
            await RunAsync("tag -d LIVE-PREV", ct);

        await RunAsync($"tag -f LIVE {head}", ct);

        return new LiveMove(Short(head), previous.Length > 0 ? Short(previous) : null);
    }

    // The mirror image, for -Rollback: the server moves app.prev into app and keeps nothing behind
    // it, so LIVE-PREV is deleted rather than chained. With nothing to return to, LIVE is removed
    // outright — after that kind of rollback the running commit is genuinely unknown here, and no tag
    // is the only honest way to say so.
    public async Task<string?> RestoreLiveAsync(CancellationToken ct)
    {
        var previous = await TagCommitAsync("LIVE-PREV", ct);
        if (previous.Length > 0)
        {
            await RunAsync($"tag -f LIVE {previous}", ct);
            await RunAsync("tag -d LIVE-PREV", ct);
            return Short(previous);
        }

        await RunAsync("tag -d LIVE", ct);
        return null;
    }

    private static string Short(string sha) => sha[..Math.Min(7, sha.Length)];

    // CurrentVersion out of Consts.cs — the single place the version lives (CLAUDE.md). Read off
    // disk rather than from the CLI's own reference to Core, because a deploy describes the working
    // copy in front of you, not the assembly this tool happened to be built against.
    public static string VersionOnDisk(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "CedarClerk.Core", "Consts.cs");
        try
        {
            var match = VersionLiteral.Match(File.ReadAllText(path));
            if (match.Success) return match.Groups[1].Value;
        }
        catch (Exception)
        {
            // Falls through to the compiled-in value, which is right far more often than it is wrong.
        }
        return CedarClerk.Core.Consts.CurrentVersion;
    }
}
