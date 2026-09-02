using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static partial class AdminEndpoints
{
    public record DiscoverySaveRequest(
        bool Enabled, bool ShowScreenshotSaturday, bool ShowProjects, bool ShowBlogs,
        string? TitleEn, string? TitleRu, string? IntroEn, string? IntroRu);

    private static void MapDiscoveryAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/discovery", async (CedarDbContext db) =>
        {
            var row = await db.DiscoverySettings.AsNoTracking().FirstOrDefaultAsync();
            var settings = DiscoveryEndpoints.Resolve(row);
            var ownerIds = await db.Users.Where(u => u.DiscoveryOptIn).Select(u => u.Id).ToListAsync();
            var projects = await db.Projects.CountAsync(p =>
                ownerIds.Contains(p.OwnerId) && p.ShowcaseSlug != null && p.ArchivedAt == null);
            var posts = await db.Drafts.CountAsync(d =>
                ownerIds.Contains(d.OwnerId) && d.IsBlogPublished && d.BlogSlug != null && !d.IsPrivate);

            return Results.Ok(new
            {
                settings.Enabled,
                settings.ShowScreenshotSaturday,
                settings.ShowProjects,
                settings.ShowBlogs,
                row?.TitleEn,
                row?.TitleRu,
                row?.IntroEn,
                row?.IntroRu,
                defaults = new { settings.Title, settings.Intro },
                optedInAuthors = ownerIds.Count,
                eligibleProjects = projects,
                eligiblePosts = posts,
            });
        });

        group.MapPut("/discovery", async (DiscoverySaveRequest req, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            var actor = (await users.GetUserAsync(principal))!;
            var row = await db.DiscoverySettings.FirstOrDefaultAsync();
            if (row is null)
            {
                row = new DiscoverySettings();
                db.DiscoverySettings.Add(row);
            }

            row.Enabled = req.Enabled;
            row.ShowScreenshotSaturday = req.ShowScreenshotSaturday;
            row.ShowProjects = req.ShowProjects;
            row.ShowBlogs = req.ShowBlogs;
            row.TitleEn = Trim(req.TitleEn);
            row.TitleRu = Trim(req.TitleRu);
            row.IntroEn = Trim(req.IntroEn);
            row.IntroRu = Trim(req.IntroRu);
            row.UpdatedAt = DateTime.UtcNow;

            Audit(db, actor, "discovery",
                details: $"enabled={req.Enabled}; projects={req.ShowProjects}; blogs={req.ShowBlogs}");
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}
