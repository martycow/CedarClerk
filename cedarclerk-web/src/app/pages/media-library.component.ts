import { Component, OnDestroy, booleanAttribute, computed, effect, inject, input, signal } from '@angular/core';
import { AssetsService, LibraryAsset, LibraryKind, LibraryPage } from '../core/assets.service';
import { ProjectsService, ProjectSummary } from '../core/projects.service';
import { AuthService } from '../core/auth.service';
import { formatBytes } from '../core/asset-index.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';

const PAGE_SIZE = 60;

// ADR-127 — the owner-wide media library. Unlike project-assets (an index of paths on someone's
// machine), everything here IS uploaded: these are the bytes the blog and Telegram serve. Delete
// goes through the server's usage scan — a file still referenced anywhere answers 409 with the
// referencing posts, and the modal shows them instead of guessing.
@Component({
    selector: 'app-media-library',
    imports: [
        IconComponent, ZonedDatePipe, IndexTabsComponent,
        SpecRowComponent, InputComponent, ButtonComponent, PageHeaderComponent, EmptyStateComponent,
    ],
    templateUrl: 'media-library.component.html',
    styleUrls: ['media-library.component.css'],
})
export class MediaLibraryComponent implements OnDestroy {
    private api = inject(AssetsService);
    private projectsApi = inject(ProjectsService);
    private auth = inject(AuthService);
    t = inject(LocaleService).t;

    /**
     * ADR-204 — the bucket this instance is pinned to. Set, the strip is gone and the list is one
     * project's files: that is how the project's Assets screen shows its uploaded half without a
     * second copy of this component. Unset, the page owns the strip and opens on every bucket.
     */
    readonly pinnedProject = input<string | null>(null);

    /**
     * Rendered inside another screen (the project's Assets board sets it alongside `pinnedProject`):
     * the page padding and the account-wide storage shelf are the host screen's business, so both
     * are dropped and what remains is one strip welded over one panel.
     */
    readonly embedded = input(false, { transform: booleanAttribute });

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

    /** null = every bucket · 'none' = the files that belong to no project · a guid = that project. */
    bucket = signal<string | null>(null);
    projects = signal<ProjectSummary[]>([]);

    selected = signal<LibraryAsset | null>(null);
    busy = signal(false);
    deleteError = signal<string | null>(null);
    usedBy = signal<{ draftId: string; title: string }[]>([]);
    uploading = signal(false);
    uploadError = signal<string | null>(null);

    readonly headerMeta = computed<HeaderMeta[]>(() => {
        const p = this.page();
        if (!p) return [];
        const total = p.counts.image + p.counts.video + p.counts.audio;
        const meta: HeaderMeta[] = [];
        if (total > 0) meta.push({ text: this.t().media.fileCount(total) });
        if (p.limitBytes) meta.push({ text: this.t().media.usage(formatBytes(p.usedBytes), formatBytes(p.limitBytes)) });
        return meta;
    });

    private thumbFailed = signal<ReadonlySet<string>>(new Set());
    private searchTimer: ReturnType<typeof setTimeout> | null = null;

    constructor() {
        // The names behind the strip. Absent while the module is off, which is what leaves the
        // strip out of a build that has no projects to sort files into.
        if (this.auth.indieDev()) {
            void this.projectsApi.list(true).then(list => this.projects.set(list)).catch(() => { });
        }
        // A pinned instance reloads when the project it is pinned to changes; the page's own
        // instance pins nothing and this settles once.
        effect(() => {
            this.pinnedProject();
            this.skip.set(0);
            void this.load();
        });
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
            project: this.pinnedProject() ?? this.bucket(),
            skip: this.skip(),
            take: PAGE_SIZE,
        };
    }

    /** All · No project · one per project that actually holds files, with its count as the badge. */
    readonly bucketTabs = computed<IndexTabItem[]>(() => {
        const labels = this.t().media;
        const buckets = this.page()?.buckets ?? [];
        if (!buckets.length) return [];
        const items: IndexTabItem[] = [
            { id: 'all', label: labels.bucketAll, badge: buckets.reduce((sum, b) => sum + b.count, 0) },
        ];
        const unfiled = buckets.find(b => b.projectId === null);
        if (unfiled) items.push({ id: 'none', label: labels.bucketNone, badge: unfiled.count, hint: labels.bucketNoneHint });
        for (const b of buckets) {
            if (b.projectId === null) continue;
            const name = this.projects().find(p => p.id === b.projectId)?.name;
            items.push({ id: b.projectId, label: name ?? labels.bucketGone, badge: b.count });
        }
        return items;
    });

    readonly bucketTab = computed(() => this.bucket() ?? 'all');

    pickBucket(id: string) {
        this.bucket.set(id === 'all' ? null : id);
        this.skip.set(0);
        this.selected.set(null);
        void this.load();
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

    clearFilters() {
        this.search.set('');
        this.type.set(null);
        this.bucket.set(null);
        this.skip.set(0);
        void this.reload();
    }

    async onUploadPicked(event: Event) {
        const input = event.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        if (!files.length || this.uploading()) return;
        this.uploading.set(true);
        this.uploadError.set(null);
        try {
            for (const file of files) await this.api.upload(file);
            this.skip.set(0);
            await this.reload();
        } catch (e) {
            this.uploadError.set(httpErrorMessage(e, this.t().media.uploadFailed));
        } finally {
            this.uploading.set(false);
        }
    }

    setView(view: 'grid' | 'list') {
        this.view.set(view);
        try { localStorage.setItem('cedar.mediaView', view); } catch { /* private mode */ }
    }

    /** The strip's own id for "no kind filter" — `null` is not a tile id. */
    readonly typeTab = computed<string>(() => this.type() ?? 'all');

    // A kind with nothing in it keeps losing its whole tile, which is not the badge's hide-at-zero
    // rule but a filter that would return an empty list whatever else was set.
    readonly typeTabs = computed<IndexTabItem[]>(() => {
        const counts = this.page()?.counts;
        const labels = this.t().media;
        const named: Record<LibraryKind, string> = {
            image: labels.images, video: labels.videos, audio: labels.audio,
        };
        const items: IndexTabItem[] = [{ id: 'all', label: labels.all, badge: this.totalUnfiltered() }];
        for (const kind of this.kinds) {
            const count = counts?.[kind] ?? 0;
            if (count > 0) items.push({ id: kind, label: named[kind], badge: count });
        }
        return items;
    });

    readonly viewTabs = computed<IndexTabItem[]>(() => [
        { id: 'grid', label: this.t().media.gridView },
        { id: 'list', label: this.t().media.listView },
    ]);

    usagePercent() {
        const p = this.page();
        if (!p || !p.limitBytes) return 0;
        return Math.min(100, Math.round(p.usedBytes / p.limitBytes * 100));
    }

    usagePercentLabel() {
        return this.page()?.limitBytes ? `${this.usagePercent()}%` : '';
    }

    pickType(id: string) {
        this.setType(id === 'all' ? null : id as LibraryKind);
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
