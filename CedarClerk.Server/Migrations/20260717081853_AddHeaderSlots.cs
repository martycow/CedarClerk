using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddHeaderSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorDisplayName",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "HeaderSlot1Type",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "HeaderSlot2Type",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "HeaderSlot3Type",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileLocation",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileUrl",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorDisplayName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HeaderSlot1Type",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HeaderSlot2Type",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HeaderSlot3Type",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfileLocation",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfileUrl",
                table: "AspNetUsers");
        }
    }
}
