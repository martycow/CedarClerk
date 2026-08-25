namespace CedarClerk.Server.Tenancy;

/// <summary>
/// Whose rows this <see cref="CedarDbContext"/> may see. Scoped, exactly like the context it
/// belongs to — a longer life would let one request's tenant leak into the next, and a shorter one
/// could not exist, since the context reads it while the scope is open.
///
/// Three states, and the default is the strict one:
/// <list type="bullet">
/// <item>unset — the request never said who it is for, and the filters answer "nothing";</item>
/// <item><see cref="For"/> — an ordinary request, scoped to one owner;</item>
/// <item><see cref="Platform"/> — a path that legitimately reads across owners, and had to say so.</item>
/// </list>
///
/// Unset reading as "nothing" is the whole design. The opposite default — unset meaning "no
/// filter" — turns every forgotten call site into a silent cross-tenant leak, while this one turns
/// it into an empty list somebody notices.
/// </summary>
public sealed class TenantProvider
{
    public string? TenantId { get; private set; }

    private bool _isPlatform;
    private bool _read;

    /// <summary>
    /// Whether the filters are built into the model at all. Reading it locks the value: the model
    /// has been picked by then, and a later change would not reach the compiled query.
    /// </summary>
    public bool IsPlatform
    {
        get { _read = true; return _isPlatform; }
    }

    public static TenantProvider For(string tenantId) => new() { TenantId = tenantId };

    public static TenantProvider Platform()
    {
        var provider = new TenantProvider();
        provider.UsePlatform();
        return provider;
    }

    /// <summary>The tenant of an ordinary request, known once the owner is.</summary>
    public void UseTenant(string tenantId)
    {
        if (_isPlatform)
            throw new InvalidOperationException("A platform scope cannot be narrowed to one tenant.");

        TenantId = tenantId;
    }

    /// <summary>
    /// Opt out of the filters for a path that reads across owners: the admin panel, the publish
    /// queue, the Quartz jobs, the bot, provider webhooks, startup. Must be called before the
    /// scope's context touches the database — the model is compiled on first use and this decides
    /// which model that is.
    /// </summary>
    public void UsePlatform()
    {
        // Too late to matter is the dangerous case: the context would already hold the filtered
        // model, the caller would believe it opted out, and the rows it came for would be missing.
        // Loud here beats a job that quietly processes nothing.
        if (_read)
            throw new InvalidOperationException(
                "Cross-tenant access must be declared before the scope's first query.");

        _isPlatform = true;
    }
}
