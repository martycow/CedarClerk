import catalog from '../languages.json';

export const CONTENT_LANGUAGES = catalog.languages.map(language => language.code);
export const DEFAULT_PRIMARY_LANGUAGE = 'ru';
export const INTERFACE_DICTIONARIES = catalog.interfaceDictionaries;
export const LANGUAGE_ENDONYMS: Record<string, string> = Object.fromEntries(
    catalog.languages.map(language => [language.code, language.endonym]));

export function endonymOf(code: string): string {
    return LANGUAGE_ENDONYMS[code] ?? code.toUpperCase();
}
