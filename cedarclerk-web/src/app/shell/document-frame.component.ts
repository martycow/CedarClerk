import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';
import { AppShellComponent } from './app-shell.component';
import { HeaderMeta } from './page-header.component';
import { ProjectSwitcherComponent, SidebarProject } from './project-switcher.component';

export type DocumentTab = 'write' | 'preview' | 'publish';

export interface DocumentTabItem {
    id: DocumentTab;
    label: string;
    icon: IconName;
}

// The editor is one document with three tabs (ADR-239 clause 9). The frame draws the top bar, the
// title line, the tabs and the footer; the editor lane fills the slots. The top bar exists only in
// the shell's rail mode — in full mode the sidebar carries the switcher.
@Component({
    selector: 'app-document-frame',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent, ProjectSwitcherComponent],
    host: { 'data-surface': 'paper' },
    template: `
        @if (rail()) {
            <div class="frame-top">
                <app-project-switcher variant="inline" [project]="project()" [projects]="projects()"
                                      [hint]="t().shell.switchProject" [fallbackName]="t().shell.allProjects" />
                <span class="frame-divider" aria-hidden="true"></span>
                <span class="frame-history">
                    <button type="button" class="frame-icon-btn" [disabled]="!canUndo()"
                            [attr.title]="t().editor.tb.undo" [attr.aria-label]="t().editor.tb.undo" (click)="undo.emit()">
                        <app-icon name="arrow-u-up-left" size="sm" />
                    </button>
                    <button type="button" class="frame-icon-btn" [disabled]="!canRedo()"
                            [attr.title]="t().editor.tb.redo" [attr.aria-label]="t().editor.tb.redo" (click)="redo.emit()">
                        <app-icon name="arrow-u-up-right" size="sm" />
                    </button>
                </span>
                @if (saveWord()) {
                    <span class="frame-save" [attr.data-state]="saveState()" role="status">
                        <app-icon [name]="saveIcon()" size="sm" />
                        <span>{{ saveWord() }}</span>
                    </span>
                }
                <span class="frame-spacer"></span>
                @if (dateLabel()) {
                    <span class="frame-date"><app-icon name="calendar-blank" size="sm" />{{ dateLabel() }}</span>
                    <span class="frame-divider" aria-hidden="true"></span>
                }
                <span class="frame-primary"><ng-content select="[primary]" /></span>
            </div>
        }

        <div class="frame-head">
            <div class="frame-heading">
                @if (kicker()) {
                    <p class="frame-kicker"><app-icon name="file-text" size="sm" />{{ kicker() }}</p>
                }
                <div class="frame-title-row">
                    <h1 class="frame-title">{{ title() }}</h1>
                    @if (statusTag(); as tag) {
                        <span class="tag" [class.ok]="tag.tone === 'ok'" [class.muted]="tag.tone === 'muted' || !tag.tone"
                              [class.warn]="tag.tone === 'warn'" [attr.title]="tag.title || null">{{ tag.text }}</span>
                    }
                </div>
            </div>
            <div class="frame-title-actions"><ng-content select="[title-actions]" /></div>
        </div>

        @if (tabs().length) {
            <div class="frame-tabs" role="tablist" [attr.aria-label]="tabsLabel() || null">
                @for (item of tabs(); track item.id) {
                    <button type="button" class="frame-tab" role="tab" [class.is-on]="item.id === tab()"
                            [attr.aria-selected]="item.id === tab()" [attr.tabindex]="item.id === tab() ? 0 : -1"
                            (click)="pick(item.id)">
                        <app-icon [name]="item.icon" size="sm" />{{ item.label }}
                    </button>
                }
            </div>
        }

        <div class="frame-body"><ng-content select="[body]" /></div>

        <div class="frame-footer">
            <span class="frame-footer-start"><ng-content select="[footer-start]" /></span>
            <span class="frame-footer-text">{{ footerText() }}</span>
            <span class="frame-footer-end"><ng-content select="[footer-end]" /></span>
        </div>
    `,
    styles: [`
        :host {
            display: flex;
            flex: 1;
            flex-direction: column;
            min-height: 0;
            font-family: var(--font-sans);
        }

        .frame-top {
            display: flex;
            align-items: center;
            gap: 14px;
            flex: none;
            box-sizing: border-box;
            min-height: var(--topbar-h);
            padding: 0 var(--space-5);
            border-bottom: 1px solid var(--border);
            background: var(--surface);
            color: var(--text);
        }

        .frame-divider {
            flex: none;
            width: 1px;
            height: 22px;
            background: var(--border);
        }

        .frame-history { display: inline-flex; gap: var(--space-1); }

        .frame-icon-btn {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: var(--hit-target);
            min-height: var(--hit-target);
            border: none;
            border-radius: var(--radius-sm);
            background: none;
            color: var(--t2);
            cursor: pointer;
        }

        .frame-icon-btn:hover:not(:disabled) { background: var(--hover); color: var(--text); }
        .frame-icon-btn:disabled { opacity: .5; cursor: default; }

        .frame-save {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--fs-13);
            color: var(--t2);
        }

        .frame-save[data-state="saved"] app-icon { color: var(--ok); }
        .frame-save[data-state="error"] { color: var(--danger); }

        .frame-spacer { flex: 1; }

        .frame-date {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--fs-14);
            color: var(--t2);
        }

        .frame-primary { display: inline-flex; align-items: center; gap: var(--space-1); }

        /* The title stands on the wall, so it writes with the wall's ink (ADR-141). */
        .frame-head {
            display: flex;
            align-items: flex-start;
            justify-content: space-between;
            gap: var(--space-5);
            flex: none;
            padding: 22px var(--space-10) 0;
            color: var(--wood-ink);
        }

        .frame-heading { display: flex; flex-direction: column; gap: 6px; min-width: 0; }

        .frame-kicker {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            margin: 0;
            font-size: var(--fs-14);
            color: var(--wood-ink-soft);
        }

        .frame-title-row { display: flex; align-items: center; gap: var(--space-3); min-width: 0; }

        .frame-title {
            margin: 0;
            overflow: hidden;
            font-family: var(--font-display);
            font-size: var(--fs-30);
            font-weight: 700;
            line-height: 1.1;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        .frame-title-actions { display: flex; align-items: center; gap: var(--space-2); flex: none; }

        .frame-tabs {
            display: flex;
            gap: 28px;
            flex: none;
            margin-top: 18px;
            padding: 0 var(--space-10);
            border-bottom: 1px solid var(--border);
        }

        .frame-tab {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            padding: 0 var(--space-1) var(--space-3);
            border: none;
            border-bottom: 2px solid transparent;
            background: none;
            color: var(--wood-ink-soft);
            font-family: var(--font-sans);
            font-size: var(--fs-15);
            font-weight: 600;
            cursor: pointer;
        }

        .frame-tab:hover { color: var(--wood-ink); }
        .frame-tab.is-on { color: var(--wood-ink); border-bottom-color: var(--accent); }

        .frame-body {
            display: flex;
            flex: 1;
            flex-direction: column;
            min-height: 0;
            padding: var(--space-4) var(--space-10);
        }

        .frame-footer {
            display: flex;
            align-items: center;
            gap: var(--space-4);
            flex: none;
            box-sizing: border-box;
            min-height: var(--footer-h);
            padding: 0 var(--space-10);
            border-top: 1px solid var(--border);
            background: var(--surface);
            color: var(--text);
        }

        .frame-footer-start, .frame-footer-end { display: inline-flex; align-items: center; gap: var(--space-2); }

        .frame-footer-text {
            flex: 1;
            font-size: var(--fs-13);
            color: var(--t3);
            text-align: center;
        }
    `],
})
export class DocumentFrameComponent {
    readonly title = input.required<string>();
    readonly kicker = input('');
    readonly statusTag = input<HeaderMeta | null>(null);
    readonly tab = input<DocumentTab>('write');
    readonly tabs = input<readonly DocumentTabItem[]>([]);
    readonly tabsLabel = input('');
    readonly saveWord = input('');
    readonly saveState = input<'saved' | 'saving' | 'error'>('saved');
    readonly dateLabel = input('');
    readonly footerText = input('');
    readonly canUndo = input(false);
    readonly canRedo = input(false);
    readonly project = input<SidebarProject | null>(null);
    readonly projects = input<readonly SidebarProject[]>([]);
    readonly tabChange = output<DocumentTab>();
    readonly undo = output<void>();
    readonly redo = output<void>();

    protected readonly t = inject(LocaleService).t;
    private readonly shell = inject(AppShellComponent, { optional: true });

    protected readonly rail = computed(() => this.shell?.mode() === 'rail');

    protected readonly saveIcon = computed<IconName>(() => {
        switch (this.saveState()) {
            case 'saving': return 'circle-notch';
            case 'error': return 'warning';
            default: return 'check';
        }
    });

    pick(id: DocumentTab): void {
        if (id !== this.tab()) this.tabChange.emit(id);
    }
}
