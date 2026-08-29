using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// Wave 2 item 11. The channel signature override is a trio that moves together: setting it needs
// the same Pro gate the owner-level signature has, clearing it is free on every plan, and neither
// direction may ever reach across owners.
public class ChannelSignatureTests
{
    private static (ServiceProvider Provider, SqliteConnection Connection) Build()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddScoped<TenantProvider>();
        services.AddDbContext<CedarDbContext>(o => o.UseSqlite(connection));
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreatePlatformScope())
            scope.ServiceProvider.GetRequiredService<CedarDbContext>().Database.EnsureCreated();

        return (provider, connection);
    }

    private static async Task<Guid> SeedAsync(ServiceProvider provider, string ownerId, PlanTiers plan,
        (string Text, string? Json, string? Url)? existing = null)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        db.Users.Add(new ApplicationUser { Id = ownerId, UserName = ownerId, Email = $"{ownerId}@test.local", PlanTier = plan });
        var channel = new Channel
        {
            OwnerId = ownerId, Title = "Chan", TelegramChatId = -100,
            PostSignature = existing?.Text,
            PostSignatureTranslationsJson = existing?.Json,
            PostSignatureUrl = existing?.Url,
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

    private static async Task<(int Status, string? Error, Channel? Channel)> PatchAsync(
        ServiceProvider provider, string uid, Guid channelId, ChannelEndpoints.SignaturePatchRequest req)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        return await ChannelEndpoints.ApplySignatureAsync(db, uid, channelId, req);
    }

    private static async Task<Channel> LoadAsync(ServiceProvider provider, Guid channelId)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        return await db.Channels.FirstAsync(c => c.Id == channelId);
    }

    [Fact]
    public async Task Another_owners_channel_is_404_not_403()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var channelId = await SeedAsync(provider, "owner-1", PlanTiers.Pro);
            using (var scope = provider.CreatePlatformScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
                db.Users.Add(new ApplicationUser { Id = "owner-2", UserName = "owner-2", Email = "owner-2@test.local", PlanTier = PlanTiers.Pro });
                await db.SaveChangesAsync();
            }

            var (status, _, _) = await PatchAsync(provider, "owner-2", channelId,
                new ChannelEndpoints.SignaturePatchRequest("mine now", null, null));

            Assert.Equal(StatusCodes.Status404NotFound, status);
            Assert.Null((await LoadAsync(provider, channelId)).PostSignature);
        }
    }

    [Fact]
    public async Task A_pro_owner_sets_the_trio_trimmed()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var channelId = await SeedAsync(provider, "owner-1", PlanTiers.Pro);

            var (status, _, _) = await PatchAsync(provider, "owner-1", channelId,
                new ChannelEndpoints.SignaturePatchRequest("  — the channel desk  ", """{"en":"the desk"}""", " https://example.com "));

            Assert.Equal(StatusCodes.Status200OK, status);
            var stored = await LoadAsync(provider, channelId);
            Assert.Equal("— the channel desk", stored.PostSignature);
            Assert.Equal("the desk", LocalizedTextMap.All(stored.PostSignatureTranslationsJson)["en"]);
            Assert.Equal("https://example.com", stored.PostSignatureUrl);
        }
    }

    [Fact]
    public async Task A_free_owner_cannot_set_a_channel_signature()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var channelId = await SeedAsync(provider, "owner-1", PlanTiers.Free);

            var (status, error, _) = await PatchAsync(provider, "owner-1", channelId,
                new ChannelEndpoints.SignaturePatchRequest("custom", null, null));

            Assert.Equal(StatusCodes.Status403Forbidden, status);
            Assert.Equal(ErrorMessages.SignatureIsPro, error);
            Assert.Null((await LoadAsync(provider, channelId)).PostSignature);
        }
    }

    [Fact]
    public async Task Null_signature_clears_the_whole_trio_on_any_plan()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var channelId = await SeedAsync(provider, "owner-1", PlanTiers.Free,
                existing: ("old", """{"en":"old"}""", "https://old.example"));

            var (status, _, _) = await PatchAsync(provider, "owner-1", channelId,
                new ChannelEndpoints.SignaturePatchRequest(null, null, null));

            Assert.Equal(StatusCodes.Status200OK, status);
            var stored = await LoadAsync(provider, channelId);
            Assert.Null(stored.PostSignature);
            Assert.Null(stored.PostSignatureTranslationsJson);
            Assert.Null(stored.PostSignatureUrl);
        }
    }

    [Fact]
    public async Task A_whitespace_signature_counts_as_clearing()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var channelId = await SeedAsync(provider, "owner-1", PlanTiers.Pro,
                existing: ("old", null, null));

            var (status, _, _) = await PatchAsync(provider, "owner-1", channelId,
                new ChannelEndpoints.SignaturePatchRequest("   ", """{"en":"kept?"}""", "https://kept.example"));

            Assert.Equal(StatusCodes.Status200OK, status);
            var stored = await LoadAsync(provider, channelId);
            Assert.Null(stored.PostSignature);
            Assert.Null(stored.PostSignatureTranslationsJson);
            Assert.Null(stored.PostSignatureUrl);
        }
    }

    [Fact]
    public async Task Oversized_translations_are_refused_with_their_own_code()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var channelId = await SeedAsync(provider, "owner-1", PlanTiers.Pro);
            var blob = $$"""{"en":"{{new string('x', ChannelEndpoints.SignatureTranslationsMaxChars)}}"}""";

            var (status, error, _) = await PatchAsync(provider, "owner-1", channelId,
                new ChannelEndpoints.SignaturePatchRequest("sig", blob, null));

            Assert.Equal(StatusCodes.Status400BadRequest, status);
            Assert.Equal(ErrorMessages.SignatureTranslationsTooLarge, error);
        }
    }

    [Fact]
    public void Malformed_or_blank_translations_normalize_to_null()
    {
        Assert.Null(ChannelEndpoints.NormalizeTranslations("{not json"));
        Assert.Null(ChannelEndpoints.NormalizeTranslations(null));
        Assert.Null(ChannelEndpoints.NormalizeTranslations("""{"en":"  "}"""));
    }

    [Fact]
    public void Valid_translations_round_trip_through_normalization()
    {
        var normalized = ChannelEndpoints.NormalizeTranslations("""{"en":"the desk","de":"der Tisch","fr":""}""");

        var map = LocalizedTextMap.All(normalized);
        Assert.Equal(2, map.Count);
        Assert.Equal("the desk", map["en"]);
        Assert.Equal("der Tisch", map["de"]);
    }
}
