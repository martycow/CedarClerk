import { Injectable, computed, signal } from '@angular/core';
import { Dict, en } from './en';
import { ru } from './ru';
import { pseudoDict } from './pseudo';

export type UiLang = 'en' | 'ru';

// Cache only — the profile (ApplicationUser.UiLanguage) is the source of truth. Without it every
// load would paint English until /api/auth/me resolves. See ADR-044.
const STORAGE_KEY = 'cedar-ui-lang';

const DICTS: Record<UiLang, Dict> = { en, ru };

// T-051 — the pseudo-locale switch. A development flag, not a language: it never appears in the
// picker and never reaches the profile, so an account cannot end up stuck in it. `?pseudo=1` in
// the URL turns it on (and `?pseudo=0` off) so a screenshot run can ask for it without a click.
const PSEUDO_KEY = 'cedar-pseudo';

// Deliberately NOT called `lang`: the editor's `lang()` signal means the *content* language of a
// post (the RU/EN tabs), which is a different axis entirely.
@Injectable({ providedIn: 'root' })
export class LocaleService {
    readonly uiLang = signal<UiLang>(this.loadInitial());
    readonly pseudo = signal<boolean>(this.loadPseudo());
    // Built once per (language, flag) pair rather than per read: `t()` is called in template
    // expressions, which run on every change-detection pass.
    private readonly pseudoDicts = new Map<UiLang, Dict>();
    readonly t = computed<Dict>(() => {
        const lang = this.uiLang();
        if (!this.pseudo()) return DICTS[lang];
        if (!this.pseudoDicts.has(lang)) this.pseudoDicts.set(lang, pseudoDict(DICTS[lang]));
        return this.pseudoDicts.get(lang)!;
    });

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

    setPseudo(on: boolean) {
        this.pseudo.set(on);
        localStorage.setItem(PSEUDO_KEY, on ? '1' : '0');
    }

    private loadPseudo(): boolean {
        const param = new URLSearchParams(location.search).get('pseudo');
        if (param === '1' || param === '0') {
            localStorage.setItem(PSEUDO_KEY, param);
            return param === '1';
        }
        return localStorage.getItem(PSEUDO_KEY) === '1';
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
