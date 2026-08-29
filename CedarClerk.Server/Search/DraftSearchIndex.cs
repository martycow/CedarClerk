using System.Data.Common;
using System.Text;
using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Search;

public sealed record BlogSearchHit(string Slug, string Title, string Snippet, DateTime? PublishedAt, string Language);

public sealed record DraftSearchHit(Guid Id, string Title, string Snippet, DateTime UpdatedAt, string DocumentType, bool IsBlogPublished);

/// <summary>
/// Full-text search over drafts and their translations (Wave 1 item 2). One FTS5 table serves
/// both surfaces: the app's own document search and the public blog's, which differ only in what
/// they are allowed to see — the published query enforces that, the index does not.
/// </summary>
public interface IDraftSearchIndex
{
    Task ReindexDraftAsync(Guid draftId, CancellationToken ct = default);
    Task RemoveDraftAsync(Guid draftId, CancellationToken ct = default);
    Task<IReadOnlyList<BlogSearchHit>> SearchPublishedAsync(string ownerId, string query, string? lang, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<DraftSearchHit>> SearchDraftsAsync(string ownerId, string query, int limit = 20, CancellationToken ct = default);
}

/// <summary>
/// The FTS5 table's DDL, in one place for the migration, the tests and the on-demand ensure.
/// The table lives OUTSIDE the EF model on purpose — SchemaDriftGuardTests compares model to
/// snapshot and must never see it; a test database built by EnsureCreated() does not have it,
/// which is why every writer ensures it first. All statements are IF NOT EXISTS, so re-running
/// them is a no-op everywhere.
/// </summary>
public static class DraftSearchSchema
{
    // remove_diacritics 2 folds accents the way unicode61 documents it, so "café" matches "cafe";
    // the id columns are UNINDEXED — they are filters, not text.
    public const string CreateTableSql = """
        CREATE VIRTUAL TABLE IF NOT EXISTS DraftSearch USING fts5(
            DraftId UNINDEXED, OwnerId UNINDEXED, Language UNINDEXED,
            Title, Body, Tags, tokenize='unicode61 remove_diacritics 2');
        """;

    // ExecuteDeleteAsync bypasses EF's interceptors, so removal is guaranteed by the database
    // itself rather than by every delete path remembering to call RemoveDraftAsync.
    public const string CreateTriggersSql = """
        CREATE TRIGGER IF NOT EXISTS DraftSearch_Drafts_ad AFTER DELETE ON Drafts BEGIN
            DELETE FROM DraftSearch WHERE DraftId = old.Id;
        END;
        CREATE TRIGGER IF NOT EXISTS DraftSearch_DraftTranslations_ad AFTER DELETE ON DraftTranslations BEGIN
            DELETE FROM DraftSearch WHERE DraftId = old.DraftId AND Language = old.Language;
        END;
        """;

    public static void Ensure(DbContext db)
    {
        db.Database.ExecuteSqlRaw(CreateTableSql);
        db.Database.ExecuteSqlRaw(CreateTriggersSql);
    }

    public static async Task EnsureAsync(DbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(CreateTableSql, ct);
        await db.Database.ExecuteSqlRawAsync(CreateTriggersSql, ct);
    }

    /// <summary>Marks flattened, paragraphs newline-joined — what the Body column holds.</summary>
    public static string BodyText(string cedarJson) => string.Join("\n", CedarPlainText.Paragraphs(cedarJson));
}

public sealed class DraftSearchIndex(CedarDbContext db) : IDraftSearchIndex
{
    // Enough rows to dedupe language versions of one draft down to `limit` distinct posts.
    private const int OverFetchFactor = 3;
    private const int MaxQueryTokens = 8;
    private const int SnippetTokens = 12;

    public async Task ReindexDraftAsync(Guid draftId, CancellationToken ct = default)
    {
        // IgnoreQueryFilters: reindexing runs from request scopes and platform scopes alike, and
        // the rows it writes are keyed by the draft id the caller already authorized.
        var draft = await db.Drafts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == draftId, ct);

        await DraftSearchSchema.EnsureAsync(db, ct);
        await db.Database.ExecuteSqlAsync($"DELETE FROM DraftSearch WHERE DraftId = {draftId}", ct);
        if (draft is null) return;

        await InsertRowAsync(draftId, draft.OwnerId, draft.PrimaryLanguage,
            draft.ArticleTitle ?? draft.Title, DraftSearchSchema.BodyText(draft.CedarJson), draft.Tags, ct);

        var translations = await db.DraftTranslations.IgnoreQueryFilters()
            .Where(t => t.DraftId == draftId)
            .Select(t => new { t.OwnerId, t.Language, t.Title, t.CedarJson })
            .ToListAsync(ct);
        foreach (var t in translations)
            await InsertRowAsync(draftId, t.OwnerId, t.Language, t.Title, DraftSearchSchema.BodyText(t.CedarJson), "", ct);
    }

    public async Task RemoveDraftAsync(Guid draftId, CancellationToken ct = default)
    {
        await DraftSearchSchema.EnsureAsync(db, ct);
        await db.Database.ExecuteSqlAsync($"DELETE FROM DraftSearch WHERE DraftId = {draftId}", ct);
    }

    public async Task<IReadOnlyList<BlogSearchHit>> SearchPublishedAsync(
        string ownerId, string query, string? lang, int limit = 20, CancellationToken ct = default)
    {
        var match = SanitizeQuery(query);
        if (match.Length == 0) return [];
        await DraftSearchSchema.EnsureAsync(db, ct);

        // Owner named twice on purpose (house rule): once on the index row, once on the joined
        // draft, so a stale index row can never surface another tenant's post.
        var sql = $"""
            SELECT DraftSearch.DraftId, d.BlogSlug, DraftSearch.Title,
                   snippet(DraftSearch, -1, '', '', '…', {SnippetTokens}),
                   d.BlogPublishedAt, DraftSearch.Language, bm25(DraftSearch)
            FROM DraftSearch
            JOIN Drafts d ON d.Id = DraftSearch.DraftId
            WHERE DraftSearch MATCH $q AND DraftSearch.OwnerId = $owner AND d.OwnerId = $owner
              AND d.IsBlogPublished = 1 AND d.IsPrivate = 0 AND d.BlogSlug IS NOT NULL
            ORDER BY bm25(DraftSearch) LIMIT $take
            """;

        var rows = new List<(Guid DraftId, BlogSearchHit Hit, double Rank)>();
        await ReadAsync(sql, match, ownerId, limit * OverFetchFactor, reader =>
        {
            rows.Add((reader.GetGuid(0),
                new BlogSearchHit(reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
                    reader.GetString(5)),
                reader.GetDouble(6)));
        }, ct);

        // One post shows once, in the reader's language when that version matched at all.
        return rows
            .GroupBy(r => r.DraftId)
            .Select(g => (lang is not null && g.FirstOrDefault(r => r.Hit.Language == lang) is { Hit: not null } preferred
                ? preferred
                : g.OrderBy(r => r.Rank).First()))
            .OrderBy(r => r.Rank)
            .Take(limit)
            .Select(r => r.Hit)
            .ToList();
    }

    public async Task<IReadOnlyList<DraftSearchHit>> SearchDraftsAsync(
        string ownerId, string query, int limit = 20, CancellationToken ct = default)
    {
        var match = SanitizeQuery(query);
        if (match.Length == 0) return [];
        await DraftSearchSchema.EnsureAsync(db, ct);

        var sql = $"""
            SELECT DraftSearch.DraftId, DraftSearch.Title,
                   snippet(DraftSearch, -1, '', '', '…', {SnippetTokens}),
                   d.UpdatedAt, d.DocumentType, d.IsBlogPublished, bm25(DraftSearch)
            FROM DraftSearch
            JOIN Drafts d ON d.Id = DraftSearch.DraftId
            WHERE DraftSearch MATCH $q AND DraftSearch.OwnerId = $owner AND d.OwnerId = $owner
            ORDER BY bm25(DraftSearch) LIMIT $take
            """;

        var rows = new List<(DraftSearchHit Hit, double Rank)>();
        await ReadAsync(sql, match, ownerId, limit * OverFetchFactor, reader =>
        {
            rows.Add((new DraftSearchHit(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
                reader.GetString(4), reader.GetBoolean(5)),
                reader.GetDouble(6)));
        }, ct);

        return rows
            .GroupBy(r => r.Hit.Id)
            .Select(g => g.OrderBy(r => r.Rank).First())
            .OrderBy(r => r.Rank)
            .Take(limit)
            .Select(r => r.Hit)
            .ToList();
    }

    /// <summary>
    /// FTS5's query language is a language, and user input is not allowed to speak it: every token
    /// is stripped to letters and digits and double-quoted, the last one gets a prefix star. No
    /// operator, column filter or NEAR() survives this — the worst a query can be is empty.
    /// </summary>
    public static string SanitizeQuery(string query)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch)) current.Append(ch);
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
                if (tokens.Count == MaxQueryTokens) break;
            }
        }
        if (current.Length > 0 && tokens.Count < MaxQueryTokens) tokens.Add(current.ToString());
        if (tokens.Count == 0) return "";

        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('"').Append(tokens[i]).Append('"');
            if (i == tokens.Count - 1) sb.Append('*');
        }
        return sb.ToString();
    }

    private async Task InsertRowAsync(Guid draftId, string ownerId, string language,
        string title, string body, string tags, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO DraftSearch (DraftId, OwnerId, Language, Title, Body, Tags)
            VALUES ({draftId}, {ownerId}, {language}, {title}, {body}, {tags})
            """, ct);
    }

    private async Task ReadAsync(string sql, string match, string ownerId, int take,
        Action<DbDataReader> onRow, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            AddParameter(cmd, "$q", match);
            AddParameter(cmd, "$owner", ownerId);
            AddParameter(cmd, "$take", take);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) onRow(reader);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
