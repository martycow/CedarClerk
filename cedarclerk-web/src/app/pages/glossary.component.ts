import { Component, OnDestroy, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { LocaleService } from '../core/i18n/locale.service';
import { GlossaryEntry, GlossaryEntryInput, GlossaryService, GlossaryTerm, glossaryTerms } from '../core/glossary.service';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
import { AuthService } from '../core/auth.service';
import { httpErrorMessage } from '../core/http-error.util';
import { DEFAULT_PRIMARY_LANGUAGE, CONTENT_LANGUAGES, endonymOf } from '../core/languages';
import { ModalComponent } from '../shared/modal.component';
import { IconComponent } from '../shared/icon.component';
import { GlossaryTermFormComponent } from '../shared/glossary-term-form.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { LanguageMenuComponent, LanguageMenuItem } from '../shared/language-menu.component';
import { SpecRowComponent, SpecScope } from '../bench/worktop/spec-row.component';
import { PlanLockComponent } from '../shared/plan-lock.component';
import { HintDotComponent } from '../shared/hint-dot.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { AppCommand, CommandRelease, CommandsService } from '../core/commands.service';
import { WorkspaceContextService, WorkspaceProperty } from '../core/workspace-context.service';

// The glossary page. An entry is defined once here and explained wherever it turns up on the blog.
// The sheet lists it per language, because that is how it is matched (ADR-320).
@Component({
    selector: 'app-glossary',
    imports: [
        IconComponent, FormsModule, ModalComponent, NgTemplateOutlet, GlossaryTermFormComponent,
        ButtonComponent, IndexTabsComponent, SpecRowComponent, PlanLockComponent, HintDotComponent,
        LanguageMenuComponent, PageHeaderComponent, EmptyStateComponent,
    ],
    templateUrl: 'glossary.component.html',
    styleUrls: ['glossary.component.css'],
})
export class GlossaryComponent implements OnInit, OnDestroy {
    t = inject(LocaleService).t;
    private api = inject(GlossaryService);
    private projectsApi = inject(ProjectsService);
    auth = inject(AuthService);
    private readonly workspace = inject(WorkspaceContextService);
    private readonly commands = inject(CommandsService);
    private commandRelease?: CommandRelease;

    // ADR-301 clause 4/5 — the whole-language translate run is real AI, so this screen registers
    // it with the rail's AI panel. The per-entry AI actions live in the entry form.
    constructor() {
        effect(() => {
            const term = this.selectedTerm();
            const c = this.t().shell.context;
            if (!term) {
                this.workspace.set({ surface: c.glossary });
                return;
            }
            const properties: WorkspaceProperty[] = [
                { label: c.language, value: term.language || this.primaryLanguage },
                { label: c.scope, value: this.scopeName(term) },
                { label: c.aliases, value: term.spellings.join(', ') || c.none },
                { label: c.usedIn, value: c.documentCount(term.usedInDrafts ?? 0) },
                // The spelling is the key every document's wikilink and every match is found by;
                // rewriting it silently unlinks the term everywhere it is used.
                { label: c.identifier, value: term.term, protected: true },
            ];
            this.workspace.set({
                surface: c.glossary,
                open: { id: term.id, kind: 'term', title: term.term, detail: term.language, icon: 'book-bookmark' },
                properties,
            });
        });

        effect(() => {
            this.t();
            this.auth.hasAiPlan();
            untracked(() => {
                this.commandRelease?.();
                this.commandRelease = this.commands.register(this.aiCommands());
            });
        });
    }

    ngOnDestroy() {
        this.commandRelease?.();
        this.workspace.clear();
    }

    /** `previewId` is the user's pick — clicking a card opens the preview; `selectedId` is only
        ever the term an edit form is open on. Reading the second alone left the rail empty for
        everyone who just clicked a term, which is the ordinary case. */
    selectedTerm(): GlossaryTerm | null {
        const id = this.previewId() ?? this.selectedId();
        return id ? this.terms().find(term => term.id === id) ?? null : null;
    }

    private scopeName(term: GlossaryTerm): string {
        if (!term.projectId) return this.t().shell.context.scopeGlobal;
        return this.projects().find(p => p.id === term.projectId)?.name ?? term.projectId;
    }

    private aiCommands(): readonly AppCommand[] {
        const ready = () => this.auth.hasAiPlan() && !this.translatingLang();
        return [
            {
                id: 'glossary.ai.translateAll', group: 'tools', label: this.t().glossary.translateAll,
                icon: 'translate', ai: true, enabled: ready, run: () => this.openTranslateAll(),
            },
        ];
    }

    readonly contentLanguages = CONTENT_LANGUAGES;
    readonly primaryLanguage = DEFAULT_PRIMARY_LANGUAGE;
    readonly endonymOf = endonymOf;

    entries = signal<GlossaryEntry[]>([]);
    /** Every entry in every language it has — the rows the sheet lists and the scanner matches. */
    terms = computed<GlossaryTerm[]>(() =>
        glossaryTerms(this.entries()).sort((a, b) => a.term.localeCompare(b.term)));
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

    // null = the "new term" form, otherwise the row the form was opened from. One form either
    // way, and it edits the whole entry: every language of it, not only the row that was clicked.
    selectedId = signal<string | null>(null);
    editing = signal(false);
    editEntry = signal<GlossaryEntry | null>(null);

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
            this.entries.set(await this.api.list());
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
        return this.contentLanguages.filter(language =>
            term.entry.languages.some(row => row.language === language));
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
        this.editEntry.set(null);
        this.editing.set(true);
        this.error.set('');
    }

    startEdit(term: GlossaryTerm) {
        this.selectedId.set(term.id);
        this.editEntry.set(term.entry);
        this.editing.set(true);
        this.error.set('');
    }

    cancelEdit() {
        this.editing.set(false);
        this.selectedId.set(null);
        this.editEntry.set(null);
    }

    // The values come from the shared form component rather than from fields on this page —
    // the page still owns which entry is being written and what happens after.
    async save(input: GlossaryEntryInput) {
        if (this.busy() || !input.name || !input.description || !input.languages.length) return;
        this.busy.set(true);
        this.error.set('');
        try {
            const entry = this.editEntry();
            if (entry) {
                const saved = await this.api.update(entry.id, input);
                this.entries.update(list => list.map(e => e.id === entry.id ? saved : e));
            } else {
                // The scope selector doubles as "where this one goes" — the hint under it says so,
                // because a filter that silently decides a property would be a trap.
                const created = await this.api.create({ ...input, projectId: this.newTermScope() });
                this.entries.update(list => [...list, created]);
            }
            // The entry may have just left the language on screen; follow it rather than show
            // a list it is no longer in.
            if (!input.languages.some(row => row.language === this.languageFilter()))
                this.selectLanguage(input.languages[0].language);
            this.cancelEdit();
            void this.refreshTerms();
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

    // Deleting removes the entry, so every language of it goes with the row that was clicked.
    async confirmDelete() {
        const target = this.deleteTarget();
        this.deleteConfirmId.set(null);
        if (!target) return;
        const entryId = target.entry.id;
        this.busy.set(true);
        try {
            await this.api.remove(entryId);
            if (this.editEntry()?.id === entryId) this.cancelEdit();
            if (this.previewTerm()?.entry.id === entryId) this.closePreview();
            this.entries.update(list => list.filter(e => e.id !== entryId));
            // Deleting a term can hand its spelling back to whichever one it was shadowing.
            void this.refreshTerms();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().glossary.deleteFailed));
        } finally {
            this.busy.set(false);
        }
    }

    aliasList(term: GlossaryTerm): string[] {
        return term.spellings;
    }

    translateSelection = signal<Set<string>>(new Set());
    translatingLang = signal<string | null>(null);
    translateError = signal('');

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

    // The whole-language sweep: one call per checked language, each costing one credit however
    // many entries it fills in.
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
                await this.api.translateAll(source, lang);
                await this.refreshTerms();
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
    // reproduces. The language switcher walks the languages of the entry.

    openPreview(term: GlossaryTerm) {
        this.previewId.set(term.id);
        this.previewLanguage.set(term.language || DEFAULT_PRIMARY_LANGUAGE);
    }

    closePreview() {
        this.previewId.set(null);
    }

    /** Every language of the previewed entry, including the one clicked, in content-language order. */
    previewGroup(): GlossaryTerm[] {
        const current = this.terms().find(t => t.id === this.previewId());
        if (!current) return [];
        const group = this.terms().filter(t => t.entry.id === current.entry.id);
        return this.contentLanguages
            .map(l => group.find(t => t.language === l))
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
    // T-350 — the strip lists the languages that actually hold terms, plus the primary and the one
    // currently selected; the menu beside it reaches the rest. A tile carries a count and a menu
    // row cannot, so the tabs stay where they earn their place — a flat row of every content
    // language did not, and ran off the edge as the list grew.
    private stripLanguages(): string[] {
        const shown = new Set<string>([DEFAULT_PRIMARY_LANGUAGE, this.languageFilter()]);
        for (const l of this.contentLanguages) if (this.countFor(l) > 0) shown.add(l);
        return this.contentLanguages.filter(l => shown.has(l));
    }

    /** What the menu offers: everything the strip does not already show. */
    offStripLanguages(): string[] {
        const shown = new Set(this.stripLanguages());
        return this.contentLanguages.filter(l => !shown.has(l));
    }

    languageMenuItems(): LanguageMenuItem[] {
        return this.contentLanguages.map(code => ({ code, hasContent: this.countFor(code) > 0 }));
    }

    languageTabs(): IndexTabItem[] {
        return this.stripLanguages().map(l => ({
            id: l,
            label: l.toUpperCase(),
            badge: this.countFor(l),
            badgeTitle: this.t().glossary.inspector.inLanguage,
            hint: endonymOf(l),
        }));
    }

    /** The card's caption: which language, which scope, how many. */
    sheetMeta(): string {
        const t = this.t().glossary;
        const scope = this.scopeFilter();
        const where = scope === null ? t.scopeAll : scope === '' ? t.scopeGlobal : this.projectName(scope);
        return `${this.languageFilter().toUpperCase()} · ${where} · ${this.visibleTerms().length}`;
    }

    headerMeta(): HeaderMeta[] {
        const t = this.t().glossary;
        return [
            { text: this.languageFilter().toUpperCase(), title: endonymOf(this.languageFilter()) },
            { text: t.rulerTerms(this.visibleTerms().length) },
            ...(this.projects().length ? [{ text: t.rulerScopes(this.projectsWithTerms()) }] : []),
        ];
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

    // ─── Where a term is used (T-260) ─────────────────────────────────────────────────────────

    /** Absent until the list endpoint has been read: no other response carries the count. */
    usageKnown(term: GlossaryTerm): boolean {
        return term.usedInDrafts != null;
    }

    /**
     * The count, plus the term that takes this one's spelling when there is one. A shadowed term
     * is not zeroed — a global term losing one project's spelling is still used everywhere else —
     * so the note has to read beside a real number as well as beside a nought.
     */
    usageValue(term: GlossaryTerm): string {
        const t = this.t().glossary.inspector;
        const count = t.usedInDrafts(term.usedInDrafts ?? 0);
        const winner = this.shadowingTerm(term);
        return winner ? `${count} · ${t.shadowedBy(winner.term)}` : count;
    }

    /**
     * Two terms in the *same* scope sharing a spelling is a glossary mistake: one of them will
     * never mark anything and its author has no way to know. Across scopes it is the override
     * T-125 exists for — a project term beating a global one is the point, not a defect.
     */
    usageIsMistake(term: GlossaryTerm): boolean {
        const winner = this.shadowingTerm(term);
        return !!winner && (winner.projectId ?? null) === (term.projectId ?? null);
    }

    /** Resolved against the terms already on screen — the shelf never asks the server for a name. */
    private shadowingTerm(term: GlossaryTerm): GlossaryTerm | null {
        const id = term.shadowedByTermId;
        return id ? this.terms().find(t => t.id === id) ?? null : null;
    }

    /**
     * A write can move both fields on rows other than the one written — taking a spelling away
     * from another term is exactly what shadowing is — and only the list endpoint carries either.
     * So a save is followed by a re-read rather than by a merge that leaves the numbers wrong.
     */
    private async refreshTerms() {
        try {
            this.entries.set(await this.api.list());
        } catch {
            // The merged list still stands; the counts catch up on the next load.
        }
    }

    /** Only the languages that actually carry a term — a full list would claim coverage. */
    usedLanguages(): string[] {
        return this.contentLanguages.filter(l => this.countFor(l) > 0).map(l => l.toUpperCase());
    }

    projectsWithTerms(): number {
        const ids = new Set(this.entries().map(t => t.projectId).filter((id): id is string => !!id));
        return ids.size;
    }
}
