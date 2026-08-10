using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectsAndDocumentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Drafts_OwnerId",
                table: "Drafts");

            migrationBuilder.AddColumn<string>(
                name: "DocumentType",
                table: "Drafts",
                type: "TEXT",
                nullable: false,
                defaultValue: "post");

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "Drafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CoverUrl = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_OwnerId_ProjectId",
                table: "Drafts",
                columns: new[] { "OwnerId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OwnerId_ArchivedAt",
                table: "Projects",
                columns: new[] { "OwnerId", "ArchivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Drafts_OwnerId_ProjectId",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Drafts");

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_OwnerId",
                table: "Drafts",
                column: "OwnerId");
        }
    }
}
