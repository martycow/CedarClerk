using CedarClerk.Localization;
using CedarClerk.Server.Email;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Notify-on-publish for blog-wide subscribers (Wave 1 item 7), queued: the publish handler only
/// writes a <see cref="BlogNotifyJob"/> row and returns — a slow Resend burst can never block a
/// publish. The row is kicked immediately (PublishJobRunner's pattern) and swept by
/// <see cref="SendBlogNotificationsJob"/> as the backstop after a restart or a missed kick.
/// </summary>
public sealed class BlogSubscriberNotifier(
    IServiceScopeFactory scopes,
    IConfiguration cfg,
    ILogger<BlogSubscriberNotifier> logger)
{
    /// <summary>A Sending row older than this was interrupted; it is failed, never re-sent blind —
    /// some addresses may already have the mail, and a blind retry doubles them.</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(15);

    public async Task EnqueueAsync(CedarDbContext db, Draft draft, CancellationToken ct = default)
    {
        var job = new BlogNotifyJob { OwnerId = draft.OwnerId, DraftId = draft.Id };
        db.BlogNotifyJobs.Add(job);
        await db.SaveChangesAsync(ct);
        Kick(job.Id);
    }

    /// <summary>Fire-and-forget immediacy; the sweeper is the deterministic path.</summary>
    public void Kick(Guid jobId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await RunOneAsync(jobId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Blog notify job {JobId} crashed outside its own error handling", jobId);
            }
        });
    }

    public async Task RunOneAsync(Guid jobId, CancellationToken ct)
    {
        using var scope = scopes.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        // The claim — a conditional update, so the kick and the sweeper cannot both mail.
        var claimed = await db.BlogNotifyJobs
            .Where(j => j.Id == jobId && j.Status == BlogNotifyJobStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BlogNotifyJobStatus.Sending), ct);
        if (claimed == 0) return;

        var job = await db.BlogNotifyJobs.FirstAsync(j => j.Id == jobId, ct);
        try
        {
            job.Error = await SendAllAsync(scope.ServiceProvider, db, job, ct);
            job.Status = job.Error is null ? BlogNotifyJobStatus.Sent : BlogNotifyJobStatus.Failed;
        }
        catch (Exception ex)
        {
            job.Status = BlogNotifyJobStatus.Failed;
            job.Error = ex.Message;
        }
        job.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Null on success. A single address failing is logged and skipped, exactly like
    /// NotifyFollowersAsync — one bad mailbox must not silence the rest of the list.
    /// </summary>
    private async Task<string?> SendAllAsync(IServiceProvider services, CedarDbContext db, BlogNotifyJob job, CancellationToken ct)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == job.DraftId && d.OwnerId == job.OwnerId, ct);
        if (draft is null) return "The post no longer exists.";
        if (!draft.IsBlogPublished || draft.IsPrivate || draft.BlogSlug is null)
            return "The post is no longer publicly published.";

        var host = await BlogTenant.HostForOwnerAsync(db, cfg, job.OwnerId, ct);
        if (host is null) return "The account has no blog host to link to.";
        var site = new BlogSite(job.OwnerId, host);

        var subscribers = await db.BlogSubscribers
            .Where(s => s.OwnerId == job.OwnerId && s.ConfirmedAt != null)
            .Select(s => new { s.Email, s.UnsubscribeToken })
            .ToListAsync(ct);
        if (subscribers.Count == 0) return null;

        var siteName = await SiteNameAsync(db, site, ct);
        var postTitle = draft.ArticleTitle ?? draft.Title;
        var postUrl = site.PostUrl(draft.BlogSlug);
        var mailer = services.GetRequiredService<ResendEmailProvider>();

        foreach (var subscriber in subscribers)
        {
            var unsubscribeUrl = $"{site.BaseUrl}/subscribe/leave?token={subscriber.UnsubscribeToken}";
            try
            {
                await mailer.SendAsync(subscriber.Email,
                    EmailTexts.BlogNewPostSubject(siteName, postTitle),
                    EmailTexts.BlogNewPostBody(siteName, postTitle, postUrl, unsubscribeUrl));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not send the new-post mail to {Email}", subscriber.Email);
            }
        }
        return null;
    }

    /// <summary>What the mail calls the blog — the author's display name when set, the tenant
    /// name otherwise, the host as the last resort.</summary>
    public static async Task<string> SiteNameAsync(CedarDbContext db, BlogSite site, CancellationToken ct = default)
    {
        var owner = await db.Users.Where(u => u.Id == site.OwnerId)
            .Select(u => new { u.AuthorDisplayName, u.TenantUsername })
            .FirstOrDefaultAsync(ct);
        return owner?.AuthorDisplayName ?? owner?.TenantUsername ?? site.Host;
    }
}
