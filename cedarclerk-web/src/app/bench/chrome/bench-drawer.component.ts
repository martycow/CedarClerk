import { ChangeDetectionStrategy, Component, booleanAttribute, input, output } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';

let nextId = 0;

// The console rebuilt as a drawer under the bench: a 32px wooden lip carrying the one line worth
// seeing while shut, and the journal behind it. It belongs closed on writing and reading screens:
// a permanently-open log eats 172px of sheet every session, and the summary on the lip is what
// earns leaving it that way.
//
// The lip is the toggle. The whole strip pulls the drawer except the index tabs hanging off its
// right edge, which switch what the journal shows without opening it — so the pull is an element
// beside them rather than around them, since a tab tile inside the pull button would be a control
// nested in a control.
@Component({
    selector: 'app-bench-drawer',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: {
        'data-surface': 'chrome',
        '[class.is-open]': 'open()',
    },
    template: `
        <div class="lip">
            <button type="button" class="pull" [attr.aria-expanded]="open()" [attr.aria-controls]="journalId"
                    [attr.title]="toggleTitle() || null" (click)="toggled.emit(!open())">
                <span class="knob" aria-hidden="true"></span>
                <span class="title">{{ heading() }}</span>
                @if (summary()) {
                    <span class="summary">{{ summary() }}</span>
                }
            </button>
            <span class="tabs"><ng-content select="[drawerTabs]" /></span>
            <span class="caret" aria-hidden="true"><app-icon name="caret-down" size="xs" /></span>
        </div>

        <div class="journal" data-surface="paper" [id]="journalId" [attr.inert]="open() ? null : ''">
            <div class="rows"><ng-content /></div>
        </div>
    `,
    styles: [`
        :host([data-surface="chrome"]) {
            display: flex;
            flex-direction: column;
            flex: none;
            min-width: 0;

            /* The pull is hardware, not type: no token family measures a drawn object, so the two
               dimensions of the brass grip are named here rather than dropped into the rule. */
            --knob-w: 26px;
            --knob-h: 7px;

            .lip {
                display: flex;
                align-items: stretch;
                min-height: var(--bench-drawer-lip);
                border-top: 1px solid var(--wood-edge);
                background-image: var(--tex-wood), var(--shelf-frame);
                box-shadow: inset 0 1px 0 rgba(255, 240, 210, .14);
            }

            /* Transparent on purpose: the lip carries the material, so the pull carries no shadow
               of its own to out-specify the ADR-140 halo when it takes focus. */
            .pull {
                display: flex;
                flex: 1;
                align-items: center;
                gap: 10px;
                min-width: 0;
                padding: 0 var(--space-3);
                border: none;
                background: none;
                text-align: left;
                cursor: pointer;
            }

            .pull:hover { background: color-mix(in srgb, var(--rail-ink) 10%, transparent); }

            .knob {
                flex: none;
                width: var(--knob-w);
                height: var(--knob-h);
                border: 1px solid var(--brass-edge);
                border-radius: var(--radius-sm);
                background: var(--grad-brass);
                box-shadow: inset 0 1px 0 rgba(255, 248, 225, .7);
            }

            .title {
                flex: none;
                font-family: var(--font-display);
                font-size: var(--text-chrome-sm);
                font-weight: 700;
                letter-spacing: .11em;
                text-transform: uppercase;
                white-space: nowrap;
                color: var(--rail-ink);
                text-shadow: 0 1px 1px color-mix(in srgb, var(--rail-edge) 60%, transparent);
            }

            .summary {
                flex: 1;
                min-width: 0;
                font-family: var(--font-readout);
                font-size: var(--text-chrome-sm);
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
                color: var(--rail-ink-soft);
            }

            /* Scenery at the lip's end, outside the pull: it shows the drawer's state and is not
               a second control for it. */
            .caret {
                flex: none;
                display: inline-flex;
                align-items: center;
                padding: 0 var(--space-3);
                --icon-xs: var(--text-chrome-sm);
                color: var(--rail-ink-soft);
                pointer-events: none;
                transition: transform var(--dur-control, 190ms) var(--ease-settle);
            }

            .tabs {
                flex: none;
                display: inline-flex;
                align-items: flex-end;
            }

            /* The slide is a height, not a display swap: the journal stays in the tree so the
               open has something to animate, and inert keeps its rows out of the tab order and
               off the accessibility tree while the drawer is shut. The duration is a motion
               token, which is the whole of honouring prefers-reduced-motion — styles.scss drops
               those to 1ms globally, and a second media query here would be a second answer. */
            .journal {
                height: 0;
                overflow: hidden;
                border-top: 1px solid var(--paper-edge);
                background-color: var(--paper-2);
                background-image: var(--tex-paper);
                box-shadow: var(--shadow-sheet-inset);
                transition: height var(--motion-base) var(--ease);
            }

            .rows {
                box-sizing: border-box;
                height: 100%;
                padding: var(--space-1) 0;
                overflow-y: auto;
            }
        }

        :host([data-surface="chrome"].is-open) .journal { height: var(--bench-drawer-open); }
        :host([data-surface="chrome"].is-open) .caret { transform: rotate(180deg); }
    `],
})
export class BenchDrawerComponent {
    readonly open = input(false, { transform: booleanAttribute });
    /** The word painted on the lip. Not `title`: a static attribute of that name survives on the
        host element and would hang a native tooltip off the whole drawer. */
    readonly heading = input('');
    /** The one line worth seeing while the drawer is shut — it is what justifies shutting it. */
    readonly summary = input('');
    /** Native tooltip for the pull; the button's accessible name is the lip's title. */
    readonly toggleTitle = input('');

    /** The state being asked for, not the state now: the drawer is controlled by its consumer. */
    readonly toggled = output<boolean>();

    readonly journalId = `bench-drawer-journal-${nextId++}`;
}
