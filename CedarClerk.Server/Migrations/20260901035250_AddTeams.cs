using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    MemberUserId = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "editor"),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    InviteToken = table.Column<string>(type: "TEXT", nullable: true),
                    InvitedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    InvitedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StatusNote = table.Column<string>(type: "TEXT", nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TeamId",
                table: "Projects",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_InviteToken",
                table: "TeamMembers",
                column: "InviteToken",
                unique: true,
                filter: "\"InviteToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_MemberUserId",
                table: "TeamMembers",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_Email",
                table: "TeamMembers",
                columns: new[] { "TeamId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_MemberUserId",
                table: "TeamMembers",
                columns: new[] { "TeamId", "MemberUserId" },
                unique: true,
                filter: "\"MemberUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_OwnerId",
                table: "Teams",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Projects_TeamId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Projects");
        }
    }
}
