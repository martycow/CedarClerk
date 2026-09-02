import { Component, ElementRef, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { DOCUMENT_TYPE_ICONS, DocumentType } from '../core/projects.service';
import { DraftSearchHit, SearchService } from '../core/search.service';
import { IconComponent } from './icon.component';
import { IconName } from './icon-data.generated';
import { OverlayCoordinatorService, OverlayLayerLease } from '../core/overlay-coordinator.service';

// Wave 1 item 2 — the Ctrl+K palette. Mounted once by the shell; the shell owns the shortcut and
// the overlay coordinator keeps this palette mutually exclusive with every other transient layer.
@Component({
    selector: 'app-search-overlay',
    imports: [FormsModule, IconComponent],
    templateUrl: 'search-overlay.component.html',
    styleUrls: ['search-overlay.component.css'],
    host: { '(document:keydown)': 'onDialogKeydown($event)' },
})
export class SearchOverlayComponent implements OnDestroy {
    private searchApi = inject(SearchService);
    private router = inject(Router);
    private overlays = inject(OverlayCoordinatorService);
    private host = inject(ElementRef<HTMLElement>);
    t = inject(LocaleService).t;

    open = computed(() => this.overlays.active() === 'search');
    query = signal('');
    results = signal<DraftSearchHit[]>([]);
    loading = signal(false);
    failed = signal(false);
    /** True once at least one query has answered — what separates "empty" from "not asked yet". */
    searched = signal(false);
    index = signal(0);

    private queryInput = viewChild<ElementRef<HTMLInputElement>>('queryInput');
    private panel = viewChild<ElementRef<HTMLElement>>('panel');
    private debounceTimer?: ReturnType<typeof setTimeout>;
    // Answers can land out of order on a slow link; only the newest request may write state.
    private request = 0;
    private layer?: OverlayLayerLease;
    private wasOpen = false;

    constructor() {
        effect(() => {
            const open = this.open();
            if (open && !this.wasOpen) this.startOpen();
            else if (this.wasOpen && !open) this.finishClose();
            this.wasOpen = open;
        });
    }

    openOverlay() {
        if (!this.overlays.open('search')) return;
        this.query.set('');
        this.results.set([]);
        this.searched.set(false);
        this.failed.set(false);
        this.index.set(0);
    }

    toggleOverlay() {
        if (this.open()) this.close();
        else this.openOverlay();
    }

    close() {
        this.overlays.close('search');
    }

    ngOnDestroy(): void {
        clearTimeout(this.debounceTimer);
        this.layer?.release();
    }

    private startOpen() {
        this.layer = this.overlays.registerLayer(this.host.nativeElement, 'search');
        queueMicrotask(() => {
            if (!this.open()) return;
            const panel = this.panel()?.nativeElement;
            if (panel) this.overlays.focusFirst(panel, this.queryInput()?.nativeElement);
        });
    }

    private finishClose() {
        this.layer?.release();
        this.layer = undefined;
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

    onDialogKeydown(event: KeyboardEvent): void {
        if (!this.open() || !this.layer?.isTop()) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopImmediatePropagation();
            this.close();
        } else if (event.key === 'Tab') {
            const panel = this.panel()?.nativeElement;
            if (panel) this.overlays.trapTab(panel, event);
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
