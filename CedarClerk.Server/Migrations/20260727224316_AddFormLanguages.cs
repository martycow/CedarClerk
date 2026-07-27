using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFormLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: EF's generated default is "", which would leave every preset that
            // predates FI4.1 claiming no language at all. They were all written in the primary
            // language, so that is what they get.
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "FormPresets",
                type: "TEXT",
                nullable: false,
                defaultValue: "ru");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationFormTranslationsJson",
                table: "Drafts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "FormPresets");

            migrationBuilder.DropColumn(
                name: "RegistrationFormTranslationsJson",
                table: "Drafts");
        }
    }
}
