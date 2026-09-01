using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class BotKnownChatAdminsSyncedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdminsSyncedAt",
                table: "BotKnownChats",
                type: "TEXT",
                nullable: true);

            // Existing rows: LastSeenAt is when the bot's own membership last changed, which is
            // exactly the moment SyncAdminsAsync last ran for that chat. Backfilling it is a
            // statement of fact, and it keeps a real admin's list from emptying on deploy.
            migrationBuilder.Sql(
                "UPDATE BotKnownChats SET AdminsSyncedAt = LastSeenAt WHERE AdminsSyncedAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminsSyncedAt",
                table: "BotKnownChats");
        }
    }
}
