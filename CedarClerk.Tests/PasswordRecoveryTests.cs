using System.Text;
using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using CedarClerk.Server.Email;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CedarClerk.Tests;

public class PasswordRecoveryTests
{
    [Fact]
    public async Task Mail_contains_a_working_reset_link_and_unknown_addresses_get_the_same_response()
    {
        using var fixture = new IdentityFixture();
        var user = await fixture.User(true);
        var unconfirmed = await fixture.User(false);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Consts.Email.ResendApiKeyCfg] = "test-only",
            [Consts.General.MainHostCfg] = "https://cedar.example.test",
        }).Build();
        var mailbox = new Mailbox();
        var email = new ResendEmailProvider(mailbox, config, NullLogger<ResendEmailProvider>.Instance);
        using var limit = new RecoveryMailLimit();
        foreach (var address in new[] { "unknown@example.test", unconfirmed.Email, user.Email })
        {
            var result = await PasswordRecoveryEndpoints.ForgotAsync(new(address), fixture.Users, email, config, limit);
            Assert.Equal(200, ((IStatusCodeHttpResult)result).StatusCode);
        }
        var message = Assert.Single(mailbox.Messages);
        using var json = JsonDocument.Parse(message);
        var html = json.RootElement.GetProperty("html").GetString()!;
        var link = WebUtility.HtmlDecode(Regex.Match(html, "href=\"([^\"]+)\"").Groups[1].Value);
        var uri = new Uri(link);
        Assert.Equal("cedar.example.test", uri.Host);
        Assert.Equal("/reset-password", uri.AbsolutePath);
        Assert.Empty(uri.Query);
        var parameters = QueryHelpers.ParseQuery(uri.Fragment[1..]);
        Assert.True((await PasswordRecoveryEndpoints.ResetAsync(new(parameters["userId"], parameters["token"], "New-Password42!"), fixture.Users)).Succeeded);
        config[Consts.Email.ResendApiKeyCfg] = null;
        var unavailable = await PasswordRecoveryEndpoints.ForgotAsync(new(user.Email), fixture.Users, email, config, limit);
        Assert.Equal(503, ((IStatusCodeHttpResult)unavailable).StatusCode);
    }

    [Theory]
    [InlineData("google", true)]
    [InlineData("Google", true)]
    [InlineData("GOOGLE", true)]
    [InlineData("telegram", false)]
    public void Google_route_resolves_without_case_sensitive_provider_names(string provider, bool expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Consts.ExternalAuth.GoogleClientIdCfg] = "test-client",
            [Consts.ExternalAuth.GoogleClientSecretCfg] = "test-secret",
        }).Build();
        Assert.Equal(expected, (ExternalAuthEndpoints.GoogleScheme(provider, config) is not null));
        config[Consts.ExternalAuth.GoogleClientSecretCfg] = null;
        Assert.False((ExternalAuthEndpoints.GoogleScheme(provider, config) is not null));
    }

    [Fact]
    public async Task Reset_changes_password_invalidates_token_and_preserves_lockout()
    {
        using var fixture = new IdentityFixture();
        var user = await fixture.User(true);
        user.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);
        await fixture.Users.UpdateAsync(user);
        var stamp = user.SecurityStamp;
        var token = await fixture.Token(user);
        var request = new PasswordRecoveryEndpoints.ResetRequest(user.Id, token, "New-Password42!");
        Assert.True((await PasswordRecoveryEndpoints.ResetAsync(request, fixture.Users)).Succeeded);
        Assert.True(await fixture.Users.CheckPasswordAsync(user, "New-Password42!"));
        Assert.False(await fixture.Users.CheckPasswordAsync(user, "Old-Password42!"));
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.False((await PasswordRecoveryEndpoints.ResetAsync(request, fixture.Users)).Succeeded);
    }

    [Fact]
    public async Task Forged_wrong_user_and_confirmation_tokens_cannot_reset_password()
    {
        using var fixture = new IdentityFixture();
        var user = await fixture.User(true);
        var another = await fixture.User(true);
        var token = await fixture.Token(user);
        var confirmation = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await fixture.Users.GenerateEmailConfirmationTokenAsync(user)));
        foreach (var request in new[]
        {
            new PasswordRecoveryEndpoints.ResetRequest(user.Id, "not-a-token!", "New-Password42!"),
            new PasswordRecoveryEndpoints.ResetRequest(another.Id, token, "New-Password42!"),
            new PasswordRecoveryEndpoints.ResetRequest(user.Id, confirmation, "New-Password42!"),
            new PasswordRecoveryEndpoints.ResetRequest(user.Id, token, "weakpass"),
            new PasswordRecoveryEndpoints.ResetRequest(null, null, null),
        }) Assert.False((await PasswordRecoveryEndpoints.ResetAsync(request, fixture.Users)).Succeeded);
        Assert.True(await fixture.Users.CheckPasswordAsync(user, "Old-Password42!"));
    }

    [Fact]
    public async Task Unconfirmed_accounts_cannot_use_recovery()
    {
        using var fixture = new IdentityFixture();
        var user = await fixture.User(false);
        var token = await fixture.Token(user);
        Assert.False((await PasswordRecoveryEndpoints.ResetAsync(new(user.Id, token, "New-Password42!"), fixture.Users)).Succeeded);
    }

    private sealed class IdentityFixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly ServiceProvider services;
        private readonly IServiceScope scope;
        public UserManager<ApplicationUser> Users { get; }

        public IdentityFixture()
        {
            connection.Open();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            collection.AddScoped(_ => TenantProvider.Platform());
            collection.AddDbContext<CedarDbContext>(o => o.UseSqlite(connection));
            collection.AddIdentityCore<ApplicationUser>(o => o.Password.RequiredLength = 8)
                .AddEntityFrameworkStores<CedarDbContext>().AddDefaultTokenProviders();
            collection.AddPasswordRecovery();
            services = collection.BuildServiceProvider();
            scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<CedarDbContext>().Database.EnsureCreated();
            Users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        }

        public async Task<ApplicationUser> User(bool confirmed)
        {
            var email = $"{Guid.NewGuid():N}@example.test";
            var user = new ApplicationUser { Email = email, UserName = email, EmailConfirmed = confirmed };
            Assert.True((await Users.CreateAsync(user, "Old-Password42!")).Succeeded);
            return user;
        }

        public async Task<string> Token(ApplicationUser user) => WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(await Users.GeneratePasswordResetTokenAsync(user)));

        public void Dispose() { scope.Dispose(); services.Dispose(); connection.Dispose(); }
    }

    private sealed class Mailbox : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Messages { get; } = [];
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Messages.Add(await request.Content!.ReadAsStringAsync(token));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
