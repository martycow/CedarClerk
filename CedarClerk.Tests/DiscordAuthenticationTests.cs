using System.Text.Json;
using CedarClerk.Server;
namespace CedarClerk.Tests;
public class DiscordAuthenticationTests
{
    [Theory]
    [InlineData("{\"verified\":true,\"email\":\"reader@example.test\"}", true)]
    [InlineData("{\"verified\":false,\"email\":\"reader@example.test\"}", false)]
    [InlineData("{\"email\":\"reader@example.test\"}", false)]
    [InlineData("{\"verified\":true,\"email\":null}", false)]
    public void Sign_in_requires_a_verified_address(string json, bool expected)
    {
        using var user = JsonDocument.Parse(json);
        Assert.Equal(expected, DiscordAuthentication.HasVerifiedEmail(user.RootElement));
    }
}
