using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftLastTelegramPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastTelegramChatId",
                table: "Drafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastTelegramMessageId",
                table: "Drafts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTelegramUsername",
                table: "Drafts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTelegramChatId",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "LastTelegramMessageId",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "LastTelegramUsername",
                table: "Drafts");
        }
    }
}
