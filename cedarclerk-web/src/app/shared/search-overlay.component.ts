import { Component, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { DOCUMENT_TYPE_ICONS, DocumentType } from '../core/projects.service';
import { DraftSearchHit, SearchService } from '../core/search.service';
import { IconComponent } from './icon.component';
import { IconName } from './icon-data.generated';

// Wave 1 item 2 — the Ctrl+K palette. Mounted once by the bench shell, so the shortcut works on
// every authenticated screen; it owns its own open state and listens for the key itself, which
// keeps the shell's edit to one template line.
@Component({
    selector: 'app-search-overlay',
    imports: [FormsModule, IconComponent],
    templateUrl: 'search-overlay.component.html',
    styleUrls: ['search-overlay.component.css'],
})
export class SearchOverlayComponent {
    private searchApi = inject(SearchService);
    private router = inject(Router);
    t = inject(LocaleService).t;

    open = signal(false);
    query = signal('');
    results = signal<DraftSearchHit[]>([]);
    loading = signal(false);
    failed = signal(false);
    /** True once at least one query has answered — what separates "empty" from "not asked yet". */
    searched = signal(false);
    index = signal(0);

    private queryInput = viewChild<ElementRef<HTMLInputElement>>('queryInput');
    private debounceTimer?: ReturnType<typeof setTimeout>;
    // Answers can land out of order on a slow link; only the newest request may write state.
    private request = 0;

    @HostListener('document:keydown', ['$event'])
    onGlobalKeydown(event: KeyboardEvent) {
        if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'k') {
            event.preventDefault();
            if (this.open()) this.close();
            else this.openOverlay();
        }
    }

    openOverlay() {
        this.open.set(true);
        this.query.set('');
        this.results.set([]);
        this.searched.set(false);
        this.failed.set(false);
        this.index.set(0);
        // The input renders on the next tick — @if has not put it in the DOM yet.
        setTimeout(() => this.queryInput()?.nativeElement.focus());
    }

    close() {
        this.open.set(false);
        clearTimeout(this.debounceTimer);
        this.request++;
        this.loading.set(false);
    }

    onQueryChange(value: string) {
        this.query.set(value);
        clearTimeout(this.debounceTimer);
        if (!value.trim()) {
            this.request++;
            this.results.set([]);
            this.searched.set(false);
            this.loading.set(false);
            this.failed.set(false);
            return;
        }
        this.debounceTimer = setTimeout(() => void this.runSearch(), 250);
    }

    private async runSearch() {
        const q = this.query().trim();
        if (!q) return;
        const request = ++this.request;
        this.loading.set(true);
        this.failed.set(false);
        try {
            const hits = await this.searchApi.drafts(q);
            if (request !== this.request) return;
            this.results.set(hits);
            this.index.set(0);
            this.searched.set(true);
        } catch {
            if (request !== this.request) return;
            this.results.set([]);
            this.failed.set(true);
            this.searched.set(true);
        } finally {
            if (request === this.request) this.loading.set(false);
        }
    }

    onInputKeydown(event: KeyboardEvent) {
        switch (event.key) {
            case 'Escape':
                event.preventDefault();
                this.close();
                break;
            case 'ArrowDown':
                event.preventDefault();
                this.move(1);
                break;
            case 'ArrowUp':
                event.preventDefault();
                this.move(-1);
                break;
            case 'Enter': {
                event.preventDefault();
                const hit = this.results()[this.index()];
                if (hit) this.openHit(hit);
                break;
            }
        }
    }

    private move(step: number) {
        const count = this.results().length;
        if (!count) return;
        this.index.set((this.index() + step + count) % count);
    }

    openHit(hit: DraftSearchHit) {
        this.close();
        void this.router.navigate(['/editor'], { queryParams: { draft: hit.id } });
    }

    iconOf(hit: DraftSearchHit): IconName {
        return DOCUMENT_TYPE_ICONS[hit.documentType as DocumentType] ?? 'newspaper';
    }
}
