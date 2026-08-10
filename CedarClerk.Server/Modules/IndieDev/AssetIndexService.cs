using System.Collections.Concurrent;
using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

public enum AssetScanStatus { Counting, Indexing, Completed, Failed, Cancelled }

/// <summary>
/// A scan in flight. In-memory only, and for the same reason <see cref="AiJobService"/>'s jobs are:
/// a scan lost to a restart costs a re-scan, nothing else. Contrast <c>PublishJob</c>, which is a
/// table precisely because a publish lost to a restart may or may not have reached a channel.
/// </summary>
public class AssetScan
{
    public required Guid Id { get; init; }
    public required Guid ProjectId { get; init; }
    public required string OwnerId { get; init; }
    public required string RootPath { get; init; }

    public AssetScanStatus Status { get; set; } = AssetScanStatus.Counting;

    /// <summary>Files worth indexing found by the counting pass. 0 while still counting.</summary>
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Indexed { get; set; }
    public int MarkedMissing { get; set; }

    /// <summary>Folders the walk could not open — reported rather than swallowed.</summary>
    public int Unreadable { get; set; }

    public string? Error { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public CancellationTokenSource Cts { get; } = new();
}

/// <summary>
/// Walks a folder and records what is in it (T-122, ADR-107). It reads names, sizes and timestamps;
/// it never opens a file and never copies one.
/// </summary>
public class AssetIndexService(IServiceScopeFactory scopes, ILogger<AssetIndexService> logger)
{
    /// <summary>One scan per project — a second request while one runs returns the running one.</summary>
    private readonly ConcurrentDictionary<Guid, AssetScan> _byProject = new();

    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);
    private const int BatchSize = 500;

    public AssetScan? Current(Guid projectId) =>
        _byProject.TryGetValue(projectId, out var scan) ? scan : null;

    public bool IsRunning(Guid projectId) =>
        Current(projectId) is { Status: AssetScanStatus.Counting or AssetScanStatus.Indexing };

    public AssetScan Start(Guid projectId, string ownerId, string rootPath)
    {
        Prune();

        if (Current(projectId) is { Status: AssetScanStatus.Counting or AssetScanStatus.Indexing } running)
            return running;

        var scan = new AssetScan { Id = Guid.NewGuid(), ProjectId = projectId, OwnerId = ownerId, RootPath = rootPath };
        _byProject[projectId] = scan;
        _ = RunAsync(scan);
        return scan;
    }

    public bool Cancel(Guid projectId)
    {
        if (!IsRunning(projectId)) return false;
        _byProject[projectId].Cts.Cancel();
        return true;
    }

    private async Task RunAsync(AssetScan scan)
    {
        // Its own scope: the request that started this is long gone by the time the walk finishes,
        // and so is its DbContext.
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var token = scan.Cts.Token;

        try
        {
            // Pass one counts, so the progress bar can say "of N" rather than guess. Cheap next to
            // pass two: this only enumerates names, while indexing stats every file.
            scan.Total = EnumerateIndexable(scan, token).Count();
            token.ThrowIfCancellationRequested();

            scan.Status = AssetScanStatus.Indexing;
            await IndexAsync(db, scan, token);

            await db.Projects
                .Where(p => p.Id == scan.ProjectId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssetsIndexedAt, DateTime.UtcNow), token);

            scan.Status = AssetScanStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            // Everything written before the cancel stays: a half-finished index is still an index,
            // and throwing it away would make cancelling a scan destructive.
            scan.Status = AssetScanStatus.Cancelled;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Asset scan failed for project {ProjectId}", scan.ProjectId);
            scan.Status = AssetScanStatus.Failed;
            scan.Error = e.Message;
        }
        finally
        {
            scan.FinishedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// The walk. Hand-rolled rather than <c>Directory.EnumerateFiles(.., AllDirectories)</c>,
    /// because that one throws the moment it meets a folder it cannot open and abandons everything
    /// after it — one permission-denied directory would end the scan. This skips that folder,
    /// counts it, and carries on.
    /// </summary>
    private static IEnumerable<string> EnumerateIndexable(AssetScan scan, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(scan.RootPath);

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
                scan.Unreadable++;
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
                catch (Exception) { scan.Unreadable++; continue; }

                pending.Push(sub);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception)
            {
                scan.Unreadable++;
                continue;
            }

            foreach (var file in files)
                if (AssetKinds.ShouldIndex(file))
                    yield return file;
        }
    }

    private async Task IndexAsync(CedarDbContext db, AssetScan scan, CancellationToken token)
    {
        // The whole project's index, in memory, keyed by relative path. Tens of thousands of small
        // rows is a few megabytes, and the alternative — a query per file — is a scan that takes
        // minutes instead of seconds.
        var existing = await db.AssetEntries
            .Where(a => a.ProjectId == scan.ProjectId)
            .ToDictionaryAsync(a => a.RelativePath, token);

        var seen = new HashSet<string>(existing.Count, StringComparer.OrdinalIgnoreCase);
        var pendingWrites = 0;

        // The tree is walked twice — once to count, once to index — and both passes tally the
        // folders they could not open. Reset, or the number reported is double the truth.
        scan.Unreadable = 0;

        foreach (var fullPath in EnumerateIndexable(scan, token))
        {
            token.ThrowIfCancellationRequested();
            scan.Processed++;

            FileInfo info;
            try
            {
                info = new FileInfo(fullPath);
                if (!info.Exists) continue;
            }
            catch (Exception)
            {
                // A file that vanished between the two passes. Not an error — just not there.
                continue;
            }

            var relative = Relative(scan.RootPath, fullPath);
            seen.Add(relative);

            if (existing.TryGetValue(relative, out var row))
            {
                var changed = row.ModifiedAt != info.LastWriteTimeUtc || row.SizeBytes != info.Length;
                row.SizeBytes = info.Length;
                row.ModifiedAt = info.LastWriteTimeUtc;
                row.IndexedAt = DateTime.UtcNow;
                // Found again — whatever it was missing from, it is back.
                row.MissingSince = null;
                // T-140 — headers are read only when the file is new or has actually moved. A
                // re-scan of a hundred thousand unchanged files therefore opens none of them, which
                // is the difference between a re-index that takes seconds and one that takes minutes.
                if (changed || row.MetadataForModifiedAt != info.LastWriteTimeUtc)
                    ReadMetadata(row, fullPath);
                // A changed file's cached thumbnail is of the old bytes; drop it and let the next
                // request make a new one.
                if (changed) row.ThumbnailForModifiedAt = null;
            }
            else
            {
                var added = new AssetEntry
                {
                    OwnerId = scan.OwnerId,
                    ProjectId = scan.ProjectId,
                    RelativePath = relative,
                    FileName = Path.GetFileName(fullPath),
                    Extension = AssetKinds.ExtensionOf(fullPath),
                    Kind = AssetKinds.FromPath(fullPath),
                    SizeBytes = info.Length,
                    ModifiedAt = info.LastWriteTimeUtc,
                };
                ReadMetadata(added, fullPath);
                db.AssetEntries.Add(added);
                scan.Indexed++;
            }

            if (++pendingWrites >= BatchSize)
            {
                await db.SaveChangesAsync(token);
                pendingWrites = 0;
            }
        }

        if (pendingWrites > 0) await db.SaveChangesAsync(token);

        // Anything the walk did not reach is MARKED, never deleted (ADR-107): an unplugged external
        // drive must read as "not found at path", not as "these files never existed".
        var now = DateTime.UtcNow;
        foreach (var (relative, row) in existing)
        {
            if (seen.Contains(relative) || row.MissingSince is not null) continue;
            row.MissingSince = now;
            scan.MarkedMissing++;
        }
        if (scan.MarkedMissing > 0) await db.SaveChangesAsync(token);
    }

    private static void ReadMetadata(AssetEntry row, string fullPath)
    {
        AssetMetadata.TryRead(fullPath, row.Kind, row);
        // Stamped whether or not anything was learned: a file whose header says nothing must not be
        // reopened on every single scan for the rest of its life.
        row.MetadataForModifiedAt = row.ModifiedAt;
    }

    /// <summary>Root-relative, '/'-separated — the same string on Windows and everywhere else.</summary>
    private static string Relative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var (projectId, scan) in _byProject)
            if (scan.FinishedAt is { } finished && finished < cutoff)
                _byProject.TryRemove(projectId, out _);
    }
}
