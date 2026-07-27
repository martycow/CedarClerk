import { Dict } from './en';

// Russian needs three plural forms where English needs two — hence the function-valued keys in
// en.ts: the shape stays identical, the arithmetic doesn't have to.
function plural(n: number, one: string, few: string, many: string): string {
    const mod100 = n % 100;
    if (mod100 >= 11 && mod100 <= 14) return many;
    const mod10 = n % 10;
    if (mod10 === 1) return one;
    if (mod10 >= 2 && mod10 <= 4) return few;
    return many;
}

// Russian dictionary (B26, ADR-044). Typed as Dict, so this file cannot drift out of sync with
// en.ts — a missing key fails the build.
export const ru: Dict = {
    common: {
        cancel: 'Отмена',
        delete: 'Удалить',
        save: 'Сохранить',
        saving: 'Сохранение…',
        loading: 'Загрузка…',
        close: 'Закрыть',
        toggleTheme: 'Сменить тему',
        nothingHere: 'Здесь пусто.',
    },
    login: {
        tagline: 'Пишите здесь. Публикуйте там. Му.',
        email: 'Почта',
        password: 'Пароль',
        submit: 'Войти',
        noAccount: 'Нет аккаунта?',
        register: 'Зарегистрироваться',
        inviteRequired: '· нужен инвайт',
        failed: 'Неверная почта или пароль',
    },
    register: {
        title: 'Присоединяйтесь к стаду',
        tagline: 'Cedar Clerk пока работает только по приглашениям.',
        taglineAsk: 'Попросите код у Marty.',
        email: 'Почта',
        password: 'Пароль',
        passwordPlaceholder: 'от 8 символов',
        inviteCode: 'Код приглашения',
        submit: 'Создать аккаунт',
        haveAccount: 'Уже есть аккаунт?',
        login: 'Войти',
        legalPrefix: 'Создавая аккаунт, вы соглашаетесь с',
        legalTerms: 'Условиями',
        legalAnd: 'и',
        legalPrivacy: 'Политикой конфиденциальности',
    },
    drafts: {
        crumb: 'Черновики',
        title: 'Черновики',
        postsCount: (n: number) => `${n} ${plural(n, 'пост', 'поста', 'постов')}`,
        search: 'Поиск по заголовку или тегу',
        untitled: 'Без названия',
        privatePost: 'Приватный пост',
        newDraft: 'Новый черновик',
        importCedar: 'Импорт .cedar',
        importZip: 'Импорт .zip',
        importZipTitle: 'Импорт Markdown (.zip)',
        viewTable: 'Таблица',
        viewGrid: 'Плитка',
        folders: {
            filter: 'Фильтр по папке',
            all: 'Все папки',
            none: 'Без папки',
            add: 'Добавить папку',
            newPlaceholder: 'Новая папка…',
            rename: 'Переименовать',
            deleteTitle: (name: string) => `Удалить «${name}»?`,
            deleteOrphans: (n: number) => `${n} ${plural(n, 'черновик окажется', 'черновика окажутся', 'черновиков окажутся')} без папки.`,
            deleteKeepsDrafts: 'Сами черновики не удаляются.',
            deleteConfirm: 'Удалить папку',
        },
        filters: {
            all: 'Все',
            draft: 'Черновики',
            scheduled: 'Запланированные',
            published: 'Опубликованные',
            attention: 'Требуют внимания',
            archived: 'В архиве',
        },
        columns: {
            title: 'Заголовок',
            state: 'Состояние',
            languages: 'Языки',
            folder: 'Папка',
            tags: 'Теги',
            activity: 'Активность',
            updated: 'Изменён',
        },
        resetColumns: 'Сбросить ширину колонок',
        status: {
            archived: 'В архиве',
            publishFailed: 'Публикация не удалась',
            scheduled: 'Запланирован',
            translationIncomplete: 'Перевод не обновлён',
            translationBehind: (langs: string) => `${langs} отстаёт`,
            published: 'Опубликован',
            blog: 'Блог',
            telegram: 'Telegram',
            draft: 'Черновик',
        },
        activity: {
            views: 'Просмотры блога',
            reactions: 'Реакции (лайки + дизлайки)',
        },
        actions: {
            archive: 'В архив',
            unarchive: 'Вернуть из архива',
            deleteDraft: 'Удалить',
        },
        deleteDraftTitle: 'Удалить черновик?',
        deleteDraftBody: 'Это действие необратимо.',
        errors: {
            load: 'Не удалось загрузить черновики',
            move: 'Не удалось переместить черновик',
            createFolder: 'Не удалось создать папку',
            renameFolder: 'Не удалось переименовать папку',
            deleteFolder: 'Не удалось удалить папку',
            update: 'Не удалось обновить черновик',
            delete: 'Не удалось удалить черновик',
            import: 'Импорт не удался — проверьте файл и попробуйте снова',
            importUnmatched: (n: number, names: string) => `Импортировано, но ${n} ${plural(n, 'изображение', 'изображения', 'изображений')} не удалось сопоставить: ${names}`,
        },
    },
    settings: {
        language: {
            nav: 'Язык',
            title: 'Язык интерфейса',
            hint: 'Относится к интерфейсу приложения, а не к языку, на котором написаны посты. Сохраняется в аккаунте, поэтому переносится на все устройства.',
            failed: 'Не удалось сохранить язык',
        },
    },
};
