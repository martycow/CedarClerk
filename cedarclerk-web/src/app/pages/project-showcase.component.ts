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
    ],
    templateUrl: 'project-showcase.component.html',
    styleUrls: ['project-showcase.component.css'],
})
export class ProjectShowcaseComponent {
    private api = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    private auth = inject(AuthService);
    t = inject(LocaleService).t;

    projectId = signal('');
    project = signal<ProjectDetail | null>(null);
    stats = signal<ShowcaseStats | null>(null);
    loading = signal(true);
    loadError = signal<string | null>(null);
    busy = signal(false);
    actionError = signal<string | null>(null);
    saved = signal(false);

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
        return slug && base ? `${base}/games/${slug}` : null;
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
            });
            const next: ProjectDetail = {
                ...project,
                showcaseSlug: result.showcaseSlug,
                showcaseLinks: this.links().trim(),
                showcaseGallery: this.gallery().trim(),
                showcaseTrailerUrl: this.trailer().trim() || null,
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
