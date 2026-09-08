import { ChangeDetectionStrategy, Component, HostListener, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { AppearanceService } from '../core/appearance.service';
import { CommentsService } from '../core/comments.service';
import { CreditBalanceService } from '../core/credit-balance.service';
import { CurrentProjectService } from '../core/current-project.service';
import { LocaleService } from '../core/i18n/locale.service';
import { OverlayCoordinatorService } from '../core/overlay-coordinator.service';
import { ProjectAccessService } from '../core/project-access.service';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
import { DebugConsoleComponent } from '../shared/debug-console.component';
import { FeedbackPanelComponent } from '../shared/feedback-panel.component';
import { SearchOverlayComponent } from '../shared/search-overlay.component';
import { NavGroup, NavItem, SidebarComponent, SidebarProject, SidebarUser } from './sidebar.component';

/** Which item stands for a path. Longest match first — the board is a child of the hub. */
const NAV_PREFIXES: readonly (readonly [string, string])[] = [
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
    ['posts', '/posts'],
    ['settings', '/settings'],
    ['glossary', '/glossary'],
    ['presets', '/presets'],
    ['teams', '/teams'],
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

// The paper-first shell (ADR-239): a sidebar beside the page, and nothing above or below it. A
// parent route rather than the root component so the pre-auth pages are outside it by the shape
// of the route tree (ADR-139 clause 1). Appearance owns the desktop width; a phone uses the rail
// so the document surface cannot be squeezed behind persistent navigation (ADR-248 clause 2).
@Component({
    selector: 'app-shell',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        RouterOutlet, SidebarComponent, FeedbackPanelComponent,
        SearchOverlayComponent, DebugConsoleComponent,
    ],
    host: {
        'data-surface': 'paper',
        '(document:keydown)': 'onKeydown($event)',
    },
    template: `
        <div class="shell" [class.is-rail]="mode() === 'rail'">
            <app-sidebar [mode]="mode()" [groups]="groups()" [activeId]="activeId()"
                         [project]="project()" [projects]="switcher()" [projectHint]="t().shell.switchProject"
                         [user]="user()" [alerts]="alerts()" [navLabel]="t().shell.screens"
                         [brand]="t().shell.brand" [brandLabel]="t().shell.logoLabel"
                         [allProjectsLabel]="t().shell.allProjects" [alertsTitle]="t().shell.alerts" />
            <main class="body" data-surface="paper">
                <router-outlet />
            </main>
        </div>

        <app-feedback-panel />
        <app-search-overlay />
        <app-debug-console />
    `,
    styles: [`
        :host { display: block; }

        /* The shell owns the viewport, so the page never sizes itself from it (ADR-239 clause 7). */
        .shell {
            position: fixed;
            inset: 0;
            display: flex;
            align-items: stretch;
        }

        /* The ground is the wall, and the wall carries the wall's ink (ADR-141); a card restates
           paper's. The pair is also handed down for what paints no ground of its own — a ghost
           button, a margin note — so it reads on the wall at night. */
        .body {
            display: flex;
            flex: 1;
            flex-direction: column;
            min-width: 0;
            min-height: 0;
            overflow: auto;
            background: var(--canvas);
            color: var(--wood-ink);
            --surface-ink: var(--wood-ink);
            --surface-ink-soft: var(--wood-ink-soft);
        }
    `],
})
export class AppShellComponent {
    private readonly router = inject(Router);
    private readonly projects = inject(ProjectsService);
    private readonly current = inject(CurrentProjectService);
    private readonly feedback = inject(CommentsService);
    private readonly access = inject(ProjectAccessService);
    private readonly creditBalance = inject(CreditBalanceService);
    private readonly overlays = inject(OverlayCoordinatorService);
    private readonly appearance = inject(AppearanceService);

    protected readonly auth = inject(AuthService);
    protected readonly t = inject(LocaleService).t;

    protected readonly search = viewChild.required(SearchOverlayComponent);

    private readonly url = signal(this.router.url);
    private readonly path = computed(() => this.url().split('?')[0].split('#')[0]);

    private readonly phoneViewport = signal(window.innerWidth <= 640);
    readonly mode = computed(() => this.phoneViewport() ? 'rail' : this.appearance.prefs().sidebarMode);

    /** Resolved once per shell; the switcher lists them and the counts are read off them. */
    private readonly summaries = signal<readonly ProjectSummary[]>([]);
    /** An empty list before the answer and an empty list for an account with no projects are the
        same value and mean opposite things. */
    private readonly namesLoaded = signal(false);
    private namesRequested = false;
    /** A project created while this shell is mounted is absent from the list fetched at login. */
    private nameRefreshId = '';

    protected readonly projectId = computed(() => {
        const seg = this.path().split('/').filter(Boolean);
        return seg[0] === 'projects' && seg[1] ? seg[1] : '';
    });

    /** The project the session is in: the URL's when it names one, the remembered one otherwise. */
    protected readonly openProjectId = computed(() => this.projectId() || this.current.id());

    /** The hub is the one screen where no project is open. */
    private readonly onHub = computed(() => this.path().replace(/\/+$/, '') === '/projects');

    private readonly projectChild = computed(() => {
        const seg = this.path().split('/').filter(Boolean);
        return seg[0] === 'projects' && seg[2] && PROJECT_CHILDREN.has(seg[2]) ? seg[2] : '';
    });

    private readonly openSummary = computed(() => {
        const id = this.openProjectId();
        return id ? this.summaries().find(p => p.id === id) ?? null : null;
    });

    private readonly openName = computed(() => {
        const id = this.openProjectId();
        const remembered = this.current.id() === id ? this.current.name() : '';
        return this.openSummary()?.name || remembered;
    });

    /** T-301 — the caller's relationship to the open project; null while in flight. */
    protected readonly projectRole = computed(() => {
        const open = this.openProjectId();
        if (!open) return null;
        this.access.ensure(open);
        return this.access.accessFor(open)?.role ?? null;
    });

    private readonly projectOpen = computed(() =>
        this.auth.indieDev() && !!this.openProjectId() && !this.onHub());

    /** The switcher card: the open project, or the hub itself while none is open (module on). */
    protected readonly project = computed<SidebarProject | null>(() => {
        if (!this.auth.indieDev()) return null;
        if (!this.projectOpen()) return { id: '', name: this.t().shell.allProjects, kind: '', link: '/projects' };
        const id = this.openProjectId();
        const summary = this.openSummary();
        const kind = summary ? this.t().projects.projectTypes[summary.createdFromPreset]?.name ?? '' : '';
        const count = this.summaries().length;
        const sub = [kind, count ? this.t().shell.projectsCount(count) : ''].filter(Boolean).join(' · ');
        return { id, name: this.openName(), kind: sub, link: ['/projects', id] };
    });

    protected readonly switcher = computed<readonly SidebarProject[]>(() => {
        if (!this.auth.indieDev()) return [];
        const child = this.projectChild();
        const onWorkspaceAction = this.onHub() || this.path().replace(/\/+$/, '') === '/teams';
        const items: SidebarProject[] = this.summaries().map(p => ({
            id: p.id, name: p.name, kind: '',
            link: child ? ['/projects', p.id, child] : ['/projects', p.id],
            active: onWorkspaceAction ? false : undefined,
        }));
        items.push({
            id: '', name: this.t().shell.allProjects, kind: '', link: '/projects',
            icon: 'folder-open', separatorBefore: !!items.length, active: this.onHub(),
        });
        items.push({
            id: '__teams__', name: this.t().shell.manageTeams, kind: '', link: '/teams', icon: 'user',
            active: this.path().replace(/\/+$/, '') === '/teams',
        });
        return items;
    });

    protected readonly alerts = computed(() => this.feedback.newComments() + this.feedback.newReactions());

    protected readonly groups = computed<readonly NavGroup[]>(() => {
        const t = this.t().shell;
        const open = this.openProjectId();
        const role = this.projectRole();
        const summary = this.openSummary();
        const write: NavItem[] = [], plan: NavItem[] = [], ship: NavItem[] = [];
        const count = (n: number | undefined) => (n && n > 0 ? n : undefined);
        const calendar: NavItem = { id: 'calendar', label: t.calendar, icon: 'calendar-blank', link: '/calendar' };
        const posts: NavItem = { id: 'posts', label: t.posts, icon: 'paper-plane-tilt', link: '/posts' };
        const metrics: NavItem = {
            id: 'metrics', label: t.metrics, icon: 'chart-bar', link: '/posts', queryParams: { tab: 'stats' },
            count: count(this.alerts()), countTitle: this.t().editor.newBadge,
        };
        // An own project is one the account's own list holds; the access answer only ever
        // narrows that (a member), so a project still in flight draws its owner's wall rather
        // than nothing — an empty sidebar was the bug this replaces.
        const own = !!summary || role === 'owner';
        const library: NavItem[] = [
            { id: 'glossary', label: this.t().glossary.crumb, icon: 'book-bookmark', link: '/glossary' },
            { id: 'presets', label: this.t().presets.crumb, icon: 'squares-four', link: '/presets' },
        ];
        if (!this.auth.indieDev() || !this.projectOpen()) {
            write.push({ id: 'documents', label: t.documents, icon: 'file-text', link: '/drafts' });
            write.push({ id: 'assets', label: t.assets, icon: 'images', link: '/library' });
            plan.push(calendar);
            ship.push(posts, metrics);
        } else if (!own && role !== null) {
            write.push({ id: 'canvas', label: t.canvas, icon: 'squares-four', link: ['/projects', open, 'canvas'] });
            plan.push(calendar);
            ship.push(posts, metrics);
        } else if (!own) {
            plan.push(calendar);
            ship.push(posts, metrics);
        } else {
            write.push({ id: 'documents', label: t.documents, icon: 'file-text', link: ['/projects', open], count: count(summary?.documentCount) });
            write.push({ id: 'assets', label: t.assets, icon: 'images', link: ['/projects', open, 'assets'], count: count(summary?.assetCount) });
            write.push({ id: 'canvas', label: t.canvas, icon: 'squares-four', link: ['/projects', open, 'canvas'] });
            write.push({ id: 'dialogues', label: t.dialogues, icon: 'tree-structure', link: ['/projects', open, 'dialogues'] });
            write.push({ id: 'showcase', label: t.showcase, icon: 'globe', title: this.t().projects.showcase.title, link: ['/projects', open, 'showcase'] });
            plan.push({ id: 'board', label: t.board, icon: 'check-square', link: ['/projects', open, 'tasks'], count: count(summary?.openTaskCount) });
            plan.push({ id: 'planner', label: t.planner, icon: 'flag', link: ['/projects', open, 'planner'] });
            plan.push(calendar);
            ship.push({ id: 'builds', label: t.builds, icon: 'cube', link: ['/projects', open, 'builds'] });
            ship.push(posts, metrics);
        }
        return [
            { id: 'write', label: t.groupWrite, items: write },
            { id: 'plan', label: t.groupPlan, items: plan },
            { id: 'ship', label: t.groupShip, items: ship },
            { id: 'library', label: t.groupLibrary, items: library },
        ];
    });

    protected readonly activeId = computed(() => {
        const id = NAV_PREFIXES.find(([, pattern]) => matches(this.path(), pattern))?.[0] ?? '';
        if (id === 'posts' && /[?&]tab=stats(&|$)/.test(this.url())) return 'metrics';
        return id;
    });

    protected readonly user = computed<SidebarUser>(() => {
        const email = this.auth.userEmail() ?? '';
        const name = this.auth.authorDisplayName() || email.split('@')[0];
        return { name, avatarUrl: this.auth.avatarUrl(), initial: (name[0] ?? '?').toUpperCase() };
    });

    constructor() {
        this.feedback.refreshNewCount();

        this.router.events.subscribe(e => {
            if (e instanceof NavigationEnd) this.url.set(e.urlAfterRedirects);
        });

        effect(() => {
            this.url();
            if (this.auth.userEmail()) void this.creditBalance.refresh();
        });

        effect(() => {
            if (!this.auth.indieDev() || this.namesRequested) return;
            this.namesRequested = true;
            this.projects.list(true)
                .then(list => {
                    this.summaries.set(list);
                    this.namesLoaded.set(true);
                    this.current.reconcile(list);
                })
                .catch(() => { this.namesRequested = false; });
        });

        // One writer for the session's project, and this is it: the resolved route. The project
        // list can predate a newly created row, so an unknown route id gets one fresh lookup.
        effect(() => {
            const id = this.projectId();
            const name = this.summaries().find(p => p.id === id)?.name;
            if (!id) return;
            if (name) {
                untracked(() => this.current.remember(id, name));
                return;
            }
            if (!this.namesLoaded()) {
                untracked(() => this.current.remember(id, ''));
                return;
            }
            if (this.nameRefreshId === id) return;
            this.nameRefreshId = id;
            untracked(() => void this.refreshProjectName(id));
        });
    }

    private async refreshProjectName(id: string): Promise<void> {
        try {
            const list = await this.projects.list(true);
            this.summaries.set(list);
            const found = list.find(project => project.id === id);
            if (found) {
                this.current.remember(found.id, found.name);
                return;
            }
        } catch {
            // A route the account can open still has a detail response even if the list failed.
        }

        try {
            const detail = await this.projects.get(id);
            this.current.remember(detail.id, detail.name);
        } catch {
            // The page owns its not-found state; the shell keeps the neutral placeholder.
        }
    }

    onKeydown(event: KeyboardEvent): void {
        if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'k') {
            event.preventDefault();
            this.search().toggleOverlay();
            return;
        }
        if ((event.ctrlKey || event.metaKey) && !event.altKey && (event.key === '`' || event.code === 'Backquote')) {
            event.preventDefault();
            this.overlays.toggle('debug');
        }
    }

    @HostListener('window:resize')
    onResize(): void {
        this.phoneViewport.set(window.innerWidth <= 640);
    }
}
