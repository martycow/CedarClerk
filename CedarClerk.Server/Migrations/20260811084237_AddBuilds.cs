using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddBuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BuildId",
                table: "GameTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Builds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Builds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryTerms_OwnerId_ProjectId_Language",
                table: "GlossaryTerms",
                columns: new[] { "OwnerId", "ProjectId", "Language" });

            migrationBuilder.CreateIndex(
                name: "IX_Builds_ProjectId_Version",
                table: "Builds",
                columns: new[] { "ProjectId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Builds");

            migrationBuilder.DropIndex(
                name: "IX_GlossaryTerms_OwnerId_ProjectId_Language",
                table: "GlossaryTerms");

            migrationBuilder.DropColumn(
                name: "BuildId",
                table: "GameTasks");
        }
    }
}
