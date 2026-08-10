import { DatePipe } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import {
    ASSET_KINDS,
    ASSET_KIND_ICONS,
    AssetDetail,
    AssetEntry,
    AssetIndexService,
    AssetKind,
    AssetPage,
    ScanState,
    desktopBridge,
    formatBytes,
} from '../core/asset-index.service';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { PageHeaderComponent } from '../shared/page-header.component';

const PAGE_SIZE = 60;
const RECENT_FOLDERS_KEY = 'cedar.assetFolders';
const SCAN_POLL_MS = 700;

// T-122 (ADR-107) — the asset screen, from docs/design_handoff_indiedev_core_loop §9-10.
//
// The one thing every state on this screen has to keep saying: **nothing is uploaded**. It is an
// index of paths on this machine, so a file that is not there right now reads "not found at path",
// never "deleted", and a kind with no thumbnail says "no preview · model" rather than showing an
// empty rectangle that looks like a broken image.
@Component({
    selector: 'app-project-assets',
    imports: [IconComponent, DatePipe, FormsModule, PageHeaderComponent, ModalComponent],
    templateUrl: 'project-assets.component.html',
    styleUrls: ['project-assets.component.css'],
})
export class ProjectAssetsComponent implements OnDestroy {
    private api = inject(AssetIndexService);
    private projects = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    auth = inject(AuthService);
    t = inject(LocaleService).t;

    readonly kinds = ASSET_KINDS;
    readonly kindIcons = ASSET_KIND_ICONS;
    readonly bytes = formatBytes;
    readonly desktop = desktopBridge();

    projectId = signal<string>('');
    project = signal<ProjectDetail | null>(null);
    page = signal<AssetPage | null>(null);
    loading = signal(true);
    loadError = signal<string | null>(null);

    view = signal<'grid' | 'list'>(this.loadView());
    kind = signal<AssetKind | null>(null);
    missingOnly = signal(false);
    search = signal('');
    skip = signal(0);

    scan = signal<ScanState | null>(null);
    picking = signal(false);
    pickPath = signal('');
    pickError = signal<string | null>(null);
    recentFolders = signal<string[]>(this.loadRecent());

    selected = signal<AssetDetail | null>(null);
    busy = signal(false);

    private pollTimer: ReturnType<typeof setInterval> | null = null;
    private searchTimer: ReturnType<typeof setTimeout> | null = null;

    /** No root chosen yet — the screen is the pick-a-folder card and nothing else. */
    needsFolder = computed(() => !this.loading() && !this.page()?.rootPath);

    scanning = computed(() => this.scan()?.running === true);

    scanPercent = computed(() => {
        const s = this.scan();
        if (!s?.running || !s.total) return 0;
        return Math.min(100, Math.round(((s.processed ?? 0) / s.total) * 100));
    });

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });
    }

    ngOnDestroy() {
        this.stopPolling();
        if (this.searchTimer) clearTimeout(this.searchTimer);
    }

    async load() {
        const id = this.projectId();
        if (!id) return;
        this.loading.set(true);
        this.loadError.set(null);
        try {
            const [project, page, scan] = await Promise.all([
                this.projects.get(id),
                this.api.list(id, this.query()),
                this.api.scanState(id),
            ]);
            this.project.set(project);
            this.page.set(page);
            this.scan.set(scan);
            // A scan started before this page was opened (or before a reload) keeps reporting.
            if (scan.running) this.startPolling();
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.assets.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    private query() {
        return {
            kind: this.kind(),
            search: this.search().trim() || undefined,
            missing: this.missingOnly(),
            skip: this.skip(),
            take: PAGE_SIZE,
        };
    }

    private async reloadList() {
        const id = this.projectId();
        if (!id) return;
        try {
            this.page.set(await this.api.list(id, this.query()));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.assets.loadFailed));
        }
    }

    setKind(kind: AssetKind | null) {
        this.kind.set(kind);
        this.missingOnly.set(false);
        this.skip.set(0);
        void this.reloadList();
    }

    showMissing() {
        this.kind.set(null);
        this.missingOnly.set(true);
        this.skip.set(0);
        void this.reloadList();
    }

    onSearch(value: string) {
        this.search.set(value);
        this.skip.set(0);
        // Debounced: the index can hold tens of thousands of rows, and a request per keystroke
        // would queue faster than it answers.
        if (this.searchTimer) clearTimeout(this.searchTimer);
        this.searchTimer = setTimeout(() => void this.reloadList(), 250);
    }

    kindCount(kind: AssetKind) {
        return this.page()?.byKind?.[kind] ?? 0;
    }

    setView(view: 'grid' | 'list') {
        this.view.set(view);
        try { localStorage.setItem('cedar.assetView', view); } catch { /* private mode */ }
    }

    // ---- folder choosing -------------------------------------------------

    startPicking() {
        this.pickPath.set(this.page()?.rootPath ?? '');
        this.pickError.set(null);
        this.picking.set(true);
    }

    /** Only in the desktop shell — a browser cannot offer a folder, which is why T-121 came first. */
    async browse() {
        const chosen = await this.desktop?.pickFolder();
        if (chosen) this.pickPath.set(chosen);
    }

    async indexFolder(path?: string) {
        const id = this.projectId();
        const target = (path ?? this.pickPath()).trim();
        if (!id || target.length === 0 || this.busy()) return;

        this.busy.set(true);
        this.pickError.set(null);
        try {
            const state = await this.api.startScan(id, target);
            this.scan.set(state);
            this.picking.set(false);
            this.rememberFolder(target);
            this.startPolling();
        } catch (e) {
            this.pickError.set(httpErrorMessage(e, this.t().projects.assets.loadFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async rescan() {
        const id = this.projectId();
        if (!id || this.busy()) return;
        this.busy.set(true);
        try {
            this.scan.set(await this.api.startScan(id));
            this.startPolling();
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.assets.loadFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async cancelScan() {
        const id = this.projectId();
        if (!id) return;
        try { await this.api.cancelScan(id); } catch { /* it may have finished first */ }
    }

    private startPolling() {
        this.stopPolling();
        this.pollTimer = setInterval(async () => {
            const id = this.projectId();
            if (!id) return;
            try {
                const state = await this.api.scanState(id);
                this.scan.set(state);
                if (!state.running) {
                    this.stopPolling();
                    // The list is only worth re-fetching once, at the end: refreshing it every tick
                    // would make a scan of a large folder fight with itself for the connection.
                    await this.reloadList();
                    this.project.set(await this.projects.get(id));
                }
            } catch {
                this.stopPolling();
            }
        }, SCAN_POLL_MS);
    }

    private stopPolling() {
        if (this.pollTimer) clearInterval(this.pollTimer);
        this.pollTimer = null;
    }

    // ---- one asset -------------------------------------------------------

    async open(asset: AssetEntry) {
        const id = this.projectId();
        if (!id) return;
        try {
            this.selected.set(await this.api.get(id, asset.id));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.assets.loadFailed));
        }
    }

    async revealSelected() {
        const path = this.selected()?.fullPath;
        if (path) await this.desktop?.reveal(path);
    }

    async reindexSelected() {
        const id = this.projectId();
        const asset = this.selected();
        if (!id || !asset || this.busy()) return;
        this.busy.set(true);
        try {
            const updated = await this.api.reindexOne(id, asset.id);
            this.selected.set({ ...asset, ...updated } as AssetDetail);
            await this.reloadList();
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.assets.loadFailed));
        } finally {
            this.busy.set(false);
        }
    }

    // ---- paging ----------------------------------------------------------

    get canPageBack() { return this.skip() > 0; }
    get canPageForward() { return this.skip() + PAGE_SIZE < (this.page()?.total ?? 0); }

    pageBack() {
        this.skip.set(Math.max(0, this.skip() - PAGE_SIZE));
        void this.reloadList();
    }

    pageForward() {
        this.skip.set(this.skip() + PAGE_SIZE);
        void this.reloadList();
    }

    rangeLabel() {
        const page = this.page();
        if (!page || page.total === 0) return '';
        const from = this.skip() + 1;
        const to = Math.min(this.skip() + page.items.length, page.total);
        return `${from}–${to} / ${page.total}`;
    }

    // ---- local preferences ----------------------------------------------

    private loadView(): 'grid' | 'list' {
        try { return localStorage.getItem('cedar.assetView') === 'list' ? 'list' : 'grid'; }
        catch { return 'grid'; }
    }

    private loadRecent(): string[] {
        try { return JSON.parse(localStorage.getItem(RECENT_FOLDERS_KEY) ?? '[]'); }
        catch { return []; }
    }

    private rememberFolder(path: string) {
        const next = [path, ...this.recentFolders().filter(p => p !== path)].slice(0, 5);
        this.recentFolders.set(next);
        try { localStorage.setItem(RECENT_FOLDERS_KEY, JSON.stringify(next)); } catch { /* private mode */ }
    }
}
