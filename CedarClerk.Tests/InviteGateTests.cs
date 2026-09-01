using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// T-003 / ADR-237 clause 2. The gate used to live inside the register handler, where only one
// caller could reach it; the external sign-in is the second, and a provider button that let people
// past it would open registration while /register still asked for a code. These tests are what say
// the two callers get the same answer.
public class InviteGateTests
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

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static Task<AuthEndpoints.InviteVerdict> ResolveAsync(
        ServiceProvider provider, string? submitted, IConfiguration cfg)
    {
        var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        return AuthEndpoints.ResolveInviteAsync(
            submitted, db, provider.GetRequiredService<IServiceScopeFactory>(), cfg);
    }

    private static async Task SeedCodeAsync(ServiceProvider provider, string code,
        bool active = true, int? maxUses = null, int uses = 0)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        db.InviteCodes.Add(new InviteCode { Code = code, IsActive = active, MaxUses = maxUses, Uses = uses });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Nothing_submitted_is_refused()
    {
        var (provider, connection) = Build();
        using var _ = connection;

        var verdict = await ResolveAsync(provider, null, Config());

        Assert.False(verdict.Admits);
        Assert.Equal(AuthEndpoints.InviteKind.None, verdict.Kind);
    }

    [Fact]
    public async Task A_live_code_admits_and_carries_the_row_to_charge()
    {
        var (provider, connection) = Build();
        using var _ = connection;
        await SeedCodeAsync(provider, "CEDAR-MOO-1");

        var verdict = await ResolveAsync(provider, "CEDAR-MOO-1", Config());

        Assert.True(verdict.Admits);
        Assert.Equal(AuthEndpoints.InviteKind.Code, verdict.Kind);
        // The completion step spends a use against this row; a verdict without it would let one
        // code make unlimited accounts.
        Assert.NotNull(verdict.Code);
    }

    [Fact]
    public async Task A_code_is_matched_regardless_of_case()
    {
        var (provider, connection) = Build();
        using var _ = connection;
        await SeedCodeAsync(provider, "CEDAR-MOO-1");

        Assert.True((await ResolveAsync(provider, "cedar-moo-1", Config())).Admits);
        Assert.True((await ResolveAsync(provider, "  CEDAR-MOO-1  ", Config())).Admits);
    }

    [Fact]
    public async Task A_spent_code_is_refused()
    {
        var (provider, connection) = Build();
        using var _ = connection;
        await SeedCodeAsync(provider, "SPENT", maxUses: 1, uses: 1);

        Assert.False((await ResolveAsync(provider, "SPENT", Config())).Admits);
    }

    [Fact]
    public async Task A_deactivated_code_is_refused()
    {
        var (provider, connection) = Build();
        using var _ = connection;
        await SeedCodeAsync(provider, "OFF", active: false);

        Assert.False((await ResolveAsync(provider, "OFF", Config())).Admits);
    }

    [Fact]
    public async Task The_config_code_is_the_fallback_and_carries_no_row()
    {
        var (provider, connection) = Build();
        using var _ = connection;

        var verdict = await ResolveAsync(provider, "from-the-drop-in",
            Config((Consts.General.InviteCodeCfg, "from-the-drop-in")));

        Assert.Equal(AuthEndpoints.InviteKind.ConfigCode, verdict.Kind);
        // Deliberately null: there is no code row to point at, and inventing one would make the
        // attribution list lie.
        Assert.Null(verdict.Code);
    }

    [Fact]
    public async Task The_config_code_is_exact_where_a_real_code_is_not()
    {
        var (provider, connection) = Build();
        using var _ = connection;
        var cfg = Config((Consts.General.InviteCodeCfg, "from-the-drop-in"));

        Assert.False((await ResolveAsync(provider, "FROM-THE-DROP-IN", cfg)).Admits);
    }

    [Fact]
    public async Task Open_registration_admits_without_anything_being_typed()
    {
        var (provider, connection) = Build();
        using var _ = connection;

        var verdict = await ResolveAsync(provider, "", Config((Consts.General.OpenRegistrationCfg, "true")));

        Assert.True(verdict.Admits);
        Assert.Equal(AuthEndpoints.InviteKind.OpenRegistration, verdict.Kind);
    }

    [Fact]
    public async Task An_unknown_code_is_refused_even_when_a_real_one_exists()
    {
        var (provider, connection) = Build();
        using var _ = connection;
        await SeedCodeAsync(provider, "CEDAR-MOO-1");

        Assert.False((await ResolveAsync(provider, "CEDAR-MOO-2", Config())).Admits);
    }
}
