import { Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
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
import { RulerService } from '../core/ruler.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';

type Filter = 'all' | 'active' | 'archived';

// T-226 (ADR-168) — the project index: where a project is found, made and compared. The hub's left
// dock absorbed the *switch* between projects, not this list, so nothing here moved onto it and
// nothing from it moved here: the dock takes no search and no create, and this screen draws no
// current-project mark.
//
// A table you scan, so it keeps data-density="compact" (ADR-164 rule 6) — the hub, a screen you
// read, carries none.
@Component({
    selector: 'app-projects',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, RouterLink, ModalComponent,
        WorktopComponent, ShelfPanelComponent, IndexTabsComponent, SpecRowComponent,
        ButtonComponent, InputComponent, StampBadgeComponent,
    ],
    templateUrl: 'projects.component.html',
    styleUrls: ['projects.component.css'],
})
export class ProjectsComponent implements OnDestroy {
    private api = inject(ProjectsService);
    private router = inject(Router);
    private locale = inject(LocaleService);
    private ruler = inject(RulerService);
    t = this.locale.t;

    readonly projectTypes = PROJECT_TYPES;
    readonly typeIcons = PROJECT_TYPE_ICONS;
    readonly initials = projectInitials;

    // Archived projects are always fetched: the filter tiles carry counts, and a count you cannot
    // show until the user clicks the tile is not a count.
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

    /** Sums over the rows already fetched — nothing here is asked for separately (ADR-168 rule 5). */
    totals = computed(() => {
        const list = this.projects();
        return {
            documents: list.reduce((n, p) => n + p.documentCount, 0),
            tasks: list.reduce((n, p) => n + p.openTaskCount, 0),
            assets: list.reduce((n, p) => n + p.assetCount, 0),
        };
    });

    filterTabs = computed<IndexTabItem[]>(() => {
        const t = this.t().projects;
        return [
            { id: 'all', label: t.filterAll, badge: this.projects().length },
            { id: 'active', label: t.filterActive, badge: this.activeCount() },
            { id: 'archived', label: t.filterArchived, badge: this.archivedCount() },
        ];
    });

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

        effect(() => {
            const t = this.t().projects;
            this.ruler.publish({
                label: t.title,
                left: [{ text: t.sub(this.projects().length, this.activeCount()) }],
            });
        });
    }

    ngOnDestroy(): void {
        this.ruler.clear();
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

    pickFilter(id: string) {
        this.filter.set(id as Filter);
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
