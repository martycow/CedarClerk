using System.Text.RegularExpressions;
using CedarClerk.Cli.Execution;

namespace CedarClerk.Cli.Pipelines;

// The far side of a transfer: how big is the file that is already there, does its checksum match,
// and the retry-and-resume loop that gets it the rest of the way.
//
// deploy.ps1 kept two copies of that loop on purpose — one for the release tarball, one for the
// desktop installer — so that a change to the newer feature could not reach the path production
// rides on. Collapsed to one here (ADR-119), and the insurance it replaced is a test: the loop is
// now exercised directly, including the resume, the over-long file and the exhausted retries, which
// the two PowerShell copies never were.
public sealed class RemoteFiles
{
    private static readonly Regex Digits = new(@"^\d+$", RegexOptions.Compiled);

    private readonly ICommandRunner _runner;

    public RemoteFiles(ICommandRunner runner) => _runner = runner;

    // -1 means the question could not be asked — ssh itself failed. That is different from "the file
    // is not there", which is 0, and the caller has to be able to tell them apart: one is a lost
    // connection, the other is a fresh upload.
    public async Task<long> SizeAsync(string path, CancellationToken ct)
    {
        var result = await _runner.RunRemoteAsync($"stat -c %s '{path}' 2>/dev/null || echo 0", ct);
        if (!result.Ok) return -1;

        var digits = result.Lines.Select(l => l.Trim()).LastOrDefault(l => Digits.IsMatch(l));
        return digits is not null && long.TryParse(digits, out var value) ? value : -1;
    }

    public async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        var result = await _runner.RunRemoteAsync($"sha256sum '{path}' | cut -d' ' -f1", ct);
        return result.StdOut.Trim().ToLowerInvariant();
    }

    public Task DeleteAsync(string path, CancellationToken ct) =>
        _runner.RunRemoteAsync($"rm -f '{path}'", ct);

    public sealed record SendReport(bool Ok, long Sent, long Total, int Attempts);

    public async Task<SendReport> SendAsync(
        string localPath,
        string remotePath,
        int retries,
        bool fresh,
        Action<long, long> onProgress,
        Action<string> onNote,
        Action<string> onWarn,
        CancellationToken ct)
    {
        var total = new FileInfo(localPath).Length;

        // A small file whose contents change under a fixed name (latest.yml) must never be resumed:
        // the bytes already there are a different manifest of the same length, and appending nothing
        // to it would look exactly like success.
        if (fresh) await DeleteAsync(remotePath, ct);

        var attempt = 0;
        var last = 0L;

        while (attempt < retries)
        {
            attempt++;

            var offset = await SizeAsync(remotePath, ct);
            if (offset < 0) return new SendReport(false, last, total, attempt);

            if (offset > total)
            {
                onWarn("the file on the server is longer than the one being sent - starting it over");
                await DeleteAsync(remotePath, ct);
                offset = 0;
            }

            if (offset == total) return new SendReport(true, total, total, attempt);

            if (offset > 0)
                onNote($"resuming at {Rendering.Format.Size(offset)} ({100 * offset / Math.Max(1, total)}% was already there)");
            else if (attempt > 1)
                onNote("starting over from the beginning");

            onProgress(offset, total);
            await _runner.StreamFileToRemoteAsync(
                localPath, offset, $"cat >> '{remotePath}'", sent => onProgress(sent, total), ct);

            // The size on the far side is the verdict, never the exit code of our own ssh: a pipe
            // that closed cleanly after half a file also exits zero.
            last = await SizeAsync(remotePath, ct);
            if (last == total)
            {
                onProgress(total, total);
                return new SendReport(true, total, total, attempt);
            }

            if (attempt < retries)
            {
                var wait = Math.Min(20, 3 * attempt);
                onWarn($"the connection dropped - retrying in {wait}s (attempt {attempt} of {retries})");
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            }
        }

        return new SendReport(false, Math.Max(last, 0), total, attempt);
    }

    // Probe scripts answer in KEY=value lines, because one round trip that returns six facts beats
    // six round trips over a link this whole file exists to distrust.
    public static string Field(string output, string key)
    {
        var match = Regex.Match(output, $@"{Regex.Escape(key)}=(\S*)");
        return match.Success ? match.Groups[1].Value : "";
    }

    public static long Number(string output, string key) =>
        long.TryParse(Field(output, key), out var value) ? value : 0;
}
