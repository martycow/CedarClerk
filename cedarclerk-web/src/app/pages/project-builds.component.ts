import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { Build, BuildsService } from '../core/builds.service';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { GameTask, TasksService } from '../core/tasks.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { PageHeaderComponent } from '../shared/page-header.component';

// T-126 (ADR-112) — build and version records.
//
// No design exists for this screen: the handoff covers the planner, the board and the assets, and
// stops there. It deliberately borrows the planner's language — stacked cards, a head strip, task
// rows — rather than inventing a third way of drawing a list of things with things inside them.
//
// What it must never imply: that this is connected to git. It records versions the author names,
// and the one thing it does beyond recording is turn a version into a changelog document.
@Component({
    selector: 'app-project-builds',
    imports: [IconComponent, DatePipe, FormsModule, RouterLink, PageHeaderComponent, ModalComponent],
    templateUrl: 'project-builds.component.html',
    styleUrls: ['project-builds.component.css'],
})
export class ProjectBuildsComponent {
    private api = inject(BuildsService);
    private tasksApi = inject(TasksService);
    private projects = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    t = inject(LocaleService).t;

    projectId = signal('');
    project = signal<ProjectDetail | null>(null);
    builds = signal<Build[]>([]);
    tasks = signal<GameTask[]>([]);
    loading = signal(true);
    loadError = signal<string | null>(null);
    busy = signal(false);
    actionError = signal<string | null>(null);

    editing = signal<Build | null>(null);
    creating = signal(false);
    draftVersion = signal('');
    draftNotes = signal('');
    draftReleased = signal('');

    unreleasedCount = computed(() => this.builds().filter(b => !b.released).length);

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
            const [project, builds, tasks] = await Promise.all([
                this.projects.get(id),
                this.api.list(id),
                this.tasksApi.list(id),
            ]);
            this.project.set(project);
            this.builds.set(builds);
            this.tasks.set(tasks);
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.builds.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    tasksOf(build: Build): GameTask[] {
        return this.tasks().filter(t => t.buildId === build.id);
    }

    openTask(task: GameTask) {
        void this.router.navigate(['/projects', this.projectId(), 'tasks'], { queryParams: { task: task.id } });
    }

    openDocument(documentId: string) {
        void this.router.navigate(['/editor'], { queryParams: { id: documentId } });
    }

    // ---- creating and editing ---------------------------------------------

    startCreating() {
        this.draftVersion.set('');
        this.draftNotes.set('');
        this.draftReleased.set('');
        this.actionError.set(null);
        this.creating.set(true);
    }

    startEditing(build: Build) {
        this.draftVersion.set(build.version);
        this.draftNotes.set(build.notes);
        this.draftReleased.set(build.releasedAt ? build.releasedAt.slice(0, 10) : '');
        this.actionError.set(null);
        this.editing.set(build);
    }

    async save() {
        const version = this.draftVersion().trim();
        if (!version) { this.actionError.set(this.t().projects.builds.versionRequired); return; }

        const input = {
            version,
            notes: this.draftNotes(),
            // An empty date field means "not released yet", which is a real state, not a missing
            // value — the list puts those first.
            releasedAt: this.draftReleased() ? new Date(this.draftReleased()).toISOString() : null,
        };
        const existing = this.editing();
        const saved = await this.run(() => existing
            ? this.api.update(existing.id, input)
            : this.api.create(this.projectId(), input));

        if (saved) { this.creating.set(false); this.editing.set(null); }
    }

    async remove(build: Build) {
        if (!confirm(this.t().projects.builds.deleteConfirm)) return;
        this.editing.set(null);
        await this.run(() => this.api.remove(build.id));
    }

    /** Creates the changelog document and opens it — there is nothing to read on this screen. */
    async createChangelog(build: Build) {
        const created = await this.run(() => this.api.createChangelog(build.id));
        if (created) this.openDocument(created.documentId);
    }

    private async run<T>(action: () => Promise<T>): Promise<T | null> {
        this.busy.set(true);
        this.actionError.set(null);
        try {
            const result = await action();
            const id = this.projectId();
            const [builds, tasks] = await Promise.all([this.api.list(id), this.tasksApi.list(id)]);
            this.builds.set(builds);
            this.tasks.set(tasks);
            return result;
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.builds.actionFailed));
            return null;
        } finally {
            this.busy.set(false);
        }
    }
}
