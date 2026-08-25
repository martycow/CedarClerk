using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantUsername",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantUsername",
                table: "AspNetUsers",
                column: "TenantUsername",
                unique: true,
                filter: "\"TenantUsername\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantUsername",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantUsername",
                table: "AspNetUsers");
        }
    }
}
