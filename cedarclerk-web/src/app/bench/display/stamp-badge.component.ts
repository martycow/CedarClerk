import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type StampTone = 'pine' | 'brass' | 'rust' | 'ink';

// A rubber stamp says what STATE a thing is in and never acts — it is not a button and takes no
// click. It is chrome rather than paper (ADR-138) because its type is --text-chrome-sm: paper's
// floor is 14px, and a stamp at 14px stops being a stamp.
@Component({
    selector: 'app-stamp-badge',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'chrome',
        '[attr.data-tone]': 'tone()',
        '[style.transform]': 'spin()',
    },
    template: `<ng-content />`,
    styles: [`
        :host {
            display: inline-flex;
            align-items: center;
            white-space: nowrap;
            border: 2px solid currentColor;
            border-radius: var(--radius-stamp);
            font-family: var(--font-display);
            font-weight: 700;
            letter-spacing: .13em;
            text-transform: uppercase;
            opacity: .92;
            color: var(--accent);
            background: var(--asoft);
        }

        /* The surface owns the size, so the lint can score it (ADR-138). */
        :host([data-surface="chrome"]) {
            padding: calc(var(--space-1) / 2) var(--space-2);
            font-size: var(--text-chrome-sm);
        }

        /* Ink and wash are one pair, so each tone takes the wash mixed from its own ink
           (ADR-145) rather than a hand-picked percentage over whatever paper it lands on. */
        :host([data-tone="brass"]) {
            color: var(--brass-lo);
            background: var(--brass-soft);
        }

        :host([data-tone="rust"]) {
            color: var(--danger);
            background: var(--danger-soft);
        }

        /* The mirror puts the neutral tone on --text-faint; a stamp carries a word, and content
           does not sit on the third tier (ADR-137 rule 5). */
        :host([data-tone="ink"]) {
            color: var(--t2);
            background: transparent;
        }
    `],
})
export class StampBadgeComponent {
    readonly tone = input<StampTone>('pine');
    /** Degrees. A stamp is never pressed perfectly straight. */
    readonly rotate = input(-2);

    protected readonly spin = computed(() => `rotate(${this.rotate()}deg)`);
}
