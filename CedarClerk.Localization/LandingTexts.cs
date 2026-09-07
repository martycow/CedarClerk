namespace CedarClerk.Localization;

public static partial class LandingTexts
{
    public static readonly LandingText Kicker = new("publishing for independent makers", "публикация для независимых авторов");

    public static readonly LandingText HeroTitle = new(
        "Build in public.<br>Keep your own home.<br>Get discovered.",
        "Делайте открыто.<br>Храните у себя.<br>Находите читателей.");

    public static readonly LandingText HeroSub = new(
        "Write a personal blog or connect every devlog to a project. Publish to your own site, "
        + $"Telegram, X, Bluesky and Discord in {Languages.ContentLanguages.Count} languages — and join Discovery when you choose.",
        "Ведите личный блог или связывайте каждый девлог с проектом. Публикуйте на своём сайте, "
        + $"в Telegram, X, Bluesky и Discord на {Languages.ContentLanguages.Count} языках — и включайте Discovery, когда решите.");
}
