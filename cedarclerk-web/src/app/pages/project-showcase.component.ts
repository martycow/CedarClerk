import { Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { AuthService } from '../core/auth.service';
import { ProjectDetail, ProjectsService, ShowcaseStats } from '../core/projects.service';
import { RulerService } from '../core/ruler.service';
import { IconComponent } from '../shared/icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';

// The public game page, edited on a screen of its own (T-159/ADR-134, grown into a site by
// ADR-216). It used to be a block at the bottom of the project-settings modal, which is where a
// reader looking for it never went: everything the page shows — store links, a trailer, a gallery,
// a domain — arrived there one field at a time until the modal held a site behind a scroll bar.
//
// One home, not two: the modal no longer carries any of this, and the hub's Links group points
// here. What stays on the hub is the address and the counters, which are readings, not settings.
@Component({
    selector: 'app-project-showcase',
    imports: [
        FormsModule, IconComponent, WorktopComponent, ShelfPanelComponent, SpecRowComponent,
        ButtonComponent, InputComponent,
    ],
    templateUrl: 'project-showcase.component.html',
    styleUrls: ['project-showcase.component.css'],
})
export class ProjectShowcaseComponent implements OnDestroy {
    private api = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    private ruler = inject(RulerService);
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
    gallery = signal('');
    domain = signal('');

    /** Live only once the server has a slug for it — an unsaved slug addresses nothing. */
    publicUrl = computed(() => {
        const slug = this.project()?.showcaseSlug;
        const base = this.auth.blogUrl();
        return slug && base ? `${base}/games/${slug}` : null;
    });

    published = computed(() => !!this.project()?.showcaseSlug);

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });

        effect(() => {
            const project = this.project();
            if (!project) return;
            const t = this.t().projects.showcase;
            this.ruler.publish({
                label: project.name,
                left: [{ text: this.published() ? t.rulerLive : t.rulerOff }],
            });
        });
    }

    ngOnDestroy(): void {
        this.ruler.clear();
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
        this.domain.set(project.customDomain ?? '');
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
            });
            const next: ProjectDetail = {
                ...project,
                showcaseSlug: result.showcaseSlug,
                showcaseLinks: this.links().trim(),
                showcaseGallery: this.gallery().trim(),
                showcaseTrailerUrl: this.trailer().trim() || null,
                customDomain: result.customDomain,
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
