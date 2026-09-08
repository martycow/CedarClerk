using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class ProjectModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProjectType",
                table: "Projects",
                newName: "CreatedFromPreset");

            migrationBuilder.CreateTable(
                name: "ProjectModules",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModuleKey = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectModules", x => new { x.ProjectId, x.ModuleKey });
                    table.ForeignKey(
                        name: "FK_ProjectModules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ADR-293 — the backfill rides in the same migration so no deploy sees the table without
            // its rows. The matrix is a frozen copy of ProjectModules' presets as of this migration:
            // a later change to the class must not rewrite what old projects were given.
            migrationBuilder.Sql("UPDATE \"Projects\" SET \"CreatedFromPreset\" = 'fullgame' WHERE \"CreatedFromPreset\" IN ('jam', 'prototype');");
            migrationBuilder.Sql("UPDATE \"Projects\" SET \"CreatedFromPreset\" = 'product' WHERE \"CreatedFromPreset\" = 'released';");
            foreach (var (preset, enabled) in Presets)
            {
                var keys = string.Join(" UNION ALL ", Keys.Select(k =>
                    $"SELECT '{k}' AS \"ModuleKey\", {(enabled.Contains(k) ? 1 : 0)} AS \"Enabled\""));
                migrationBuilder.Sql(
                    "INSERT INTO \"ProjectModules\" (\"ProjectId\", \"ModuleKey\", \"OwnerId\", \"Enabled\") " +
                    $"SELECT p.\"Id\", k.\"ModuleKey\", p.\"OwnerId\", k.\"Enabled\" FROM \"Projects\" p, ({keys}) k " +
                    $"WHERE p.\"CreatedFromPreset\" = '{preset}';");
            }
        }

        private static readonly string[] Keys =
            ["documents", "assets", "site", "posts", "calendar", "metrics", "tasks", "planner", "builds", "canvas", "dialogues"];

        private static readonly (string Preset, string[] Enabled)[] Presets =
        [
            ("empty", ["documents"]),
            ("blog", ["documents", "assets", "site", "posts", "calendar", "metrics"]),
            ("work", ["documents", "site", "posts", "metrics", "canvas"]),
            ("product", ["documents", "site", "posts", "metrics", "tasks", "planner", "builds"]),
            ("fullgame", ["documents", "assets", "posts", "metrics", "tasks", "planner", "builds", "canvas", "dialogues"]),
            ("vault", ["documents"]),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectModules");

            migrationBuilder.RenameColumn(
                name: "CreatedFromPreset",
                table: "Projects",
                newName: "ProjectType");
        }
    }
}
