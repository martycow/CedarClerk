using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CedarClerk.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWave2Rhythm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PinAfterSend",
                table: "ScheduledPosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Silent",
                table: "ScheduledPosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SlotId",
                table: "ScheduledPosts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CtaButtonsJson",
                table: "Drafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvergreenCategory",
                table: "Drafts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EvergreenMaxSends",
                table: "Drafts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvergreenSendCount",
                table: "Drafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvergreenUntil",
                table: "Drafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEvergreen",
                table: "Drafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LastPinnedMessageId",
                table: "Channels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostSignature",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostSignatureTranslationsJson",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostSignatureUrl",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelInviteLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    InviteLink = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelInviteLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelMemberDailies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InviteLinkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Joins = table.Column<int>(type: "INTEGER", nullable: false),
                    Leaves = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMemberDailies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeUtcMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueSlots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackedLinkClickDailies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackedLinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Day = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Clicks = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedLinkClickDailies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackedLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    DraftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Network = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClickCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastClickAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPosts_SlotId_ScheduledAtUtc",
                table: "ScheduledPosts",
                columns: new[] { "SlotId", "ScheduledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelInviteLinks_InviteLink",
                table: "ChannelInviteLinks",
                column: "InviteLink",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelInviteLinks_OwnerId_ChannelId",
                table: "ChannelInviteLinks",
                columns: new[] { "OwnerId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMemberDailies_OwnerId_ChannelId_Day_InviteLinkId",
                table: "ChannelMemberDailies",
                columns: new[] { "OwnerId", "ChannelId", "Day", "InviteLinkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueSlots_OwnerId_TargetId",
                table: "QueueSlots",
                columns: new[] { "OwnerId", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedLinkClickDailies_TrackedLinkId_Day",
                table: "TrackedLinkClickDailies",
                columns: new[] { "TrackedLinkId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedLinks_Code",
                table: "TrackedLinks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedLinks_OwnerId_DraftId",
                table: "TrackedLinks",
                columns: new[] { "OwnerId", "DraftId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelInviteLinks");

            migrationBuilder.DropTable(
                name: "ChannelMemberDailies");

            migrationBuilder.DropTable(
                name: "QueueSlots");

            migrationBuilder.DropTable(
                name: "TrackedLinkClickDailies");

            migrationBuilder.DropTable(
                name: "TrackedLinks");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledPosts_SlotId_ScheduledAtUtc",
                table: "ScheduledPosts");

            migrationBuilder.DropColumn(
                name: "PinAfterSend",
                table: "ScheduledPosts");

            migrationBuilder.DropColumn(
                name: "Silent",
                table: "ScheduledPosts");

            migrationBuilder.DropColumn(
                name: "SlotId",
                table: "ScheduledPosts");

            migrationBuilder.DropColumn(
                name: "CtaButtonsJson",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "EvergreenCategory",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "EvergreenMaxSends",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "EvergreenSendCount",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "EvergreenUntil",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "IsEvergreen",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "LastPinnedMessageId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "PostSignature",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "PostSignatureTranslationsJson",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "PostSignatureUrl",
                table: "Channels");
        }
    }
}
