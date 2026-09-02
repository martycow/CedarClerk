import { ChangeDetectionStrategy, Component, OnDestroy, booleanAttribute, input, isDevMode } from '@angular/core';

/** The ground the top is cut from: paper, or the wall the page stands against. */
export type WorktopTone = 'paper' | 'wall';
export type WorktopEdge = 'strip' | 'chips';

let liveTops = 0;

// The page's content column (ADR-239 clause 2): a card with an optional caption line carrying the
// two labels a screen used to chalk on its edge. The frame's line is chrome; what lies in it is
// paper, and the body says so itself so that a sheet inside is not governed by chrome's numbers.
@Component({
    selector: 'app-worktop',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'chrome',
        '[attr.data-tone]': 'tone()',
        '[attr.data-edge]': 'edge()',
        '[class.scrolls]': 'scroll()',
    },
    template: `
        @if (label() || meta()) {
            <div class="wt-edge">
                @if (label()) { <span class="wt-label label">{{ label() }}</span> }
                <span class="wt-span"></span>
                @if (meta()) { <span class="wt-meta">{{ meta() }}</span> }
            </div>
        }
        <div class="wt-body" data-surface="paper"><ng-content /></div>
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
            background-color: var(--sheet);
            color: var(--text);
        }

        :host(:not(:focus-visible)) { box-shadow: var(--shadow); }

        .wt-edge {
            flex: none;
            display: flex;
            align-items: center;
            gap: 10px;
            box-sizing: border-box;
            min-height: var(--hit-chrome);
            padding: 0 var(--space-4);
            border-bottom: 1px solid var(--paper-edge);
            color: var(--t2);
        }

        .wt-label, .wt-meta {
            min-width: 0;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .wt-span { flex: 1; }

        :host([data-surface="chrome"]) .wt-meta { font-size: var(--text-chrome); }

        :host([data-tone="wall"]) { background-color: var(--surface); }

        .wt-body {
            flex: 1;
            min-width: 0;
            min-height: 0;
            display: flex;
            flex-direction: column;
        }

        :host(.scrolls) .wt-body { overflow: auto; }
    `],
})
export class WorktopComponent implements OnDestroy {
    /** Caption on the top edge, left. */
    readonly label = input('');
    /** The same line, right — zoom, width, language. */
    readonly meta = input('');
    /** Kept for the pages that still name them; the card draws neither (ADR-239 clause 2). */
    readonly grid = input(true, { transform: booleanAttribute });
    readonly lamp = input(true, { transform: booleanAttribute });
    readonly tone = input<WorktopTone>('paper');
    readonly edge = input<WorktopEdge>('strip');
    /** The body scrolls what it holds instead of clipping it. */
    readonly scroll = input(false, { transform: booleanAttribute });

    constructor() {
        if (++liveTops > 1 && isDevMode())
            console.warn('app-worktop: a bench has one top — a second worktop is on screen');
    }

    ngOnDestroy(): void {
        liveTops--;
    }
}
