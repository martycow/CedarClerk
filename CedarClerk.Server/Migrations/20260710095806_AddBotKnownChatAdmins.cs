using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddBotKnownChatAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BotKnownChatAdmins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BotKnownChatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TelegramUserId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotKnownChatAdmins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BotKnownChatAdmins_BotKnownChats_BotKnownChatId",
                        column: x => x.BotKnownChatId,
                        principalTable: "BotKnownChats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BotKnownChatAdmins_BotKnownChatId_TelegramUserId",
                table: "BotKnownChatAdmins",
                columns: new[] { "BotKnownChatId", "TelegramUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotKnownChatAdmins");
        }
    }
}
