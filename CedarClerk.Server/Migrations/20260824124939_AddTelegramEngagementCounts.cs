using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramEngagementCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TelegramCommentCount",
                table: "ChannelStatSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TelegramReactionCount",
                table: "ChannelStatSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CommentCount",
                table: "ChannelPosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReactionCount",
                table: "ChannelPosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatsSeenAt",
                table: "ChannelPosts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TelegramCommentCount",
                table: "ChannelStatSnapshots");

            migrationBuilder.DropColumn(
                name: "TelegramReactionCount",
                table: "ChannelStatSnapshots");

            migrationBuilder.DropColumn(
                name: "CommentCount",
                table: "ChannelPosts");

            migrationBuilder.DropColumn(
                name: "ReactionCount",
                table: "ChannelPosts");

            migrationBuilder.DropColumn(
                name: "StatsSeenAt",
                table: "ChannelPosts");
        }
    }
}
