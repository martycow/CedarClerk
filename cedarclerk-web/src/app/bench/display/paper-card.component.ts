import { booleanAttribute, Component, computed, input, numberAttribute, output } from '@angular/core';

// The reading surface every other bench surface sits on: warm stock, paper noise, a real drop
// shadow and a degree or two of rotation so a stack looks handled. Ink never lies on wood
// directly — a paper card comes between.
//
// The brass pin is projected rather than owned: `<app-brass-pin pin>` drops into the [pin] slot.
@Component({
    selector: 'app-paper-card',
    standalone: true,
    host: {
        'data-surface': 'paper',
        '[class.is-interactive]': 'interactive()',
        '[attr.role]': 'interactive() ? "button" : null',
        '[attr.tabindex]': 'interactive() ? 0 : null',
        '(click)': 'onClick()',
        '(keydown)': 'onKeydown($event)',
    },
    template: `
        <div class="pc-drop">
            <div class="pc-sheet" [class.pc-bright]="bright()" [class.pc-cut]="!deckled()"
                 [style.transform]="'rotate(' + angle() + 'deg)'"
                 [style.clip-path]="deckled() ? edge() : null">
                <span class="pc-pin"><ng-content select="[pin]" /></span>
                <ng-content />
            </div>
        </div>
    `,
    styles: [`
        :host([data-surface="paper"]) {
            display: block;

            /* The lift is the whole hover language of the system — 2-3px on --ease-swing, never a
               glow, a blur or a scale. The tokens carry no elevation-offset family, so the one
               value the design system states is named rather than dropped into the rule. */
            --pc-lift: 3px;

            /* A deckled sheet is cut with clip-path, and a clipped box casts no box-shadow at all —
               the sheet's shadow has to be a filter on a wrapper or the torn edge loses its lift. */
            .pc-drop {
                filter: var(--shadow-card-drop);
                transition: transform var(--dur-tap, 150ms) var(--ease-swing);
            }

            .pc-sheet {
                position: relative;
                box-sizing: border-box;
                padding: var(--paper-card-pad, var(--space-4) var(--space-4) calc(var(--space-4) + var(--space-1)));
                background-color: var(--sheet);
                background-image: var(--tex-paper);
                transition: transform var(--dur-tap, 150ms) var(--ease-swing);
            }

            .pc-bright { background-color: var(--paper-bright); }

            /* Square-cut stock: the border is the edge when there is no tear to be the edge. */
            .pc-cut {
                border: var(--border-paper);
                border-radius: var(--radius-paper);
            }

            .pc-pin {
                position: absolute;
                top: calc(var(--space-2) * -1);
                left: 50%;
                translate: -50%;
                z-index: 2;
                display: inline-flex;
            }
        }

        :host([data-surface="paper"].is-interactive) .pc-drop { cursor: pointer; }
        :host([data-surface="paper"].is-interactive:hover) .pc-drop {
            transform: translateY(calc(var(--pc-lift) * -1));
        }

        @media (prefers-reduced-motion: reduce) {
            .pc-drop, .pc-sheet { transition: none; }
        }
    `],
})
export class PaperCardComponent {
    /** Torn bottom edge (drafts, notes); otherwise the sheet is cut square with a warm border. */
    readonly deckled = input(false, { transform: booleanAttribute });
    /** Same seed, same tear. */
    readonly seed = input(7, { transform: numberAttribute });
    /** Brighter stock, for a sheet that is being written on. */
    readonly bright = input(false, { transform: booleanAttribute });
    /** Degrees. Held to -2..2: past that the stack stops looking handled and starts looking broken. */
    readonly rotate = input(-0.5, { transform: numberAttribute });
    /** Gives the card a button role, a tab stop and the hover lift. */
    readonly interactive = input(false, { transform: booleanAttribute });

    readonly activated = output<void>();

    readonly angle = computed(() => Math.min(2, Math.max(-2, this.rotate())));

    readonly edge = computed(() => {
        let x = this.seed();
        const r = () => (x = (x * 9301 + 49297) % 233280) / 233280;
        const pts = ['0% 0%', '100% 0%'];
        for (let p = 100; p >= 0; p -= 5 + Math.floor(r() * 4)) pts.push(`${p}% ${(100 - r() * 2.6).toFixed(1)}%`);
        pts.push('0% 98.6%');
        return `polygon(${pts.join(', ')})`;
    });

    onClick(): void {
        if (this.interactive()) this.activated.emit();
    }

    onKeydown(event: KeyboardEvent): void {
        if (!this.interactive() || (event.key !== 'Enter' && event.key !== ' ')) return;
        event.preventDefault();
        this.activated.emit();
    }
}
