using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CedarClerk.Server.Search;

/// <summary>
/// Keeps DraftSearch in step with ordinary saves: every tracked add/update/delete of a Draft or
/// DraftTranslation rewrites that (DraftId, Language) row after the save commits. Untracked paths
/// (ExecuteDeleteAsync) are covered by the migration's triggers instead — the two mechanisms
/// together are the whole maintenance story, see <see cref="DraftSearchSchema"/>.
///
/// Registered on the context in Program.cs, deliberately not inside the context itself: a test
/// database built by EnsureCreated() has no FTS table, and every existing test constructs the
/// context directly.
/// </summary>
public sealed class DraftSearchInterceptor : SaveChangesInterceptor
{
    private sealed record UpsertRow(Guid DraftId, string OwnerId, string Language, string Title, string Body, string Tags);

    private sealed class Pending
    {
        public readonly List<UpsertRow> Upserts = [];
        /// <summary>Language null means every row of the draft.</summary>
        public readonly List<(Guid DraftId, string? Language)> Removals = [];
    }

    // Per-context, because one interceptor instance serves every context of the application.
    private static readonly ConditionalWeakTable<DbContext, Pending> Work = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        Flush(eventData.Context);
        return base.SavedChangesAsync(eventData, result, ct);
    }

    private static void Capture(DbContext? context)
    {
        if (context is null) return;
        Pending? pending = null;
        Pending Get() => pending ??= Work.GetOrCreateValue(context);

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is Draft draft)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                    case EntityState.Modified:
                        if (entry.State == EntityState.Modified)
                        {
                            var oldLang = entry.OriginalValues.GetValue<string>(nameof(Draft.PrimaryLanguage));
                            if (oldLang != draft.PrimaryLanguage) Get().Removals.Add((draft.Id, oldLang));
                        }
                        Get().Upserts.Add(new UpsertRow(draft.Id, draft.OwnerId, draft.PrimaryLanguage,
                            draft.ArticleTitle ?? draft.Title, DraftSearchSchema.BodyText(draft.CedarJson), draft.Tags));
                        break;
                    case EntityState.Deleted:
                        Get().Removals.Add((draft.Id, null));
                        break;
                }
            }
            else if (entry.Entity is DraftTranslation translation)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                    case EntityState.Modified:
                        if (entry.State == EntityState.Modified)
                        {
                            var oldLang = entry.OriginalValues.GetValue<string>(nameof(DraftTranslation.Language));
                            if (oldLang != translation.Language) Get().Removals.Add((translation.DraftId, oldLang));
                        }
                        Get().Upserts.Add(new UpsertRow(translation.DraftId, translation.OwnerId, translation.Language,
                            translation.Title, DraftSearchSchema.BodyText(translation.CedarJson), ""));
                        break;
                    case EntityState.Deleted:
                        Get().Removals.Add((translation.DraftId, translation.Language));
                        break;
                }
            }
        }
    }

    private static void Flush(DbContext? context)
    {
        if (context is null || !Work.TryGetValue(context, out var pending)) return;
        Work.Remove(context);
        if (pending.Upserts.Count == 0 && pending.Removals.Count == 0) return;

        // A broken index must degrade search, never break the save that already committed —
        // the next save of the same draft (or the startup backfill) rewrites the rows anyway.
        try
        {
            DraftSearchSchema.Ensure(context);
            foreach (var (draftId, language) in pending.Removals)
            {
                if (language is null)
                    context.Database.ExecuteSql($"DELETE FROM DraftSearch WHERE DraftId = {draftId}");
                else
                    context.Database.ExecuteSql($"DELETE FROM DraftSearch WHERE DraftId = {draftId} AND Language = {language}");
            }
            foreach (var row in pending.Upserts)
            {
                context.Database.ExecuteSql(
                    $"DELETE FROM DraftSearch WHERE DraftId = {row.DraftId} AND Language = {row.Language}");
                context.Database.ExecuteSql($"""
                    INSERT INTO DraftSearch (DraftId, OwnerId, Language, Title, Body, Tags)
                    VALUES ({row.DraftId}, {row.OwnerId}, {row.Language}, {row.Title}, {row.Body}, {row.Tags})
                    """);
            }
        }
        catch (Exception)
        {
            // Swallowed by design — see above.
        }
    }
}
