import { ChangeDetectionStrategy, Component, ElementRef, booleanAttribute, inject, input, output, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CedarLogoComponent } from '../../shared/cedar-logo.component';
import { IconComponent } from '../../shared/icon.component';

// The one piece of wood in the chrome: a park-sign board carrying what belongs to the whole
// screen — brand, project switcher, breadcrumb — and, on the right, the screen's save state, its
// one primary action and the account.
//
// V2 has no menu bar. RailHeader.prompt.md says where what it carried went, and the last of it is
// the `dots` button in the right slot: "the rare rest live behind one dots button". That slot is
// this component's `[menu]` projection, and ADR-151 spends it on the theme toggle and Appearance.
//
// Every label is an input rather than a translation looked up here: the bench library holds no
// locale, the same way app-task-tag leaves its drag hint to the consumer.
@Component({
    selector: 'app-rail-header',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [CedarLogoComponent, IconComponent, RouterLink],
    host: {
        'data-surface': 'chrome',
        'role': 'banner',
        '(document:click)': 'onDocumentClick($event)',
        '(document:keydown.escape)': 'onEscape()',
    },
    template: `
        <app-cedar-logo class="mark" [size]="22" fill="var(--pine-mark)" />
        <span class="brand">{{ brand() }}</span>
        @if (version()) { <span class="version">{{ version() }}</span> }

        @if (project() && projectLink()) {
            <!-- A link, for the reason every hook on the wall is one: a middle click opens the
                 destination in a tab. It is not aria-haspopup — ARIA 1.2 reads that as "menu",
                 and what happens here is a navigation.
                 The project name is the accessible name; an aria-label of "switch project" would
                 replace visible text that is not inside it, which is the WCAG 2.5.3 failure, so
                 the purpose rides on the tooltip. -->
            <a class="tile" [routerLink]="projectLink()" [attr.title]="projectHint() || null">
                <span class="tile-name">{{ project() }}</span>
                <app-icon name="caret-down" size="xs" />
            </a>
        }

        @if (crumbs().length) {
            <nav class="crumbs" [attr.aria-label]="crumbsLabel()">
                <ol>
                    @for (crumb of crumbs(); track $index; let last = $last) {
                        <li [attr.aria-current]="last ? 'page' : null">{{ crumb }}</li>
                    }
                </ol>
            </nav>
        }

        <span class="spacer"></span>
        <ng-content />

        <div class="menu-anchor">
            @if (hasMenu()) {
                <button #trigger type="button" class="dots" [attr.aria-label]="menuLabel()"
                        aria-haspopup="true" [attr.aria-expanded]="menuOpen()" (click)="toggleMenu()">
                    <app-icon name="dots-three" size="xs" />
                </button>
            }
            <!-- Never behind @if: the slot is what holds the projected controls, and a control
                 destroyed on close loses whatever state it was carrying. -->
            <div #panel class="menu" role="group" [attr.aria-label]="menuLabel()" [hidden]="!menuOpen()">
                <ng-content select="[menu]" />
            </div>
        </div>

        <ng-content select="[account]" />
    `,
    styles: [`
        :host([data-surface="chrome"]) {
            position: sticky;
            top: 0;
            z-index: 10;
            display: flex;
            align-items: center;
            gap: var(--space-3);
            box-sizing: border-box;
            height: var(--bench-rail-h);
            padding: 0 var(--space-4);
            background-color: var(--rail-lo);
            background-image: var(--tex-wood), var(--surface-rail);
            border-bottom: 2px solid var(--rail-edge);
            box-shadow: var(--shadow-rail);
            font-family: var(--font-sans);
            /* The prompt file's one hard rule: ink on the rail is always the cream, never an
               ink-on-paper colour. The soft cream composites to 4.37:1 over the rail, so it is
               spent on separators and on nothing that is read. */
            color: var(--rail-ink);
        }

        :host([data-surface="chrome"]) .mark { flex: none; }

        /* The kit paints the wordmark at 18px display. Chrome type is 11-13px (ADR-138 item 2),
           so the size goes to the logo mark beside it and the word is told apart by face and
           weight instead. */
        :host([data-surface="chrome"]) .brand {
            font-family: var(--font-display);
            font-size: var(--text-chrome);
            font-weight: 700;
            letter-spacing: .02em;
            white-space: nowrap;
            text-shadow: 0 1px 1px var(--rail-edge);
        }

        :host([data-surface="chrome"]) .version {
            font-family: var(--font-readout);
            font-size: var(--text-chrome-sm);
            white-space: nowrap;
        }

        :host([data-surface="chrome"]) .tile {
            display: inline-flex;
            align-items: center;
            gap: 7px;
            min-height: var(--hit-chrome);
            padding: 0 10px;
            border: 1px solid var(--tile-edge, rgba(20, 12, 4, .5));
            border-radius: var(--radius-stamp);
            background-color: var(--sign-tile-hi);
            background-image: var(--tex-wood), var(--grad-sign-tile);
            background-size: 420px, auto;
            color: var(--rail-ink);
            font-family: var(--font-display);
            font-size: var(--text-chrome);
            font-weight: 700;
            letter-spacing: .01em;
            white-space: nowrap;
            text-decoration: none;
            cursor: pointer;
            text-shadow: 0 1px 1px color-mix(in srgb, var(--rail-edge) 50%, transparent);
        }

        /* Withheld while focused: the ADR-140 ring spends its second layer on a box-shadow, and a
           component's own shadow out-specifies the global rule that draws it. */
        :host([data-surface="chrome"]) .tile:not(:focus-visible) {
            box-shadow: inset 0 1px 0 rgba(255, 240, 210, .16), 0 1px 2px rgba(20, 12, 4, .4);
        }
        :host([data-surface="chrome"]) .tile:hover { filter: brightness(1.08); }

        /* The kit's caret is a 10px glyph; the icon takes that size through its own token. */
        :host([data-surface="chrome"]) .tile app-icon { --icon-xs: var(--fs-10); opacity: .65; }

        :host([data-surface="chrome"]) .tile-name {
            overflow: hidden;
            max-width: 18ch;
            text-overflow: ellipsis;
        }

        :host([data-surface="chrome"]) .crumbs { min-width: 0; }

        :host([data-surface="chrome"]) .crumbs ol {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
            margin: 0;
            padding: 0;
            list-style: none;
        }

        :host([data-surface="chrome"]) .crumbs li {
            overflow: hidden;
            max-width: 24ch;
            font-size: var(--text-chrome);
            font-weight: 600;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        /* Decoration, and drawn rather than written so a screen reader never announces it. */
        :host([data-surface="chrome"]) .crumbs li::before {
            content: '/';
            margin-right: var(--space-2);
            color: var(--rail-ink-soft);
        }

        :host([data-surface="chrome"]) .spacer { flex: 1; }

        :host([data-surface="chrome"]) .menu-anchor { position: relative; flex: none; }

        :host([data-surface="chrome"]) .dots {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: var(--hit-chrome);
            min-height: var(--hit-chrome);
            border: var(--border-rail-btn);
            border-radius: var(--radius-plaque);
            background: var(--rail-btn-face, rgba(0, 0, 0, .16));
            color: var(--rail-ink);
            cursor: pointer;
        }

        :host([data-surface="chrome"]) .dots:hover { background: var(--rail-btn-face-hover, rgba(0, 0, 0, .28)); }

        /* The panel hangs off the rail and lands on paper: the rail's ink rule governs the board,
           not what is pinned under it. */
        :host([data-surface="chrome"]) .menu {
            position: absolute;
            top: calc(100% + var(--space-2));
            right: 0;
            z-index: 1;
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

        :host([data-surface="chrome"]) .menu[hidden] { display: none; }
    `],
})
export class RailHeaderComponent {
    private readonly el = inject(ElementRef<HTMLElement>);
    private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');
    private readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');

    readonly brand = input('Cedar Clerk');
    readonly version = input('');
    /** The active project. Empty renders no tile — the switcher has nothing to switch. */
    readonly project = input('');
    /**
     * Where the tile goes; anything routerLink takes. Empty renders no tile either: a switcher
     * with no destination is a sign, not a control.
     */
    readonly projectLink = input<string | readonly unknown[]>('');
    /** Tooltip of the switcher tile; the consumer's to translate. The name stays the project's. */
    readonly projectHint = input('');
    readonly crumbs = input<readonly string[]>([]);
    readonly crumbsLabel = input('Breadcrumb');
    readonly menuLabel = input('More');
    /** False hides the dots button; the `[menu]` slot still holds whatever was handed to it. */
    readonly hasMenu = input(true, { transform: booleanAttribute });

    readonly menuOpenChange = output<boolean>();

    private readonly open = signal(false);
    readonly menuOpen = this.open.asReadonly();

    toggleMenu(): void { this.setMenu(!this.open()); }

    closeMenu(): void { this.setMenu(false); }

    // The menu-button pattern: Escape closes and hands focus back to the button. Hiding the panel
    // first would drop focus on <body> — [hidden] takes the focused entry out of the tree — so the
    // question of whether we hold focus at all is asked before the panel goes.
    onEscape(): void {
        if (!this.open()) return;
        const held = this.el.nativeElement.contains(document.activeElement);
        this.setMenu(false);
        if (held) this.trigger()?.nativeElement.focus();
    }

    onDocumentClick(event: MouseEvent): void {
        if (!this.open()) return;
        const target = event.target instanceof Element ? event.target : null;
        if (target && this.panel().nativeElement.contains(target)) {
            // An entry that leaves the page takes the panel with it, or it hangs over whatever
            // was navigated to; one that acts in place — theme, Appearance — leaves it standing.
            if (target.closest('a[href]')) this.setMenu(false);
            return;
        }
        if (target && this.el.nativeElement.contains(target)) return;
        this.setMenu(false);
    }

    private setMenu(next: boolean): void {
        if (this.open() === next) return;
        this.open.set(next);
        this.menuOpenChange.emit(next);
    }
}
