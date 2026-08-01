import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { LocaleService } from '../core/i18n/locale.service';
import { GlossaryService, GlossaryTerm } from '../core/glossary.service';
import { AssetsService } from '../core/assets.service';
import { httpErrorMessage } from '../core/http-error.util';
import { DEFAULT_PRIMARY_LANGUAGE, CONTENT_LANGUAGES, endonymOf } from '../core/languages';
import { PageHeaderComponent } from '../shared/page-header.component';
import { ModalComponent } from '../shared/modal.component';
import { IconComponent } from '../shared/icon.component';

// Idea #11 — the glossary page. A term is defined once here and explained wherever it turns up on
// the blog; nothing is scanned or marked in the editor, since the ask was for the published page.
@Component({
    selector: 'app-glossary',
    imports: [IconComponent, FormsModule, PageHeaderComponent, ModalComponent, NgTemplateOutlet],
    templateUrl: 'glossary.component.html',
    styleUrls: ['glossary.component.css'],
})
export class GlossaryComponent implements OnInit {
    t = inject(LocaleService).t;
    private api = inject(GlossaryService);
    private assets = inject(AssetsService);

    readonly contentLanguages = CONTENT_LANGUAGES;
    readonly primaryLanguage = DEFAULT_PRIMARY_LANGUAGE;
    readonly endonymOf = endonymOf;

    terms = signal<GlossaryTerm[]>([]);
    loading = signal(true);
    error = signal('');
    busy = signal(false);
    uploading = signal(false);
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

    async ngOnInit() {
        try {
            this.terms.set(await this.api.list());
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().glossary.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    visibleTerms(): GlossaryTerm[] {
        const lang = this.languageFilter();
        return this.terms().filter(t => (t.language || DEFAULT_PRIMARY_LANGUAGE) === lang);
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

    canSave(): boolean {
        return !this.busy() && this.editTerm.trim().length > 0 && this.editDescription.trim().length > 0;
    }

    async save() {
        if (!this.canSave()) return;
        this.busy.set(true);
        this.error.set('');
        const input = {
            term: this.editTerm.trim(),
            description: this.editDescription.trim(),
            aliases: this.editAliases.trim(),
            imageUrl: this.editImageUrl(),
            language: this.editLanguage(),
            isCaseSensitive: this.editCaseSensitive(),
        };
        try {
            const id = this.selectedId();
            if (id) {
                const saved = await this.api.update(id, input);
                this.terms.update(list => list.map(t => t.id === id ? saved : t));
            } else {
                const created = await this.api.create(input);
                this.terms.update(list => [...list, created].sort((a, b) => a.term.localeCompare(b.term)));
            }
            this.languageFilter.set(input.language);
            this.editing.set(false);
            this.selectedId.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().glossary.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    // The image goes through the ordinary asset upload, like the avatar (IF1): same type
    // whitelist, same storage quota, same public serving, no second pipeline.
    async onImageChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const file = input.files?.[0];
        input.value = '';
        if (!file) return;
        this.uploading.set(true);
        this.error.set('');
        try {
            const { url } = await this.assets.upload(file);
            this.editImageUrl.set(url);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().glossary.imageFailed));
        } finally {
            this.uploading.set(false);
        }
    }

    clearImage() {
        this.editImageUrl.set(null);
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

    // ─── Preview (Marty, 01.08.2026) ───────────────────────────────────────────
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
}
