import { Component, OnDestroy, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AssetsService, LibraryAsset, LibraryKind, LibraryPage } from '../core/assets.service';
import { formatBytes } from '../core/asset-index.service';
import { IconName } from './icon-data.generated';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { IconComponent } from './icon.component';
import { ModalComponent } from './modal.component';

const PAGE_SIZE = 60;

// ADR-127 — the editor's pick-from-library modal. Deliberately its own lightweight component
// rather than a reuse of the /media page: the picker needs none of delete/usage, and
// editor.component is large enough without hosting a whole page inside it.
@Component({
    selector: 'app-media-picker',
    imports: [IconComponent, FormsModule, ModalComponent],
    templateUrl: './media-picker.component.html',
    styleUrl: './media-picker.component.css',
})
export class MediaPickerComponent implements OnDestroy {
    private api = inject(AssetsService);
    t = inject(LocaleService).t;

    picked = output<LibraryAsset>();
    closed = output<void>();

    readonly bytes = formatBytes;
    readonly kinds: LibraryKind[] = ['image', 'video', 'audio'];
    readonly kindIcons: Record<LibraryKind, IconName> = { image: 'image', video: 'video-camera', audio: 'music-notes' };

    page = signal<LibraryPage | null>(null);
    loading = signal(true);
    error = signal<string | null>(null);
    type = signal<LibraryKind | null>(null);
    search = signal('');
    skip = signal(0);

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
        this.error.set(null);
        try {
            this.page.set(await this.api.list({
                q: this.search().trim() || undefined,
                type: this.type(),
                skip: this.skip(),
                take: PAGE_SIZE,
            }));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().media.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    setType(type: LibraryKind | null) {
        this.type.set(type);
        this.skip.set(0);
        void this.load();
    }

    onSearch(value: string) {
        this.search.set(value);
        this.skip.set(0);
        if (this.searchTimer) clearTimeout(this.searchTimer);
        this.searchTimer = setTimeout(() => void this.load(), 250);
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

    get canPageBack() { return this.skip() > 0; }
    get canPageForward() { return this.skip() + PAGE_SIZE < (this.page()?.total ?? 0); }

    pageBack() {
        this.skip.set(Math.max(0, this.skip() - PAGE_SIZE));
        void this.load();
    }

    pageForward() {
        this.skip.set(this.skip() + PAGE_SIZE);
        void this.load();
    }
}
