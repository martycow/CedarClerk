using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetMetadataAndLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "AssetEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "AssetEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataForModifiedAt",
                table: "AssetEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SampleRate",
                table: "AssetEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ThumbnailForModifiedAt",
                table: "AssetEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "AssetEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EntityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromType = table.Column<string>(type: "TEXT", nullable: false),
                    FromId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToType = table.Column<string>(type: "TEXT", nullable: false),
                    ToId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntityLinks_FromType_FromId_ToType_ToId",
                table: "EntityLinks",
                columns: new[] { "FromType", "FromId", "ToType", "ToId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntityLinks_OwnerId_FromType_FromId",
                table: "EntityLinks",
                columns: new[] { "OwnerId", "FromType", "FromId" });

            migrationBuilder.CreateIndex(
                name: "IX_EntityLinks_OwnerId_ToType_ToId",
                table: "EntityLinks",
                columns: new[] { "OwnerId", "ToType", "ToId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntityLinks");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "AssetEntries");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "AssetEntries");

            migrationBuilder.DropColumn(
                name: "MetadataForModifiedAt",
                table: "AssetEntries");

            migrationBuilder.DropColumn(
                name: "SampleRate",
                table: "AssetEntries");

            migrationBuilder.DropColumn(
                name: "ThumbnailForModifiedAt",
                table: "AssetEntries");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "AssetEntries");
        }
    }
}
