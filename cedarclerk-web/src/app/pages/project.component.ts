import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import {
    DOCUMENT_TYPES,
    DOCUMENT_TYPE_ICONS,
    DocumentType,
    PROJECT_TYPE_ICONS,
    ProjectDetail,
    ProjectDocument,
    ProjectsService,
    projectInitials,
} from '../core/projects.service';
import { isOverdue } from '../core/tasks.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { PageHeaderComponent } from '../shared/page-header.component';

/** How many recent documents a type's card shows before "View all" is the only way to more. */
const RECENT_PER_TYPE = 3;

// The handoff's split, and it is by importance rather than by count: the three kinds of document a
// game project is actually built out of get a card each, the rest share a row of tiles.
const FEATURED_TYPES: DocumentType[] = ['post', 'design', 'script'];
const TILE_TYPES: DocumentType[] = DOCUMENT_TYPES.filter(t => !FEATURED_TYPES.includes(t));

// Phase 13 / T-120 — the project dashboard, the module's central screen
// (docs/design_handoff_indiedev_core_loop, chosen variant: Overview-first).
//
// Comfortable density: this is a screen you read, not a table you scan. It carries no
// data-density attribute, which is exactly how /settings stays comfortable while /drafts is
// compact using the same component styles (ADR-071, principle 3).
@Component({
    selector: 'app-project',
    imports: [IconComponent, DatePipe, FormsModule, RouterLink, PageHeaderComponent, ModalComponent],
    templateUrl: 'project.component.html',
    styleUrls: ['project.component.css'],
})
export class ProjectComponent {
    private api = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    t = inject(LocaleService).t;

    readonly featuredTypes = FEATURED_TYPES;
    readonly tileTypes = TILE_TYPES;
    readonly docTypes = DOCUMENT_TYPES;
    readonly docIcons = DOCUMENT_TYPE_ICONS;
    readonly projectIcons = PROJECT_TYPE_ICONS;
    readonly initials = projectInitials;
    readonly overdue = isOverdue;

    project = signal<ProjectDetail | null>(null);
    loading = signal(true);
    loadError = signal<string | null>(null);

    addingDocument = signal(false);
    editing = signal(false);
    editName = signal('');
    editDescription = signal('');
    actionError = signal<string | null>(null);
    busy = signal(false);
    // Deleting a project is two clicks on the same button rather than a second modal on top of the
    // first: the explanation of what survives is what matters here, and it fits under the button.
    confirmDelete = false;

    /** Documents grouped by type, each already ordered newest-first by the server. */
    byType = computed(() => {
        const groups = new Map<DocumentType, ProjectDocument[]>();
        for (const type of DOCUMENT_TYPES) groups.set(type, []);
        for (const doc of this.project()?.documents ?? []) groups.get(doc.documentType)?.push(doc);
        return groups;
    });

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (id) void this.load(id);
        });
    }

    countOf(type: DocumentType) {
        return this.byType().get(type)?.length ?? 0;
    }

    recentOf(type: DocumentType) {
        return (this.byType().get(type) ?? []).slice(0, RECENT_PER_TYPE);
    }

    latestOf(type: DocumentType) {
        return this.byType().get(type)?.[0] ?? null;
    }

    async load(id: string) {
        this.loading.set(true);
        this.loadError.set(null);
        try {
            this.project.set(await this.api.get(id));
        } catch (e) {
            this.project.set(null);
            this.loadError.set(httpErrorMessage(e, this.t().projects.notFound));
        } finally {
            this.loading.set(false);
        }
    }

    openDocument(doc: ProjectDocument) {
        void this.router.navigate(['/editor'], { queryParams: { draft: doc.id } });
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
        this.actionError.set(null);
        this.confirmDelete = false;
        this.editing.set(true);
    }

    async saveEdit() {
        const project = this.project();
        const name = this.editName().trim();
        if (!project || name.length === 0 || this.busy()) return;

        this.busy.set(true);
        this.actionError.set(null);
        try {
            await this.api.update(project.id, name, this.editDescription().trim(), project.coverUrl);
            this.project.set({ ...project, name, description: this.editDescription().trim() });
            this.editing.set(false);
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
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
