import { VersionService } from '../core/version.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { ChangeDetectionStrategy, Component, HostListener, OnDestroy, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { IconComponent } from '../shared/icon.component';
import { AuthService } from '../core/auth.service';
import { AppearanceService } from '../core/appearance.service';
import { AppCommand, CommandRelease, CommandsService } from '../core/commands.service';
import { CommentsService } from '../core/comments.service';
import { CreditBalanceService } from '../core/credit-balance.service';
import { CurrentProjectService } from '../core/current-project.service';
import { LocaleService } from '../core/i18n/locale.service';
import { OverlayCoordinatorService } from '../core/overlay-coordinator.service';
import { ProjectAccessService } from '../core/project-access.service';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
import { CommandPaletteComponent } from '../shared/command-palette.component';
import { DebugConsoleComponent } from '../shared/debug-console.component';
import { FeedbackPanelComponent } from '../shared/feedback-panel.component';
import { FeedbackFormService } from '../core/feedback-form.service';
import { SearchOverlayComponent } from '../shared/search-overlay.component';
import { InspectorRailComponent } from './inspector-rail.component';
import { ProjectSwitcherComponent } from './project-switcher.component';
import { MenuBarComponent } from './menu-bar.component';
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
    ['metrics', '/metrics'],
    ['forms', '/forms'],
    ['settings', '/settings'],
    ['glossary', '/glossary'],
    ['presets', '/presets'],
    ['ai', '/ai'],
    ['teams', '/teams'],
    ['admin', '/admin'],
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
        RouterOutlet, RouterLink, IconComponent, SidebarComponent, FeedbackPanelComponent, CedarLogoComponent,
        SearchOverlayComponent, DebugConsoleComponent,
        MenuBarComponent, InspectorRailComponent, CommandPaletteComponent, ProjectSwitcherComponent,
    ],
    host: {
        'data-surface': 'paper',
        '(document:keydown)': 'onKeydown($event)',
    },
    template: `
        <div class="shell" [class.is-rail]="mode() === 'rail'">
            <app-menu-bar>
                <a class="app-brand" routerLink="/projects" [attr.aria-label]="t().shell.allProjects">
                    <app-cedar-logo [size]="28" />
                    <span class="app-name">{{ t().shell.brand }}
                        @if (version.version(); as value) { <small class="app-version">v{{ value }}</small> }
                    </span>
                </a>
                <app-project-switcher [project]="project()" [projects]="switcher()" variant="inline"
                    [hint]="t().shell.switchProject" [fallbackName]="t().shell.allProjects" />
                <button type="button" class="bar-icon" [attr.aria-label]="t().shell.commands.toggleSidebar"
                        [attr.title]="t().shell.commands.toggleSidebar" [attr.aria-expanded]="mode() === 'full'"
                        (click)="toggleSidebar()">
                    <app-icon name="layout" size="sm" />
                </button>
                <div bar-end class="bar-end">
                    <button type="button" class="bar-icon" [attr.aria-label]="fullscreenLabel()" [attr.title]="fullscreenLabel()"
                            (click)="toggleFullscreen()">
                        <app-icon [name]="isFullscreen() ? 'arrows-in-simple' : 'arrows-out-simple'" size="sm" />
                    </button>
                    <button type="button" class="bar-logout" (click)="auth.logout()">
                        <app-icon name="sign-out" size="sm" /><span>{{ t().editor.logout }}</span>
                    </button>
                </div>
            </app-menu-bar>
            <div class="workspace-row">
                <app-sidebar [mode]="mode()" [groups]="groups()" [activeId]="activeId()"
                             [project]="project()" [projects]="switcher()" [projectHint]="t().shell.switchProject"
                             [user]="user()" [navLabel]="t().shell.screens"
                             [feedbackLabel]="t().feedbackForm.title" (feedback)="feedbackForm.openForm()"
                             [brand]="t().shell.brand" [brandLabel]="t().shell.logoLabel"
                             [allProjectsLabel]="t().shell.allProjects" />
                <main class="body" data-surface="paper">
                    <router-outlet />
                </main>
                <app-inspector-rail [startCollapsed]="isProjectOverview()" />
            </div>
        </div>

        <app-feedback-panel />
        <app-search-overlay />
        <app-command-palette />
        <app-debug-console />
    `,
    styles: [`
        :host { display: block; }
        .app-brand { display: flex; align-items: center; gap: var(--space-2); padding: var(--space-1) var(--space-3); flex: none; border-radius: var(--radius-sm); color: inherit; text-decoration: none; }
        .app-brand:hover { background: var(--hover); }
        .app-brand:focus-visible, .bar-icon:focus-visible, .bar-logout:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
        .bar-icon, .bar-logout { display: inline-flex; align-items: center; justify-content: center; gap: var(--space-1); flex: none; align-self: center; min-width: var(--hit-chrome); min-height: var(--hit-chrome); padding: 0 var(--space-2); border: 0; border-radius: var(--radius-sm); background: transparent; color: inherit; font: inherit; font-size: var(--fs-ui); cursor: pointer; }
        .bar-icon:hover, .bar-logout:hover { background: var(--hover); }
        .bar-end { display: flex; align-items: center; gap: var(--space-1); padding-right: var(--space-2); }
        @media (max-width: 640px) { .bar-logout span { display: none; } }
        .app-name { display: flex; flex-direction: column; font-family: var(--font-sans); font-size: var(--text-chrome); font-weight: 700; white-space: nowrap; }
        .app-version { font-size: var(--text-chrome-sm); color: var(--t2); font-weight: 400; }


        /* The shell owns the viewport, so the page never sizes itself from it (ADR-239 clause 7).
           ADR-301 clause 1 puts the menu row above it — the one thing ADR-239 said would never be
           there, and the reason that clause is superseded for the signed-in shell. */
        .shell {
            position: fixed;
            inset: 0;
            display: flex;
            flex-direction: column;
            align-items: stretch;
        }

        .workspace-row {
            position: relative;
            display: flex;
            flex: 1;
            align-items: stretch;
            min-height: 0;
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
export class AppShellComponent implements OnDestroy {
    protected readonly version = inject(VersionService);
    private readonly router = inject(Router);
    private readonly projects = inject(ProjectsService);
    private readonly current = inject(CurrentProjectService);
    private readonly feedback = inject(CommentsService);
    private readonly access = inject(ProjectAccessService);
    private readonly creditBalance = inject(CreditBalanceService);
    private readonly overlays = inject(OverlayCoordinatorService);
    private readonly appearance = inject(AppearanceService);
    private readonly commands = inject(CommandsService);
    protected readonly feedbackForm = inject(FeedbackFormService);

    protected readonly auth = inject(AuthService);
    protected readonly t = inject(LocaleService).t;

    protected readonly search = viewChild.required(SearchOverlayComponent);
    protected readonly palette = viewChild.required(CommandPaletteComponent);
    private readonly inspector = viewChild.required(InspectorRailComponent);

    private commandRelease?: CommandRelease;

    private readonly url = signal(this.router.url);
    protected readonly isProjectOverview = computed(() => /^\/projects\/[^/]+\/?$/.test(this.path()));
    private readonly path = computed(() => this.url().split('?')[0].split('#')[0]);

    private readonly phoneViewport = signal(window.innerWidth <= 640);
    protected readonly isFullscreen = signal(!!document.fullscreenElement);

    protected fullscreenLabel(): string {
        return this.isFullscreen() ? this.t().editor.exitFullscreen : this.t().editor.enterFullscreen;
    }

    protected async toggleFullscreen(): Promise<void> {
        try {
            if (document.fullscreenElement) await document.exitFullscreen();
            else await document.documentElement.requestFullscreen();
        } catch {
            // A browser or desktop policy may refuse fullscreen; the current shell stays usable.
        }
    }

    @HostListener('document:fullscreenchange')
    onFullscreenChange(): void {
        this.isFullscreen.set(!!document.fullscreenElement);
    }

    toggleSidebar() { this.setPref({ sidebarMode: this.appearance.prefs().sidebarMode === 'rail' ? 'full' : 'rail' }); }
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
        return seg[0] === 'projects' && seg[1] ? seg[1] : new URLSearchParams(this.url().split('?')[1] ?? '').get('project') ?? '';
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
        !!this.openProjectId() && !this.onHub());

    /** The switcher card: the open project, or the hub itself while none is open (module on). */
    protected readonly project = computed<SidebarProject | null>(() => {
        if (!this.projectOpen()) return { id: '', name: this.t().shell.allProjects, kind: '', link: '/projects' };
        const id = this.openProjectId();
        const summary = this.openSummary();
        const kind = summary ? this.t().projects.projectTypes[summary.createdFromPreset]?.name ?? '' : '';
        const count = this.summaries().length;
        const sub = [kind, count ? this.t().shell.projectsCount(count) : ''].filter(Boolean).join(' · ');
        return { id, name: this.openName(), kind: sub, link: ['/projects', id] };
    });

    protected readonly switcher = computed<readonly SidebarProject[]>(() => {
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
        const posts: NavItem = { id: 'posts', label: t.posts, icon: 'paper-plane-tilt', link: '/posts',
            count: count(this.alerts()), countTitle: this.t().editor.newBadge };
        // ADR-316: Metrics and Forms are pages of their own, beside Publishing in Ship.
        const metrics: NavItem = { id: 'metrics', label: t.metrics, icon: 'chart-bar', link: '/metrics' };
        const forms: NavItem = { id: 'forms', label: t.forms, icon: 'clipboard-text', link: '/forms' };
        // An own project is one the account's own list holds; the access answer only ever
        // narrows that (a member), so a project still in flight draws its owner's wall rather
        // than nothing — an empty sidebar was the bug this replaces.
        const own = !!summary || role === 'owner';
        const library: NavItem[] = [
            { id: 'glossary', label: this.t().glossary.crumb, icon: 'book-bookmark', link: '/glossary' },
            { id: 'presets', label: this.t().presets.crumb, icon: 'squares-four', link: '/presets' },
            { id: 'ai', label: this.t().ai.crumb, icon: 'sparkle', link: '/ai' },
        ];
        // ADR-322: with the module on, the workspace screens exist only inside an own project.
        if (!this.auth.indieDev()) {
            write.push({ id: 'documents', label: t.documents, icon: 'file-text', link: '/drafts' });
            write.push({ id: 'assets', label: t.assets, icon: 'images', link: '/library' });
            plan.push(calendar);
            ship.push(posts, metrics, forms);
        } else if (!this.projectOpen()) {
            // The hub: pick a project first.
        } else if (!own && role !== null) {
            write.push({ id: 'canvas', label: t.canvas, icon: 'squares-four', link: ['/projects', open, 'canvas'] });
        } else if (!own) {
            // Access still in flight for a project that is not in the account's list.
        } else {
            calendar.queryParams = { project: open };
            posts.queryParams = { project: open };
            metrics.queryParams = { project: open };
            forms.queryParams = { project: open };
            write.push({ id: 'documents', label: t.documents, icon: 'file-text', link: '/drafts', queryParams: { project: open }, count: count(summary?.documentCount) });
            write.push({ id: 'assets', label: t.assets, icon: 'images', link: ['/projects', open, 'assets'], count: count(summary?.assetCount) });
            write.push({ id: 'canvas', label: t.canvas, icon: 'squares-four', link: ['/projects', open, 'canvas'] });
            write.push({ id: 'dialogues', label: t.dialogues, icon: 'tree-structure', link: ['/projects', open, 'dialogues'] });
            write.push({ id: 'showcase', label: t.showcase, icon: 'globe', title: this.t().projects.showcase.title, link: ['/projects', open, 'showcase'] });
            plan.push({ id: 'board', label: t.board, icon: 'check-square', link: ['/projects', open, 'tasks'], count: count(summary?.openTaskCount) });
            plan.push({ id: 'planner', label: t.planner, icon: 'flag', link: ['/projects', open, 'planner'] });
            plan.push(calendar);
            ship.push({ id: 'builds', label: t.builds, icon: 'cube', link: ['/projects', open, 'builds'] });
            ship.push(posts, metrics, forms);
        }
        const admin: NavItem[] = this.auth.isAdmin()
            ? [{ id: 'admin', label: t.admin, icon: 'shield-check', link: '/admin' }]
            : [];
        return [
            { id: 'write', label: t.groupWrite, items: write },
            { id: 'plan', label: t.groupPlan, items: plan },
            { id: 'ship', label: t.groupShip, items: ship },
            { id: 'library', label: t.groupLibrary, items: library },
            { id: 'admin', label: t.developer, items: admin },
        ];
    });

    protected readonly activeId = computed(() => {
        const id = NAV_PREFIXES.find(([, pattern]) => matches(this.path(), pattern))?.[0] ?? '';
        return id;
    });

    protected readonly user = computed<SidebarUser>(() => {
        const email = this.auth.userEmail() ?? '';
        const name = this.auth.authorDisplayName() || email.split('@')[0];
        return { name, avatarUrl: this.auth.avatarUrl(), initial: (name[0] ?? '?').toUpperCase() };
    });

    /** ADR-301 clause 2 — the account-wide set. A page adds its own on mount; later wins a
        collision, so a screen's Save replaces this one without either side knowing about the other. */
    private shellCommands(): readonly AppCommand[] {
        const labels = this.t().shell.commands;
        const go = (path: string, queryParams?: Record<string, string>) =>
            () => void this.router.navigate([path], queryParams ? { queryParams } : {});
        // projectScopeGuard sends a project-scoped screen with no open project back to the hub,
        // which from the hub itself is a click that does nothing — so the row greys out instead.
        const inProject = () => !this.auth.indieDev() || !!this.openProjectId();
        const commands: AppCommand[] = [
            { id: 'file.new', group: 'file', label: labels.newDocument, icon: 'plus', enabled: inProject, run: go('/drafts', { new: '1' }) },
            { id: 'file.documents', group: 'file', label: labels.openDocuments, icon: 'file-text', enabled: inProject, run: go('/drafts') },
            { id: 'file.library', group: 'file', label: labels.library, icon: 'images', enabled: inProject, run: go('/library') },
            {
                id: 'file.download', group: 'file', label: labels.download, icon: 'download-simple',
                separatorBefore: true, run: go('/download'),
            },
            {
                id: 'edit.search', group: 'edit', label: labels.search, icon: 'magnifying-glass',
                shortcut: 'Ctrl+K', run: () => this.search().openOverlay(),
            },
            { id: 'edit.glossary', group: 'edit', label: labels.glossary, icon: 'book-bookmark', separatorBefore: true, run: go('/glossary') },
            { id: 'edit.presets', group: 'edit', label: labels.presets, icon: 'squares-four', run: go('/presets') },
            {
                id: 'view.sidebar', group: 'view', label: labels.toggleSidebar, icon: 'layout',
                checked: () => this.appearance.prefs().sidebarMode === 'rail',
                run: () => this.setPref({ sidebarMode: this.appearance.prefs().sidebarMode === 'rail' ? 'full' : 'rail' }),
            },
            {
                id: 'view.inspector', group: 'view', label: labels.toggleInspector, icon: 'list',
                checked: () => this.inspector().open(),
                run: () => this.inspector().toggle(),
            },
            {
                id: 'view.theme', group: 'view', label: labels.toggleTheme, icon: 'moon',
                checked: () => this.appearance.paintedTheme() === 'dark',
                run: () => this.setPref({ theme: this.appearance.paintedTheme() === 'dark' ? 'light' : 'dark' }),
            },
            { id: 'view.calendar', group: 'view', label: labels.calendar, icon: 'calendar-blank', separatorBefore: true, enabled: inProject, run: go('/calendar') },
            { id: 'view.posts', group: 'view', label: labels.posts, icon: 'paper-plane-tilt', enabled: inProject, run: go('/posts') },
            {
                id: 'tools.palette', group: 'tools', label: labels.palette, icon: 'terminal-window',
                shortcut: 'Ctrl+Shift+P', run: () => this.palette().openOverlay(),
            },
            { id: 'tools.ai', group: 'tools', label: labels.aiOperations, icon: 'sparkle', run: go('/ai') },
            { id: 'tools.settings', group: 'tools', label: labels.settings, icon: 'gear', separatorBefore: true, run: go('/settings') },
            {
                id: 'tools.debug', group: 'tools', label: labels.debugConsole, icon: 'terminal-window',
                shortcut: 'Ctrl+`', run: () => this.overlays.toggle('debug'),
            },
            { id: 'help.terms', group: 'help', label: labels.terms, icon: 'info', run: go('/terms') },
            { id: 'help.privacy', group: 'help', label: labels.privacy, icon: 'shield-check', run: go('/privacy') },
            { id: 'help.about', group: 'help', label: this.t().shell.aboutLanding, icon: 'tree-evergreen', separatorBefore: true, run: () => { window.location.href = '/welcome'; } },
        ];
        if (this.auth.isAdmin()) {
            commands.push(
                { id: 'tools.styleguide', group: 'tools', label: labels.styleguide, icon: 'palette', separatorBefore: true, run: go('/dev/styleguide') },
                { id: 'tools.icons', group: 'tools', label: labels.icons, icon: 'image', run: go('/dev/icons') },
            );
        }
        return commands;
    }

    private setPref(patch: Parameters<AppearanceService['preview']>[0]): void {
        this.appearance.preview(patch);
        void this.appearance.commit();
    }

    ngOnDestroy(): void {
        this.commandRelease?.();
    }

    constructor() {
        this.feedback.refreshNewCount();

        // Labels are locale-bound, so the set is rebuilt when the dictionary or the admin flag
        // changes — a menu row that kept its English label after a language switch was the bug.
        effect(() => {
            this.t();
            this.auth.isAdmin();
            untracked(() => {
                this.commandRelease?.();
                this.commandRelease = this.commands.register(this.shellCommands());
            });
        });

        this.router.events.subscribe(e => {
            if (e instanceof NavigationEnd) this.url.set(e.urlAfterRedirects);
        });

        effect(() => {
            this.url();
            if (this.auth.userEmail()) void this.creditBalance.refresh();
        });

        effect(() => {
            if (this.namesRequested) return;
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
            if (this.onHub()) {
                untracked(() => this.current.forget());
                return;
            }
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
        // Shift first: Ctrl+Shift+P is a command, Ctrl+P is the browser's print and stays its own.
        if ((event.ctrlKey || event.metaKey) && event.shiftKey && !event.altKey
            && (event.key.toLowerCase() === 'p' || event.code === 'KeyP')) {
            event.preventDefault();
            this.palette().toggleOverlay();
            return;
        }
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
