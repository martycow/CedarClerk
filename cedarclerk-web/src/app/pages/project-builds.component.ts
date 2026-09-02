import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { Build, BuildsService } from '../core/builds.service';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { GameTask, TasksService, isOverdue } from '../core/tasks.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';
import { StampBadgeComponent, StampTone } from '../bench/display/stamp-badge.component';
import { TaskTagComponent } from '../bench/display/task-tag.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';

const STATUS_TONES: Record<GameTask['status'], StampTone> = {
    done: 'pine',
    in_progress: 'brass',
    planned: 'ink',
    backlog: 'ink',
};

// T-126 (ADR-112) — build and version records, ported onto the bench by T-226 (ADR-168) and onto
// the paper-first shell by ADR-239: a page header carrying what the chalked edge used to say, the
// versions in a scrolling column, the summary as a plain card beside them.
//
// No design exists for this screen: the handoff covers the planner, the board and the assets, and
// stops there. It deliberately borrows the planner's language — a paper card per version, a head
// strip, the same task tags — rather than inventing a third way of drawing a list of things with
// things inside them.
//
// What it must never imply: that this is connected to git. It records versions the author names,
// and the one thing it does beyond recording is turn a version into a changelog document.
@Component({
    selector: 'app-project-builds',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, RouterLink, ModalComponent,
        PageHeaderComponent, EmptyStateComponent, SpecRowComponent, PaperCardComponent,
        StampBadgeComponent, TaskTagComponent, ButtonComponent,
    ],
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

    readonly overdue = isOverdue;
    readonly statusTones = STATUS_TONES;

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
    draftPublic = signal(false);
    draftDownloadUrl = signal('');

    unreleasedCount = computed(() => this.builds().filter(b => !b.released).length);

    /** The state the empty sheet is drawn for; the header drops its own copy of the action there. */
    isEmpty = computed(() => !this.loading() && !this.loadError() && !this.builds().length);
    releasedCount = computed(() => this.builds().filter(b => b.released).length);

    /** The newest version that is actually out — an unreleased record is a plan, not a version. */
    latestReleased = computed<Build | null>(() => {
        const out = this.builds().filter(b => b.released && b.releasedAt);
        if (out.length === 0) return null;
        return out.reduce((a, b) => (a.releasedAt! >= b.releasedAt! ? a : b));
    });

    /** Sums over what is already loaded (ADR-168 rule 5) — nothing is fetched for the shelf. */
    assignedCount = computed(() => this.tasks().filter(t => t.buildId).length);
    documentCount = computed(() => this.builds().reduce((n, b) => n + b.documents.length, 0));

    /** The former rule readout: how many versions, how many not out yet. */
    headerMeta = computed<HeaderMeta[]>(() => {
        if (!this.project()) return [];
        return [{ text: this.t().projects.builds.sub(this.builds().length, this.unreleasedCount()) }];
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

    /** The date a tag prints. The overdue word stays beside it: rust ink alone carries no meaning
        to a reader who cannot see it. */
    dueLabel(task: GameTask, formatted: string): string {
        const t = this.t().projects.tasks;
        if (!task.dueAt) return t.noDueDate;
        return isOverdue(task) ? `${formatted} · ${t.overdue}` : formatted;
    }

    // ---- creating and editing ---------------------------------------------

    startCreating() {
        this.draftVersion.set('');
        this.draftNotes.set('');
        this.draftReleased.set('');
        this.draftPublic.set(false);
        this.draftDownloadUrl.set('');
        this.actionError.set(null);
        this.creating.set(true);
    }

    startEditing(build: Build) {
        this.draftVersion.set(build.version);
        this.draftNotes.set(build.notes);
        this.draftReleased.set(build.releasedAt ? build.releasedAt.slice(0, 10) : '');
        this.draftPublic.set(build.isPublic);
        this.draftDownloadUrl.set(build.downloadUrl ?? '');
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
            // T-299 — offering a build needs somewhere to get it; the server refuses the pair the
            // other way round, and the field sits under the toggle so the reason is visible.
            isPublic: this.draftPublic(),
            downloadUrl: this.draftDownloadUrl().trim() || null,
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
        // `draft` and not `id`: it is the only query parameter the editor reads, and the other
        // spelling silently opened whichever draft happened to be newest (ADR-168 rule 3).
        if (created) void this.router.navigate(['/editor'], { queryParams: { draft: created.documentId } });
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
