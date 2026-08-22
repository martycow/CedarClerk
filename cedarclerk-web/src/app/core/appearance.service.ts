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
    accentLight: BENCH_ACCENT.hex,
    accentDark: BENCH_ACCENT.hex,
    sheetWidth: 'normal',
    typeface: 'system',
    fontSize: 17,
    lineHeight: 1.75,
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

export const SHEET_WIDTH_PX: Record<AppearancePrefs['sheetWidth'], number> = {
    narrow: 560, normal: 640, wide: 820, full: 1040,
};

// The three named faces are the self-hosted ones (ADR-143); the other two stacks are what the OS
// already has, not a new loading path.
export const TYPEFACE_STACK: Record<AppearancePrefs['typeface'], string> = {
    system: 'var(--font-sans)',
    serif: 'var(--font-serif)',
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
        const stored = { ...DEFAULT_APPEARANCE, ...parsed };
        // Snapped to a preset here rather than only at paint time, so the panel marks the swatch
        // the app is actually showing.
        const merged = {
            ...stored,
            accentLight: presetFor(stored.accentLight).hex,
            accentDark: presetFor(stored.accentDark).hex,
        };
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
        const day = presetFor(p.accentLight), night = presetFor(p.accentDark);
        // The default writes nothing: this rule and the base block have equal specificity and this
        // one comes later in <head>, so an injected copy would pin the bench accent against
        // styles.scss for every logged-in user.
        el.textContent = day === BENCH_ACCENT && night === BENCH_ACCENT ? ''
            : `:root{--accent:${day.hex}}:root[data-theme="dark"]{--accent:${night.night}}`;
    }
}
