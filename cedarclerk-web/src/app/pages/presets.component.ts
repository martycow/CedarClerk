import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import {
    ExportPresetConfig, PRESET_KINDS, Preset, PresetKind, PresetsService, ProjectPresetConfig,
    parseDocumentConfig, parseExportConfig, parseProjectConfig,
} from '../core/presets.service';
import {
    DOCUMENT_TYPES, DOCUMENT_TYPE_ICONS, DocumentType, PROJECT_TYPES, PROJECT_TYPE_ICONS, ProjectType,
    STARTER_DOCUMENT_TYPE,
} from '../core/projects.service';
import { CONTENT_LANGUAGES, endonymOf } from '../core/languages';
import { EXPORT_DESTINATIONS, EXPORT_DESTINATION_BRANDS, ExportDestinationId } from '../core/export-destinations';
import { IconName } from '../shared/icon-data.generated';
import { IconComponent } from '../shared/icon.component';
import { BrandIconComponent } from '../shared/brand-icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { HintDotComponent } from '../shared/hint-dot.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';

/** The form behind every kind — one shape, and each kind reads the fields it owns. */
interface PresetForm {
    name: string;
    baseType: DocumentType;
    headings: string;
    projectType: ProjectType;
    projectDocumentType: DocumentType | null;
    documentTitle: string;
    description: string;
    destinations: ExportDestinationId[];
    languages: string[];
}

// T-331/T-355 — the Preset Manager. Three kinds of named starting point: a document (what it
// publishes as and the headings it begins with), a project (its type, its first document and what
// that document is called) and an export (which destinations a post goes to). One screen rather
// than three, because they are the same object with three configs — the index strip switches which
// kind is being managed, exactly as it switches a language on the Glossary.
@Component({
    selector: 'app-presets',
    imports: [
        FormsModule, NgTemplateOutlet, IconComponent, BrandIconComponent, ButtonComponent, IndexTabsComponent,
        HintDotComponent, PageHeaderComponent, EmptyStateComponent,
    ],
    templateUrl: 'presets.component.html',
    styleUrls: ['presets.component.css'],
})
export class PresetsComponent {
    private api = inject(PresetsService);
    t = inject(LocaleService).t;

    readonly kinds = PRESET_KINDS;
    readonly baseTypes = DOCUMENT_TYPES;
    readonly typeIcons = DOCUMENT_TYPE_ICONS;
    readonly projectTypes = PROJECT_TYPES;
    readonly projectTypeIcons = PROJECT_TYPE_ICONS;
    readonly destinations = EXPORT_DESTINATIONS;
    readonly destinationBrands = EXPORT_DESTINATION_BRANDS;
    readonly contentLanguages = CONTENT_LANGUAGES;

    kind = signal<PresetKind>('document');
    presets = signal<Preset[]>([]);
    loading = signal(true);
    error = signal<string | null>(null);
    busy = signal(false);

    // The one being edited (its id) or 'new' while creating, or null.
    editingId = signal<string | null>(null);
    form = signal<PresetForm>(this.blank());

    headerMeta = computed<HeaderMeta[]>(() => [{ text: this.t().presets.count(this.presets().length) }]);

    constructor() {
        void this.load();
    }

    kindTabs(): IndexTabItem[] {
        return this.kinds.map(k => ({ id: k, label: this.kindLabel(k), hint: this.kindIntro(k) }));
    }

    kindLabel(kind: PresetKind): string { return this.t().presets.kinds[kind].name; }
    kindIntro(kind: PresetKind): string { return this.t().presets.kinds[kind].intro; }

    selectKind(kind: string) {
        if (kind === this.kind()) return;
        this.kind.set(kind as PresetKind);
        this.editingId.set(null);
        void this.load();
    }

    private blank(): PresetForm {
        return {
            name: '',
            baseType: 'post',
            headings: '',
            projectType: 'fullgame',
            projectDocumentType: null,
            documentTitle: '',
            description: '',
            destinations: [],
            languages: [],
        };
    }

    async load() {
        this.loading.set(true);
        this.error.set(null);
        try { this.presets.set(await this.api.list(this.kind())); }
        catch (e) { this.error.set(httpErrorMessage(e, this.t().presets.loadFailed)); }
        finally { this.loading.set(false); }
    }

    startNew() {
        this.form.set(this.blank());
        this.editingId.set('new');
    }

    startEdit(p: Preset) {
        const form = this.blank();
        form.name = p.name;
        if (p.kind === 'document') {
            const c = parseDocumentConfig(p.configJson);
            form.baseType = c.baseType as DocumentType;
            form.headings = c.headings.join('\n');
        } else if (p.kind === 'project') {
            const c = parseProjectConfig(p.configJson);
            form.projectType = c.projectType as ProjectType;
            form.projectDocumentType = c.documentType as DocumentType | null;
            form.documentTitle = c.documentTitle ?? '';
            form.description = c.description;
        } else {
            const c = parseExportConfig(p.configJson);
            form.destinations = c.destinations as ExportDestinationId[];
            form.languages = c.languages;
        }
        this.form.set(form);
        this.editingId.set(p.id);
    }

    cancel() { this.editingId.set(null); }

    pickBaseType(type: DocumentType) { this.form.update(f => ({ ...f, baseType: type })); }

    pickProjectType(type: ProjectType) { this.form.update(f => ({ ...f, projectType: type })); }

    // Null means "whatever the project type implies" — the same fallback the server applies, shown
    // as a choice rather than hidden behind an empty field.
    pickProjectDocumentType(type: DocumentType | null) {
        this.form.update(f => ({ ...f, projectDocumentType: type }));
    }

    toggleDestination(id: ExportDestinationId) {
        this.form.update(f => ({
            ...f,
            destinations: f.destinations.includes(id)
                ? f.destinations.filter(d => d !== id)
                : [...f.destinations, id],
        }));
    }

    toggleLanguage(code: string) {
        this.form.update(f => ({
            ...f,
            languages: f.languages.includes(code)
                ? f.languages.filter(l => l !== code)
                : [...f.languages, code],
        }));
    }

    canSave = computed(() => this.form().name.trim().length > 0);

    async save() {
        if (!this.canSave() || this.busy()) return;
        const f = this.form();
        const kind = this.kind();
        const config = kind === 'document'
            ? {
                baseType: f.baseType,
                icon: this.typeIcons[f.baseType],
                headings: f.headings.split('\n').map(h => h.trim()).filter(Boolean),
            }
            : kind === 'project'
                ? {
                    projectType: f.projectType,
                    documentType: f.projectDocumentType,
                    documentTitle: f.documentTitle.trim() || null,
                    description: f.description.trim(),
                } satisfies ProjectPresetConfig
                : { destinations: f.destinations, languages: f.languages } satisfies ExportPresetConfig;

        this.busy.set(true);
        this.error.set(null);
        try {
            const id = this.editingId();
            const input = { kind, name: f.name.trim(), config };
            if (id && id !== 'new') await this.api.update(id, input);
            else await this.api.create(input);
            this.editingId.set(null);
            await this.load();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().presets.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async remove(p: Preset) {
        if (!confirm(this.t().presets.deleteConfirm(p.name))) return;
        this.busy.set(true);
        try { await this.api.remove(p.id); await this.load(); }
        catch (e) { this.error.set(httpErrorMessage(e, this.t().presets.saveFailed)); }
        finally { this.busy.set(false); }
    }

    /** The icon a stored row is drawn with — its own for a document, its type's for the rest. */
    iconOf(p: Preset): IconName {
        if (p.kind === 'document') return parseDocumentConfig(p.configJson).icon as IconName;
        if (p.kind === 'project') {
            return this.projectTypeIcons[parseProjectConfig(p.configJson).projectType as ProjectType] ?? 'cube';
        }
        return 'paper-plane-tilt';
    }

    /** The badge beside a row's name: what it publishes as, or what type of project it builds. */
    labelOf(p: Preset): string {
        if (p.kind === 'document') return this.baseTypeName(parseDocumentConfig(p.configJson).baseType);
        if (p.kind === 'project') return this.projectTypeName(parseProjectConfig(p.configJson).projectType);
        return this.t().presets.export.destinationCount(parseExportConfig(p.configJson).destinations.length);
    }

    /** The line under a row's name — what the preset actually does, in its own terms. */
    summaryOf(p: Preset): string | null {
        if (p.kind === 'document') {
            const headings = parseDocumentConfig(p.configJson).headings;
            return headings.length ? headings.join(' · ') : null;
        }
        if (p.kind === 'project') {
            const c = parseProjectConfig(p.configJson);
            const starter = c.documentType ?? STARTER_DOCUMENT_TYPE[c.projectType as ProjectType] ?? 'post';
            const parts = [this.t().presets.project.startsWith(this.baseTypeName(starter))];
            if (c.documentTitle) parts.push(`«${c.documentTitle}»`);
            return parts.join(' · ');
        }
        const c = parseExportConfig(p.configJson);
        if (!c.destinations.length) return null;
        const names = c.destinations.map(d => this.destinationName(d)).join(' · ');
        return c.languages.length ? `${names} — ${c.languages.map(l => this.languageLabel(l)).join(' · ')}` : names;
    }

    baseTypeName(type: string): string {
        const t = this.t().projects.docTypes;
        return (t as Record<string, { name: string }>)[type]?.name ?? type;
    }

    projectTypeName(type: string): string {
        const t = this.t().projects.projectTypes;
        return (t as Record<string, { name: string }>)[type]?.name ?? type;
    }

    destinationName(id: string): string {
        return (this.t().presets.export.destinations as Record<string, string>)[id] ?? id;
    }

    defaultProjectDocumentTypeName(): string {
        return this.baseTypeName(STARTER_DOCUMENT_TYPE[this.form().projectType]);
    }

    languageLabel(code: string): string {
        return `${code.toUpperCase()} · ${endonymOf(code)}`;
    }
}
