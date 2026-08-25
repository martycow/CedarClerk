using CedarClerk.Server.Tenancy;

namespace CedarClerk.Tests;

// The Host header alone, decided without a database. Everything the middleware does past this
// point is a lookup; everything before it is this function.
public class TenantHostTests
{
    private const string Domain = "cedarclerk.app";

    private static TenantHostResult Resolve(string? host) => TenantHost.Resolve(host, Domain);

    [Fact]
    public void First_label_of_the_tenant_domain_is_the_tenant()
    {
        var result = Resolve("marty.cedarclerk.app");
        Assert.Equal(TenantHostKind.Tenant, result.Kind);
        Assert.Equal("marty", result.Username);
    }

    [Fact]
    public void Host_is_case_insensitive()
    {
        var result = Resolve("MARTY.CedarClerk.App");
        Assert.Equal(TenantHostKind.Tenant, result.Kind);
        Assert.Equal("marty", result.Username);
    }

    [Fact]
    public void Trailing_dot_is_still_the_same_host()
    {
        var result = Resolve("marty.cedarclerk.app.");
        Assert.Equal(TenantHostKind.Tenant, result.Kind);
        Assert.Equal("marty", result.Username);
    }

    [Fact]
    public void Apex_is_not_a_tenant() =>
        Assert.Equal(TenantHostKind.Apex, Resolve("cedarclerk.app").Kind);

    [Fact]
    public void Www_is_reserved_not_a_tenant()
    {
        var result = Resolve("www.cedarclerk.app");
        Assert.Equal(TenantHostKind.Reserved, result.Kind);
        Assert.Null(result.Username);
    }

    [Fact]
    public void Every_reserved_subdomain_resolves_as_reserved()
    {
        foreach (var reserved in CedarClerk.Core.Consts.ReservedSubdomains)
            Assert.Equal(TenantHostKind.Reserved, Resolve($"{reserved}.{Domain}").Kind);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("blog.mooexe.dev")]
    [InlineData("cedarclerk.mooexe.dev")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_domains_are_left_alone(string? host) =>
        Assert.Equal(TenantHostKind.NotTenantDomain, Resolve(host).Kind);

    // The suffix has to end at a label boundary. Matching "ends with cedarclerk.app" as plain text
    // would hand this host's first label — "notcedarclerk" has no first label — to the resolver.
    [Theory]
    [InlineData("notcedarclerk.app")]
    [InlineData("evilcedarclerk.app")]
    public void A_domain_that_merely_ends_with_the_same_letters_is_not_the_tenant_domain(string host) =>
        Assert.Equal(TenantHostKind.NotTenantDomain, Resolve(host).Kind);

    // A name that cannot be a DNS label cannot be a tenant either — and it is not "some other
    // site" the way an unknown domain is, so it is a 404 rather than a pass-through.
    [Theory]
    [InlineData("-marty.cedarclerk.app")]
    [InlineData("marty-.cedarclerk.app")]
    [InlineData("marty_cow.cedarclerk.app")]
    public void Invalid_labels_are_rejected(string host) =>
        Assert.Equal(TenantHostKind.Invalid, Resolve(host).Kind);

    [Fact]
    public void A_label_longer_than_63_characters_is_rejected() =>
        Assert.Equal(TenantHostKind.Invalid, Resolve($"{new string('a', 64)}.{Domain}").Kind);

    [Fact]
    public void A_label_of_exactly_63_characters_is_a_tenant()
    {
        var name = new string('a', 63);
        var result = Resolve($"{name}.{Domain}");
        Assert.Equal(TenantHostKind.Tenant, result.Kind);
        Assert.Equal(name, result.Username);
    }

    // A nested subdomain is not a tenant: the wildcard certificate does not cover it, and treating
    // its first label as a name would make one tenant addressable at infinitely many hosts.
    [Theory]
    [InlineData("a.b.cedarclerk.app")]
    [InlineData("marty.blog.cedarclerk.app")]
    public void Nested_subdomains_are_rejected(string host) =>
        Assert.Equal(TenantHostKind.Invalid, Resolve(host).Kind);

    [Fact]
    public void An_empty_first_label_is_rejected() =>
        Assert.Equal(TenantHostKind.Invalid, Resolve(".cedarclerk.app").Kind);

    [Fact]
    public void The_tenant_domain_is_configuration_not_a_literal()
    {
        var result = TenantHost.Resolve("marty.example.test", "example.test");
        Assert.Equal(TenantHostKind.Tenant, result.Kind);
        Assert.Equal("marty", result.Username);
        Assert.Equal(TenantHostKind.NotTenantDomain, TenantHost.Resolve("marty.cedarclerk.app", "example.test").Kind);
    }
}
