namespace CedarClerk.Server;

// Teams (T-358). A file of its own beside Entities.Collab.cs, which it grows out of: a
// ProjectMember answers "who else may act on THIS project", a TeamMember answers "who else may act
// on every project this team holds". The two live side by side rather than one replacing the other
// — inviting one person to one board must not require standing a team up first.

/// <summary>
/// A named group of people, owned by one account.
///
/// <para>A team always belongs to a user: <see cref="OwnerId"/> is an account, never another team,
/// and there is no transfer. That is what keeps every question about a team answerable inside one
/// tenant — billing, quotas and the media gate all still key on a single owner.</para>
/// </summary>
public class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The account that owns the team, i.e. the tenant every row here belongs to.</summary>
    public string OwnerId { get; set; } = default!;

    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One person's place in a team. The invitation half is the same shape as
/// <see cref="ProjectMember"/>'s and for the same reasons — the token is the credential, the address
/// is only where it was posted.
///
/// <para>What a team adds is <see cref="Status"/>. A role says what someone may do; a status says
/// whether they may do it at all right now. They are two columns rather than two more roles because
/// restoring a restricted member must give back the role they had, and a single column would have
/// forgotten it.</para>
/// </summary>
public class TeamMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The team owner, i.e. the tenant this row belongs to. Never the invitee.</summary>
    public string OwnerId { get; set; } = default!;

    public Guid TeamId { get; set; }

    /// <summary>Stored lowercase by the endpoint, like <see cref="ProjectMember.Email"/>.</summary>
    public string Email { get; set; } = "";

    /// <summary>Filled on accept. Null means invited and not yet accepted.</summary>
    public string? MemberUserId { get; set; }

    /// <summary>One of <see cref="CedarClerk.Core.ProjectRoles"/> — the same two the canvas uses.</summary>
    public string Role { get; set; } = CedarClerk.Core.ProjectRoles.Editor;

    /// <summary>One of <see cref="CedarClerk.Core.TeamMemberStatuses"/>.</summary>
    public string Status { get; set; } = CedarClerk.Core.TeamMemberStatuses.Active;

    /// <summary>
    /// <see cref="CedarClerk.Core.Consts.Canvas.InviteTokenBytes"/> random bytes, base64url.
    /// Cleared on accept, which is what makes the token single-use.
    /// </summary>
    public string? InviteToken { get; set; }

    public string InvitedByUserId { get; set; } = "";

    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    public DateTime? AcceptedAt { get; set; }

    /// <summary>
    /// Why the member was restricted or banned, for the owner's own memory. Never shown to the
    /// member: a ban notice that argues its case invites the argument back.
    /// </summary>
    public string? StatusNote { get; set; }

    public DateTime? StatusChangedAt { get; set; }
}
