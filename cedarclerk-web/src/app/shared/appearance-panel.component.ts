import { Component, OnInit, inject, signal } from '@angular/core';
import { DragDropModule, CdkDragDrop, moveItemInArray, transferArrayItem } from '@angular/cdk/drag-drop';
import { AppearanceService, ACCENT_PRESETS, AppearancePrefs } from '../core/appearance.service';
import { ToolbarLayoutService } from '../core/toolbar-layout.service';
import { TOOLBAR_GROUPS, ToolbarButtonId, ToolbarPreset, presetLayout } from '../core/toolbar-layout';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';

// I14/B15 — appearance and toolbar customization moved out of the settings page into a panel that
// sits beside the writing sheet, because every control here changes how *that sheet* looks and
// judging a type size or a line height on a different screen is guesswork. The sheet is the
// preview; nothing extra had to be built to preview it.
//
// Extracted rather than duplicated: /settings dropped both sections, so there is still exactly
// one place each of these lives (the same reasoning as B6/I11 for navigation).
@Component({
    selector: 'app-appearance-panel',
    imports: [DragDropModule],
    templateUrl: 'appearance-panel.component.html',
    styleUrls: ['appearance-panel.component.css'],
})
export class AppearancePanelComponent implements OnInit {
    appearance = inject(AppearanceService);
    toolbarLayout = inject(ToolbarLayoutService);
    t = inject(LocaleService).t;

    readonly accentPresets = ACCENT_PRESETS;
    readonly toolbarGroups = TOOLBAR_GROUPS;
    // The AI group is pinned — it isn't a normal toolbar group that can be moved between rows.
    readonly movableToolbarGroups = TOOLBAR_GROUPS.filter(g => g.id !== 'ai');

    appearanceMode = signal<'light' | 'dark'>('light');
    appearanceError = signal<string | null>(null);
    row1Groups = signal<string[]>([]);
    row2Groups = signal<string[]>([]);
    toolbarError = signal<string | null>(null);

    // Collapsed by default: the panel shares horizontal space with the sheet, and the sheet is
    // what the user came for.
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

    // Applies instantly and saves in the background, no Save button (ADR-035) — these are pure
    // preference toggles, and immediate visual feedback is the entire point of this panel.
    activeAccentHex(): string {
        const p = this.appearance.prefs();
        return this.appearanceMode() === 'dark' ? p.accentDark : p.accentLight;
    }

    isActivePreset(hex: string): boolean {
        return this.activeAccentHex().toUpperCase() === hex.toUpperCase();
    }

    private async saveAppearance(patch: Partial<AppearancePrefs>) {
        this.appearanceError.set(null);
        try {
            await this.appearance.save(patch);
        } catch (e) {
            this.appearanceError.set(httpErrorMessage(e, this.t().settings.errors.appearance));
        }
    }

    pickAccentPreset(hex: string) {
        this.saveAppearance(this.appearanceMode() === 'dark' ? { accentDark: hex } : { accentLight: hex });
    }

    setSheetWidth(value: AppearancePrefs['sheetWidth']) {
        this.saveAppearance({ sheetWidth: value });
    }

    setTypeface(value: AppearancePrefs['typeface']) {
        this.saveAppearance({ typeface: value });
    }

    setFontSize(px: number) {
        this.saveAppearance({ fontSize: px });
    }

    setLineHeight(value: number) {
        this.saveAppearance({ lineHeight: value });
    }

    toggleAppearanceFlag(key: 'showParagraphNumbers' | 'showLineRules' | 'showWordCount' | 'focusModeHideToolbar' | 'sheetFlush', ev: Event) {
        this.saveAppearance({ [key]: (ev.target as HTMLInputElement).checked });
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
