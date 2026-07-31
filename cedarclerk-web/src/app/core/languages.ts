// Content languages a post can exist in (NF2). Mirrors CedarClerk.Localization.Languages — the
// server validates against its own copy, this one only drives the editor's tabs and labels.
export const CONTENT_LANGUAGES = ['ru', 'en', 'de', 'fr', 'es', 'ja', 'uk', 'be', 'ka'];

// The language a *new* draft starts in. Which language an existing draft is written in is
// `Draft.primaryLanguage` and is per-draft (ADR-064) — never assume this constant for one that
// already exists, which is exactly the mistake that left the diff gutter and several exports
// Russian-only. There is deliberately no "translation languages" list any more: whether a language
// is a translation depends on the draft (ADR-065).
export const DEFAULT_PRIMARY_LANGUAGE = 'ru';

// Endonyms — a language name is only useful to someone who reads it, so these are never
// translated. Shown next to the two-letter tab codes (DB3.1: flag emoji don't render on Windows).
export const LANGUAGE_ENDONYMS: Record<string, string> = {
    ru: 'Русский',
    en: 'English',
    de: 'Deutsch',
    fr: 'Français',
    es: 'Español',
    ja: '日本語',
    uk: 'Українська',
    be: 'Беларуская',
    ka: 'ქართული',
};

export function endonymOf(code: string): string {
    return LANGUAGE_ENDONYMS[code] ?? code.toUpperCase();
}
