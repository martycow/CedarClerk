import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { LocaleService } from '../core/i18n/locale.service';
import {
    PROJECT_TYPES,
    PROJECT_TYPE_ICONS,
    ProjectSummary,
    ProjectType,
    ProjectsService,
    projectInitials,
} from '../core/projects.service';
import { httpErrorMessage } from '../core/http-error.util';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { PageHeaderComponent } from '../shared/page-header.component';

type Filter = 'all' | 'active' | 'archived';

// Phase 13 / T-120 — the module's list screen, built from
// docs/design_handoff_indiedev_core_loop (chosen variant: table rows, not a card grid).
//
// Anatomy is deliberately the same as /drafts: toolbar → filter chips → table, compact density.
// Two screens that list things should not feel like two products, and the handoff says so.
@Component({
    selector: 'app-projects',
    imports: [IconComponent, ZonedDatePipe, FormsModule, PageHeaderComponent, ModalComponent],
    templateUrl: 'projects.component.html',
    styleUrls: ['projects.component.css'],
})
export class ProjectsComponent {
    private api = inject(ProjectsService);
    private router = inject(Router);
    private locale = inject(LocaleService);
    t = this.locale.t;

    readonly projectTypes = PROJECT_TYPES;
    readonly typeIcons = PROJECT_TYPE_ICONS;
    readonly initials = projectInitials;

    // Archived projects are always fetched: the filter chips carry counts, and a count you cannot
    // show until the user clicks the chip is not a count.
    projects = signal<ProjectSummary[]>([]);
    loading = signal(true);
    loadError = signal<string | null>(null);
    filter = signal<Filter>('all');
    search = signal('');

    creating = signal(false);
    createType = signal<ProjectType>('fullgame');
    createName = signal('');
    createError = signal<string | null>(null);
    saving = signal(false);

    activeCount = computed(() => this.projects().filter(p => !p.archivedAt).length);
    archivedCount = computed(() => this.projects().filter(p => p.archivedAt).length);

    visible = computed(() => {
        const needle = this.search().trim().toLowerCase();
        const filter = this.filter();
        return this.projects().filter(p => {
            if (filter === 'active' && p.archivedAt) return false;
            if (filter === 'archived' && !p.archivedAt) return false;
            return needle.length === 0 || p.name.toLowerCase().includes(needle);
        });
    });

    constructor() {
        void this.load();
    }

    async load() {
        this.loading.set(true);
        this.loadError.set(null);
        try {
            this.projects.set(await this.api.list(true));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    open(project: ProjectSummary) {
        void this.router.navigate(['/projects', project.id]);
    }

    startCreate() {
        this.createType.set('fullgame');
        this.createName.set('');
        this.createError.set(null);
        this.creating.set(true);
    }

    async create() {
        const name = this.createName().trim();
        if (name.length === 0 || this.saving()) return;

        this.saving.set(true);
        this.createError.set(null);
        try {
            // The starter document's title comes from the client because the server has no second
            // language — see ADR-103's implementation note.
            const type = this.createType();
            const created = await this.api.create({
                name,
                projectType: type,
                documentTitle: this.t().projects.projectTypes[type].starter,
                language: this.locale.uiLang(),
            });
            this.creating.set(false);
            void this.router.navigate(['/projects', created.id]);
        } catch (e) {
            this.createError.set(httpErrorMessage(e, this.t().projects.create.failed));
        } finally {
            this.saving.set(false);
        }
    }

    /** T-160 (ADR-133) — "Cedar Quest" from the empty state; deleting it later is the ordinary path. */
    async createExample() {
        if (this.saving()) return;
        this.saving.set(true);
        this.loadError.set(null);
        try {
            const created = await this.api.createExample(this.locale.uiLang());
            void this.router.navigate(['/projects', created.id]);
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.create.failed));
        } finally {
            this.saving.set(false);
        }
    }
}
