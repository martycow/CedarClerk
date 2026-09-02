import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleService } from '../../core/i18n/locale.service';
import { IconComponent } from '../../shared/icon.component';
import { DestinationState, StateTagComponent } from './state-tag.component';

export type PreviewDestination = 'blog' | 'telegram' | 'x' | 'bluesky' | 'discord';
export type Readiness = DestinationState;

/** One row of the destinations card. Readiness is composed by the tab, never fetched. */
export interface DestinationRow {
    id: PreviewDestination;
    name: string;
    readiness: Readiness;
    detail: string;
}

@Component({
    selector: 'app-preview-destinations',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [RouterLink, IconComponent, StateTagComponent],
    host: { 'data-surface': 'paper', class: 'card' },
    template: `
        <span class="label pd-caption" id="pd-caption">{{ t().editor.previewTab.destinations }}</span>
        <div class="pd-list" role="tablist" aria-orientation="vertical" aria-labelledby="pd-caption">
            @for (row of rows(); track row.id) {
                <button type="button" class="pd-row" role="tab" [class.is-on]="row.id === selected()"
                        [attr.aria-selected]="row.id === selected()" [attr.tabindex]="row.id === selected() ? 0 : -1"
                        (click)="pick.emit(row.id)">
                    <span class="pd-text">
                        <span class="pd-name">{{ row.name }}</span>
                        <app-state-tag [state]="row.readiness" />
                        <span class="pd-detail">{{ row.detail }}</span>
                    </span>
                    <span class="pd-thumb" [attr.data-kind]="row.id" aria-hidden="true">
                        @switch (row.id) {
                            @case ('blog') { <i class="th th-title"></i><i class="th th-hero"></i><i class="th th-line"></i><i class="th th-line"></i><i class="th th-line th-short"></i> }
                            @case ('telegram') { <i class="th th-bubble"></i><i class="th th-line"></i><i class="th th-line"></i><i class="th th-bubble th-small"></i><i class="th th-line"></i> }
                            @default { <i class="th th-avatar"></i><i class="th th-line"></i><i class="th th-line"></i><i class="th th-line th-short"></i><i class="th th-blank"></i> }
                        }
                    </span>
                </button>
            }
        </div>
        <span class="pd-spacer"></span>
        <a class="pd-manage" routerLink="/settings" [queryParams]="{ tab: 'integrations' }">
            <app-icon name="gear" size="sm" />{{ t().editor.previewTab.manage }}
        </a>
    `,
    styles: [`
        :host {
            display: flex;
            flex-direction: column;
            width: 288px;
            flex: none;
            min-height: 0;
            overflow: hidden;
            font-family: var(--font-sans);
        }

        .pd-caption { padding: var(--space-4) var(--space-4) 10px; }

        .pd-list { display: flex; flex-direction: column; overflow: auto; }

        .pd-row {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            width: 100%;
            padding: var(--space-3) 14px;
            border: 0;
            border-left: 3px solid transparent;
            background: none;
            font-family: inherit;
            color: var(--text);
            text-align: left;
            cursor: pointer;
        }

        .pd-row:hover { background: var(--alt); }
        .pd-row.is-on { border-left-color: var(--accent); background: var(--asoft); }

        .pd-text { display: flex; flex: 1; flex-direction: column; align-items: flex-start; gap: 4px; min-width: 0; }

        .pd-name { font-size: var(--fs-15); font-weight: 700; }

        .pd-detail { font-size: var(--fs-13); color: var(--t2); }

        .pd-thumb {
            display: flex;
            flex-direction: column;
            gap: 3px;
            flex: none;
            width: 56px;
            height: 70px;
            box-sizing: border-box;
            padding: 6px;
            border: 1px solid var(--border);
            border-radius: 3px;
            background: var(--sheet);
        }

        .th { display: block; height: 3px; background: var(--paper-edge); }
        .th-title { height: 5px; width: 60%; background: var(--border); }
        .th-hero { height: 18px; background: var(--border); }
        .th-short { width: 70%; }
        .th-bubble { height: 10px; border-radius: 2px; background: var(--border); }
        .th-small { height: 8px; margin-top: 4px; }
        .th-blank { flex: 1; background: var(--surface); }
        .th-avatar { width: 10px; height: 10px; border-radius: 50%; background: var(--accent); }

        .pd-spacer { flex: 1; }

        .pd-manage {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            min-height: var(--hit-touch);
            padding: 0 var(--space-4);
            border-top: 1px solid var(--paper-edge);
            font-weight: 600;
            color: var(--text);
            text-decoration: none;
        }

        .pd-manage:hover { background: var(--alt); }

        @media (max-width: 1180px) {
            :host { flex-direction: row; align-items: stretch; width: 100%; }
            .pd-caption { display: none; }
            .pd-list { flex: 1; flex-direction: row; overflow: auto hidden; }
            .pd-row {
                flex: 1 0 auto;
                width: auto;
                align-items: center;
                justify-content: center;
                gap: var(--space-2);
                padding: var(--space-2) var(--space-3);
                border-left: 0;
                border-bottom: 3px solid transparent;
            }
            .pd-row.is-on { border-bottom-color: var(--accent); }
            .pd-text { flex: none; align-items: center; }
            .pd-name { font-size: var(--fs-14); white-space: nowrap; }
            .pd-detail { font-size: var(--fs-12); white-space: nowrap; }
            .pd-thumb, .pd-spacer { display: none; }
            .pd-manage { flex: none; padding: 0 var(--space-3); border-top: 0; border-left: 1px solid var(--paper-edge); white-space: nowrap; }
        }

        @media (max-width: 759px) {
            .pd-detail { display: none; }
        }
    `],
})
export class PreviewDestinationsComponent {
    readonly rows = input<readonly DestinationRow[]>([]);
    readonly selected = input<PreviewDestination>('blog');
    readonly pick = output<PreviewDestination>();

    protected readonly t = inject(LocaleService).t;
}
