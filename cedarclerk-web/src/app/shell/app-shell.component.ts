import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { CommentsService } from '../core/comments.service';
import { CreditBalanceService } from '../core/credit-balance.service';
import { CurrentProjectService } from '../core/current-project.service';
import { DebugLogService } from '../core/debug-log.service';
import { LocaleService } from '../core/i18n/locale.service';
import { ProjectAccessService } from '../core/project-access.service';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
import { AppearancePanelComponent } from '../shared/appearance-panel.component';
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
// of the route tree (ADR-139 clause 1). The sidebar collapses to its rail on the editor and
// nowhere else, decided by the route (clause 5).
@Component({
    selector: 'app-shell',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        RouterOutlet, SidebarComponent, AppearancePanelComponent, FeedbackPanelComponent,
        SearchOverlayComponent, DebugConsoleComponent,
    ],
    host: {
        'data-surface': 'paper',
        '(document:keydown)': 'onKeydown($event)',
    },
    template: `
        <div class="shell" [class.is-rail]="mode() === 'rail'">
            <app-sidebar [mode]="mode()" [groups]="groups()" [foot]="foot()" [activeId]="activeId()"
                         [project]="project()" [projects]="switcher()" [projectHint]="t().shell.switchProject"
                         [user]="user()" [alerts]="alerts()" [navLabel]="t().shell.screens"
                         [brand]="t().shell.brand" [brandLabel]="t().shell.logoHome"
                         [allProjectsLabel]="t().shell.allProjects" [alertsTitle]="t().shell.alerts"
                         (openAppearance)="appearance().open.set(true)" />
            <main class="body" data-surface="paper">
                <router-outlet />
            </main>
        </div>

        <app-appearance-panel />
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

    protected readonly auth = inject(AuthService);
    protected readonly log = inject(DebugLogService);
    protected readonly t = inject(LocaleService).t;

    protected readonly appearance = viewChild.required(AppearancePanelComponent);

    private readonly url = signal(this.router.url);
    private readonly path = computed(() => this.url().split('?')[0].split('#')[0]);

    readonly mode = computed<'full' | 'rail'>(() => this.path().startsWith('/editor') ? 'rail' : 'full');

    /** Resolved once per shell; the switcher lists them and the counts are read off them. */
    private readonly summaries = signal<readonly ProjectSummary[]>([]);
    /** An empty list before the answer and an empty list for an account with no projects are the
        same value and mean opposite things. */
    private readonly namesLoaded = signal(false);
    private namesRequested = false;

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
        const kind = summary ? this.t().projects.projectTypes[summary.projectType]?.name ?? '' : '';
        const count = this.summaries().length;
        const sub = [kind, count ? this.t().shell.projectsCount(count) : ''].filter(Boolean).join(' · ');
        return { id, name: this.openName(), kind: sub, link: ['/projects', id] };
    });

    /** Every project, then the hub (ADR-186). Switching keeps the open screen (ADR-221). */
    protected readonly switcher = computed<readonly SidebarProject[]>(() => {
        if (!this.projectOpen() || !this.summaries().length) return [];
        const child = this.projectChild();
        const items: SidebarProject[] = this.summaries().map(p => ({
            id: p.id, name: p.name, kind: '',
            link: child ? ['/projects', p.id, child] : ['/projects', p.id],
        }));
        items.push({ id: '', name: this.t().shell.allProjects, kind: '', link: '/projects' });
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
        if (!this.auth.indieDev()) {
            write.push({ id: 'documents', label: t.documents, icon: 'file-text', link: '/drafts' });
            write.push({ id: 'assets', label: t.assets, icon: 'images', link: '/library' });
            plan.push(calendar);
            ship.push(posts, metrics);
        } else if (this.projectOpen() && role !== null && role !== 'owner') {
            write.push({ id: 'canvas', label: t.canvas, icon: 'squares-four', link: ['/projects', open, 'canvas'] });
            plan.push(calendar);
            ship.push(posts, metrics);
        } else if (this.projectOpen() && role === 'owner') {
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
        ];
    });

    protected readonly foot = computed<readonly NavItem[]>(() => {
        const t = this.t().shell;
        const items: NavItem[] = [];
        if (this.auth.indieDev()) items.push({ id: 'hub', label: t.allProjects, icon: 'folder-open', link: '/projects' });
        items.push({ id: 'settings', label: t.settings, icon: 'gear', link: '/settings' });
        return items;
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

        // One writer for the session's project, and this is it: the resolved route. An id the
        // loaded list does not hold is a project shared with this account, not its own.
        effect(() => {
            const id = this.projectId();
            const name = this.summaries().find(p => p.id === id)?.name;
            if (!id) return;
            if (!name && this.namesLoaded()) return;
            untracked(() => this.current.remember(id, name ?? ''));
        });
    }

    onKeydown(event: KeyboardEvent): void {
        if ((event.ctrlKey || event.metaKey) && !event.altKey && (event.key === '`' || event.code === 'Backquote')) {
            event.preventDefault();
            this.log.open.update(v => !v);
        }
    }
}
