import { booleanAttribute, Component, computed, input, output } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';

export type LeafState = 'active' | 'idle' | 'dried';

// A pinned leaf: one rounded corner pair, moss-green stock, a brass pin on the left. Leaves label
// things (a draft's topic) and filter things (a stats source), and the swatch is what makes a leaf
// double as a chart series key — the legend IS the filter strip.
//
// The pin is projected: `<app-brass-pin pin>` drops into the [pin] slot, and an empty slot is how
// the mirror's pinned={false} ports.
@Component({
    selector: 'app-leaf-tag',
    standalone: true,
    imports: [IconComponent],
    host: {
        'data-surface': 'paper',
        '[class.is-active]': 'state() === "active"',
        '[class.is-dried]': 'state() === "dried"',
        '[class.has-remove]': 'removable()',
        '[attr.title]': 'hint() || null',
    },
    template: `
        <ng-content select="[pin]" />
        <span class="lt-pick"
              [attr.role]="pickable() ? 'button' : null"
              [attr.tabindex]="pickable() ? 0 : null"
              [attr.aria-pressed]="pickable() ? state() === 'active' : null"
              [attr.aria-disabled]="interactive() && !pickable() ? 'true' : null"
              (click)="onPick()" (keydown)="onKeydown($event)">
            @if (swatch()) {
                <span class="lt-swatch" [style.background]="swatch()"></span>
            }
            <ng-content />
        </span>
        @if (removable()) {
            <button type="button" class="lt-remove" [attr.aria-label]="removeLabel() || null"
                    [attr.title]="removeLabel() || null" (click)="onRemove($event)">
                <app-icon name="x" size="xs" />
            </button>
        }
    `,
    styles: [`
        /* The surface is in the selector and not only on the host element: tools/check-density.mjs
           reads declared CSS, so this is what puts its 44px targets and its 14px type under it. */
        :host([data-surface="paper"]) {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            box-sizing: border-box;
            min-height: var(--hit-target);
            padding: 0 var(--space-3) 0 var(--space-2);
            border: 1px solid color-mix(in srgb, var(--leaf-ink) 35%, transparent);
            border-radius: var(--radius-paper) var(--radius-lg) var(--radius-paper) var(--radius-lg);
            background-image: linear-gradient(135deg, var(--leaf-bg), var(--leaf-bg-2));
            color: var(--leaf-ink);
            font-family: var(--font-sans);
            font-size: var(--fs-ui);
            font-weight: 700;
            white-space: nowrap;

            .lt-pick {
                display: inline-flex;
                align-items: center;
                gap: var(--space-1);
                min-height: var(--hit-target);
            }

            .lt-pick[role="button"] { cursor: pointer; }

            .lt-swatch {
                flex: none;
                width: var(--space-2);
                height: var(--space-2);
                border-radius: var(--radius-stamp);
                box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--leaf-ink) 40%, transparent);
            }

            .lt-remove {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                min-width: var(--hit-target);
                min-height: var(--hit-target);
                padding: 0;
                border: none;
                background: none;
                color: inherit;
                font: inherit;
                cursor: pointer;
            }
        }

        :host([data-surface="paper"].is-active) {
            border-color: color-mix(in srgb, var(--leaf-ink) 55%, transparent);
            box-shadow: var(--shadow-paper-sm);
        }

        /* Dried means no data or switched off, never an error — an error is a rust stamp, and
           nothing in here reaches for --danger. --t3 is the app's disabled tier by definition. */
        :host([data-surface="paper"].is-dried) {
            background-image: none;
            background-color: var(--alt);
            border-color: var(--border);
            color: var(--t3);
        }

        /* The remove button owns its own 44px target, so the leaf gives up its right padding
           rather than adding to it. */
        :host([data-surface="paper"].has-remove) { padding-right: 0; }
    `],
})
export class LeafTagComponent {
    /** active = selected filter · idle = available · dried = disabled / no data. */
    readonly state = input<LeafState>('idle');
    /** Gives the leaf a button role and a tab stop; a dried leaf keeps neither. */
    readonly interactive = input(false, { transform: booleanAttribute });
    /**
     * The chart series colour drawn before the label. A token reference (`var(--series-1)`), never
     * a literal — the value is bound straight onto the element, where no contrast pass can read it.
     */
    readonly swatch = input('');
    readonly removable = input(false, { transform: booleanAttribute });
    /** Accessible name for the remove control — the consumer owns the translation. */
    readonly removeLabel = input('');
    readonly hint = input('');

    readonly activated = output<void>();
    readonly removed = output<void>();

    readonly pickable = computed(() => this.interactive() && this.state() !== 'dried');

    onPick(): void {
        if (this.pickable()) this.activated.emit();
    }

    onKeydown(event: KeyboardEvent): void {
        if (!this.pickable() || (event.key !== 'Enter' && event.key !== ' ')) return;
        event.preventDefault();
        this.activated.emit();
    }

    onRemove(event: Event): void {
        event.stopPropagation();
        this.removed.emit();
    }
}
