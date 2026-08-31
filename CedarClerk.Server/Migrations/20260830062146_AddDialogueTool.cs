using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDialogueTool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DialogueLineTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    DialogueScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineId = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DialogueLineTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DialogueScripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    GraphJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DialogueScripts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DialogueLineTranslations_DialogueScriptId_LineId_Language",
                table: "DialogueLineTranslations",
                columns: new[] { "DialogueScriptId", "LineId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DialogueScripts_ProjectId_UpdatedAt",
                table: "DialogueScripts",
                columns: new[] { "ProjectId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DialogueLineTranslations");

            migrationBuilder.DropTable(
                name: "DialogueScripts");
        }
    }
}
