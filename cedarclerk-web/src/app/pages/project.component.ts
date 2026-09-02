import { Component, computed, inject, signal } from '@angular/core';
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
    ProjectDetail,
    ProjectDocument,
    ProjectSummary,
    ProjectsService,
    ShowcaseStats,
    isPublishableType,
    projectInitials,
} from '../core/projects.service';
import { Build, BuildsService } from '../core/builds.service';
import { Preset, PresetsService, parseDocumentConfig } from '../core/presets.service';
import { TaskPriority, isOverdue } from '../core/tasks.service';
import { sprintProgress } from '../core/sprints.service';
import { AuthService } from '../core/auth.service';
import { Channel, ChannelsService } from '../core/channels.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { AssetsService, LibraryAsset } from '../core/assets.service';
import { Team, TeamsService } from '../core/teams.service';
import { MediaPickerComponent } from '../shared/media-picker.component';

type DocFilter = 'all' | 'live' | 'drafts' | 'archived';

const MS_PER_DAY = 86_400_000;

// T-223 (ADR-160) — the hub: one project's dashboard. Main.png (ADR-239): a header with the
// state, kind, count and last edit; the most recent document as a "continue writing" card over a
// scrolling document list; a right column with today's sprint, the next task and where the
// project's work goes. Everything drawn is on ProjectDetail, the summary row, the build list and
// the account's channels — no new endpoint (ADR-168 rule 5).
@Component({
    selector: 'app-project',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, RouterLink, ModalComponent,
        PageHeaderComponent, EmptyStateComponent, ButtonComponent, InputComponent, MediaPickerComponent,
    ],
    templateUrl: 'project.component.html',
    styleUrls: ['project.component.css'],
})
export class ProjectComponent {
    private api = inject(ProjectsService);
    private presetsApi = inject(PresetsService);
    private teamsApi = inject(TeamsService);
    private assets = inject(AssetsService);
    private buildsApi = inject(BuildsService);
    private channelsApi = inject(ChannelsService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private auth = inject(AuthService);
    t = inject(LocaleService).t;

    readonly docTypes = DOCUMENT_TYPES;
    readonly docIcons = DOCUMENT_TYPE_ICONS;
    readonly isPublishableType = isPublishableType;
    readonly initials = projectInitials;
    readonly overdue = isOverdue;
    readonly sprintPercent = sprintProgress;

    project = signal<ProjectDetail | null>(null);
    /** The project list, and the only place an asset count and a last-edit date can be read from. */
    projects = signal<readonly ProjectSummary[]>([]);
    /** null until the build list answers — and if it never does (ADR-160 rule 4). */
    builds = signal<readonly Build[] | null>(null);
    /** The account's Telegram channels: a project has no channel table of its own (ADR-239 cl. 12). */
    channels = signal<readonly Channel[]>([]);
    loading = signal(true);
    loadError = signal<string | null>(null);

    docFilter = signal<DocFilter>('all');
    docSearch = signal('');

    addingDocument = signal(false);
    // T-331 — the user's document presets, offered alongside the built-in types in the New-document
    // dialog; loaded once the dialog first opens.
    documentPresets = signal<Preset[]>([]);
    editing = signal(false);
    editName = signal('');
    editDescription = signal('');
    editCoverUrl = signal<string | null>(null);
    coverPickerOpen = signal(false);
    // T-358 — which team reaches this project. '' is "nobody but me", which is a real answer and
    // therefore an option in the list rather than an empty select.
    teams = signal<Team[]>([]);
    editTeamId = signal<string>('');
    /** T-296/T-297 — the public page's counters; null until they arrive, and on a page with none. */
    showcaseStats = signal<ShowcaseStats | null>(null);
    actionError = signal<string | null>(null);
    busy = signal(false);
    // Deleting a project is two clicks on the same button rather than a second modal on top of the
    // first: the explanation of what survives is what matters here, and it fits under the button.
    confirmDelete = false;

    /** This project's row in the list — `assetCount` and `lastActivityAt` live on the summary. */
    summary = computed(() => {
        const id = this.project()?.id;
        return (id && this.projects().find(p => p.id === id)) || null;
    });

    documents = computed(() =>
        [...(this.project()?.documents ?? [])].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)));

    filteredDocuments = computed(() => {
        const needle = this.docSearch().trim().toLowerCase();
        const filter = this.docFilter();
        return this.documents().filter(d => {
            if (filter === 'live' && !(d.isBlogPublished && !d.isArchived)) return false;
            if (filter === 'drafts' && (d.isBlogPublished || d.isArchived)) return false;
            if (filter === 'archived' && !d.isArchived) return false;
            return !needle || d.title.toLowerCase().includes(needle);
        });
    });

    /** What "Continue writing" opens: the document touched last. */
    resumeDoc = computed<ProjectDocument | null>(() => this.documents()[0] ?? null);

    /** The one task the side column shows — the server already sorted them by urgency. */
    upNext = computed(() => this.project()?.upNext[0] ?? null);

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

    headerMeta = computed<HeaderMeta[]>(() => {
        const p = this.project();
        if (!p) return [];
        const t = this.t().projects;
        const meta: HeaderMeta[] = [
            { text: p.archivedAt ? t.stateArchived : t.stateActive, tag: true, tone: p.archivedAt ? 'muted' : 'ok' },
            { text: t.projectTypes[p.projectType].name },
            { text: t.documentCount(p.documents.length) },
        ];
        const at = this.summary()?.lastActivityAt;
        if (at) meta.push({ text: `${t.hub.lastEdit} ${formatInZone(at, 'd MMM')}` });
        return meta;
    });

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (id) void this.load(id);
        });

        void this.loadProjects();
        void this.loadChannels();
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
        // Beside the detail rather than before it: the build count is one row's number, and a
        // slow or failing list must not hold the whole page (ADR-160 rule 4).
        try {
            this.builds.set(await this.buildsApi.list(id));
        } catch {
            this.builds.set(null);
        }

        this.showcaseStats.set(null);
        if (this.project()?.showcaseSlug) await this.loadShowcaseStats(id);
    }

    /** Same rule as the builds above: counters are one group of rows, not the page. */
    private async loadShowcaseStats(id: string) {
        try {
            this.showcaseStats.set(await this.api.showcaseStats(id));
        } catch {
            this.showcaseStats.set(null);
        }
    }

    /** The list survives a failure in silence: it feeds two readouts, and the page it stands on loaded. */
    private async loadProjects() {
        try {
            this.projects.set(await this.api.list(true));
        } catch {
            this.projects.set([]);
        }
    }

    /** No channel is a real answer the column prints; a failed call reads the same way. */
    private async loadChannels() {
        try {
            this.channels.set(await this.channelsApi.list());
        } catch {
            this.channels.set([]);
        }
    }

    channelLabel(channel: Channel): string {
        return channel.username ? `@${channel.username}` : channel.title;
    }

    channelUrl(channel: Channel): string | null {
        return channel.username ? `https://t.me/${channel.username}` : null;
    }

    blogUrl(): string | null {
        return this.auth.blogUrl();
    }

    docTone(doc: ProjectDocument): 'ok' | 'muted' {
        return doc.isBlogPublished && !doc.isArchived ? 'ok' : 'muted';
    }

    documentState(doc: ProjectDocument): string {
        const t = this.t().drafts.status;
        if (doc.isArchived) return t.archived;
        return doc.isBlogPublished ? t.published : t.draft;
    }

    prioTone(priority: TaskPriority): 'danger' | 'warn' | 'muted' {
        return priority === 1 ? 'danger' : priority === 2 ? 'warn' : 'muted';
    }

    openAddDocument() {
        this.addingDocument.set(true);
        // Best-effort: the built-in types are always there, presets are a bonus row.
        this.presetsApi.list('document').then(p => this.documentPresets.set(p)).catch(() => { /* ignore */ });
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

    /** T-331 — a preset applies its base type and skeleton server-side; the title is the preset's name. */
    async createFromPreset(preset: Preset) {
        const project = this.project();
        if (!project || this.busy()) return;
        this.busy.set(true);
        this.actionError.set(null);
        try {
            const created = await this.api.createDocument(project.id, 'post', preset.name, preset.id);
            this.addingDocument.set(false);
            void this.router.navigate(['/editor'], { queryParams: { draft: created.id } });
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.newDoc.failed));
        } finally {
            this.busy.set(false);
        }
    }

    presetConfig = (p: Preset) => parseDocumentConfig(p.configJson);

    startEdit() {
        const project = this.project();
        if (!project) return;
        this.editName.set(project.name);
        this.editDescription.set(project.description);
        this.editCoverUrl.set(project.coverUrl);
        this.editTeamId.set(project.teamId ?? '');
        // Loaded when the dialog opens rather than with the screen: most visits never edit.
        void this.loadTeams();
        this.actionError.set(null);
        this.confirmDelete = false;
        this.editing.set(true);
    }

    /** The live public URL; the page itself is edited on /projects/:id/showcase. */
    showcaseUrl(): string | null {
        const slug = this.project()?.showcaseSlug;
        const base = this.auth.blogUrl();
        return slug && base ? `${base}/games/${slug}` : null;
    }

    async saveEdit() {
        const project = this.project();
        const name = this.editName().trim();
        if (!project || name.length === 0 || this.busy()) return;

        this.busy.set(true);
        this.actionError.set(null);
        try {
            const coverUrl = this.editCoverUrl();
            // The team is its own endpoint, and is only written when it actually moved: it is a
            // different permission from renaming a project and must not ride along with one.
            const teamId = this.editTeamId() || null;
            if (teamId !== (project.teamId ?? null)) {
                await this.teamsApi.setProjectTeam(project.id, teamId);
                this.project.set({ ...project, teamId });
            }
            await this.api.update(project.id, name, this.editDescription().trim(), coverUrl);
            this.project.set({
                ...project,
                name,
                description: this.editDescription().trim(),
                coverUrl,
                teamId,
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

    // T-353 — the cover opens the one asset window instead of a bare file input. The picker
    // uploads as well as picks, so nothing is lost by dropping the private flow: a file chosen
    // there lands in the library, which is where a project's logo belongs anyway.
    // Best-effort: with no teams the row simply offers "nobody but me", which is the truth.
    private async loadTeams() {
        try { this.teams.set(await this.teamsApi.list()); }
        catch { this.teams.set([]); }
    }

    pickedCover(asset: LibraryAsset) {
        this.editCoverUrl.set(`/media/${asset.localPath}`);
        this.coverPickerOpen.set(false);
    }

    removeCover() {
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
