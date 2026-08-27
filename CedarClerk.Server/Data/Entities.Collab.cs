namespace CedarClerk.Server;

// Collaboration on an indie-dev project (T-301, ADR-217). A file of its own beside
// Entities.IndieDev.cs and Entities.Canvas.cs for the same reason those exist: the module reads and
// deletes in one piece, and access control is a concern the canvas merely happens to be the first
// consumer of.

/// <summary>
/// Who may act on a <see cref="Project"/> besides its owner.
///
/// <para><see cref="OwnerId"/> is the **project owner's** id, never the invitee's — the row lives in
/// the owner's tenant like everything else the project holds. That split is the whole entity: give
/// the row to the invitee and the global filter hides it from the person who sent it; drop the
/// distinction and a member's tenant owns data it did not create.</para>
///
/// <para>There is no "owner" role. The owner is <c>project.OwnerId == callerUserId</c> and has no row
/// here at all, because a role column that could say "owner" is a row away from being edited into
/// ownership (ADR-217).</para>
/// </summary>
public class ProjectMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The project owner, i.e. the tenant this row belongs to. Never the invitee.</summary>
    public string OwnerId { get; set; } = default!;

    /// <summary>Plain scalar with no navigation, like <see cref="GameTask.ProjectId"/>.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Stored lowercase by the endpoint, so the unique index needs no collation of its own — the
    /// same treatment <see cref="ShowcaseFollower.Email"/> gets.
    ///
    /// A delivery address, not a credential: the invitation is accepted by whoever holds the token,
    /// because comparing the accepting account's address to this one breaks on aliases and second
    /// accounts.
    /// </summary>
    public string Email { get; set; } = "";

    /// <summary>Filled on accept. Null means invited and not yet accepted.</summary>
    public string? MemberUserId { get; set; }

    /// <summary>One of <see cref="CedarClerk.Core.ProjectRoles"/>.</summary>
    public string Role { get; set; } = CedarClerk.Core.ProjectRoles.Editor;

    /// <summary>
    /// <see cref="CedarClerk.Core.Consts.Canvas.InviteTokenBytes"/> random bytes, base64url.
    /// Cleared to null on accept, which is what makes the token single-use.
    /// </summary>
    public string? InviteToken { get; set; }

    /// <summary>Who sent it — the members list says so, and the invitation mail signs with it.</summary>
    public string InvitedByUserId { get; set; } = "";

    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Non-null exactly when <see cref="MemberUserId"/> is; the two are written together.</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>Last hub connect. Cosmetic — the members list shows who is around.</summary>
    public DateTime? LastSeenAt { get; set; }
}
