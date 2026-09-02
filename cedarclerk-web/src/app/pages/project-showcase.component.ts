import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { AuthService } from '../core/auth.service';
import {
    DISCOVERY_CATEGORIES, DiscoveryCategory, ProjectDetail, ProjectsService, ShowcaseStats,
} from '../core/projects.service';
import { IconComponent } from '../shared/icon.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { LibraryAsset } from '../core/assets.service';
import { MediaPickerComponent } from '../shared/media-picker.component';
import { PlanLockComponent } from '../shared/plan-lock.component';
import { IconName } from '../shared/icon-data.generated';
import {
    newShowcaseBlock, parseShowcaseLayout, serializeShowcaseLayout, ShowcaseBlock,
    ShowcaseBlockKind, SHOWCASE_BLOCK_KINDS,
} from '../core/showcase-layout';

// The public game page, edited on a screen of its own (T-159/ADR-134, grown into a site by
// ADR-216). It used to be a block at the bottom of the project-settings modal, which is where a
// reader looking for it never went: everything the page shows — store links, a trailer, a gallery,
// a domain — arrived there one field at a time until the modal held a site behind a scroll bar.
//
// One home, not two: the modal no longer carries any of this, and the hub's "Where it goes" card
// points here. What stays on the hub is the address and the counters, which are readings, not
// settings.
@Component({
    selector: 'app-project-showcase',
    imports: [
        FormsModule, IconComponent, PageHeaderComponent, ButtonComponent, InputComponent, MediaPickerComponent,
        PlanLockComponent,
    ],
    templateUrl: 'project-showcase.component.html',
    styleUrls: ['project-showcase.component.css'],
})
export class ProjectShowcaseComponent {
    private api = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    auth = inject(AuthService);
    t = inject(LocaleService).t;

    projectId = signal('');
    project = signal<ProjectDetail | null>(null);
    stats = signal<ShowcaseStats | null>(null);
    loading = signal(true);
    loadError = signal<string | null>(null);
    busy = signal(false);
    actionError = signal<string | null>(null);
    saved = signal(false);
    blocks = signal<ShowcaseBlock[]>([]);
    selectedBlockId = signal('hero');
    addBlockKind = signal<ShowcaseBlockKind>('about');
    aiBusy = signal(false);
    aiSuggestion = signal<string | null>(null);
    aiError = signal<string | null>(null);

    enabled = signal(false);
    slug = signal('');
    links = signal('');
    trailer = signal('');
    discoveryCategory = signal<DiscoveryCategory>('other');
    readonly discoveryCategories = DISCOVERY_CATEGORIES;
    gallery = signal('');
    galleryPickerOpen = signal(false);
    domain = signal('');
    // Wave 1 item 6 — the /press page's optional facts; empty means the section is omitted there.
    pressContact = signal('');
    pressPrice = signal('');
    pressEngine = signal('');
    pressGenre = signal('');
    pressFactsheet = signal('');

    selectedBlock = computed(() => this.blocks().find(block => block.id === this.selectedBlockId()) ?? null);
    visibleBlocks = computed(() => this.blocks().filter(block => block.visible));
    availableBlockKinds = computed(() => SHOWCASE_BLOCK_KINDS.filter(
        kind => !this.blocks().some(block => block.kind === kind)));
    galleryImages = computed(() => this.gallery().split('\n').map(value => value.trim()).filter(Boolean));
    previewLinks = computed(() => this.links().split('\n').map(value => {
        const [label, url] = value.split('|', 2).map(part => part?.trim());
        return label && url ? { label, url } : null;
    }).filter((value): value is { label: string; url: string } => !!value));

    private readonly blockIcons: Record<ShowcaseBlockKind, IconName> = {
        hero: 'layout', about: 'text-align-left', links: 'link', trailer: 'film-slate', gallery: 'images',
        downloads: 'download-simple', devlog: 'newspaper', follow: 'heart', roadmap: 'list-checks',
    };

    blockIcon(kind: ShowcaseBlockKind): IconName { return this.blockIcons[kind]; }

    blockLabel(kind: ShowcaseBlockKind): string {
        return this.t().projects.showcase.blocks[kind];
    }

    // T-353 — the gallery is a list of URLs, one per line, and it stays that way: an author can
    // still paste a link to something hosted elsewhere. What the picker adds is the case that had
    // no answer before — a picture already in the library, or one being uploaded now — appended
    // rather than replacing what is typed.
    appendGalleryImage(asset: LibraryAsset) {
        const url = `/media/${asset.localPath}`;
        const lines = this.gallery().split('\n').map(l => l.trim()).filter(Boolean);
        if (!lines.includes(url)) lines.push(url);
        this.gallery.set(lines.join('\n'));
        this.galleryPickerOpen.set(false);
    }

    /** The public press-kit page, live wherever the showcase itself is. */
    pressUrl = computed(() => {
        const url = this.publicUrl();
        return url ? `${url}/press` : null;
    });

    /** Live only once the server has a slug for it — an unsaved slug addresses nothing. */
    publicUrl = computed(() => {
        const slug = this.project()?.showcaseSlug;
        const base = this.auth.blogUrl();
        return slug && base ? `${base}/showcase/${slug}` : null;
    });

    published = computed(() => !!this.project()?.showcaseSlug);

    /** The former rule readout: whether the page is live, as a tag. */
    headerMeta = computed<HeaderMeta[]>(() => {
        if (!this.project()) return [];
        const t = this.t().projects.showcase;
        const live = this.published();
        return [
            { text: live ? t.live : t.off, tag: true, tone: live ? 'ok' : 'muted', title: live ? t.rulerLive : t.rulerOff },
        ];
    });

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });
    }

    async load() {
        const id = this.projectId();
        if (!id) return;
        this.loading.set(true);
        this.loadError.set(null);
        try {
            const project = await this.api.get(id);
            this.project.set(project);
            this.fillFrom(project);
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.showcase.loadFailed));
        } finally {
            this.loading.set(false);
        }
        // Beside the page rather than before it: counters are one group of rows, and a page with
        // none must still be editable.
        this.stats.set(null);
        if (this.published()) await this.loadStats(id);
    }

    private async loadStats(id: string) {
        try {
            this.stats.set(await this.api.showcaseStats(id));
        } catch {
            this.stats.set(null);
        }
    }

    private fillFrom(project: ProjectDetail) {
        this.enabled.set(!!project.showcaseSlug);
        this.slug.set(project.showcaseSlug ?? '');
        this.links.set(project.showcaseLinks ?? '');
        this.gallery.set(project.showcaseGallery ?? '');
        this.trailer.set(project.showcaseTrailerUrl ?? '');
        this.discoveryCategory.set(project.discoveryCategory ?? 'other');
        this.domain.set(project.customDomain ?? '');
        this.pressContact.set(project.pressContactEmail ?? '');
        this.pressPrice.set(project.pressPrice ?? '');
        this.pressEngine.set(project.pressEngine ?? '');
        this.pressGenre.set(project.pressGenre ?? '');
        this.pressFactsheet.set(project.pressFactsheetRows ?? '');
        const layout = parseShowcaseLayout(project.showcaseBlocksJson);
        this.blocks.set(layout.blocks);
        this.selectedBlockId.set(layout.blocks[0]?.id ?? 'hero');
        this.aiSuggestion.set(null);
        this.aiError.set(null);
    }

    selectBlock(id: string) {
        this.selectedBlockId.set(id);
        this.aiSuggestion.set(null);
        this.aiError.set(null);
    }

    updateSelected(patch: Partial<Pick<ShowcaseBlock, 'visible' | 'title' | 'body'>>) {
        const id = this.selectedBlockId();
        this.blocks.update(blocks => blocks.map(block => block.id === id
            ? { ...block, ...patch, visible: block.kind === 'hero' ? true : (patch.visible ?? block.visible) }
            : block));
        this.saved.set(false);
    }

    moveSelected(delta: number) {
        const blocks = [...this.blocks()];
        const from = blocks.findIndex(block => block.id === this.selectedBlockId());
        const to = from + delta;
        if (from < 0 || to < 0 || to >= blocks.length) return;
        [blocks[from], blocks[to]] = [blocks[to], blocks[from]];
        this.blocks.set(blocks);
        this.saved.set(false);
    }

    removeSelected() {
        const selected = this.selectedBlock();
        if (!selected || selected.kind === 'hero') return;
        this.blocks.update(blocks => blocks.filter(block => block.id !== selected.id));
        this.selectedBlockId.set(this.blocks()[0]?.id ?? 'hero');
        this.saved.set(false);
    }

    addBlock() {
        const kind = this.addBlockKind();
        if (this.blocks().some(block => block.kind === kind)) return;
        const block = newShowcaseBlock(kind);
        this.blocks.update(blocks => [...blocks, block]);
        this.selectBlock(block.id);
        this.saved.set(false);
        this.addBlockKind.set(this.availableBlockKinds()[0] ?? 'about');
    }

    blockTitle(block: ShowcaseBlock): string {
        return block.title || this.blockLabel(block.kind);
    }

    blockBody(block: ShowcaseBlock): string {
        if (block.body) return block.body;
        return block.kind === 'hero' ? this.project()?.description ?? '' : '';
    }

    async assist(kind: 'polish' | 'shorten' | 'ideas') {
        const block = this.selectedBlock();
        const text = block ? this.blockBody(block).trim() : '';
        if (!block || !text || this.aiBusy() || !this.auth.hasAiPlan()) return;

        this.aiBusy.set(true);
        this.aiSuggestion.set(null);
        this.aiError.set(null);
        try {
            const { jobId } = await this.api.startShowcaseAssist(this.projectId(), kind, text);
            const deadline = Date.now() + 600_000;
            while (Date.now() < deadline) {
                await new Promise(resolve => setTimeout(resolve, 1500));
                const job = await this.api.getShowcaseAssist(jobId);
                if (job.status === 'completed' && job.result) {
                    this.aiSuggestion.set(job.result.suggestion);
                    return;
                }
                if (job.status === 'failed') throw new Error(job.error || this.t().projects.showcase.aiFailed);
            }
            throw new Error(this.t().projects.showcase.aiTimedOut);
        } catch (e) {
            this.aiError.set(e instanceof Error ? e.message : httpErrorMessage(e, this.t().projects.showcase.aiFailed));
        } finally {
            this.aiBusy.set(false);
        }
    }

    applySuggestion() {
        const suggestion = this.aiSuggestion();
        if (!suggestion) return;
        this.updateSelected({ body: suggestion });
        this.aiSuggestion.set(null);
    }

    async save() {
        const project = this.project();
        if (!project || this.busy()) return;

        this.busy.set(true);
        this.actionError.set(null);
        this.saved.set(false);
        try {
            // The server slugifies and may hand back a different slug than the one typed, so the
            // answer is what the form ends up holding.
            const result = await this.api.setShowcase(project.id, {
                enabled: this.enabled(),
                slug: this.slug().trim() || null,
                links: this.links().trim(),
                gallery: this.gallery().trim(),
                trailerUrl: this.trailer().trim() || null,
                customDomain: this.domain().trim() || null,
                pressContactEmail: this.pressContact().trim() || null,
                pressPrice: this.pressPrice().trim() || null,
                pressEngine: this.pressEngine().trim() || null,
                pressGenre: this.pressGenre().trim() || null,
                pressFactsheetRows: this.pressFactsheet().trim() || null,
                discoveryCategory: this.discoveryCategory(),
                blocksJson: serializeShowcaseLayout(this.blocks()),
            });
            const next: ProjectDetail = {
                ...project,
                showcaseSlug: result.showcaseSlug,
                showcaseLinks: this.links().trim(),
                showcaseGallery: this.gallery().trim(),
                showcaseTrailerUrl: this.trailer().trim() || null,
                showcaseBlocksJson: result.blocksJson,
                customDomain: result.customDomain,
                pressContactEmail: this.pressContact().trim() || null,
                pressPrice: this.pressPrice().trim() || null,
                pressEngine: this.pressEngine().trim() || null,
                pressGenre: this.pressGenre().trim() || null,
                pressFactsheetRows: this.pressFactsheet().trim() || null,
                discoveryCategory: result.discoveryCategory,
            };
            this.project.set(next);
            this.fillFrom(next);
            this.saved.set(true);
            if (result.showcaseSlug) void this.loadStats(project.id);
            else this.stats.set(null);
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }
}
