import { Injectable, computed, signal } from '@angular/core';
import { Dict, en } from './en';
import { ru } from './ru';

export type UiLang = 'en' | 'ru';

// Cache only — the profile (ApplicationUser.UiLanguage) is the source of truth. Without it every
// load would paint English until /api/auth/me resolves. See ADR-044.
const STORAGE_KEY = 'cedar-ui-lang';

const DICTS: Record<UiLang, Dict> = { en, ru };

// Deliberately NOT called `lang`: the editor's `lang()` signal means the *content* language of a
// post (the RU/EN tabs), which is a different axis entirely.
@Injectable({ providedIn: 'root' })
export class LocaleService {
    readonly uiLang = signal<UiLang>(this.loadInitial());
    readonly t = computed<Dict>(() => DICTS[this.uiLang()]);

    constructor() {
        this.apply(this.uiLang());
    }

    // Called with the value from the profile once /api/auth/me has resolved. Null means the user
    // never picked one, so whatever the browser suggested stays.
    adoptProfileLanguage(uiLanguage: string | null) {
        if (uiLanguage === 'en' || uiLanguage === 'ru') this.set(uiLanguage);
    }

    set(lang: UiLang) {
        this.uiLang.set(lang);
        localStorage.setItem(STORAGE_KEY, lang);
        this.apply(lang);
    }

    private apply(lang: UiLang) {
        document.documentElement.lang = lang;
    }

    private loadInitial(): UiLang {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === 'en' || stored === 'ru') return stored;
        return navigator.language?.toLowerCase().startsWith('ru') ? 'ru' : 'en';
    }
}

// Interpolation for the handful of strings that need it: fmt(t().drafts.count, { n: 3 }).
export function fmt(template: string, params: Record<string, string | number>): string {
    return template.replace(/\{(\w+)\}/g, (whole, key) => String(params[key] ?? whole));
}
