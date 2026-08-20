import { ChangeDetectionStrategy, Component, OnDestroy, booleanAttribute, computed, input, isDevMode } from '@angular/core';

/** The ground the top is cut from: squared paper, or the plaster the bench stands against. */
export type WorktopTone = 'paper' | 'wall';

let liveTops = 0;

// The viewport as the lit top of the bench: squared paper under a lamp, a chalked strip along its
// edge, and one object lying on it. The frame is chrome; what lies on it is paper, and the body
// says so itself so that a sheet inside a chrome box is not governed by chrome's numbers.
@Component({
    selector: 'app-worktop',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'chrome',
        '[attr.data-tone]': 'tone()',
        '[class.scrolls]': 'scroll()',
        '[style.--wt-lamp]': `lamp() ? 'var(--lamp)' : null`,
        '[style.--wt-grid]': 'gridLayer()',
    },
    template: `
        @if (label() || meta()) {
            <div class="wt-edge">
                @if (label()) { <span class="wt-label">{{ label() }}</span> }
                <span class="wt-span"></span>
                @if (meta()) { <span class="wt-meta">{{ meta() }}</span> }
            </div>
        }
        <div class="wt-body" data-surface="paper"><ng-content /></div>
    `,
    styles: [`
        :host {
            position: relative;
            display: flex;
            flex-direction: column;
            min-width: 0;
            min-height: 0;
            box-sizing: border-box;
            overflow: hidden;
            border: 1px solid var(--wood-edge);
            border-radius: var(--radius-sm);
            background-color: var(--surface);
            /* Lamp over rules over stock, and every layer is the token the checker scores ink
               against — a rule drawn here instead would be a surface nothing measures. */
            background-image: var(--wt-lamp, none), var(--wt-grid, none), var(--tex-paper);
        }

        /* ADR-140 spends the app's one box-shadow on the focus halo, and a second on the same
           element replaces it rather than joining it, so the inset stands down while focused. */
        :host(:not(:focus-visible)) { box-shadow: var(--shadow-sheet-inset); }

        .wt-edge {
            flex: none;
            display: flex;
            align-items: center;
            gap: var(--space-2);
            box-sizing: border-box;
            height: var(--bench-panel-hd);
            padding: 0 var(--space-3);
            border-bottom: 1px dashed var(--rule-ink);
            font-family: var(--font-mono);
            letter-spacing: .06em;
            text-transform: uppercase;
            /* Sheet width, zoom, language and word count are content, not decoration, so the
               strip takes the measured secondary ink rather than the faintest tier (ADR-074). */
            color: var(--t2);
        }

        .wt-label, .wt-meta {
            min-width: 0;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .wt-span { flex: 1; }

        /* The surface owns the size, so the lint can score it (ADR-138). */
        :host([data-surface="chrome"]) .wt-edge { font-size: var(--text-chrome-sm); }

        /* Plaster, not paper: the wall carries one measured ink and no pencil rules. */
        :host([data-tone="wall"]) { background-color: var(--canvas); }
        :host([data-tone="wall"]) .wt-edge { color: var(--wood-ink); }

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
    /** Chalked label on the top edge, left. */
    readonly label = input('');
    /** The same strip, right — zoom, width, language. */
    readonly meta = input('');
    /** Pencil-squared rules; off for a plain surface. */
    readonly grid = input(true, { transform: booleanAttribute });
    /** The warm lamp wash from one corner. */
    readonly lamp = input(true, { transform: booleanAttribute });
    readonly tone = input<WorktopTone>('paper');
    /** The body scrolls what it holds instead of clipping it. */
    readonly scroll = input(false, { transform: booleanAttribute });

    // Ink is scored over the rules on paper and nowhere else, so a ruled wall would be a surface
    // no pair in check-contrast can name.
    readonly gridLayer = computed(() => (this.grid() && this.tone() === 'paper' ? 'var(--grid-worktop)' : null));

    constructor() {
        if (++liveTops > 1 && isDevMode())
            console.warn('app-worktop: a bench has one top — a second worktop is on screen');
    }

    ngOnDestroy(): void {
        liveTops--;
    }
}
