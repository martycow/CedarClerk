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
        'data-box': 'trim',
        '[class.is-active]': 'state() === "active"',
        '[class.is-dried]': 'state() === "dried"',
        '[class.is-pickable]': 'pickable()',
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
           reads declared CSS, so this is what puts its floors and its type under it. A leaf is a
           chip, so the box it is drawn at is trim and not paper's own (ADR-200); the coarse-pointer
           floor is still spent from the surface, which is why the surface is still named. */
        :host([data-surface="paper"][data-box="trim"]) {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            box-sizing: border-box;
            min-height: var(--hit-trim);
            padding: 0 11px 0 6px;
            border: 1px solid rgba(90, 110, 60, .5);
            border-radius: 2px 12px 2px 12px;
            background-image: linear-gradient(135deg, var(--leaf-bg), var(--leaf-bg-2));
            color: var(--leaf-ink);
            font-family: var(--font-sans);
            font-size: var(--fs-12);
            font-weight: 700;
            white-space: nowrap;

            .lt-pick {
                display: inline-flex;
                align-items: center;
                gap: 5px;
                min-height: var(--hit-trim);
            }

            .lt-pick[role="button"] { cursor: pointer; }

            .lt-swatch {
                flex: none;
                width: 9px;
                height: 9px;
                border-radius: 2px;
                box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--leaf-ink) 40%, transparent);
            }

            .lt-remove {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                min-width: var(--hit-trim);
                min-height: var(--hit-trim);
                padding: 0;
                border: none;
                background: none;
                color: inherit;
                font: inherit;
                cursor: pointer;
            }
        }

        :host([data-surface="paper"][data-box="trim"].is-active) {
            border-color: rgba(90, 110, 60, .65);
            box-shadow: 0 1px 3px rgba(40, 22, 6, .3);
        }

        /* A leaf that can be picked has to show whether it is: the kit tells active from idle by
           border alpha alone, which two RU/EN leaves side by side do not survive. Unpicked, it lies
           on the dried leaf's pale stock with its own green ink and edge; a leaf that only labels
           keeps the green, idle being its only state. */
        :host([data-surface="paper"][data-box="trim"].is-pickable:not(.is-active)) {
            background-image: none;
            background-color: var(--leaf-dried-bg);
        }

        /* Dried means no data or switched off, never an error — an error is a rust stamp, and
           nothing in here reaches for --danger. */
        :host([data-surface="paper"][data-box="trim"].is-dried) {
            background-image: none;
            background-color: var(--leaf-dried-bg, rgba(228, 221, 196, .82));
            border-color: var(--leaf-dried-edge, rgba(120, 110, 70, .5));
            color: var(--leaf-dried-ink, #7A7050);
        }

        /* The remove button owns its own 44px target, so the leaf gives up its right padding
           rather than adding to it. */
        :host([data-surface="paper"][data-box="trim"].has-remove) { padding-right: 0; }
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
