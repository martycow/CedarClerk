namespace CedarClerk.Core;

/// <summary>
/// Whether a team member may act at all, independent of what their role says they may do (T-358).
/// Strings, like <see cref="ProjectRoles"/>: a fourth state should be a constant and a UI, never a
/// migration.
///
/// <para>Two columns rather than two extra roles, because restoring somebody has to give back the
/// role they had and a single column would have forgotten it.</para>
/// </summary>
public static class TeamMemberStatuses
{
    /// <summary>Full use of the role.</summary>
    public const string Active = "active";

    /// <summary>Read-only, whatever the role says. The way to pause somebody without removing them.</summary>
    public const string Restricted = "restricted";

    /// <summary>
    /// No access at all, and the invitation cannot be re-accepted. Deliberately not a deletion: a
    /// removed row lets the same address be invited again by anyone with the button, which is
    /// exactly what a ban is meant to stop.
    /// </summary>
    public const string Banned = "banned";

    public static readonly IReadOnlyList<string> All = [Active, Restricted, Banned];

    public static bool IsKnown(string? status) => status is not null && All.Contains(status);

    /// <summary>Whether the membership grants anything at all.</summary>
    public static bool GrantsAccess(string? status) => status != Banned;

    /// <summary>Whether the membership's role is allowed to write, or is held read-only.</summary>
    public static bool AllowsWrite(string? status) => status == Active;

    /// <summary>The role this status leaves in force — restriction is expressed as a read-only role.</summary>
    public static string EffectiveRole(string? status, string? role) =>
        AllowsWrite(status) ? role ?? ProjectRoles.Viewer : ProjectRoles.Viewer;
}
