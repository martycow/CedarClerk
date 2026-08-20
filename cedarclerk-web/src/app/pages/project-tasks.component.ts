import { Component, OnDestroy, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { formatInZone } from '../core/display-time';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { ProjectDetail, ProjectsService, DOCUMENT_TYPE_ICONS } from '../core/projects.service';
import {
    GameTask,
    LINK_TARGET_ICONS,
    LinkTarget,
    TASK_PRIORITIES,
    TASK_STATUSES,
    TaskPriority,
    TaskStatus,
    TasksService,
    isOverdue,
} from '../core/tasks.service';
import { Sprint, SprintsService } from '../core/sprints.service';
import { Build, BuildsService } from '../core/builds.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { RulerService } from '../core/ruler.service';
import { RulerReadout } from '../bench/chrome/ruler-bar.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';
import { StampBadgeComponent, StampTone } from '../bench/display/stamp-badge.component';
import { TaskTagComponent } from '../bench/display/task-tag.component';

const VIEW_KEY = 'cedar.taskView';

type Filter = 'all' | 'open' | 'overdue';
type SortKey = 'title' | 'status' | 'priority' | 'dueAt';

// T-123 (ADR-106) — the task board, from docs/design_handoff_indiedev_core_loop §3-4.
//
// Both views are required by the design and neither is a fallback: the board answers "what is
// happening", the list answers "what is due and in what order". They share one sorted source so
// they cannot disagree.
//
// The open task is a query parameter rather than component state, which is what makes a task
// linkable — the dashboard's "Up next" rail opens a card by navigating here.
@Component({
    selector: 'app-project-tasks',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, ModalComponent, RouterLink,
        ButtonComponent, InputComponent, IndexTabsComponent, ShelfPanelComponent,
        LeafTagComponent, StampBadgeComponent, TaskTagComponent,
    ],
    templateUrl: 'project-tasks.component.html',
    styleUrls: ['project-tasks.component.css'],
})
export class ProjectTasksComponent implements OnDestroy {
    private api = inject(TasksService);
    private projects = inject(ProjectsService);
    private sprintsApi = inject(SprintsService);
    private buildsApi = inject(BuildsService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private ruler = inject(RulerService);
    t = inject(LocaleService).t;

    readonly statuses = TASK_STATUSES;
    readonly priorities = TASK_PRIORITIES;
    readonly linkIcons = LINK_TARGET_ICONS;
    readonly docIcons = DOCUMENT_TYPE_ICONS;
    readonly overdue = isOverdue;

    projectId = signal('');
    project = signal<ProjectDetail | null>(null);
    tasks = signal<GameTask[]>([]);
    sprints = signal<Sprint[]>([]);
    builds = signal<Build[]>([]);
    /** null = every sprint; '' = the tasks in none of them. */
    sprintFilter = signal<string | null>(null);
    loading = signal(true);
    loadError = signal<string | null>(null);
    busy = signal(false);
    actionError = signal<string | null>(null);

    view = signal<'board' | 'list'>(this.loadView());
    filter = signal<Filter>('all');
    search = signal('');
    sort = signal<SortKey>('priority');
    sortAsc = signal(true);

    /** The task whose modal is open, or null. Mirrors the `task` query parameter. */
    openTaskId = signal<string | null>(null);

    creating = signal(false);
    newTitle = signal('');
    newStatus = signal<TaskStatus>('backlog');
    newPriority = signal<TaskPriority>(2);

    /** Edits live in the modal until saved, so a half-typed title never reaches the board. */
    draftTitle = signal('');
    draftDescription = signal('');
    draftAssignee = signal('');
    draftDue = signal('');
    linking = signal(false);

    openTask = computed(() => this.tasks().find(t => t.id === this.openTaskId()) ?? null);

    openCount = computed(() => this.tasks().filter(t => t.status !== 'done').length);

    overdueCount = computed(() => this.tasks().filter(t => isOverdue(t)).length);

    /** Documents of this project that the open task is not already linked to. */
    linkableDocuments = computed(() => {
        const task = this.openTask();
        const linked = new Set((task?.links ?? []).filter(l => l.type === 'document').map(l => l.id));
        return (this.project()?.documents ?? []).filter(d => !linked.has(d.id));
    });

    private matching = computed(() => {
        const needle = this.search().trim().toLowerCase();
        const filter = this.filter();
        const sprint = this.sprintFilter();
        return this.tasks().filter(t => {
            if (filter === 'open' && t.status === 'done') return false;
            if (filter === 'overdue' && !isOverdue(t)) return false;
            // '' is a filter in its own right — "planned into nothing" is a real question, and the
            // planner's own "No sprint" group asks it too.
            if (sprint !== null && (t.sprintId ?? '') !== sprint) return false;
            if (!needle) return true;
            return t.title.toLowerCase().includes(needle)
                || t.description.toLowerCase().includes(needle)
                || t.assignee.toLowerCase().includes(needle);
        });
    });

    /** The list view's own order. The board keeps the server's, which is already by column. */
    sorted = computed(() => {
        const key = this.sort();
        const dir = this.sortAsc() ? 1 : -1;
        return [...this.matching()].sort((a, b) => dir * this.compare(a, b, key));
    });

    empty = computed(() => !this.loading() && this.tasks().length === 0);

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });
        // A task id in the URL opens its card, including on a cold load or a shared link.
        this.route.queryParamMap.subscribe(q => this.openTaskId.set(q.get('task')));

        // The rail carries the project and the crumb, so what this screen has left to say is a
        // tally, and the rule is where a tally goes (ADR-159 clause 3).
        effect(() => {
            const tasks = this.tasks();
            const right: RulerReadout[] = [];
            const late = this.overdueCount();
            if (late) right.push({ text: `${late} ${this.t().projects.tasks.overdue}` });
            this.ruler.publish({
                label: this.project()?.name ?? '',
                left: [{ text: this.t().projects.tasks.sub(this.openCount(), tasks.length) }],
                right,
            });
        });

        // The modal's fields follow which task is open — and only that. Reading `tasks` untracked
        // is the point: every save replaces the array, and a reload must not throw away what is
        // being typed in a card that never closed.
        effect(() => {
            const id = this.openTaskId();
            if (!id) return;
            const task = untracked(() => this.tasks().find(t => t.id === id));
            if (task) this.beginEdit(task);
        });
    }

    ngOnDestroy(): void {
        this.ruler.clear();
    }

    async load() {
        const id = this.projectId();
        if (!id) return;
        this.loading.set(true);
        this.loadError.set(null);
        try {
            const [project, tasks, sprints, builds] = await Promise.all([
                this.projects.get(id), this.api.list(id), this.sprintsApi.list(id), this.buildsApi.list(id),
            ]);
            this.project.set(project);
            this.tasks.set(tasks);
            this.sprints.set(sprints);
            this.builds.set(builds);
            // Opened by URL before the tasks existed — the effect above could not fill the fields.
            const open = tasks.find(t => t.id === this.openTaskId());
            if (open) this.beginEdit(open);
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.tasks.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    column(status: TaskStatus) {
        return this.matching().filter(t => t.status === status);
    }

    columnCount(status: TaskStatus) {
        return this.tasks().filter(t => t.status === status).length;
    }

    /** The strip switches what this screen shows; it never leaves the screen (IndexTabs' rule). */
    viewTabs = computed<IndexTabItem[]>(() => [
        { id: 'board', label: this.t().projects.tasks.viewBoard },
        { id: 'list', label: this.t().projects.tasks.viewList },
    ]);

    /** Where a card and a row point: this screen, with the task in the query (ADR-163). */
    boardLink = computed(() => ['/projects', this.projectId(), 'tasks']);

    /** Alternating so a column of tags reads as a stack of paper rather than as a leaning tower. */
    tilt(index: number): number {
        return index % 2 === 0 ? -0.8 : 0.8;
    }

    /** The tag draws the date rust when it is late; the word is what says so without colour. */
    dueLabel(task: GameTask): string {
        if (!task.dueAt) return '';
        const date = formatInZone(task.dueAt, 'd MMM');
        return isOverdue(task) ? `${date} · ${this.t().projects.tasks.overdue}` : date;
    }

    statusTone(status: TaskStatus): StampTone {
        return status === 'done' ? 'pine' : status === 'in_progress' ? 'brass' : 'ink';
    }

    setView(view: 'board' | 'list') {
        this.view.set(view);
        try { localStorage.setItem(VIEW_KEY, view); } catch { /* private mode */ }
    }

    setSort(key: SortKey) {
        if (this.sort() === key) this.sortAsc.update(v => !v);
        else { this.sort.set(key); this.sortAsc.set(true); }
    }

    // ---- the card ---------------------------------------------------------

    close() {
        void this.router.navigate([], {
            relativeTo: this.route,
            queryParams: { task: null },
            queryParamsHandling: 'merge',
        });
    }

    /** Called when the modal opens, so the fields start from what is stored. */
    beginEdit(task: GameTask) {
        this.draftTitle.set(task.title);
        this.draftDescription.set(task.description);
        this.draftAssignee.set(task.assignee);
        this.draftDue.set(task.dueAt ? task.dueAt.slice(0, 10) : '');
    }

    async saveEdits(task: GameTask) {
        const title = this.draftTitle().trim();
        if (!title) { this.actionError.set(this.t().projects.tasks.titleRequired); return; }

        const due = this.draftDue().trim();
        await this.run(() => this.api.update(task.id, {
            title,
            description: this.draftDescription(),
            assignee: this.draftAssignee(),
            // An emptied date field is a request to clear it — which is a different request from
            // not touching it, and the API needs to be told which one this is.
            ...(due ? { dueAt: new Date(due).toISOString() } : { clearDueAt: true }),
        }));
    }

    setStatus(task: GameTask, status: TaskStatus) {
        return this.run(() => this.api.update(task.id, { status }));
    }

    setPriority(task: GameTask, priority: TaskPriority) {
        return this.run(() => this.api.update(task.id, { priority }));
    }

    toggleDone(task: GameTask) {
        // Reopening puts the task back where work happens, not back at the start of the queue.
        return this.setStatus(task, task.status === 'done' ? 'in_progress' : 'done');
    }

    toggleArchived(task: GameTask) {
        return this.run(() => this.api.update(task.id, { archived: !task.archivedAt }));
    }

    async remove(task: GameTask) {
        if (!confirm(this.t().projects.tasks.deleteConfirm)) return;
        this.close();
        await this.run(() => this.api.remove(task.id));
    }

    async addLink(task: GameTask, documentId: string) {
        this.linking.set(false);
        await this.run(() => this.api.link(task.id, 'document', documentId));
    }

    removeLink(task: GameTask, type: LinkTarget, id: string) {
        return this.run(() => this.api.unlink(task.id, type, id));
    }

    // ---- creating ---------------------------------------------------------

    startCreating(status: TaskStatus = 'backlog') {
        this.newTitle.set('');
        this.newStatus.set(status);
        this.newPriority.set(2);
        this.actionError.set(null);
        this.creating.set(true);
    }

    async create() {
        const title = this.newTitle().trim();
        if (!title) { this.actionError.set(this.t().projects.tasks.titleRequired); return; }

        const created = await this.run(() => this.api.create(this.projectId(), {
            title,
            status: this.newStatus(),
            priority: this.newPriority(),
        }));
        if (created) this.creating.set(false);
    }

    // ---- plumbing ---------------------------------------------------------

    /**
     * Every mutation reloads the board rather than patching one row: a status change moves a card
     * between columns and can change every other card's order, and reconciling that by hand is
     * three chances to show a board that does not match the database.
     */
    private async run<T>(action: () => Promise<T>): Promise<T | null> {
        this.busy.set(true);
        this.actionError.set(null);
        try {
            const result = await action();
            this.tasks.set(await this.api.list(this.projectId()));
            return result;
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.tasks.actionFailed));
            return null;
        } finally {
            this.busy.set(false);
        }
    }

    private compare(a: GameTask, b: GameTask, key: SortKey): number {
        switch (key) {
            case 'title': return a.title.localeCompare(b.title);
            case 'status': return TASK_STATUSES.indexOf(a.status) - TASK_STATUSES.indexOf(b.status);
            case 'priority': return a.priority - b.priority;
            case 'dueAt':
                // Undated tasks sort last in both directions: "no deadline" is not a late deadline,
                // and flipping the column should not fill the top of the table with blanks.
                if (!a.dueAt && !b.dueAt) return 0;
                if (!a.dueAt) return 1;
                if (!b.dueAt) return -1;
                return a.dueAt.localeCompare(b.dueAt);
        }
    }

    /** The sprint a task is in, or null. Used for the S-chip and the card's sprint field. */
    sprintOf(task: GameTask): Sprint | null {
        return this.sprints().find(s => s.id === task.sprintId) ?? null;
    }

    sprintTaskCount(sprintId: string): number {
        return this.tasks().filter(t => (t.sprintId ?? '') === sprintId).length;
    }

    setSprint(task: GameTask, sprintId: string) {
        return this.run(() => this.api.update(task.id, sprintId
            ? { sprintId }
            : { clearSprint: true }));
    }

    setBuild(task: GameTask, buildId: string) {
        return this.run(() => this.api.update(task.id, buildId ? { buildId } : { clearBuild: true }));
    }

    /** T-159 (ADR-134) — onto (or off) the project's public showcase roadmap; title and status only. */
    setPublicRoadmap(task: GameTask, isPublicRoadmap: boolean) {
        return this.run(() => this.api.update(task.id, { isPublicRoadmap }));
    }

    linkCounts(task: GameTask) {
        return {
            document: task.links.filter(l => l.type === 'document').length,
            asset: task.links.filter(l => l.type === 'asset').length,
            task: task.links.filter(l => l.type === 'task').length,
        };
    }

    private loadView(): 'board' | 'list' {
        try {
            return localStorage.getItem(VIEW_KEY) === 'list' ? 'list' : 'board';
        } catch {
            return 'board';
        }
    }
}
