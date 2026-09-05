using System.Text.RegularExpressions;
using CedarClerk.Server;

namespace CedarClerk.Tests;

// T-258. The journal stores the action and nothing about how grave it is; the level the admin
// panel draws is derived here, so the table below is the whole of the policy.
public class AdminAuditSeverityTests
{
    [Theory]
    [InlineData("lock", "warn")]
    [InlineData("delete-account", "warn")]
    [InlineData("purge-orphans", "warn")]
    [InlineData("grant-admin", "warn")]
    [InlineData("revoke-admin", "warn")]
    [InlineData("unlock", "ok")]
    [InlineData("reset-trial", "ok")]
    [InlineData("invite-create", "ok")]
    [InlineData("invite-enable", "ok")]
    [InlineData("plan", "info")]
    [InlineData("credits", "info")]
    [InlineData("invite-disable", "info")]
    [InlineData("invite-attribute", "info")]
    [InlineData("discovery", "info")]
    [InlineData("landing", "info")]
    public void Every_audited_action_has_its_severity(string action, string severity)
    {
        Assert.Equal(severity, AdminEndpoints.SeverityOf(action));
    }

    [Fact]
    public void An_unknown_action_is_info()
    {
        Assert.Equal(AdminEndpoints.AuditSeverity.Info, AdminEndpoints.SeverityOf("something-new"));
        Assert.Equal(AdminEndpoints.AuditSeverity.Info, AdminEndpoints.SeverityOf(""));
    }

    // A new Audit(...) call lands in "info" by default, which is the wrong level for anything that
    // locks or deletes. This ties the table above to the source, so an action added without a row
    // here fails the suite rather than quietly drawing as routine.
    [Fact]
    public void Every_action_the_server_audits_is_in_the_table()
    {
        var known = typeof(AdminAuditSeverityTests)
            .GetMethod(nameof(Every_audited_action_has_its_severity))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false)
            .Cast<InlineDataAttribute>()
            .Select(a => (string)a.GetData(null!).Single()[0]!)
            .ToHashSet();

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CedarClerk.Server", "AdminEndpoints.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var audited = Directory.GetFiles(Path.Combine(dir!.FullName, "CedarClerk.Server"), "AdminEndpoints*.cs")
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"Audit\(db,\s*\w+,\s*""([a-z-]+)""")
                .Select(m => m.Groups[1].Value))
            .ToHashSet();

        Assert.NotEmpty(audited);
        Assert.Empty(audited.Except(known));
    }
}
