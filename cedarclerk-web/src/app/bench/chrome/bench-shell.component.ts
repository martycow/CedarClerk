import { ChangeDetectionStrategy, Component, HostListener, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { CommentsService } from '../../core/comments.service';
import { CreditBalanceService } from '../../core/credit-balance.service';
import { DebugLogService } from '../../core/debug-log.service';
import { LocaleService } from '../../core/i18n/locale.service';
import { ProjectsService } from '../../core/projects.service';
import { CurrentProjectService } from '../../core/current-project.service';
import { RailActionsService } from '../../core/rail-actions.service';
import { RulerService } from '../../core/ruler.service';
import { ThemeService } from '../../core/theme.service';
import { VersionService } from '../../core/version.service';
import { AccountMenuComponent } from '../../shared/account-menu.component';
import { AppearancePanelComponent } from '../../shared/appearance-panel.component';
import { DebugConsoleComponent } from '../../shared/debug-console.component';
import { IconComponent } from '../../shared/icon.component';
import { SearchOverlayComponent } from '../../shared/search-overlay.component';
import { ButtonComponent } from '../forms/button.component';
import { ResinDropComponent } from '../display/resin-drop.component';
import { HookRailComponent, HookRailItem } from './hook-rail.component';
import { RailHeaderComponent, RailProject } from './rail-header.component';
import { RulerBarComponent, RulerReadout } from './ruler-bar.component';

/** Which hook stands for a path (ADR-139). Longest match first — the board is a child of the hub. */
const HOOK_PREFIXES: readonly (readonly [string, string])[] = [
    ['board', '/projects/:id/tasks'],
    ['assets', '/projects/:id/assets'],
    ['planner', '/projects/:id/planner'],
    ['builds', '/projects/:id/builds'],
    ['showcase', '/projects/:id/showcase'],
    ['canvas', '/projects/:id/canvas'],
    ['dialogues', '/projects/:id/dialogues'],
    ['documents', '/projects/:id'],
    ['hub', '/projects'],
    ['documents', '/drafts'],
    ['documents', '/editor'],
    ['assets', '/library'],
    ['calendar', '/calendar'],
    ['metrics', '/posts'],
    ['admin', '/admin'],
    ['settings', '/settings'],
];

/** Child screens the switcher carries across a project change; deeper paths fold to the child. */
const PROJECT_CHILDREN: ReadonlySet<string> =
    new Set(['assets', 'tasks', 'planner', 'builds', 'canvas', 'showcase', 'dialogues']);

function matches(path: string, pattern: string): boolean {
    const p = path.split('/').filter(Boolean);
    const q = pattern.split('/').filter(Boolean);
    if (p.length < q.length) return false;
    return q.every((seg, i) => seg === ':id' || seg === p[i]);
}

// The bench: a pegboard wall down the left edge, a sign board across the top, the page between
// them, and the drawer and rule along the bottom. It is a parent route rather than the root
// component so that the pre-auth pages are outside it by the shape of the route tree instead of by
// a list of URL prefixes someone has to remember to extend (ADR-139 clause 1).
@Component({
    selector: 'app-bench-shell',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { '[class.drawer-open]': 'log.open()' },
    imports: [
        RouterOutlet, RouterLink, IconComponent, HookRailComponent, RailHeaderComponent,
        RulerBarComponent, AccountMenuComponent, AppearancePanelComponent, DebugConsoleComponent,
        ResinDropComponent, ButtonComponent, SearchOverlayComponent,
    ],
    template: `
        <div class="shell">
            <app-rail-header [version]="versionLabel()" [project]="projectLabel()"
                             projectLink="/projects" [projects]="switcher()" [projectId]="openProjectId()"
                             [projectHint]="t().shell.switchProject" [crumbs]="crumbs()"
                             [crumbsLabel]="t().shell.breadcrumb"
                             [homeLabel]="t().shell.logoHome">
                <!-- The default slot RailHeader.prompt.md reserves for save state and the
                     screen's one primary action. Neither is the shell's: a page publishes them
                     and the rail renders them, as data and never as a template (ADR-159). -->
                @if (rail.save(); as s) {
                    <app-resin-drop [state]="s.state" [label]="s.label || ''" [title]="s.hint || ''" />
                }
                @if (rail.primary(); as action) {
                    <app-button class="rail-primary" variant="pine" size="sm" surface="chrome"
                                [disabled]="!!action.disabled"
                                [title]="action.hint || ''" (clicked)="action.run()">
                        <app-icon [name]="action.icon" size="xs" />
                        {{ action.label }}
                    </app-button>
                }

                <!--T-351 — the finite resource on the rail: shown once there is anything to
                    watch (a paid plan or a non-zero balance), a door to the wallet otherwise
                    hidden from a Free account with nothing metered.-->
                @if (creditChip(); as credits) {
                    <a account class="credit-chip" routerLink="/settings" [queryParams]="{ tab: 'billing' }"
                       [title]="t().shell.credits(credits)">
                        <app-icon name="drop" size="xs" />
                        <span class="credit-count">{{ credits }}</span>
                    </a>
                }
                <app-account-menu account />
            </app-rail-header>

            <div class="row">
                <app-hook-rail [items]="hooks()" [value]="activeHook()" [label]="t().shell.screens"
                               [trayLabel]="t().shell.more">
                    <div tray class="menu-items">
                        <button type="button" class="menu-item theme-toggle" (click)="theme.toggle()">
                            <app-icon [name]="theme.theme() === 'dark' ? 'sun' : 'moon'" size="sm" />
                            {{ t().common.toggleTheme }}
                        </button>
                        <button type="button" class="menu-item" (click)="appearance().open.set(true)">
                            <app-icon name="palette" size="sm" />
                            {{ t().settings.appearance.title }}
                        </button>
                        <a class="menu-item" routerLink="/glossary">
                            <app-icon name="book-bookmark" size="sm" />
                            {{ t().glossary.crumb }}
                        </a>
                        <!--T-339 — development surfaces; a user has no business seeing them.-->
                        @if (auth.isAdmin()) {
                            <a class="menu-item" routerLink="/dev/styleguide">
                                <app-icon name="palette" size="sm" />
                                {{ t().shell.styleguide }}
                            </a>
                            <a class="menu-item" routerLink="/dev/icons">
                                <app-icon name="squares-four" size="sm" />
                                {{ t().shell.icons }}
                            </a>
                        }
                    </div>
                </app-hook-rail>

                <!-- The ground every screen stands on. ADR-138 item 5's carve-out keys on this
                     attribute: the touch floor reaches what is under here and stops at the chrome
                     nested inside it. -->
                <main class="body" data-surface="paper">
                    <router-outlet />
                </main>
            </div>
        </div>

        <!-- No top: the strip grows upward from the bottom edge as the drawer opens, which keeps
             the rule on the screen's edge and slides the lip up off it. -->
        <div class="bench-bottom">
            <app-debug-console>
                <!--The window's own state, so the shell's and not a page's (ADR-188). It hangs on
                the drawer lip rather than in the rail (ADR-201): the top rail is where a screen's
                work is, and a window control standing among a page's actions read as one of them.-->
                <app-button lipActions class="lip-fullscreen" variant="paper" size="sm" surface="chrome"
                            [title]="fullscreenLabel()" (clicked)="toggleFullscreen()">
                    <app-icon [name]="isFullscreen() ? 'arrows-in-simple' : 'arrows-out-simple'" size="xs" />
                </app-button>
                <!--T-330 — the way back to the landing lives on the bottom edge, off the prime
                    header spot it used to take. An href: the landing is a server page.-->
                <app-button lipActions variant="paper" size="sm" surface="chrome"
                            href="/welcome" [title]="t().shell.aboutLanding">
                    <app-icon name="tree-evergreen" size="xs" />
                </app-button>
            </app-debug-console>
            <app-ruler-bar [label]="ruler.label()" [left]="ruler.left()" [right]="rulerRight()" />
        </div>

        <!-- Hoisted out of the editor (ADR-151 clause 2): a trigger in shared chrome cannot open a
             modal parented to one page. -->
        <app-appearance-panel />

        <!-- Ctrl+K works on every screen in the shell; the overlay listens for the key itself. -->
        <app-search-overlay />
    `,
    styles: [`
        :host {
            display: block;
            padding-bottom: var(--bench-bottom-h);
        }

        /* The open journal is reserved, not overlaid (ADR-153 clause 6): every height below that
           subtracts the bottom chrome follows this one property. */
        :host.drawer-open {
            --bench-bottom-h: calc(var(--bench-drawer-lip) + var(--bench-ruler-h) + var(--bench-drawer-open));
        }

        /* The shell owns the viewport: a page scrolls inside .body, never as a document under the
           rail. Rail, hooks, drawer and rule paint their own wood over the plaster. */
        .shell {
            display: flex;
            flex-direction: column;
            height: calc(100vh - var(--bench-bottom-h));
            background: var(--surface-page);
        }

        .row {
            display: flex;
            flex: 1;
            align-items: stretch;
            min-height: 0;
        }

        /* T-351 — the wallet's number on the rail: chrome box, rail ink, mono digits. */
        .credit-chip {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            min-height: var(--hit-chrome);
            padding: 0 var(--space-2);
            border: var(--border-rail-btn);
            border-radius: var(--radius-plaque);
            background: var(--rail-btn-face, rgba(0, 0, 0, .16));
            color: var(--rail-ink);
            text-decoration: none;
            font-family: var(--font-mono);
            font-size: var(--text-chrome);
            font-weight: 600;
        }

        .credit-chip:hover { background: var(--rail-btn-face-hover, rgba(0, 0, 0, .28)); }

        app-hook-rail {
            height: calc(100vh - var(--bench-rail-h) - var(--bench-bottom-h));
        }

        .body {
            display: flex;
            flex: 1;
            flex-direction: column;
            min-width: 0;
            min-height: 0;
            overflow: auto;
        }

        .bench-bottom {
            position: fixed;
            left: 0;
            right: 0;
            bottom: 0;
            z-index: 200;
            display: flex;
            flex-direction: column;
        }

        .menu-items {
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
            min-width: 180px;
        }

        /* The panel hangs off the rail, so its entries are chrome and measure like the rail's own
           controls rather than like the page under it. */
        .menu-item {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            min-height: var(--hit-chrome);
            padding: 0 var(--space-3);
            border: none;
            border-radius: var(--radius-stamp);
            background: none;
            color: var(--text);
            font-family: var(--font-sans);
            font-size: var(--text-chrome);
            text-align: left;
            text-decoration: none;
            white-space: nowrap;
            cursor: pointer;
        }

        .menu-item:hover { background: var(--hover-strong); }
    `],
})
export class BenchShellComponent {
    private readonly router = inject(Router);
    private readonly projects = inject(ProjectsService);
    private readonly current = inject(CurrentProjectService);
    private readonly version = inject(VersionService);
    private readonly feedback = inject(CommentsService);
    private readonly creditBalance = inject(CreditBalanceService);

    protected readonly auth = inject(AuthService);
    protected readonly theme = inject(ThemeService);
    protected readonly rail = inject(RailActionsService);
    protected readonly ruler = inject(RulerService);
    protected readonly log = inject(DebugLogService);
    protected readonly t = inject(LocaleService).t;

    protected readonly appearance = viewChild.required(AppearancePanelComponent);

    private readonly url = signal(this.router.url);
    private readonly path = computed(() => this.url().split('?')[0].split('#')[0]);

    /** Resolved once per shell, and only when a route names a project we cannot name. */
    private readonly projectNames = signal<ReadonlyMap<string, string>>(new Map());
    /** An empty map before the answer and an empty map for an account with no projects are the
        same value and mean opposite things. */
    private readonly namesLoaded = signal(false);
    private namesRequested = false;

    protected readonly projectId = computed(() => {
        const seg = this.path().split('/').filter(Boolean);
        return seg[0] === 'projects' && seg[1] ? seg[1] : '';
    });

    // Kept in sync with a listener rather than just toggled: Esc and the browser's own chrome can
    // leave fullscreen without going through this button.
    protected readonly isFullscreen = signal(!!document.fullscreenElement);

    @HostListener('document:fullscreenchange')
    protected onFullscreenChange() {
        this.isFullscreen.set(!!document.fullscreenElement);
    }

    protected fullscreenLabel(): string {
        const t = this.t().editor;
        return this.isFullscreen() ? t.exitFullscreen : t.enterFullscreen;
    }

    protected async toggleFullscreen() {
        try {
            if (document.fullscreenElement) await document.exitFullscreen();
            else await document.documentElement.requestFullscreen();
        } catch {
            // Denied by the browser (permissions policy, or not a user gesture) — nothing to
            // report, the button simply doesn't take effect.
        }
    }

    protected readonly versionLabel = computed(() => {
        const v = this.version.version();
        return v ? `v${v}` : '';
    });

    // The rule carries what the page publishes and nothing more. The version is not appended here:
    // the rail already prints it on every screen, and the same number in two places on one screen
    // reads as two numbers that happen to agree.
    protected readonly rulerRight = computed<readonly RulerReadout[]>(() => this.ruler.right());

    /** T-351 — the number the chip shows, or null while there is nothing worth watching. */
    protected readonly creditChip = computed<number | null>(() => {
        const balance = this.creditBalance.balance();
        if (balance === null) return null;
        const tier = this.auth.planTier();
        return balance > 0 || (tier !== null && tier !== 'Free') ? balance : null;
    });

    /** The project the session is in: the URL's when it names one, the remembered one otherwise. */
    protected readonly openProjectId = computed(() => this.projectId() || this.current.id());

    /** The hub is the one screen where no project is open — the remembered one does not count
        there, or the wall would offer a board, a planner and a build list for a project the
        reader has just stepped out of. */
    private readonly onHub = computed(() => this.path().replace(/\/+$/, '') === '/projects');

    protected readonly projectLabel = computed(() => {
        if (!this.auth.indieDev() || !this.projectId()) return '';
        const id = this.openProjectId();
        // The remembered name answers for the remembered project and no other: while the name map
        // is still in flight it used to stand in for whichever project the URL named, which put a
        // different project's name on the rail than the one on the page.
        const remembered = this.current.id() === id ? this.current.name() : '';
        return (id && this.projectNames().get(id)) || remembered || this.t().shell.allProjects;
    });

    /** The screen open inside the project — canvas/:boardId folds to canvas, whose list a switch lands on. */
    private readonly projectChild = computed(() => {
        const seg = this.path().split('/').filter(Boolean);
        return seg[0] === 'projects' && seg[2] && PROJECT_CHILDREN.has(seg[2]) ? seg[2] : '';
    });

    /** Every project, then the hub — which is where "All projects" used to send you (ADR-186).
        Switching keeps the open screen: on /projects/A/assets every entry links to that project's
        assets, and the pages refetch off paramMap themselves. */
    protected readonly switcher = computed<readonly RailProject[]>(() => {
        if (!this.auth.indieDev() || !this.projectId()) return [];
        const names = this.projectNames();
        if (!names.size) return [];
        const child = this.projectChild();
        const items: RailProject[] = [...names].map(([id, name]) =>
            ({ id, name, link: child ? ['/projects', id, child] : ['/projects', id] }));
        items.push({ id: '', name: this.t().shell.allProjects, link: '/projects' });
        return items;
    });

    protected readonly hooks = computed<readonly HookRailItem[]>(() => {
        const t = this.t().shell;
        const open = this.openProjectId();
        const items: HookRailItem[] = [];
        if (this.auth.indieDev()) items.push({ id: 'hub', icon: 'game-controller', label: t.hub, link: '/projects' });
        if (!this.auth.indieDev()) {
            items.push({ id: 'documents', icon: 'pencil-simple', label: t.documents, link: '/drafts' });
            items.push({ id: 'calendar', icon: 'clock', label: t.calendar, link: '/calendar' });
            items.push({ id: 'assets', icon: 'images', label: t.assets, link: '/library' });
            items.push({
                id: 'metrics', icon: 'chart-bar', label: t.metrics, link: '/posts',
                badge: this.feedback.newComments() + this.feedback.newReactions(),
                badgeTitle: this.t().editor.newBadge,
            });
        } else if (open && !this.onHub()) {
            items.push({ id: 'documents', icon: 'pencil-simple', label: t.documents, link: ['/projects', open] });
            items.push({ id: 'board', icon: 'check-square', label: t.board, link: ['/projects', open, 'tasks'] });
            items.push({ id: 'planner', icon: 'flag', label: t.planner, link: ['/projects', open, 'planner'] });
            items.push({ id: 'builds', icon: 'cube', label: t.builds, link: ['/projects', open, 'builds'] });
            items.push({ id: 'assets', icon: 'images', label: t.assets, link: ['/projects', open, 'assets'] });
            items.push({ id: 'canvas', icon: 'squares-four', label: t.canvas, link: ['/projects', open, 'canvas'] });
            items.push({ id: 'dialogues', icon: 'tree-structure', label: t.dialogues, link: ['/projects', open, 'dialogues'] });
            items.push({ id: 'showcase', icon: 'rocket-launch', label: t.showcase, title: this.t().projects.showcase.title, link: ['/projects', open, 'showcase'] });
            items.push({ id: 'calendar', icon: 'clock', label: t.calendar, link: '/calendar' });
            items.push({
                id: 'metrics', icon: 'chart-bar', label: t.metrics, link: '/posts',
                badge: this.feedback.newComments() + this.feedback.newReactions(),
                badgeTitle: this.t().editor.newBadge,
            });
        }
        if (this.auth.isAdmin()) {
            items.push({ id: 'admin', icon: 'shield-check', label: t.admin, link: '/admin' });
        }
        // The caption is the wall's short form — the full word clips on it — and the tooltip carries the word.
        items.push({ id: 'settings', icon: 'gear', label: t.settings, title: this.t().settings.crumb, link: '/settings', end: true });
        return items;
    });

    protected readonly activeHook = computed(() => {
        const path = this.path();
        return HOOK_PREFIXES.find(([, pattern]) => matches(path, pattern))?.[0] ?? '';
    });

    protected readonly crumbs = computed<readonly string[]>(() => {
        const t = this.t();
        const seg = this.path().split('/').filter(Boolean);
        switch (seg[0]) {
            case 'drafts': return [t.drafts.crumb];
            case 'editor': return [t.shell.editorCrumb];
            case 'posts': return [t.manager.crumb];
            case 'calendar': return [t.calendar.crumb];
            case 'glossary': return [t.glossary.crumb];
            case 'library': return [t.media.crumb];
            case 'settings': return [t.settings.crumb];
            case 'admin': return [t.admin.crumb];
            case 'dev': return [seg[1] === 'icons' ? t.shell.icons : t.shell.styleguide];
            case 'projects': return this.projectCrumbs(seg);
            default: return [];
        }
    });

    constructor() {
        // The rail is the only global signal that feedback has arrived, so the shell is what asks:
        // before this it was fetched by whichever screen happened to open, and landing anywhere but
        // the editor left the tally at zero over unread comments (ADR-155).
        this.feedback.refreshNewCount();

        this.router.events.subscribe(e => {
            if (e instanceof NavigationEnd) this.url.set(e.urlAfterRedirects);
        });

        // T-351 — the credit chip's number, re-read per navigation: every metered action ends in
        // a page turn, and one turn of staleness is honest enough for a chip that opens the wallet.
        effect(() => {
            this.url();
            if (this.auth.userEmail()) void this.creditBalance.refresh();
        });

        // Fetched once a session and on every screen, not only where the URL names a project:
        // the switcher lists them and the Board hook needs the remembered one to still exist.
        effect(() => {
            if (!this.auth.indieDev() || this.namesRequested) return;
            this.namesRequested = true;
            this.projects.list(true)
                .then(list => {
                    this.projectNames.set(new Map(list.map(p => [p.id, p.name])));
                    this.namesLoaded.set(true);
                    this.current.reconcile(list);
                })
                .catch(() => { this.namesRequested = false; });
        });

        // One writer for the session's project, and this is it: the resolved route.
        effect(() => {
            const id = this.projectId();
            const name = this.projectNames().get(id);
            if (!id) return;
            // Once the list has answered, an id it does not hold is a project this account does not
            // own — a board shared with them. That is not the session's project: the rail's hooks
            // would point at routes a member gets a 404 from, and the `reconcile` behind the next
            // list would throw the reader's own project away on the way past.
            if (!name && this.namesLoaded()) return;
            // untracked: remember() reads the state it writes, and the service's own session effect
            // reads it too — tracked, the two would feed each other. The empty name is deliberate:
            // borrowing the remembered one put a different project's name on the tile.
            untracked(() => this.current.remember(id, name ?? ''));
        });
    }

    private projectCrumbs(seg: readonly string[]): readonly string[] {
        const t = this.t().projects;
        // The tile already carries the project's own name, so the crumb says what is open inside it.
        if (!seg[1]) return [t.crumb];
        switch (seg[2]) {
            case 'tasks': return [t.tasks.crumb];
            case 'assets': return [t.assets.crumb];
            case 'planner': return [t.planner.crumb];
            case 'builds': return [t.builds.crumb];
            case 'canvas': return [t.canvas.crumb];
            case 'dialogues': return [t.dialogues.crumb];
            case 'showcase': return [t.showcase.crumb];
            default: return [];
        }
    }
}
