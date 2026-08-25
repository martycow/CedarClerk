using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class PerOwnerBlogSlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_ShowcaseSlug",
                table: "Projects");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OwnerId_ShowcaseSlug",
                table: "Projects",
                columns: new[] { "OwnerId", "ShowcaseSlug" },
                unique: true,
                filter: "\"ShowcaseSlug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_OwnerId_BlogSlug",
                table: "Drafts",
                columns: new[] { "OwnerId", "BlogSlug" },
                unique: true,
                filter: "\"BlogSlug\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_OwnerId_ShowcaseSlug",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Drafts_OwnerId_BlogSlug",
                table: "Drafts");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ShowcaseSlug",
                table: "Projects",
                column: "ShowcaseSlug",
                unique: true,
                filter: "\"ShowcaseSlug\" IS NOT NULL");
        }
    }
}
