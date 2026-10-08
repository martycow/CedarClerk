import { Injectable, computed, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark';
/** ADR-321: what the person chose; `system` follows the OS and keeps following it. */
export type ThemeMode = Theme | 'system';
export const THEME_STORAGE_KEY = 'cedar-theme';

export function isThemeMode(value: unknown): value is ThemeMode {
    return value === 'light' || value === 'dark' || value === 'system';
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
    readonly mode = signal<ThemeMode>(this.loadInitial());
    private readonly systemDark = signal(this.query()?.matches ?? false);
    /** The theme actually painted. */
    readonly theme = computed<Theme>(() => {
        const mode = this.mode();
        return mode === 'system' ? (this.systemDark() ? 'dark' : 'light') : mode;
    });

    constructor() {
        this.query()?.addEventListener?.('change', e => this.systemDark.set(e.matches));
        this.apply(this.theme());
        effect(() => this.apply(this.theme()));
    }

    toggle() {
        this.set(this.theme() === 'dark' ? 'light' : 'dark');
    }

    set(mode: ThemeMode) {
        this.mode.set(mode);
        try {
            localStorage.setItem(THEME_STORAGE_KEY, mode);
        } catch {
            // A private or locked-down browser can deny storage; the live theme still applies.
        }
        this.apply(this.theme());
    }

    private apply(theme: Theme) {
        document.documentElement.dataset['theme'] = theme;
    }

    private query(): MediaQueryList | undefined {
        return window.matchMedia?.('(prefers-color-scheme: dark)');
    }

    private loadInitial(): ThemeMode {
        try {
            const stored = localStorage.getItem(THEME_STORAGE_KEY);
            if (isThemeMode(stored)) return stored;
        } catch {
            // Following the OS is the signed-out fallback when storage is unavailable.
        }
        return 'system';
    }
}
