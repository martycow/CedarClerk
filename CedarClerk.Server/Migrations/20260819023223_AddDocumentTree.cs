using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentDraftId",
                table: "Drafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiblingOrder",
                table: "Drafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentDraftId",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "SiblingOrder",
                table: "Drafts");
        }
    }
}
