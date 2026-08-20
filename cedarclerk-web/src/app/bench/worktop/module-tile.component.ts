import { ChangeDetectionStrategy, Component, computed, input, numberAttribute, output } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';
import { IconName } from '../../shared/icon-data.generated';

// A module of the workshop as a small paper plate nailed to the board: an icon, a name, the one
// number worth knowing in mono, and one line of context under it. The hub is a wall of these.
//
// The API is closed on purpose — one icon, one name, one count, one subline, and no content slot.
// The kit's rule is that a tile carries ONE number and that a second one belongs on the screen the
// tile opens; a projection slot is how the second number gets in.
@Component({
    selector: 'app-module-tile',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: {
        'data-surface': 'paper',
        'role': 'button',
        'tabindex': '0',
        '[attr.title]': 'hint() || null',
        '[style.transform]': 'tilt()',
        '(click)': 'activated.emit()',
        '(keydown)': 'onKeydown($event)',
    },
    template: `
        <span class="mt-head">
            <app-icon [name]="icon()" size="sm" />
            <span class="mt-name">{{ name() }}</span>
        </span>
        <span class="mt-count">{{ count() }}</span>
        @if (sub()) {
            <span class="mt-sub">{{ sub() }}</span>
        }
    `,
    styles: [`
        /* The surface is in the selector and not only on the host element: tools/check-density.mjs
           reads declared CSS, so this is what puts its 44px box and its 14px type under it. */
        :host([data-surface="paper"]) {
            display: flex;
            flex-direction: column;
            align-items: flex-start;
            justify-content: center;
            gap: var(--space-1);
            min-width: 0;
            min-height: var(--hit-target);
            box-sizing: border-box;
            padding: var(--space-3) var(--space-4);
            border: var(--border-paper);
            border-radius: var(--radius-paper);
            background-color: var(--sheet);
            background-image: var(--tex-paper);
            font-family: var(--font-sans);
            text-align: left;
            cursor: pointer;

            /* The lift is the whole hover language of the system — 2-3px on --ease-swing, never a
               glow or a scale — and no token family measures an elevation offset, so the one value
               the design system states is named here rather than dropped into the rule. */
            --mt-lift: 2px;

            /* translate rather than transform: the host's transform carries the tile's angle, and
               a hover that wrote transform would straighten the plate on the way up. The duration
               is a motion token, which is the whole of honouring prefers-reduced-motion —
               styles.scss drops those to 1ms globally, and a second media query here would be a
               second answer. */
            transition: translate var(--motion-fast) var(--ease-swing);

            .mt-head {
                display: flex;
                align-items: center;
                gap: var(--space-2);
                min-width: 0;
                max-width: 100%;
                color: var(--accent);
            }

            .mt-name {
                min-width: 0;
                font-family: var(--font-display);
                font-size: var(--fs-body);
                font-weight: 700;
                letter-spacing: .01em;
                color: var(--text);
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }

            .mt-count {
                font-family: var(--font-mono);
                font-size: var(--text-readout);
                line-height: 1.1;
                letter-spacing: -.02em;
                color: var(--text);
                font-variant-numeric: tabular-nums;
            }

            /* The mirror puts this line on --text-faint. ADR-137 rule 5 names this call site: a
               subline is content, and content does not sit on the third tier. */
            .mt-sub {
                max-width: 100%;
                font-family: var(--font-mono);
                font-size: var(--fs-ui);
                color: var(--t2);
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }
        }

        /* ADR-140 spends the app's one box-shadow on the focus halo, and a second on the same
           element replaces it rather than joining it, so the paper's own shadow stands down while
           the tile is focused. */
        :host([data-surface="paper"]:not(:focus-visible)) { box-shadow: var(--shadow-paper-sm); }

        :host([data-surface="paper"]:hover) { translate: 0 calc(var(--mt-lift) * -1); }
    `],
})
export class ModuleTileComponent {
    readonly icon = input.required<IconName>();
    readonly name = input.required<string>();
    /** The one number worth knowing. A string when the module counts in something other than
        integers — a sprint's `S4`, a version, a dash for nothing yet. */
    readonly count = input.required<string | number>();
    /** One line of context under the number. */
    readonly sub = input('');
    /** Degrees. The consumer's to vary across a grid — same tile, same angle every render. */
    readonly rotate = input(0, { transform: numberAttribute });
    /** Native tooltip. Not `title`: a static attribute of that name would survive on the host and
        hang a second tooltip off the tile. */
    readonly hint = input('');

    readonly activated = output<void>();

    // The kit holds a wall of plates to ±0.6°: past that a grid stops reading as hand-nailed and
    // starts reading as broken.
    protected readonly tilt = computed(() => `rotate(${Math.min(0.6, Math.max(-0.6, this.rotate()))}deg)`);

    protected onKeydown(event: KeyboardEvent): void {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        event.preventDefault();
        this.activated.emit();
    }
}
