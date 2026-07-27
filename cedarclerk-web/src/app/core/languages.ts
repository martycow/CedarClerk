// Content languages a post can exist in (NF2). Mirrors CedarClerk.Localization.Languages — the
// server validates against its own copy, this one only drives the editor's tabs and labels.
export const PRIMARY_LANGUAGE = 'ru';

export const TRANSLATION_LANGUAGES = ['en', 'de', 'fr', 'es', 'ja'] as const;

export const CONTENT_LANGUAGES = [PRIMARY_LANGUAGE, ...TRANSLATION_LANGUAGES];

// Endonyms — a language name is only useful to someone who reads it, so these are never
// translated. Shown next to the two-letter tab codes (DB3.1: flag emoji don't render on Windows).
export const LANGUAGE_ENDONYMS: Record<string, string> = {
    ru: 'Русский',
    en: 'English',
    de: 'Deutsch',
    fr: 'Français',
    es: 'Español',
    ja: '日本語',
};

export function endonymOf(code: string): string {
    return LANGUAGE_ENDONYMS[code] ?? code.toUpperCase();
}
