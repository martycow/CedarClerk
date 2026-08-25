using CedarClerk.Core;

namespace CedarClerk.Tests;

// The shared validator behind both ends of a tenant name: the Host header on the way in, and
// registration on the way out. One rule set, so a name that resolves is a name that could be
// registered and vice versa.
public class UsernameRulesTests
{
    [Theory]
    [InlineData("marty")]
    [InlineData("a")]
    [InlineData("a1")]
    [InlineData("1a")]
    [InlineData("123")]
    [InlineData("marty-cow")]
    [InlineData("a-b-c-1-2-3")]
    public void Accepts_valid_dns_labels(string name) =>
        Assert.True(Usernames.IsValidFormat(name));

    [Theory]
    [InlineData("-marty")]
    [InlineData("marty-")]
    [InlineData("-")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("marty cow")]
    [InlineData("marty_cow")]
    [InlineData("marty.cow")]
    [InlineData("marty!")]
    [InlineData("márty")]
    [InlineData("мarty")]
    public void Rejects_anything_that_is_not_a_dns_label(string? name) =>
        Assert.False(Usernames.IsValidFormat(name));

    [Fact]
    public void Accepts_exactly_63_characters_and_rejects_64()
    {
        Assert.True(Usernames.IsValidFormat(new string('a', 63)));
        Assert.False(Usernames.IsValidFormat(new string('a', 64)));
    }

    // A hostname is case-insensitive, so uppercase is not an error — it is the same name written
    // differently. Normalize is what makes both ends agree on which spelling is stored.
    [Theory]
    [InlineData("Marty", "marty")]
    [InlineData("MARTY", "marty")]
    [InlineData("  Marty  ", "marty")]
    public void Normalize_lowercases_and_trims(string input, string expected) =>
        Assert.Equal(expected, Usernames.Normalize(input));

    [Fact]
    public void Normalize_of_nothing_is_null()
    {
        Assert.Null(Usernames.Normalize(null));
        Assert.Null(Usernames.Normalize(""));
        Assert.Null(Usernames.Normalize("   "));
    }

    [Fact]
    public void Uppercase_is_valid_once_normalized() =>
        Assert.True(Usernames.IsValidFormat(Usernames.Normalize("Marty")));

    [Fact]
    public void Every_reserved_subdomain_is_reserved()
    {
        foreach (var reserved in Consts.ReservedSubdomains)
            Assert.True(Usernames.IsReserved(reserved), $"'{reserved}' should be reserved");
    }

    [Theory]
    [InlineData("WWW")]
    [InlineData("Admin")]
    [InlineData("API")]
    public void Reserved_check_ignores_case(string name) =>
        Assert.True(Usernames.IsReserved(name));

    [Theory]
    [InlineData("marty")]
    [InlineData("wwww")]
    [InlineData("administrator")]
    public void Ordinary_names_are_not_reserved(string name) =>
        Assert.False(Usernames.IsReserved(name));

    // What registration asks: a name that is both well-formed and not spoken for by the platform.
    [Theory]
    [InlineData("marty", true)]
    [InlineData("Marty", true)]
    [InlineData("www", false)]
    [InlineData("-marty", false)]
    [InlineData("marty.cow", false)]
    [InlineData(null, false)]
    public void IsAssignable_combines_format_and_reservation(string? name, bool expected) =>
        Assert.Equal(expected, Usernames.IsAssignable(name));
}
