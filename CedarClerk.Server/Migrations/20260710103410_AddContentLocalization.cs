using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddContentLocalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited default: existing rows are all primary-language posts, not "".
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "ScheduledPosts",
                type: "TEXT",
                nullable: false,
                defaultValue: "ru");

            migrationBuilder.CreateTable(
                name: "DraftTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    CedarJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftTranslations_Drafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "Drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftTranslations_DraftId_Language",
                table: "DraftTranslations",
                columns: new[] { "DraftId", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftTranslations");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "ScheduledPosts");
        }
    }
}
