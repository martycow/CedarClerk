using System.Collections.Concurrent;

namespace CedarClerk.Server.Modules.Agent;

public enum AgentScanStatus { Counting, Scanning, Completed, Failed, Cancelled }

/// <summary>
/// A scan in flight. In-memory only, for the same reason the old <c>AssetScan</c> was: a scan lost to
/// a restart costs a re-scan and nothing else. Here it is even cheaper — the agent restarts with the
/// shell, and the cloud already holds every batch that made it across.
/// </summary>
public class AgentScan
{
    public required Guid Id { get; init; }
    public required string RootPath { get; init; }

    public AgentScanStatus Status { get; set; } = AgentScanStatus.Counting;

    /// <summary>Files worth indexing found by the counting pass. 0 while still counting.</summary>
    public int Total { get; set; }
    public int Described { get; set; }

    /// <summary>Folders the walk could not open — reported rather than swallowed.</summary>
    public int Unreadable { get; set; }

    public string? Error { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    /// <summary>
    /// What the walk has described so far, in walk order. Appended by the scan, read by paged
    /// requests — hence the lock rather than a plain List: the shell pulls pages while the walk is
    /// still filling this, which is the whole point of streaming the results out as they appear.
    /// </summary>
    public List<ScannedFile> Files { get; } = [];
}

/// <summary>
/// Runs folder scans for the desktop shell (ADR-117). Structurally the same job-tracking the deleted
/// <c>AssetIndexService</c> did — one scan at a time, progress, cancellation, pruning of finished
/// jobs — with the database half removed: rows accumulate in memory and the page uploads them.
/// </summary>
public class AgentScanService(ILogger<AgentScanService> logger)
{
    private readonly ConcurrentDictionary<Guid, AgentScan> _scans = new();

    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);

    public AgentScan? Get(Guid id) => _scans.TryGetValue(id, out var scan) ? scan : null;

    public AgentScan Start(string rootPath)
    {
        Prune();
        var scan = new AgentScan { Id = Guid.NewGuid(), RootPath = rootPath };
        _scans[scan.Id] = scan;
        _ = RunAsync(scan);
        return scan;
    }

    public bool Cancel(Guid id)
    {
        if (Get(id) is not { Status: AgentScanStatus.Counting or AgentScanStatus.Scanning } scan) return false;
        scan.Cts.Cancel();
        return true;
    }

    /// <summary>
    /// A page of what has been described so far. Never blocks on the walk finishing: the shell starts
    /// uploading the first five hundred files while the walk is still deep in the tree.
    /// </summary>
    public List<ScannedFile> Page(AgentScan scan, int skip, int take)
    {
        lock (scan.Files)
        {
            if (skip >= scan.Files.Count) return [];
            return scan.Files.GetRange(skip, Math.Min(take, scan.Files.Count - skip));
        }
    }

    private Task RunAsync(AgentScan scan) => Task.Run(() =>
    {
        var token = scan.Cts.Token;
        try
        {
            // Pass one counts, so progress can say "of N" rather than guess. Cheap next to pass two:
            // this only enumerates names, while describing stats every file and reads its header.
            scan.Total = AssetFolderWalker
                .EnumerateIndexable(scan.RootPath, () => scan.Unreadable++, token)
                .Count();
            token.ThrowIfCancellationRequested();

            scan.Status = AgentScanStatus.Scanning;
            // Both passes tally the folders they could not open. Reset, or the number reported is
            // double the truth.
            scan.Unreadable = 0;

            foreach (var fullPath in AssetFolderWalker.EnumerateIndexable(scan.RootPath, () => scan.Unreadable++, token))
            {
                token.ThrowIfCancellationRequested();
                if (AssetFolderWalker.Describe(scan.RootPath, fullPath) is not { } described) continue;
                lock (scan.Files) scan.Files.Add(described);
                scan.Described++;
            }

            scan.Status = AgentScanStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            // Everything described before the cancel stays readable, and everything already uploaded
            // stays uploaded: a half-finished index is still an index, exactly as before ADR-117.
            scan.Status = AgentScanStatus.Cancelled;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Agent scan of {Root} failed", scan.RootPath);
            scan.Status = AgentScanStatus.Failed;
            scan.Error = e.Message;
        }
        finally
        {
            scan.FinishedAt = DateTime.UtcNow;
        }
    }, CancellationToken.None);

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var (id, scan) in _scans)
            if (scan.FinishedAt is { } finished && finished < cutoff)
                _scans.TryRemove(id, out _);
    }
}
