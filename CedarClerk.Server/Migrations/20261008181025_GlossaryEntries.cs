using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class GlossaryEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlossaryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsCaseSensitive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlossaryEntryLanguages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    EntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    LocalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    SpellingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    LocalizedDescription = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryEntryLanguages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlossaryEntryLanguages_GlossaryEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "GlossaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryEntries_OwnerId_ProjectId",
                table: "GlossaryEntries",
                columns: new[] { "OwnerId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryEntryLanguages_EntryId_Language",
                table: "GlossaryEntryLanguages",
                columns: new[] { "EntryId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryEntryLanguages_OwnerId_Language",
                table: "GlossaryEntryLanguages",
                columns: new[] { "OwnerId", "Language" });

            // ADR-320 — every GlossaryTerms row becomes one GlossaryEntryLanguages row under the
            // same Id, so DraftGlossaryExclusions and GlossaryTermUsages keep pointing at it.
            // GlossaryTerms itself is left untouched: it is what Down goes back to.
            foreach (var statement in CopyStatements)
                migrationBuilder.Sql(statement);
        }

        private const string Whitespace = "' ' || char(9) || char(10) || char(13)";

        private static readonly string[] CopyStatements =
        [
            // The group a row belongs to: SourceTermId followed to its end. The depth bound is
            // what stops a self-reference or a loop, both of which the old upsert could write.
            """
            CREATE TEMP TABLE "_GlossaryRoot" AS
            WITH RECURSIVE walk("Id", "Root", "Depth") AS (
                SELECT "Id", "Id", 0 FROM "GlossaryTerms"
                UNION ALL
                SELECT w."Id", t."SourceTermId", w."Depth" + 1
                FROM walk w JOIN "GlossaryTerms" t ON t."Id" = w."Root"
                WHERE t."SourceTermId" IS NOT NULL AND t."SourceTermId" <> t."Id" AND w."Depth" < 8
            )
            SELECT w."Id", w."Root" FROM walk w
            WHERE w."Depth" = (SELECT MAX(x."Depth") FROM walk x WHERE x."Id" = w."Id");
            """,
            // The row an entry is made from: the root itself, or the oldest row of a group whose
            // root was deleted.
            """
            CREATE TEMP TABLE "_GlossaryLeader" AS
            SELECT g."Root", (
                SELECT t."Id" FROM "GlossaryTerms" t JOIN "_GlossaryRoot" r ON r."Id" = t."Id"
                WHERE r."Root" = g."Root"
                ORDER BY (t."Id" = g."Root") DESC, t."CreatedAt", t."Id" LIMIT 1) AS "LeaderId"
            FROM (SELECT DISTINCT "Root" FROM "_GlossaryRoot") g;
            """,
            // A row joins its leader's entry only when the entry can hold it without changing it:
            // same owner, scope, case flag and image, and the first row of its language. Any other
            // row keeps everything it has as an entry of its own.
            """
            CREATE TEMP TABLE "_GlossaryEntryOf" AS
            SELECT t."Id", CASE
                WHEN t."Id" = l."LeaderId" THEN t."Id"
                WHEN t."OwnerId" = h."OwnerId"
                    AND t."ProjectId" IS h."ProjectId"
                    AND t."IsCaseSensitive" = h."IsCaseSensitive"
                    AND COALESCE(t."ImageUrl", '') = COALESCE(h."ImageUrl", '')
                    AND t."Language" <> h."Language"
                    AND NOT EXISTS (
                        SELECT 1 FROM "GlossaryTerms" o JOIN "_GlossaryRoot" ro ON ro."Id" = o."Id"
                        WHERE ro."Root" = r."Root" AND o."Id" <> t."Id" AND o."Id" <> l."LeaderId"
                            AND o."Language" = t."Language"
                            AND o."OwnerId" = h."OwnerId"
                            AND o."ProjectId" IS h."ProjectId"
                            AND o."IsCaseSensitive" = h."IsCaseSensitive"
                            AND COALESCE(o."ImageUrl", '') = COALESCE(h."ImageUrl", '')
                            AND (o."CreatedAt" < t."CreatedAt" OR (o."CreatedAt" = t."CreatedAt" AND o."Id" < t."Id")))
                    THEN l."LeaderId"
                ELSE t."Id" END AS "EntryId"
            FROM "GlossaryTerms" t
            JOIN "_GlossaryRoot" r ON r."Id" = t."Id"
            JOIN "_GlossaryLeader" l ON l."Root" = r."Root"
            JOIN "GlossaryTerms" h ON h."Id" = l."LeaderId";
            """,
            $$"""
            CREATE TEMP TABLE "_GlossarySpellings" AS
            WITH RECURSIVE split("Id", "Rest", "Acc") AS (
                SELECT "Id", "Aliases" || ',', '' FROM "GlossaryTerms"
                UNION ALL
                SELECT "Id", SUBSTR("Rest", INSTR("Rest", ',') + 1),
                    CASE WHEN TRIM(SUBSTR("Rest", 1, INSTR("Rest", ',') - 1), {{Whitespace}}) = '' THEN "Acc"
                        ELSE "Acc" || CASE WHEN "Acc" = '' THEN '' ELSE ',' END
                            || json_quote(TRIM(SUBSTR("Rest", 1, INSTR("Rest", ',') - 1), {{Whitespace}})) END
                FROM split WHERE "Rest" <> ''
            )
            SELECT "Id", '[' || "Acc" || ']' AS "Json" FROM split WHERE "Rest" = '';
            """,
            """
            INSERT INTO "GlossaryEntries" ("Id", "OwnerId", "Name", "Description", "ImageUrl", "IsCaseSensitive", "ProjectId", "CreatedAt", "UpdatedAt")
            SELECT t."Id", t."OwnerId", t."Term", t."Description", t."ImageUrl", t."IsCaseSensitive", t."ProjectId", t."CreatedAt", t."UpdatedAt"
            FROM "GlossaryTerms" t JOIN "_GlossaryEntryOf" e ON e."Id" = t."Id"
            WHERE e."EntryId" = t."Id";
            """,
            """
            INSERT INTO "GlossaryEntryLanguages" ("Id", "OwnerId", "EntryId", "Language", "LocalizedName", "SpellingsJson", "LocalizedDescription", "CreatedAt", "UpdatedAt")
            SELECT t."Id", t."OwnerId", e."EntryId", t."Language", t."Term", s."Json", t."Description", t."CreatedAt", t."UpdatedAt"
            FROM "GlossaryTerms" t
            JOIN "_GlossaryEntryOf" e ON e."Id" = t."Id"
            JOIN "_GlossarySpellings" s ON s."Id" = t."Id";
            """,
            """DROP TABLE "_GlossarySpellings";""",
            """DROP TABLE "_GlossaryEntryOf";""",
            """DROP TABLE "_GlossaryLeader";""",
            """DROP TABLE "_GlossaryRoot";""",
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlossaryEntryLanguages");

            migrationBuilder.DropTable(
                name: "GlossaryEntries");
        }
    }
}
