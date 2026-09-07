import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';
import { Theme, ThemeService } from './theme.service';

export type SidebarMode = 'full' | 'rail';
export const SIDEBAR_MODE_STORAGE_KEY = 'cedar-sidebar-mode';

export interface AppearancePrefs {
    theme: Theme;
    sidebarMode: SidebarMode;
    accentLight: string;
    accentDark: string;
    sheetWidth: 'narrow' | 'normal' | 'wide' | 'full';
    typeface: 'system' | 'serif' | 'serifClassic' | 'mono' | 'rounded' | 'departure';
    fontSize: number; // px, sheet base (before zoom)
    lineHeight: number;
    showParagraphNumbers: boolean;
    showLineRules: boolean;
    showInvisibles: boolean;
    tableRows: number;
    tableCols: number;
    showWordCount: boolean;
    focusModeHideToolbar: boolean;
    sheetFlush: boolean; // no paper card — sheet merges with the canvas
}

// check-contrast.mjs verifies each tone as text and button fill against its theme's paper (ADR-141).
export const ACCENT_PRESETS: { name: string; hex: string; night: string }[] = [
    { name: 'Cedar', hex: '#39543C', night: '#39543C' },
    { name: 'Bark', hex: '#755934', night: '#624B2C' },
    { name: 'Slate', hex: '#4A5A6B', night: '#425160' },
    { name: 'Ink', hex: '#3A3730', night: '#3A3730' },
    { name: 'Rust', hex: '#914A29', night: '#7A3F22' },
];

const BENCH_ACCENT = ACCENT_PRESETS[0];

export const HEX_COLOUR = /^#[0-9a-fA-F]{6}$/;

// Mirror --surface: validation also runs for the inactive theme, whose live token is unavailable.
export const PAPER_SURFACE: Record<Theme, string> = { light: '#F1EADA', dark: '#D9CEAE' };

// WCAG 2.2 SC 1.4.11 — the floor for a control boundary or a graphical object. The shipped
// presets are held to 4.5:1 by check-contrast.mjs; a colour the user typed is held to this.
export const ACCENT_MIN_CONTRAST = 3;

export function relativeLuminance(hex: string): number {
    const channel = (i: number) => {
        const c = parseInt(hex.slice(i, i + 2), 16) / 255;
        return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

export function contrastRatio(a: string, b: string): number {
    const la = relativeLuminance(a), lb = relativeLuminance(b);
    return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

export function accentContrast(hex: string, theme: Theme): number {
    return HEX_COLOUR.test(hex) ? contrastRatio(hex, PAPER_SURFACE[theme]) : 0;
}

// At luminance 0.179, black and white have equal contrast.
export function accentInk(hex: string): 'light' | 'dark' {
    return relativeLuminance(hex) > 0.179 ? 'dark' : 'light';
}

const presetFor = (hex: string) => ACCENT_PRESETS.find(p => p.hex.toUpperCase() === hex.toUpperCase());

export const isAccentPreset = (hex: string) => !!presetFor(hex);

// A preset's day hex identifies both tones; custom colors must pass the theme's contrast floor.
export function storedAccent(hex: string, theme: Theme): string {
    const preset = presetFor(hex);
    if (preset) return preset.hex;
    return accentContrast(hex, theme) >= ACCENT_MIN_CONTRAST ? hex.toUpperCase() : BENCH_ACCENT.hex;
}

export function resolveAccent(hex: string, theme: Theme): string {
    const key = storedAccent(hex, theme);
    const preset = presetFor(key);
    return preset ? (theme === 'dark' ? preset.night : preset.hex) : key;
}

export const DEFAULT_APPEARANCE: AppearancePrefs = {
    theme: 'light',
    sidebarMode: 'full',
    accentLight: BENCH_ACCENT.hex,
    accentDark: BENCH_ACCENT.hex,
    sheetWidth: 'normal',
    typeface: 'system',
    fontSize: 17,
    lineHeight: 1.75,
    showParagraphNumbers: false,
    showLineRules: false,
    showInvisibles: false,
    tableRows: 3,
    tableCols: 3,
    showWordCount: true,
    focusModeHideToolbar: false,
    sheetFlush: false,
};

// A table wider or taller than this stops being a table and starts being a spreadsheet — and
// Telegram's Blocks renderer has to carry every cell.
export const MAX_TABLE_SIZE = 10;

export const SHEET_WIDTH_PX: Record<AppearancePrefs['sheetWidth'], number | null> = {
    narrow: 640, normal: 760, wide: 960, full: null,
};

// The three named faces are the self-hosted ones (ADR-143); the other two stacks are what the OS
// already has, not a new loading path.
export const TYPEFACE_STACK: Record<AppearancePrefs['typeface'], string> = {
    system: 'var(--font-sans)',
    serif: 'var(--font-serif)',
    serifClassic: '"Times New Roman", Times, "Liberation Serif", serif',
    mono: 'var(--font-mono)',
    rounded: 'ui-rounded, "SF Pro Rounded", "Segoe UI Rounded", var(--font-sans)',
    departure: 'var(--font-readout)',
};

// Public blog reading controls stay reader-owned. Account appearance paints the app shell and
// editor; the settings surface debounces persistence so a slider drag is one write.
@Injectable({ providedIn: 'root' })
export class AppearanceService {
    private auth = inject(AuthService);
    private theme = inject(ThemeService);
    readonly prefs = signal<AppearancePrefs>(this.bootstrapDefaults());
    readonly dirty = signal(false);
    private loadedOwner: string | null | undefined;
    private loadedSource: string | null | undefined;
    private changeVersion = 0;
    private commitQueue: Promise<void> | null = null;

    loadFromAuth() {
        const owner = this.auth.userEmail();
        const source = this.auth.appearancePrefsJson();
        if (owner === this.loadedOwner && (this.dirty() || source === this.loadedSource)) return;

        let parsed: Partial<AppearancePrefs> = {};
        try {
            parsed = JSON.parse(source ?? '{}');
        } catch {
            // Corrupt or foreign blob — fall back to defaults rather than fail navigation.
        }
        const stored = { ...this.bootstrapDefaults(), ...parsed };
        // Snapped to a preset here rather than only at paint time, so the panel marks the swatch
        // the app is actually showing.
        const merged = {
            ...stored,
            theme: stored.theme === 'dark' ? 'dark' as const : 'light' as const,
            sidebarMode: stored.sidebarMode === 'rail' ? 'rail' as const : 'full' as const,
            accentLight: storedAccent(String(stored.accentLight), 'light'),
            accentDark: storedAccent(String(stored.accentDark), 'dark'),
        };
        this.prefs.set(merged);
        this.loadedOwner = owner;
        this.loadedSource = source;
        this.changeVersion++;
        this.dirty.set(false);
        this.applyVisuals(merged);
    }

    // Applies live (sheet + accent CSS var) without saving — the panel calls this on every
    // control interaction so the preview stays instant even though persistence no longer is.
    preview(patch: Partial<AppearancePrefs>) {
        const merged = { ...this.prefs(), ...patch };
        this.prefs.set(merged);
        this.applyVisuals(merged);
        this.changeVersion++;
        this.dirty.set(true);
    }

    commit(): Promise<void> {
        const source = JSON.stringify(this.prefs());
        const version = this.changeVersion;
        const save = async () => {
            await this.auth.saveAppearancePrefs(source);
            this.loadedOwner = this.auth.userEmail();
            this.loadedSource = this.auth.appearancePrefsJson();
            if (version === this.changeVersion) this.dirty.set(false);
        };
        const write = this.commitQueue
            ? this.commitQueue.catch(() => undefined).then(save)
            : save();
        this.commitQueue = write;
        const clear = () => {
            if (this.commitQueue === write) this.commitQueue = null;
        };
        void write.then(clear, clear);
        return write;
    }

    private bootstrapDefaults(): AppearancePrefs {
        let sidebarMode: SidebarMode = 'full';
        try {
            sidebarMode = localStorage.getItem(SIDEBAR_MODE_STORAGE_KEY) === 'rail' ? 'rail' : 'full';
        } catch {
            // The expanded sidebar is the safe signed-out fallback when storage is unavailable.
        }
        return { ...DEFAULT_APPEARANCE, theme: this.theme.theme(), sidebarMode };
    }

    private applyVisuals(p: AppearancePrefs) {
        this.theme.set(p.theme);
        try {
            localStorage.setItem(SIDEBAR_MODE_STORAGE_KEY, p.sidebarMode);
        } catch {
            // The signal remains the source of truth for this session.
        }
        this.applyAccent(p);
    }

    private applyAccent(p: AppearancePrefs) {
        let el = document.getElementById('__appearance-accent') as HTMLStyleElement | null;
        if (!el) {
            el = document.createElement('style');
            el.id = '__appearance-accent';
            document.head.appendChild(el);
        }
        const day = resolveAccent(p.accentLight, 'light'), night = resolveAccent(p.accentDark, 'dark');
        // The default writes nothing: this rule and the base block have equal specificity and this
        // one comes later in <head>, so an injected copy would pin the bench accent against
        // styles.scss for every logged-in user.
        const ink = (hex: string) => accentInk(hex) === 'dark' ? 'var(--text)' : 'var(--sheet)';
        el.textContent = day === BENCH_ACCENT.hex && night === BENCH_ACCENT.hex ? ''
            : `:root{--accent:${day};--accent-ink:${ink(day)}}`
            + `:root[data-theme="dark"]{--accent:${night};--accent-ink:${ink(night)}}`;
    }
}
