import { Component, OnInit, inject, signal } from '@angular/core';
import { DragDropModule, CdkDragDrop, moveItemInArray, transferArrayItem } from '@angular/cdk/drag-drop';
import { AppearanceService, ACCENT_PRESETS, AppearancePrefs, MAX_TABLE_SIZE } from '../core/appearance.service';
import { ToolbarLayoutService } from '../core/toolbar-layout.service';
import { TOOLBAR_GROUPS, ToolbarButtonId, ToolbarPreset, presetLayout } from '../core/toolbar-layout';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeService } from '../core/theme.service';
import { ModalComponent } from './modal.component';
import { httpErrorMessage } from '../core/http-error.util';
import { IconComponent } from './icon.component';

// Long enough that a slider drag is one write, short enough that closing the modal right after a
// click never races the save (the modal's own close path flushes it anyway — see apply()).
const APPEARANCE_COMMIT_DEBOUNCE_MS = 600;

// I14/B15 put appearance and toolbar customization in a panel beside the writing sheet, so every
// control's effect on *that sheet* was visible without judging it on a different screen.
//
// Moved into a modal (28.07.2026, ADR following ADR-053) — the always-present sliding column
// broke the page's own layout (a second, page-level scrollbar alongside the sheet's own) and,
// per Marty's direct call, didn't belong pinned to the side regardless. Live preview while
// dragging a slider is the one thing this trades away; everything still updates the sheet the
// instant the modal closes (the `prefs` signal never stopped updating live, only its visibility
// changed) rather than requiring Apply-then-close to see anything.
@Component({
    selector: 'app-appearance-panel',
    imports: [IconComponent, DragDropModule, ModalComponent],
    templateUrl: 'appearance-panel.component.html',
    styleUrls: ['appearance-panel.component.css'],
})
export class AppearancePanelComponent implements OnInit {
    appearance = inject(AppearanceService);
    toolbarLayout = inject(ToolbarLayoutService);
    theme = inject(ThemeService);
    t = inject(LocaleService).t;

    readonly accentPresets = ACCENT_PRESETS;
    readonly toolbarGroups = TOOLBAR_GROUPS;
    // The AI group is pinned — it isn't a normal toolbar group that can be moved between rows.
    readonly movableToolbarGroups = TOOLBAR_GROUPS.filter(g => g.id !== 'ai');

    appearanceError = signal<string | null>(null);
    // T-041 — Apply was real (it persisted the prefs) but read as decoration, because every
    // control already changed the sheet live (ADR-053) and the toolbar half of this same panel
    // saved itself on click. The appearance half now saves itself too; this is the indicator that
    // replaced the button.
    saveState = signal<'saved' | 'saving' | 'error'>('saved');
    private commitTimer?: ReturnType<typeof setTimeout>;
    row1Groups = signal<string[]>([]);
    row2Groups = signal<string[]>([]);
    toolbarError = signal<string | null>(null);

    // Closed by default; opened from the topbar's palette button (editor.component.html holds
    // the trigger via a template reference variable, since the button lives in a different part
    // of that template than this component's own tag).
    open = signal(false);

    ngOnInit() {
        this.initToolbarRows();
    }

    private initToolbarRows() {
        // Read through the service's normalizer so the panel shows exactly the order the toolbar
        // renders — the two used to be derived separately and could disagree.
        this.row1Groups.set([...this.toolbarLayout.row1Ordered()]);
        this.row2Groups.set([...this.toolbarLayout.row2Ordered()]);
    }

    // FI1: the toggle used to only pick which theme's accent the swatches below edit, while the
    // app's actual theme stayed whatever it already was — indistinguishable from a dead control.
    // It now IS the real theme switch (instant, like every other theme toggle in the app); the
    // accent swatches simply follow whichever theme that leaves you on.
    activeAccentHex(): string {
        const p = this.appearance.prefs();
        return this.theme.theme() === 'dark' ? p.accentDark : p.accentLight;
    }

    // The swatch shows the tone the theme will actually paint, not the preset's day hex — night
    // derives its own (ADR-141), and a swatch that ignores that advertises a colour you cannot get.
    swatchHex(preset: { hex: string; night: string }): string {
        return this.theme.theme() === 'dark' ? preset.night : preset.hex;
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
        this.previewAndSave(this.theme.theme() === 'dark' ? { accentDark: hex } : { accentLight: hex });
    }

    setSheetWidth(value: AppearancePrefs['sheetWidth']) {
        this.previewAndSave({ sheetWidth: value });
    }

    setTypeface(value: AppearancePrefs['typeface']) {
        this.previewAndSave({ typeface: value });
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

    toggleAppearanceFlag(key: 'showParagraphNumbers' | 'showLineRules' | 'showWordCount' | 'focusModeHideToolbar' | 'sheetFlush', ev: Event) {
        this.previewAndSave({ [key]: (ev.target as HTMLInputElement).checked });
    }

    // Debounced rather than per-change: a slider drag is dozens of changes, and the point of
    // AppearanceService's preview/commit split was to stop firing one save per tick.
    private commitSoon() {
        clearTimeout(this.commitTimer);
        this.commitTimer = setTimeout(() => void this.apply(), APPEARANCE_COMMIT_DEBOUNCE_MS);
    }

    // Closing must not swallow a debounce still in flight.
    close() {
        clearTimeout(this.commitTimer);
        this.open.set(false);
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

    // Presets set the whole layout; drag-and-drop moves whole groups between rows (not individual
    // buttons — see core/toolbar-layout.ts for why); the checkbox catalog hides/shows individual
    // buttons regardless of which row their group is in.
    async pickToolbarPreset(preset: ToolbarPreset) {
        this.toolbarError.set(null);
        try {
            await this.toolbarLayout.save(presetLayout(preset));
            this.initToolbarRows();
        } catch (e) {
            this.toolbarError.set(httpErrorMessage(e, this.t().settings.errors.toolbar));
        }
    }

    async dropToolbarGroup(event: CdkDragDrop<string[]>) {
        if (event.previousContainer === event.container) {
            moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
        } else {
            transferArrayItem(event.previousContainer.data, event.container.data, event.previousIndex, event.currentIndex);
        }
        this.toolbarError.set(null);
        try {
            // Both rows are persisted, in order — saving only row2 was why reordering appeared to
            // work in the panel and then reverted on reload.
            await this.toolbarLayout.save({
                ...this.toolbarLayout.layout(),
                preset: 'custom',
                row1Groups: [...this.row1Groups()],
                row2Groups: [...this.row2Groups()],
            });
        } catch (e) {
            this.toolbarError.set(httpErrorMessage(e, this.t().settings.errors.toolbar));
        }
    }

    groupLabel(id: string): string {
        return this.toolbarGroups.find(g => g.id === id)?.label ?? id;
    }

    groupButtonIds(group: { buttons: { id: ToolbarButtonId }[] }): ToolbarButtonId[] {
        return group.buttons.map(b => b.id);
    }

    isButtonHidden(id: ToolbarButtonId): boolean {
        return this.toolbarLayout.layout().hiddenButtons.includes(id);
    }

    groupVisibleCount(buttonIds: ToolbarButtonId[]): number {
        return buttonIds.filter(id => !this.isButtonHidden(id)).length;
    }

    private async saveHiddenButtons(hiddenButtons: ToolbarButtonId[]) {
        this.toolbarError.set(null);
        try {
            await this.toolbarLayout.save({ ...this.toolbarLayout.layout(), preset: 'custom', hiddenButtons });
        } catch (e) {
            this.toolbarError.set(httpErrorMessage(e, this.t().settings.errors.toolbar));
        }
    }

    toggleButtonVisible(id: ToolbarButtonId, ev: Event) {
        const checked = (ev.target as HTMLInputElement).checked;
        const current = this.toolbarLayout.layout().hiddenButtons;
        this.saveHiddenButtons(checked ? current.filter(b => b !== id) : [...current, id]);
    }

    toggleGroupVisible(buttonIds: ToolbarButtonId[], ev: Event) {
        const checked = (ev.target as HTMLInputElement).checked;
        const current = this.toolbarLayout.layout().hiddenButtons;
        this.saveHiddenButtons(checked
            ? current.filter(id => !buttonIds.includes(id as ToolbarButtonId))
            : [...new Set([...current, ...buttonIds])]);
    }
}
