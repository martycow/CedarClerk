using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    FromDraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToDraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_FromDraftId_ToDraftId",
                table: "DocumentLinks",
                columns: new[] { "FromDraftId", "ToDraftId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_ToDraftId",
                table: "DocumentLinks",
                column: "ToDraftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentLinks");
        }
    }
}
