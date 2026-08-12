using System.Globalization;
using System.Security.Cryptography;
using CedarClerk.Cli.Execution;

namespace CedarClerk.Cli.Pipelines;

// One release, packed into one file.
//
// tar stays an external program (ADR-119 decision 4). .NET 8 ships System.Formats.Tar and the
// temptation to drop the dependency was real, but a Windows file has no unix mode for TarWriter to
// copy, so the server would unpack a tree of 000-permission files — a deploy that passes every check
// and then does not start. Changing the packer in the same session as the orchestrator is also the
// exact mistake ADR-118 spent a stage avoiding. The bytes on the wire are the same as yesterday's.
public static class Tarball
{
    public sealed record Packed(string Path, long Bytes, string Sha256, bool Reused, int FileCount, long SourceBytes);

    // Reusing a byte-identical archive is what makes a resumed upload possible at all: the half-file
    // on the server is a valid prefix only if this side did not repack in between, and gzip stamps a
    // timestamp, so repacking the same input still produces different bytes.
    public static string Signature(IReadOnlyList<FileInfo> files)
    {
        var bytes = files.Sum(f => f.Length);
        var newest = files.Count == 0
            ? DateTime.UnixEpoch
            : files.Max(f => f.LastWriteTimeUtc);
        return $"{files.Count}|{bytes}|{newest.ToString("o", CultureInfo.InvariantCulture)}";
    }

    public static IReadOnlyList<FileInfo> Contents(string directory) =>
        new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories);

    public static async Task<Packed> PackAsync(
        ICommandRunner runner,
        string publishDir,
        string cacheDir,
        string version,
        Action<string> onNote,
        CancellationToken ct)
    {
        Directory.CreateDirectory(cacheDir);

        var files = Contents(publishDir);
        var signature = Signature(files);
        var tarPath = Path.Combine(cacheDir, $"cedar-{version}.tar.gz");
        var metaPath = tarPath + ".meta";

        var reused = File.Exists(tarPath)
                     && File.Exists(metaPath)
                     && File.ReadAllText(metaPath).Trim() == signature;

        if (reused)
        {
            onNote($"reusing the artifact packed at {File.GetLastWriteTime(tarPath):HH:mm} - an interrupted upload can continue");
        }
        else
        {
            if (File.Exists(tarPath)) File.Delete(tarPath);

            var result = await runner.RunLocalAsync("tar", await FlagsAsync(runner, tarPath, publishDir, ct), ct);
            if (!result.Ok)
                throw new PipelineStop("Packing the tarball failed.",
                    state: result.StdErr.Split('\n').Where(l => l.Trim().Length > 0).Take(6),
                    hints: new[] { "tar is at C:\\Windows\\System32\\tar.exe on Windows 10/11; Git for Windows ships one too." });

            File.WriteAllText(metaPath, signature);
        }

        var info = new FileInfo(tarPath);
        return new Packed(tarPath, info.Length, await Sha256Async(tarPath, ct), reused,
            files.Count, files.Sum(f => f.Length));
    }

    // GNU tar reads "C:\..." as host "C", path "\..." — bsdtar, which is what System32\tar.exe is,
    // does not. Asking which one is on PATH is cheaper than being wrong on somebody else's machine.
    private static async Task<string> FlagsAsync(ICommandRunner runner, string tarPath, string publishDir, CancellationToken ct)
    {
        var banner = await runner.RunLocalAsync("tar", "--version", ct);
        var forceLocal = banner.StdOut.Contains("GNU tar", StringComparison.OrdinalIgnoreCase) ? "--force-local " : "";
        return $"{forceLocal}-czf \"{tarPath}\" -C \"{publishDir}\" .";
    }

    public static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
