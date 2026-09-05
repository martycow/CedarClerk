import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { LocaleService } from '../core/i18n/locale.service';
import {
    DOCUMENT_TYPE_ICONS,
    DocumentType,
    PROJECT_TYPES,
    PROJECT_TYPE_ICONS,
    ProjectSummary,
    ProjectType,
    ProjectsService,
    STARTER_DOCUMENT_TYPE,
    projectInitials,
} from '../core/projects.service';
import { Preset, PresetsService, parseProjectConfig } from '../core/presets.service';
import { MembersService, SharedProject } from '../core/members.service';
import { httpErrorMessage } from '../core/http-error.util';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';

// T-302 — 'shared' is a fourth tile on the same strip rather than a screen of its own: a project
// somebody shared is still a project, and the place a reader looks for "my projects" is this one.
// Until it existed, a member's only route back was the invitation mail, so losing the mail lost the
// project.
type Filter = 'all' | 'active' | 'archived' | 'shared';

/** The two shelves of the New-project dialog: the four built-in types, or the user's own presets. */
type CreateSource = 'builtin' | 'mine';

/** What the dialog's detail column reads back for the current pick. */
interface CreateSelection {
    preset: Preset | null;
    type: ProjectType;
    name: string;
    blurb: string;
    about: string;
    documentType: DocumentType;
    documentTitle: string;
    outline: string[];
}

// T-226 (ADR-168) — the project index: where a project is found, made and compared. Hub.png
// (ADR-239): a header with the two counts, one strip and a search, then a card grid that fills the
// width, with the invitation to start a project as its last cell. The "This week / Needs attention"
// strip the artboard draws needs an aggregate the API does not have (T-366).
@Component({
    selector: 'app-projects',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, NgTemplateOutlet, RouterLink, ModalComponent,
        PageHeaderComponent, EmptyStateComponent, IndexTabsComponent,
        ButtonComponent, InputComponent,
    ],
    templateUrl: 'projects.component.html',
    styleUrls: ['projects.component.css'],
})
export class ProjectsComponent {
    private api = inject(ProjectsService);
    private presetsApi = inject(PresetsService);
    private membersApi = inject(MembersService);
    private router = inject(Router);
    private locale = inject(LocaleService);
    t = this.locale.t;

    readonly projectTypes = PROJECT_TYPES;
    readonly typeIcons = PROJECT_TYPE_ICONS;
    readonly docIcons = DOCUMENT_TYPE_ICONS;
    readonly initials = projectInitials;

    // Archived projects are always fetched: the header counts them, and a count you cannot show
    // until the user clicks the tile is not a count.
    projects = signal<ProjectSummary[]>([]);
    loading = signal(true);
    loadError = signal<string | null>(null);
    filter = signal<Filter>('all');
    search = signal('');

    creating = signal(false);
    createType = signal<ProjectType>('empty');
    // T-331 — set when the pick came from a saved project preset rather than a built-in type.
    createPresetId = signal<string | null>(null);
    projectPresets = signal<Preset[]>([]);
    createSource = signal<CreateSource>('builtin');
    presetSearch = signal('');
    createName = signal('');
    createError = signal<string | null>(null);
    saving = signal(false);

    /** Projects other people share with this account — never mixed into the owned list. */
    shared = signal<SharedProject[]>([]);

    activeCount = computed(() => this.projects().filter(p => !p.archivedAt).length);
    archivedCount = computed(() => this.projects().filter(p => p.archivedAt).length);
    /** A shared project is still page data: when any card exists, creation belongs in the header. */
    hasProjectData = computed(() => this.projects().length > 0 || this.shared().length > 0);

    /** The header's meta line: the two state counts, from the rows already fetched. */
    headerMeta = computed<HeaderMeta[]>(() => {
        const t = this.t().projects;
        return [
            { text: t.activeCount(this.activeCount()) },
            { text: t.archivedCount(this.archivedCount()) },
        ];
    });

    sourceTabs = computed<IndexTabItem[]>(() => {
        const t = this.t().projects.create;
        return [
            { id: 'builtin', label: t.sourceBuiltIn },
            { id: 'mine', label: t.sourceMine, badge: this.projectPresets().length || undefined },
        ];
    });

    /** The search box narrows whichever shelf is open; it never empties the other one. */
    visibleTypes = computed(() => {
        const needle = this.presetSearch().trim().toLowerCase();
        const types = this.t().projects.projectTypes;
        return this.projectTypes.filter(type => !needle
            || types[type].name.toLowerCase().includes(needle)
            || types[type].blurb.toLowerCase().includes(needle));
    });

    visiblePresets = computed(() => {
        const needle = this.presetSearch().trim().toLowerCase();
        return this.projectPresets().filter(p => !needle
            || p.name.toLowerCase().includes(needle)
            || parseProjectConfig(p.configJson).description.toLowerCase().includes(needle));
    });

    /** The detail column: the pick, and the one document the project is born with (ADR-103). */
    selection = computed<CreateSelection | null>(() => {
        const t = this.t().projects;
        const presetId = this.createPresetId();
        const preset = presetId ? this.projectPresets().find(p => p.id === presetId) ?? null : null;
        const type = this.createType();
        const typeText = t.projectTypes[type];
        if (!typeText) return null;
        const config = preset ? parseProjectConfig(preset.configJson) : null;
        const documentType = (config?.documentType as DocumentType | null) ?? STARTER_DOCUMENT_TYPE[type];
        return {
            preset,
            type,
            name: preset ? preset.name : typeText.name,
            blurb: preset ? typeText.name : typeText.blurb,
            about: preset ? config?.description ?? '' : typeText.about,
            documentType,
            documentTitle: preset ? this.presetStarter(preset) : typeText.starter,
            outline: this.starterOutline(documentType, type),
        };
    });

    filterTabs = computed<IndexTabItem[]>(() => {
        const t = this.t().projects;
        return [
            { id: 'all', label: t.filterAll },
            { id: 'active', label: t.filterActive },
            { id: 'archived', label: t.filterArchived },
            { id: 'shared', label: t.filterShared },
        ];
    });

    /** The shared list under its own search, since the two lists never merge. */
    visibleShared = computed(() => {
        const needle = this.search().trim().toLowerCase();
        return this.shared().filter(p => !needle || p.name.toLowerCase().includes(needle));
    });

    visible = computed(() => {
        const needle = this.search().trim().toLowerCase();
        const filter = this.filter();
        if (filter === 'shared') return [];
        return this.projects().filter(p => {
            if (filter === 'active' && p.archivedAt) return false;
            if (filter === 'archived' && !p.archivedAt) return false;
            return needle.length === 0 || p.name.toLowerCase().includes(needle);
        });
    });

    constructor() {
        void this.load();
        void this.loadShared();
    }

    /** What the badge on a shared card says — the same three words the canvas already uses. */
    roleWord(role: string): string {
        const c = this.t().projects.canvas;
        return role === 'owner' ? c.roleOwner : role === 'viewer' ? c.roleViewer : c.roleEditor;
    }

    // Best-effort and silent: a failure here must not turn the reader's own project list into an
    // error screen. An empty shared tile is the same thing an empty shared list looks like.
    private async loadShared() {
        try { this.shared.set(await this.membersApi.shared()); }
        catch { this.shared.set([]); }
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
        this.createType.set('empty');
        this.createPresetId.set(null);
        this.createSource.set('builtin');
        this.presetSearch.set('');
        this.createName.set('');
        this.createError.set(null);
        this.creating.set(true);
        void this.loadPresets();
    }

    // T-331 — the user's own project presets stand beside the four built-in types. Failing to
    // load them is not an error the dialog reports: the built-ins are still a complete offer.
    private async loadPresets() {
        try { this.projectPresets.set(await this.presetsApi.list('project')); }
        catch { this.projectPresets.set([]); }
    }

    pickSource(id: string) {
        this.createSource.set(id as CreateSource);
    }

    presetType(preset: Preset): ProjectType {
        return parseProjectConfig(preset.configJson).projectType as ProjectType;
    }

    /** A preset's card line: its own description, or the name of the type it produces. */
    presetBlurb(preset: Preset): string {
        return parseProjectConfig(preset.configJson).description
            || this.t().projects.projectTypes[this.presetType(preset)]?.name
            || this.t().presets.kinds.project.name;
    }

    // Mirrors StarterTemplates.For on the server: the headings the starter document is born with,
    // in the interface language, which is the language the document is created in.
    starterOutline(documentType: DocumentType, projectType: ProjectType): string[] {
        const o = this.t().projects.create.outline;
        if (documentType === 'design') return projectType === 'jam' ? o.jamDesign : o.design;
        if (documentType === 'note') return projectType === 'empty' ? [] : o.note;
        if (documentType === 'changelog') return o.changelog;
        return [];
    }

    pickPreset(preset: Preset) {
        this.createPresetId.set(preset.id);
        this.createType.set(parseProjectConfig(preset.configJson).projectType as ProjectType);
    }

    pickType(type: ProjectType) {
        this.createPresetId.set(null);
        this.createType.set(type);
    }

    presetStarter(preset: Preset): string {
        const config = parseProjectConfig(preset.configJson);
        return config.documentTitle
            || this.t().projects.projectTypes[config.projectType as ProjectType].starter;
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
            const presetId = this.createPresetId();
            const preset = presetId
                ? this.projectPresets().find(p => p.id === presetId) ?? null
                : null;
            const created = await this.api.create({
                name,
                projectType: type,
                // A preset carries its own starter title; without one the client supplies it,
                // because the server has no second language (ADR-103's implementation note).
                documentTitle: preset
                    ? this.presetStarter(preset)
                    : this.t().projects.projectTypes[type].starter,
                language: this.locale.uiLang(),
                presetId: presetId ?? undefined,
            });
            this.creating.set(false);
            void this.router.navigate(['/projects', created.id]);
        } catch (e) {
            this.createError.set(httpErrorMessage(e, this.t().projects.create.failed));
        } finally {
            this.saving.set(false);
        }
    }
}
