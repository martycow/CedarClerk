import { ChangeDetectionStrategy, Component, booleanAttribute, computed, effect, input, isDevMode, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BrassHookComponent } from '../scenery/brass-hook.component';
import { IconComponent } from '../../shared/icon.component';
import { IconName } from '../../shared/icon-data.generated';

export interface HookRailItem {
    id: string;
    icon: IconName;
    /** Visible caption, and the hook's accessible name whenever it is set. */
    label?: string;
    /** Tooltip. It becomes the accessible name only for a hook with no caption. */
    title?: string;
    /** Anything routerLink takes. A hook is a link, so a middle click opens it in a tab. */
    link: string | readonly unknown[];
    /** Pins the hook to the bottom of the wall, the way the kit anchors Settings. */
    end?: boolean;
}

/** The legibility limit HookRail.prompt.md sets: "a wall with everything on it is a wall you stop reading." */
const MAX_HOOKS = 7;

// The tool wall: a pegboard strip down the left edge where the screens hang from brass hooks.
// It is how the user moves between screens, so each hook is a real link and the current one is
// marked three ways that survive a colourblind eye — aria-current, a raised sign tile, and a
// brass bar on the wall's inner edge.
//
// The active hook comes in as `value` rather than being read off routerLinkActive: one hook
// covers two paths (Text is /drafts and /editor) and another a child path, so the shell computes
// it from a route-prefix table (ADR-139).
@Component({
    selector: 'app-hook-rail',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [RouterLink, IconComponent, BrassHookComponent],
    host: {
        'data-surface': 'chrome',
        'role': 'navigation',
        '[attr.aria-label]': 'label()',
    },
    template: `
        <ul class="wall">
            @for (item of items(); track item.id) {
                <li [class.is-tail]="item.end">
                    <a class="hook" [class.is-current]="item.id === value()"
                       [routerLink]="item.link"
                       [attr.aria-current]="item.id === value() ? 'page' : null"
                       [attr.title]="item.title || item.label || null"
                       [attr.aria-label]="item.label ? null : (item.title || null)"
                       (click)="picked.emit(item.id)">
                        @if (hooks()) { <app-brass-hook class="peg" /> }
                        <app-icon [name]="item.icon" size="sm" />
                        @if (item.label) { <span class="cap">{{ item.label }}</span> }
                        <span class="here" aria-hidden="true"></span>
                    </a>
                </li>
            }
        </ul>
    `,
    styles: [`
        :host([data-surface="chrome"]) {
            display: flex;
            flex: none;
            box-sizing: border-box;
            width: var(--bench-tool-w);
            background: var(--pegboard);
            border-right: 2px solid var(--rail-edge);
            box-shadow: var(--shadow-rail);
            font-family: var(--font-sans);
            color: var(--rail-ink);
        }

        :host([data-surface="chrome"]) .wall {
            display: flex;
            flex: 1;
            flex-direction: column;
            align-items: center;
            gap: var(--space-3);
            margin: 0;
            padding: var(--space-3) 0;
            list-style: none;
        }

        /* One list, bottom-anchored by margin rather than by a second list: a nav split in two
           announces two navigations. */
        :host([data-surface="chrome"]) li.is-tail { margin-top: auto; }

        :host([data-surface="chrome"]) .hook {
            position: relative;
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: var(--space-1);
            box-sizing: border-box;
            width: calc(var(--bench-tool-w) - var(--space-2));
            min-height: var(--hit-chrome);
            padding: var(--space-1) 0;
            border: var(--border-rail-btn);
            border-radius: var(--radius-stamp);
            background: var(--hook-face);
            color: var(--rail-ink);
            text-decoration: none;
        }

        :host([data-surface="chrome"]) .hook:hover { background: var(--rail-lo); }

        /* The hook hangs over the top edge of the tool, which is why the tool carries no margin
           of its own up there — the peg is drawn outside the box. */
        :host([data-surface="chrome"]) .peg {
            position: absolute;
            top: calc(var(--space-3) * -1);
            left: 50%;
            translate: -50%;
        }

        /* Chrome type is 11px at its smallest, so the kit's 8.5px uppercase tracking does not
           survive the port: a tracked caps caption at 11px runs past a 52px wall. Regular case,
           and a caption too long for the wall is clipped rather than shrunk. */
        :host([data-surface="chrome"]) .cap {
            max-width: 100%;
            overflow: hidden;
            font-size: var(--text-chrome-sm);
            font-weight: 600;
            line-height: 1.2;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        :host([data-surface="chrome"]) .hook.is-current {
            border-color: var(--brass-edge);
            background: var(--grad-sign-tile);
            font-weight: 700;
        }

        :host([data-surface="chrome"]) .hook.is-current:not(:focus-visible) { box-shadow: var(--shadow-shelf); }
        :host([data-surface="chrome"]) .hook.is-current .cap { font-weight: 700; }

        /* Third cue, and the one that is a shape rather than a colour: a brass bar standing on the
           wall's inner edge under the current tool. */
        :host([data-surface="chrome"]) .here { display: none; }

        :host([data-surface="chrome"]) .hook.is-current .here {
            display: block;
            position: absolute;
            top: var(--space-1);
            bottom: var(--space-1);
            right: calc(var(--space-1) * -1);
            width: var(--space-1);
            border-radius: var(--radius-stamp);
            background: var(--grad-brass);
        }
    `],
})
export class HookRailComponent {
    readonly items = input<readonly HookRailItem[]>([]);
    /** Id of the hook standing for the screen on show. */
    readonly value = input('');
    /** Accessible name of the navigation landmark; the consumer's to translate. */
    readonly label = input('Screens');
    /** False drops the brass hooks, the way the mirror's `hooks={false}` does. */
    readonly hooks = input(true, { transform: booleanAttribute });

    readonly picked = output<string>();

    private readonly overBudget = computed(() => this.items().length > MAX_HOOKS);

    constructor() {
        effect(() => {
            if (!this.overBudget() || !isDevMode()) return;
            console.warn(`app-hook-rail: ${this.items().length} hooks — the wall holds ${MAX_HOOKS}.`);
        });
    }
}
