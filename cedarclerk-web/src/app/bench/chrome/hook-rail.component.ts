import { ChangeDetectionStrategy, Component, ElementRef, booleanAttribute, inject, input, output, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BrassHookComponent } from '../scenery/brass-hook.component';
import { IconComponent } from '../../shared/icon.component';
import { IconName } from '../../shared/icon-data.generated';
import { indexTabBadgeLabel } from './index-tabs.component';

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
    /**
     * What is waiting behind that screen, hung off the tool as a tally. A number follows the app's
     * counter rules — nothing at zero or below, `99+` past ninety-nine. A string is drawn as written.
     */
    badge?: number | string;
    /** Says what the tally counts, which the number alone cannot. */
    badgeTitle?: string;
}

// The tool wall: a pegboard strip down the left edge where the screens hang from brass hooks.
// It is how the user moves between screens, so each hook is a real link and the current one is
// marked three ways that survive a colourblind eye — aria-current, a raised sign tile, and a
// brass bar on the wall's inner edge.
//
// The active hook comes in as `value` rather than being read off routerLinkActive: one hook
// covers two paths (Text is /drafts and /editor) and another a child path, so the shell computes
// it from a route-prefix table (ADR-139).
//
// At the foot of the wall, under the tail hook, hangs the tray for display preferences and
// development surfaces. The navigation list scrolls independently when the active context has
// more tools than fit on the wall.
@Component({
    selector: 'app-hook-rail',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [RouterLink, IconComponent, BrassHookComponent],
    host: {
        'data-surface': 'chrome',
        'role': 'navigation',
        '[attr.aria-label]': 'label()',
        '(document:click)': 'onDocumentClick($event)',
        '(document:keydown.escape)': 'onEscape()',
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
                        @if (tally(item); as count) {
                            <span class="tally" [attr.title]="item.badgeTitle || null"
                                  [attr.aria-label]="item.badgeTitle ? count + ' ' + item.badgeTitle : null">{{ count }}</span>
                        }
                    </a>
                </li>
            }
        </ul>

        @if (hasTray()) {
            <div class="tray">
                <button #trayTrigger type="button" class="dots" [attr.aria-label]="trayLabel()"
                        aria-haspopup="true" [attr.aria-expanded]="trayOpen()" (click)="toggleTray()">
                    <app-icon name="dots-three" size="sm" />
                </button>
                <!-- Never behind @if: the slot is what holds the projected controls, and a control
                     destroyed on close loses whatever state it was carrying. -->
                <div #trayPanel class="tray-panel" role="group" [attr.aria-label]="trayLabel()"
                     [hidden]="!trayOpen()">
                    <ng-content select="[tray]" />
                </div>
            </div>
        }
    `,
    styles: [`
        :host([data-surface="chrome"]) {
            position: relative;
            display: flex;
            flex-direction: column;
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
            min-height: 0;
            flex-direction: column;
            align-items: center;
            gap: 6px;
            margin: 0;
            padding: var(--space-3) var(--space-1);
            /* Clipped across, scrolled down: a hook is drawn at the wall's full width, so the
               moment the vertical bar appears it takes the width back and the wall grows a second,
               horizontal bar under the last tool. */
            overflow: hidden auto;
            overscroll-behavior: contain;
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
            gap: 2px;
            box-sizing: border-box;
            width: 100%;
            max-width: calc(var(--bench-tool-w) - var(--space-1));
            min-height: var(--hit-chrome);
            padding: 5px 0 4px;
            border: 1px solid rgba(242, 232, 206, .16);
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
            top: -11px;
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
            background-color: var(--sign-tile-hi);
            background-image: var(--tex-wood), var(--grad-sign-tile);
            background-size: 420px, auto;
            font-weight: 700;
        }

        :host([data-surface="chrome"]) .hook.is-current:not(:focus-visible) {
            box-shadow: inset 0 1px 0 rgba(255, 240, 210, .2), 0 1px 2px rgba(20, 12, 4, .45);
        }
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

        /* A work ticket hung on the tool's own peg and notched over its top corner — the wall says
           a station has something waiting the way a workshop does, with a tag rather than a light.
           It rides above the hook because a ticket hangs in front of the hardware, and it takes the
           index tabs' ground and ink so chrome carries one badge instead of two dialects.
           No min-height: chrome's 30px floor is a control's floor, and a tally is not a control. */
        :host([data-surface="chrome"]) .tally {
            position: absolute;
            top: calc(var(--space-2) * -1);
            right: 0;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 0 var(--space-1);
            border: 1px solid var(--brass-edge);
            border-radius: var(--radius-stamp);
            background: var(--tab-badge);
            color: var(--rail-edge);
            font-family: var(--font-readout);
            font-size: var(--text-chrome-sm);
            font-weight: 700;
            line-height: 1.5;
        }

        /* The foot of the wall. The panel opens sideways rather than upward: the rule and the
           drawer lip are under this edge, and a menu growing over them would be a paper sheet on
           top of the chrome it belongs beside. */
        :host([data-surface="chrome"]) .tray {
            position: relative;
            flex: none;
            display: flex;
            justify-content: center;
            padding: var(--space-2) 0 var(--space-3);
        }

        :host([data-surface="chrome"]) .dots {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 100%;
            max-width: calc(var(--bench-tool-w) - var(--space-1));
            min-height: var(--hit-chrome);
            border: var(--border-rail-btn);
            border-radius: var(--radius-stamp);
            background: var(--hook-face);
            color: var(--rail-ink);
            cursor: pointer;
        }

        :host([data-surface="chrome"]) .dots:hover { background: var(--rail-lo); }

        /* Paper, pinned to the wall: the rail's ink rule governs the pegboard, not what hangs off
           it. */
        :host([data-surface="chrome"]) .tray-panel {
            position: absolute;
            bottom: var(--space-2);
            left: calc(100% + var(--space-2));
            z-index: 2;
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
            padding: var(--space-2);
            border: var(--border-paper);
            border-radius: var(--radius-plaque);
            background-color: var(--sheet);
            background-image: var(--tex-paper);
            box-shadow: var(--shadow-paper);
            color: var(--text);
        }

        :host([data-surface="chrome"]) .tray-panel[hidden] { display: none; }
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
    /** Accessible name of the tray control; the consumer's to translate. */
    readonly trayLabel = input('More');
    /** False drops the tray button; the `[tray]` slot then holds nothing worth opening. */
    readonly hasTray = input(true, { transform: booleanAttribute });

    readonly picked = output<string>();
    readonly trayOpenChange = output<boolean>();

    private readonly el = inject(ElementRef<HTMLElement>);
    private readonly trayTrigger = viewChild<ElementRef<HTMLButtonElement>>('trayTrigger');
    private readonly trayPanel = viewChild<ElementRef<HTMLElement>>('trayPanel');

    private readonly open = signal(false);
    readonly trayOpen = this.open.asReadonly();

    toggleTray(): void { this.setTray(!this.open()); }

    closeTray(): void { this.setTray(false); }

    // The menu-button pattern: Escape closes and hands focus back to the button. Hiding the panel
    // first would drop focus on <body> — [hidden] takes the focused entry out of the tree — so the
    // question of whether we hold focus at all is asked before the panel goes.
    onEscape(): void {
        if (!this.open()) return;
        const held = this.el.nativeElement.contains(document.activeElement);
        this.setTray(false);
        if (held) this.trayTrigger()?.nativeElement.focus();
    }

    onDocumentClick(event: MouseEvent): void {
        if (!this.open()) return;
        const target = event.target instanceof Element ? event.target : null;
        const panel = this.trayPanel()?.nativeElement;
        if (target && panel?.contains(target)) {
            // An entry that leaves the page takes the panel with it, or it hangs over whatever was
            // navigated to; one that acts in place — theme, Appearance — leaves it standing.
            if (target.closest('a[href]')) this.setTray(false);
            return;
        }
        // The trigger, and nothing else on the wall: a hook clicked beside it is a navigation, and
        // the panel must not survive it.
        if (target && this.trayTrigger()?.nativeElement.contains(target)) return;
        this.setTray(false);
    }

    private setTray(next: boolean): void {
        if (this.open() === next) return;
        this.open.set(next);
        this.trayOpenChange.emit(next);
    }

    tally(item: HookRailItem): string {
        return indexTabBadgeLabel(item.badge);
    }

}
