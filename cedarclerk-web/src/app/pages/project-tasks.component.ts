import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CdkDropList, CdkDrag, CdkDropListGroup, CdkDragDrop } from '@angular/cdk/drag-drop';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { dayInZone, formatInZone, wallClockToInstant } from '../core/display-time';
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
} from '../core/tasks.service';
import { Sprint, SprintsService } from '../core/sprints.service';
import { Build, BuildsService } from '../core/builds.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { AssetsService } from '../core/assets.service';
import { SortHeaderComponent } from '../bench/worktop/sort-header.component';
import { ariaSort } from '../core/collection-query';

const VIEW_KEY = 'cedar.taskView';
const MS_PER_DAY = 86_400_000;

function civilDayNumber(day: string): number {
    const [year, month, date] = day.split('-').map(Number);
    return Date.UTC(year, month - 1, date);
}

type Filter = 'all' | 'open' | 'overdue';
type SortKey = 'title' | 'status' | 'priority' | 'dueAt' | 'links';
type Tone = 'ok' | 'warn' | 'muted' | 'danger';

// T-123 (ADR-106) — the task board. Board.png (ADR-239): a header carrying the tally, the sprint
// covering today and its days left; the view, state and sprint strips with the search; four
// full-height columns, each naming what lands in it when it holds nothing.
//
// Both views are required by the design and neither is a fallback: the board answers "what is
// happening", the list answers "what is due and in what order". They share one sorted source so
// they cannot disagree.
//
// The open task is a query parameter rather than component state, which is what makes a task
// linkable — the dashboard's "Up next" card opens a card by navigating here.
@Component({
    selector: 'app-project-tasks',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, ModalComponent, RouterLink,
        PageHeaderComponent, EmptyStateComponent, ButtonComponent, InputComponent,
        CdkDropListGroup, CdkDropList, CdkDrag,
        SortHeaderComponent,
    ],
    templateUrl: 'project-tasks.component.html',
    styleUrls: ['project-tasks.component.css'],
})
export class ProjectTasksComponent {
    private api = inject(TasksService);
    private assets = inject(AssetsService);
    private projects = inject(ProjectsService);
    private sprintsApi = inject(SprintsService);
    private buildsApi = inject(BuildsService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    t = inject(LocaleService).t;

    readonly statuses = TASK_STATUSES;
    readonly priorities = TASK_PRIORITIES;
    readonly linkIcons = LINK_TARGET_ICONS;
    readonly docIcons = DOCUMENT_TYPE_ICONS;
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
    newDescription = signal('');
    newFiles = signal<File[]>([]);
    newStatus = signal<TaskStatus>('backlog');
    newPriority = signal<TaskPriority>(2);

    /** Edits live in the modal until saved, so a half-typed title never reaches the board. */
    draftTitle = signal('');
    draftDescription = signal('');
    draftAssignee = signal('');
    draftDue = signal('');
    draftStatus = signal<TaskStatus>('backlog');
    draftPriority = signal<TaskPriority>(2);
    draftSprintId = signal('');
    draftBuildId = signal('');
    draftPublicRoadmap = signal(false);
    linking = signal(false);

    openTask = computed(() => this.tasks().find(t => t.id === this.openTaskId()) ?? null);

    openCount = computed(() => this.tasks().filter(t => t.status !== 'done').length);

    overdueCount = computed(() => this.tasks().filter(t => this.overdue(t)).length);

    /** The sprint covering today — the header's second readout, and the "Planned" column's hint. */
    currentSprint = computed<Sprint | null>(() => this.project()?.currentSprint ?? null);

    /** Days from today to the current sprint's end, floored at zero. */
    sprintDaysLeft = computed(() => {
        const sprint = this.currentSprint();
        if (!sprint) return 0;
        const endDay = civilDayNumber(dayInZone(sprint.endsAt));
        const today = civilDayNumber(dayInZone(new Date()));
        return Math.max(0, Math.round((endDay - today) / MS_PER_DAY));
    });

    /** The former rule readouts, in the same order (CONTRACT §D1): the tally, the sprint, the late count. */
    headerMeta = computed<HeaderMeta[]>(() => {
        const t = this.t().projects;
        const meta: HeaderMeta[] = [{ text: t.tasks.openCount(this.openCount()) }];
        const sprint = this.currentSprint();
        if (sprint) {
            meta.push({ text: t.tasks.sprintMeta(sprint.number, sprint.name) });
            meta.push({ text: t.hub.daysLeft(this.sprintDaysLeft()) });
        }
        const late = this.overdueCount();
        if (late) meta.push({ text: t.tasks.overdueCount(late), tag: true, tone: 'warn' });
        return meta;
    });

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
            if (filter === 'overdue' && !this.overdue(t)) return false;
            // '' is a filter in its own right — "planned into nothing" is a real question, and the
            // planner's own "No sprint" group asks it too.
            if (sprint !== null && (t.sprintId ?? '') !== sprint) return false;
            if (!needle) return true;
            return t.title.toLowerCase().includes(needle)
                || t.description.toLowerCase().includes(needle)
                || t.assignee.toLowerCase().includes(needle);
        });
    });

    hasCollectionFilters = computed(() => this.filter() !== 'all'
        || this.sprintFilter() !== null
        || this.search().trim().length > 0);
    matchingCount = computed(() => this.matching().length);

    /** The list view's own order. The board keeps the server's, which is already by column. */
    sorted = computed(() => {
        const key = this.sort();
        const dir = this.sortAsc() ? 1 : -1;
        return [...this.matching()].sort((a, b) => {
            if (key === 'dueAt' && !!a.dueAt !== !!b.dueAt) return a.dueAt ? -1 : 1;
            return dir * this.compare(a, b, key) || a.id.localeCompare(b.id);
        });
    });

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });
        // A task id in the URL opens its card, including on a cold load or a shared link.
        this.route.queryParamMap.subscribe(q => this.openTaskId.set(q.get('task')));

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

    clearCollectionFilters(): void {
        this.filter.set('all');
        this.sprintFilter.set(null);
        this.search.set('');
    }

    /**
     * T-354 — a card dropped into another column becomes that column's status. A drop inside the
     * same column is left alone: the board keeps the server's order (ADR-106), so reordering here
     * would be a lie the next reload corrects. `run` reloads the board, same as every mutation.
     */
    onDrop(event: CdkDragDrop<TaskStatus>) {
        const task = event.item.data as GameTask;
        const target = event.container.data;
        if (event.previousContainer === event.container || task.status === target) return;
        void this.setStatus(task, target);
    }

    columnCount(status: TaskStatus) {
        return this.tasks().filter(t => t.status === status).length;
    }

    /** What an empty column says (ADR-239 clause 8): where a task lands here, not "nothing here". */
    columnEmpty(status: TaskStatus): string {
        const t = this.t().projects.tasks;
        switch (status) {
            case 'backlog': return t.emptyBacklog;
            case 'planned': {
                const picked = this.sprintFilter();
                const sprint = picked
                    ? this.sprints().find(s => s.id === picked) ?? null
                    : this.currentSprint();
                return sprint ? t.emptyPlanned(`S${sprint.number}`) : t.emptyPlannedNoSprint;
            }
            case 'in_progress': return t.emptyInProgress;
            default: return t.emptyDone;
        }
    }

    /** Where a card and a row point: this screen, with the task in the query (ADR-163). */
    boardLink = computed(() => ['/projects', this.projectId(), 'tasks']);

    /** The date a row prints; the word is what says late without colour. */
    dueLabel(task: GameTask): string {
        if (!task.dueAt) return '';
        const date = formatInZone(task.dueAt, 'd MMM');
        return this.overdue(task) ? `${date} · ${this.t().projects.tasks.overdue}` : date;
    }

    /** A deadline is a civil date in the account zone, never a browser-local instant. */
    overdue(task: Pick<GameTask, 'dueAt' | 'status'>, now = new Date()): boolean {
        if (!task.dueAt || task.status === 'done') return false;
        const dueDay = dayInZone(task.dueAt);
        return dueDay.length > 0 && dueDay < dayInZone(now);
    }

    statusTone(status: TaskStatus): Tone {
        return status === 'done' ? 'ok' : status === 'in_progress' ? 'warn' : 'muted';
    }

    prioTone(priority: TaskPriority): Tone {
        return priority === 1 ? 'danger' : priority === 2 ? 'warn' : 'muted';
    }

    docLinkCount(task: GameTask): number {
        return task.links.filter(l => l.type === 'document').length;
    }

    setView(view: 'board' | 'list') {
        this.view.set(view);
        try { localStorage.setItem(VIEW_KEY, view); } catch { /* private mode */ }
    }

    setSort(key: SortKey) {
        if (this.sort() === key) this.sortAsc.update(v => !v);
        else { this.sort.set(key); this.sortAsc.set(true); }
    }

    columnSort(key: SortKey): 'ascending' | 'descending' | null {
        return ariaSort(this.sort() === key, this.sortAsc() ? 'asc' : 'desc');
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
        this.draftDue.set(task.dueAt ? dayInZone(task.dueAt) : '');
        this.draftStatus.set(task.status);
        this.draftPriority.set(task.priority);
        this.draftSprintId.set(task.sprintId ?? '');
        this.draftBuildId.set(task.buildId ?? '');
        this.draftPublicRoadmap.set(task.isPublicRoadmap);
    }

    async saveEdits(task: GameTask, statusOverride?: TaskStatus): Promise<boolean> {
        const title = this.draftTitle().trim();
        if (!title) { this.actionError.set(this.t().projects.tasks.titleRequired); return false; }

        const due = this.draftDue().trim();
        const dueInstant = due ? wallClockToInstant(due, '00:00') : null;
        if (due && !dueInstant) {
            this.actionError.set(this.t().projects.tasks.dueInvalid);
            return false;
        }
        const sprintId = this.draftSprintId();
        const buildId = this.draftBuildId();
        const status = statusOverride ?? this.draftStatus();
        const saved = await this.run(() => this.api.update(task.id, {
            title,
            description: this.draftDescription(),
            assignee: this.draftAssignee(),
            status,
            priority: this.draftPriority(),
            isPublicRoadmap: this.draftPublicRoadmap(),
            // An emptied date field is a request to clear it — which is a different request from
            // not touching it, and the API needs to be told which one this is.
            ...(dueInstant ? { dueAt: dueInstant.toISOString() } : { clearDueAt: true }),
            ...(sprintId ? { sprintId } : { clearSprint: true }),
            ...(buildId ? { buildId } : { clearBuild: true }),
        }));
        if (saved && statusOverride) this.draftStatus.set(statusOverride);
        return saved !== null;
    }

    setStatus(task: GameTask, status: TaskStatus) {
        return this.run(() => this.api.update(task.id, { status }));
    }

    toggleDone(task: GameTask) {
        // Reopening puts the task back where work happens, not back at the start of the queue.
        return this.saveEdits(task, this.draftStatus() === 'done' ? 'in_progress' : 'done');
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
        this.newDescription.set('');
        this.newFiles.set([]);
        this.newStatus.set(status);
        this.newPriority.set(2);
        this.actionError.set(null);
        this.creating.set(true);
    }

    async create() {
        const title = this.newTitle().trim();
        if (!title) { this.actionError.set(this.t().projects.tasks.titleRequired); return; }

        this.busy.set(true);
        this.actionError.set(null);
        let created: GameTask | null = null;
        try {
            created = await this.api.create(this.projectId(), {
                title,
                description: this.newDescription(),
                status: this.newStatus(),
                priority: this.newPriority(),
            });
            for (const file of this.newFiles()) {
                const asset = await this.assets.upload(file);
                await this.api.link(created.id, 'attachment', asset.id);
            }
            this.creating.set(false);
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.tasks.actionFailed));
            // A successfully created task must not be submitted twice if a later upload failed.
            if (created) this.creating.set(false);
        } finally {
            try { this.tasks.set(await this.api.list(this.projectId())); } catch { /* keep the mutation error */ }
            this.busy.set(false);
        }
    }

    chooseFiles(event: Event) {
        const input = event.target as HTMLInputElement;
        this.newFiles.set(Array.from(input.files ?? []));
        input.value = '';
    }

    removeNewFile(index: number) {
        this.newFiles.update(files => files.filter((_, i) => i !== index));
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
            case 'links': return a.links.length - b.links.length;
            case 'dueAt':
                // Undated tasks sort last in both directions: "no deadline" is not a late deadline,
                // and flipping the column should not fill the top of the table with blanks.
                if (!a.dueAt || !b.dueAt) return 0;
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

    linkCounts(task: GameTask) {
        return {
            document: task.links.filter(l => l.type === 'document').length,
            asset: task.links.filter(l => l.type === 'asset' || l.type === 'attachment').length,
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
