import { ChangeDetectionStrategy, Component, booleanAttribute, computed, inject, input } from '@angular/core';

export type ShelfTone = 'paper' | 'cork';

// A shelf board with a carved sign tile for a header and a paper sheet set into it. Every dock,
// sidebar and inspector on the bench is one of these.
//
// Two rules from ShelfPanel.prompt.md are structural, so both are put where they cannot be missed
// rather than left to prose. A panel's own commands go in `[actions]` and never in a global
// toolbar, so the header carries no command input at all — the slot is the only door in. And a
// panel may never show another panel: one board, one sheet, refused in the constructor because the
// check is exact and lexical, so nesting cannot survive a first render to be caught in review.
//
// The board and its header are chrome, the sheet is paper (ADR-138 item 1). The sheet spells its
// own surface so that projected content lands on paper's numbers, and names no type size, since
// inherited type is the one leak encapsulation cannot stop.
@Component({
    selector: 'app-shelf-panel',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'chrome',
        'role': 'region',
        '[attr.aria-label]': 'title()',
    },
    template: `
        <header class="sp-head">
            <span class="sp-title">{{ title() }}</span>
            @if (hasCount()) {
                <span class="sp-count">{{ count() }}</span>
            }
            <span class="sp-spacer"></span>
            <span class="sp-actions"><ng-content select="[actions]" /></span>
        </header>
        <div class="sp-sheet" data-surface="paper"
             [class.is-cork]="tone() === 'cork'" [class.is-flush]="flush()">
            <ng-content />
        </div>
    `,
    styles: [`
        :host([data-surface="chrome"]) {
            display: flex;
            flex-direction: column;
            min-width: 0;
            min-height: 0;
            box-sizing: border-box;
            padding: var(--space-1);
            border: 1px solid var(--wood-edge);
            border-radius: var(--radius-sm);
            background: var(--shelf-frame);
            box-shadow: var(--shadow-shelf);
        }

        :host([data-surface="chrome"]) .sp-head {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            flex: none;
            box-sizing: border-box;
            min-height: var(--bench-panel-hd);
            padding: 0 var(--space-2) 0 var(--space-3);
            border-bottom: 1px solid var(--wood-edge);
            background-color: var(--sign-tile-hi);
            background-image: var(--tex-wood), var(--grad-sign-tile);
            box-shadow: inset 0 1px 0 color-mix(in srgb, var(--rail-ink) 15%, transparent);
        }

        :host([data-surface="chrome"]) .sp-head .sp-title {
            font-family: var(--font-display);
            font-size: var(--text-chrome-sm);
            font-weight: 700;
            letter-spacing: .11em;
            text-transform: uppercase;
            white-space: nowrap;
            color: var(--rail-ink);
            text-shadow: 0 1px 1px color-mix(in srgb, var(--rail-edge) 60%, transparent);
        }

        /* Cream at full strength: the soft cream measures 3.6:1 on the lit stop of the tile, and a
           counter is read. The mono face against the title's display caps is what sets it back. */
        :host([data-surface="chrome"]) .sp-head .sp-count {
            font-family: var(--font-mono);
            font-size: var(--text-chrome-sm);
            color: var(--rail-ink);
            white-space: nowrap;
        }

        :host([data-surface="chrome"]) .sp-spacer { flex: 1; }

        :host([data-surface="chrome"]) .sp-actions {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
        }

        /* Outside every chrome-scoped rule above on purpose: this is the paper half of the density
           contract, and it names no type size so nothing inherits a chrome measurement across the
           slot. */
        .sp-sheet[data-surface="paper"] {
            flex: 1;
            min-width: 0;
            min-height: 0;
            overflow: auto;
            box-sizing: border-box;
            padding: var(--space-3);
            background-color: var(--sheet);
            background-image: var(--tex-paper);
            box-shadow: var(--shadow-sheet-inset);
        }

        /* Lists and tables that rule their own rows to the panel's edge. */
        .sp-sheet.is-flush { padding: 0; }

        /* Cork has no colour token of its own; the board takes the lit wood face, which is what
           keeps it moving with the theme instead of staying a light-mode tan on a dark bench. */
        .sp-sheet.is-cork {
            background-color: var(--wood-hi);
            background-image: var(--tex-cork);
            color: var(--wood-ink);
        }
    `],
})
export class ShelfPanelComponent {
    /** Carved into the header tile — uppercase display serif, wide tracking. */
    readonly title = input.required<string>();
    /** Mono counter beside the title. Shown as written: a panel holding nothing still says 0. */
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
