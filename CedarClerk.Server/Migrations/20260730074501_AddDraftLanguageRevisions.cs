using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftLanguageRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceLanguage",
                table: "DraftTranslations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryLanguage",
                table: "Drafts",
                type: "TEXT",
                nullable: false,
                defaultValue: "ru");

            migrationBuilder.CreateTable(
                name: "DraftRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    CedarJson = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Destination = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftRevisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftRevisions_DraftId_Language_Kind_Destination_CreatedAt",
                table: "DraftRevisions",
                columns: new[] { "DraftId", "Language", "Kind", "Destination", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftRevisions");

            migrationBuilder.DropColumn(
                name: "SourceLanguage",
                table: "DraftTranslations");

            migrationBuilder.DropColumn(
                name: "PrimaryLanguage",
                table: "Drafts");
        }
    }
}
