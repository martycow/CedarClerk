import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { Sprint, SprintsService, sprintProgress } from '../core/sprints.service';
import { GameTask, TasksService, isOverdue } from '../core/tasks.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { PageHeaderComponent } from '../shared/page-header.component';

/** A sprint plus the tasks planned into it — what one card on this screen draws. */
interface SprintGroup {
    sprint: Sprint | null;
    tasks: GameTask[];
}

// T-124 (ADR-106/111) — the development planner, from docs/design_handoff_indiedev_core_loop §8.
//
// Stacked cards rather than a timeline: the question this screen answers is "what is in this
// stretch and what is left of it", which is a list per sprint, not a position on an axis.
//
// Order is current → planned → No sprint → finished, and finished sprints collapse. Nothing is
// hidden by collapsing: an unfinished task in a sprint whose days ran out still appears, because
// pretending work vanished with the date is the one thing this screen must not do.
@Component({
    selector: 'app-project-planner',
    imports: [IconComponent, ZonedDatePipe, FormsModule, RouterLink, PageHeaderComponent, ModalComponent],
    templateUrl: 'project-planner.component.html',
    styleUrls: ['project-planner.component.css'],
})
export class ProjectPlannerComponent {
    private api = inject(SprintsService);
    private tasksApi = inject(TasksService);
    private projects = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    t = inject(LocaleService).t;

    readonly overdue = isOverdue;
    readonly progress = sprintProgress;

    projectId = signal('');
    project = signal<ProjectDetail | null>(null);
    sprints = signal<Sprint[]>([]);
    tasks = signal<GameTask[]>([]);
    loading = signal(true);
    loadError = signal<string | null>(null);
    busy = signal(false);
    actionError = signal<string | null>(null);

    /** Finished sprints start collapsed; expanding one is remembered only while the page is open. */
    expanded = signal<ReadonlySet<string>>(new Set());

    editing = signal<Sprint | null>(null);
    creating = signal(false);
    draftName = signal('');
    draftStarts = signal('');
    draftEnds = signal('');

    openCount = computed(() => this.tasks().filter(t => t.status !== 'done').length);

    /**
     * The screen, in order. "No sprint" sits between what is planned and what has finished: it is
     * the pile work comes out of, so it belongs next to the future, not after the past.
     */
    groups = computed<SprintGroup[]>(() => {
        const tasks = this.tasks();
        const byId = new Map<string, GameTask[]>();
        for (const task of tasks) {
            if (!task.sprintId) continue;
            const planned = byId.get(task.sprintId);
            if (planned) planned.push(task);
            else byId.set(task.sprintId, [task]);
        }

        const sprints = this.sprints();
        const groups: SprintGroup[] = sprints
            .filter(s => s.state !== 'finished')
            .map(s => ({ sprint: s, tasks: byId.get(s.id) ?? [] }));

        groups.push({ sprint: null, tasks: tasks.filter(t => !t.sprintId) });

        groups.push(...sprints
            .filter(s => s.state === 'finished')
            .map(s => ({ sprint: s, tasks: byId.get(s.id) ?? [] })));

        return groups;
    });

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });
    }

    async load() {
        const id = this.projectId();
        if (!id) return;
        this.loading.set(true);
        this.loadError.set(null);
        try {
            const [project, sprints, tasks] = await Promise.all([
                this.projects.get(id),
                this.api.list(id),
                this.tasksApi.list(id),
            ]);
            this.project.set(project);
            this.sprints.set(sprints);
            this.tasks.set(tasks);
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.planner.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    /**
     * A finished sprint is collapsed only when everything in it is done. One left unfinished is
     * shown, because that is the case a collapsed summary would hide — and it is exactly the case
     * worth seeing.
     */
    isCollapsed(group: SprintGroup): boolean {
        if (group.sprint?.state !== 'finished') return false;
        if (this.expanded().has(group.sprint.id)) return false;
        return group.tasks.every(t => t.status === 'done');
    }

    toggle(sprint: Sprint) {
        this.expanded.update(set => {
            const next = new Set(set);
            if (!next.delete(sprint.id)) next.add(sprint.id);
            return next;
        });
    }

    openTask(task: GameTask) {
        void this.router.navigate(['/projects', this.projectId(), 'tasks'], { queryParams: { task: task.id } });
    }

    // ---- creating and editing ---------------------------------------------

    startCreating() {
        const today = new Date();
        // Two weeks, because a default has to be some length and that is the one most sprints are.
        // Both fields are editable before anything is saved.
        this.draftName.set('');
        this.draftStarts.set(this.asDateInput(today));
        this.draftEnds.set(this.asDateInput(new Date(today.getTime() + 13 * 86_400_000)));
        this.actionError.set(null);
        this.creating.set(true);
    }

    startEditing(sprint: Sprint) {
        this.draftName.set(sprint.name);
        this.draftStarts.set(sprint.startsAt.slice(0, 10));
        this.draftEnds.set(sprint.endsAt.slice(0, 10));
        this.actionError.set(null);
        this.editing.set(sprint);
    }

    async save() {
        const name = this.draftName().trim();
        if (!name) { this.actionError.set(this.t().projects.planner.nameRequired); return; }
        if (this.draftEnds() < this.draftStarts()) {
            this.actionError.set(this.t().projects.planner.endsBeforeStarts);
            return;
        }

        const input = { name, startsAt: this.draftStarts(), endsAt: this.draftEnds() };
        const existing = this.editing();
        const saved = await this.run(() => existing
            ? this.api.update(existing.id, input)
            : this.api.create(this.projectId(), input));

        if (saved) { this.creating.set(false); this.editing.set(null); }
    }

    async remove(sprint: Sprint) {
        if (!confirm(this.t().projects.planner.deleteConfirm)) return;
        this.editing.set(null);
        await this.run(() => this.api.remove(sprint.id));
    }

    /** Reloads both lists: moving a sprint's dates can change every other sprint's state. */
    private async run<T>(action: () => Promise<T>): Promise<T | null> {
        this.busy.set(true);
        this.actionError.set(null);
        try {
            const result = await action();
            const id = this.projectId();
            const [sprints, tasks] = await Promise.all([this.api.list(id), this.tasksApi.list(id)]);
            this.sprints.set(sprints);
            this.tasks.set(tasks);
            return result;
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.planner.actionFailed));
            return null;
        } finally {
            this.busy.set(false);
        }
    }

    private asDateInput(date: Date): string {
        // Local parts, not toISOString: that converts to UTC and hands back yesterday for anyone
        // east of Greenwich in the evening.
        const month = `${date.getMonth() + 1}`.padStart(2, '0');
        const day = `${date.getDate()}`.padStart(2, '0');
        return `${date.getFullYear()}-${month}-${day}`;
    }
}
