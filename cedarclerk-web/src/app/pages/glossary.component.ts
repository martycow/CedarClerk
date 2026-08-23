import { Component, OnDestroy, OnInit, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { LocaleService } from '../core/i18n/locale.service';
import { GlossaryService, GlossaryTerm, GlossaryTermInput } from '../core/glossary.service';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
import { AuthService } from '../core/auth.service';
import { httpErrorMessage } from '../core/http-error.util';
import { DEFAULT_PRIMARY_LANGUAGE, CONTENT_LANGUAGES, endonymOf } from '../core/languages';
import { ModalComponent } from '../shared/modal.component';
import { IconComponent } from '../shared/icon.component';
import { GlossaryTermFormComponent } from '../shared/glossary-term-form.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { SpecRowComponent, SpecScope } from '../bench/worktop/spec-row.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';
import { RailActionsService } from '../core/rail-actions.service';
import { RulerService } from '../core/ruler.service';

// Idea #11 — the glossary page. A term is defined once here and explained wherever it turns up on
// the blog; nothing is scanned or marked in the editor, since the ask was for the published page.
@Component({
    selector: 'app-glossary',
    imports: [
        IconComponent, FormsModule, ModalComponent, NgTemplateOutlet, GlossaryTermFormComponent,
        ButtonComponent, IndexTabsComponent, LeafTagComponent, ShelfPanelComponent, SpecRowComponent,
        WorktopComponent,
    ],
    templateUrl: 'glossary.component.html',
    styleUrls: ['glossary.component.css'],
})
export class GlossaryComponent implements OnInit, OnDestroy {
    t = inject(LocaleService).t;
    private api = inject(GlossaryService);
    private projectsApi = inject(ProjectsService);
    auth = inject(AuthService);
    private ruler = inject(RulerService);
    private rail = inject(RailActionsService);

    readonly contentLanguages = CONTENT_LANGUAGES;
    readonly primaryLanguage = DEFAULT_PRIMARY_LANGUAGE;
    readonly endonymOf = endonymOf;

    terms = signal<GlossaryTerm[]>([]);
    /**
     * T-125 — which scope is being looked at: null = everything, '' = global only, an id = that
     * project (its own terms plus the global ones, the same set its documents render with).
     */
    scopeFilter = signal<string | null>(null);
    projects = signal<ProjectSummary[]>([]);
    loading = signal(true);
    error = signal('');
    busy = signal(false);
    deleteConfirmId = signal<string | null>(null);

    // null = the "new term" form, otherwise the id being edited. One form either way: a separate
    // create dialog and edit pane would be the same six fields twice.
    selectedId = signal<string | null>(null);
    editing = signal(false);

    editTerm = '';
    editDescription = '';
    editAliases = '';
    editImageUrl = signal<string | null>(null);
    editLanguage = signal<string>(DEFAULT_PRIMARY_LANGUAGE);
    editCaseSensitive = signal(false);

    // The term whose tooltip is being previewed, and which language version of it is on screen.
    // Separate from `selectedId` on purpose: previewing is reading, editing is writing, and the
    // list has to be able to show one card in each state at the same time.
    previewId = signal<string | null>(null);
    previewLanguage = signal<string>(DEFAULT_PRIMARY_LANGUAGE);

    // Terms are listed per language, because that is how they are matched.
    languageFilter = signal<string>(DEFAULT_PRIMARY_LANGUAGE);
    readonly pageSize = 20;
    page = signal(1);

    async ngOnInit() {
        try {
            this.terms.set(await this.api.list());
            // Only when the module is on: with it off there are no projects, and the scope row
            // has nothing to offer beyond "global", which is the only scope that exists there.
            if (this.auth.indieDev()) {
                try {
                    this.projects.set(await this.projectsApi.list());
                } catch {
                    this.projects.set([]);
                }
            }
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().glossary.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    visibleTerms(): GlossaryTerm[] {
        const lang = this.languageFilter();
        const scope = this.scopeFilter();
        return this.terms().filter(t => {
            if ((t.language || DEFAULT_PRIMARY_LANGUAGE) !== lang) return false;
            if (scope === null) return true;
            if (scope === '') return !t.projectId;
            // A project's view includes the global terms, because that is what its documents see.
            return !t.projectId || t.projectId === scope;
        });
    }

    pageCount(): number {
        return Math.max(1, Math.ceil(this.visibleTerms().length / this.pageSize));
    }

    pageNumber(): number {
        return Math.min(this.page(), this.pageCount());
    }

    pagedTerms(): GlossaryTerm[] {
        const start = (this.pageNumber() - 1) * this.pageSize;
        return this.visibleTerms().slice(start, start + this.pageSize);
    }

    selectLanguage(language: string) {
        this.languageFilter.set(language);
        this.page.set(1);
    }

    selectScope(scope: string | null) {
        this.scopeFilter.set(scope);
        this.page.set(1);
    }

    setPage(page: number) {
        this.page.set(Math.max(1, Math.min(page, this.pageCount())));
    }

    groupLanguages(term: GlossaryTerm): string[] {
        const root = term.sourceTermId ?? term.id;
        const group = this.terms().filter(t => (t.sourceTermId ?? t.id) === root);
        return this.contentLanguages.filter(language =>
            group.some(t => (t.language || DEFAULT_PRIMARY_LANGUAGE) === language));
    }

    scopeCount(scope: string | null): number {
        const lang = this.languageFilter();
        return this.terms().filter(t => {
            if ((t.language || DEFAULT_PRIMARY_LANGUAGE) !== lang) return false;
            if (scope === null) return true;
            if (scope === '') return !t.projectId;
            return t.projectId === scope;
        }).length;
    }

    projectName(id: string): string {
        return this.projects().find(p => p.id === id)?.name ?? '';
    }

    /** Where "New term" will put it: the selected project, or global for "all" and "global". */
    newTermScope(): string | null {
        const scope = this.scopeFilter();
        return scope ? scope : null;
    }

    countFor(lang: string): number {
        return this.terms().filter(t => (t.language || DEFAULT_PRIMARY_LANGUAGE) === lang).length;
    }

    startNew() {
        this.selectedId.set(null);
        this.editTerm = '';
        this.editDescription = '';
        this.editAliases = '';
        this.editImageUrl.set(null);
        this.editLanguage.set(this.languageFilter());
        this.editCaseSensitive.set(false);
        this.editing.set(true);
        this.error.set('');
    }

    startEdit(term: GlossaryTerm) {
        this.selectedId.set(term.id);
        this.editTerm = term.term;
        this.editDescription = term.description;
        this.editAliases = term.aliases;
        this.editImageUrl.set(term.imageUrl);
        this.editLanguage.set(term.language || DEFAULT_PRIMARY_LANGUAGE);
        this.editCaseSensitive.set(term.isCaseSensitive);
        this.editing.set(true);
        this.error.set('');
    }

    cancelEdit() {
        this.editing.set(false);
        this.selectedId.set(null);
    }

    // The values come from the shared form component rather than from fields on this page —
    // the page still owns which row is being written and what happens after.
    async save(input: GlossaryTermInput) {
        if (this.busy() || !input.term || !input.description) return;
        this.busy.set(true);
        this.error.set('');
        try {
            const id = this.selectedId();
            if (id) {
                const saved = await this.api.update(id, input);
                this.terms.update(list => list.map(t => t.id === id ? saved : t));
            } else {
                // The scope selector doubles as "where this one goes" — the hint under it says so,
                // because a filter that silently decides a property would be a trap.
                const created = await this.api.create({ ...input, projectId: this.newTermScope() });
                this.terms.update(list => [...list, created].sort((a, b) => a.term.localeCompare(b.term)));
            }
            this.selectLanguage(input.language);
            this.editing.set(false);
            this.selectedId.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().glossary.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    deleteTarget(): GlossaryTerm | null {
        const id = this.deleteConfirmId();
        return id ? this.terms().find(t => t.id === id) ?? null : null;
    }

    async confirmDelete() {
        const id = this.deleteConfirmId();
        this.deleteConfirmId.set(null);
        if (!id) return;
        this.busy.set(true);
        try {
            await this.api.remove(id);
            this.terms.update(list => list.filter(t => t.id !== id));
            if (this.selectedId() === id) this.cancelEdit();
            if (this.previewId() === id) this.closePreview();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().glossary.deleteFailed));
        } finally {
            this.busy.set(false);
        }
    }

    aliasList(term: GlossaryTerm): string[] {
        return term.aliases.split(',').map(a => a.trim()).filter(a => a.length > 0);
    }

    // ADR-061 — "selected languages" is a frontend loop: one /translate call per checked
    // language, sequentially, so quota use and errors stay per-language.
    translateFor = signal<GlossaryTerm | null>(null);
    translateSelection = signal<Set<string>>(new Set());
    translatingLang = signal<string | null>(null);
    translateError = signal('');

    translateTargets(term: GlossaryTerm): string[] {
        return this.contentLanguages.filter(l => l !== (term.language || DEFAULT_PRIMARY_LANGUAGE));
    }

    openTranslate(term: GlossaryTerm) {
        this.translateFor.set(term);
        this.translateSelection.set(new Set());
        this.translateError.set('');
    }

    closeTranslate() {
        if (this.translatingLang()) return;
        this.translateFor.set(null);
    }

    toggleTranslateLang(lang: string) {
        this.translateSelection.update(s => {
            const next = new Set(s);
            next.has(lang) ? next.delete(lang) : next.add(lang);
            return next;
        });
    }

    canTranslate(): boolean {
        return this.translateSelection().size > 0 && !this.translatingLang();
    }

    async runTranslate() {
        const source = this.translateFor();
        if (!source || !this.canTranslate()) return;
        this.translateError.set('');
        const langs = this.contentLanguages.filter(l => this.translateSelection().has(l));
        for (const lang of langs) {
            this.translatingLang.set(lang);
            try {
                const saved = await this.api.translate(source.id, lang);
                this.terms.update(list => list.some(t => t.id === saved.id)
                    ? list.map(t => t.id === saved.id ? saved : t)
                    : [...list, saved].sort((a, b) => a.term.localeCompare(b.term)));
                this.toggleTranslateLang(lang);
            } catch (e) {
                // Stop on the first failure; the untouched languages stay checked for a retry.
                this.translateError.set(httpErrorMessage(e, this.t().glossary.translateFailed));
                this.translatingLang.set(null);
                return;
            }
        }
        this.translatingLang.set(null);
        this.translateFor.set(null);
    }

    // ADR-062 — the whole-language sweep. Reuses the per-term modal's selection/progress/error
    // signals (only one of the two modals is ever open) but calls the batch endpoint, so each
    // checked language costs one AI call regardless of how many terms there are.
    translateAllOpen = signal(false);

    translateAllTargets(): string[] {
        return this.contentLanguages.filter(l => l !== this.languageFilter());
    }

    openTranslateAll() {
        this.translateAllOpen.set(true);
        this.translateSelection.set(new Set());
        this.translateError.set('');
    }

    closeTranslateAll() {
        if (this.translatingLang()) return;
        this.translateAllOpen.set(false);
    }

    async runTranslateAll() {
        if (!this.canTranslate()) return;
        this.translateError.set('');
        const source = this.languageFilter();
        const langs = this.contentLanguages.filter(l => this.translateSelection().has(l));
        for (const lang of langs) {
            this.translatingLang.set(lang);
            try {
                const { terms } = await this.api.translateAll(source, lang);
                this.terms.update(list => {
                    const byId = new Map(list.map(t => [t.id, t]));
                    for (const t of terms) byId.set(t.id, t);
                    return [...byId.values()].sort((a, b) => a.term.localeCompare(b.term));
                });
                this.toggleTranslateLang(lang);
            } catch (e) {
                // Stop on the first failure; the untouched languages stay checked for a retry.
                this.translateError.set(httpErrorMessage(e, this.t().glossary.translateFailed));
                this.translatingLang.set(null);
                return;
            }
        }
        this.translatingLang.set(null);
        this.translateAllOpen.set(false);
    }

    // ─── Preview ──────────────────────────────────────────────────────────────
    // The point is to see the real tooltip, not a description in a form field: the blog renders
    // the term as a heading, the description under it and an optional image, and that is what this
    // reproduces. The language switcher walks the translation group rather than the whole list,
    // which is why GlossaryTerm.sourceTermId exists.

    openPreview(term: GlossaryTerm) {
        this.previewId.set(term.id);
        this.previewLanguage.set(term.language || DEFAULT_PRIMARY_LANGUAGE);
    }

    closePreview() {
        this.previewId.set(null);
    }

    /** Every language version of the previewed term, including itself, in content-language order. */
    previewGroup(): GlossaryTerm[] {
        const current = this.terms().find(t => t.id === this.previewId());
        if (!current) return [];
        const root = current.sourceTermId ?? current.id;
        const group = this.terms().filter(t => (t.sourceTermId ?? t.id) === root);
        return this.contentLanguages
            .map(l => group.find(t => (t.language || DEFAULT_PRIMARY_LANGUAGE) === l))
            .filter((t): t is GlossaryTerm => !!t);
    }

    /** What the tooltip shows right now — the group member for the selected language. */
    previewTerm(): GlossaryTerm | null {
        const lang = this.previewLanguage();
        return this.previewGroup().find(t => (t.language || DEFAULT_PRIMARY_LANGUAGE) === lang)
            ?? this.terms().find(t => t.id === this.previewId())
            ?? null;
    }

    // ─── The bench's own chrome (ADR-167 clauses 7 and 8) ─────────────────────────────────────
    // The language strip is an index: it picks which set of terms the sheet lists, and the tally on
    // a tile is that set's size, so an empty language is visibly empty before it is opened.
    languageTabs(): IndexTabItem[] {
        return this.contentLanguages.map(l => ({
            id: l,
            label: l.toUpperCase(),
            badge: this.countFor(l),
            badgeTitle: this.t().glossary.inspector.inLanguage,
            hint: endonymOf(l),
        }));
    }

    worktopLabel(): string {
        return this.t().glossary.crumb;
    }

    worktopMeta(): string {
        const t = this.t().glossary;
        const scope = this.scopeFilter();
        const where = scope === null ? t.scopeAll : scope === '' ? t.scopeGlobal : this.projectName(scope);
        return `${this.languageFilter().toUpperCase()} · ${where} · ${this.visibleTerms().length}`;
    }

    /** Exclusive, so the shelf can never describe a term and the glossary at the same time. */
    inspectorScope(): SpecScope {
        return this.previewTerm() ? 'selection' : 'document';
    }

    /** The sign names the sheet; the count slot names what is on it. */
    inspectorTitle(): string {
        return this.t().editor.inspector.title;
    }

    inspectorScopeWord(): string {
        const t = this.t().glossary.inspector;
        return this.inspectorScope() === 'selection' ? t.scopeTerm : t.scopeGlossary;
    }

    /** Only the languages that actually carry a term — a full list would claim coverage. */
    usedLanguages(): string[] {
        return this.contentLanguages.filter(l => this.countFor(l) > 0).map(l => l.toUpperCase());
    }

    projectsWithTerms(): number {
        const ids = new Set(this.terms().map(t => t.projectId).filter((id): id is string => !!id));
        return ids.size;
    }

    private readonly rulerFeed = effect(() => {
        const t = this.t().glossary;
        this.ruler.publish({
            label: t.crumb,
            left: [{ text: this.languageFilter().toUpperCase(), title: endonymOf(this.languageFilter()) }],
            right: [
                { text: t.rulerTerms(this.visibleTerms().length) },
                ...(this.projects().length ? [{ text: t.rulerScopes(this.projectsWithTerms()) }] : []),
            ],
        });
    });

    // The screen's one primary action (ADR-159 clause 1): creating a term is the only command here
    // that belongs to the glossary rather than to one row of it.
    private readonly railFeed = effect(() => {
        const t = this.t().glossary;
        this.rail.publish({
            primary: { label: t.newTerm, icon: 'plus', hint: t.newTerm, run: () => this.startNew() },
        });
    });

    ngOnDestroy() {
        this.ruler.clear();
        this.rail.clear();
    }
}
