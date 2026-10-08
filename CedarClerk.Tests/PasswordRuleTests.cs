using CedarClerk.Server;

namespace CedarClerk.Tests;

public class PasswordRuleTests
{
    [Theory]
    [InlineData("abcdef1!")]
    [InlineData("ПАРОЛЬ12#")]
    [InlineData("a1!aaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Accepts_a_letter_a_digit_and_a_symbol_within_8_to_32(string password) =>
        Assert.True(PasswordRule.IsSatisfied(password));

    [Theory]
    [InlineData("abc1!")]
    [InlineData("abcdefgh1")]
    [InlineData("abcdefgh!")]
    [InlineData("12345678!")]
    [InlineData("abcd efg1")]
    [InlineData("a1!aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(null)]
    public void Refuses_anything_else(string? password) =>
        Assert.False(PasswordRule.IsSatisfied(password));
}
