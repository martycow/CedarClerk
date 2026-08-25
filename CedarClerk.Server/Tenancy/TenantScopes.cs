namespace CedarClerk.Server.Tenancy;

/// <summary>
/// Opening a scope and saying what it is for, in one call — so that a background path cannot get
/// half of it right. There is no way to open a working cross-tenant scope by accident: a plain
/// <c>CreateScope</c> gets the strict default and reads nothing.
/// </summary>
public static class TenantScopes
{
    /// <summary>
    /// A scope that reads across owners: Quartz jobs, the publish queue, the bot, startup.
    /// Nothing here runs inside an HTTP request, so there is no Host and no signed-in user to take
    /// a tenant from — the work is the platform's own, on everybody's rows at once.
    /// </summary>
    public static IServiceScope CreatePlatformScope(this IServiceScopeFactory factory)
    {
        var scope = factory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantProvider>().UsePlatform();
        return scope;
    }

    /// <inheritdoc cref="CreatePlatformScope(IServiceScopeFactory)"/>
    public static IServiceScope CreatePlatformScope(this IServiceProvider services)
    {
        var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantProvider>().UsePlatform();
        return scope;
    }

    /// <summary>
    /// A scope that acts for one owner off the request thread — the AI jobs, which outlive the
    /// request that started them but still belong to whoever asked.
    /// </summary>
    public static IServiceScope CreateTenantScope(this IServiceScopeFactory factory, string ownerId)
    {
        var scope = factory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantProvider>().UseTenant(ownerId);
        return scope;
    }
}
