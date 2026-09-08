using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class DraftRevisionService
{
    public static class Kinds
    {
        public const string Save = "save";
        public const string Telegram = "telegram";
        public const string Blog = "blog";
        // T-016 — a marker, not a copy of anything new: it names the point the author rewound to
        // so the history reads as a story. Not pruned (restores are rare and deliberate).
        public const string Restore = "restore";
    }

    // ADR-065 — a ceiling on the *edit* history only. The autosave fires on every pause in typing,
    // so without one a few weeks of writing is hundreds of megabytes of near-identical documents in
    // a SQLite file that gets copied off the box every night. Publication revisions are never
    // pruned: they are the baselines the publish guard diffs against. Two limits, whichever bites
    // first: the newest 50 per draft and language, and nothing older than 90 days — the count
    // alone let a document that was worked on once keep its fifty full copies forever.
    public const int MaxSaveRevisionsPerLanguage = 50;
    public static readonly TimeSpan MaxSaveRevisionAge = TimeSpan.FromDays(90);

    /// <summary>
    /// Records a revision unless the newest one for the same target already holds this exact
    /// content. Adds to the change tracker — the caller still owns <c>SaveChangesAsync</c>.
    /// </summary>
    public static async Task RecordAsync(CedarDbContext db, Guid draftId, string language, string title, string cedarJson,
        string kind = Kinds.Save, string? destination = null, CancellationToken ct = default)
    {
        var latest = await db.DraftRevisions
            .Where(r => r.DraftId == draftId && r.Language == language && r.Kind == kind && r.Destination == destination)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Deliberately compares the content itself rather than a timestamp: the point is to keep
        // one row per *distinct* version, and two saves a second apart usually differ by nothing.
        if (latest is not null && latest.Title == title && latest.CedarJson == cedarJson) return;

        // From the draft rather than from the ambient tenant: this is also called by the publish
        // queue, which runs with no tenant at all.
        // Local first: a caller that created the draft in this same unit of work has not written
        // it yet, so a query would not find it.
        var ownerId = db.Drafts.Local.FirstOrDefault(d => d.Id == draftId)?.OwnerId
                      ?? await db.Drafts.Where(d => d.Id == draftId).Select(d => d.OwnerId).FirstOrDefaultAsync(ct)
                      ?? "";

        db.DraftRevisions.Add(new DraftRevision
        {
            DraftId = draftId, OwnerId = ownerId, Language = language, Title = title, CedarJson = cedarJson,
            Kind = kind, Destination = destination,
        });

        if (kind == Kinds.Save) await PruneSavesAsync(db, draftId, language, ct);
    }

    private static async Task PruneSavesAsync(CedarDbContext db, Guid draftId, string language, CancellationToken ct)
    {
        var countCutoff = await db.DraftRevisions
            .Where(r => r.DraftId == draftId && r.Language == language && r.Kind == Kinds.Save)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(MaxSaveRevisionsPerLanguage - 1)
            .Select(r => (DateTime?)r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var ageCutoff = DateTime.UtcNow - MaxSaveRevisionAge;

        await db.DraftRevisions
            .Where(r => r.DraftId == draftId && r.Language == language && r.Kind == Kinds.Save
                        && (r.CreatedAt < ageCutoff || (countCutoff != null && r.CreatedAt <= countCutoff)))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// The age limit over every owner's rows at once — for the nightly job, since the per-save
    /// pruning above only ever runs on a document somebody is still editing. Returns the count removed.
    /// </summary>
    public static Task<int> PruneExpiredSavesAsync(CedarDbContext db, DateTime now, CancellationToken ct = default)
    {
        var ageCutoff = now - MaxSaveRevisionAge;
        return db.DraftRevisions
            .Where(r => r.Kind == Kinds.Save && r.CreatedAt < ageCutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// The stored content for a language: the draft's own slot when it is that draft's primary
    /// language, its translation row otherwise. Null when no such version exists.
    /// </summary>
    public static async Task<(string Title, string CedarJson)?> ResolveAsync(CedarDbContext db, Draft draft, string language, CancellationToken ct = default)
    {
        if (language == draft.PrimaryLanguage)
            return (draft.Title, draft.CedarJson);
        var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == draft.Id && t.Language == language, ct);
        return translation is null ? null : (translation.Title, translation.CedarJson);
    }

    /// <summary>
    /// Identifies an exact version of a document. Short on purpose — it travels to the browser and
    /// back as the "this is what I was shown" token of the publish guard, and 64 bits of a SHA-256
    /// is far past what an accidental collision between two versions of one post would need.
    /// </summary>
    public static string Fingerprint(string title, string cedarJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title + "\n" + cedarJson));
        return Convert.ToHexString(bytes)[..16];
    }

    public static int BlockCount(string cedarJson) => Blocks(cedarJson).Count;

    public record PublishPreview(string Language, bool PublishedBefore, string Fingerprint, object? Diff);

    /// <summary>
    /// What an update to <paramref name="kind"/>/<paramref name="destination"/> would change, diffed
    /// against the content actually sent there last time. Null when the draft has no such language.
    /// </summary>
    public static async Task<PublishPreview?> PreviewAsync(CedarDbContext db, Draft draft, string language,
        string kind, string? destination, CancellationToken ct = default)
    {
        var current = await ResolveAsync(db, draft, language, ct);
        if (current is null) return null;

        var previous = await LastPublishedAsync(db, draft.Id, language, kind, destination, ct);
        return new PublishPreview(language, previous is not null,
            Fingerprint(current.Value.Title, current.Value.CedarJson),
            previous is null ? null : Diff(previous.CedarJson, current.Value.CedarJson));
    }

    /// <summary>
    /// ADR-065 — the publish guard, enforced here rather than trusted to the client. A first
    /// publication to a target needs no confirmation (there is nothing to overwrite); overwriting
    /// one requires the caller to name the exact version it showed the user.
    /// </summary>
    public static async Task<bool> ConfirmationSatisfiedAsync(CedarDbContext db, Draft draft, string language,
        string kind, string? destination, string? confirmedFingerprint, CancellationToken ct = default)
    {
        if (await LastPublishedAsync(db, draft.Id, language, kind, destination, ct) is null) return true;

        var current = await ResolveAsync(db, draft, language, ct);
        if (current is null) return true; // nothing to publish; the caller's own 404 is the better error
        return confirmedFingerprint == Fingerprint(current.Value.Title, current.Value.CedarJson);
    }

    private static Task<DraftRevision?> LastPublishedAsync(CedarDbContext db, Guid draftId, string language,
        string kind, string? destination, CancellationToken ct) =>
        db.DraftRevisions
            .Where(r => r.DraftId == draftId && r.Language == language && r.Kind == kind && r.Destination == destination)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    // Top-level TipTap blocks correspond to the editor's visible lines/blocks closely enough for
    // a publication warning. It intentionally does not pretend to be a character-perfect diff.
    public static object Diff(string beforeJson, string afterJson)
    {
        var before = Blocks(beforeJson);
        var after = Blocks(afterJson);
        var n = before.Count; var m = after.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        for (var j = m - 1; j >= 0; j--)
            dp[i, j] = before[i] == after[j] ? dp[i + 1, j + 1] : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var added = new List<int>(); var removed = new List<int>();
        var lines = new List<object>();
        var x = 0; var y = 0;
        while (x < n && y < m)
        {
            if (before[x].Json == after[y].Json)
            {
                lines.Add(new { kind = "context", beforeLine = x + 1, afterLine = y + 1, text = before[x].Text });
                x++; y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                removed.Add(x + 1);
                lines.Add(new { kind = "removed", beforeLine = x + 1, afterLine = (int?)null, text = before[x].Text });
                x++;
            }
            else
            {
                added.Add(y + 1);
                lines.Add(new { kind = "added", beforeLine = (int?)null, afterLine = y + 1, text = after[y].Text });
                y++;
            }
        }
        while (x < n)
        {
            removed.Add(x + 1);
            lines.Add(new { kind = "removed", beforeLine = x + 1, afterLine = (int?)null, text = before[x].Text });
            x++;
        }
        while (y < m)
        {
            added.Add(y + 1);
            lines.Add(new { kind = "added", beforeLine = (int?)null, afterLine = y + 1, text = after[y].Text });
            y++;
        }
        var changed = Math.Min(added.Count, removed.Count);
        return new { beforeLines = n, afterLines = m, addedLines = added, removedLines = removed,
            changedLines = changed, totalChanged = Math.Max(added.Count, removed.Count), lines };
    }

    private sealed record DiffBlock(string Json, string Text);

    private static List<DiffBlock> Blocks(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray().Select(x => new DiffBlock(x.GetRawText(), BlockText(x))).ToList() : [];
        }
        catch (JsonException) { return []; }
    }

    private static string BlockText(JsonElement block)
    {
        var text = new StringBuilder();
        CollectText(block, text);
        if (text.Length > 0) return text.ToString();
        return block.TryGetProperty("type", out var type) ? $"[{type.GetString()}]" : "[block]";
    }

    private static void CollectText(JsonElement node, StringBuilder text)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                text.Append(value.GetString());
            if (node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object
                && attrs.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String)
                text.Append(label.GetString());
            if (node.TryGetProperty("content", out var content)) CollectText(content, text);
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray()) CollectText(child, text);
        }
    }
}
