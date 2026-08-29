using CedarClerk.Server.Search;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <summary>
    /// Wave 1 item 2 — the FTS5 search table, raw SQL and deliberately OUTSIDE the EF model: the
    /// model diff of this migration is empty, so SchemaDriftGuardTests (model-vs-snapshot only)
    /// never sees it, and it must never gain an entity or DbSet. The DDL itself lives in
    /// <see cref="DraftSearchSchema"/>, shared with the tests and the on-demand ensure.
    ///
    /// The triggers are what survive ExecuteDeleteAsync, which bypasses EF's interceptors —
    /// row upkeep on ordinary saves is DraftSearchInterceptor's job.
    /// </summary>
    public partial class AddDraftSearchFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DraftSearchSchema.CreateTableSql);
            migrationBuilder.Sql(DraftSearchSchema.CreateTriggersSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS DraftSearch_Drafts_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS DraftSearch_DraftTranslations_ad;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS DraftSearch;");
        }
    }
}
