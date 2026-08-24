import { Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { formatInZone } from '../core/display-time';
import {
    DOCUMENT_TYPES,
    DOCUMENT_TYPE_ICONS,
    DocumentType,
    PROJECT_TYPE_ICONS,
    ProjectDetail,
    ProjectDocument,
    ProjectSummary,
    ProjectsService,
    projectInitials,
} from '../core/projects.service';
import { Build, BuildsService } from '../core/builds.service';
import { isOverdue } from '../core/tasks.service';
import { sprintProgress } from '../core/sprints.service';
import { RulerReadout } from '../bench/chrome/ruler-bar.component';
import { RulerService } from '../core/ruler.service';
import { IconName } from '../shared/icon-data.generated';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { StampBadgeComponent, StampTone } from '../bench/display/stamp-badge.component';
import { TaskTagComponent } from '../bench/display/task-tag.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { ModuleTileComponent } from '../bench/worktop/module-tile.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';
import { AssetsService } from '../core/assets.service';

/** One plate on the wall. `link` is the screen it opens — ADR-160 rule 1: no door, no plate. */
interface ModulePlate {
    id: string;
    icon: IconName;
    name: string;
    count: string | number;
    sub: string;
    link: readonly unknown[];
    rotate: number;
}

// Held to ±0.6° by ModuleTile itself; fixed per position so the wall does not reshuffle on a
// re-render.
const TILT = [-0.35, 0.25, -0.2, 0.3];

const MS_PER_DAY = 86_400_000;

// T-223 (ADR-160) — the hub: the workshop bench for one project. Three columns — the projects
// shelf on the left, the lit top and the wall of module plates in the middle, today's sprint and
// tasks on the right — inside the bench shell, which owns the rail, the drawer and the rule.
//
// Comfortable density: this is a screen you read, not a table you scan. It carries no
// data-density attribute; the material split (ADR-138) is what governs the chrome inside it.
@Component({
    selector: 'app-project',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, RouterLink, ModalComponent,
        WorktopComponent, ShelfPanelComponent, ModuleTileComponent, SpecRowComponent,
        StampBadgeComponent, TaskTagComponent, ButtonComponent,
    ],
    templateUrl: 'project.component.html',
    styleUrls: ['project.component.css'],
})
export class ProjectComponent implements OnDestroy {
    private api = inject(ProjectsService);
    private assets = inject(AssetsService);
    private buildsApi = inject(BuildsService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private ruler = inject(RulerService);
    t = inject(LocaleService).t;

    readonly docTypes = DOCUMENT_TYPES;
    readonly docIcons = DOCUMENT_TYPE_ICONS;
    readonly projectIcons = PROJECT_TYPE_ICONS;
    readonly initials = projectInitials;
    readonly overdue = isOverdue;
    readonly today = new Date();
    readonly sprintPercent = sprintProgress;

    project = signal<ProjectDetail | null>(null);
    /** The left shelf, and the only place an asset count for this project can be read from. */
    projects = signal<readonly ProjectSummary[]>([]);
    /** null until the build list answers — and if it never does (ADR-160 rule 4). */
    builds = signal<readonly Build[] | null>(null);
    loading = signal(true);
    loadError = signal<string | null>(null);

    addingDocument = signal(false);
    editing = signal(false);
    editName = signal('');
    editDescription = signal('');
    editCoverUrl = signal<string | null>(null);
    editCoverFile = signal<File | null>(null);
    editShowcase = signal(false);
    editShowcaseSlug = signal('');
    editShowcaseLinks = signal('');
    actionError = signal<string | null>(null);
    busy = signal(false);
    // Deleting a project is two clicks on the same button rather than a second modal on top of the
    // first: the explanation of what survives is what matters here, and it fits under the button.
    confirmDelete = false;

    /** This project's row in the list — `documentCount`, `assetCount` and `lastActivityAt` live
        on the summary and nowhere else. */
    summary = computed(() => {
        const id = this.project()?.id;
        return (id && this.projects().find(p => p.id === id)) || null;
    });

    documents = computed(() =>
        [...(this.project()?.documents ?? [])].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)));

    /** What "Continue" opens: the document touched last. */
    resumeDoc = computed<ProjectDocument | null>(() => this.documents()[0] ?? null);

    published = computed(() => this.documents().filter(d => d.isBlogPublished).length);

    /** The newest version that is actually out — an unreleased record is a plan, not a version. */
    releasedBuild = computed<Build | null>(() => {
        const out = (this.builds() ?? []).filter(b => b.released && b.releasedAt);
        if (out.length === 0) return null;
        return out.reduce((a, b) => (a.releasedAt! >= b.releasedAt! ? a : b));
    });

    /** Days from today to the end of the sprint covering it, floored at zero. */
    sprintDaysLeft = computed(() => {
        const sprint = this.project()?.currentSprint;
        if (!sprint) return 0;
        const end = new Date(sprint.endsAt);
        const endDay = Date.UTC(end.getFullYear(), end.getMonth(), end.getDate());
        const now = new Date();
        const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
        return Math.max(0, Math.round((endDay - today) / MS_PER_DAY));
    });

    tiles = computed<ModulePlate[]>(() => {
        const p = this.project();
        if (!p) return [];
        const t = this.t().projects;
        const sprint = p.currentSprint;
        const build = this.releasedBuild();
        const builds = this.builds();
        const assets = this.summary();

        const plates: ModulePlate[] = [
            {
                id: 'tasks', icon: 'check-square', name: t.tasks.title,
                count: p.openTaskCount,
                sub: t.hub.tasksSub(p.taskCounts.in_progress ?? 0),
                link: ['/projects', p.id, 'tasks'], rotate: 0,
            },
            {
                id: 'sprint', icon: 'flag', name: t.railPendingTitle,
                count: sprint ? `S${sprint.number}` : '—',
                sub: sprint ? t.planner.progress(sprint.doneCount, sprint.taskCount) : t.planner.noCurrentSprint,
                link: ['/projects', p.id, 'planner'], rotate: 0,
            },
            {
                id: 'assets', icon: 'images', name: t.assets.title,
                count: assets ? assets.assetCount : '—',
                sub: assets && assets.assetCount === 0 ? t.hub.assetsNone : '',
                link: ['/projects', p.id, 'assets'], rotate: 0,
            },
            {
                id: 'builds', icon: 'cube', name: t.builds.title,
                count: builds ? builds.length : '—',
                sub: build ? t.hub.buildsSub(build.version) : builds ? t.hub.buildsNone : '',
                link: ['/projects', p.id, 'builds'], rotate: 0,
            },
        ];
        return plates.map((plate, i) => ({ ...plate, rotate: TILT[i % TILT.length] }));
    });

    /** The right-hand chalk chip on the bench top: when this project was last written to. */
    heroMeta = computed(() => {
        const at = this.summary()?.lastActivityAt;
        return at ? `${this.t().projects.hub.lastEdit} ${formatInZone(at, 'MM/dd')}` : '';
    });

    sprintLeft = computed(() => {
        const sprint = this.project()?.currentSprint;
        return sprint ? this.t().projects.hub.sprintLeftValue(this.sprintDaysLeft(), formatInZone(sprint.endsAt, 'MM/dd')) : '';
    });

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (id) void this.load(id);
        });

        void this.loadProjects();

        effect(() => {
            const p = this.project();
            if (!p) return;
            const t = this.t().projects.hub;
            const sprint = p.currentSprint;
            const assets = this.summary();
            const left: RulerReadout[] = [];
            if (sprint) left.push({ text: t.rulerSprint(sprint.number, sprint.doneCount, sprint.taskCount), title: sprint.name });
            if (assets) left.push({ text: t.rulerAssets(assets.assetCount) });
            this.ruler.publish({
                label: p.name,
                left,
                right: [
                    { text: t.rulerDocs(p.documents.length) },
                    { text: t.rulerTasks(p.openTaskCount) },
                ],
            });
        });
    }

    ngOnDestroy(): void {
        this.ruler.clear();
    }

    async load(id: string) {
        this.loading.set(true);
        this.loadError.set(null);
        this.builds.set(null);
        try {
            this.project.set(await this.api.get(id));
        } catch (e) {
            this.project.set(null);
            this.loadError.set(httpErrorMessage(e, this.t().projects.notFound));
        } finally {
            this.loading.set(false);
        }
        // Beside the detail rather than before it: the build count is one plate's number, and a
        // slow or failing list must not hold the whole bench (ADR-160 rule 4).
        try {
            this.builds.set(await this.buildsApi.list(id));
        } catch {
            this.builds.set(null);
        }
    }

    /** The shelf survives a failure in silence: it is a switcher, and the page it stands on loaded. */
    private async loadProjects() {
        try {
            this.projects.set(await this.api.list(true));
        } catch {
            this.projects.set([]);
        }
    }

    /** Brass is the build number's; a draft is pressed as a faint outline (StampBadge.prompt.md). */
    documentTone(doc: ProjectDocument): StampTone {
        return doc.isBlogPublished && !doc.isArchived ? 'pine' : 'ink';
    }

    documentState(doc: ProjectDocument): string {
        const t = this.t().drafts.status;
        if (doc.isArchived) return t.archived;
        return doc.isBlogPublished ? t.published : t.draft;
    }

    async createDocument(type: DocumentType) {
        const project = this.project();
        if (!project || this.busy()) return;

        this.busy.set(true);
        this.actionError.set(null);
        try {
            const created = await this.api.createDocument(project.id, type, this.t().projects.docTypes[type].name);
            this.addingDocument.set(false);
            void this.router.navigate(['/editor'], { queryParams: { draft: created.id } });
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.newDoc.failed));
        } finally {
            this.busy.set(false);
        }
    }

    startEdit() {
        const project = this.project();
        if (!project) return;
        this.editName.set(project.name);
        this.editDescription.set(project.description);
        this.editCoverUrl.set(project.coverUrl);
        this.editCoverFile.set(null);
        this.editShowcase.set(!!project.showcaseSlug);
        this.editShowcaseSlug.set(project.showcaseSlug ?? '');
        this.editShowcaseLinks.set(project.showcaseLinks ?? '');
        this.actionError.set(null);
        this.confirmDelete = false;
        this.editing.set(true);
    }

    /** The live public URL, shown under the toggle so the page is one click away once it exists. */
    showcaseUrl(): string | null {
        const slug = this.project()?.showcaseSlug;
        return slug ? `https://blog.mooexe.dev/games/${slug}` : null;
    }

    async saveEdit() {
        const project = this.project();
        const name = this.editName().trim();
        if (!project || name.length === 0 || this.busy()) return;

        this.busy.set(true);
        this.actionError.set(null);
        try {
            let coverUrl = this.editCoverUrl();
            const coverFile = this.editCoverFile();
            if (coverFile) coverUrl = (await this.assets.upload(coverFile)).url;
            await this.api.update(project.id, name, this.editDescription().trim(), coverUrl);
            // T-159 — the showcase saves with the same button; the server slugifies and may rename.
            const showcase = await this.api.setShowcase(project.id,
                this.editShowcase(), this.editShowcaseSlug().trim() || null, this.editShowcaseLinks().trim());
            this.project.set({
                ...project,
                name,
                description: this.editDescription().trim(),
                coverUrl,
                showcaseSlug: showcase.showcaseSlug,
                showcaseLinks: this.editShowcaseLinks().trim(),
            });
            this.projects.update(rows => rows.map(row => row.id === project.id
                ? { ...row, name, description: this.editDescription().trim(), coverUrl }
                : row));
            this.editing.set(false);
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    chooseCover(event: Event) {
        const input = event.target as HTMLInputElement;
        this.editCoverFile.set(input.files?.[0] ?? null);
        input.value = '';
    }

    removeCover() {
        this.editCoverFile.set(null);
        this.editCoverUrl.set(null);
    }

    async toggleArchived() {
        const project = this.project();
        if (!project || this.busy()) return;

        this.busy.set(true);
        this.actionError.set(null);
        try {
            const result = await this.api.setArchived(project.id, !project.archivedAt);
            this.project.set({ ...project, archivedAt: result.archivedAt });
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    /** Documents survive: the server detaches them rather than deleting (ProjectEndpoints). */
    async deleteProject() {
        const project = this.project();
        if (!project || this.busy()) return;

        this.busy.set(true);
        this.actionError.set(null);
        try {
            await this.api.remove(project.id);
            void this.router.navigate(['/projects']);
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
            this.busy.set(false);
        }
    }
}
