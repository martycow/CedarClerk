using CedarClerk.Localization;

namespace CedarClerk.Server.Email;

/// <summary>
/// The mail this server sends, in the reader's language (T-002). Separate from `ErrorMessages`
/// because these are not errors and are the only strings here carrying markup — but they read the
/// same <c>CurrentUICulture</c>, so a Russian account gets a Russian mail without a call site
/// passing anything.
/// </summary>
public static class EmailTexts
{
    public static string ConfirmSubject => Localized(
        "Подтвердите адрес почты — Cedar Clerk",
        "Confirm your email — Cedar Clerk");

    /// <summary>
    /// Deliberately plain HTML: mail clients disagree about almost everything else, and a link
    /// that works in all of them beats a design that works in some. The raw URL is repeated under
    /// the button because a client that strips the anchor still leaves something usable.
    /// </summary>
    public static string ConfirmBody(string link) => Localized(
        $"""
         <p>Здравствуйте!</p>
         <p>Подтвердите адрес почты, чтобы им можно было восстановить доступ к аккаунту Cedar Clerk.</p>
         <p><a href="{link}" style="display:inline-block;padding:10px 18px;background:#566842;color:#fff;border-radius:8px;text-decoration:none">Подтвердить почту</a></p>
         <p style="color:#686257;font-size:13px">Если кнопка не работает, откройте ссылку: <br>{link}</p>
         <p style="color:#686257;font-size:13px">Если вы не регистрировались в Cedar Clerk, просто не отвечайте на это письмо.</p>
         """,
        $"""
         <p>Hello!</p>
         <p>Confirm your email address so it can be used to recover access to your Cedar Clerk account.</p>
         <p><a href="{link}" style="display:inline-block;padding:10px 18px;background:#566842;color:#fff;border-radius:8px;text-decoration:none">Confirm email</a></p>
         <p style="color:#686257;font-size:13px">If the button does not work, open this link:<br>{link}</p>
         <p style="color:#686257;font-size:13px">If you did not sign up for Cedar Clerk, simply ignore this message.</p>
         """);

    public static string FollowConfirmSubject(string projectName) => Localized(
        $"Подтвердите подписку на девлог — {projectName}",
        $"Confirm your devlog subscription — {projectName}");

    public static string FollowConfirmBody(string projectName, string link) => Localized(
        $"""
         <p>Кто-то (надеемся, вы) подписался на девлог игры «{projectName}».</p>
         <p><a href="{link}" style="display:inline-block;padding:10px 18px;background:#566842;color:#fff;border-radius:8px;text-decoration:none">Подтвердить подписку</a></p>
         <p style="color:#686257;font-size:13px">Если кнопка не работает, откройте ссылку:<br>{link}</p>
         <p style="color:#686257;font-size:13px">Если это были не вы, просто не открывайте ссылку — без неё подписка не включится.</p>
         """,
        $"""
         <p>Someone (we hope you) subscribed to the devlog of "{projectName}".</p>
         <p><a href="{link}" style="display:inline-block;padding:10px 18px;background:#566842;color:#fff;border-radius:8px;text-decoration:none">Confirm subscription</a></p>
         <p style="color:#686257;font-size:13px">If the button does not work, open this link:<br>{link}</p>
         <p style="color:#686257;font-size:13px">If this was not you, do not open the link — without it nothing is turned on.</p>
         """);

    public static string DevlogSubject(string projectName, string postTitle) => Localized(
        $"{projectName}: {postTitle}",
        $"{projectName}: {postTitle}");

    public static string DevlogBody(string projectName, string postTitle, string postUrl, string unsubscribeUrl) => Localized(
        $"""
         <p>Новый девлог игры «{projectName}»:</p>
         <p><a href="{postUrl}" style="font-size:17px">{postTitle}</a></p>
         <p style="color:#686257;font-size:13px"><a href="{unsubscribeUrl}" style="color:#686257">Отписаться</a></p>
         """,
        $"""
         <p>A new devlog for "{projectName}":</p>
         <p><a href="{postUrl}" style="font-size:17px">{postTitle}</a></p>
         <p style="color:#686257;font-size:13px"><a href="{unsubscribeUrl}" style="color:#686257">Unsubscribe</a></p>
         """);

    /// <summary>
    /// Written in the inviter's language rather than the reader's: the invitee has no account yet,
    /// so there is no preference to read, and the person choosing the words is the one sending it.
    /// </summary>
    public static string ProjectInviteSubject(string projectName) => Localized(
        $"Приглашение в проект «{projectName}» — Cedar Clerk",
        $"You are invited to \"{projectName}\" — Cedar Clerk");

    public static string ProjectInviteBody(string projectName, string inviterName, string link)
    {
        var project = System.Net.WebUtility.HtmlEncode(projectName);
        var who = System.Net.WebUtility.HtmlEncode(inviterName);

        return Localized(
            $"""
             <p>{who} приглашает вас поработать над проектом «{project}» в Cedar Clerk.</p>
             <p><a href="{link}" style="display:inline-block;padding:10px 18px;background:#566842;color:#fff;border-radius:8px;text-decoration:none">Принять приглашение</a></p>
             <p style="color:#686257;font-size:13px">Если кнопка не работает, откройте ссылку:<br>{link}</p>
             <p style="color:#686257;font-size:13px">Если вы не ждали этого письма, просто не открывайте ссылку.</p>
             """,
            $"""
             <p>{who} invites you to work on "{project}" in Cedar Clerk.</p>
             <p><a href="{link}" style="display:inline-block;padding:10px 18px;background:#566842;color:#fff;border-radius:8px;text-decoration:none">Accept the invitation</a></p>
             <p style="color:#686257;font-size:13px">If the button does not work, open this link:<br>{link}</p>
             <p style="color:#686257;font-size:13px">If you were not expecting this, simply do not open the link.</p>
             """);
    }

    private static string Localized(string ru, string en) =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? ru : en;
}
