import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AssetsService, LibraryAsset, LibraryKind, LibraryPage } from '../core/assets.service';
import { formatBytes } from '../core/asset-index.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';
import { ModalComponent } from '../shared/modal.component';
import { PageHeaderComponent } from '../shared/page-header.component';

const PAGE_SIZE = 60;

// ADR-127 — the owner-wide media library. Unlike project-assets (an index of paths on someone's
// machine), everything here IS uploaded: these are the bytes the blog and Telegram serve. Delete
// goes through the server's usage scan — a file still referenced anywhere answers 409 with the
// referencing posts, and the modal shows them instead of guessing.
@Component({
    selector: 'app-media-library',
    imports: [IconComponent, FormsModule, ZonedDatePipe, ModalComponent, PageHeaderComponent],
    templateUrl: 'media-library.component.html',
    styleUrls: ['media-library.component.css'],
})
export class MediaLibraryComponent implements OnDestroy {
    private api = inject(AssetsService);
    t = inject(LocaleService).t;

    readonly bytes = formatBytes;
    readonly kinds: LibraryKind[] = ['image', 'video', 'audio'];
    readonly kindIcons: Record<LibraryKind, IconName> = { image: 'image', video: 'video-camera', audio: 'music-notes' };

    page = signal<LibraryPage | null>(null);
    loading = signal(true);
    loadError = signal<string | null>(null);

    view = signal<'grid' | 'list'>(this.loadView());
    type = signal<LibraryKind | null>(null);
    search = signal('');
    skip = signal(0);

    selected = signal<LibraryAsset | null>(null);
    busy = signal(false);
    deleteError = signal<string | null>(null);
    usedBy = signal<{ draftId: string; title: string }[]>([]);

    private thumbFailed = signal<ReadonlySet<string>>(new Set());
    private searchTimer: ReturnType<typeof setTimeout> | null = null;

    constructor() {
        void this.load();
    }

    ngOnDestroy() {
        if (this.searchTimer) clearTimeout(this.searchTimer);
    }

    async load() {
        this.loading.set(true);
        this.loadError.set(null);
        try {
            this.page.set(await this.api.list(this.query()));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().media.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    private query() {
        return {
            q: this.search().trim() || undefined,
            type: this.type(),
            skip: this.skip(),
            take: PAGE_SIZE,
        };
    }

    private async reload() {
        try {
            this.page.set(await this.api.list(this.query()));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().media.loadFailed));
        }
    }

    setType(type: LibraryKind | null) {
        this.type.set(type);
        this.skip.set(0);
        void this.reload();
    }

    onSearch(value: string) {
        this.search.set(value);
        this.skip.set(0);
        if (this.searchTimer) clearTimeout(this.searchTimer);
        this.searchTimer = setTimeout(() => void this.reload(), 250);
    }

    setView(view: 'grid' | 'list') {
        this.view.set(view);
        try { localStorage.setItem('cedar.mediaView', view); } catch { /* private mode */ }
    }

    totalUnfiltered() {
        const c = this.page()?.counts;
        return c ? c.image + c.video + c.audio : 0;
    }

    kindOf(asset: LibraryAsset): LibraryKind {
        if (asset.contentType.startsWith('video/')) return 'video';
        if (asset.contentType.startsWith('audio/')) return 'audio';
        return 'image';
    }

    url(asset: LibraryAsset) {
        return `/media/${asset.localPath}`;
    }

    showsThumbnail(asset: LibraryAsset) {
        return this.kindOf(asset) === 'image' && !this.thumbFailed().has(asset.id);
    }

    onThumbError(asset: LibraryAsset) {
        this.thumbFailed.update(set => new Set(set).add(asset.id));
    }

    open(asset: LibraryAsset) {
        this.selected.set(asset);
        this.deleteError.set(null);
        this.usedBy.set([]);
    }

    close() {
        this.selected.set(null);
    }

    async confirmDelete() {
        const asset = this.selected();
        if (!asset || this.busy()) return;
        this.busy.set(true);
        this.deleteError.set(null);
        this.usedBy.set([]);
        try {
            await this.api.remove(asset.id);
            this.selected.set(null);
            await this.reload();
        } catch (e) {
            // 409 carries the referencing posts — show where the file lives instead of a bare "no".
            const body = (e as { error?: { usedBy?: { draftId: string; title: string }[] } }).error;
            this.usedBy.set(body?.usedBy ?? []);
            this.deleteError.set(httpErrorMessage(e, this.t().media.deleteFailed));
        } finally {
            this.busy.set(false);
        }
    }

    get canPageBack() { return this.skip() > 0; }
    get canPageForward() { return this.skip() + PAGE_SIZE < (this.page()?.total ?? 0); }

    pageBack() {
        this.skip.set(Math.max(0, this.skip() - PAGE_SIZE));
        void this.reload();
    }

    pageForward() {
        this.skip.set(this.skip() + PAGE_SIZE);
        void this.reload();
    }

    rangeLabel() {
        const page = this.page();
        if (!page || page.total === 0) return '';
        const from = this.skip() + 1;
        const to = Math.min(this.skip() + page.items.length, page.total);
        return `${from}–${to} / ${page.total}`;
    }

    private loadView(): 'grid' | 'list' {
        try { return localStorage.getItem('cedar.mediaView') === 'list' ? 'list' : 'grid'; }
        catch { return 'grid'; }
    }
}
