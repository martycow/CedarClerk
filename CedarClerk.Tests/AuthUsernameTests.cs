using System.Globalization;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// Phase 6 — a name is no longer optional at registration. The decision itself lives in one place:
// Usernames.Check for everything a rule can settle, plus the single database question on top. These
// exercise that pair, since the endpoint is a lambda around it.
public class AuthUsernameTests
{
    private static CedarDbContext Database(params (string Email, string? Username)[] accounts)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();

        foreach (var (email, username) in accounts)
            db.Users.Add(new ApplicationUser { UserName = email, Email = email, TenantUsername = username });
        db.SaveChanges();
        return db;
    }

    private static string Ru(Func<string> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            return read();
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task A_missing_name_is_refused()
    {
        using var db = Database();

        foreach (var raw in new[] { null, "", "   " })
            Assert.Equal(UsernameVerdict.Missing, await AuthEndpoints.VerdictAsync(db, raw));
    }

    [Theory]
    [InlineData("Marty COW")]
    [InlineData("-x")]
    [InlineData("x-")]
    [InlineData("a_b")]
    [InlineData("marty.cow")]
    public async Task A_name_that_is_not_a_dns_label_is_refused(string raw)
    {
        using var db = Database();
        Assert.Equal(UsernameVerdict.Invalid, await AuthEndpoints.VerdictAsync(db, raw));
    }

    [Fact]
    public async Task Seventeen_characters_is_one_too_many_for_a_new_name()
    {
        using var db = Database();

        Assert.Equal(UsernameVerdict.Free, await AuthEndpoints.VerdictAsync(db, new string('a', 16)));
        Assert.Equal(UsernameVerdict.Invalid, await AuthEndpoints.VerdictAsync(db, new string('a', 17)));
    }

    [Fact]
    public void An_older_name_longer_than_sixteen_still_resolves_as_a_host() =>
        Assert.True(Usernames.IsValidFormat(new string('a', 63)));

    [Theory]
    [InlineData("admin")]
    [InlineData("www")]
    [InlineData("api")]
    [InlineData("blog")]
    public async Task A_reserved_name_is_refused(string raw)
    {
        using var db = Database();
        Assert.Equal(UsernameVerdict.Reserved, await AuthEndpoints.VerdictAsync(db, raw));
    }

    [Fact]
    public async Task Every_reserved_subdomain_is_out_of_reach()
    {
        using var db = Database();

        foreach (var reserved in Consts.ReservedSubdomains)
            Assert.Equal(UsernameVerdict.Reserved, await AuthEndpoints.VerdictAsync(db, reserved));
    }

    // The name is stored lowercased, so a differently-spelled request for the same name has to
    // collide — otherwise two accounts would race for one subdomain and the index would decide.
    [Fact]
    public async Task A_taken_name_collides_whatever_the_case()
    {
        using var db = Database(("a@x.test", "martycow"));

        Assert.Equal(UsernameVerdict.Taken, await AuthEndpoints.VerdictAsync(db, "martycow"));
        Assert.Equal(UsernameVerdict.Taken, await AuthEndpoints.VerdictAsync(db, "MartyCow"));
        Assert.Equal(UsernameVerdict.Taken, await AuthEndpoints.VerdictAsync(db, "  MARTYCOW "));
    }

    [Fact]
    public async Task A_free_name_is_free()
    {
        using var db = Database(("a@x.test", "martycow"));
        Assert.Equal(UsernameVerdict.Free, await AuthEndpoints.VerdictAsync(db, "someone-else"));
    }

    // The accounts that predate names hold null, and nothing about them may make a name look taken.
    [Fact]
    public async Task Accounts_with_no_name_block_nothing()
    {
        using var db = Database(("a@x.test", null), ("b@x.test", null));

        Assert.Equal(UsernameVerdict.Free, await AuthEndpoints.VerdictAsync(db, "marty"));
        Assert.Equal(2, db.Users.Count());
    }

    // The database is asked exactly one question; everything else is Usernames', and a second copy
    // of the rules here is how the form and the server start disagreeing.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Marty COW")]
    [InlineData("admin")]
    [InlineData("marty")]
    public async Task Nothing_is_decided_that_Usernames_had_not_already_decided(string? raw)
    {
        using var db = Database();
        Assert.Equal(Usernames.Check(raw), await AuthEndpoints.VerdictAsync(db, raw));
    }

    [Fact]
    public void Each_refusal_carries_its_own_message()
    {
        Assert.Equal(ErrorMessages.UsernameRequired, AuthEndpoints.Refusal(UsernameVerdict.Missing, null));
        Assert.Equal(ErrorMessages.UsernameInvalid, AuthEndpoints.Refusal(UsernameVerdict.Invalid, "a_b"));
        Assert.Equal(ErrorMessages.UsernameInvalid, AuthEndpoints.Refusal(UsernameVerdict.Reserved, "admin"));
        Assert.Contains("martycow", AuthEndpoints.Refusal(UsernameVerdict.Taken, "martycow")!);
        Assert.Null(AuthEndpoints.Refusal(UsernameVerdict.Free, "marty"));
    }

    [Fact]
    public void Refusals_are_localized()
    {
        Assert.NotEqual(ErrorMessages.UsernameRequired, Ru(() => ErrorMessages.UsernameRequired));
        Assert.Contains("имя", Ru(() => ErrorMessages.UsernameRequired));
        Assert.Contains("занято", Ru(() => ErrorMessages.UsernameTaken("marty")));
    }

    // What GET /api/auth/username-available answers with. The reason is null exactly when the name
    // is free, and it never distinguishes one owner from another.
    [Theory]
    [InlineData(UsernameVerdict.Free, null)]
    [InlineData(UsernameVerdict.Missing, "invalid")]
    [InlineData(UsernameVerdict.Invalid, "invalid")]
    [InlineData(UsernameVerdict.Reserved, "reserved")]
    [InlineData(UsernameVerdict.Taken, "taken")]
    public void Availability_reports_a_reason_only_when_the_name_is_unavailable(UsernameVerdict verdict, string? reason)
    {
        Assert.Equal(reason, AuthEndpoints.Reason(verdict));
        Assert.Equal(verdict == UsernameVerdict.Free, AuthEndpoints.Reason(verdict) is null);
    }

    // A name that passes the check is a name the account can actually be created with — the same
    // normalisation on both sides, so the row holds what the resolver will look for.
    [Fact]
    public async Task An_accepted_name_is_stored_normalized_and_resolves()
    {
        using var db = Database();
        Assert.Equal(UsernameVerdict.Free, await AuthEndpoints.VerdictAsync(db, "  MartyCow "));

        db.Users.Add(new ApplicationUser
        {
            UserName = "a@x.test",
            Email = "a@x.test",
            TenantUsername = Usernames.Normalize("  MartyCow "),
        });
        db.SaveChanges();

        Assert.Equal("martycow", db.Users.Single().TenantUsername);
        Assert.Equal(new TenantHostResult(TenantHostKind.Tenant, "martycow"),
            TenantHost.Resolve("MartyCow." + Consts.URLs.TenantHost, Consts.URLs.TenantHost));
    }
}
