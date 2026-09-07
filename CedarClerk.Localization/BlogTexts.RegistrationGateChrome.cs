namespace CedarClerk.Localization;

public static partial class BlogTexts
{
    public sealed record RegistrationGateChrome(
        string Heading, string Blurb, string Submit,
        string NamePlaceholder, string NickPlaceholder, string EmailPlaceholder,
        string SocialPlaceholder, string ChoosePlaceholder, string AgreeLabel);

    // English is the fallback for any code not listed, which is also what the app's own UI
    // locales do (ADR-050) — an untranslated gate in English beats one in a language the reader
    // definitely didn't ask for.
    public static readonly IReadOnlyDictionary<string, RegistrationGateChrome> GateChrome =
        new Dictionary<string, RegistrationGateChrome>
        {
            ["ru"] = new("Это приватный пост", "Заполните форму ниже, чтобы получить доступ.", "Получить доступ",
                "Имя и фамилия", "Никнейм", "Почта", "Ссылка на соцсеть", "Выберите…", "Я согласен/-на"),
            ["en"] = new("This post is private", "Fill in the form below to get access.", "Get access",
                "First and last name", "Nickname", "Email", "A social link", "Choose…", "I agree"),
            ["de"] = new("Dieser Beitrag ist privat", "Füllen Sie das Formular aus, um Zugang zu erhalten.", "Zugang erhalten",
                "Vor- und Nachname", "Spitzname", "E-Mail", "Ein Social-Media-Link", "Auswählen…", "Ich stimme zu"),
            ["fr"] = new("Cet article est privé", "Remplissez le formulaire ci-dessous pour obtenir l'accès.", "Obtenir l'accès",
                "Nom et prénom", "Pseudo", "E-mail", "Un lien vers un réseau social", "Choisir…", "J'accepte"),
            ["es"] = new("Esta publicación es privada", "Rellena el formulario para obtener acceso.", "Obtener acceso",
                "Nombre y apellidos", "Apodo", "Correo electrónico", "Un enlace a una red social", "Elegir…", "Estoy de acuerdo"),
            ["ja"] = new("この投稿は非公開です", "アクセスするには以下のフォームにご記入ください。", "アクセスする",
                "氏名", "ニックネーム", "メールアドレス", "SNSのリンク", "選択してください…", "同意します"),
            // T-013 — these three have not been read by a native speaker; they are here because an
            // English gate on a Ukrainian post is a worse default, not because they are polished.
            ["uk"] = new("Цей допис приватний", "Заповніть форму нижче, щоб отримати доступ.", "Отримати доступ",
                "Ім'я та прізвище", "Нікнейм", "Пошта", "Посилання на соцмережу", "Оберіть…", "Я погоджуюсь"),
            ["be"] = new("Гэты пост прыватны", "Запоўніце форму ніжэй, каб атрымаць доступ.", "Атрымаць доступ",
                "Імя і прозвішча", "Нік", "Пошта", "Спасылка на сацсетку", "Абярыце…", "Я згодны/-ая"),
            ["ka"] = new("ეს პოსტი პირადია", "წვდომის მისაღებად შეავსეთ ქვემოთ მოცემული ფორმა.", "წვდომის მიღება",
                "სახელი და გვარი", "მეტსახელი", "ელფოსტა", "სოციალური ქსელის ბმული", "აირჩიეთ…", "ვეთანხმები"),
        };

}
