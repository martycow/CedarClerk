using System.Security.Cryptography;
using System.Text;
using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class DocumentProjects
{
    public static Guid PersonalId(string ownerId) => new(SHA256.HashData(Encoding.UTF8.GetBytes("cedar-personal-project:" + ownerId)).AsSpan(0, 16));

    public static async Task<Guid> PersonalAsync(CedarDbContext db, string ownerId)
    {
        var id = PersonalId(ownerId);
        var existing = db.Projects.Local.FirstOrDefault(p => p.Id == id)
            ?? await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId);
        if (existing is not null)
        {
            existing.ArchivedAt = null;
            return id;
        }
        var project = new Project { Id = id, OwnerId = ownerId, Name = "Personal", CreatedFromPreset = ProjectTypes.Blog };
        project.Modules.AddRange(ProjectModules.ForPreset(ProjectTypes.Blog)!.Select(m =>
            new ProjectModule { OwnerId = ownerId, ProjectId = id, ModuleKey = m.Key, Enabled = m.Value }));
        db.Projects.Add(project);
        return id;
    }

    public static async Task<int> BackfillAsync(CedarDbContext db)
    {
        var drafts = await db.Drafts.Where(d => d.ProjectId == null).ToListAsync();
        foreach (var group in drafts.GroupBy(d => d.OwnerId))
        {
            var projectId = await PersonalAsync(db, group.Key);
            foreach (var draft in group) draft.ProjectId = projectId;
            await AssetFiling.FileAsync(db, group.Key, projectId, group.Select(d => d.Id).ToArray());
        }
        if (drafts.Count > 0) await db.SaveChangesAsync();
        return drafts.Count;
    }
}
