using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWave1Reach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PressContactEmail",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PressEngine",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PressFactsheetRows",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PressGenre",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PressPrice",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewToken",
                table: "Drafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlogNotifyJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    DraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogNotifyJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlogSubscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    ConfirmToken = table.Column<string>(type: "TEXT", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UnsubscribeToken = table.Column<string>(type: "TEXT", nullable: false),
                    VisitorHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogSubscribers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_PreviewToken",
                table: "Drafts",
                column: "PreviewToken",
                unique: true,
                filter: "\"PreviewToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BlogNotifyJobs_Status_CreatedAt",
                table: "BlogNotifyJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BlogSubscribers_ConfirmToken",
                table: "BlogSubscribers",
                column: "ConfirmToken");

            migrationBuilder.CreateIndex(
                name: "IX_BlogSubscribers_OwnerId_Email",
                table: "BlogSubscribers",
                columns: new[] { "OwnerId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlogSubscribers_UnsubscribeToken",
                table: "BlogSubscribers",
                column: "UnsubscribeToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlogNotifyJobs");

            migrationBuilder.DropTable(
                name: "BlogSubscribers");

            migrationBuilder.DropIndex(
                name: "IX_Drafts_PreviewToken",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "PressContactEmail",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PressEngine",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PressFactsheetRows",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PressGenre",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PressPrice",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PreviewToken",
                table: "Drafts");
        }
    }
}
