namespace CedarClerk.Localization;

public static partial class BlogTexts
{
    public static string Reply(bool english) => english ? "Reply" : "Ответить";

    public static string DevlogPostCount(bool english, int count) => english
        ? $"{count} devlog {(count == 1 ? "post" : "posts")}" : $"Постов в девлоге: {count}";

    public static string OpenInTelegram(bool english) =>
        english ? "Open in Telegram" : "Открыть в Telegram";

    public static string AuthorLinks(bool english) =>
        english ? "Author links" : "Ссылки автора";

    public static string Author(bool english) =>
        english ? "Author" : "Автор";

    public static string AllPosts(bool english) =>
        english ? "All posts" : "Все записи";

    public static string ArchiveDescription(bool english) =>
        english ? "Browse the archive or find something to read." : "Листайте архив или найдите интересную тему.";

    public static string FollowByEmail(bool english) =>
        english ? "Follow by email" : "Подписаться на почту";

    public static string Topics(bool english) =>
        english ? "Topics" : "Темы";

    public static string TopicsHint(bool english) =>
        english ? "Choose topics to narrow the list. Posts must match every selected topic." : "Выберите темы. В списке останутся записи со всеми выбранными тегами.";

    public static string Posts(bool english) =>
        english ? "Posts: " : "Записей: ";

    public static string ResetFilters(bool english) =>
        english ? "Reset filters" : "Сбросить фильтры";

    public static string Parts(bool english) =>
        english ? " parts" : " частей";

    public static string EmptySeriesHtml(bool english) =>
        english ? "<p class=\"empty\">Nothing published in this series yet.</p>" : "<p class=\"empty\">В этой серии пока ничего не опубликовано.</p>";

    public static string Part(bool english) =>
        english ? "Part " : "Часть ";

    public static string BackToPosts(bool english) =>
        english ? "All posts" : "Все посты";

    public static string CopyLink(bool english) =>
        english ? "Copy link" : "Скопировать ссылку";

    public static string Copied(bool english) =>
        english ? "Copied!" : "Скопировано!";

    public static string Previous(bool english) =>
        english ? "Previous" : "Предыдущая";

    public static string Next(bool english) =>
        english ? "Next" : "Следующая";

    public static string NewerPost(bool english) =>
        english ? "Newer post" : "Следующая запись";

    public static string OlderPost(bool english) =>
        english ? "Older post" : "Предыдущая запись";

    public static string ReadNext(bool english) =>
        english ? "Read next" : "Читать дальше";

    public static string BackToTop(bool english) =>
        english ? "Back to top" : "Наверх";

    public static string CheckYourInboxTheLinkInThat(bool english) =>
        english ? "Check your inbox — the link in that mail is what turns it on." : "Проверьте почту — подписка включится по ссылке из письма.";

    public static string DoneYouAreFollowingThisGame(bool english) =>
        english ? "Done — you are following this game." : "Готово — вы подписаны на эту игру.";

    public static string YouAreUnsubscribed(bool english) =>
        english ? "You are unsubscribed." : "Вы отписались.";

    public static string ThatDoesNotLookLikeAnEmail(bool english) =>
        english ? "That does not look like an email address." : "Это не похоже на адрес почты.";

    public static string ThatAddressIsAlreadyFollowing(bool english) =>
        english ? "That address is already following." : "Этот адрес уже подписан.";

    public static string TooManyAttemptsTryAgainTomorrow(bool english) =>
        english ? "Too many attempts. Try again tomorrow." : "Слишком много попыток. Попробуйте завтра.";

    public static string ThatLinkIsNoLongerValid(bool english) =>
        english ? "That link is no longer valid." : "Ссылка больше не действует.";

    public static string FollowTheDevlog(bool english) =>
        english ? "Follow the devlog" : "Следить за девлогом";

    public static string YourEmail(bool english) =>
        english ? "your@email" : "ваша@почта";

    public static string EmailAddress(bool english) =>
        english ? "Email address" : "Адрес почты";

    public static string Follow(bool english) =>
        english ? "Follow" : "Подписаться";

    public static string MailWhenNewDevlogIsOutAnd(bool english) =>
        english ? "A mail when a new devlog is out, and nothing else. Unsubscribe from any of them." : "Письмо, когда выходит новый девлог, и ничего кроме. Отписаться можно из любого письма.";

    public static string DownloadPressPackZip(bool english) =>
        english ? "Download press pack (.zip)" : "Скачать пресс-пак (.zip)";

    public static string Factsheet(bool english) =>
        english ? "Factsheet" : "Факты";

    public static string Developer(bool english) =>
        english ? "Developer" : "Разработчик";

    public static string Price(bool english) =>
        english ? "Price" : "Цена";

    public static string Engine(bool english) =>
        english ? "Engine" : "Движок";

    public static string Genre(bool english) =>
        english ? "Genre" : "Жанр";

    public static string Links(bool english) =>
        english ? "Links" : "Ссылки";

    public static string PressContact(bool english) =>
        english ? "Press contact" : "Контакт для прессы";

    public static string LogoKeyArt(bool english) =>
        english ? "Logo & key art" : "Лого и арты";

    public static string CoverImage(bool english) =>
        english ? "Cover image" : "Обложка";

    public static string AboutTheProject(bool english) =>
        english ? "About the project" : "О проекте";

    public static string Screenshots(bool english) =>
        english ? "Screenshots" : "Скриншоты";

    public static string DownloadAll(bool english) =>
        english ? "download all" : "скачать все";

    public static string Trailer(bool english) =>
        english ? "Trailer" : "Трейлер";

    public static string Devlog(bool english) =>
        english ? "Devlog" : "Девлог";

    public static string ReadTheFeed(bool english) =>
        english ? "read the feed" : "читать ленту";

    public static string Latest(bool english) =>
        english ? "Latest: " : "Последний: ";

    public static string DraftPreviewThisIsWorkingCopyShared(bool english) =>
        english ? "Draft preview — this is a working copy, shared by its author. It may change or disappear." : "Предпросмотр черновика — это рабочая копия, которой поделился автор. Она может измениться или исчезнуть.";

    public static string SearchPosts(bool english) =>
        english ? "Search posts" : "Поиск по постам";

    public static string Search(bool english) =>
        english ? "Search" : "Найти";

    public static string SearchHeading(bool english) =>
        english ? "Search" : "Поиск";

    public static string NothingFound(bool english) =>
        english ? "Nothing found." : "Ничего не найдено.";

    public static string ProjectShowcase(bool english) =>
        english ? "Project showcase" : "Страница проекта";

    public static string NoProjectDescriptionHasBeenAddedYet(bool english) =>
        english ? "No project description has been added yet." : "Описание проекта пока не добавлено.";

    public static string PressKit(bool english) =>
        english ? "Press kit" : "Пресс-кит";

    public static string ProjectHighlights(bool english) =>
        english ? "Project highlights" : "О проекте в цифрах";

    public static string DevlogEntries(bool english) =>
        english ? "Devlog entries" : "Записи девлога";

    public static string PublicBuilds(bool english) =>
        english ? "Public builds" : "Публичные сборки";

    public static string About(bool english) =>
        english ? "About" : "О проекте";

    public static string NoDescriptionHasBeenAdded(bool english) =>
        english ? "No description has been added." : "Описание пока не добавлено.";

    public static string FindTheProject(bool english) =>
        english ? "Find the project" : "Где найти проект";

    public static string ProjectTrailer(bool english) =>
        english ? "Project trailer" : "Трейлер проекта";

    public static string Gallery(bool english) =>
        english ? "Gallery" : "Галерея";

    public static string Downloads(bool english) =>
        english ? "Downloads" : "Скачать";

    public static string NothingPublishedYet(bool english) =>
        english ? "Nothing published yet." : "Пока ничего не опубликовано.";

    public static string PublicProjectUpdatesWillAppearHere(bool english) =>
        english ? "Public project updates will appear here." : "Здесь появятся публичные новости проекта.";

    public static string FollowLabel(bool english) =>
        english ? "Follow" : "Следить за проектом";

    public static string Roadmap(bool english) =>
        english ? "Roadmap" : "Роадмап";

    public static string InProgress(bool english) =>
        english ? "In progress" : "В работе";

    public static string Planned(bool english) =>
        english ? "Planned" : "Запланировано";

    public static string Someday(bool english) =>
        english ? "Someday" : "Когда-нибудь";

    public static string Done(bool english) =>
        english ? "Done" : "Готово";

    public static string Projects(bool english) =>
        english ? "Projects" : "Проекты";

    public static string Project(bool english) =>
        english ? "Project" : "Проект";

    public static string DoneYouAreSubscribed(bool english) =>
        english ? "Done — you are subscribed." : "Готово — вы подписаны.";

    public static string ThatAddressIsAlreadySubscribed(bool english) =>
        english ? "That address is already subscribed." : "Этот адрес уже подписан.";

    public static string NewPostsByEmail(bool english) =>
        english ? "New posts by email" : "Новые посты на почту";

    public static string Subscribe(bool english) =>
        english ? "Subscribe" : "Подписаться";

    public static string SubscriptionHint(bool english) =>
        english ? "A mail when a new post is published, and nothing else. Unsubscribe from any of them." : "Письмо, когда выходит новый пост, и ничего кроме. Отписаться можно из любого письма.";

    public static string Contents(bool english) =>
        english ? "Contents" : "Оглавление";

    public static string Comments(bool english) =>
        english ? "Comments" : "Комментарии";

    public static string ShowMoreComments(bool english) =>
        english ? "Show more comments" : "Показать больше комментариев";

    public static string NameOptional(bool english) =>
        english ? "Name (optional)" : "Имя (необязательно)";

    public static string AddComment(bool english) =>
        english ? "Add a comment…" : "Добавить комментарий…";

    public static string Send(bool english) =>
        english ? "Send" : "Отправить";

    public static string ReplyingTo(bool english) =>
        english ? "Replying to" : "Ответ";

    public static string Cancel(bool english) =>
        english ? "Cancel" : "Отмена";

    public static string PostPublished(bool english) =>
        english ? "Post published" : "Пост опубликован";

    public static string Like(bool english) =>
        english ? "Like" : "Нравится";

    public static string Dislike(bool english) =>
        english ? "Dislike" : "Не нравится";

}
