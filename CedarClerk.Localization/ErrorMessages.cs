using System.Globalization;

namespace CedarClerk.Localization;

/// <summary>
/// T-050 — server-side messages, in the reader's language.
///
/// The language comes from <see cref="CultureInfo.CurrentUICulture"/>, which the server sets per
/// request from the signed-in account's <c>UiLanguage</c> (falling back to Accept-Language). That
/// is deliberate: .NET already flows CurrentUICulture across await boundaries, so **no call site
/// has to pass a language** — every existing `ErrorMessages.X` reference keeps working and simply
/// starts answering in Russian for a Russian UI.
///
/// Members are properties, not consts: a const is baked into the caller at compile time and could
/// never be language-dependent.
///
/// **T-050 is closed (01.08.2026)**: the ~60 inline English literals that used to live in the
/// endpoint files are all here now, and `ErrorMessageLocalizationTests` fails the build if a new
/// one is written — verified to actually go red, not merely to exist.
/// </summary>
public static class ErrorMessages
{
    public static string DraftNotFound => Ru("Черновик не найден.", "Draft not found.");
    public static string InvalidToken => Ru("Недействительный токен.", "Invalid token.");
    // T-050, second half (01.08.2026): the ~60 messages that used to live as inline English
    // literals in the endpoint files. Moved verbatim in meaning — this is a translation, not a
    // rewording, so a message someone already recognises stays recognisable.
    public static string DescriptionRequired => Ru("Нужно описание.", "A description is required");
    public static string HandleAndAppPasswordRequired => Ru("Нужны хэндл и app-пароль.", "A handle and an app password are required");
    public static string TermRequired => Ru("Нужен термин.", "A term is required");
    public static string AiEditProPlus => Ru("Правка через ИИ доступна с тарифа Pro и оплачивается кредитами.", "AI editing needs Pro and is paid in credits.");
    public static string AiEditNotConfigured => Ru("Правка через ИИ не настроена.", "AI editing is not configured");
    public static string AppearancePrefsTooLarge => Ru("Настройки оформления слишком большие.", "Appearance preferences are too large");
    public static string AutoTranslateNotConfigured => Ru("Авто-перевод не настроен.", "Auto-translate is not configured");
    public static string AvatarMustBeUploaded => Ru("Аватар должен быть загруженным изображением.", "Avatar must be an uploaded image");
    public static string AssetInUse => Ru("Файл используется в постах — сначала уберите его оттуда.", "This file is used by posts — remove it from them first");
    public static string AssetIdOrPathRequired => Ru("Нужен id файла или путь к нему.", "An asset id or path is required");
    public static string BothTagsRequired => Ru("Нужны и старый, и новый тег.", "Both the old and the new tag are required");
    public static string InvalidEmail => Ru("Введите корректный адрес почты.", "Enter a valid email address");
    public static string ImportFileNotFound => Ru("Файл не найден в каталоге import-tmp.", "File not found in import-tmp directory.");
    public static string FormTooLarge => Ru("Форма слишком большая.", "Form is too large");
    public static string InvalidDocumentStructure => Ru("Некорректная структура документа.", "Invalid document structure.");
    public static string InvalidFileName => Ru("Некорректное имя файла.", "Invalid file name.");
    public static string InvalidInviteCode => Ru("Неверный инвайт-код.", "Invalid invite code");
    public static string InvalidTelegramSignature => Ru("Подпись входа через Telegram недействительна или устарела.", "Invalid or expired Telegram login signature");
    public static string ExternalProviderNotConfigured =>
        Ru("Этот способ входа не настроен на сервере.", "This sign-in method is not configured on this server");
    public static string ExternalLoginExpired =>
        Ru("Вход через провайдера истёк — начните заново.", "The provider sign-in expired — start again");
    public static string ExternalNoEmail =>
        Ru("Провайдер не вернул адрес почты — войдите паролем.", "The provider returned no email address — sign in with a password instead");
    public static string ExternalAlreadyLinkedToOther =>
        Ru("Этот аккаунт провайдера уже привязан к другому аккаунту Cedar Clerk.", "This provider account is already linked to a different Cedar Clerk account");
    public static string ExternalEmailTaken =>
        Ru("На этот адрес уже есть аккаунт — войдите паролем, и привязка добавится сама.", "An account already holds this address — sign in with your password and the link will be added");
    public static string ExternalLastWayIn =>
        Ru("Это единственный способ войти в аккаунт — сначала задайте пароль.", "This is the only way into this account — set a password first");
    public static string TelegramNoAccount =>
        Ru("К этому Telegram не привязан аккаунт Cedar Clerk. Войдите другим способом и привяжите его в Настройках.", "No Cedar Clerk account is linked to this Telegram. Sign in another way and link it in Settings");
    public static string NewDraftDefaultsTooLarge => Ru("Настройки нового черновика слишком большие.", "New-draft defaults are too large");
    public static string NoMarkdownInZip => Ru("Внутри архива нет ни одного .md-файла.", "No .md file found inside the zip.");
    public static string NoStripeSubscription => Ru("На этом аккаунте нет подписки Stripe.", "No Stripe subscription on this account");
    public static string ChannelNotFoundOrNoAccess => Ru("Канал не найден или нет доступа к нему.", "No TG-channel was found or no access to that channel");
    public static string LinkTelegramBeforeChannel =>
        Ru("Сначала привяжите свой Telegram в Настройках → Интеграции — так мы проверим, что канал ваш.",
           "Link your Telegram in Settings → Integrations first — that is how we confirm the channel is yours.");
    public static string NotChannelAdmin =>
        Ru("Вы не администратор этого канала — подключить можно только свой.",
           "You are not an administrator of this channel — you can only connect your own.");
    public static string NoAccountWithEmail => Ru("Аккаунта с такой почтой нет.", "No account with that email.");
    public static string NoSuchInviteCode => Ru("Такого инвайт-кода нет.", "No such invite code");
    public static string NoTermsInLanguage => Ru("На этом языке терминов нет.", "No terms in this language");
    public static string NothingToTranslate => Ru("Нечего переводить — сначала напишите тексты на исходном языке.", "Nothing to translate — write the texts in the source language first");
    public static string DestinationNotConnected => Ru("Одно из назначений не подключено.", "One of those destinations is not connected");
    public static string PickADestination => Ru("Выберите хотя бы одно назначение.", "Pick at least one destination");
    public static string PickALanguage => Ru("Выберите хотя бы один язык для перевода.", "Pick at least one language to translate into");
    public static string SignatureIsPro => Ru("Подпись под постом доступна на Pro — перейдите на этот план.", "Post signature is a Pro feature. Upgrade to use it.");
    public static string SignatureTranslationsTooLarge => Ru("Переводы подписи слишком большие.", "Signature translations are too large");
    public static string PresetHasNoForm => Ru("У пресета нет формы.", "Preset has no form");
    public static string PublishToBlogFirst => Ru("Сначала опубликуйте черновик в блоге.", "Publish this draft to the blog first");
    public static string RegistrationFormTooLarge => Ru("Форма регистрации слишком большая.", "Registration form is too large");
    public static string SameSourceAndTarget => Ru("Исходный и целевой языки совпадают.", "Source and target language are the same");
    public static string TagRequired => Ru("Нужен тег.", "Tag is required");
    public static string BotNotRunningNoToken => Ru("Telegram-бот не запущен (не настроен токен).", "Telegram bot is not running (no token configured)");
    public static string SlugHasNoUsableCharacters => Ru("В этом адресе нет пригодных символов.", "That URL has no usable characters");
    public static string SlugTaken => Ru("Такой адрес уже занят.", "That URL is already taken");
    public static string SeriesNameEmptySlug => Ru("Из названия серии не получается адрес — добавьте буквы или цифры.", "Series name produces an empty URL — add letters or digits");
    public static string SeriesNameTaken => Ru("Серия с таким названием уже есть.", "A series with this name already exists");
    public static string TreeWouldCycle => Ru("Документ нельзя вложить в самого себя или в собственный поддокумент.", "A document cannot be nested inside itself or its own child");
    public static string TreeTooDeep => Ru("Дерево слишком глубокое — максимум 10 уровней.", "The tree is too deep — 10 levels at most");
    public static string InviteCodeExists => Ru("Такой код уже существует.", "That code already exists");
    public static string NameReservedForAuthor => Ru("Это имя зарезервировано за автором поста.", "That name is reserved for the post's author.");
    public static string NotAZipArchive => Ru("Файл не является корректным .zip-архивом.", "The file is not a valid .zip archive.");
    public static string FormHasNoText => Ru("В форме пока нет текста для перевода.", "The form has no text to translate yet");
    public static string FormAlreadyInLanguage => Ru("Форма уже написана на этом языке.", "The form is already written in this language");
    public static string ServiceMustBeHttps => Ru("Адрес сервиса должен быть https://-ссылкой.", "The service address must be an https:// URL");
    public static string TermAlreadyInLanguage => Ru("Термин уже есть на этом языке.", "The term is already in this language");
    public static string ThirdSlotIsPro => Ru("Третий слот шапки доступен на Pro — перейдите на этот план.", "The third header slot is a Pro feature. Upgrade to use it.");
    public static string TranslationUnusable => Ru("Перевод вернулся непригодным — попробуйте ещё раз.", "The translation came back unusable — try again");
    public static string TelegramAlreadyLinked => Ru("Этот Telegram-аккаунт уже привязан к другому аккаунту Cedar Clerk.", "This Telegram account is already linked to another Cedar Clerk account");
    public static string ToolbarLayoutTooLarge => Ru("Раскладка панели слишком большая.", "Toolbar layout is too large");
    public static string TrialAlreadyUsed => Ru("Пробный период уже использован на этом аккаунте.", "Trial has already been used on this account");
    public static string UnsupportedChatType => Ru("Неподдерживаемый тип чата.", "Unsupported chat type");
    public static string UnsupportedUiLanguage => Ru("Неподдерживаемый язык интерфейса.", "Unsupported interface language");
    public static string WatermarkTooLong => Ru("Текст водяного знака слишком длинный.", "Watermark text is too long");
    public static string LocationTooLong => Ru("Название локации слишком длинное.", "Location is too long");
    public static string FeedbackEmpty => Ru("Напишите сообщение.", "Write a message.");
    public static string FeedbackTooLong => Ru("Сообщение слишком длинное.", "The message is too long.");
    public static string UnknownPresetKind => Ru("Неизвестный тип пресета.", "Unknown preset kind.");
    public static string PresetNameInvalid => Ru("Нужно название пресета (до 60 символов).", "A preset needs a name (up to 60 characters).");
    public static string TeamNameInvalid(int max) =>
        Ru($"Нужно название команды (до {max} символов).", $"A team needs a name (up to {max} characters).");
    public static string TeamLimitReached(int limit) =>
        Ru($"Больше {limit} команд создать нельзя.", $"No more than {limit} teams.");
    public static string TeamMemberLimitReached(int limit) =>
        Ru($"В команде не может быть больше {limit} участников.", $"A team holds at most {limit} people.");
    public static string TeamMemberAlreadyInvited =>
        Ru("Этот адрес уже приглашён в команду.", "That address is already invited to this team.");
    public static string TeamMemberBanned =>
        Ru("Этот адрес заблокирован в команде — снимите блокировку, чтобы пригласить снова.",
            "That address is banned from this team — lift the ban to invite it again.");
    public static string UnknownTeamMemberStatus(string status) =>
        Ru($"Неизвестный статус участника: {status}.", $"Unknown member status: {status}.");

    public static string PresetKindImmutable => Ru("Тип пресета менять нельзя.", "A preset's kind cannot be changed.");
    public static string PresetLimitReached(int limit) =>
        Ru($"Больше {limit} пресетов одного типа хранить нельзя.", $"No more than {limit} presets of one kind.");
    public static string CannotChangeOwnAdmin => Ru("Нельзя менять собственные права администратора.", "You cannot change your own admin rights");
    public static string CannotLockOwnAccount => Ru("Нельзя заблокировать собственный аккаунт.", "You cannot lock your own account");
    public static string CannotDeleteOwnAccount => Ru("Нельзя удалить собственный аккаунт.", "You cannot delete your own account");

    public static string StripePlanNotConfigured => Ru("Stripe не настроен для этого плана — см. docs/integrations-setup.md", "Stripe is not configured for this plan — see docs/integrations-setup.md");
    public static string StripeNotConfigured => Ru("Stripe не настроен — см. docs/integrations-setup.md", "Stripe is not configured — see docs/integrations-setup.md");
    public static string PayPalAuthFailed => Ru("PayPal не принял ключи — проверьте ClientId/Secret и Cedar:PayPal:Mode (live или sandbox)", "PayPal auth failed — check ClientId/Secret (and Cedar:PayPal:Mode: live vs sandbox)");
    public static string PayPalNoApprovalLink => Ru("PayPal не вернул ссылку на подтверждение.", "PayPal did not return an approval link");
    public static string BlueskyCredentialsRefused => Ru("Bluesky не принял эти данные — проверьте хэндл и используйте app-пароль, а не пароль от аккаунта.", "Bluesky refused those credentials — check the handle and use an app password, not your account password");
    public static string DraftNotFoundPlain => Ru("Черновик не найден.", "Draft not found");
    public static string ScheduleOnlyToOwnChannels => Ru("Планировать можно только в подключённые каналы — сначала подключите канал (окно «Каналы»).", "You can only schedule posts to your connected channels — connect this channel first (Channels popup)");
    /// <summary>T-106 — an earlier part of the thread did not go out, so this one must not either.</summary>
    public static string ThreadPartAbandoned(int index) => Ru(
        $"Предыдущая часть треда не ушла — часть {index + 1} не отправлена, чтобы в канале не осталось дыры.",
        $"An earlier part of the thread did not go out — part {index + 1} was held back rather than leaving a gap.");

    public static string UnknownNetwork(string network) => Ru(
        $"Неизвестная сеть: {network}", $"Unknown network: {network}");

    public static string DraftIsEmpty => Ru("Черновик пуст.", "Draft is empty.");

    /// <summary>T-106 — the document shrank between parts, so the part being sent no longer exists.</summary>
    public static string ThreadPartGone => Ru(
        "Документ изменился во время публикации треда — часть больше не существует. Опубликуйте заново.",
        "The document changed while the thread was being published — this part no longer exists. Publish again.");

    public static string BotNotRunning => Ru("Telegram-бот не запущен.", "Telegram bot is not running.");
    /// <summary>ADR-189 — the bounds come from the caller: they are CreditPacks' to state, and
    /// this project does not reference Core.</summary>
    public static string CreditAmountOutOfRange(int min, int max) => Ru(
        $"Укажите пакет или количество кредитов от {min} до {max}.",
        $"Name a pack, or a number of credits between {min} and {max}.");

    // T-089 — the one Bluesky failure an author can act on: the stored app password no longer opens
    // a session (revoked in Bluesky's settings, or unreadable because the DataProtection key ring
    // and the database were separated — see PublishTargetSecrets).
    /// <summary>
    /// A document the network would refuse, known before sending (T-086). The issue list travels
    /// beside this so the client can say WHICH limit — this line only says whose verdict it is.
    /// </summary>
    public static string PublishWontFit(string network) => Ru(
        $"Пост не помещается в ограничения сети {network} — исправьте отмеченное и попробуйте снова.",
        $"This post does not fit {network}'s limits — fix what is listed and try again.");

    public static string XReconnect => Ru(
        "Подключение X недействительно — переподключите аккаунт в настройках публикации.",
        "The X connection is no longer valid — reconnect the account in publishing settings.");
    public static string XNotConfigured => Ru(
        "Публикация в X не настроена на сервере.",
        "X publishing is not configured on the server.");
    public static string NotEnoughCredits => Ru(
        "Недостаточно кредитов — пополните баланс в Настройках → Кредиты.",
        "Not enough credits — top up your balance in Settings → Credits.");
    public static string XApiCreditsDepleted => Ru(
        "У X-приложения кончились API-кредиты — пополните pay-per-use баланс в X Developer Portal (это не кредиты Cedar Clerk).",
        "The X app is out of API credits — top up pay-per-use billing in the X Developer Portal (this is not your Cedar Clerk credits).");
    public static string BlueskyReconnect => Ru(
        "Не удалось войти в Bluesky — переподключите аккаунт в настройках.",
        "Could not sign in to Bluesky — reconnect the account in settings.");
    public static string DiscordWebhookRequired => Ru(
        "Вставьте URL вебхука Discord — его выдаёт настройка канала: Integrations → Webhooks.",
        "Paste a Discord webhook URL — the channel's settings issue one under Integrations → Webhooks.");
    public static string DiscordWebhookInvalid => Ru(
        "Discord не принял этот вебхук — проверьте URL (он должен начинаться с https://discord.com/api/webhooks/).",
        "Discord refused this webhook — check the URL (it must start with https://discord.com/api/webhooks/).");
    public static string WaitlistEmailInvalid => Ru(
        "Это не похоже на адрес почты.",
        "That does not look like an email address.");
    public static string LandingImageUnsupported(string contentType) => Ru(
        $"Такой формат сюда не годится: {contentType}. Скриншот — это PNG, JPEG или WebP.",
        $"That format does not belong here: {contentType}. A screenshot is PNG, JPEG or WebP.");
    public static string LandingImageTooLarge(long maxMb) => Ru(
        $"Файл слишком большой — не больше {maxMb} МБ.",
        $"The file is too large — {maxMb}MB at most.");
    public static string LandingBadFileName => Ru(
        "Такого файла здесь нет.",
        "There is no such file here.");
    public static string ShowcaseSlugEmpty => Ru(
        "Слаг получился пустым — используйте латинские буквы или цифры.",
        "The slug came out empty — use latin letters or digits.");
    public static string UsernameRequired => Ru(
        "Выберите имя — оно станет адресом вашего блога.",
        "Pick a name — it becomes the address of your blog.");
    public static string UsernameInvalid => Ru(
        "Имя может состоять только из латинских букв, цифр и дефисов внутри — до 63 символов, и некоторые имена зарезервированы.",
        "A name may hold only latin letters, digits and inner hyphens — up to 63 characters, and some names are reserved.");
    public static string UsernameTaken(string username) => Ru(
        $"«{username}» уже занято — выберите другое имя.",
        $"'{username}' is already taken — pick another name.");
    public static string BuildDownloadUrlInvalid => Ru(
        "Ссылка на скачивание должна начинаться с http:// или https://.",
        "A download link has to start with http:// or https://.");
    public static string BuildPublicNeedsUrl => Ru(
        "Чтобы выложить сборку, нужна ссылка на скачивание.",
        "Offering a build for download needs a link to it.");
    public static string ShowcaseGalleryTooLong(int maxChars) => Ru(
        $"Список картинок слишком длинный — не больше {maxChars} символов.",
        $"The image list is too long — {maxChars} characters at most.");
    public static string ShowcaseTrailerNotYouTube => Ru(
        "Это не ссылка на видео YouTube.",
        "That is not a link to a YouTube video.");
    public static string ShowcaseDomainInvalid => Ru(
        "Это не похоже на доменное имя — например, mygame.com.",
        "That does not look like a domain name — mygame.com, for example.");
    public static string ShowcaseDomainIsOurs => Ru(
        "Этот домен и так наш — свой домен нужен другой.",
        "That domain is already ours — a domain of your own is a different one.");
    public static string ShowcaseDomainTaken(string domain) => Ru(
        $"«{domain}» уже занят другим проектом.",
        $"'{domain}' is already claimed by another project.");
    public static string ShowcaseSlugTaken(string slug) => Ru(
        $"«{slug}» уже занят — выберите другой слаг.",
        $"'{slug}' is already taken — pick another slug.");
    public static string DiscordReconnect => Ru(
        "Вебхук Discord больше не работает — вероятно, удалён в настройках канала. Переподключите его в настройках.",
        "The Discord webhook no longer works — likely deleted in the channel's settings. Reconnect it in settings.");
    public static string LinkYouTelegram => Ru("Сначала привяжите аккаунт Telegram.", "Link your Telegram account first.");
    public static string TelegramBillingNotConfigured =>
        Ru("Оплата через Telegram Stars не настроена!", "Telegram Stars billing is not configured!");
    public static string PaypalNotConfigured => Ru("PayPal ещё не подключён.", "PayPal is not wired up yet.");
    public static string AutoTranslateProPlus =>
        Ru("Автоперевод доступен с тарифа Pro и оплачивается кредитами.",
           "Auto-translate needs Pro and is paid in credits.");
    public static string NotEnoughCreditsForAi =>
        Ru("Не хватает кредитов для ИИ-вызова — пополните баланс в Настройках → Оплата.",
           "Not enough credits for this AI call — top up in Settings → Billing.");
    public static string LanguageRequiresPro =>
        Ru("Этот язык доступен на Pro — Free ограничен английским и японским.",
           "This language is a Pro feature — Free is limited to English and Japanese.");
    public static string AutoTranslateNoProvider =>
        Ru("Настроенный провайдер не умеет автоперевод.", "Auto-translate is not available with the configured provider");

    public static string CreateLanguageBeforePrimary =>
        Ru("Сначала создайте версию на этом языке, потом делайте её основной.",
           "Create this language version before making it primary.");

    // ADR-065 — the publish guard's answer to "you confirmed a diff of something else".
    public static string PublishConfirmationStale =>
        Ru("Пост изменился после того, как обновление было показано — посмотрите изменения и подтвердите заново.",
           "This post changed after the update was previewed — review the changes and confirm again.");

    // T-018.1 / T-018.3 — the two ways a save is refused rather than silently applied.
    public static string SaveShrinkNeedsConfirmation =>
        Ru("Это сохранение удалит почти весь текст — подтвердите, что так и задумано.",
           "This save would delete most of the text — confirm that it's intentional.");
    public static string SaveConflict =>
        Ru("Эту версию изменили в другом месте после того, как вы её открыли — перезагрузите перед сохранением.",
           "This version was edited elsewhere after you loaded it — reload before saving.");

    // T-013 — named provider and language, because the fix is switching one or picking the other.
    public static string LanguageNotSupportedByProvider(string lang, string provider) =>
        Ru($"Настроенный провайдер перевода ({provider}) не умеет переводить на {lang.ToUpperInvariant()}.",
           $"The configured translation provider ({provider}) cannot translate into {lang.ToUpperInvariant()}.");

    public static string AiDailyLimitReached(int limit) =>
        Ru($"Дневной лимит AI ({limit} вызовов) исчерпан — обнулится в полночь UTC.",
           $"Daily AI limit ({limit} calls) reached — resets at midnight UTC.");

    public static string LanguageIsPrimary(string lang) =>
        Ru($"{lang.ToUpperInvariant()} — основной язык этого черновика, правьте его на главной вкладке.",
           $"{lang.ToUpperInvariant()} is this draft's primary language — edit it on the main tab.");

    public static string NoVersionInLanguage(string lang) =>
        Ru($"Версии {lang.ToUpperInvariant()} у этого черновика нет",
           $"No {lang.ToUpperInvariant()} version of this draft");

    // Indie-gamedev module (T-120, ADR-102/103). Interpolated messages live here for the same
    // reason the plain ones do — the test above only catches `error = "..."`, so an interpolated
    // literal would have slipped through the guard while still answering in English regardless of
    // who is reading.
    public static string DocumentTypeNotPublishable =>
        Ru("Этот документ — рабочий материал, а не пост: опубликовать его нельзя. Смените тип документа, если хотели именно опубликовать.",
           "This document is working material, not a post, so it cannot be published. Change its type if publishing is what you meant.");

    public static string DocumentTypeBlogPublished =>
        Ru("Пост опубликован в блоге. Снимите публикацию, прежде чем менять тип на рабочий материал.",
           "This post is published on the blog. Unpublish it before changing its type to working material.");

    public static string ProjectNeedsOneDocument =>
        Ru("В проекте должен остаться хотя бы один документ. Удалите проект целиком или сначала добавьте другой документ.",
           "A project must keep at least one document. Delete the project instead, or add another document first.");

    // Admin credit adjustments (11.08.2026).
    public static string CreditAmountRequired =>
        Ru("Укажите, сколько кредитов начислить или списать — ноль ничего не меняет.",
           "Say how many credits to add or take back — zero changes nothing.");

    public static string CreditsWouldGoNegative(int balance) =>
        Ru($"На балансе {balance}; списать больше нельзя — отрицательный баланс приложение читать не умеет.",
           $"The balance is {balance}; taking more would go negative, which nothing in the app can read.");

    // T-122 — the asset index. Pushed up from the desktop agent since ADR-117, so these are refusals
    // aimed at a client that is uploading rather than at a server that is walking.
    public static string AssetFolderRequired =>
        Ru("Сначала выберите папку для индексации.", "Choose a folder to index first.");

    public static string AssetFolderNotFound(string path) =>
        Ru($"Папки «{path}» нет — проверьте путь или подключите диск.",
           $"There is no folder at '{path}' — check the path, or plug the drive back in.");

    public static string AssetMachineRequired =>
        Ru("Не указано, на какой машине лежит папка — без этого нельзя отличить файл от его отпечатка.",
           "The machine holding the folder was not named — without it, a file cannot be told from its fingerprint.");

    public static string AssetScanStampRequired =>
        Ru("Не указано время начала сканирования — без него нельзя понять, какие файлы обход не нашёл.",
           "The scan's start time is missing — without it, there is no way to tell which files the walk did not find.");

    public static string AssetBatchTooLarge(int max) =>
        Ru($"За один раз принимается не больше {max} файлов.", $"At most {max} files are accepted at once.");

    public static string AssetIndexFull(int max) =>
        Ru($"В индексе проекта не может быть больше {max} файлов. Выберите папку поуже — например, только Assets, без сборок и кэшей.",
           $"A project's index holds at most {max} files. Choose a narrower folder — Assets alone, without builds and caches.");

    public static string AssetPathRejected(string path) =>
        Ru($"Путь «{path}» отклонён: ожидается путь внутри выбранной папки.",
           $"The path '{path}' was rejected: a path inside the chosen folder was expected.");

    public static string AssetThumbFormRequired =>
        Ru("Превью загружаются формой с файлами.", "Previews are uploaded as a file form.");

    public static string AssetThumbBatchTooLarge(int max) =>
        Ru($"За один раз принимается не больше {max} превью.", $"At most {max} previews are accepted at once.");

    public static string AssetThumbBudgetExhausted(long budget) =>
        Ru($"Место под превью исчерпано — предел {budget / (1024 * 1024)} МБ. Индекс работает, новые превью не сохраняются.",
           $"The preview allowance is used up — the limit is {budget / (1024 * 1024)} MB. The index still works; new previews are not being stored.");

    public static string UnknownProjectType(string type) =>
        Ru($"Неизвестный тип проекта «{type}».", $"Unknown project type '{type}'.");

    public static string UnknownDocumentType(string type) =>
        Ru($"Неизвестный тип документа «{type}».", $"Unknown document type '{type}'.");

    public static string ProjectNameLength(int max) =>
        Ru($"Имя проекта — от 1 до {max} символов.", $"Project name must be 1-{max} characters");

    public static string ProjectDescriptionLength(int max) =>
        Ru($"Описание проекта — не длиннее {max} символов.", $"Project description must be at most {max} characters");

    // T-123 — the task tracker.
    public static string TaskTitleLength(int max) =>
        Ru($"Название задачи — от 1 до {max} символов.", $"Task title must be 1-{max} characters");

    public static string TaskDescriptionLength(int max) =>
        Ru($"Описание задачи — не длиннее {max} символов. Задаче, которой нужно больше, стоит быть документом.",
           $"Task description must be at most {max} characters. A task that needs more is really a document.");

    public static string TaskAssigneeLength(int max) =>
        Ru($"Исполнитель — не длиннее {max} символов.", $"Assignee must be at most {max} characters");

    public static string UnknownTaskStatus(string status) =>
        Ru($"Неизвестный статус задачи «{status}».", $"Unknown task status '{status}'.");

    public static string UnknownTaskPriority(int priority) =>
        Ru($"Приоритет — 1, 2 или 3; получено {priority}.", $"Priority is 1, 2 or 3 — got {priority}.");

    public static string UnknownLinkTarget(string type) =>
        Ru($"Неизвестный тип связи «{type}».", $"Unknown link target '{type}'.");

    public static string TaskCannotLinkToItself =>
        Ru("Задачу нельзя связать с ней же самой.", "A task cannot be linked to itself.");

    // T-124 — sprints.
    public static string SprintNameLength(int max) =>
        Ru($"Имя спринта — от 1 до {max} символов.", $"Sprint name must be 1-{max} characters");

    public static string SprintEndsBeforeItStarts =>
        Ru("Спринт не может закончиться раньше, чем начался.", "A sprint cannot end before it starts.");

    public static string UnknownSprint =>
        Ru("Такого спринта нет — возможно, он удалён.", "There is no such sprint — it may have been deleted.");

    // T-126 — builds and versions.
    public static string BuildVersionLength(int max) =>
        Ru($"Версия — от 1 до {max} символов.", $"A version must be 1-{max} characters");

    public static string BuildNotesLength(int max) =>
        Ru($"Заметки к версии — не длиннее {max} символов.", $"Build notes must be at most {max} characters");

    public static string BuildVersionTaken(string version) =>
        Ru($"Версия «{version}» в этом проекте уже есть.", $"This project already has a version '{version}'.");

    public static string UnknownBuild =>
        Ru("Такой версии нет — возможно, она удалена.", "There is no such version — it may have been deleted.");

    // ADR-116 — /downloads/latest before any desktop build has been shipped, or with a manifest
    // that names no installer. One message for both: from the outside they are the same situation.
    public static string NoDesktopBuildPublished =>
        Ru("Десктопная сборка ещё не опубликована.", "No desktop build has been published yet.");

    // T-301 — project collaborators and the reference board.
    public static string UnknownProjectRole(string role) =>
        Ru($"Неизвестная роль в проекте «{role}».", $"Unknown project role '{role}'.");

    public static string CannotInviteYourself =>
        Ru("Вы и так участник этого проекта.", "You are already on this project.");

    public static string ProjectMemberAlreadyInvited =>
        Ru("Этот адрес уже приглашён в проект.", "That address is already invited to this project.");

    public static string ProjectMemberLimitReached(int max) =>
        Ru($"В проекте не больше {max} соавторов.", $"A project holds at most {max} collaborators.");

    public static string InviteNotFound =>
        Ru("Это приглашение больше не действует.", "That invitation is no longer valid.");

    public static string InviteAlreadyAccepted =>
        Ru("Этим приглашением уже воспользовался другой аккаунт.", "That invitation has already been used by another account.");

    // The one refusal in the module that is a 403 rather than a 404: a viewer is looking at the
    // thing they were just refused, so "no such project" would deny what is on their screen.
    public static string NoWriteAccessToProject =>
        Ru("Этот проект можно смотреть, но не менять.", "You can look at this project, but not change it.");

    public static string BoardNameLength(int max) =>
        Ru($"Имя доски — от 1 до {max} символов.", $"A board name must be 1-{max} characters");

    public static string UnknownCanvasBackground(string value) =>
        Ru($"Неизвестный фон доски «{value}».", $"Unknown canvas background '{value}'.");

    public static string BoardLimitReached(int max) =>
        Ru($"В проекте не больше {max} досок.", $"A project holds at most {max} boards.");

    public static string CanvasItemLimitReached(int max) =>
        Ru($"На доске не больше {max} объектов.", $"A board holds at most {max} items.");

    public static string UnknownCanvasItemKind(string kind) =>
        Ru($"Неизвестный объект доски «{kind}».", $"Unknown canvas item '{kind}'.");

    public static string CanvasPayloadTooLarge(int max) =>
        Ru($"В этом объекте больше {max} символов данных.", $"That item carries more than {max} characters of data.");

    public static string CanvasPayloadInvalid =>
        Ru("Содержимое этого объекта доска не понимает.", "That item's contents are not in a shape this board understands.");

    public static string CanvasGeometryInvalid =>
        Ru("У объекта должны быть положительные ширина и высота.", "An item needs a positive width and height.");

    public static string UnknownBoard =>
        Ru("Такой доски нет — возможно, она удалена.", "There is no such board — it may have been deleted.");

    // Wave 2 "Rhythm" — the queue, calendar, tracked-link and template answers.
    public static string QueueSlotDayInvalid =>
        Ru("День недели — от 0 (воскресенье) до 6 (суббота).", "dayOfWeek must be 0 (Sunday) to 6 (Saturday)");

    public static string QueueSlotTimeInvalid =>
        Ru("Время — от 0 до 1439 минут от полуночи UTC.", "timeUtcMinutes must be 0 to 1439");

    public static string EvergreenMaxSendsInvalid =>
        Ru("Лимит отправок — не меньше 1.", "maxSends must be at least 1");

    public static string ScheduledPostNotPending =>
        Ru("Перенести можно только ожидающий пост.", "Only a pending post can be rescheduled");

    public static string TrackedLinkUrlInvalid =>
        Ru("Ссылка должна быть абсолютной http(s).", "URL must be absolute http(s)");

    public static string UnknownTemplate =>
        Ru("Такого шаблона нет.", "There is no such template.");

    public static string FromTemplateSourceRequired =>
        Ru("Выберите стартовый шаблон или один из своих шаблонов.", "Name a starter template or one of your own templates");

    public static string CtaTooManyButtons(int max) =>
        Ru($"Не больше {max} кнопок.", $"At most {max} buttons");

    public static string CtaButtonTextLength(int max) =>
        Ru($"Текст кнопки — от 1 до {max} символов.", $"Button text must be 1 to {max} characters");

    public static string CtaButtonUrlInvalid =>
        Ru("Ссылка кнопки должна быть абсолютной http(s).", "Button URL must be absolute http(s)");

    public static string InviteLinkNameLength(int max) =>
        Ru($"Имя ссылки — от 1 до {max} символов.", $"Link name must be 1 to {max} characters");

    // Dialogue tool (Yarn) — scripts, node graphs and the xlsx translation sheet.
    public static string DialogueNameLength(int max) =>
        Ru($"Имя диалога — от 1 до {max} символов.", $"Dialogue name must be 1 to {max} characters");

    public static string DialogueGraphInvalid =>
        Ru("Граф диалога не читается — это не список узлов.", "Dialogue graph is not a readable node list");

    public static string DialogueGraphTooLarge(int maxKb) =>
        Ru($"Граф диалога больше {maxKb} КБ.", $"Dialogue graph exceeds {maxKb} KB");

    public static string DialogueNodeTitleDuplicate(string title) =>
        Ru($"Два узла называются \"{title}\" — Yarn различает узлы по имени.", $"Two nodes are titled \"{title}\" — Yarn addresses nodes by title");

    public static string DialogueXlsxUnreadable =>
        Ru("Файл не читается как xlsx-таблица.", "The file is not a readable xlsx workbook");

    public static string DialogueXlsxNoIdColumn =>
        Ru("В таблице нет колонки Id — экспортируйте лист из этого же диалога.", "The sheet has no Id column — export the sheet from this dialogue first");

    // Russian is the only translated locale for now, matching the app's own UI dictionaries
    // (en.ts/ru.ts): every other UI language already falls back to English there, and shipping
    // machine-quality German error text would be a worse answer than the English original.
    private static string Ru(string russian, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? russian : english;
}
