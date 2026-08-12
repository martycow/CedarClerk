using CedarClerk.Core;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Server.Modules.Agent;

/// <summary>
/// One file as the disk describes it (ADR-117). Deliberately not <see cref="AssetEntry"/>: this
/// crosses a process boundary and then a network one, and it carries no ids, no owner and no
/// project — the cloud decides all three. The agent's job ends at "here is what is in the folder".
/// </summary>
public record ScannedFile
{
    /// <summary>Root-relative, '/'-separated — the same string on Windows and everywhere else.</summary>
    public required string RelativePath { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required string Kind { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime ModifiedAt { get; init; }

    // What the file's own header said, or null where it said nothing. Read here rather than in the
    // cloud for the obvious reason: the cloud never sees the bytes.
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? DurationMs { get; init; }
    public int? SampleRate { get; init; }
}

/// <summary>
/// Walks a folder and describes what is in it. Extracted from <c>AssetIndexService</c> when the walk
/// moved out of the hosted server and into the desktop agent (ADR-117): the traversal itself is
/// unchanged, it just returns rows now instead of writing them.
///
/// It reads names, sizes, timestamps and headers. It never copies a file and never writes one.
/// </summary>
public static class AssetFolderWalker
{
    /// <summary>
    /// The walk. Hand-rolled rather than <c>Directory.EnumerateFiles(.., AllDirectories)</c>,
    /// because that one throws the moment it meets a folder it cannot open and abandons everything
    /// after it — one permission-denied directory would end the scan. This skips that folder,
    /// counts it, and carries on (ADR-107, refinement 3).
    /// </summary>
    /// <param name="onUnreadable">
    /// Called once per folder the walk could not open. Reported rather than swallowed: "41 files,
    /// 3 folders unreadable" is a different fact from "41 files".
    /// </param>
    public static IEnumerable<string> EnumerateIndexable(
        string rootPath, Action onUnreadable, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (Exception)
            {
                onUnreadable();
                continue;
            }

            foreach (var sub in subdirectories)
            {
                var name = Path.GetFileName(sub);
                // Engine caches and build output hold more files than the project and no authored
                // asset — skipping them is the difference between a scan and an afternoon.
                if (AssetKinds.SkippedDirectories.Contains(name)) continue;
                // A reparse point can point at its own parent; following one walks forever.
                try
                {
                    if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch (Exception) { onUnreadable(); continue; }

                pending.Push(sub);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception)
            {
                onUnreadable();
                continue;
            }

            foreach (var file in files)
                if (AssetKinds.ShouldIndex(file))
                    yield return file;
        }
    }

    /// <summary>
    /// Describes one file, or null if it has gone since the walk saw it — a normal thing to meet in
    /// a folder of tens of thousands, and not an error.
    /// </summary>
    /// <remarks>
    /// The header is read for every file the agent describes, unlike the old local scan which
    /// skipped unchanged ones by comparing <c>MetadataForModifiedAt</c>. The agent has no database
    /// to compare against, so the saving moved rather than vanished: it is the *cloud* that skips
    /// unchanged rows on import.
    /// </remarks>
    public static ScannedFile? Describe(string rootPath, string fullPath)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(fullPath);
            if (!info.Exists) return null;
        }
        catch (Exception)
        {
            return null;
        }

        var kind = AssetKinds.FromPath(fullPath);
        var header = AssetMetadata.TryRead(fullPath, kind);

        return new ScannedFile
        {
            RelativePath = Relative(rootPath, fullPath),
            FileName = Path.GetFileName(fullPath),
            Extension = AssetKinds.ExtensionOf(fullPath),
            Kind = kind,
            SizeBytes = info.Length,
            ModifiedAt = info.LastWriteTimeUtc,
            Width = header?.Width,
            Height = header?.Height,
            DurationMs = header?.DurationMs,
            SampleRate = header?.SampleRate,
        };
    }

    /// <summary>Root-relative, '/'-separated — the same string on Windows and everywhere else.</summary>
    public static string Relative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');
}
