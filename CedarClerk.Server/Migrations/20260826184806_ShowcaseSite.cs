using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class ShowcaseSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomDomain",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShowcaseGallery",
                table: "Projects",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShowcaseTrailerUrl",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownloadUrl",
                table: "Builds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Builds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ShowcaseFollowers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    ConfirmToken = table.Column<string>(type: "TEXT", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UnsubscribeToken = table.Column<string>(type: "TEXT", nullable: false),
                    VisitorHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShowcaseFollowers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShowcaseStatDailies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShowcaseStatDailies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CustomDomain",
                table: "Projects",
                column: "CustomDomain",
                unique: true,
                filter: "\"CustomDomain\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShowcaseFollowers_ConfirmToken",
                table: "ShowcaseFollowers",
                column: "ConfirmToken");

            migrationBuilder.CreateIndex(
                name: "IX_ShowcaseFollowers_ProjectId_Email",
                table: "ShowcaseFollowers",
                columns: new[] { "ProjectId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShowcaseFollowers_UnsubscribeToken",
                table: "ShowcaseFollowers",
                column: "UnsubscribeToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShowcaseStatDailies_ProjectId_Day_Kind_Label",
                table: "ShowcaseStatDailies",
                columns: new[] { "ProjectId", "Day", "Kind", "Label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShowcaseFollowers");

            migrationBuilder.DropTable(
                name: "ShowcaseStatDailies");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CustomDomain",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CustomDomain",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ShowcaseGallery",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ShowcaseTrailerUrl",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DownloadUrl",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Builds");
        }
    }
}
