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

    private static string Localized(string ru, string en) =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? ru : en;
}
