import { ChangeDetectionStrategy, Component, ElementRef, computed, input, output, viewChildren } from '@angular/core';

export interface IndexTabItem {
    id: string;
    label: string;
    /**
     * A number counts things and follows the counter rules the app already had: nothing is drawn at
     * zero or below, and anything past 99 draws as `99+` so a runaway count cannot stretch the tile.
     * A string is drawn as written — that is how a badge says something other than a quantity.
     */
    badge?: number | string;
    /** Accessible name and tooltip for the badge, when the number alone does not say what it counts. */
    badgeTitle?: string;
    /** Tooltip for the tile itself. */
    hint?: string;
    /** id of the element this tile shows, so the pair is announced as a tab and its panel. */
    panelId?: string;
}

/**
 * Exported because the badge rules outlive this component: the hook rail hangs the same tally on a
 * tool and reads it through here (ADR-155). A badge is not every number the chrome draws — a shelf
 * panel's count and a ruler readout are shown as written, zero included.
 */
export function indexTabBadgeLabel(badge: number | string | undefined | null): string {
    if (badge === undefined || badge === null) return '';
    if (typeof badge === 'number') {
        if (!Number.isFinite(badge) || badge <= 0) return '';
        return badge > 99 ? '99+' : String(Math.trunc(badge));
    }
    return badge.trim();
}

// Painted index tiles on the edge of a shelf: the lit tile is raised, brightened and underlined in
// brass, the rest sit back in the shadow.
//
// IndexTabs.prompt.md gives them one jurisdiction — they switch what a panel or a drawer SHOWS and
// never navigate between screens, which is the rail's job. That is enforced by what the API lacks:
// no href, no route, no link input. A tile is a <button> and the only thing leaving here is the id
// of the body to show.
@Component({
    selector: 'app-index-tabs',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'chrome',
        'data-box': 'trim',
        'role': 'tablist',
        '[attr.aria-label]': 'label() || null',
    },
    template: `
        @for (item of items(); track item.id; let i = $index) {
            <button #tile type="button" class="it-tile" role="tab"
                    [class.is-on]="item.id === value()"
                    [attr.aria-selected]="item.id === value()"
                    [attr.tabindex]="i === rovingIndex() ? 0 : -1"
                    [attr.title]="item.hint || null"
                    [attr.aria-controls]="item.panelId || null"
                    (click)="pick(item.id)" (keydown)="onKeydown($event, i)">
                <span class="it-label">{{ item.label }}</span>
                @if (badgeOf(item); as count) {
                    <!-- An aria-label REPLACES the text it is on, so the title alone would take the
                         count out of the tile's name and leave "Checks unresolved checks". -->
                    <span class="it-badge" [attr.title]="item.badgeTitle || null"
                          [attr.aria-label]="item.badgeTitle ? count + ' ' + item.badgeTitle : null">{{ count }}</span>
                }
            </button>
        }
    `,
    styles: [`
        :host([data-surface="chrome"]) {
            display: flex;
            align-items: flex-end;
            gap: 3px;
            flex: none;
            min-width: 0;
            /* The strip stands ON the board it switches, never above it with a gap: a tile is an
               index tile cut into the shelf edge, and a step between the two is the defect Marty
               called a threshold. The hair of overlap is what welds the tile to the frame. */
            margin-bottom: -1px;
        }

        :host([data-surface="chrome"][data-box="trim"]) .it-tile {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            box-sizing: border-box;
            min-height: var(--hit-trim);
            padding: 0 11px;
            border: 1px solid var(--wood-edge);
            border-radius: var(--radius-sm) var(--radius-sm) 0 0;
            background-color: var(--sign-tile-hi);
            background-image: var(--tex-wood), var(--grad-sign-tile);
            filter: brightness(.86);
            color: var(--rail-ink-dim, rgba(242, 232, 206, .8));
            font-family: var(--font-display);
            font-size: var(--text-chrome-sm);
            font-weight: 700;
            letter-spacing: .09em;
            text-transform: uppercase;
            white-space: nowrap;
            text-shadow: 0 1px 1px color-mix(in srgb, var(--rail-edge) 60%, transparent);
            cursor: pointer;
            transition: filter var(--motion-fast) var(--ease-settle);
        }

        :host([data-surface="chrome"][data-box="trim"]) .it-tile:hover { filter: brightness(1); }

        /* The raise is height, not a shadow: a tile sits on the shelf edge, and the lit one stands
           a step proud of the ones behind it. */
        :host([data-surface="chrome"][data-box="trim"]) .it-tile.is-on {
            min-height: calc(var(--hit-trim) + 3px);
            border-bottom: 2px solid var(--brass);
            filter: brightness(1.16);
            color: var(--rail-ink);
        }

        :host([data-surface="chrome"][data-box="trim"]) .it-badge {
            flex: none;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 0 var(--space-1);
            border-radius: var(--radius-stamp);
            background: var(--tab-badge);
            color: var(--rail-edge);
            font-family: var(--font-readout);
            font-size: var(--text-chrome-sm);
            font-weight: 700;
            letter-spacing: normal;
            text-shadow: none;
        }
    `],
})
export class IndexTabsComponent {
    readonly items = input.required<IndexTabItem[]>();
    /** Id of the tile that is lit. Nothing is lit when it names no item. */
    readonly value = input('');
    /** Accessible name for the strip — say which panel it switches. */
    readonly label = input('');

    readonly selected = output<string>();

    private readonly tiles = viewChildren<ElementRef<HTMLButtonElement>>('tile');

    /** One tab stop for the whole strip; the arrow keys move within it. */
    readonly rovingIndex = computed(() => {
        const at = this.items().findIndex(i => i.id === this.value());
        return at === -1 ? 0 : at;
    });

    badgeOf(item: IndexTabItem): string {
        return indexTabBadgeLabel(item.badge);
    }

    pick(id: string): void {
        if (id !== this.value()) this.selected.emit(id);
    }

    onKeydown(event: KeyboardEvent, index: number): void {
        const count = this.items().length;
        if (!count) return;
        let next: number;
        switch (event.key) {
            case 'ArrowRight': next = (index + 1) % count; break;
            case 'ArrowLeft': next = (index - 1 + count) % count; break;
            case 'Home': next = 0; break;
            case 'End': next = count - 1; break;
            default: return;
        }
        event.preventDefault();
        this.pick(this.items()[next].id);
        this.tiles()[next]?.nativeElement.focus();
    }
}
