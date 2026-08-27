using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

/// <summary>
/// One in-memory SQLite database opened under several tenants, which is what the canvas tests are
/// actually about: the same rows have to be visible through the project owner's scope and invisible
/// through a member's own, and only a shared connection can show both.
/// </summary>
internal sealed class CanvasFixture : IDisposable
{
    private readonly SqliteConnection connection;

    public CanvasFixture()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var seed = Platform();
        seed.Database.EnsureCreated();
    }

    public CedarDbContext Platform() => Open(TenantProvider.Platform());

    public CedarDbContext As(string ownerId) => Open(TenantProvider.For(ownerId));

    private CedarDbContext Open(TenantProvider tenant) =>
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, tenant);

    public void Dispose() => connection.Dispose();

    public string User(string id)
    {
        using var db = Platform();
        db.Users.Add(new ApplicationUser { Id = id, UserName = $"{id}@local.test", Email = $"{id}@local.test" });
        db.SaveChanges();
        return id;
    }

    public Guid Project(string ownerId, string name = "Cedar Quest", bool archived = false)
    {
        using var db = Platform();
        var project = new Project
        {
            OwnerId = ownerId,
            Name = name,
            ArchivedAt = archived ? DateTime.UtcNow : null,
        };
        db.Projects.Add(project);
        db.SaveChanges();
        return project.Id;
    }

    public Guid Board(string ownerId, Guid projectId, string name = "Mood")
    {
        using var db = Platform();
        var board = new CanvasBoard { OwnerId = ownerId, ProjectId = projectId, Name = name };
        db.CanvasBoards.Add(board);
        db.SaveChanges();
        return board.Id;
    }

    public Guid Member(string ownerId, Guid projectId, string email, string role, string? memberUserId,
        string? token = null)
    {
        using var db = Platform();
        var member = new ProjectMember
        {
            OwnerId = ownerId,
            ProjectId = projectId,
            Email = email,
            Role = role,
            MemberUserId = memberUserId,
            AcceptedAt = memberUserId is null ? null : DateTime.UtcNow,
            InviteToken = token,
            InvitedByUserId = ownerId,
        };
        db.ProjectMembers.Add(member);
        db.SaveChanges();
        return member.Id;
    }

    public async Task<ProjectAccess> AccessAsync(Guid projectId, string userId)
    {
        await using var db = Platform();
        var access = await ProjectAccessResolver.ResolveAsync(db, projectId, userId);
        Assert.NotNull(access);
        return access!;
    }

    public static CanvasItemInput Note(Guid id, double x = 0, double y = 0, string text = "hello") =>
        new(id, CanvasItemKinds.Note, x, y, 220, 180, 0, "", Payload($$"""{"text":"{{text}}","align":"left"}"""));

    public static System.Text.Json.JsonElement Payload(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
}
