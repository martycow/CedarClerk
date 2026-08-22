import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { CommentsService } from '../../core/comments.service';
import { DebugLogService } from '../../core/debug-log.service';
import { LocaleService } from '../../core/i18n/locale.service';
import { ProjectsService } from '../../core/projects.service';
import { RailActionsService } from '../../core/rail-actions.service';
import { RulerService } from '../../core/ruler.service';
import { ThemeService } from '../../core/theme.service';
import { VersionService } from '../../core/version.service';
import { AccountMenuComponent } from '../../shared/account-menu.component';
import { AppearancePanelComponent } from '../../shared/appearance-panel.component';
import { DebugConsoleComponent } from '../../shared/debug-console.component';
import { IconComponent } from '../../shared/icon.component';
import { ButtonComponent } from '../forms/button.component';
import { ResinDropComponent } from '../display/resin-drop.component';
import { HookRailComponent, HookRailItem } from './hook-rail.component';
import { RailHeaderComponent } from './rail-header.component';
import { RulerBarComponent, RulerReadout } from './ruler-bar.component';

/** Which hook stands for a path (ADR-139). Longest match first — the board is a child of the hub. */
const HOOK_PREFIXES: readonly (readonly [string, string])[] = [
    ['board', '/projects/:id/tasks'],
    ['hub', '/projects'],
    ['text', '/drafts'],
    ['text', '/editor'],
    ['assets', '/library'],
    ['metrics', '/posts'],
    ['settings', '/settings'],
];

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
        ResinDropComponent, ButtonComponent,
    ],
    template: `
        <div class="shell">
            <app-rail-header [version]="versionLabel()" [project]="projectLabel()"
                             projectLink="/projects"
                             [projectHint]="t().shell.switchProject" [crumbs]="crumbs()"
                             [crumbsLabel]="t().shell.breadcrumb" [menuLabel]="t().shell.more">
                <div menu class="menu-items">
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
                    @if (auth.isAdmin()) {
                        <a class="menu-item" routerLink="/admin">
                            <app-icon name="shield-check" size="sm" />
                            {{ t().admin.crumb }}
                        </a>
                    }
                    <a class="menu-item" routerLink="/dev/styleguide">
                        <app-icon name="palette" size="sm" />
                        {{ t().shell.styleguide }}
                    </a>
                    <a class="menu-item" routerLink="/dev/icons">
                        <app-icon name="squares-four" size="sm" />
                        {{ t().shell.icons }}
                    </a>
                </div>
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

                <app-account-menu account />
            </app-rail-header>

            <div class="row">
                <app-hook-rail [items]="hooks()" [value]="activeHook()" [label]="t().shell.screens" />

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
            <app-debug-console />
            <app-ruler-bar [label]="ruler.label()" [left]="ruler.left()" [right]="rulerRight()" />
        </div>

        <!-- Hoisted out of the editor (ADR-151 clause 2): a trigger in shared chrome cannot open a
             modal parented to one page. -->
        <app-appearance-panel />
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
    private readonly version = inject(VersionService);
    private readonly feedback = inject(CommentsService);

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
    private namesRequested = false;

    protected readonly projectId = computed(() => {
        const seg = this.path().split('/').filter(Boolean);
        return seg[0] === 'projects' && seg[1] ? seg[1] : '';
    });

    protected readonly versionLabel = computed(() => {
        const v = this.version.version();
        return v ? `v${v}` : '';
    });

    // The rule carries what the page publishes and nothing more. The version is not appended here:
    // the rail already prints it on every screen, and the same number in two places on one screen
    // reads as two numbers that happen to agree.
    protected readonly rulerRight = computed<readonly RulerReadout[]>(() => this.ruler.right());

    // The switcher is account-scoped wherever the project is unknown, which is every screen but
    // /projects/:id: DraftMeta carries no projectId (ADR-139 consequence), so a draft, a post or a
    // library asset cannot say which project it belongs to. A tile reading a project name there
    // would be a confident wrong answer rather than a missing one.
    protected readonly projectLabel = computed(() => {
        if (!this.auth.indieDev()) return '';
        const id = this.projectId();
        return (id && this.projectNames().get(id)) || this.t().shell.allProjects;
    });

    protected readonly hooks = computed<readonly HookRailItem[]>(() => {
        const t = this.t().shell;
        const id = this.projectId();
        const items: HookRailItem[] = [];
        if (this.auth.indieDev()) items.push({ id: 'hub', icon: 'game-controller', label: t.hub, link: '/projects' });
        items.push({ id: 'text', icon: 'pencil-simple', label: t.text, link: '/drafts' });
        // Without a project in the URL the board has no owner to open, so the hook lands on the
        // hub — the one screen that can name which board was meant. Same gap as the switcher.
        if (this.auth.indieDev()) items.push({ id: 'board', icon: 'check-square', label: t.board, link: id ? ['/projects', id, 'tasks'] : '/projects' });
        items.push({ id: 'assets', icon: 'images', label: t.assets, link: '/library' });
        items.push({
            id: 'metrics', icon: 'chart-bar', label: t.metrics, link: '/posts',
            badge: this.feedback.newComments() + this.feedback.newReactions(),
            badgeTitle: this.t().editor.newBadge,
        });
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

        effect(() => {
            const id = this.projectId();
            if (!id || !this.auth.indieDev() || this.namesRequested || this.projectNames().has(id)) return;
            this.namesRequested = true;
            this.projects.list(true)
                .then(list => this.projectNames.set(new Map(list.map(p => [p.id, p.name]))))
                .catch(() => { this.namesRequested = false; });
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
            default: return [];
        }
    }
}
