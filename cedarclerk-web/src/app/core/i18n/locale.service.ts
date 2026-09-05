import { Injectable, computed, signal } from '@angular/core';
import { Dict, en } from './en';
import { pseudoDict } from './pseudo';

export type UiLang = 'en' | 'ru';

// Cache only — the profile (ApplicationUser.UiLanguage) is the source of truth. Without it every
// load would paint English until /api/auth/me resolves. See ADR-044.
const STORAGE_KEY = 'cedar-ui-lang';

// English ships in the initial bundle: it defines Dict's shape and stands in while another
// dictionary is still on its way. Every other language is its own lazy chunk (ADR-263), and the
// app initializer holds the first paint until the active one has arrived.
const LOADERS: Record<Exclude<UiLang, 'en'>, () => Promise<Dict>> = {
    ru: () => import('./ru').then(m => m.ru),
};

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
    private readonly dicts = signal<Partial<Record<UiLang, Dict>>>({ en });
    private readonly pending = new Map<UiLang, Promise<Dict>>();
    // Built once per (dictionary, flag) pair rather than per read: `t()` is called in template
    // expressions, which run on every change-detection pass.
    private readonly pseudoDicts = new WeakMap<Dict, Dict>();
    readonly t = computed<Dict>(() => {
        const dict = this.dicts()[this.uiLang()] ?? en;
        if (!this.pseudo()) return dict;
        if (!this.pseudoDicts.has(dict)) this.pseudoDicts.set(dict, pseudoDict(dict));
        return this.pseudoDicts.get(dict)!;
    });

    constructor() {
        this.apply(this.uiLang());
        void this.load(this.uiLang());
    }

    // Resolves once the active language's dictionary is in memory. The app boots behind it, so
    // `t()` never paints a fallback on first render.
    ready(): Promise<void> {
        return this.load(this.uiLang()).then(() => undefined);
    }

    // Fetches every other dictionary so a later switch finds it in memory and is instant, the way
    // route chunks are preloaded once the first screen has rendered.
    preloadAll(): Promise<void> {
        const others = Object.keys(LOADERS) as UiLang[];
        return Promise.all(others.map(lang => this.load(lang))).then(() => undefined);
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
        void this.load(lang);
    }

    private load(lang: UiLang): Promise<Dict> {
        const have = this.dicts()[lang];
        if (have) return Promise.resolve(have);
        let promise = this.pending.get(lang);
        if (!promise) {
            promise = LOADERS[lang as keyof typeof LOADERS]()
                .then(dict => {
                    this.dicts.update(current => ({ ...current, [lang]: dict }));
                    return dict;
                })
                // A failed chunk leaves English on screen and nothing cached, so the next set()
                // asks again instead of remembering the failure.
                .catch((err: unknown) => {
                    console.warn(`i18n: dictionary "${lang}" failed to load`, err);
                    return en;
                })
                .finally(() => this.pending.delete(lang));
            this.pending.set(lang, promise);
        }
        return promise;
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
