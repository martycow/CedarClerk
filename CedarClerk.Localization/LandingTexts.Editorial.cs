namespace CedarClerk.Localization;

public static partial class LandingTexts
{
    public static readonly (string Key, LandingText Label, LandingText Default)[] EditorialFields =
    [
        ("workflowTitle", new("Workflow accessible label", "Название раздела для скринридера"), new("From a draft to your audience", "От черновика к читателям")),
        ("writeTitle", new("Step 1: title", "Шаг 1: заголовок"), new("Write", "Напишите")),
        ("writeBody", new("Step 1: description", "Шаг 1: описание"), new("Capture your thoughts, progress, and ideas in a clean editor.", "Соберите мысли, идеи и новости в удобном редакторе.")),
        ("channelsTitle", new("Step 2: title", "Шаг 2: заголовок"), new("Choose channels", "Выберите каналы")),
        ("channelsBody", new("Step 2: description", "Шаг 2: описание"), new("Publish to your blog and connected channels.", "Публикуйте в своём блоге и подключённых каналах.")),
        ("publishTitle", new("Step 3: title", "Шаг 3: заголовок"), new("Publish", "Опубликуйте")),
        ("publishBody", new("Step 3: description", "Шаг 3: описание"), new("Share with one click, or on your own schedule.", "Поделитесь сразу или выберите время публикации.")),
        ("examplesTitle", new("Examples: heading", "Примеры: заголовок"), new("A blog on its own. A project when you need one.", "Блог сам по себе. Проект, когда он нужен.")),
        ("examplesBody", new("Examples: description", "Примеры: описание"), new("Start with a personal blog, or keep your documents, media, and publishing destinations together in a project.", "Начните с личного блога или объедините документы, медиа и каналы публикации внутри проекта.")),
        ("examplesLink", new("Showcase link label", "Текст ссылки на блог"), new("Explore a live blog", "Посмотреть настоящий блог")),
        ("pricingTitle", new("Pricing: heading", "Тарифы: заголовок"), new("Simple, transparent pricing.", "Простые, понятные тарифы.")),
        ("pricingBody", new("Pricing: introduction", "Тарифы: описание"), new("All plans include a personal blog. Choose the right space to grow.", "Личный блог есть в каждом тарифе. Выберите пространство для роста.")),
        ("faqTitle", new("FAQ: heading", "Вопросы: заголовок"), new("Frequently asked questions", "Частые вопросы")),
        ("faq1Question", new("FAQ 1: question", "Вопрос 1"), new("Can I use it without a public project?", "Можно вести блог без публичного проекта?")),
        ("faq1Answer", new("FAQ 1: answer", "Ответ 1"), new("Yes. Start with a personal blog. You do not need to publish a project Showcase to write and share posts.", "Да. Начните с личного блога. Чтобы писать и публиковать посты, не нужна публичная витрина проекта.")),
        ("faq2Question", new("FAQ 2: question", "Вопрос 2"), new("Can I publish to several channels?", "Можно публиковать в нескольких каналах?")),
        ("faq2Answer", new("FAQ 2: answer", "Ответ 2"), new("Choose your blog and connected channels for each post. Your plan determines how many channels you can connect.", "Выбирайте блог и подключённые каналы для каждого поста. Количество каналов зависит от тарифа.")),
        ("faq3Question", new("FAQ 3: question", "Вопрос 3"), new("When can I join?", "Когда можно присоединиться?")),
        ("faq3Answer", new("FAQ 3: answer", "Ответ 3"), new("Access opens by invitation. Leave your email and we will let you know when your invitation is ready.", "Доступ открывается по приглашению. Оставьте email, и мы напишем, когда ваше приглашение будет готово.")),
        ("closingTitle", new("Closing invitation: heading", "Приглашение внизу: заголовок"), new("Your next update deserves a home.", "У вашей следующей истории будет дом.")),
        ("closingBody", new("Closing invitation: description", "Приглашение внизу: описание"), new("Join the invite-only beta and find a home for what you are making.", "Присоединяйтесь к закрытой бете и делитесь тем, что создаёте.")),
    ];

    public static readonly LandingText HowItWorks = new("How it works", "Как это работает");
    public static readonly LandingText Examples = new("Examples", "Примеры");
    public static readonly LandingText AllTools = new("Explore all tools", "Все инструменты");
    public static readonly LandingText Menu = new("Menu", "Меню");
    public static readonly LandingText SeparateCredits = new("X publishing credits are purchased separately from your subscription.", "Кредиты для публикаций в X приобретаются отдельно от подписки.");
}
