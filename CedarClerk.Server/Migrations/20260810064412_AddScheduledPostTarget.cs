using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledPostTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Network",
                table: "ScheduledPosts",
                type: "TEXT",
                nullable: false,
                // Not "" — every row that exists when this runs is a Telegram schedule, and an
                // empty network would read as an unknown one in the Posts Manager (ADR-099).
                defaultValue: "telegram");

            migrationBuilder.AddColumn<Guid>(
                name: "TargetId",
                table: "ScheduledPosts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Network",
                table: "ScheduledPosts");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "ScheduledPosts");
        }
    }
}
