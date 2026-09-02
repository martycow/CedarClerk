import { ChangeDetectionStrategy, Component, booleanAttribute, computed, inject, input } from '@angular/core';

export type ShelfTone = 'paper' | 'cork';

// A plain card with a caption line (ADR-239 clause 2): the label, the count beside it, and the
// panel's own commands in `[actions]` — the slot is the only door in for a control. A panel may
// never show another panel, refused in the constructor because the check is exact and lexical.
@Component({
    selector: 'app-shelf-panel',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'paper',
        'role': 'region',
        '[attr.aria-label]': 'title()',
    },
    template: `
        <header class="sp-head">
            <span class="sp-name">
                <span class="sp-title label">{{ title() }}</span>
                @if (hasCount()) {
                    <span class="sp-count">{{ count() }}</span>
                }
            </span>
            <span class="sp-spacer"></span>
            <span class="sp-actions"><ng-content select="[actions]" /></span>
        </header>
        <div class="sp-sheet" data-surface="paper"
             [class.is-cork]="tone() === 'cork'" [class.is-flush]="flush()">
            <ng-content />
        </div>
    `,
    styles: [`
        :host {
            display: flex;
            flex-direction: column;
            min-width: 0;
            min-height: 0;
            box-sizing: border-box;
            overflow: hidden;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            background: var(--sheet);
            box-shadow: var(--shadow);
            color: var(--text);
        }

        .sp-head {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            flex: none;
            box-sizing: border-box;
            min-height: var(--hit-touch);
            padding: 0 var(--space-3) 0 var(--space-4);
        }

        .sp-name {
            display: inline-flex;
            align-items: baseline;
            gap: var(--space-2);
            min-width: 0;
        }

        .sp-title { white-space: nowrap; }

        .sp-count {
            font-size: var(--fs-13);
            font-weight: 600;
            font-variant-numeric: tabular-nums;
            color: var(--t3);
            white-space: nowrap;
        }

        .sp-spacer { flex: 1; }

        .sp-actions {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
        }

        .sp-sheet {
            flex: 1;
            min-width: 0;
            min-height: 0;
            overflow: auto;
            box-sizing: border-box;
            padding: 0 var(--space-4) var(--space-4);
        }

        .sp-sheet.is-flush { padding: 0; }

        .sp-sheet.is-cork {
            padding: var(--space-3);
            background-color: var(--cork, #C9A46B);
            background-image: var(--tex-cork);
            color: var(--wood-ink);
        }
    `],
})
export class ShelfPanelComponent {
    readonly title = input.required<string>();
    /** Shown as written: a panel holding nothing still says 0. */
    readonly count = input<string | number | null>(null);
    readonly tone = input<ShelfTone>('paper');
    /** Drop the sheet's inner padding. */
    readonly flush = input(false, { transform: booleanAttribute });

    readonly hasCount = computed(() => this.count() !== null && this.count() !== '');

    constructor() {
        if (inject(ShelfPanelComponent, { optional: true, skipSelf: true })) {
            throw new Error('app-shelf-panel: a shelf carries one sheet — never nest a panel inside another.');
        }
    }
}
