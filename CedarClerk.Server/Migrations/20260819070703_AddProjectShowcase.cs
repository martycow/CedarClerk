using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectShowcase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShowcaseLinks",
                table: "Projects",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShowcaseSlug",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublicRoadmap",
                table: "GameTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ShowcaseSlug",
                table: "Projects",
                column: "ShowcaseSlug",
                unique: true,
                filter: "\"ShowcaseSlug\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_ShowcaseSlug",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ShowcaseLinks",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ShowcaseSlug",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IsPublicRoadmap",
                table: "GameTasks");
        }
    }
}
