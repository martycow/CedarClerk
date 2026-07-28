import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';

export interface AppearancePrefs {
    accentLight: string;
    accentDark: string;
    sheetWidth: 'narrow' | 'normal' | 'wide' | 'full';
    typeface: 'system' | 'serif' | 'serifClassic' | 'mono' | 'rounded';
    fontSize: number; // px, sheet base (before zoom)
    lineHeight: number;
    showParagraphNumbers: boolean;
    showLineRules: boolean;
    // Default size for Insert → Table (I5). Bounded by MAX_TABLE_SIZE — "within reason", as asked.
    tableRows: number;
    tableCols: number;
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
    showParagraphNumbers: false,
    showLineRules: false,
    tableRows: 3,
    tableCols: 3,
    showWordCount: true,
    focusModeHideToolbar: false,
    sheetFlush: false,
};

// A table wider or taller than this stops being a table and starts being a spreadsheet — and
// Telegram's Blocks renderer has to carry every cell.
export const MAX_TABLE_SIZE = 10;

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

// System stacks only (FI1) — no webfonts ship (see docs/DESIGN.md), so "more typefaces" means
// more of what the OS already has, not a new loading path.
export const TYPEFACE_STACK: Record<AppearancePrefs['typeface'], string> = {
    system: 'var(--font-sans)',
    serif: 'Georgia, "Iowan Old Style", serif',
    serifClassic: '"Times New Roman", Times, "Liberation Serif", serif',
    mono: 'var(--font-mono)',
    rounded: 'ui-rounded, "SF Pro Rounded", "Segoe UI Rounded", var(--font-sans)',
};

// Personal editor preferences (ADR-035, revised by FI1) — deliberately scoped to the authoring
// app only, never applied to the public blog (which keeps its own fixed branding). Only the
// accent is genuinely global chrome (topbar/toolbar/buttons everywhere); the writing-sheet prefs
// (width/typeface/font-size/etc.) are read directly by EditorComponent since they only affect its
// own template.
//
// FI1 reversed ADR-035's "applies instantly, no Save button" for this half of the panel: `prefs`
// still updates (and the sheet still re-renders) on every interaction — that live preview is the
// entire point of the side panel — but the network round-trip is now deferred to an explicit
// `commit()`, so dragging a slider no longer fires a save per tick. `preview()` is the live-only
// half, `commit()` is the persist half; `dirty` is what the panel's Apply button gates on.
@Injectable({ providedIn: 'root' })
export class AppearanceService {
    private auth = inject(AuthService);
    readonly prefs = signal<AppearancePrefs>(DEFAULT_APPEARANCE);
    readonly dirty = signal(false);
    private committed: AppearancePrefs = DEFAULT_APPEARANCE;

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
        this.committed = merged;
        this.dirty.set(false);
        this.applyAccent(merged);
    }

    // Applies live (sheet + accent CSS var) without saving — the panel calls this on every
    // control interaction so the preview stays instant even though persistence no longer is.
    preview(patch: Partial<AppearancePrefs>) {
        const merged = { ...this.prefs(), ...patch };
        this.prefs.set(merged);
        this.applyAccent(merged);
        this.dirty.set(true);
    }

    // Persists whatever is currently being previewed. Throws on failure — the caller (the
    // panel's Apply button) is what shows the error, same as every other explicit save in the app.
    async commit(): Promise<void> {
        const current = this.prefs();
        await this.auth.saveAppearancePrefs(JSON.stringify(current));
        this.committed = current;
        this.dirty.set(false);
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
