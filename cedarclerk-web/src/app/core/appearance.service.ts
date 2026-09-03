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
    // Default size for Insert → Table (I5). Bounded by MAX_TABLE_SIZE — "within reason", as asked.
    tableRows: number;
    tableCols: number;
    showWordCount: boolean;
    focusModeHideToolbar: boolean;
    sheetFlush: boolean; // no paper card — sheet merges with the canvas
}

// Two tones per preset, because night is derived downward against cream paper rather than mixed
// towards white (ADR-141): no single value clears 4.5:1 on both #F1EADA and #D9CEAE, and the
// night sheet has to stay readable as a label on the same colour used as a button fill.
// tools/check-contrast.mjs reads this list and scores every entry in both roles and both themes.
export const ACCENT_PRESETS: { name: string; hex: string; night: string }[] = [
    { name: 'Cedar', hex: '#39543C', night: '#39543C' },
    { name: 'Bark', hex: '#755934', night: '#624B2C' },
    { name: 'Slate', hex: '#4A5A6B', night: '#425160' },
    { name: 'Ink', hex: '#3A3730', night: '#3A3730' },
    { name: 'Rust', hex: '#914A29', night: '#7A3F22' },
];

const BENCH_ACCENT = ACCENT_PRESETS[0];

// A stored accent from another palette has no vetted night tone, and the picker offers nothing but
// these five — so it resolves to the bench accent rather than painting an unmeasured colour.
const presetFor = (hex: string) =>
    ACCENT_PRESETS.find(p => p.hex.toUpperCase() === hex.toUpperCase()) ?? BENCH_ACCENT;

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
            accentLight: presetFor(stored.accentLight).hex,
            accentDark: presetFor(stored.accentDark).hex,
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
        const day = presetFor(p.accentLight), night = presetFor(p.accentDark);
        // The default writes nothing: this rule and the base block have equal specificity and this
        // one comes later in <head>, so an injected copy would pin the bench accent against
        // styles.scss for every logged-in user.
        el.textContent = day === BENCH_ACCENT && night === BENCH_ACCENT ? ''
            : `:root{--accent:${day.hex}}:root[data-theme="dark"]{--accent:${night.night}}`;
    }
}
