import { ConfirmationService } from '../core/confirmation.service';
import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { formatInZone } from '../core/display-time';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { AuthService } from '../core/auth.service';
import { DraftsService, DraftMeta, isPublishedAnywhere, RegistrationForm, parseRegistrationForm } from '../core/drafts.service';
import { FormPresetsService, FormPreset } from '../core/form-presets.service';
import { PostsService, ScheduledPost } from '../core/posts.service';
import { PublishEvent, PublishedPost, PublishService } from '../core/publish.service';
import { LinksService, TrackedLink } from '../core/links.service';
import { DEFAULT_PRIMARY_LANGUAGE } from '../core/languages';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { ModalComponent } from '../shared/modal.component';
import { CommentsComponent } from './comments.component';
import { TagPickerComponent } from '../shared/tag-picker.component';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { FormRefComponent } from '../shared/form-ref.component';
import { TagUsageService } from '../core/tag-usage.service';
import { FoldersService } from '../core/folders.service';
import { ProjectsService, ProjectSummary, DOCUMENT_TYPE_ICONS, isPublishableType } from '../core/projects.service';
import { IconComponent } from '../shared/icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';
import { GrowthChartComponent, GrowthSeries, SeriesSlot } from '../bench/worktop/growth-chart.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { BrandIconComponent, BrandIconName } from '../shared/brand-icon.component';
import { PopoverComponent } from '../shared/popover.component';
import { SortDirection } from '../core/collection-query';
import { WorkspaceContextService } from '../core/workspace-context.service';

// The first DraftStatSnapshot night (8.6): a calendar day, compared against the ISO date prefix.
const STAT_HISTORY_START = '2026-08-01';
export type PostStateFilter = 'all' | 'live' | 'scheduled' | 'draft';
const STATE_FILTERS: readonly PostStateFilter[] = ['all', 'live', 'scheduled', 'draft'];
export type PostDetailTab = 'overview' | 'publishing' | 'engagement' | 'details';
const DETAIL_TABS: readonly PostDetailTab[] = ['overview', 'publishing', 'engagement', 'details'];
type PostSort = 'published' | 'updated' | 'title' | 'activity';

/** One row of the Publishing destinations card. */
export interface PostDestination {
    key: string;
    name: string;
    brand: BrandIconName | null;
    reached: boolean;
    state: string;
    url: string | null;
    linkLabel: string;
}

export interface PostActivity {
    label: string;
    at: string | null;
    url: string | null;
}

/** The destinations every post is measured against; another network joins the list once reached. */
const CORE_NETWORKS: readonly string[] = ['x', 'bluesky'];
const NETWORK_BRANDS: Record<string, BrandIconName> = { telegram: 'telegram', x: 'twitter', bluesky: 'bluesky' };

// ADR-317 — the Publishing Manager: the post list on the left, the selected post on the right
// under Overview · Publishing · Engagement · Details.
@Component({
    selector: 'app-posts-manager',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, ModalComponent, CommentsComponent,
        TagPickerComponent, FolderPickerComponent, FormRefComponent, ButtonComponent,
        LeafTagComponent, PageHeaderComponent, EmptyStateComponent, IndexTabsComponent,
        GrowthChartComponent, InputComponent, BrandIconComponent, PopoverComponent,
    ],
    templateUrl: 'posts-manager.component.html',
    styleUrls: ['publishing-workspace.css', 'posts-manager.component.css'],
})
export class PostsManagerComponent implements OnInit, OnDestroy {
    private readonly confirmation = inject(ConfirmationService);
    private readonly workspace = inject(WorkspaceContextService);
    auth = inject(AuthService);
    private draftsApi = inject(DraftsService);
    private presetsApi = inject(FormPresetsService);
    private postsApi = inject(PostsService);
    private publishApi = inject(PublishService);
    private linksApi = inject(LinksService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    feedback = inject(CommentsService);
    private tagUsageApi = inject(TagUsageService);
    private foldersApi = inject(FoldersService);
    private projectsApi = inject(ProjectsService);
    private locale = inject(LocaleService);
    readonly docIcons = DOCUMENT_TYPE_ICONS;
    t = this.locale.t;
    /** ADR-322: the open project's posts only; absent with the projects module off. */
    readonly project = this.route.snapshot.queryParamMap.get('project');

    loading = signal(true);
    error = signal('');
    busy = signal(false);

    drafts = signal<DraftMeta[]>([]);
    selectedId = signal<string | null>(null);
    detailTab = signal<PostDetailTab>('overview');
    private failedCovers = signal<ReadonlySet<string>>(new Set());

    // Empty while the projects module is off, which is what keeps the control off the page.
    projects = signal<ProjectSummary[]>([]);

    // Everything here changes metadata only. Body text stays the editor's job; the rename still
    // has to round-trip cedarJson because the save endpoint takes title and body together.
    editTitle = '';
    // The headline readers see, kept apart from the draft's own name. Empty means "same as the name".
    editArticleTitle = '';
    editTags = signal<string[]>([]);
    editSlug = '';
    search = signal('');
    stateFilter = signal<PostStateFilter>('all');
    postSort = signal<PostSort>('published');
    postSortDirection = signal<SortDirection>('desc');
    // Per-draft count of comments arrived since the last look, for the list's "+N" chip.
    newByDraft = signal<Record<string, number>>({});
    renaming = signal(false);
    deleteConfirmId = signal<string | null>(null);

    unpublishing = signal(false);
    scheduled = signal<ScheduledPost[]>([]);

    // The form attached to the selected post. A post gets one by picking a preset, which is copied.
    regForm = signal<RegistrationForm | null>(null);
    postFormLanguages = signal<string[]>([]);
    regBusy = signal(false);
    presets = signal<FormPreset[]>([]);
    presetsLoading = signal(false);
    presetLoadError = signal('');
    readonly primaryLanguage = DEFAULT_PRIMARY_LANGUAGE;

    /** Where each post went on the short-post networks — the publish queue's own record. */
    published = signal<PublishedPost[]>([]);
    /** Every publication; the Telegram rows are the channel's send records (ADR-317 §4). */
    events = signal<PublishEvent[]>([]);

    async ngOnInit() {
        this.restoreCollectionQuery();
        this.feedback.refreshNewCount();
        void this.loadPresets();
        this.loadProjects();
        try {
            this.drafts.set(await this.draftsApi.list());
            this.loadPublished();
            this.loadEvents();
            this.loadScheduled();
            this.loadNewFeedback();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.load));
        } finally {
            this.loading.set(false);
        }
        // The calendar's published tickets deep-link here with the post to select.
        const asked = this.route.snapshot.queryParamMap.get('draft');
        if (asked) {
            const draft = this.postPool().find(d => d.id === asked);
            if (draft) this.select(draft);
        }
    }

    // Fire-and-forget: a missing "+N" chip is cosmetic and must not hold up the page.
    private async loadNewFeedback() {
        try {
            const feedback = await this.feedback.listAll();
            const counts: Record<string, number> = {};
            for (const c of feedback.comments) {
                if (c.isNew) counts[c.draftId] = (counts[c.draftId] ?? 0) + 1;
            }
            this.newByDraft.set(counts);
        } catch { /* ignore */ }
    }

    // ADR-317 — the selected post is drawn by this page alone, so the shell is told the surface
    // and nothing about the post.
    constructor() {
        effect(() => this.workspace.set({ surface: this.t().shell.context.posts }));
    }

    ngOnDestroy() {
        this.workspace.clear();
    }

    selected(): DraftMeta | null {
        const id = this.selectedId();
        return id ? this.postPool().find(d => d.id === id) ?? null : null;
    }

    selectFirstPost() {
        const first = this.visiblePosts()[0];
        if (first) void this.select(first);
    }

    readonly postPool = computed(() => this.drafts().filter(d =>
        !d.isTemplate && isPublishableType(d.documentType) && (!this.project || d.projectId === this.project)));

    /** The page header's "+ New post": the Documents screen's own new-document dialog. */
    readonly newPostParams = computed(() =>
        this.project ? { new: '1', project: this.project } : { new: '1' });

    readonly postSortOptions = computed(() => {
        const t = this.t().manager;
        return [
            { value: 'published:desc', label: t.sortPublishedDesc },
            { value: 'published:asc', label: t.sortPublishedAsc },
            { value: 'updated:desc', label: t.sortUpdatedDesc },
            { value: 'updated:asc', label: t.sortUpdatedAsc },
            { value: 'title:asc', label: t.sortTitleAsc },
            { value: 'title:desc', label: t.sortTitleDesc },
            { value: 'activity:desc', label: t.sortActivityDesc },
            { value: 'activity:asc', label: t.sortActivityAsc },
        ];
    });

    readonly stateChips = computed(() => {
        const t = this.t().manager;
        return [
            { value: 'all' as const, label: t.filterAll },
            { value: 'live' as const, label: t.chipPublished },
            { value: 'scheduled' as const, label: t.scheduled },
            { value: 'draft' as const, label: t.chipDrafts },
        ];
    });

    readonly detailTabs = computed<IndexTabItem[]>(() => {
        const t = this.t().manager;
        const selected = this.selectedId();
        return [
            { id: 'overview', label: t.detailTabs.overview },
            { id: 'publishing', label: t.detailTabs.publishing },
            {
                id: 'engagement', label: t.detailTabs.engagement,
                badge: selected ? this.newFeedbackFor(selected) : 0, badgeTitle: t.newSinceLastLook,
            },
            { id: 'details', label: t.detailTabs.details },
        ];
    });

    setDetailTab(id: string) {
        if (!DETAIL_TABS.includes(id as PostDetailTab)) return;
        // Hovering feedback rows is what clears the count, so leaving them is when to re-check it.
        if (this.detailTab() === 'engagement' && id !== 'engagement') this.feedback.refreshNewCount();
        this.detailTab.set(id as PostDetailTab);
    }

    postSortValue(): string {
        return `${this.postSort()}:${this.postSortDirection()}`;
    }

    coverFailed(draft: DraftMeta): boolean {
        return !!draft.coverImagePath && this.failedCovers().has(`${draft.id}:${draft.coverImagePath}`);
    }

    markCoverFailed(draft: DraftMeta): void {
        if (!draft.coverImagePath) return;
        this.failedCovers.update(current => new Set(current).add(`${draft.id}:${draft.coverImagePath}`));
    }

    postActivity(draft: DraftMeta): string {
        return this.t().manager.activityMetric(draft.viewCount + draft.reactionCount);
    }

    postActivityTitle(draft: DraftMeta): string {
        return this.t().manager.activityBreakdown(draft.viewCount, draft.reactionCount);
    }

    hasPostFilters(): boolean {
        return !!this.search().trim() || this.stateFilter() !== 'all';
    }

    onPostSearch(value: string) {
        this.search.set(value);
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    setStateFilter(value: PostStateFilter) {
        if (!STATE_FILTERS.includes(value)) return;
        this.stateFilter.set(value);
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    setPostSortValue(value: string) {
        const [key, direction] = value.split(':');
        if (!['published', 'updated', 'title', 'activity'].includes(key)
            || (direction !== 'asc' && direction !== 'desc')) return;
        this.postSort.set(key as PostSort);
        this.postSortDirection.set(direction);
        this.syncCollectionQuery();
    }

    clearPostFilters() {
        this.search.set('');
        this.stateFilter.set('all');
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    private ensureVisibleSelection() {
        const selected = this.selectedId();
        if (selected && !this.visiblePosts().some(d => d.id === selected)) this.selectedId.set(null);
    }

    visiblePosts(): DraftMeta[] {
        const q = this.search().trim().toLowerCase();
        const state = this.stateFilter();
        const matched = this.postPool().filter(d => {
            if (q && !d.title.toLowerCase().includes(q) && !d.tags.toLowerCase().includes(q)) return false;
            return state === 'all' || this.publishState(d) === state;
        });
        const key = this.postSort();
        const direction = this.postSortDirection() === 'asc' ? 1 : -1;
        return [...matched].sort((a, b) => {
            if (key === 'published' && !!a.blogPublishedAt !== !!b.blogPublishedAt)
                return a.blogPublishedAt ? -1 : 1;
            const compared = key === 'published'
                ? (a.blogPublishedAt ?? '').localeCompare(b.blogPublishedAt ?? '')
                : key === 'updated'
                    ? a.updatedAt.localeCompare(b.updatedAt)
                    : key === 'title'
                        ? a.title.localeCompare(b.title)
                        : (a.viewCount + a.reactionCount) - (b.viewCount + b.reactionCount);
            return direction * compared || a.id.localeCompare(b.id);
        });
    }

    private async loadScheduled() {
        try {
            this.scheduled.set(await this.postsApi.listScheduled(this.project));
        } catch { /* the list is context, not the point of the page */ }
    }

    // T-038 — a sent schedule is history, not a task: it stays out of the list unless asked for.
    // Failed ones are never hidden, since those are exactly the ones needing attention.
    showSentSchedules = signal(false);

    scheduledFor(draftId: string): ScheduledPost[] {
        const all = this.scheduled().filter(p => p.draftId === draftId);
        return this.showSentSchedules() ? all : all.filter(p => p.status !== 'Sent');
    }

    sentScheduleCount(draftId: string): number {
        return this.scheduled().filter(p => p.draftId === draftId && p.status === 'Sent').length;
    }

    /** ADR-099 — network names are display strings, not translated: "Bluesky" is "Bluesky". */
    private readonly networkLabels: Record<string, string> = { telegram: 'Telegram', bluesky: 'Bluesky', x: 'X', discord: 'Discord', linkedin: 'LinkedIn' };

    scheduledNetworkLabel(post: ScheduledPost): string {
        return this.networkLabels[post.network] ?? post.network;
    }

    // The three nightly series a post has, on their fixed palette slots. The leaf strip below the
    // heading is the legend and the filter at once (ADR-149): one line per metric, never a sum.
    readonly growthMetrics: { key: 'viewCount' | 'likeCount' | 'commentCount'; slot: SeriesSlot; label: () => string }[] = [
        { key: 'viewCount', slot: 1, label: () => this.t().manager.groups.views },
        { key: 'likeCount', slot: 2, label: () => this.t().manager.groups.likes },
        { key: 'commentCount', slot: 3, label: () => this.t().manager.groups.comments },
    ];
    growthShown = signal<ReadonlySet<string>>(new Set(['viewCount', 'likeCount', 'commentCount']));

    toggleGrowthMetric(key: string) {
        this.growthShown.update(set => {
            const next = new Set(set);
            next.has(key) ? next.delete(key) : next.add(key);
            return next;
        });
    }

    historyLoading = signal(false);
    private historyFor = signal<string | null>(null);
    private history = signal<{ viewCount: number; likeCount: number; commentCount: number; takenAt: string }[]>([]);

    async loadHistory(draftId: string) {
        if (this.historyFor() === draftId) return;
        this.historyFor.set(draftId);
        this.historyLoading.set(true);
        try {
            const res = await this.postsApi.statHistory(draftId);
            this.history.set(res.snapshots);
        } catch {
            this.history.set([]);
        } finally {
            this.historyLoading.set(false);
        }
    }

    /** Snapshots started on 01.08.2026; a post published before that has no earlier series to draw. */
    predatesHistory(publishedAt: string | null): boolean {
        return !!publishedAt && publishedAt.slice(0, 10) < STAT_HISTORY_START;
    }

    /** Null with fewer than two snapshots — one point is not a line, and a flat line would lie. */
    growthSeries(): GrowthSeries[] | null {
        const rows = this.history();
        if (rows.length < 2) return null;
        const drawn = this.growthMetrics
            .filter(m => this.growthShown().has(m.key))
            .map(m => ({ slot: m.slot, name: m.label(), points: rows.map(r => r[m.key]) }));
        return drawn.length ? drawn : null;
    }

    growthLabels(): string[] {
        return this.history().map(r => formatInZone(r.takenAt, 'MM/dd'));
    }

    overviewSeries(): GrowthSeries[] | null {
        const rows = this.history();
        return rows.length < 2
            ? null
            : [{ slot: 1, name: this.t().manager.groups.views, points: rows.map(r => r.viewCount) }];
    }

    // One publish state per post, resolved in a fixed order (Marty, 01.08.2026): an archived post
    // is archived whatever else is true of it, a published one is live, and everything else has
    // simply not gone out yet. Two chips saying different things about the same post is the
    // confusion this replaces.
    publishState(d: DraftMeta): 'archived' | 'live' | 'scheduled' | 'draft' {
        if (d.isArchived) return 'archived';
        if (isPublishedAnywhere(d)) return 'live';
        return this.hasPendingSchedule(d.id) ? 'scheduled' : 'draft';
    }

    publishStateLabel(d: DraftMeta): string {
        const state = this.publishState(d);
        if (state === 'archived') return this.t().manager.archived;
        if (state === 'live') return this.t().manager.liveChip;
        return state === 'scheduled' ? this.t().manager.scheduled : this.t().manager.unpublishedChip;
    }

    hasPendingSchedule(draftId: string): boolean {
        return this.scheduled().some(p => p.draftId === draftId && p.status === 'Pending');
    }

    // SQLite stores no DateTimeKind and the server sends UTC without a 'Z', which the browser
    // would otherwise read as local time.
    utcDate(iso: string): Date {
        return new Date(/Z|[+-]\d{2}:\d{2}$/.test(iso) ? iso : iso + 'Z');
    }

    async cancelScheduled(id: string) {
        if (!await this.confirmation.confirm({ message: this.t().common.cancelScheduleConfirm, confirmLabel: this.t().common.confirm })) return;
        try {
            await this.postsApi.cancelScheduled(id);
            this.scheduled.update(list => list.filter(p => p.id !== id));
            this.ensureVisibleSelection();
            this.syncCollectionQuery();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.save));
        }
    }

    async unpublish(d: DraftMeta) {
        if (this.busy()) return;
        if (!await this.confirmation.confirm({ message: this.t().common.unpublishConfirm, confirmLabel: this.t().common.confirm })) return;
        this.busy.set(true);
        this.unpublishing.set(true);
        this.error.set('');
        try {
            await this.draftsApi.unpublishFromBlog(d.id);
            this.patch(d.id, { isBlogPublished: false });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().editor.errors.unpublish));
        } finally {
            this.busy.set(false);
            this.unpublishing.set(false);
        }
    }

    // Six language chips in a list column is a wall of two-letter boxes that says little more
    // than three of them plus a count. The full list stays available as the chip's tooltip.
    private static readonly VisibleLanguageChips = 3;

    // DraftMeta.languages holds the TRANSLATIONS only — the primary language is implicit in the
    // model, which meant a post written in one language showed no language at all on its card.
    // The card's second line is "which languages does this post exist in", and the answer is never
    // "none". Primary first, since that is the version the rest of them were made from.
    private allLanguages(d: DraftMeta): string[] {
        const primary = d.primaryLanguage || 'ru';
        return [primary, ...d.languages.filter(l => l !== primary)];
    }

    visibleLanguages(d: DraftMeta): string[] {
        return this.allLanguages(d).slice(0, PostsManagerComponent.VisibleLanguageChips);
    }

    extraLanguageCount(d: DraftMeta): number {
        return Math.max(0, this.allLanguages(d).length - PostsManagerComponent.VisibleLanguageChips);
    }

    extraLanguageTitle(d: DraftMeta): string {
        return this.allLanguages(d).slice(PostsManagerComponent.VisibleLanguageChips).map(l => l.toUpperCase()).join(', ');
    }

    newFeedbackFor(draftId: string): number {
        return this.newByDraft()[draftId] ?? 0;
    }

    async select(d: DraftMeta) {
        this.selectedId.set(d.id);
        this.editTitle = d.title;
        this.editTags.set(d.tags.split(',').map(x => x.trim()).filter(x => x.length > 0));
        this.editSlug = d.blogSlug ?? '';
        this.editArticleTitle = '';
        this.loadArticleTitle(d.id);
        this.regForm.set(null);
        this.postFormLanguages.set([]);
        if (d.isPrivate) this.loadForm();
        this.loadTrackedLinks(d.id);
        this.loadHistory(d.id);
        this.syncCollectionQuery();
    }

    // ─── Tracked links (Wave 2 item 16) — the clicks a post's short links collected ───────────
    draftLinks = signal<TrackedLink[]>([]);
    draftLinksLoading = signal(false);

    private async loadTrackedLinks(draftId: string) {
        this.draftLinks.set([]);
        this.draftLinksLoading.set(true);
        try {
            const links = await this.linksApi.listForDraft(draftId);
            if (this.selectedId() === draftId) this.draftLinks.set(links);
        } catch {
            // 404 until the server lane lands — the row simply does not render.
            this.draftLinks.set([]);
        } finally {
            this.draftLinksLoading.set(false);
        }
    }

    totalClicks(): number {
        return this.draftLinks().reduce((sum, l) => sum + l.clickCount, 0);
    }

    shortUrlOf(link: TrackedLink): string {
        return this.linksApi.shortUrlOf(link);
    }

    blogUrl(d: DraftMeta): string | null {
        const base = this.auth.blogUrl();
        return d.blogSlug && base ? `${base}/${d.blogSlug}` : null;
    }

    /**
     * Failing to load this must not take the page down with it: a missing "where it went" list is
     * a smaller loss than a Publishing Manager that will not open.
     */
    private async loadPublished() {
        try {
            this.published.set((await this.publishApi.published()).posts);
        } catch {
            this.published.set([]);
        }
    }

    private async loadEvents() {
        try {
            this.events.set((await this.publishApi.events(this.project)).events);
        } catch {
            this.events.set([]);
        }
    }

    /** The short-post networks a draft reached; Telegram and the blog have their own rows. */
    publishedElsewhere(d: DraftMeta): PublishedPost[] {
        return this.published().filter(p => p.draftId === d.id && p.network !== 'telegram');
    }

    publishedNetwork(d: DraftMeta, network: string): PublishedPost | null {
        return this.published().find(p => p.draftId === d.id && p.network === network) ?? null;
    }

    /** Every Telegram send of the post, newest first, as the channel recorded them. */
    telegramSends(d: DraftMeta): PublishEvent[] {
        return this.events().filter(e => e.draftId === d.id && e.network === 'telegram');
    }

    private date(iso: string | null | undefined): string {
        return iso ? formatInZone(iso, 'd MMM yyyy') : '';
    }

    destinations(d: DraftMeta): PostDestination[] {
        const t = this.t().manager;
        const sent = (at: string | null | undefined) => at ? t.studio.sentOn(this.date(at)) : t.studio.published;
        const rows: PostDestination[] = [{
            key: 'blog', name: t.blog, brand: null, reached: d.isBlogPublished,
            state: d.isBlogPublished
                ? (d.blogPublishedAt ? t.studio.liveSince(this.date(d.blogPublishedAt)) : t.studio.published)
                : t.studio.notPublished,
            url: d.isBlogPublished ? this.blogUrl(d) : null,
            linkLabel: t.openBlog,
        }];

        // A post sent before sends were recorded has a message id on the draft and no send row.
        const latest = this.telegramSends(d)[0] ?? null;
        const telegramReached = !!latest || !!d.lastTelegramMessageId;
        rows.push({
            key: 'telegram', name: t.telegram, brand: 'telegram', reached: telegramReached,
            state: telegramReached ? sent(latest?.publishedAt) : t.studio.notPublished,
            url: latest?.publicUrl ?? null,
            linkLabel: latest && latest.partCount > 1 ? t.studio.viewThread : t.studio.viewMessage,
        });

        const elsewhere = this.publishedElsewhere(d);
        const networks = [...CORE_NETWORKS, ...new Set(elsewhere.map(p => p.network).filter(n => !CORE_NETWORKS.includes(n)))];
        for (const network of networks) {
            const post = elsewhere.find(p => p.network === network) ?? null;
            const name = this.networkLabel(network);
            rows.push({
                key: network, name, brand: NETWORK_BRANDS[network] ?? null, reached: !!post,
                state: post ? sent(post.finishedAt) : t.studio.notPublished,
                url: post?.publicUrl ?? null,
                linkLabel: post && post.partCount > 1 ? t.studio.viewThread : t.openPost(name),
            });
        }
        return rows;
    }

    reachedCount(d: DraftMeta): number {
        return this.destinations(d).filter(row => row.reached).length;
    }

    /** The first destination the post has not reached, or null when it is live on all of them. */
    nextStep(d: DraftMeta): PostDestination | null {
        return this.destinations(d).find(row => !row.reached) ?? null;
    }

    activity(d: DraftMeta): PostActivity[] {
        const t = this.t().manager;
        const rows: PostActivity[] = [
            ...this.telegramSends(d).map(send => ({
                label: t.studio.publishedTo(t.telegram), at: send.publishedAt, url: send.publicUrl,
            })),
            ...this.publishedElsewhere(d).map(post => ({
                label: t.studio.publishedTo(this.networkLabel(post.network)), at: post.finishedAt, url: post.publicUrl,
            })),
        ];
        if (d.isBlogPublished) rows.push({
            label: t.studio.publishedTo(t.blog), at: d.blogPublishedAt, url: this.blogUrl(d),
        });
        rows.push({ label: t.studio.draftPrepared, at: d.createdAt, url: null });
        return rows
            .sort((a, b) => (b.at ?? '').localeCompare(a.at ?? ''))
            .slice(0, 6);
    }

    networkLabel(network: string): string {
        if (network === 'x') return 'X';
        if (network === 'linkedin') return 'LinkedIn';
        return network.charAt(0).toUpperCase() + network.slice(1);
    }

    folderName(d: DraftMeta): string {
        const folder = d.folderId ? this.foldersApi.folders().find(f => f.id === d.folderId) : null;
        return folder?.name ?? this.t().manager.studio.noFolder;
    }

    languagesLine(d: DraftMeta): string {
        return this.allLanguages(d).map(l => l.toUpperCase()).join(' · ');
    }

    private patch(id: string, patch: Partial<DraftMeta>) {
        this.drafts.update(list => list.map(d => d.id === id ? { ...d, ...patch } : d));
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    async saveMeta() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        this.renaming.set(true);
        this.error.set('');
        try {
            const title = this.editTitle.trim() || 'Untitled';
            if (title !== d.title) {
                // The save endpoint replaces title and body together, so the body has to be
                // fetched and handed back unchanged — a rename must not touch content.
                const full = await this.draftsApi.get(d.id);
                await this.draftsApi.update(d.id, title, full.cedarJson);
            }
            const articleTitle = this.editArticleTitle.trim();
            await this.draftsApi.setArticleTitle(d.id, articleTitle);
            const tags = this.editTags().join(',');
            if (tags !== d.tags) {
                await this.draftsApi.updateTags(d.id, tags);
                this.tagUsageApi.refresh();
            }
            this.patch(d.id, { title, tags });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.save));
        } finally {
            this.busy.set(false);
            this.renaming.set(false);
        }
    }

    async assignFolder(folderId: string | null) {
        const d = this.selected();
        if (!d || d.folderId === folderId) return;
        try {
            await this.draftsApi.setDraftFolder(d.id, folderId);
            this.patch(d.id, { folderId });
            this.foldersApi.reload();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.move));
        }
    }

    async togglePrivate() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        try {
            await this.draftsApi.setDraftPrivate(d.id, !d.isPrivate);
            this.patch(d.id, { isPrivate: !d.isPrivate });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.privacy));
        } finally {
            this.busy.set(false);
        }
    }

    async setEngagement(disableReactions: boolean, disableComments: boolean) {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        try {
            await this.draftsApi.setEngagement(d.id, disableReactions, disableComments);
            this.patch(d.id, { disableReactions, disableComments });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.save));
        } finally {
            this.busy.set(false);
        }
    }

    async toggleDisableCopy() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        try {
            await this.draftsApi.setDraftDisableCopy(d.id, !d.disableCopy);
            this.patch(d.id, { disableCopy: !d.disableCopy });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.privacy));
        } finally {
            this.busy.set(false);
        }
    }

    async toggleArchive() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        try {
            const res = d.isArchived ? await this.draftsApi.unarchive(d.id) : await this.draftsApi.archive(d.id);
            this.patch(d.id, { isArchived: res.isArchived });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.update));
        } finally {
            this.busy.set(false);
        }
    }

    async confirmDelete() {
        const id = this.deleteConfirmId();
        if (!id) return;
        this.deleteConfirmId.set(null);
        this.busy.set(true);
        try {
            await this.draftsApi.remove(id);
            this.drafts.update(list => list.filter(d => d.id !== id));
            if (this.selectedId() === id) this.selectedId.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.delete));
        } finally {
            this.busy.set(false);
        }
    }

    // FI3.4 — slugified server-side, so a hand-typed URL can't end up unroutable, and rejected
    // if another post already holds it (blog lookup is by slug across all owners).
    async saveSlug(d: DraftMeta) {
        const slug = this.editSlug.trim();
        if (!slug || this.busy()) return;
        this.busy.set(true);
        this.error.set('');
        try {
            const res = await this.draftsApi.setBlogSlug(d.id, slug);
            this.editSlug = res.blogSlug;
            this.drafts.update(list => list.map(x => x.id === d.id ? { ...x, blogSlug: res.blogSlug } : x));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.slug));
        } finally {
            this.busy.set(false);
        }
    }

    // FI3.7 — one dropdown handles both "use this preset" and "remove the form".
    pickPreset(value: string) {
        if (!value) return;
        if (value === '__none') {
            this.clearPostForm();
            return;
        }
        const preset = this.presets().find(p => p.id === value);
        if (preset) this.applyPresetToPost(preset);
    }

    // Lives on the full draft rather than the list projection - the list is already wide and
    // the article title is only ever needed for the one post being looked at.
    private async loadArticleTitle(id: string) {
        try {
            const full = await this.draftsApi.get(id);
            if (this.selectedId() === id) this.editArticleTitle = full.articleTitle ?? '';
        } catch { /* the field simply stays empty; saving it still works */ }
    }

    async loadForm() {
        const d = this.selected();
        if (!d || !d.isPrivate) return;
        try {
            const full = await this.draftsApi.get(d.id);
            if (this.selectedId() !== d.id) return;
            this.regForm.set(parseRegistrationForm(full.registrationFormJson, this.locale.uiLang()));
            this.postFormLanguages.set(full.formLanguages ?? []);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadForm));
        }
    }

    // The raw blob goes over the wire untouched — re-serializing the single-language projection
    // here would silently strip a v2 blob's other languages. The displayed form and language
    // list always come back from the server's response, whatever shape was written.
    private async persistFormJson(formJson: string | null, language = DEFAULT_PRIMARY_LANGUAGE) {
        const d = this.selected();
        if (!d) return;
        this.regBusy.set(true);
        try {
            const res = await this.draftsApi.setRegistrationForm(d.id, formJson, language);
            this.postFormLanguages.set(res.formLanguages ?? []);
            this.regForm.set(parseRegistrationForm(res.registrationFormJson, this.locale.uiLang()));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.saveForm));
        } finally {
            this.regBusy.set(false);
        }
    }

    // A post gets a form by choosing a preset, never by editing one in place — the definition is
    // authored once on the Forms page. The preset is COPIED here (N12), so editing it afterwards
    // can't rewrite a post that already used it. A v2 preset carries every language in one blob,
    // so one click attaches them all (ADR-060); a legacy v1 preset still fills only its slot.
    async applyPresetToPost(p: FormPreset) {
        await this.persistFormJson(p.formJson, p.language || DEFAULT_PRIMARY_LANGUAGE);
    }

    async clearPostForm() {
        if (!await this.confirmation.confirm(this.t().common.removeFormConfirm)) return;
        await this.persistFormJson(null);
    }

    async loadPresets() {
        this.presetsLoading.set(true);
        this.presetLoadError.set('');
        try {
            this.presets.set(await this.presetsApi.list());
        } catch (e) {
            this.presetLoadError.set(httpErrorMessage(e, this.t().manager.errors.loadPresets));
        } finally {
            this.presetsLoading.set(false);
        }
    }

    /** One tone per publish state: live is good news, a pending send is a warning, the rest rests. */
    stateTone(d: DraftMeta): 'ok' | 'warn' | 'muted' {
        if (this.publishState(d) === 'live') return 'ok';
        return this.hasPendingSchedule(d.id) ? 'warn' : 'muted';
    }

    // Screen-level counts, so the header says the same thing on every tab.
    headerMeta(): HeaderMeta[] {
        const t = this.t().manager;
        return [
            { text: t.rulerPosts(this.postPool().length) },
            { text: t.rulerPublished(this.publishedCount()) },
            ...(this.pendingScheduleCount() ? [{ text: t.rulerScheduled(this.pendingScheduleCount()) }] : []),
        ];
    }

    sheetLabel(): string {
        const d = this.selected();
        return d ? (d.title || this.t().drafts.untitled) : this.t().manager.crumb;
    }

    publishedCount(): number {
        return this.postPool().filter(d => isPublishedAnywhere(d)).length;
    }

    pendingScheduleCount(): number {
        return this.scheduled().filter(p => p.status === 'Pending').length;
    }

    private restoreCollectionQuery() {
        const params = this.route.snapshot.queryParamMap;
        this.search.set(params.get('q') ?? '');
        const state = params.get('status');
        if (state && STATE_FILTERS.includes(state as PostStateFilter)) this.stateFilter.set(state as PostStateFilter);
        const sort = params.get('sort');
        const direction = params.get('dir');
        if (sort && ['published', 'updated', 'title', 'activity'].includes(sort))
            this.postSort.set(sort as PostSort);
        if (direction === 'asc' || direction === 'desc') this.postSortDirection.set(direction);
    }

    private syncCollectionQuery() {
        void this.router.navigate([], {
            relativeTo: this.route,
            replaceUrl: true,
            queryParamsHandling: 'merge',
            queryParams: {
                q: this.search().trim() || null,
                status: this.stateFilter() === 'all' ? null : this.stateFilter(),
                sort: this.postSort() === 'published' ? null : this.postSort(),
                dir: this.postSort() === 'published' && this.postSortDirection() === 'desc'
                    ? null : this.postSortDirection(),
                draft: this.selectedId(),
            },
        });
    }

    private async loadProjects() {
        if (!this.auth.indieDev()) return;
        try {
            this.projects.set(await this.projectsApi.list(true));
        } catch {
            // A hub that will not answer is not a reason to fail the whole manager: the picker
            // simply does not appear, exactly as it does when the module is off.
        }
    }

    projectName(id: string | null): string {
        if (!id) return this.t().manager.noProject;
        return this.projects().find(p => p.id === id)?.name ?? this.t().manager.noProject;
    }

    async setProject(draftId: string, projectId: string) {
        const current = this.drafts().find(d => d.id === draftId)?.projectId ?? null;
        const next = projectId || null;
        if (next === current) return;
        this.busy.set(true);
        this.error.set('');
        try {
            if (next) await this.projectsApi.attachDocument(next, draftId);
            else if (current) await this.projectsApi.detachDocument(current, draftId);
            this.drafts.update(list => list.map(d => (d.id === draftId ? { ...d, projectId: next } : d)));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.load));
        } finally {
            this.busy.set(false);
        }
    }
}
