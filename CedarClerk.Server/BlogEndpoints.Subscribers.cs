using CedarClerk.Localization;
using System.Text;
using CedarClerk.Core;
using CedarClerk.Server.Email;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Wave 1 item 7 — the showcase follower generalized to the blog root: an address and two tokens,
// no account, double opt-in throughout. Mirrors Followers.cs deliberately, down to the outcome
// vocabulary — one mental model for "mail me when something new is up", whichever page offered it.
public static partial class BlogEndpoints
{
    /// <summary>
    /// The box on the index foot and under every post. A plain form posting to a plain path, same
    /// as the follow form: an address field is not a reason for JavaScript.
    /// </summary>
    private static string RenderSubscribeBox(HttpContext ctx, bool en, string backPath)
    {
        var notice = ctx.Request.Query["subscribe"].ToString() switch
        {
            "sent" => BlogTexts.CheckYourInboxTheLinkInThat(en),
            "confirmed" => BlogTexts.DoneYouAreSubscribed(en),
            "left" => BlogTexts.YouAreUnsubscribed(en),
            "invalid" => BlogTexts.ThatDoesNotLookLikeAnEmail(en),
            "already" => BlogTexts.ThatAddressIsAlreadySubscribed(en),
            "toomany" => BlogTexts.TooManyAttemptsTryAgainTomorrow(en),
            "expired" => BlogTexts.ThatLinkIsNoLongerValid(en),
            _ => null,
        };

        var sb = new StringBuilder();
        sb.Append("<div class=\"subscribe-box\" id=\"subscribe\">");
        sb.Append("<div class=\"subscribe-title\">").Append(BlogTexts.NewPostsByEmail(en)).Append("</div>");
        sb.Append("<form class=\"follow-form\" method=\"post\" action=\"/subscribe\">");
        sb.Append("<input type=\"hidden\" name=\"back\" value=\"").Append(Html(backPath)).Append("\">");
        sb.Append("<input class=\"follow-input\" type=\"email\" name=\"email\" required maxlength=\"")
          .Append(FollowerEmailMaxLength).Append("\" placeholder=\"")
          .Append(BlogTexts.YourEmail(en)).Append("\" aria-label=\"")
          .Append(BlogTexts.EmailAddress(en)).Append("\">");
        sb.Append("<button class=\"follow-button\" type=\"submit\">").Append(BlogTexts.Subscribe(en)).Append("</button>");
        sb.Append("</form>");
        sb.Append("<p class=\"follow-hint\">")
          .Append(BlogTexts.SubscriptionHint(en))
          .Append("</p>");
        if (notice is not null)
            sb.Append("<p class=\"follow-notice\">").Append(Html(notice)).Append("</p>");
        sb.Append("</div>");

        return sb.ToString();
    }

    private static async Task PostSubscribeAsync(HttpContext ctx, CedarDbContext db, BlogSite site)
    {
        var form = await ctx.Request.ReadFormAsync();
        var back = SafeLocalPath(form["back"].ToString());
        var email = form["email"].ToString().Trim().ToLowerInvariant();
        if (email.Length is 0 or > FollowerEmailMaxLength || !LooksLikeEmail(email))
        {
            RedirectWithOutcome(ctx, back, "invalid");
            return;
        }

        // The showcase follower's ceiling, shared on purpose: same anonymous form, same row+mail
        // cost per submit.
        var visitor = VisitorHash(ctx);
        var since = DateTime.UtcNow - Consts.Showcase.FollowWindow;
        var recent = await db.BlogSubscribers.CountAsync(s => s.VisitorHash == visitor && s.CreatedAt >= since);
        if (recent >= Consts.Showcase.MaxFollowsPerVisitor)
        {
            RedirectWithOutcome(ctx, back, "toomany");
            return;
        }

        var existing = await db.BlogSubscribers.FirstOrDefaultAsync(s => s.OwnerId == site.OwnerId && s.Email == email);
        if (existing is { ConfirmedAt: not null })
        {
            RedirectWithOutcome(ctx, back, "already");
            return;
        }

        // An address that asked twice without confirming gets a fresh token, not a second row —
        // the unique (OwnerId, Email) index would refuse one anyway.
        var subscriber = existing ?? new BlogSubscriber
        {
            OwnerId = site.OwnerId,
            Email = email,
            UnsubscribeToken = PrivateAccess.NewToken(),
            VisitorHash = visitor,
        };
        subscriber.ConfirmToken = PrivateAccess.NewToken();
        if (existing is null) db.BlogSubscribers.Add(subscriber);
        await db.SaveChangesAsync();

        var channel = await GetBlogChannelInfoAsync(db, site);
        var blogName = channel?.Title ?? "Cedar Clerk Blog";
        var confirmUrl = $"{site.BaseUrl}/subscribe/confirm?token={subscriber.ConfirmToken}";
        await SendFollowMailAsync(ctx, subscriber.Email,
            EmailTexts.BlogSubscribeConfirmSubject(blogName),
            EmailTexts.BlogSubscribeConfirmBody(blogName, confirmUrl));

        RedirectWithOutcome(ctx, back, "sent");
    }

    private static async Task ConfirmSubscriberAsync(HttpContext ctx, CedarDbContext db, BlogSite site)
    {
        var token = ctx.Request.Query["token"].ToString();
        var subscriber = token.Length == 0
            ? null
            : await db.BlogSubscribers.FirstOrDefaultAsync(s => s.OwnerId == site.OwnerId && s.ConfirmToken == token);
        if (subscriber is null)
        {
            RedirectWithOutcome(ctx, "/", "expired");
            return;
        }

        subscriber.ConfirmedAt = DateTime.UtcNow;
        subscriber.ConfirmToken = null;
        await db.SaveChangesAsync();
        RedirectWithOutcome(ctx, "/", "confirmed");
    }

    private static async Task RemoveSubscriberAsync(HttpContext ctx, CedarDbContext db, BlogSite site)
    {
        var token = ctx.Request.Query["token"].ToString();

        // Deleted, not flagged — same rule as the showcase follower: an address that asked to be
        // forgotten is not a row to keep.
        var removed = token.Length == 0
            ? 0
            : await db.BlogSubscribers
                .Where(s => s.OwnerId == site.OwnerId && s.UnsubscribeToken == token)
                .ExecuteDeleteAsync();

        RedirectWithOutcome(ctx, "/", removed > 0 ? "left" : "expired");
    }

    /// <summary>
    /// Redirect-after-post back to the page the box stood on, with a word for the reader. 303 so a
    /// refresh never re-posts the form.
    /// </summary>
    private static void RedirectWithOutcome(HttpContext ctx, string backPath, string outcome)
    {
        var separator = backPath.Contains('?') ? '&' : '?';
        ctx.Response.StatusCode = StatusCodes.Status303SeeOther;
        ctx.Response.Headers.Location = $"{backPath}{separator}subscribe={outcome}#subscribe";
    }

    /// <summary>
    /// The hidden back field is attacker-writable like any form value; only a same-site path may
    /// come out of it. Anything else lands on the index.
    /// </summary>
    private static string SafeLocalPath(string raw) =>
        raw.StartsWith('/') && !raw.StartsWith("//") && !raw.Contains('\\') && Uri.IsWellFormedUriString(raw, UriKind.Relative)
            ? raw
            : "/";
}
