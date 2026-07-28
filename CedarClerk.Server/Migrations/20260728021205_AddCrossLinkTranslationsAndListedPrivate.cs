using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossLinkTranslationsAndListedPrivate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsListedWhilePrivate",
                table: "Drafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BlogLinkTextTranslationsJson",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramLinkTextTranslationsJson",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsListedWhilePrivate",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "BlogLinkTextTranslationsJson",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TelegramLinkTextTranslationsJson",
                table: "AspNetUsers");
        }
    }
}
