using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSocialLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SocialFacebookUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialGithubUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialInstagramUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialTwitterUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialYoutubeUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SocialFacebookUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialGithubUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialInstagramUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialTwitterUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SocialYoutubeUrl",
                table: "AspNetUsers");
        }
    }
}
