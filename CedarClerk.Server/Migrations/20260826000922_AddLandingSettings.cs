using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LandingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KickerEn = table.Column<string>(type: "TEXT", nullable: true),
                    KickerRu = table.Column<string>(type: "TEXT", nullable: true),
                    HeroTitleEn = table.Column<string>(type: "TEXT", nullable: true),
                    HeroTitleRu = table.Column<string>(type: "TEXT", nullable: true),
                    HeroSubEn = table.Column<string>(type: "TEXT", nullable: true),
                    HeroSubRu = table.Column<string>(type: "TEXT", nullable: true),
                    ProofEn = table.Column<string>(type: "TEXT", nullable: true),
                    ProofRu = table.Column<string>(type: "TEXT", nullable: true),
                    NoteEn = table.Column<string>(type: "TEXT", nullable: true),
                    NoteRu = table.Column<string>(type: "TEXT", nullable: true),
                    ShowcaseBlog = table.Column<string>(type: "TEXT", nullable: true),
                    ShowShots = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowFeatures = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowPricing = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowRoadmap = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowStory = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShotsJson = table.Column<string>(type: "TEXT", nullable: true),
                    RoadmapJson = table.Column<string>(type: "TEXT", nullable: true),
                    StoryJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandingSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LandingSettings");
        }
    }
}
