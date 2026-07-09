using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftBlogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BlogPublishedAt",
                table: "Drafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlogSlug",
                table: "Drafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBlogPublished",
                table: "Drafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlogPublishedAt",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "BlogSlug",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "IsBlogPublished",
                table: "Drafts");
        }
    }
}
