import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
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
    LinkedDocument,
    desktopBridge,
    formatBytes,
    formatDuration,
} from '../core/asset-index.service';
import { AssetSyncService } from '../core/asset-sync.service';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';

const PAGE_SIZE = 60;
const RECENT_FOLDERS_KEY = 'cedar.assetFolders';

// T-122 (ADR-107) — the asset screen, from docs/design_handoff_indiedev_core_loop §9-10.
//
// The one thing every state on this screen has to keep saying: **the asset's bytes were never
// uploaded**. It is an index of paths on one machine, so a file that is not there right now reads
// "not found at path", never "deleted", and a kind with no preview says "no preview · model" rather
// than showing an empty rectangle that looks like a broken image.
//
// **Since ADR-117 there is a second thing it must keep saying: whose machine.** The index lives in the
// cloud and opens anywhere, so most of the time this screen is looking at *fingerprints* — a preview
// and some metadata standing in for a file that is somewhere else. `isLocal` decides which of the two
// it is, and it decides it by comparing machines, never by assuming.
@Component({
    selector: 'app-project-assets',
    imports: [IconComponent, ZonedDatePipe, FormsModule, ModalComponent],
    templateUrl: 'project-assets.component.html',
    styleUrls: ['project-assets.component.css'],
})
export class ProjectAssetsComponent implements OnDestroy {
    private api = inject(AssetIndexService);
    private projects = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    auth = inject(AuthService);
    sync = inject(AssetSyncService);
    t = inject(LocaleService).t;

    readonly kinds = ASSET_KINDS;
    readonly kindIcons = ASSET_KIND_ICONS;
    readonly bytes = formatBytes;
    readonly duration = formatDuration;
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

    /** This machine, when there is one. Null in a browser, which is what makes everything a fingerprint. */
    thisMachine = signal<{ id: string; name: string } | null>(null);

    pickError = signal<string | null>(null);
    recentFolders = signal<string[]>(this.loadRecent());

    selected = signal<AssetDetail | null>(null);
    links = signal<LinkedDocument[]>([]);
    linking = signal(false);
    busy = signal(false);

    // Thumbnails that failed to load. A tile falls back to the honest "no preview" label rather
    // than leaving the browser's broken-image icon, which on this screen would read as "the file
    // is damaged" when it usually means "the server could not decode this format".
    private thumbFailed = signal<ReadonlySet<string>>(new Set());

    private searchTimer: ReturnType<typeof setTimeout> | null = null;

    /** No root chosen yet — the screen is the pick-a-folder card and nothing else. */
    needsFolder = computed(() => !this.loading() && !this.page()?.rootPath);

    /** Whether this client can index at all. A browser cannot: it has no bridge to a filesystem. */
    canIndex = computed(() => this.desktop !== undefined);

    scanning = this.sync.running;
    scanPercent = this.sync.indexPercent;
    progress = this.sync.progress;

    /**
     * Whether the files are on *this* machine.
     *
     * The whole fingerprint distinction rests on this one line, and it defaults to false on purpose:
     * an unknown machine, a project indexed before ADR-117 or any browser all answer "not here", which
     * is the honest reading. Claiming a file is reachable and being wrong costs an author a dead
     * button and a moment of doubt about their own disk; the reverse costs nothing.
     */
    isLocal = computed(() => {
        const source = this.page()?.sourceMachine ?? null;
        const mine = this.thisMachine();
        return source !== null && mine !== null && source.id === mine.id;
    });

    /** "MARTY-PC", or a plain "another machine" when the name was never recorded. */
    sourceMachineName = computed(() =>
        this.page()?.sourceMachine?.name ?? this.t().projects.assets.unknownMachine);

    constructor() {
        void this.loadMachine();
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });
    }

    ngOnDestroy() {
        if (this.searchTimer) clearTimeout(this.searchTimer);
    }

    private async loadMachine() {
        try {
            const machine = await this.desktop?.machine();
            this.thisMachine.set(machine ?? null);
        } catch {
            // No bridge, or a shell that refused it. Either way this is not the machine holding
            // anything, and the screen says so rather than guessing.
            this.thisMachine.set(null);
        }
    }

    async load() {
        const id = this.projectId();
        if (!id) return;
        this.loading.set(true);
        this.loadError.set(null);
        try {
            const [project, page] = await Promise.all([
                this.projects.get(id),
                this.api.list(id, this.query()),
            ]);
            this.project.set(project);
            this.page.set(page);
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

    /**
     * The OS folder picker, which also grants the folder to the local agent.
     *
     * **Only route to a folder there is** — the typed-path field is gone with ADR-117. A path the page
     * typed would have to be granted by the page too, and then the grant would guard nothing: the
     * point of it is that disk access begins with a gesture the human made.
     */
    async browse() {
        try {
            const chosen = await this.desktop?.pickFolder();
            if (chosen) await this.indexFolder(chosen);
        } catch (e) {
            // The shell answers in English by decision (ADR-116), so its text is logged rather than
            // shown — a page cannot translate a sentence the shell invented.
            console.warn('[cedar] folder picker:', e);
            this.pickError.set(this.t().projects.assets.agentUnavailable);
        }
    }

    async indexFolder(path: string) {
        const id = this.projectId();
        const target = path.trim();
        if (!id || target.length === 0 || this.scanning()) return;

        this.pickError.set(null);
        this.rememberFolder(target);
        await this.sync.run(id, target);
        // Once, at the end. Refreshing the list on every batch would make a scan of a large folder
        // compete with itself for the connection.
        await this.reloadList();
        this.project.set(await this.projects.get(id));
    }

    /**
     * Re-scans the folder already chosen.
     *
     * Only offered on the machine that holds it: from anywhere else the path names a folder this
     * process cannot see, and the agent would refuse it — correctly, but confusingly. The shell
     * re-grants folders it remembers at launch, so this needs no second trip through the picker.
     */
    async rescan() {
        const root = this.page()?.rootPath;
        if (!root || !this.isLocal() || this.scanning()) return;
        await this.indexFolder(root);
    }

    cancelScan() {
        this.sync.cancel();
    }

    // ---- one asset -------------------------------------------------------

    thumbUrl(asset: AssetEntry) {
        return this.api.thumbnailUrl(this.projectId(), asset.id);
    }

    showsThumbnail(asset: AssetEntry) {
        return asset.hasThumbnail && !this.thumbFailed().has(asset.id);
    }

    /**
     * A preview is coming but has not arrived — a different state from "this file can never have one",
     * and worth its own label. Without the distinction, a freshly indexed folder viewed from a browser
     * looks identical to a folder full of formats nothing can decode.
     */
    awaitingThumbnail(asset: AssetEntry) {
        return !asset.hasThumbnail && asset.canHaveThumbnail && !asset.missingSince;
    }

    onThumbError(asset: AssetEntry) {
        this.thumbFailed.update(set => new Set(set).add(asset.id));
    }

    /** The project's documents minus the ones already linked — what the picker may offer. */
    linkableDocuments() {
        const linked = new Set(this.links().map(l => l.id));
        return (this.project()?.documents ?? []).filter(d => !linked.has(d.id));
    }

    async open(asset: AssetEntry) {
        const id = this.projectId();
        if (!id) return;
        try {
            this.selected.set(await this.api.get(id, asset.id));
            this.linking.set(false);
            this.links.set(await this.api.links(id, asset.id));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.assets.loadFailed));
        }
    }

    async addLink(draftId: string) {
        const id = this.projectId();
        const asset = this.selected();
        if (!id || !asset || this.busy()) return;
        this.busy.set(true);
        try {
            await this.api.addLink(id, asset.id, draftId);
            this.links.set(await this.api.links(id, asset.id));
            this.linking.set(false);
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async removeLink(draftId: string) {
        const id = this.projectId();
        const asset = this.selected();
        if (!id || !asset || this.busy()) return;
        this.busy.set(true);
        try {
            await this.api.removeLink(id, asset.id, draftId);
            this.links.set(await this.api.links(id, asset.id));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    /** "2048×1024", "2:14 · 48 kHz" — whatever the header actually said, and nothing else. */
    detailsOf(asset: AssetEntry) {
        const parts = [this.bytes(asset.sizeBytes)];
        if (asset.width && asset.height) parts.unshift(`${asset.width}×${asset.height}`);
        if (asset.durationMs) {
            parts.unshift(this.duration(asset.durationMs));
            if (asset.sampleRate) parts.splice(1, 0, `${Math.round(asset.sampleRate / 1000)} kHz`);
        }
        return parts.join(' · ');
    }

    /** Shown only when the file really is here — see `isLocal`. */
    async revealSelected() {
        const path = this.selected()?.fullPath;
        if (path && this.isLocal()) await this.desktop?.reveal(path);
    }

    /**
     * Re-stats one file: the agent looks, this sends the answer up.
     *
     * Two round trips where there used to be one, and the reason is the same one behind the whole
     * change — the process that can see the file and the process that stores what it saw are no longer
     * the same process.
     */
    async reindexSelected() {
        const id = this.projectId();
        const asset = this.selected();
        const root = this.page()?.rootPath;
        if (!id || !asset || !root || !this.isLocal() || this.busy()) return;
        this.busy.set(true);
        try {
            const answer = await this.desktop?.stat(root, asset.relativePath);
            // A file the agent cannot find needs no push: the row is already marked, or it will be by
            // the next sweep. Pushing a "still here" record for a file that is gone is the one wrong
            // thing this button could do.
            if (answer?.file) await this.api.pushOne(id, answer.file);
            this.selected.set(await this.api.get(id, asset.id));
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
