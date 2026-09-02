using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscoveryCategory",
                table: "Projects",
                type: "TEXT",
                nullable: false,
                defaultValue: "other");

            migrationBuilder.AddColumn<bool>(
                name: "DiscoveryOptIn",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DiscoverySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowScreenshotSaturday = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowProjects = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowBlogs = table.Column<bool>(type: "INTEGER", nullable: false),
                    TitleEn = table.Column<string>(type: "TEXT", nullable: true),
                    TitleRu = table.Column<string>(type: "TEXT", nullable: true),
                    IntroEn = table.Column<string>(type: "TEXT", nullable: true),
                    IntroRu = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoverySettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoverySettings");

            migrationBuilder.DropColumn(
                name: "DiscoveryCategory",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DiscoveryOptIn",
                table: "AspNetUsers");
        }
    }
}
