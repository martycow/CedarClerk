using CedarClerk.Core;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Bot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CedarClerk.Tests;

public class TelegramBotDisabledTests
{
    [Fact]
    public void Console_profile_disables_the_canonical_server_token_and_development_settings()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "cedar.json")))
            root = root.Parent;
        Assert.NotNull(root);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "cedar.json")));
        var profile = document.RootElement.GetProperty("programs")[0];
        var env = profile.GetProperty("serve").GetProperty("env");
        var tokenKey = Consts.Telegram.BotTokenCfg.Replace(":", "__");
        Assert.Equal(" ", env.GetProperty(tokenKey).GetString());
        Assert.Equal("LocalNoBot", env.GetProperty("ASPNETCORE_ENVIRONMENT").GetString());
        var smokeEnv = profile.GetProperty("actions").GetProperty("smoke").GetProperty("steps")[0].GetProperty("env");
        Assert.Equal(" ", smokeEnv.GetProperty(tokenKey).GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task Absent_or_whitespace_token_disables_the_bot_before_network_access(string? token)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [Consts.Telegram.BotTokenCfg] = token }).Build();
        using var services = new ServiceCollection().BuildServiceProvider();
        var analytics = new ProductAnalytics(null, config, NullLogger<ProductAnalytics>.Instance);
        using var bot = new TestBot(config, services.GetRequiredService<IServiceScopeFactory>(), analytics);

        await bot.ExecuteForTest(new CancellationToken(canceled: true));

        Assert.False(bot.IsRunning);
        Assert.Throws<InvalidOperationException>(() => bot.Client);
    }

    private sealed class TestBot(IConfiguration config, IServiceScopeFactory scopes, ProductAnalytics analytics)
        : TelegramBotService(config, NullLogger<TelegramBotService>.Instance, scopes, analytics)
    {
        public Task ExecuteForTest(CancellationToken token) => ExecuteAsync(token);
    }
}
