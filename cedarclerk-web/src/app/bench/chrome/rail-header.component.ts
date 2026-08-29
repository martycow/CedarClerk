import { ChangeDetectionStrategy, Component, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CedarLogoComponent } from '../../shared/cedar-logo.component';
import { IconComponent } from '../../shared/icon.component';

/** One entry in the switcher: a project, or the hub at the end of the list. */
export interface RailProject {
    id: string;
    name: string;
    /** Anything routerLink takes. Every entry is an anchor, so a middle click opens it in a tab. */
    link: string | readonly unknown[];
}

// The one piece of wood in the chrome: a park-sign board carrying what belongs to the whole
// screen — brand, project switcher, breadcrumb — and, on the right, the screen's save state, its
// one primary action and the account.
//
// The overflow the mirror's `dots` button stood for is not here: it hangs at the foot of the tool
// wall instead (ADR-183), where four of its six entries are screens and the wall is what names
// screens. The right slot keeps what the prompt file reserved it for.
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
        <!-- The label repeats the visible brand so the accessible name still contains it
             (WCAG 2.5.3); indieDevGuard turns the target into /drafts when the module is off. -->
        <a class="home" routerLink="/projects" [attr.aria-label]="homeLabel() || null">
            <app-cedar-logo class="mark" [size]="22" fill="var(--pine-mark)" />
            <span class="brand">{{ brand() }}</span>
        </a>
        @if (version()) { <span class="version">{{ version() }}</span> }
        <!-- A plain href on purpose: the landing is served by the server middleware, and the SPA
             router would swallow a routerLink to a path it owns nothing at. -->
        @if (aboutHref() && aboutLabel()) {
            <a class="about" [href]="aboutHref()">{{ aboutLabel() }}</a>
        }

        @if (project()) {
            <div class="tile-anchor">
                @if (projects().length) {
                    <!-- A switcher, so a menu button (ADR-186). The entries inside it are anchors,
                         which is where the middle-click-opens-a-tab reason actually lives.
                         The project name is the accessible name; an aria-label of "switch project"
                         would replace visible text that is not inside it, which is the WCAG 2.5.3
                         failure, so the purpose rides on the tooltip. -->
                    <button #switcher type="button" class="tile" aria-haspopup="true"
                            [attr.aria-expanded]="switcherOpen()" [attr.title]="projectHint() || null"
                            (click)="toggleSwitcher()">
                        <span class="tile-name">{{ project() }}</span>
                        <app-icon name="caret-down" size="xs" />
                    </button>

                    <div #switcherPanel class="switcher" data-surface="paper" role="group"
                         [attr.aria-label]="projectHint() || null" [hidden]="!switcherOpen()">
                        @for (p of projects(); track p.id) {
                            <a class="switcher-item" [class.is-on]="p.id === projectId()"
                               [routerLink]="p.link"
                               [attr.aria-current]="p.id === projectId() ? 'true' : null">{{ p.name }}</a>
                        }
                    </div>
                } @else if (projectLink()) {
                    <a class="tile" [routerLink]="projectLink()" [attr.title]="projectHint() || null">
                        <span class="tile-name">{{ project() }}</span>
                        <app-icon name="caret-down" size="xs" />
                    </a>
                }
            </div>
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

        /* A door, not a hyperlink: it keeps the rail's ink and gap, and says so only on approach.
           No box-shadow of its own, so the ADR-140 focus ring is never out-specified. */
        :host([data-surface="chrome"]) .home {
            display: inline-flex;
            flex: none;
            align-items: center;
            gap: var(--space-3);
            border-radius: var(--radius-stamp);
            color: var(--rail-ink);
            text-decoration: none;
        }

        :host([data-surface="chrome"]) .home:hover { filter: brightness(1.12); }

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

        /* Full ink, not the soft cream: 11px is read, and the soft cream is below AA at that size. */
        :host([data-surface="chrome"]) .about {
            color: var(--rail-ink);
            font-family: var(--font-readout);
            font-size: var(--text-chrome-sm);
            white-space: nowrap;
            text-decoration: none;
        }

        :host([data-surface="chrome"]) .about:hover { text-decoration: underline; }

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

        :host([data-surface="chrome"]) .tile-anchor { position: relative; flex: none; }

        /* Paper hung off the sign board, and it says so: the rail's ink rule governs the wood, not
           what is pinned under it, and a list of project names is read at paper's size. Written
           without the chrome host qualifier for that reason — inside one, these are chrome's
           numbers and 14px type is out of the band (ADR-138). */
        .switcher[data-surface="paper"] {
            position: absolute;
            top: calc(100% + var(--space-2));
            left: 0;
            z-index: 11;
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
            min-width: 200px;
            padding: var(--space-2);
            border: var(--border-paper);
            border-radius: var(--radius-plaque);
            background-color: var(--sheet);
            background-image: var(--tex-paper);
            box-shadow: var(--shadow-paper);
            color: var(--text);
        }

        .switcher[data-surface="paper"][hidden] { display: none; }

        .switcher[data-surface="paper"] .switcher-item {
            display: flex;
            align-items: center;
            box-sizing: border-box;
            min-height: var(--hit-target);
            padding: var(--space-2) var(--space-3);
            border-radius: var(--radius-field);
            color: var(--text);
            font-family: var(--font-sans);
            font-size: var(--fs-ui);
            text-decoration: none;
            white-space: nowrap;
        }

        .switcher[data-surface="paper"] .switcher-item:hover { background: var(--hover); }
        .switcher[data-surface="paper"] .switcher-item.is-on { font-weight: 700; background: var(--hover); }

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
    `],
})
export class RailHeaderComponent {
    readonly brand = input('Cedar Clerk');
    /** Accessible name of the brand link; the consumer's to translate. It should repeat the brand. */
    readonly homeLabel = input('');
    readonly version = input('');
    /** A full-page href beside the version (the landing lives outside the SPA); empty renders nothing. */
    readonly aboutHref = input('');
    readonly aboutLabel = input('');
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
    /** Everything the tile can switch to, the hub included. Empty leaves the tile a plain link. */
    readonly projects = input<readonly RailProject[]>([]);
    /** Which entry is the one on show — marked, not just styled. */
    readonly projectId = input('');

    private readonly el = inject(ElementRef<HTMLElement>);
    private readonly switcher = viewChild<ElementRef<HTMLButtonElement>>('switcher');
    private readonly switcherPanel = viewChild<ElementRef<HTMLElement>>('switcherPanel');

    private readonly open = signal(false);
    readonly switcherOpen = this.open.asReadonly();

    toggleSwitcher(): void { this.open.set(!this.open()); }

    // The menu-button pattern: Escape closes and hands focus back to the button. Hiding the panel
    // first would drop focus on <body> — [hidden] takes the focused entry out of the tree.
    onEscape(): void {
        if (!this.open()) return;
        const held = this.el.nativeElement.contains(document.activeElement);
        this.open.set(false);
        if (held) this.switcher()?.nativeElement.focus();
    }

    onDocumentClick(event: MouseEvent): void {
        if (!this.open()) return;
        const target = event.target instanceof Element ? event.target : null;
        // Every entry is a destination, so any click inside the panel takes it with them.
        if (target && this.switcher()?.nativeElement.contains(target)) return;
        this.open.set(false);
    }
}
