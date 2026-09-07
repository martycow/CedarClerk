namespace CedarClerk.Localization;

public static partial class LandingTexts
{
    public static string WaitlistSuccess(bool russian) =>
        russian ? "Вы в списке — инвайт придёт на эту почту." : "You are on the list — the invite will land in this inbox.";

    public static string WaitlistFailure(bool russian) =>
        russian ? "Не получилось отправить — попробуйте ещё раз." : "Could not send — try again.";

    public static string ConsentTitle(bool russian) =>
        russian ? "Cookies для продуктовой аналитики" : "Cookies for product analytics";

    public static string ConsentDescription(bool russian) =>
        russian ? "Мы используем PostHog (EU), чтобы видеть, какими частями Cedar Clerk пользуются и где люди застревают. "
            + "Ничего из этого не продаётся и не передаётся дальше, а читателей блогов так не считают никогда." : "We use PostHog (EU) to see which parts of Cedar Clerk are used and where people get stuck. "
            + "Nothing here is sold or shared onward, and blog readers are never counted this way.";

    public static string Accept(bool russian) =>
        russian ? "Принять" : "Accept";

    public static string Decline(bool russian) =>
        russian ? "Отклонить" : "Decline";

    public static string PrivacyPolicy(bool russian) =>
        russian ? "Политика приватности" : "Privacy policy";

    public static string PageTitle(bool russian) =>
        russian ? "Cedar Clerk — публикуйтесь независимо. Показывайте, что создаёте." : "Cedar Clerk — publish independently. Show what you make.";

    public static string BlockEditor(bool russian) =>
        russian ? "Блочный редактор" : "A block editor";

    public static string EditorDescription(bool russian) =>
        russian ? "Текст, таблицы, медиа, формулы, спойлеры и код — один документ, из которого рисуется всё остальное." : "Text, tables, media, formulas, spoilers and code — one document, and everything else is drawn from it.";

    public static string PublishingToTelegram(bool russian) =>
        russian ? "Публикация в Telegram" : "Publishing to Telegram";

    public static string TelegramDescription(bool russian) =>
        russian ? "Пост уходит в канал нативными блоками: медиа с настоящей подписью, а не ссылка на картинку." : "The post reaches the channel as native blocks: media with a real caption, not a link to a picture.";

    public static string NetworksTitle(bool russian, int count) =>
        russian ? $"{count} сети из одного текста" : $"{count} networks, one text";

    public static string NetworksDescription(bool russian) =>
        russian ? "Telegram, X, Bluesky и Discord — это рендереры одного документа, а не четыре копии, которые надо держать в согласии." : "Telegram, X, Bluesky and Discord are renderers of one document, not four copies to keep in agreement.";

    public static string BlogOnYourOwnSubdomain(bool russian) =>
        russian ? "Блог на своём поддомене" : "A blog on your own subdomain";

    public static string BlogDescription(bool russian) =>
        russian ? "Не зеркало канала, а полноценный адрес: архив, поиск, RSS и ссылки, которые не пропадут." : "Not a mirror of the channel but an address of its own: archive, search, RSS and links that will not disappear.";

    public static string LanguagesTitle(bool russian, int count) =>
        russian ? $"{count} языков в одной записи" : $"{count} languages in one entry";

    public static string TranslationDescription(bool russian) =>
        russian ? "Перевод дописывает только то, что изменилось, и не трогает правки, сделанные руками." : "Auto-translate rewrites only what changed and leaves your own corrections alone.";

    public static string CommentsAndReactions(bool russian) =>
        russian ? "Комментарии и реакции" : "Comments and reactions";

    public static string DiscussionDescription(bool russian) =>
        russian ? "Обсуждение под выбранным фрагментом, а не под всей статьёй. Приватные посты открываются по форме или личной ссылке." : "Discussion under a chosen fragment, not under the whole article. Private posts open behind a form or a personal link.";

    public static string TasksAndSprints(bool russian) =>
        russian ? "Задачи и спринты" : "Tasks and sprints";

    public static string TasksDescription(bool russian) =>
        russian ? "Доска, спринты и сроки живут рядом с текстами — девлог рядом с планом, а не в другой вкладке." : "A board, sprints and due dates live beside the writing — the devlog next to the plan, not in another tab.";

    public static string AnAssetIndex(bool russian) =>
        russian ? "Индекс ассетов" : "An asset index";

    public static string AssetsDescription(bool russian) =>
        russian ? "Каждый скриншот и арт — с описанием и с тем, где он уже опубликован." : "Every screenshot and art file, described, and told where it has already been published.";

    public static string Builds(bool russian) =>
        russian ? "Сборки" : "Builds";

    public static string BuildsDescription(bool russian) =>
        russian ? "Версия, дата, изменения. Закрытый спринт собирается в черновик девлога одной кнопкой." : "Version, date, changes. A finished sprint assembles into a devlog draft in one button.";

    public static string Scheduler(bool russian) =>
        russian ? "Планировщик" : "A scheduler";

    public static string SchedulerDescription(bool russian) =>
        russian ? "Черновик уходит в назначенный час — во все подключённые сети сразу." : "A draft goes out at the hour you set — to every connected network at once.";

    public static string DayAndNight(bool russian) =>
        russian ? "День и ночь" : "Day and night";

    public static string AppearanceDescription(bool russian) =>
        russian ? "Тема, кегль и гарнитура — выбор читателя. Обе темы нарисованы, а не инвертированы." : "Theme, size and typeface are the reader's call. Both themes are drawn, not inverted.";

    public static string TheTextsStayYours(bool russian) =>
        russian ? "Тексты остаются вашими" : "The texts stay yours";

    public static string ExportDescription(bool russian) =>
        russian ? "Открытый формат и выгрузка файлом в любой момент — вместе с медиа, без переговоров." : "An open format and an export file whenever you ask — media included, no conversation required.";

    public static string Free(bool russian) =>
        russian ? "Бесплатно" : "Free";

    public static string Forever(bool russian) =>
        russian ? "/ навсегда" : "/ forever";

    public static string OneChannelAndYourOwnBlog(bool russian) =>
        russian ? "Один канал и свой блог" : "One channel and your own blog";

    public static string Start(bool russian) =>
        russian ? "старт" : "start";

    public static string SingleChannelLimit(bool russian, int count) =>
        russian ? $"{count} канал" : $"{count} channel";

    public static string MediaLimit(bool russian, string gigabytes) =>
        russian ? $"{gigabytes} на медиа" : $"{gigabytes} of media";

    public static string BlogCommentsReactionsRSS(bool russian) =>
        russian ? "Блог, комментарии, реакции, RSS" : "Blog, comments, reactions, RSS";

    public static string FreeSignature(bool russian) =>
        russian ? "Подпись Cedar Clerk под постом" : "A Cedar Clerk line under each post";

    public static string PerMonth(bool russian) =>
        russian ? "/ мес" : "/ mo";

    public static string SeveralChannelsAndYourOwnVoice(bool russian) =>
        russian ? "Несколько каналов и свой голос" : "Several channels and your own voice";

    public static string Popular(bool russian) =>
        russian ? "популярный" : "popular";

    public static string ProChannelLimit(bool russian, int count) =>
        russian ? $"{count} канала" : $"{count} channels";

    public static string YourOwnSignatureWithLink(bool russian) =>
        russian ? "Своя подпись со ссылкой" : "Your own signature, with a link";

    public static string HeaderSlotLimit(bool russian, int count) =>
        russian ? $"{count} слота в шапке поста" : $"{count} slots in the post header";

    public static string WithAITranslationAndEditing(bool russian) =>
        russian ? "С переводом и правкой через ИИ" : "With AI translation and editing";

    public static string ProPlusChannelLimit(bool russian, int count) =>
        russian ? $"{count} каналов" : $"{count} channels";

    public static string DailyAiLimit(bool russian, int count) =>
        russian ? $"Перевод и правка через ИИ — {count} операций в день" : $"AI translation and editing — {count} operations a day";

    public static string EverythingInPro(bool russian) =>
        russian ? "Всё из Pro" : "Everything in Pro";

    public static string Community(bool russian) =>
        russian ? "Сообщество" : "Community";

    public static string Screenshots(bool russian) =>
        russian ? "Скриншоты" : "Screenshots";

    public static string WhatItDoes(bool russian) =>
        russian ? "Что умеет" : "What it does";

    public static string Pricing(bool russian) =>
        russian ? "Цены" : "Pricing";

    public static string Story(bool russian) =>
        russian ? "История" : "Story";

    public static string Download(bool russian) =>
        russian ? "Скачать" : "Download";

    public static string WhatItLooksLike(bool russian) =>
        russian ? "Как это выглядит" : "What it looks like";

    public static string RealScreenshotsNotMockups(bool russian) =>
        russian ? "живые скриншоты, не мокапы" : "real screenshots, not mockups";

    public static string DoneInProgressNext(bool russian) =>
        russian ? "что готово, что в работе, что дальше" : "done, in progress, next";

    public static string WhyThisExists(bool russian) =>
        russian ? "Зачем это сделано" : "Why this exists";

    public static string BrieflyByMilestones(bool russian) =>
        russian ? "коротко, по вехам" : "briefly, by milestones";

    public static string TheDesktopApp(bool russian) =>
        russian ? "Приложение для рабочего стола" : "The desktop app";

    public static string TheSameBenchInItsOwnWindow(bool russian) =>
        russian ? "Тот же верстак, в своём окне" : "The same bench, in its own window";

    public static string DesktopDescription(bool russian) =>
        russian ? "Всё лежит на вашем аккаунте на сервере — приложение только другая дверь к нему. Установите на второй машине и продолжайте с того же места." : "Everything lives in your account on the server — the app is just another door to it. Install it on a second machine and pick up where you left off.";

    public static string KeepsItselfUpdatedWithEveryRelease(bool russian) =>
        russian ? "обновляется само при каждом релизе" : "keeps itself updated with every release";

    public static string DownloadForWindows(bool russian) =>
        russian ? "Скачать для Windows" : "Download for Windows";

    public static string InviteOnlyBeta(bool russian) =>
        russian ? "бета по инвайтам" : "invite-only beta";

    public static string LogIn(bool russian) =>
        russian ? "Войти" : "Log in";

    public static string JoinTheWaitlist(bool russian) =>
        russian ? "В лист ожидания" : "Join the waitlist";

    public static string LanguageSummary(bool russian, int count) =>
        russian ? $"{count} языков · RU / EN интерфейс" : $"{count} languages · RU / EN interface";

    public static string EmailForAnInvite(bool russian) =>
        russian ? "Почта для инвайта" : "Email for an invite";

    public static string Email(bool russian) =>
        russian ? "Почта" : "Email";

    public static string SaveMySeat(bool russian) =>
        russian ? "Занять место" : "Save my seat";

    public static string WaitlistHint(bool russian) =>
        russian ? "Одно письмо, когда откроются двери. Рассылки не будет." : "One letter when the doors open. No newsletter.";

    public static string PostOnCedarClerkBlog(bool russian) =>
        russian ? "Пост на блоге Cedar Clerk" : "A post on a Cedar Clerk blog";

    public static string OnePostEveryAddress(bool russian) =>
        russian ? "один пост — все адреса" : "one post, every address";

    public static string Draft(bool russian) =>
        russian ? "черновик" : "draft";

    public static string Blog(bool russian) =>
        russian ? "блог" : "blog";

    public static string WhatIsAlreadyOnTheBench(bool russian) =>
        russian ? "Что уже стоит на верстаке" : "What is already on the bench";

    public static string Tools(bool russian) =>
        russian ? "инструментов" : "tools";

    public static string WhatItCosts(bool russian) =>
        russian ? "Сколько стоит" : "What it costs";

    public static string PricingHint(bool russian) =>
        russian ? "цифры берутся из кода, который их применяет" : "these numbers come from the code that enforces them";

    public static string TrialPrice(bool russian, int price) =>
        russian ? $"Пробный доступ — ${price} за семь дней Pro+, один раз на аккаунт. Регистрация пока по инвайтам." : $"A trial is ${price} for seven days of Pro+, once per account. Registration is invite-only for now.";

    public static string TheDoorsOpenByList(bool russian) =>
        russian ? "Двери открываются по списку" : "The doors open by list";

    public static string InviteHint(bool russian) =>
        russian ? "Оставьте почту — инвайт придёт, когда мы будем готовы вас впустить." : "Leave an email — the invite arrives when we are ready to let you in.";

    public static string Terms(bool russian) =>
        russian ? "Условия" : "Terms";

    public static string Privacy(bool russian) =>
        russian ? "Приватность" : "Privacy";

    public static string LiveBlog(bool russian) =>
        russian ? "живой блог" : "a live blog";

}
