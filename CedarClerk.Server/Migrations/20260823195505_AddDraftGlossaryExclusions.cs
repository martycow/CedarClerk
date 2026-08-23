using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftGlossaryExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftGlossaryExclusions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    DraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GlossaryTermId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftGlossaryExclusions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftGlossaryExclusions_DraftId_GlossaryTermId_Language",
                table: "DraftGlossaryExclusions",
                columns: new[] { "DraftId", "GlossaryTermId", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftGlossaryExclusions");
        }
    }
}
