using System.Collections.Concurrent;

namespace CedarClerk.Server.Tenancy;

/// <summary>
/// The Host → owner answer, held for seconds instead of asked once per request. A blog page pulls
/// its stylesheet, its fonts and every image from the same host, so uncached this is a dozen
/// AspNetUsers queries per page view, each on a freshly built context and SQLite connection.
///
/// Misses are cached on the same terms: an unknown subdomain that costs a query is an anonymous
/// caller's lever on a machine with 2 GB and no swap.
///
/// Nothing invalidates an entry — expiry is the only invalidation, which is why the lifetimes are
/// seconds rather than minutes. A name that moves between accounts would otherwise serve one blog
/// under another's host, and this way no rename or deletion path has to remember this class exists.
/// </summary>
public class TenantOwnerCache(TimeProvider? time = null)
{
    /// <summary>
    /// The Host → owner cache. Separate from the media one on purpose: a flood of invented
    /// subdomains fills its own budget and cannot evict the asset answers a blog page depends on.
    /// </summary>
    public sealed class ForHosts(TimeProvider? time = null) : TenantOwnerCache(time);

    /// <summary>The file → owner cache. Separate from the host one, same reason.</summary>
    public sealed class ForMedia(TimeProvider? time = null) : TenantOwnerCache(time);

    /// <summary>
    /// The custom domain → "owner|slug" cache (T-300). Its own budget again: an unknown Host header
    /// is the cheapest thing on the internet to invent, and this one is asked about every host that
    /// is not ours.
    /// </summary>
    public sealed class ForDomains(TimeProvider? time = null) : TenantOwnerCache(time);

    public static readonly TimeSpan HitLifetime = TimeSpan.FromSeconds(30);

    /// <summary>Shorter than a hit: a subdomain that has just become real should start answering.</summary>
    public static readonly TimeSpan MissLifetime = TimeSpan.FromSeconds(10);

    /// <summary>Names, not requests — only a well-formed username ever reaches this far.</summary>
    public const int Capacity = 10_000;

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private int estimate;
    private int sweeping;

    private readonly record struct Entry(string? OwnerId, long ExpiresAt);

    /// <summary>Readable so a test can hold the cache to its own bound.</summary>
    public int Count => entries.Count;

    /// <summary>
    /// Drop one name. Expiry is still what keeps an entry honest in general — this is for the one
    /// change that cannot wait for it: a deleted account whose subdomain would otherwise keep
    /// answering, with an empty blog, until the entry aged out (T-292).
    /// </summary>
    public void Forget(string name)
    {
        if (entries.TryRemove(name, out _)) Interlocked.Decrement(ref estimate);
    }

    public async ValueTask<string?> GetAsync(
        string name, Func<CancellationToken, Task<string?>> load, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcTicks;

        if (entries.TryGetValue(name, out var cached))
        {
            if (cached.ExpiresAt > now) return cached.OwnerId;
            if (entries.TryRemove(name, out _)) Interlocked.Decrement(ref estimate);
        }

        var ownerId = await load(ct);
        Store(name, new Entry(ownerId, now + (ownerId is null ? MissLifetime : HitLifetime).Ticks));
        return ownerId;
    }

    private void Store(string name, Entry entry)
    {
        if (!entries.TryAdd(name, entry))
        {
            entries[name] = entry;
            return;
        }

        // Counted rather than measured: ConcurrentDictionary.Count locks every bucket, and this is
        // the path a flood of unknown names takes.
        if (Interlocked.Increment(ref estimate) > Capacity) Sweep();
    }

    /// <summary>
    /// Expired entries first, then the oldest live ones. Emptying the dictionary instead would hand
    /// the next burst exactly the miss storm the cache exists to absorb.
    /// </summary>
    private void Sweep()
    {
        if (Interlocked.CompareExchange(ref sweeping, 1, 0) != 0) return;
        try
        {
            var now = clock.GetUtcNow().UtcTicks;
            foreach (var (name, entry) in entries)
                if (entry.ExpiresAt <= now)
                    entries.TryRemove(name, out _);

            var live = entries.Count;
            var target = Capacity * 3 / 4;
            if (live > target)
            {
                var oldest = entries.OrderBy(e => e.Value.ExpiresAt).Take(live - target)
                    .Select(e => e.Key).ToList();
                foreach (var name in oldest)
                    entries.TryRemove(name, out _);
            }

            Interlocked.Exchange(ref estimate, entries.Count);
        }
        finally
        {
            Interlocked.Exchange(ref sweeping, 0);
        }
    }
}
