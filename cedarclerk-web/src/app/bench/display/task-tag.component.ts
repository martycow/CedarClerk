import { booleanAttribute, Component, computed, input, numberAttribute, output } from '@angular/core';

// A luggage tag hanging from a brass hook: clipped top corners, a wood-and-brass eyelet, a
// priority chip, an optional stamp and a due date on the right. This is a task everywhere in the
// product — planner board, sprint shelf, the hub's "up next".
//
// Hook and stamp are projected, not owned: `<app-brass-hook hook>` and `<app-stamp-badge stamp>`
// drop into their slots, and leaving a slot empty is how the mirror's hook={false} ports.
@Component({
    selector: 'app-task-tag',
    standalone: true,
    host: {
        'data-surface': 'paper',
        'role': 'button',
        'tabindex': '0',
        '[class.is-done]': 'done()',
        '[attr.title]': 'hint() || null',
        '(click)': 'activated.emit()',
        '(keydown)': 'onKeydown($event)',
    },
    template: `
        <span class="tt-hook"><ng-content select="[hook]" /></span>
        <div class="tt-drop">
            <div class="tt-tag" [style.transform]="'rotate(' + angle() + 'deg)'">
                <span class="tt-eyelet"></span>
                <div class="tt-body"><ng-content /></div>
                <div class="tt-meta">
                    @if (prio()) {
                        <span class="tt-prio" [class.tt-p1]="prio() === 1">P{{ prio() }}</span>
                    }
                    <span class="tt-stamp"><ng-content select="[stamp]" /></span>
                    <span class="tt-spacer"></span>
                    @if (due()) {
                        <span class="tt-due" [class.tt-overdue]="overdue()">{{ due() }}</span>
                    }
                </div>
            </div>
        </div>
    `,
    styles: [`
        /* The surface is in the selector and not only on the host element: tools/check-density.mjs
           reads declared CSS, so this is what puts its 44px box and its 14px type under it. */
        :host([data-surface="paper"]) {
            display: flex;
            flex-direction: column;
            align-items: center;
            cursor: grab;

            .tt-hook {
                display: inline-flex;
                position: relative;
                z-index: 1;
                margin-bottom: calc(var(--space-2) * -1);
            }

            /* Clipped corners mean the tag itself can carry no box-shadow — a clip cuts the shadow
               away with the corner. The lift lives on a wrapper as a filter instead. */
            .tt-drop {
                width: 100%;
                filter: var(--shadow-card-drop);
            }

            .tt-tag {
                position: relative;
                box-sizing: border-box;
                min-height: var(--hit-target);
                padding: var(--space-5) var(--space-3) var(--space-3);
                background-color: var(--sheet);
                background-image: var(--tex-paper);
                clip-path: polygon(
                    var(--space-2) 0, calc(100% - var(--space-2)) 0, 100% var(--space-2),
                    100% 100%, 0 100%, 0 var(--space-2));
                transition: transform var(--motion-fast) var(--ease-swing);
            }

            .tt-eyelet {
                position: absolute;
                top: var(--space-1);
                left: 50%;
                translate: -50%;
                width: var(--space-3);
                height: var(--space-3);
                border-radius: 50%;
                background: var(--wood);
                box-shadow:
                    inset 0 1px 2px var(--wood-edge),
                    0 0 0 2px var(--brass),
                    0 0 0 3px var(--brass-edge);
            }

            .tt-body {
                font-family: var(--font-sans);
                font-size: var(--fs-ui);
                font-weight: 600;
                line-height: 1.35;
                color: var(--text);
            }

            .tt-meta {
                display: flex;
                align-items: center;
                gap: var(--space-2);
                margin-top: var(--space-2);
            }

            .tt-stamp { display: inline-flex; }
            .tt-spacer { flex: 1; }

            /* The chip is a small plaque, so its ink is the cream that goes on wood. It is cut from
               the two dark wood steps and not the mirror's lit ones: cream on --wood-hi is 2.4:1,
               and this label is read rather than looked at. */
            .tt-prio {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                padding: 0 var(--space-2);
                border: 1px solid var(--wood-edge);
                border-radius: var(--radius-stamp);
                background: linear-gradient(180deg, var(--wood-lo), var(--wood-edge));
                color: var(--rail-ink);
                font-family: var(--font-mono);
                font-size: var(--fs-ui);
                font-weight: 700;
            }

            /* Priority one is resin-filled, which flips the ink dark: resin is the lightest metal
               in the palette and it is one of the few that does not move at night. */
            .tt-p1 {
                background: linear-gradient(180deg, var(--resin-hi), var(--resin));
                color: var(--rail-edge);
            }

            .tt-due {
                font-family: var(--font-mono);
                font-size: var(--fs-ui);
                font-weight: 600;
                color: var(--t2);
                white-space: nowrap;
            }

            /* Only the date turns rust. Never the tag, never the rail it hangs on. */
            .tt-due.tt-overdue {
                color: var(--danger);
                font-weight: 700;
            }
        }

        /* Fades rather than strikes through: a struck-out task is unreadable, a faded one is done. */
        :host([data-surface="paper"].is-done) .tt-tag { opacity: .62; }

        @media (prefers-reduced-motion: reduce) {
            .tt-tag { transition: none; }
        }
    `],
})
export class TaskTagComponent {
    /** 1 = highest — matches the app's three priorities. */
    readonly prio = input<1 | 2 | 3 | null>(null);
    readonly due = input('');
    readonly overdue = input(false, { transform: booleanAttribute });
    readonly done = input(false, { transform: booleanAttribute });
    /** Degrees. Held to -2..2, the same range the paper card is held to. */
    readonly rotate = input(-0.8, { transform: numberAttribute });
    /** Title text — the drag/drop affordance is described here, so it is the consumer's to translate. */
    readonly hint = input('');

    readonly activated = output<void>();

    readonly angle = computed(() => Math.min(2, Math.max(-2, this.rotate())));

    onKeydown(event: KeyboardEvent): void {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        event.preventDefault();
        this.activated.emit();
    }
}
