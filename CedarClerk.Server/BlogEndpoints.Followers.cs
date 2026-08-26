using System.Text;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Email;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// T-297 (ADR-216) — following one game's devlog by address. A follower is an address and two
// tokens, not an account: the reader-identity question (T-004) stays open, and leaving needs no
// login. Double opt-in throughout — an unconfirmed row is never mailed anything but its own
// confirmation.
public static partial class BlogEndpoints
{
    private const int FollowerEmailMaxLength = 200;

    /// <summary>
    /// The form, plus whatever the last submit had to say. A plain form posting to a plain path:
    /// the blog's pages carry no JavaScript of their own, and an address field is not a reason to
    /// start.
    /// </summary>
    private static string RenderFollowForm(HttpContext ctx, Project project, bool en)
    {
        var notice = ctx.Request.Query["follow"].ToString() switch
        {
            "sent" => en
                ? "Check your inbox — the link in that mail is what turns it on."
                : "Проверьте почту — подписка включится по ссылке из письма.",
            "confirmed" => en ? "Done — you are following this game." : "Готово — вы подписаны на эту игру.",
            "left" => en ? "You are unsubscribed." : "Вы отписались.",
            "invalid" => en ? "That does not look like an email address." : "Это не похоже на адрес почты.",
            "already" => en ? "That address is already following." : "Этот адрес уже подписан.",
            "toomany" => en ? "Too many attempts. Try again tomorrow." : "Слишком много попыток. Попробуйте завтра.",
            "expired" => en ? "That link is no longer valid." : "Ссылка больше не действует.",
            _ => null,
        };

        var sb = new StringBuilder();
        sb.Append("<h2 class=\"showcase-section\">").Append(en ? "Follow the devlog" : "Следить за девлогом").Append("</h2>");
        sb.Append("<form class=\"follow-form\" method=\"post\" action=\"").Append(ShowcasePath(ctx, project, "/follow")).Append("\">");
        sb.Append("<input class=\"follow-input\" type=\"email\" name=\"email\" required maxlength=\"")
          .Append(FollowerEmailMaxLength).Append("\" placeholder=\"")
          .Append(en ? "your@email" : "ваша@почта").Append("\" aria-label=\"")
          .Append(en ? "Email address" : "Адрес почты").Append("\">");
        sb.Append("<button class=\"follow-button\" type=\"submit\">").Append(en ? "Follow" : "Подписаться").Append("</button>");
        sb.Append("</form>");
        sb.Append("<p class=\"follow-hint\">")
          .Append(en
              ? "A mail when a new devlog is out, and nothing else. Unsubscribe from any of them."
              : "Письмо, когда выходит новый девлог, и ничего кроме. Отписаться можно из любого письма.")
          .Append(" <a href=\"").Append(ShowcasePath(ctx, project, "/rss.xml")).Append("\">RSS</a></p>");
        if (notice is not null)
            sb.Append("<p class=\"follow-notice\">").Append(Html(notice)).Append("</p>");

        return sb.ToString();
    }

    private static async Task PostFollowAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var project = await FindShowcaseAsync(db, site, slug);
        if (project is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var form = await ctx.Request.ReadFormAsync();
        var email = form["email"].ToString().Trim().ToLowerInvariant();
        if (email.Length is 0 or > FollowerEmailMaxLength || !LooksLikeEmail(email))
        {
            await RedirectToShowcaseAsync(ctx, project, "invalid");
            return;
        }

        // The same ceiling the registration form has, and for the same reason: this one writes a
        // row and sends a mail, both on an anonymous request.
        var visitor = VisitorHash(ctx);
        var since = DateTime.UtcNow - Consts.Showcase.FollowWindow;
        var recent = await db.ShowcaseFollowers.CountAsync(f => f.VisitorHash == visitor && f.CreatedAt >= since);
        if (recent >= Consts.Showcase.MaxFollowsPerVisitor)
        {
            await RedirectToShowcaseAsync(ctx, project, "toomany");
            return;
        }

        var existing = await db.ShowcaseFollowers.FirstOrDefaultAsync(f => f.ProjectId == project.Id && f.Email == email);
        if (existing is { ConfirmedAt: not null })
        {
            await RedirectToShowcaseAsync(ctx, project, "already");
            return;
        }

        // An address that asked twice without confirming gets a fresh token rather than a second
        // row — the unique index would refuse the row, and the reader would meet an error for
        // having lost a mail.
        var follower = existing ?? new ShowcaseFollower
        {
            OwnerId = project.OwnerId,
            ProjectId = project.Id,
            Email = email,
            UnsubscribeToken = PrivateAccess.NewToken(),
            VisitorHash = visitor,
        };
        follower.ConfirmToken = PrivateAccess.NewToken();
        if (existing is null) db.ShowcaseFollowers.Add(follower);
        await db.SaveChangesAsync();

        var confirmUrl = $"{site.BaseUrl}{ShowcasePath(ctx, project, $"/confirm?token={follower.ConfirmToken}")}";
        await SendFollowMailAsync(ctx, follower.Email,
            EmailTexts.FollowConfirmSubject(project.Name),
            EmailTexts.FollowConfirmBody(project.Name, confirmUrl));

        await RedirectToShowcaseAsync(ctx, project, "sent");
    }

    private static async Task ConfirmFollowAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var project = await FindShowcaseAsync(db, site, slug);
        var token = ctx.Request.Query["token"].ToString();
        if (project is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var follower = token.Length == 0
            ? null
            : await db.ShowcaseFollowers.FirstOrDefaultAsync(f => f.ProjectId == project.Id && f.ConfirmToken == token);
        if (follower is null)
        {
            await RedirectToShowcaseAsync(ctx, project, "expired");
            return;
        }

        follower.ConfirmedAt = DateTime.UtcNow;
        follower.ConfirmToken = null;
        await db.SaveChangesAsync();
        await RedirectToShowcaseAsync(ctx, project, "confirmed");
    }

    private static async Task UnsubscribeFollowerAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var project = await FindShowcaseAsync(db, site, slug);
        var token = ctx.Request.Query["token"].ToString();
        if (project is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Deleted, not marked: an address that asked to be forgotten is not a row to keep.
        var removed = token.Length == 0
            ? 0
            : await db.ShowcaseFollowers
                .Where(f => f.ProjectId == project.Id && f.UnsubscribeToken == token)
                .ExecuteDeleteAsync();

        await RedirectToShowcaseAsync(ctx, project, removed > 0 ? "left" : "expired");
    }

    /// <summary>
    /// Every follow path answers with a redirect and a word for the reader, so a refresh never
    /// re-posts the form and the browser's back button lands on a page rather than on a POST.
    /// </summary>
    private static Task RedirectToShowcaseAsync(HttpContext ctx, Project project, string outcome)
    {
        ctx.Response.StatusCode = StatusCodes.Status303SeeOther;
        ctx.Response.Headers.Location = ShowcasePath(ctx, project, $"?follow={outcome}#follow");
        return Task.CompletedTask;
    }

    /// <summary>
    /// A mail failure never turns a successful row into an error — same rule as the form-response
    /// mail in <c>PostRegistrationAsync</c>. The reader is waiting on the subscription, not on us
    /// reaching Resend.
    /// </summary>
    private static async Task SendFollowMailAsync(HttpContext ctx, string email, string subject, string html)
    {
        try
        {
            await ctx.RequestServices.GetRequiredService<Email.ResendEmailProvider>().SendAsync(email, subject, html);
        }
        catch (Exception ex)
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ShowcaseFollow")
                .LogWarning(ex, "Could not send the showcase follow mail to {Email}", email);
        }
    }

    // Deliberately not a regular expression: the only claim worth making here is that the string
    // has one @ with something either side of it, and the confirmation mail decides the rest.
    private static bool LooksLikeEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0
               && email.LastIndexOf('.') > at + 1 && !email.Contains(' ');
    }

    /// <summary>
    /// The mail a new devlog sends its followers (T-297). Confirmed addresses only, and only for a
    /// public post: a private one is behind a gate, and a mail announcing it would be a link its
    /// reader cannot open.
    ///
    /// Sent inline, one after another. A queue would be the right shape at thousands of addresses;
    /// at the numbers a single game's devlog has, it would be machinery guarding nothing, and a
    /// publish that waits a second longer is a publish that already happened.
    /// </summary>
    private static async Task NotifyFollowersAsync(CedarDbContext db, ResendEmailProvider mailer,
        ILoggerFactory loggerFactory, Draft draft, BlogSite site)
    {
        if (draft.IsPrivate || draft.ProjectId is not { } projectId) return;

        var project = await db.Projects.FirstOrDefaultAsync(p =>
            p.Id == projectId && p.OwnerId == site.OwnerId && p.ShowcaseSlug != null && p.ArchivedAt == null);
        if (project is null) return;

        var followers = await db.ShowcaseFollowers
            .Where(f => f.ProjectId == project.Id && f.ConfirmedAt != null)
            .Select(f => new { f.Email, f.UnsubscribeToken })
            .ToListAsync();
        if (followers.Count == 0) return;

        var logger = loggerFactory.CreateLogger("ShowcaseFollow");
        var postTitle = draft.ArticleTitle ?? draft.Title;
        var postUrl = site.PostUrl(draft.BlogSlug!);
        var subject = EmailTexts.DevlogSubject(project.Name, postTitle);

        foreach (var follower in followers)
        {
            var unsubscribeUrl = $"{site.BaseUrl}/games/{project.ShowcaseSlug}/unsubscribe?token={follower.UnsubscribeToken}";
            try
            {
                await mailer.SendAsync(follower.Email, subject,
                    EmailTexts.DevlogBody(project.Name, postTitle, postUrl, unsubscribeUrl));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not send the devlog mail to {Email}", follower.Email);
            }
        }
    }
}
