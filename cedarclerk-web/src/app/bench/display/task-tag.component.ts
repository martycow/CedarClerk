import { booleanAttribute, Component, computed, input, numberAttribute, output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Params, QueryParamsHandling, RouterLink } from '@angular/router';

// A luggage tag hanging from a brass hook: clipped top corners, a wood-and-brass eyelet, a
// priority chip, an optional stamp and a due date on the right. This is a task everywhere in the
// product — planner board, sprint shelf, the hub's "up next".
//
// Hook and stamp are projected, not owned: `<app-brass-hook hook>` and `<app-stamp-badge stamp>`
// drop into their slots, and leaving a slot empty is how the mirror's hook={false} ports.
//
// The control wraps the hook and the tag rather than being the host, and it is an anchor whenever
// the consumer hands it a route (ADR-163). It stays outside the clip on purpose: `.tt-tag` cuts its
// own corners, and a clip cuts a focus ring away with them.
@Component({
    selector: 'app-task-tag',
    standalone: true,
    imports: [NgTemplateOutlet, RouterLink],
    host: {
        'data-surface': 'paper',
        '[class.is-done]': 'done()',
    },
    template: `
        @if (link(); as route) {
            <a class="tt-plate" [routerLink]="route" [queryParams]="queryParams()"
               [queryParamsHandling]="queryParamsHandling()" [attr.title]="hint() || null">
                <ng-container [ngTemplateOutlet]="face" />
            </a>
        } @else {
            <button type="button" class="tt-plate" [attr.title]="hint() || null"
                    (click)="activated.emit()">
                <ng-container [ngTemplateOutlet]="face" />
            </button>
        }

        <ng-template #face>
            <span class="tt-hook"><ng-content select="[hook]" /></span>
            <span class="tt-drop">
                <span class="tt-tag" [style.transform]="'rotate(' + angle() + 'deg)'">
                    <span class="tt-eyelet"></span>
                    <span class="tt-body"><ng-content /></span>
                    <span class="tt-meta">
                        @if (prio()) {
                            <span class="tt-prio" [class.tt-p1]="prio() === 1">P{{ prio() }}</span>
                        }
                        <span class="tt-stamp"><ng-content select="[stamp]" /></span>
                        <span class="tt-spacer"></span>
                        @if (due()) {
                            <span class="tt-due" [class.tt-overdue]="overdue()">{{ due() }}</span>
                        }
                    </span>
                </span>
            </span>
        </ng-template>
    `,
    styles: [`
        :host { display: flex; }

        /* The surface is in the selector and not only on the host element: tools/check-density.mjs
           reads declared CSS, so this is what puts its 44px box and its 14px type under it. */
        :host([data-surface="paper"]) .tt-plate {
            display: flex;
            flex: 1;
            flex-direction: column;
            align-items: center;
            min-width: 0;
            padding: 0;
            border: none;
            background: none;
            font: inherit;
            color: inherit;
            text-align: left;
            text-decoration: none;
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
                display: block;
                width: 100%;
                filter: var(--shadow-tag, drop-shadow(0 1px 2px rgba(50, 30, 10, .3)) drop-shadow(0 6px 12px rgba(50, 30, 10, .24)));
            }

            .tt-tag {
                display: block;
                position: relative;
                box-sizing: border-box;
                min-height: var(--hit-target);
                padding: 19px 14px var(--space-3);
                background-color: var(--sheet);
                background-image: var(--tex-paper);
                clip-path: polygon(
                    var(--space-2) 0, calc(100% - var(--space-2)) 0, 100% var(--space-2),
                    100% 100%, 0 100%, 0 var(--space-2));
                transition: transform var(--dur-control, 190ms) var(--ease-swing);
            }

            .tt-eyelet {
                position: absolute;
                top: 5px;
                left: 50%;
                translate: -50%;
                width: 10px;
                height: 10px;
                border-radius: 50%;
                background: var(--wood);
                box-shadow:
                    inset 0 1px 2px rgba(30, 16, 4, .6),
                    0 0 0 2px var(--brass),
                    0 0 0 3px var(--brass-edge);
            }

            .tt-body {
                display: block;
                font-family: var(--font-sans);
                font-size: var(--fs-ui);
                font-weight: 600;
                line-height: 1.35;
                color: var(--text);
            }

            .tt-meta {
                display: flex;
                flex-wrap: wrap;
                align-items: center;
                gap: var(--space-2);
                margin-top: var(--space-2);
            }

            /* A stamp inside a tag is pressed smaller; the hooks inherit into the projected badge. */
            .tt-stamp {
                display: inline-flex;
                --stamp-pad: 1px 6px;
                --stamp-border-w: 1px;
                --stamp-tracking: .1em;
            }
            .tt-spacer { flex: 1; }

            /* The chip is a small plaque, so its ink is the cream that goes on wood. It is cut from
               the two dark wood steps and not the mirror's lit ones: cream on --wood-hi is 2.4:1,
               and this label is read rather than looked at. No min-width: paper's 44px floor is a
               control's floor, and a chip is not a control — the padding is what makes it a plaque. */
            .tt-prio {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                height: 18px;
                padding: 0 var(--space-2);
                border: 1px solid var(--wood-edge);
                border-radius: var(--radius-stamp);
                background: linear-gradient(180deg, var(--wood-lo), var(--wood-edge));
                color: var(--rail-ink);
                text-shadow: 0 1px 1px rgba(40, 22, 6, .5);
                box-shadow: 0 1px 0 var(--wood-edge);
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
    /**
     * The screen this tag opens; anything routerLink takes. A destination is a link, so a middle
     * click opens it in a tab (ADR-163). Empty is a tag whose activation is not a navigation — a
     * board card opening an inspector beside it — and that one is a button speaking through
     * `activated`.
     */
    readonly link = input<string | readonly unknown[] | null>(null);
    /** A task is addressed by a query everywhere in the app, never by a path segment. */
    readonly queryParams = input<Params | null>(null);
    /** Unset, the tag replaces the query set; `'merge'` keeps a filter the screen already carries beside it (ADR-260). */
    readonly queryParamsHandling = input<QueryParamsHandling | null>(null);

    /** Silent in link form — there the anchor is the navigation. */
    readonly activated = output<void>();

    readonly angle = computed(() => Math.min(2, Math.max(-2, this.rotate())));
}
