import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '../core/i18n/locale.service';
import { GlossaryService, GlossaryTerm } from '../core/glossary.service';
import { AssetsService } from '../core/assets.service';
import { httpErrorMessage } from '../core/http-error.util';
import { PRIMARY_LANGUAGE, CONTENT_LANGUAGES } from '../core/languages';
import { PageHeaderComponent } from '../shared/page-header.component';
import { ModalComponent } from '../shared/modal.component';
import {
    LucideTrash2 as Trash2, LucidePlus as Plus,
    LucideRefreshCw as RefreshCw, LucideImage as ImageIcon, LucideX as X, LucideInfo as Info,
} from '@lucide/angular';

// Idea #11 — the glossary page. A term is defined once here and explained wherever it turns up on
// the blog; nothing is scanned or marked in the editor, since the ask was for the published page.
@Component({
    selector: 'app-glossary',
    imports: [
        FormsModule, PageHeaderComponent, ModalComponent,
        Trash2, Plus, RefreshCw, ImageIcon, X, Info,
    ],
    templateUrl: 'glossary.component.html',
    styleUrls: ['glossary.component.css'],
})
export class GlossaryComponent implements OnInit {
    t = inject(LocaleService).t;
    private api = inject(GlossaryService);
    private assets = inject(AssetsService);

    readonly contentLanguages = CONTENT_LANGUAGES;
    readonly primaryLanguage = PRIMARY_LANGUAGE;

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
    editLanguage = signal<string>(PRIMARY_LANGUAGE);

    // Terms are listed per language, because that is how they are matched.
    languageFilter = signal<string>(PRIMARY_LANGUAGE);

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
        return this.terms().filter(t => (t.language || PRIMARY_LANGUAGE) === lang);
    }

    countFor(lang: string): number {
        return this.terms().filter(t => (t.language || PRIMARY_LANGUAGE) === lang).length;
    }

    startNew() {
        this.selectedId.set(null);
        this.editTerm = '';
        this.editDescription = '';
        this.editAliases = '';
        this.editImageUrl.set(null);
        this.editLanguage.set(this.languageFilter());
        this.editing.set(true);
        this.error.set('');
    }

    startEdit(term: GlossaryTerm) {
        this.selectedId.set(term.id);
        this.editTerm = term.term;
        this.editDescription = term.description;
        this.editAliases = term.aliases;
        this.editImageUrl.set(term.imageUrl);
        this.editLanguage.set(term.language || PRIMARY_LANGUAGE);
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
}
