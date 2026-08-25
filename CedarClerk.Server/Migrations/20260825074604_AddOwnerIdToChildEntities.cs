using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerIdToChildEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Reactions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "PostRegistrations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "PostInvites",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "PollVotes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "DraftTranslations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "DraftTargetTexts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "DraftStatSnapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "DraftRevisions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Comments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "ChannelStatSnapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "ChannelPosts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");


            // Every column above landed with defaultValue "" — EF's only option for a NOT NULL
            // column on a table that already has rows. An owner of "" matches no tenant, so
            // without this block every existing translation, comment, reaction and revision would
            // vanish from its owner's screens the moment the filters came on.
            //
            // Each row takes the owner of its parent, which is where that fact already lives.
            // EXISTS rather than a bare correlated subquery: an orphan (a child whose parent was
            // deleted before foreign keys covered it) would otherwise write NULL into a NOT NULL
            // column and abort the whole migration. An orphan keeps "" and stays invisible, which
            // is the honest answer — nobody owns it.

            migrationBuilder.Sql("""
                UPDATE "Reactions" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "Reactions"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "Reactions"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "PostRegistrations" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "PostRegistrations"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "PostRegistrations"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "PostInvites" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "PostInvites"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "PostInvites"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "PollVotes" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "PollVotes"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "PollVotes"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "DraftTranslations" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "DraftTranslations"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "DraftTranslations"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "DraftTargetTexts" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "DraftTargetTexts"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "DraftTargetTexts"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "DraftStatSnapshots" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "DraftStatSnapshots"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "DraftStatSnapshots"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "DraftRevisions" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "DraftRevisions"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "DraftRevisions"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "Comments" SET "OwnerId" = (SELECT p."OwnerId" FROM "Drafts" p WHERE p."Id" = "Comments"."DraftId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Drafts" p WHERE p."Id" = "Comments"."DraftId");
                """);

            migrationBuilder.Sql("""
                UPDATE "ChannelStatSnapshots" SET "OwnerId" = (SELECT p."OwnerId" FROM "Channels" p WHERE p."Id" = "ChannelStatSnapshots"."ChannelId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Channels" p WHERE p."Id" = "ChannelStatSnapshots"."ChannelId");
                """);

            migrationBuilder.Sql("""
                UPDATE "ChannelPosts" SET "OwnerId" = (SELECT p."OwnerId" FROM "Channels" p WHERE p."Id" = "ChannelPosts"."ChannelId")
                WHERE "OwnerId" = '' AND EXISTS (SELECT 1 FROM "Channels" p WHERE p."Id" = "ChannelPosts"."ChannelId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_OwnerId_DraftId",
                table: "Reactions",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostRegistrations_OwnerId_DraftId",
                table: "PostRegistrations",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostInvites_OwnerId_DraftId",
                table: "PostInvites",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_PollVotes_OwnerId_DraftId",
                table: "PollVotes",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftTranslations_OwnerId_DraftId",
                table: "DraftTranslations",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftTargetTexts_OwnerId_DraftId",
                table: "DraftTargetTexts",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftStatSnapshots_OwnerId_DraftId",
                table: "DraftStatSnapshots",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftRevisions_OwnerId_DraftId",
                table: "DraftRevisions",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_OwnerId_DraftId",
                table: "Comments",
                columns: new[] { "OwnerId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelStatSnapshots_OwnerId_ChannelId",
                table: "ChannelStatSnapshots",
                columns: new[] { "OwnerId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelPosts_OwnerId_ChannelId",
                table: "ChannelPosts",
                columns: new[] { "OwnerId", "ChannelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reactions_OwnerId_DraftId",
                table: "Reactions");

            migrationBuilder.DropIndex(
                name: "IX_PostRegistrations_OwnerId_DraftId",
                table: "PostRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_PostInvites_OwnerId_DraftId",
                table: "PostInvites");

            migrationBuilder.DropIndex(
                name: "IX_PollVotes_OwnerId_DraftId",
                table: "PollVotes");

            migrationBuilder.DropIndex(
                name: "IX_DraftTranslations_OwnerId_DraftId",
                table: "DraftTranslations");

            migrationBuilder.DropIndex(
                name: "IX_DraftTargetTexts_OwnerId_DraftId",
                table: "DraftTargetTexts");

            migrationBuilder.DropIndex(
                name: "IX_DraftStatSnapshots_OwnerId_DraftId",
                table: "DraftStatSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_DraftRevisions_OwnerId_DraftId",
                table: "DraftRevisions");

            migrationBuilder.DropIndex(
                name: "IX_Comments_OwnerId_DraftId",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_ChannelStatSnapshots_OwnerId_ChannelId",
                table: "ChannelStatSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ChannelPosts_OwnerId_ChannelId",
                table: "ChannelPosts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Reactions");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "PostRegistrations");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "PostInvites");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "PollVotes");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "DraftTranslations");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "DraftTargetTexts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "DraftStatSnapshots");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "DraftRevisions");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "ChannelStatSnapshots");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "ChannelPosts");
        }
    }
}
