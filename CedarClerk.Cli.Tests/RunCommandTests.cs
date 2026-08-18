using CedarClerk.Cli.Commands;
using CedarClerk.Cli.Configuration;

namespace CedarClerk.Cli.Tests;

// The serve environment is the safety of `cedar run`: get it wrong and the published server either
// long-polls the production bot token (the 409 in .claude/rules/telegram-bot.md) or migrates a
// database that the next build deletes.
public class RunCommandTests
{
    private static CliConfig Config() => new() { RepoRoot = @"D:\repo" };

    [Fact]
    public void Bot_token_is_blanked_with_whitespace_not_removed()
    {
        var token = RunCommand.ServeEnvironment(Config()).Single(p => p.Key == "Cedar__BotToken");

        // A single space: IsNullOrWhiteSpace-off for TelegramBotService, while "" would DELETE the
        // variable on Windows and let a globally exported token through to the child.
        Assert.True(string.IsNullOrWhiteSpace(token.Value));
        Assert.NotEqual("", token.Value);
    }

    [Fact]
    public void Data_lives_in_the_dev_directory_not_in_publish()
    {
        var config = Config();
        var data = RunCommand.ServeEnvironment(config).Single(p => p.Key == CedarClerk.Core.Consts.DataDirectoryKey);

        Assert.Equal(Path.Combine(config.ServerProject, "data"), data.Value);
        Assert.DoesNotContain("publish", data.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serves_the_published_server_dll()
    {
        Assert.Equal(Path.Combine(@"D:\repo", "publish", "CedarClerk.Server.dll"), RunCommand.ServerDll(Config()));
    }
}
