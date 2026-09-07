import catalog from '../languages.json';
import type { Dict } from './en';

export const DICTIONARY_LOADERS = {
    ru: () => import('./ru').then(module => module.ru),
} satisfies Record<string, () => Promise<Dict>>;

export type UiLang = 'en' | keyof typeof DICTIONARY_LOADERS;

export function isInterfaceLanguage(language: string | null): language is UiLang {
    return language !== null && catalog.interfaceDictionaries.includes(language)
        && (language === 'en' || Object.hasOwn(DICTIONARY_LOADERS, language));
}

export function initialInterfaceLanguage(stored: string | null, browserLanguage: string): UiLang {
    if (isInterfaceLanguage(stored)) return stored;
    return browserLanguage.toLowerCase().startsWith('ru') ? 'ru' : 'en';
}

export const INTERFACE_LANGUAGE_OPTIONS: { lang: UiLang; code: string; label: string }[] =
    catalog.languages.flatMap(language => isInterfaceLanguage(language.code)
        ? [{ lang: language.code, code: language.code.toUpperCase(), label: language.endonym }]
        : []);
