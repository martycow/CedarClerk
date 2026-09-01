using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGlossaryTermUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlossaryTermUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    GlossaryTermId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Occurrences = table.Column<int>(type: "INTEGER", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryTermUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryTermUsages_OwnerId_DraftId",
                table: "GlossaryTermUsages",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryTermUsages_OwnerId_GlossaryTermId",
                table: "GlossaryTermUsages",
                columns: new[] { "OwnerId", "GlossaryTermId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlossaryTermUsages");
        }
    }
}
