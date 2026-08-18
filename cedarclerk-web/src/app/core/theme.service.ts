import { Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';
export type Skin = 'default' | 'forest';
const STORAGE_KEY = 'cedar-theme';
const SKIN_KEY = 'cedar-skin';

@Injectable({ providedIn: 'root' })
export class ThemeService {
    readonly theme = signal<Theme>(this.loadInitial());
    readonly skin = signal<Skin>(this.loadInitialSkin());

    constructor() {
        this.apply(this.theme());
        this.applySkin(this.skin());
    }

    toggle() {
        this.set(this.theme() === 'dark' ? 'light' : 'dark');
    }

    set(theme: Theme) {
        this.theme.set(theme);
        localStorage.setItem(STORAGE_KEY, theme);
        this.apply(theme);
    }

    setSkin(skin: Skin) {
        this.skin.set(skin);
        localStorage.setItem(SKIN_KEY, skin);
        this.applySkin(skin);
    }

    private apply(theme: Theme) {
        document.documentElement.dataset['theme'] = theme;
    }

    // The attribute is absent (not "default") for the default skin, so :root[data-skin="forest"]
    // simply never matches and the default skin cannot change by a pixel.
    private applySkin(skin: Skin) {
        if (skin === 'forest') document.documentElement.dataset['skin'] = skin;
        else delete document.documentElement.dataset['skin'];
    }

    private loadInitial(): Theme {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === 'light' || stored === 'dark') return stored;
        return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    private loadInitialSkin(): Skin {
        return localStorage.getItem(SKIN_KEY) === 'forest' ? 'forest' : 'default';
    }
}
