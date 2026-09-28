using CedarClerk.Core;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Bot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CedarClerk.Tests;

public class TelegramBotDisabledTests
{
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
