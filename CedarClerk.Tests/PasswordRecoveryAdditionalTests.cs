using System.Net;
using System.Text;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Email;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

public class PasswordRecoveryAdditionalTests
{
    private sealed class Mail : HttpMessageHandler, IHttpClientFactory
    {
        public string? Body { get; private set; }
        public HttpClient CreateClient(string name) => new(this, false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly ServiceProvider services;
        private readonly IServiceScope scope;
        public readonly Mail Mail = new();
        public readonly RecoveryMailLimit MailLimit = new();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Consts.Email.ResendApiKeyCfg] = "test-only-key",
            [Consts.General.MainHostCfg] = "https://cedar.example",
        }).Build();
        public UserManager<ApplicationUser> Users => scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        public ResendEmailProvider Email => scope.ServiceProvider.GetRequiredService<ResendEmailProvider>();
        public IDataProtectionProvider Protection => services.GetRequiredService<IDataProtectionProvider>();

        public Fixture()
        {
            connection.Open();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton(Configuration);
            collection.AddSingleton<IHttpClientFactory>(Mail);
            collection.AddSingleton<ResendEmailProvider>();
            collection.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            collection.AddSingleton(TenantProvider.Platform());
            collection.AddDbContext<CedarDbContext>(o => o.UseSqlite(connection));
            collection.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Tokens.PasswordResetTokenProvider = PasswordRecoveryTokenProvider.ProviderName;
            }).AddEntityFrameworkStores<CedarDbContext>().AddDefaultTokenProviders()
                .AddTokenProvider<PasswordRecoveryTokenProvider>(PasswordRecoveryTokenProvider.ProviderName);
            services = collection.BuildServiceProvider();
            scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<CedarDbContext>().Database.EnsureCreated();
        }

        public async Task<ApplicationUser> User(string email = "author@example.test", bool confirmed = true, bool password = true)
        {
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = confirmed };
            var result = password ? await Users.CreateAsync(user, "Original-Password8") : await Users.CreateAsync(user);
            Assert.True(result.Succeeded);
            return user;
        }

        public void Dispose() { scope.Dispose(); services.Dispose(); connection.Dispose(); Mail.Dispose(); MailLimit.Dispose(); }
    }

    private static int? Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;
    private static int Status(IdentityResult result) => result.Succeeded ? 200 : 400;
    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    [Fact]
    public async Task Emailed_link_resets_once_and_invalidates_old_password_and_stamp()
    {
        using var f = new Fixture();
        var user = await f.User();
        var stamp = user.SecurityStamp;
        Assert.Equal(200, Status(await PasswordRecoveryEndpoints.ForgotAsync(new(user.Email), f.Users, f.Email, f.Configuration, f.MailLimit)));
        using var mail = JsonDocument.Parse(f.Mail.Body!);
        var html = WebUtility.HtmlDecode(mail.RootElement.GetProperty("html").GetString()!);
        var link = html.Split("href=\"")[1].Split('"')[0];
        Assert.StartsWith("https://cedar.example/reset-password#", link);
        Assert.DoesNotContain("?", link);
        var parts = QueryHelpers.ParseQuery(new Uri(link).Fragment[1..]);
        var request = new PasswordRecoveryEndpoints.ResetRequest(parts["userId"], parts["token"], "Replacement-Password9");
        Assert.Equal(200, Status(await PasswordRecoveryEndpoints.ResetAsync(request, f.Users)));
        Assert.False(await f.Users.CheckPasswordAsync(user, "Original-Password8"));
        Assert.True(await f.Users.CheckPasswordAsync(user, "Replacement-Password9"));
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.Equal(400, Status(await PasswordRecoveryEndpoints.ResetAsync(request, f.Users)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Ineligible_and_unknown_accounts_have_identical_responses_without_mail(bool confirmed, bool password)
    {
        using var f = new Fixture();
        var user = await f.User(confirmed: confirmed, password: password);
        var known = await PasswordRecoveryEndpoints.ForgotAsync(new(user.Email), f.Users, f.Email, f.Configuration, f.MailLimit);
        var unknown = await PasswordRecoveryEndpoints.ForgotAsync(new("missing@example.test"), f.Users, f.Email, f.Configuration, f.MailLimit);
        Assert.Equal(200, Status(known));
        Assert.Equal(Status(known), Status(unknown));
        Assert.Null(f.Mail.Body);
    }

    [Fact]
    public async Task Wrong_user_malformed_token_and_weak_password_cannot_reset()
    {
        using var f = new Fixture();
        var owner = await f.User();
        var other = await f.User("other@example.test");
        var token = Encode(await f.Users.GeneratePasswordResetTokenAsync(owner));
        Assert.Equal(400, Status(await PasswordRecoveryEndpoints.ResetAsync(new(other.Id, token, "Replacement-Password9"), f.Users)));
        Assert.Equal(400, Status(await PasswordRecoveryEndpoints.ResetAsync(new(owner.Id, "!broken", "Replacement-Password9"), f.Users)));
        Assert.Equal(400, Status(await PasswordRecoveryEndpoints.ResetAsync(new(owner.Id, token, "weak"), f.Users)));
        Assert.True(await f.Users.CheckPasswordAsync(owner, "Original-Password8"));
        Assert.True(await f.Users.CheckPasswordAsync(other, "Original-Password8"));
    }

    [Theory]
    [InlineData(-120, 400)]
    [InlineData(-30, 200)]
    public async Task Protected_token_obeys_one_hour_lifetime(int ageMinutes, int expectedStatus)
    {
        using var f = new Fixture();
        var user = await f.User();
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
        {
            writer.Write(DateTimeOffset.UtcNow.AddMinutes(ageMinutes).UtcTicks);
            writer.Write(user.Id);
            writer.Write("ResetPassword");
            writer.Write(user.SecurityStamp!);
        }
        var protectedBytes = f.Protection.CreateProtector(PasswordRecoveryTokenProvider.ProviderName).Protect(bytes.ToArray());
        var token = Encode(Convert.ToBase64String(protectedBytes));
        Assert.Equal(expectedStatus, Status(await PasswordRecoveryEndpoints.ResetAsync(new(user.Id, token, "Replacement-Password9"), f.Users)));
        Assert.Equal(expectedStatus == 400, await f.Users.CheckPasswordAsync(user, "Original-Password8"));
    }

    [Theory]
    [InlineData("google", "id", "secret", "Google")]
    [InlineData("Google", "id", "secret", "Google")]
    [InlineData("GOOGLE", "id", "secret", "Google")]
    [InlineData("other", "id", "secret", null)]
    [InlineData("google", "id", "", null)]
    public void Google_url_resolves_to_registered_scheme(string provider, string id, string secret, string? expected)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Consts.ExternalAuth.GoogleClientIdCfg] = id,
            [Consts.ExternalAuth.GoogleClientSecretCfg] = secret,
        }).Build();
        Assert.Equal(expected, ExternalAuthEndpoints.GoogleScheme(provider, cfg));
    }
}
