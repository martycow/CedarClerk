import { Dict } from './en';

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
    settings: {
        language: {
            nav: 'Язык',
            title: 'Язык интерфейса',
            hint: 'Относится к интерфейсу приложения, а не к языку, на котором написаны посты. Сохраняется в аккаунте, поэтому переносится на все устройства.',
            failed: 'Не удалось сохранить язык',
        },
    },
};
