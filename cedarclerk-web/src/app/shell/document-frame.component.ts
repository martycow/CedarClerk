import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';
import { HeaderMeta } from './page-header.component';

export type DocumentTab = 'write' | 'preview' | 'publish';

export interface DocumentTabItem {
    id: DocumentTab;
    label: string;
    compactLabel?: string;
    icon: IconName;
}

@Component({
    selector: 'app-document-frame',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: { 'data-surface': 'paper' },
    template: `
        <header class="frame-header">
            <div class="frame-top">
                @if (kicker()) {
                    <span class="frame-kicker"><app-icon name="file-text" size="sm" />{{ kicker() }}</span>
                }
                @if (statusTag(); as tag) {
                    <span class="tag" [class.ok]="tag.tone === 'ok'" [class.muted]="tag.tone === 'muted' || !tag.tone"
                          [class.warn]="tag.tone === 'warn'" [attr.title]="tag.title || null">{{ tag.text }}</span>
                }
                <span class="frame-spacer"></span>
                @if (saveWord()) {
                    <span class="frame-save" [attr.data-state]="saveState()" role="status">
                        <app-icon [name]="saveIcon()" size="sm" />
                        <span>{{ saveWord() }}</span>
                    </span>
                }
                @if (dateLabel()) {
                    <span class="frame-divider" aria-hidden="true"></span>
                    <span class="frame-date"><app-icon name="calendar-blank" size="sm" />{{ dateLabel() }}</span>
                }
            </div>

            @if (tabs().length) {
                <div class="frame-tabs" role="tablist" [attr.aria-label]="tabsLabel() || null">
                    @for (item of tabs(); track item.id) {
                        <button type="button" class="frame-tab" role="tab" [class.is-on]="item.id === tab()"
                                [attr.aria-label]="item.label"
                                [attr.aria-selected]="item.id === tab()" [attr.tabindex]="item.id === tab() ? 0 : -1"
                                [attr.id]="'frame-tab-' + item.id" [attr.aria-controls]="'frame-panel-' + item.id"
                                (click)="pick(item.id)" (keydown)="onTabKey($event, item.id)">
                            <app-icon [name]="item.icon" size="sm" />
                            <span class="frame-tab-label" [attr.data-compact-label]="item.compactLabel || item.label">{{ item.label }}</span>
                        </button>
                    }
                </div>
            }
        </header>

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

        :host { --frame-gutter: clamp(var(--space-4), 2vw, var(--space-10)); }

        .frame-header {
            flex: none;
            padding: var(--space-2) 0;
            border-bottom: 1px solid var(--frame-rule);
            color: var(--wood-ink);
        }

        .frame-top {
            display: flex;
            align-items: center;
            gap: clamp(var(--space-2), 1vw, 14px);
            flex: none;
            box-sizing: border-box;
            min-height: var(--hit-target);
            padding: 0 var(--frame-gutter);
            color: var(--wood-ink);
        }

        .frame-divider {
            flex: none;
            width: 1px;
            height: 22px;
            background: var(--frame-control-edge);
        }

        .frame-save {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--fs-13);
            color: var(--wood-ink);
            white-space: nowrap;
        }

        .frame-save[data-state="saved"] app-icon { color: var(--pine-mark); }
        .frame-save[data-state="error"] { color: var(--danger); }

        .frame-spacer { flex: 1; }

        .frame-date {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--fs-14);
            color: var(--wood-ink-soft);
            white-space: nowrap;
        }

        .frame-kicker {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
            overflow: hidden;
            font-size: var(--fs-14);
            color: var(--wood-ink-soft);
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        .frame-tabs {
            display: grid;
            grid-template-columns: repeat(3, minmax(0, 1fr));
            gap: var(--space-2);
            flex: none;
            margin-top: var(--space-1);
            padding: 0 var(--frame-gutter);
        }

        .frame-tab {
            display: flex;
            align-items: center;
            justify-content: center;
            gap: var(--space-2);
            min-width: 0;
            min-height: var(--space-8);
            padding: 0 var(--space-3);
            border: 1px solid var(--frame-control-edge);
            border-radius: var(--radius-sm);
            background: var(--frame-tab-rest);
            color: var(--wood-ink-soft);
            font-family: var(--font-display);
            font-size: var(--fs-17);
            font-weight: 600;
            cursor: pointer;
        }

        .frame-tab:hover { background: var(--frame-tab-hover); color: var(--wood-ink); }
        .frame-tab.is-on {
            border-color: var(--wood-ink);
            background: var(--frame-tab-active);
            box-shadow: inset 0 0 0 1px var(--wood-ink);
            color: var(--wood-ink);
            font-weight: 700;
        }

        .frame-tab-label {
            min-width: 0;
            overflow: hidden;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        @media (max-width: 640px) {
            .frame-tabs { gap: var(--space-1); padding-inline: var(--space-2); }
            .frame-tab { gap: var(--space-1); padding-inline: var(--space-1); }
            .frame-tab-label { font-size: 0; }
            .frame-tab-label::after {
                content: attr(data-compact-label);
                font-size: var(--fs-ui);
            }
        }

        @media (max-width: 420px) {
            .frame-tab app-icon { display: none; }
            .frame-tab-label::after { font-size: var(--fs-13); }
        }

        .frame-body {
            display: flex;
            flex: 1;
            flex-direction: column;
            min-height: 0;
            padding: var(--space-2) var(--frame-gutter) var(--space-4);
        }

        .frame-footer {
            display: flex;
            align-items: center;
            gap: var(--space-4);
            flex: none;
            box-sizing: border-box;
            min-height: var(--footer-h);
            padding: 0 var(--frame-gutter);
            border-top: 1px solid var(--border);
            background: var(--surface);
            color: var(--text);
        }

        .frame-footer-start, .frame-footer-end { display: inline-flex; align-items: center; gap: var(--space-2); }

        .frame-footer-text {
            flex: 1;
            min-width: 0;
            overflow: hidden;
            font-size: var(--fs-13);
            color: var(--t3);
            text-align: center;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        @media (max-width: 759px) {
            .frame-date, .frame-divider { display: none; }
            .frame-tab { gap: var(--space-1); padding-inline: var(--space-1); font-size: var(--fs-14); }
            .frame-footer {
                flex-direction: column;
                align-items: stretch;
                gap: var(--space-2);
                padding-block: var(--space-2);
            }
            .frame-footer-start, .frame-footer-end { width: 100%; min-width: 0; }
            .frame-footer-end { justify-content: flex-end; }
            .frame-footer-text { order: 1; text-align: left; }
        }
    `],
})
export class DocumentFrameComponent {
    readonly kicker = input('');
    readonly statusTag = input<HeaderMeta | null>(null);
    readonly tab = input<DocumentTab>('write');
    readonly tabs = input<readonly DocumentTabItem[]>([]);
    readonly tabsLabel = input('');
    readonly saveWord = input('');
    readonly saveState = input<'saved' | 'saving' | 'error'>('saved');
    readonly dateLabel = input('');
    readonly footerText = input('');
    readonly tabChange = output<DocumentTab>();

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

    /** Arrow keys walk the tablist and select as they go; Home and End jump to its ends. */
    onTabKey(event: KeyboardEvent, id: DocumentTab): void {
        const ids = this.tabs().map(item => item.id);
        const at = ids.indexOf(id);
        let next: DocumentTab | undefined;
        switch (event.key) {
            case 'ArrowRight': next = ids[(at + 1) % ids.length]; break;
            case 'ArrowLeft': next = ids[(at - 1 + ids.length) % ids.length]; break;
            case 'Home': next = ids[0]; break;
            case 'End': next = ids[ids.length - 1]; break;
            default: return;
        }
        event.preventDefault();
        const target = event.currentTarget as HTMLElement | null;
        const list = target?.parentElement;
        (list?.querySelector<HTMLElement>(`#frame-tab-${next}`))?.focus();
        this.pick(next);
    }
}
