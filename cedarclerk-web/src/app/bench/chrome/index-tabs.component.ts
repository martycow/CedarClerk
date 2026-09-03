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
 * Exported because the badge rules outlive this component: the sidebar draws the same count on a
 * nav item and reads it through here. A badge is not every number drawn — a card's count is shown
 * as written, zero included.
 */
export function indexTabBadgeLabel(badge: number | string | undefined | null): string {
    if (badge === undefined || badge === null) return '';
    if (typeof badge === 'number') {
        if (!Number.isFinite(badge) || badge <= 0) return '';
        return badge > 99 ? '99+' : String(Math.trunc(badge));
    }
    return badge.trim();
}

// A segmented control (the artboards' `.seg`): the lit tile is a raised sheet, the rest sit in the
// trough. It switches what a panel SHOWS and never navigates between screens — enforced by what
// the API lacks: no href, no route, no link input. A tile is a <button> and the only thing leaving
// here is the id of the body to show.
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
            display: inline-flex;
            align-items: center;
            gap: 2px;
            flex: none;
            min-width: 0;
            padding: 3px;
            border-radius: var(--radius-md);
            background: var(--surface);
        }

        :host([data-surface="chrome"][data-box="trim"]) .it-tile {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            box-sizing: border-box;
            min-height: var(--hit-surface, var(--hit-trim));
            padding: 0 12px;
            border: none;
            border-radius: var(--radius-sm);
            background: none;
            color: var(--t2);
            font-family: var(--font-sans);
            font-size: var(--fs-12);
            font-weight: 600;
            white-space: nowrap;
            cursor: pointer;
        }

        :host([data-surface="chrome"][data-box="trim"]) .it-tile:hover { color: var(--text); }

        :host([data-surface="chrome"][data-box="trim"]) .it-tile.is-on {
            background: var(--sheet);
            color: var(--text);
            box-shadow: var(--shadow-paper-sm);
        }

        :host([data-surface="chrome"][data-box="trim"]) .it-badge {
            flex: none;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 0 var(--space-1);
            border-radius: var(--radius-stamp);
            background: var(--asoft);
            color: var(--accent);
            font-size: var(--fs-11);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
        }

        @media (max-width: 640px) {
            :host(.phone-grid) {
                display: grid;
                grid-template-columns: repeat(2, minmax(0, 1fr));
                width: 100%;
            }

            :host(.phone-grid) .it-tile {
                width: 100%;
                justify-content: center;
            }
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
