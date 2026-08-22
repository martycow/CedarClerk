using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMoreSocialLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SocialBlueskyUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialItchUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialRedditUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialSteamUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialTelegramUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialThreadsUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SocialBlueskyUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialItchUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialRedditUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialSteamUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialTelegramUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialThreadsUrl",
                table: "AspNetUsers");
        }
    }
}
