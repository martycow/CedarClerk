using CedarClerk.Localization;
using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server;

/// <summary>ADR-325: 8–32 characters with a letter, a digit and a symbol; case is not required.</summary>
public sealed class PasswordRule : IPasswordValidator<ApplicationUser>
{
    public const int MinLength = 8;
    public const int MaxLength = 32;

    public static bool IsSatisfied(string? password) =>
        password is { Length: >= MinLength and <= MaxLength }
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit)
        && password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password) =>
        Task.FromResult(IsSatisfied(password)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError { Code = nameof(PasswordRule), Description = ErrorMessages.PasswordRule(MinLength, MaxLength) }));
}
