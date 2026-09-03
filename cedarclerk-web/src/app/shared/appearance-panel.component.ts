import { Component, OnDestroy, inject, signal } from '@angular/core';
import { AppearanceService, ACCENT_PRESETS, AppearancePrefs, MAX_TABLE_SIZE } from '../core/appearance.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { IconComponent } from './icon.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';

// Long enough that a slider drag is one write, short enough that leaving Preferences right after a
// click does not normally expose the network round-trip.
const APPEARANCE_COMMIT_DEBOUNCE_MS = 600;

@Component({
    selector: 'app-appearance-panel',
    imports: [IconComponent, LeafTagComponent],
    templateUrl: 'appearance-panel.component.html',
    styleUrls: ['appearance-panel.component.css'],
})
export class AppearancePanelComponent implements OnDestroy {
    appearance = inject(AppearanceService);
    t = inject(LocaleService).t;

    readonly accentPresets = ACCENT_PRESETS;

    appearanceError = signal<string | null>(null);
    // T-041 — Apply was real (it persisted the prefs) but read as decoration, because every
    // control already changed the sheet live (ADR-053). The panel saves itself; this is the
    // indicator that replaced the button.
    saveState = signal<'saved' | 'saving' | 'error'>('saved');
    private commitTimer?: ReturnType<typeof setTimeout>;

    activeAccentHex(): string {
        const p = this.appearance.prefs();
        return p.theme === 'dark' ? p.accentDark : p.accentLight;
    }

    // The swatch shows the tone the theme will actually paint, not the preset's day hex — night
    // derives its own (ADR-141), and a swatch that ignores that advertises a colour you cannot get.
    swatchHex(preset: { hex: string; night: string }): string {
        return this.appearance.prefs().theme === 'dark' ? preset.night : preset.hex;
    }

    isActivePreset(hex: string): boolean {
        return this.activeAccentHex().toUpperCase() === hex.toUpperCase();
    }

    // FI1/T-041: every control updates the sheet immediately through `AppearanceService.prefs`
    // and the write follows on its own a moment later — there is no button to press.
    private previewAndSave(patch: Partial<AppearancePrefs>) {
        this.appearance.preview(patch);
        this.commitSoon();
    }

    pickAccentPreset(hex: string) {
        this.previewAndSave(this.appearance.prefs().theme === 'dark' ? { accentDark: hex } : { accentLight: hex });
    }

    setTheme(value: AppearancePrefs['theme']) {
        this.previewAndSave({ theme: value });
    }

    setSidebarMode(value: AppearancePrefs['sidebarMode']) {
        this.previewAndSave({ sidebarMode: value });
    }

    setSheetWidth(value: AppearancePrefs['sheetWidth']) {
        this.previewAndSave({ sheetWidth: value });
    }

    setTypeface(value: AppearancePrefs['typeface']) {
        this.previewAndSave(value === 'departure' ? { typeface: value, fontSize: 22 } : { typeface: value });
    }

    setFontSize(px: number) {
        this.previewAndSave({ fontSize: px });
    }

    setLineHeight(value: number) {
        this.previewAndSave({ lineHeight: value });
    }

    readonly maxTableSize = MAX_TABLE_SIZE;

    private clampTable(n: number): number {
        return Number.isFinite(n) ? Math.min(Math.max(Math.round(n), 1), MAX_TABLE_SIZE) : 3;
    }

    setTableRows(n: number) {
        this.previewAndSave({ tableRows: this.clampTable(n) });
    }

    setTableCols(n: number) {
        this.previewAndSave({ tableCols: this.clampTable(n) });
    }

    toggleAppearanceFlag(key: 'showParagraphNumbers' | 'showLineRules' | 'showInvisibles' | 'showWordCount' | 'focusModeHideToolbar' | 'sheetFlush', ev: Event) {
        this.previewAndSave({ [key]: (ev.target as HTMLInputElement).checked });
    }

    // Debounced rather than per-change: a slider drag is dozens of changes, and the point of
    // AppearanceService's preview/commit split was to stop firing one save per tick.
    private commitSoon() {
        clearTimeout(this.commitTimer);
        this.commitTimer = setTimeout(() => void this.apply(), APPEARANCE_COMMIT_DEBOUNCE_MS);
    }

    ngOnDestroy(): void {
        clearTimeout(this.commitTimer);
        void this.apply();
    }

    async apply() {
        if (!this.appearance.dirty()) return;
        this.appearanceError.set(null);
        this.saveState.set('saving');
        try {
            await this.appearance.commit();
            this.saveState.set('saved');
        } catch (e) {
            this.saveState.set('error');
            this.appearanceError.set(httpErrorMessage(e, this.t().settings.errors.appearance));
        }
    }

}
