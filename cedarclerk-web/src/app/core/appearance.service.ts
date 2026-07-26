import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';

export interface AppearancePrefs {
    accentLight: string;
    accentDark: string;
    sheetWidth: 'narrow' | 'normal' | 'wide' | 'full';
    typeface: 'system' | 'serif' | 'mono';
    fontSize: number; // px, sheet base (before zoom)
    lineHeight: number;
    showRuler: boolean;
    showParagraphNumbers: boolean;
    showWordCount: boolean;
    focusModeHideToolbar: boolean;
    sheetFlush: boolean; // no paper card — sheet merges with the canvas
}

export const DEFAULT_APPEARANCE: AppearancePrefs = {
    accentLight: '#5B6E46',
    accentDark: '#5B6E46',
    sheetWidth: 'normal',
    typeface: 'system',
    fontSize: 16,
    lineHeight: 1.6,
    showRuler: false,
    showParagraphNumbers: false,
    showWordCount: true,
    focusModeHideToolbar: false,
    sheetFlush: false,
};

export const ACCENT_PRESETS: { name: string; hex: string }[] = [
    { name: 'Cedar', hex: '#5B6E46' },
    { name: 'Bark', hex: '#8A6A3E' },
    { name: 'Slate', hex: '#4A5A6B' },
    { name: 'Ink', hex: '#3A3730' },
    { name: 'Rust', hex: '#A0522D' },
];

export const SHEET_WIDTH_PX: Record<AppearancePrefs['sheetWidth'], number> = {
    narrow: 560, normal: 680, wide: 820, full: 1040,
};

export const TYPEFACE_STACK: Record<AppearancePrefs['typeface'], string> = {
    system: 'var(--font-sans)',
    serif: 'Georgia, "Iowan Old Style", serif',
    mono: 'var(--font-mono)',
};

// Personal editor preferences (ADR-035) — deliberately scoped to the authoring app only, never
// applied to the public blog (which keeps its own fixed branding). Only the accent is genuinely
// global chrome (topbar/toolbar/buttons everywhere); the writing-sheet prefs (width/typeface/
// font-size/etc.) are read directly by EditorComponent since they only affect its own template.
@Injectable({ providedIn: 'root' })
export class AppearanceService {
    private auth = inject(AuthService);
    readonly prefs = signal<AppearancePrefs>(DEFAULT_APPEARANCE);

    // Idempotent — safe to call on every authGuard pass, not just the first one.
    loadFromAuth() {
        let parsed: Partial<AppearancePrefs> = {};
        try {
            parsed = JSON.parse(this.auth.appearancePrefsJson() ?? '{}');
        } catch {
            // Corrupt or foreign blob — fall back to defaults rather than fail navigation.
        }
        const merged = { ...DEFAULT_APPEARANCE, ...parsed };
        this.prefs.set(merged);
        this.applyAccent(merged);
    }

    async save(patch: Partial<AppearancePrefs>): Promise<void> {
        const merged = { ...this.prefs(), ...patch };
        this.prefs.set(merged);
        this.applyAccent(merged);
        await this.auth.saveAppearancePrefs(JSON.stringify(merged));
    }

    private applyAccent(p: AppearancePrefs) {
        let el = document.getElementById('__appearance-accent') as HTMLStyleElement | null;
        if (!el) {
            el = document.createElement('style');
            el.id = '__appearance-accent';
            document.head.appendChild(el);
        }
        // Same dark-mix formula already used for the shipped Cedar accent (styles.scss) — keeps
        // every preset legible on bark instead of re-deriving a new ratio per preset.
        el.textContent =
            `:root{--accent:${p.accentLight}}` +
            `:root[data-theme="dark"]{--accent:color-mix(in srgb, ${p.accentDark} 55%, #E8F0E8 45%)}`;
    }
}
