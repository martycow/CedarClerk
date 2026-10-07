using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Publishing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CedarClerk.Tests;

// ADR-315. "Send preview to me" goes to the author's own chat with the bot and records nothing.
// The guards around the send are decided here; the Bot API call itself needs a bot, which a test
// does not have, so the "bot is down" case proves the draft is left untouched.
public class PreviewToMeTests : IDisposable
{
    private const string Owner = "o1";
    private const string ParagraphJson =
        """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Body."}]}]}""";

    private readonly string mediaDir = Path.Combine(Path.GetTempPath(), "cedar-preview-" + Guid.NewGuid().ToString("N"));

    public PreviewToMeTests() => Directory.CreateDirectory(mediaDir);
    public void Dispose() { try { Directory.Delete(mediaDir, true); } catch (IOException) { } }

    private static Draft Seed(CedarDbContext db, long? telegramUserId)
    {
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = Owner, TelegramUserId = telegramUserId });
        var draft = new Draft { OwnerId = Owner, Title = "Devlog", CedarJson = ParagraphJson, PrimaryLanguage = "en" };
        db.Drafts.Add(draft);
        db.SaveChanges();
        return draft;
    }

    private TelegramPublishTarget Target(CedarDbContext db)
    {
        var cfg = new ConfigurationBuilder().Build();
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var bot = new TelegramBotService(cfg, NullLogger<TelegramBotService>.Instance, scopes,
            new ProductAnalytics(null, cfg, NullLogger<ProductAnalytics>.Instance));
        return new TelegramPublishTarget(db, bot, cfg, new MediaPaths(mediaDir),
            new MediaGrant(DataProtectionProvider.Create("cedar-preview-test")), NullLogger<TelegramPublishTarget>.Instance);
    }

    [Fact]
    public async Task An_account_without_linked_telegram_is_told_to_link_it()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db, telegramUserId: null);

        var result = await PostEndpoints.SendPreviewToOwnerAsync(draft.Id, Owner, null, db, [Target(db)]);

        Assert.Equal(ErrorMessages.PreviewNeedsTelegram, result.Error);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Another_accounts_draft_is_not_found()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db, telegramUserId: 42);

        var result = await PostEndpoints.SendPreviewToOwnerAsync(draft.Id, "someone-else", null, db, [Target(db)]);

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    [Fact]
    public async Task With_the_bot_down_the_preview_fails_and_the_draft_is_untouched()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db, telegramUserId: 42);

        var result = await PostEndpoints.SendPreviewToOwnerAsync(draft.Id, Owner, null, db, [Target(db)]);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Null(draft.LastTelegramMessageId);
        Assert.Null(draft.LastTelegramChatId);
        Assert.Empty(db.ChannelPosts);
        Assert.Empty(db.DraftRevisions);
    }

    [Fact]
    public void The_two_new_messages_exist_in_every_language_table()
    {
        // ErrorMessageLocalizationTests covers the tables; this pins that the texts are not empty.
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.PreviewNeedsTelegram));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.PreviewBotNotStarted));
    }
}
