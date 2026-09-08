using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server;

public sealed class PasswordRecoveryTokenProvider(
    IDataProtectionProvider protection, ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(protection, Microsoft.Extensions.Options.Options.Create(new DataProtectionTokenProviderOptions
    {
        Name = ProviderName,
        TokenLifespan = TimeSpan.FromHours(1),
    }), logger)
{
    public const string ProviderName = "CedarPasswordRecovery";
}
