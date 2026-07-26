using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveAndPreferenceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Drafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AppearancePrefsJson",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewDraftDefaultsJson",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolbarLayoutJson",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "AppearancePrefsJson",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NewDraftDefaultsJson",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ToolbarLayoutJson",
                table: "AspNetUsers");
        }
    }
}
