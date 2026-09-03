import { Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';
export const THEME_STORAGE_KEY = 'cedar-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
    readonly theme = signal<Theme>(this.loadInitial());

    constructor() {
        this.apply(this.theme());
    }

    toggle() {
        this.set(this.theme() === 'dark' ? 'light' : 'dark');
    }

    set(theme: Theme) {
        this.theme.set(theme);
        try {
            localStorage.setItem(THEME_STORAGE_KEY, theme);
        } catch {
            // A private or locked-down browser can deny storage; the live theme still applies.
        }
        this.apply(theme);
    }

    private apply(theme: Theme) {
        document.documentElement.dataset['theme'] = theme;
    }

    private loadInitial(): Theme {
        try {
            const stored = localStorage.getItem(THEME_STORAGE_KEY);
            if (stored === 'light' || stored === 'dark') return stored;
        } catch {
            // The OS preference below is the signed-out fallback when storage is unavailable.
        }
        return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
}
