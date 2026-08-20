import { Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';
const STORAGE_KEY = 'cedar-theme';

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
        localStorage.setItem(STORAGE_KEY, theme);
        this.apply(theme);
    }

    private apply(theme: Theme) {
        document.documentElement.dataset['theme'] = theme;
    }

    private loadInitial(): Theme {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === 'light' || stored === 'dark') return stored;
        return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
}
