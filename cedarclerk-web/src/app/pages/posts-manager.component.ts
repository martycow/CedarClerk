import { ConfirmationService } from '../core/confirmation.service';
import { Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { formatInZone } from '../core/display-time';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { AuthService } from '../core/auth.service';
import {
    DraftsService, DraftMeta, PostRegistration,
    RegistrationForm, RegistrationQuestion, RegistrationQuestionType, parseRegistrationForm,
} from '../core/drafts.service';
import {
    FormPresetsService, FormPreset, RegistrationFormEdit, FormQuestionEdit,
    normalizeFormForEdit, blankFormEdit, newQuestionId, newOptionId,
} from '../core/form-presets.service';
import { PostsService, ScheduledPost } from '../core/posts.service';
import { PublishedPost, PublishService } from '../core/publish.service';
import { LinksService, TrackedLink } from '../core/links.service';
import { DEFAULT_PRIMARY_LANGUAGE, CONTENT_LANGUAGES } from '../core/languages';
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
import { StatsComponent } from './stats.component';
import { IconComponent } from '../shared/icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { PlanLockComponent } from '../shared/plan-lock.component';
import { LanguageMenuComponent } from '../shared/language-menu.component';
import { HintDotComponent } from '../shared/hint-dot.component';
import { IndexTabItem, indexTabBadgeLabel } from '../bench/chrome/index-tabs.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';
import { SpecRowComponent, SpecScope } from '../bench/worktop/spec-row.component';
import { GrowthChartComponent, GrowthSeries, SeriesSlot } from '../bench/worktop/growth-chart.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { BrandIconComponent } from '../shared/brand-icon.component';
import { PopoverComponent } from '../shared/popover.component';
import { SortDirection } from '../core/collection-query';

// FI3.5 removed the 'feedback' tab; ?tab=feedback still resolves (to posts, where feedback now
// lives) because links to it exist in the wild — the account menu, and Marty's own bookmarks.
export type ManagerTab = 'posts' | 'stats' | 'forms';
const MANAGER_TABS: ManagerTab[] = ['posts', 'stats', 'forms'];
const RETIRED_TABS: Record<string, ManagerTab> = { feedback: 'posts' };
// The first DraftStatSnapshot night (8.6): a calendar day, compared against the ISO date prefix.
const STAT_HISTORY_START = '2026-08-01';
type PostStateFilter = 'all' | 'live' | 'draft' | 'scheduled' | 'archived';
type PostVisibilityFilter = 'all' | 'public' | 'private';
type PostSort = 'published' | 'updated' | 'title' | 'activity';
type PresetSort = 'created' | 'name' | 'questions';

// N7 — the Posts Manager. Comments/reactions and stats used to be two separate top-level pages
// with their own headers; they are now tab bodies here (their routes redirect), so there is one
// place that answers "what happened to my posts". The forms tab is intentionally read-only for
// now — editing, per-question breakdowns and the pie chart are N10, presets are N12.
@Component({
    selector: 'app-posts-manager',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, ModalComponent, CommentsComponent, StatsComponent,
        TagPickerComponent, FolderPickerComponent, FormRefComponent, ButtonComponent,
        PlanLockComponent, HintDotComponent,
        LeafTagComponent, SpecRowComponent, PageHeaderComponent, EmptyStateComponent,
        GrowthChartComponent, LanguageMenuComponent, InputComponent, BrandIconComponent,
        PopoverComponent,
    ],
    templateUrl: 'posts-manager.component.html',
    styleUrls: ['posts-manager.component.css'],
})
export class PostsManagerComponent implements OnInit {
    private readonly confirmation = inject(ConfirmationService);
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

    @ViewChild('presetSheetBody') private presetSheetBody?: ElementRef<HTMLElement>;

    tab = signal<ManagerTab>('posts');
    loading = signal(true);
    error = signal('');
    busy = signal(false);

    drafts = signal<DraftMeta[]>([]);
    selectedId = signal<string | null>(null);
    private failedCovers = signal<ReadonlySet<string>>(new Set());

    // ADR-203 — a post written before the module existed belongs to no project, and there was no
    // screen that could put it in one: the project hub only offers documents it created itself.
    // Empty while the module is off, which is what keeps the control off the sheet entirely.
    projects = signal<ProjectSummary[]>([]);

    // Minimal edits (Marty's wording) — everything here changes metadata only. Body text stays
    // the editor's job; the rename below still has to round-trip cedarJson because the save
    // endpoint takes title and body together.
    editTitle = '';
    // Idea #4 - the headline readers see, kept apart from the draft's own name above. Empty
    // means "same as the name", which is what it was before this field existed.
    editArticleTitle = '';
    editTags = signal<string[]>([]);
    // FI3.4 — the published post's own URL.
    editSlug = '';
    // FI3.10 — the list is publish-date ordered; search is how you reach one post directly.
    search = signal('');
    stateFilter = signal<PostStateFilter>('all');
    visibilityFilter = signal<PostVisibilityFilter>('all');
    projectFilter = signal('all');
    languageFilter = signal('all');
    postSort = signal<PostSort>('published');
    postSortDirection = signal<SortDirection>('desc');
    // Per-draft count of comments arrived since the last look, for the list's "+N" chip.
    newByDraft = signal<Record<string, number>>({});
    renaming = signal(false);
    deleteConfirmId = signal<string | null>(null);

    // FI2.1/FI2.8 — the two things the Export window stopped doing, because it exports and does
    // not manage what is already out there.
    unpublishing = signal(false);
    scheduled = signal<ScheduledPost[]>([]);

    registrations = signal<PostRegistration[]>([]);
    registrationsLoading = signal(false);
    // A submission the owner wants gone (their own test answers polluting the charts) — held here
    // while the confirm modal is up, same shape as deleteConfirmId/deletePresetId.
    deleteRegistrationTarget = signal<PostRegistration | null>(null);
    registrationDeleting = signal(false);
    // T-108 — revoking throws a live reader out, so it confirms like delete; restoring lets one
    // back in and does not.
    revokeRegistrationTarget = signal<PostRegistration | null>(null);
    registrationRevoking = signal(false);

    // The form attached to the currently selected post. Shown on the POSTS tab (a post is where a
    // form is used), never edited there directly — you pick a preset, and the preset is copied.
    regForm = signal<RegistrationForm | null>(null);
    // FI4.1 — languages the selected post has a registration form for, primary first.
    postFormLanguages = signal<string[]>([]);
    regBusy = signal(false);

    // Forms tab — presets only. It used to require picking a post first and edited that post's
    // form, which made presets look like a property of a post; they aren't. Forms are authored
    // here as presets and chosen per post elsewhere.
    presets = signal<FormPreset[]>([]);
    presetsLoading = signal(false);
    presetsLoaded = signal(false);
    presetLoadError = signal('');
    private presetsLoadPromise: Promise<void> | null = null;
    selectedPresetId = signal<string | null>(null);
    presetSearch = signal('');
    presetLanguageFilter = signal('all');
    presetSort = signal<PresetSort>('created');
    presetSortDirection = signal<SortDirection>('desc');
    presetName = '';
    readonly primaryLanguage = DEFAULT_PRIMARY_LANGUAGE;
    readonly contentLanguages = CONTENT_LANGUAGES;
    // ADR-060 — the editor works on the v2 multi-language blob natively: one skeleton of stable
    // question/option ids, per-language texts on top, so "Да" and "Yes" stay one answer.
    presetForm = signal<RegistrationFormEdit | null>(null);
    presetState = signal<'saved' | 'dirty' | 'saving' | 'error'>('saved');
    deletePresetId = signal<string | null>(null);
    // The language a machine translation is currently running for, or null.
    presetTranslating = signal<string | null>(null);
    presetTranslateError = signal('');

    /** Where each post actually went — filled from the publish queue's own record. */
    published = signal<PublishedPost[]>([]);

    async ngOnInit() {
        this.restoreCollectionQuery();
        this.feedback.refreshNewCount();
        // The default tab is 'posts' and setTab() only runs for a ?tab= deep link, so without
        // this eager load a plain landing here never fetched the presets at all — the form
        // dropdown then claimed "no saved presets yet" over a non-empty library.
        void this.loadPresets();
        this.loadProjects();
        try {
            this.drafts.set(await this.draftsApi.list());
            this.loadPublished();
            this.loadScheduled();
            this.loadNewFeedback();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.load));
        } finally {
            this.loading.set(false);
        }
        // The export modal links straight here when no preset exists yet (I9), so land on the
        // tab that was asked for rather than on the default one.
        const requested = this.route.snapshot.queryParamMap.get('tab');
        if (requested) {
            const resolved = RETIRED_TABS[requested] ?? (MANAGER_TABS.includes(requested as ManagerTab) ? requested as ManagerTab : null);
            if (resolved) this.setTab(resolved);
        }
        // Wave 2 — the calendar's sent tickets deep-link here with the post to select.
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

    setTab(tab: ManagerTab) {
        // Leaving the posts tab is when the badge is worth re-checking: hovering feedback rows
        // there is exactly what clears the count.
        if (this.tab() === 'posts' && tab !== 'posts') this.feedback.refreshNewCount();
        // Leaving the forms tab with unsaved preset edits commits them rather than dropping them.
        if (this.tab() === 'forms' && tab !== 'forms') this.flushPreset();
        this.tab.set(tab);
        this.syncCollectionQuery();
        if (tab === 'forms') this.resetPresetScroll();
        // Presets are needed by both tabs now: authored on forms, applied to a post on posts.
        if ((tab === 'forms' || tab === 'posts') && !this.presetsLoaded()) void this.loadPresets();
    }

    selected(): DraftMeta | null {
        const id = this.selectedId();
        return id ? this.postPool().find(d => d.id === id) ?? null : null;
    }

    selectFirstPost() {
        const first = this.visiblePosts()[0];
        if (first) void this.select(first);
    }

    readonly postPool = computed(() =>
        this.drafts().filter(d => !d.isTemplate && isPublishableType(d.documentType)));

    readonly availableLanguages = computed(() => [...new Set(
        this.postPool().flatMap(d => this.allLanguages(d)))].sort());

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

    readonly availablePresetLanguages = computed(() => [...new Set(
        this.presets().flatMap(p => this.presetLanguagesOf(p)))].sort());

    readonly presetSortOptions = computed(() => {
        const t = this.t().manager.forms;
        return [
            { value: 'created:desc', label: t.sortNewest },
            { value: 'created:asc', label: t.sortOldest },
            { value: 'name:asc', label: t.sortNameAsc },
            { value: 'name:desc', label: t.sortNameDesc },
            { value: 'questions:desc', label: t.sortQuestionsDesc },
            { value: 'questions:asc', label: t.sortQuestionsAsc },
        ];
    });

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

    filterCount(): number {
        return Number(this.stateFilter() !== 'all')
            + Number(this.visibilityFilter() !== 'all')
            + Number(this.projectFilter() !== 'all')
            + Number(this.languageFilter() !== 'all');
    }

    hasPostFilters(): boolean {
        return !!this.search().trim() || this.filterCount() > 0;
    }

    onPostSearch(value: string) {
        this.search.set(value);
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    setStateFilter(value: string) {
        if (!['all', 'live', 'draft', 'scheduled', 'archived'].includes(value)) return;
        this.stateFilter.set(value as PostStateFilter);
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    setVisibilityFilter(value: string) {
        if (!['all', 'public', 'private'].includes(value)) return;
        this.visibilityFilter.set(value as PostVisibilityFilter);
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    setProjectFilter(value: string) {
        this.projectFilter.set(value);
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    setLanguageFilter(value: string) {
        this.languageFilter.set(value);
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
        this.visibilityFilter.set('all');
        this.projectFilter.set('all');
        this.languageFilter.set('all');
        this.ensureVisibleSelection();
        this.syncCollectionQuery();
    }

    presetSortValue(): string {
        return `${this.presetSort()}:${this.presetSortDirection()}`;
    }

    hasPresetFilters(): boolean {
        return !!this.presetSearch().trim() || this.presetLanguageFilter() !== 'all';
    }

    onPresetSearch(value: string) {
        this.presetSearch.set(value);
        this.ensureVisiblePresetSelection();
        this.syncCollectionQuery();
    }

    setPresetLanguageFilter(value: string) {
        this.presetLanguageFilter.set(value);
        this.ensureVisiblePresetSelection();
        this.syncCollectionQuery();
    }

    setPresetSortValue(value: string) {
        const [key, direction] = value.split(':');
        if (!['created', 'name', 'questions'].includes(key)
            || (direction !== 'asc' && direction !== 'desc')) return;
        this.presetSort.set(key as PresetSort);
        this.presetSortDirection.set(direction);
        this.syncCollectionQuery();
    }

    clearPresetFilters() {
        this.presetSearch.set('');
        this.presetLanguageFilter.set('all');
        this.syncCollectionQuery();
    }

    private ensureVisibleSelection() {
        const selected = this.selectedId();
        if (selected && !this.visiblePosts().some(d => d.id === selected)) this.selectedId.set(null);
    }

    private ensureVisiblePresetSelection() {
        const selected = this.selectedPresetId();
        if (!selected || this.visiblePresets().some(p => p.id === selected)) return;
        void this.flushPreset();
        this.selectedPresetId.set(null);
        this.presetForm.set(null);
        this.presetState.set('saved');
    }

    visiblePosts(): DraftMeta[] {
        const q = this.search().trim().toLowerCase();
        const state = this.stateFilter();
        const visibility = this.visibilityFilter();
        const project = this.projectFilter();
        const language = this.languageFilter();
        const matched = this.postPool().filter(d => {
            if (q && !d.title.toLowerCase().includes(q) && !d.tags.toLowerCase().includes(q)) return false;
            if (state !== 'all' && this.publishState(d) !== state) return false;
            if (visibility === 'private' && !d.isPrivate) return false;
            if (visibility === 'public' && d.isPrivate) return false;
            if (project === 'none' && d.projectId !== null) return false;
            if (project !== 'all' && project !== 'none' && d.projectId !== project) return false;
            if (language !== 'all' && !this.allLanguages(d).includes(language)) return false;
            return true;
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

    // FI3.6 — how much arrived on this post since the last look. Comments only: reactions have no
    // per-draft "new" count in the feedback payload.
    private async loadScheduled() {
        try {
            this.scheduled.set(await this.postsApi.listScheduled());
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
    private readonly networkLabels: Record<string, string> = { telegram: 'Telegram', bluesky: 'Bluesky', x: 'X', discord: 'Discord' };

    scheduledNetworkLabel(post: ScheduledPost): string {
        return this.networkLabels[post.network] ?? post.network;
    }

    // ─── Detail groups (8.4) and the growth chart (8.6) ───────────────────────────────────────
    // Which groups are open is a per-browser preference, not per-post: someone who works with
    // scheduling wants the scheduling group open on every post they touch.
    private static readonly OPEN_KEY = 'cedar-post-detail-open';
    private openGroups = signal<Set<string>>(new Set(this.loadOpenGroups()));

    private loadOpenGroups(): string[] {
        try {
            const raw = localStorage.getItem(PostsManagerComponent.OPEN_KEY);
            // Basics and destinations by default: what the post is, and where it went.
            return raw ? JSON.parse(raw) as string[] : ['basics', 'destinations'];
        } catch {
            return ['basics', 'destinations'];
        }
    }

    isOpen(group: string): boolean {
        return this.openGroups().has(group);
    }

    rememberOpen(group: string, event: Event) {
        const open = (event.target as HTMLDetailsElement).open;
        this.openGroups.update(set => {
            const next = new Set(set);
            open ? next.add(group) : next.delete(group);
            localStorage.setItem(PostsManagerComponent.OPEN_KEY, JSON.stringify([...next]));
            return next;
        });
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

    /** The overview needs the view line immediately; the detailed group reuses the same response. */
    async loadHistory(draftId: string, forOverview = false) {
        if ((!forOverview && !this.isOpen('growth')) || this.historyFor() === draftId) return;
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
        if (d.isBlogPublished) return 'live';
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
        this.registrations.set([]);
        this.regForm.set(null);
        if (d.isPrivate) this.loadForm();
        this.loadTrackedLinks(d.id);
        this.loadHistory(d.id, true);
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
     * a smaller loss than a Posts Manager that will not open.
     */
    private async loadPublished() {
        try {
            this.published.set((await this.publishApi.published()).posts);
        } catch {
            this.published.set([]);
        }
    }

    /**
     * The short-post networks a draft reached. Telegram and the blog have their own rows already,
     * built from the draft itself; these are the ones nothing outside the editor ever showed.
     */
    publishedElsewhere(d: DraftMeta): PublishedPost[] {
        return this.published().filter(p => p.draftId === d.id && p.network !== 'telegram');
    }

    publishedNetwork(d: DraftMeta, network: string): PublishedPost | null {
        return this.published().find(p => p.draftId === d.id && p.network === network) ?? null;
    }

    studioActivity(d: DraftMeta): { label: string; at: string | null }[] {
        const rows: { label: string; at: string | null }[] = this.publishedElsewhere(d).map(post => ({
            label: this.t().manager.studio.publishedTo(this.networkLabel(post.network)),
            at: post.finishedAt,
        }));
        if (this.telegramUrl(d)) rows.push({
            label: this.t().manager.studio.publishedTo(this.t().manager.telegram),
            at: d.updatedAt,
        });
        if (d.isBlogPublished) rows.push({
            label: this.t().manager.studio.publishedTo(this.t().manager.blog),
            at: d.blogPublishedAt,
        });
        return rows
            .sort((a, b) => (b.at ?? '').localeCompare(a.at ?? ''))
            .slice(0, 5);
    }

    networkLabel(network: string): string {
        return network === 'x' ? 'X' : network.charAt(0).toUpperCase() + network.slice(1);
    }

    telegramUrl(d: DraftMeta): string | null {
        return d.lastTelegramUsername && d.lastTelegramMessageId
            ? `https://t.me/${d.lastTelegramUsername}/${d.lastTelegramMessageId}`
            : null;
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

    // Answers are keyed by question id (ADR-042); the labels live in the form definition, which
    // this tab has in hand — so unlike the first cut, they resolve to the real question text. A
    // question deleted after someone answered it falls back to its raw key rather than vanishing.
    // T-035 — the submission opened in full; null when the list is just a list.
    selectedRegistration = signal<PostRegistration | null>(null);

    registrationAnswers(r: PostRegistration): { label: string; value: string }[] {
        if (!r.answersJson) return [];
        let parsed: Record<string, string>;
        try {
            parsed = JSON.parse(r.answersJson) as Record<string, string>;
        } catch {
            return [];
        }
        const questions = this.regForm()?.questions ?? [];
        return Object.entries(parsed)
            .filter(([, value]) => `${value}`.trim().length > 0)
            .map(([key, value]) => {
                const q = questions.find(x => x.id === key);
                // Stored answers are option ids (ADR-060) — shown as the current form's labels.
                // A raw value with no matching option (free text, or a pre-v2 row) shows as-is.
                const labelById = new Map((q?.options ?? []).map(o => [o.id, o.label]));
                const display = (v: string) => labelById.get(v) ?? v;
                return {
                    label: q?.label || key,
                    value: q?.type === 'multi'
                        ? splitMultiAnswer(value).map(display).join(', ')
                        : display(String(value)),
                };
            });
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

    // ---------- The post's own form (Posts tab) ----------

    async loadForm() {
        const d = this.selected();
        if (!d || !d.isPrivate) return;
        this.registrationsLoading.set(true);
        try {
            const [full, regs] = await Promise.all([
                this.draftsApi.get(d.id),
                this.draftsApi.listRegistrations(d.id),
            ]);
            this.regForm.set(parseRegistrationForm(full.registrationFormJson, this.locale.uiLang()));
            this.postFormLanguages.set(full.formLanguages ?? []);
            this.registrations.set(regs);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadForm));
        } finally {
            this.registrationsLoading.set(false);
        }
    }

    // Deleting a submission also revokes that reader's access — the row carries the grant
    // (ADR-084), and removing a test account should mean exactly that. The charts recompute from
    // the updated list on their own: distribution() reads registrations().
    async confirmDeleteRegistration() {
        const d = this.selected();
        const target = this.deleteRegistrationTarget();
        if (!d || !target || this.registrationDeleting()) return;
        this.registrationDeleting.set(true);
        try {
            await this.draftsApi.deleteRegistration(d.id, target.id);
            this.registrations.update(list => list.filter(r => r.id !== target.id));
            this.deleteRegistrationTarget.set(null);
            if (this.selectedRegistration()?.id === target.id) this.selectedRegistration.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadForm));
        } finally {
            this.registrationDeleting.set(false);
        }
    }

    async confirmRevokeRegistration() {
        const target = this.revokeRegistrationTarget();
        if (!target) return;
        const done = await this.setRegistrationRevoked(target, true);
        if (done) this.revokeRegistrationTarget.set(null);
    }

    restoreRegistration(r: PostRegistration) {
        return this.setRegistrationRevoked(r, false);
    }

    private async setRegistrationRevoked(target: PostRegistration, revoked: boolean): Promise<boolean> {
        const d = this.selected();
        if (!d || this.registrationRevoking()) return false;
        this.registrationRevoking.set(true);
        try {
            const res = revoked
                ? await this.draftsApi.revokeRegistration(d.id, target.id)
                : await this.draftsApi.restoreRegistration(d.id, target.id);
            const patch = (r: PostRegistration) => r.id === target.id ? { ...r, isRevoked: res.isRevoked } : r;
            this.registrations.update(list => list.map(patch));
            const open = this.selectedRegistration();
            if (open?.id === target.id) this.selectedRegistration.set(patch(open));
            return true;
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.forms.revokeFailed));
            return false;
        } finally {
            this.registrationRevoking.set(false);
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
    // authored once on the Forms tab. The preset is COPIED here (N12), so editing it afterwards
    // can't rewrite a post that already used it. A v2 preset carries every language in one blob,
    // so one click attaches them all (ADR-060); a legacy v1 preset still fills only its slot.
    async applyPresetToPost(p: FormPreset) {
        await this.persistFormJson(p.formJson, p.language || DEFAULT_PRIMARY_LANGUAGE);
    }

    async clearPostForm() {
        if (!await this.confirmation.confirm(this.t().common.removeFormConfirm)) return;
        await this.persistFormJson(null);
    }

    // ---------- Preset authoring (Forms tab) ----------

    loadPresets(): Promise<void> {
        if (this.presetsLoaded()) return Promise.resolve();
        if (!this.presetsLoadPromise) {
            this.presetsLoadPromise = this.fetchPresets().finally(() => {
                this.presetsLoadPromise = null;
            });
        }
        return this.presetsLoadPromise;
    }

    private async fetchPresets() {
        this.presetsLoading.set(true);
        this.presetLoadError.set('');
        try {
            const remote = await this.presetsApi.list();
            this.presets.update(current => {
                const currentById = new Map(current.map(p => [p.id, p]));
                const remoteIds = new Set(remote.map(p => p.id));
                return [
                    ...remote.map(p => currentById.get(p.id) ?? p),
                    ...current.filter(p => !remoteIds.has(p.id)),
                ];
            });
            this.presetsLoaded.set(true);
        } catch (e) {
            this.presetLoadError.set(httpErrorMessage(e, this.t().manager.errors.loadPresets));
        } finally {
            this.presetsLoading.set(false);
        }
    }

    selectedPreset(): FormPreset | null {
        const id = this.selectedPresetId();
        return id ? this.presets().find(p => p.id === id) ?? null : null;
    }

    // Keyed on the blob itself so the list rows don't re-parse on every change-detection pass;
    // an edit produces a new formJson string and naturally misses the cache.
    private presetLangsCache = new Map<string, string[]>();
    presetLanguagesOf(p: FormPreset): string[] {
        let cached = this.presetLangsCache.get(p.formJson);
        if (!cached) {
            cached = normalizeFormForEdit(p.formJson, p.language || DEFAULT_PRIMARY_LANGUAGE).languages;
            this.presetLangsCache.set(p.formJson, cached);
        }
        return cached;
    }

    presetQuestionCount(p: FormPreset): number {
        return normalizeFormForEdit(p.formJson, p.language || DEFAULT_PRIMARY_LANGUAGE).questions.length;
    }

    visiblePresets(): FormPreset[] {
        const query = this.presetSearch().trim().toLowerCase();
        const language = this.presetLanguageFilter();
        const key = this.presetSort();
        const direction = this.presetSortDirection() === 'asc' ? 1 : -1;
        return this.presets()
            .filter(p => (!query || p.name.toLowerCase().includes(query))
                && (language === 'all' || this.presetLanguagesOf(p).includes(language)))
            .sort((a, b) => {
                const compared = key === 'created'
                    ? a.createdAt.localeCompare(b.createdAt)
                    : key === 'name'
                        ? a.name.localeCompare(b.name)
                        : this.presetQuestionCount(a) - this.presetQuestionCount(b);
                return direction * compared || a.id.localeCompare(b.id);
            });
    }

    async selectPreset(p: FormPreset) {
        await this.flushPreset();
        this.selectedPresetId.set(p.id);
        this.presetName = p.name;
        this.presetForm.set(normalizeFormForEdit(p.formJson, p.language || DEFAULT_PRIMARY_LANGUAGE));
        this.presetState.set('saved');
        this.presetTranslateError.set('');
        this.resetPresetScroll();
    }

    // Created immediately rather than held as a local draft: a preset with no id has nowhere to
    // be saved to, and the list is the only place it would show up.
    async newPreset() {
        await this.flushPreset();
        const blank = blankFormEdit(DEFAULT_PRIMARY_LANGUAGE);
        try {
            const created = await this.presetsApi.create(
                this.t().manager.forms.untitledPreset, JSON.stringify(blank), DEFAULT_PRIMARY_LANGUAGE);
            this.presets.update(list => [...list, created]);
            this.presetsLoaded.set(true);
            this.selectedPresetId.set(created.id);
            this.presetName = created.name;
            this.presetForm.set(blank);
            this.presetState.set('saved');
            this.presetTranslateError.set('');
            this.resetPresetScroll();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.savePreset));
        }
    }

    private resetPresetScroll() {
        const body = this.presetSheetBody?.nativeElement;
        if (body) body.scrollTop = 0;
    }

    private editPreset(next: RegistrationFormEdit) {
        this.presetForm.set(next);
        this.presetState.set('dirty');
    }

    markPresetNameDirty() {
        this.presetState.set('dirty');
    }

    async savePreset() {
        const p = this.selectedPreset();
        const form = this.presetForm();
        const name = this.presetName.trim();
        if (!p || !form || !name) return;
        this.presetState.set('saving');
        try {
            const saved = await this.presetsApi.update(p.id, name, JSON.stringify(form), form.languages[0]);
            this.presets.update(list => list.map(x => x.id === saved.id ? saved : x));
            this.presetState.set('saved');
        } catch (e) {
            this.presetState.set('error');
            this.error.set(httpErrorMessage(e, this.t().manager.errors.savePreset));
        }
    }

    // ---------- Preset languages (ADR-060) ----------

    presetLangs(): string[] {
        return this.presetForm()?.languages ?? [];
    }

    addableLanguages(): string[] {
        const used = this.presetLangs();
        return CONTENT_LANGUAGES.filter(l => !used.includes(l));
    }

    addPresetLanguage(lang: string) {
        const form = this.presetForm();
        if (!form || form.languages.includes(lang)) return;
        this.editPreset({ ...form, languages: [...form.languages, lang] });
    }

    // The first language is the skeleton's fallback — everything else may go. Removing one also
    // strips its texts so a re-added language starts clean instead of resurrecting stale copy.
    async removePresetLanguage(lang: string) {
        if (!await this.confirmation.confirm(this.t().common.removeAuthoredContentConfirm)) return;
        const form = this.presetForm();
        if (!form || form.languages[0] === lang) return;
        const strip = (map: Record<string, string>) => {
            const { [lang]: _, ...rest } = map;
            return rest;
        };
        this.editPreset({
            ...form,
            languages: form.languages.filter(l => l !== lang),
            intro: strip(form.intro),
            responseEmailSubject: strip(form.responseEmailSubject),
            responseEmailBody: strip(form.responseEmailBody),
            questions: form.questions.map(q => ({
                ...q,
                label: strip(q.label),
                options: q.options.map(o => ({ ...o, label: strip(o.label) })),
            })),
        });
    }

    // Fills one language by machine-translating the preset's first language server-side (same
    // Pro Plus + daily-quota gates as post auto-translate). Unsaved edits are flushed first so
    // the server translates what's on screen, and the saved result replaces the local state.
    async translatePresetLanguage(lang: string) {
        const p = this.selectedPreset();
        if (!p || this.presetTranslating()) return;
        this.presetTranslateError.set('');
        this.presetTranslating.set(lang);
        try {
            await this.flushPreset();
            const saved = await this.presetsApi.translate(p.id, lang);
            this.presets.update(list => list.map(x => x.id === saved.id ? saved : x));
            this.presetForm.set(normalizeFormForEdit(saved.formJson, saved.language || DEFAULT_PRIMARY_LANGUAGE));
            this.presetState.set('saved');
        } catch (e) {
            this.presetTranslateError.set(httpErrorMessage(e, this.t().manager.errors.translatePreset));
        } finally {
            this.presetTranslating.set(null);
        }
    }

    private async flushPreset() {
        if (this.presetState() === 'dirty') await this.savePreset();
    }

    async confirmDeletePreset() {
        const id = this.deletePresetId();
        if (!id) return;
        this.deletePresetId.set(null);
        try {
            await this.presetsApi.remove(id);
            this.presets.update(list => list.filter(x => x.id !== id));
            if (this.selectedPresetId() === id) {
                this.selectedPresetId.set(null);
                this.presetForm.set(null);
                this.presetState.set('saved');
            }
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.deletePreset));
        }
    }

    // ---------- Preset form-definition editing ----------

    togglePresetField(field: 'requireName' | 'requireNickname' | 'requireEmail' | 'requireSocial') {
        const form = this.presetForm();
        if (!form) return;
        this.editPreset({ ...form, [field]: !form[field] });
    }

    setReplySubject(lang: string, value: string) {
        this.setFormTextMap('responseEmailSubject', lang, value);
    }

    setReplyBody(lang: string, value: string) {
        this.setFormTextMap('responseEmailBody', lang, value);
    }

    /** Shared by both reply fields: a blank clears the entry rather than storing an empty string. */
    private setFormTextMap(key: 'responseEmailSubject' | 'responseEmailBody', lang: string, value: string) {
        const form = this.presetForm();
        if (!form) return;
        const next = { ...form[key] };
        if (value.trim()) next[lang] = value.trim(); else delete next[lang];
        this.editPreset({ ...form, [key]: next });
    }

    setIntro(lang: string, intro: string) {
        const form = this.presetForm();
        if (!form) return;
        const next = { ...form.intro };
        if (intro.trim()) next[lang] = intro;
        else delete next[lang];
        this.editPreset({ ...form, intro: next });
    }

    addQuestion() {
        const form = this.presetForm();
        if (!form) return;
        const q: FormQuestionEdit = { id: newQuestionId(), label: {}, type: 'text', required: false, options: [] };
        this.editPreset({ ...form, questions: [...form.questions, q] });
    }

    updateQuestion(id: string, patch: Partial<FormQuestionEdit>) {
        const form = this.presetForm();
        if (!form) return;
        this.editPreset({ ...form, questions: form.questions.map(q => q.id === id ? { ...q, ...patch } : q) });
    }

    async removeQuestion(id: string) {
        if (!await this.confirmation.confirm(this.t().common.removeAuthoredContentConfirm)) return;
        const form = this.presetForm();
        if (!form) return;
        this.editPreset({ ...form, questions: form.questions.filter(q => q.id !== id) });
    }

    setQuestionLabel(id: string, lang: string, value: string) {
        const q = this.presetForm()?.questions.find(x => x.id === id);
        if (!q) return;
        const label = { ...q.label };
        if (value.trim()) label[lang] = value;
        else delete label[lang];
        this.updateQuestion(id, { label });
    }

    setQuestionType(id: string, type: RegistrationQuestionType) {
        const q = this.presetForm()?.questions.find(x => x.id === id);
        if (!q) return;
        // An optional consent checkbox isn't a meaningful concept — forced on here (Core's Parse
        // forces it again server-side, so a hand-edited/older blob can't bypass it either).
        // Switching to a choice type seeds two empty option rows so there's something to type into.
        const options = (type === 'choice' || type === 'multi') && q.options.length === 0
            ? [{ id: newOptionId(), label: {} }, { id: newOptionId(), label: {} }]
            : q.options;
        if (type === 'consent') { this.updateQuestion(id, { type, required: true, options }); return; }
        // T-031 — a required block nobody can fill in is a form that cannot be submitted; Core's
        // Parse forces this too, so a hand-edited blob can't bypass it either.
        if (type === 'static') { this.updateQuestion(id, { type, required: false, options }); return; }
        this.updateQuestion(id, { type, options });
    }

    setQuestionImage(id: string, url: string) {
        this.updateQuestion(id, { imageUrl: url.trim() || null });
    }

    addOption(qId: string) {
        const q = this.presetForm()?.questions.find(x => x.id === qId);
        if (!q) return;
        this.updateQuestion(qId, { options: [...q.options, { id: newOptionId(), label: {} }] });
    }

    async removeOption(qId: string, optId: string) {
        if (!await this.confirmation.confirm(this.t().common.removeAuthoredContentConfirm)) return;
        const q = this.presetForm()?.questions.find(x => x.id === qId);
        if (!q) return;
        this.updateQuestion(qId, { options: q.options.filter(o => o.id !== optId) });
    }

    setOptionLabel(qId: string, optId: string, lang: string, value: string) {
        const q = this.presetForm()?.questions.find(x => x.id === qId);
        if (!q) return;
        this.updateQuestion(qId, {
            options: q.options.map(o => {
                if (o.id !== optId) return o;
                const label = { ...o.label };
                if (value.trim()) label[lang] = value;
                else delete label[lang];
                return { ...o, label };
            }),
        });
    }

    // ---------- Answer distribution (N10) ----------

    // Only closed questions have a distribution worth drawing — free text would produce as many
    // slices as submissions.
    chartQuestions(): RegistrationQuestion[] {
        return (this.regForm()?.questions ?? []).filter(q => q.type === 'choice' || q.type === 'multi');
    }

    distribution(q: RegistrationQuestion): PieSlice[] {
        // Stored answers are option ids (ADR-060), so "Да" picked on the RU form and "Yes" on
        // the EN one land in the same bucket; the bucket is displayed under the primary label.
        // Pre-v2 rows stored the label text itself, which simply misses the map and shows as-is.
        const labelById = new Map((q.options ?? []).map(o => [o.id, o.label]));
        const counts = new Map<string, number>();
        for (const r of this.registrations()) {
            if (!r.answersJson) continue;
            let parsed: Record<string, string>;
            try {
                parsed = JSON.parse(r.answersJson) as Record<string, string>;
            } catch {
                continue;
            }
            const raw = parsed[q.id];
            if (raw === undefined) continue;
            const values = q.type === 'multi' ? splitMultiAnswer(raw) : [String(raw)];
            for (const v of values) {
                if (!v.trim()) continue;
                const label = labelById.get(v) ?? v;
                counts.set(label, (counts.get(label) ?? 0) + 1);
            }
        }

        const ordered = [...counts.entries()].sort((a, b) => b[1] - a[1]);
        // Six is the ceiling on distinguishable series; anything past it folds into one "Other"
        // slice rather than inventing a seventh colour.
        const head = ordered.slice(0, SERIES_COUNT - 1);
        const tail = ordered.slice(SERIES_COUNT - 1);
        const slices = head.map(([label, count]) => ({ label, count }));
        if (tail.length) slices.push({ label: this.t().manager.forms.other, count: tail.reduce((sum, [, c]) => sum + c, 0) });

        const total = slices.reduce((sum, s) => sum + s.count, 0);
        if (total === 0) return [];

        // One angle pass, so the arcs and the legend can never disagree about who owns what.
        let angle = -Math.PI / 2;
        return slices.map((s, i) => {
            const sweep = (s.count / total) * Math.PI * 2;
            const slice: PieSlice = {
                label: s.label,
                count: s.count,
                percent: Math.round((s.count / total) * 100),
                color: `var(--series-${(i % SERIES_COUNT) + 1})`,
                path: arcPath(angle, angle + sweep),
            };
            angle += sweep;
            return slice;
        });
    }

    // The strip switches what the body shows and never navigates. The feedback tally rides the
    // Posts tab as its badge, so the hide-at-zero and 99+ rules come from the one function that
    // owns them.
    tabs(): IndexTabItem[] {
        const t = this.t().manager;
        return [
            {
                id: 'posts', label: t.tabs.posts,
                badge: this.feedback.newComments() + this.feedback.newReactions(),
                badgeTitle: t.newSinceLastLook,
            },
            { id: 'stats', label: t.tabs.stats },
            { id: 'forms', label: t.tabs.forms },
        ];
    }

    pickTab(id: string) {
        if (MANAGER_TABS.includes(id as ManagerTab)) this.setTab(id as ManagerTab);
    }

    badgeOf(item: IndexTabItem): string {
        return indexTabBadgeLabel(item.badge);
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

    /** Exclusive: the shelf describes the selected post, or the library, and never both. */
    inspectorScope(): SpecScope {
        return this.selected() ? 'selection' : 'document';
    }

    inspectorTitle(): string {
        const t = this.t().manager.inspector;
        return this.inspectorScope() === 'selection' ? t.post : t.library;
    }

    // The count slot names the object only when the title does not already: «ПОСТ пост» is one
    // word said twice.
    inspectorScopeWord(): string {
        const t = this.t().manager.inspector;
        const word = this.inspectorScope() === 'selection' ? t.scopePost : t.scopeLibrary;
        return word.toLowerCase() === this.inspectorTitle().toLowerCase() ? '' : word;
    }

    /** Chalked on the top edge of the sheet: which post is lying on it, and how it stands. */
    sheetLabel(): string {
        const d = this.selected();
        return d ? (d.title || this.t().drafts.untitled) : this.t().manager.crumb;
    }

    sheetMeta(): string {
        const d = this.selected();
        if (!d) return '';
        return `${this.publishStateLabel(d)} · ${this.allLanguages(d).map(l => l.toUpperCase()).join(' ')}`;
    }

    presetSheetLabel(): string {
        const preset = this.presets().find(p => p.id === this.selectedPresetId());
        return preset?.name || this.t().manager.tabs.forms;
    }

    presetSheetMeta(): string {
        const form = this.presetForm();
        return form ? this.presetLangs().map(l => l.toUpperCase()).join(' ') : '';
    }

    publishedCount(): number {
        return this.postPool().filter(d => d.isBlogPublished).length;
    }

    privateCount(): number {
        return this.postPool().filter(d => d.isPrivate).length;
    }

    archivedCount(): number {
        return this.postPool().filter(d => d.isArchived).length;
    }

    pendingScheduleCount(): number {
        return this.scheduled().filter(p => p.status === 'Pending').length;
    }

    private restoreCollectionQuery() {
        const params = this.route.snapshot.queryParamMap;
        this.search.set(params.get('q') ?? '');
        const state = params.get('status');
        if (state && ['live', 'draft', 'scheduled', 'archived'].includes(state))
            this.stateFilter.set(state as PostStateFilter);
        const visibility = params.get('visibility');
        if (visibility === 'public' || visibility === 'private')
            this.visibilityFilter.set(visibility);
        const project = params.get('project');
        if (project) this.projectFilter.set(project);
        const language = params.get('language');
        if (language) this.languageFilter.set(language);
        const sort = params.get('sort');
        const direction = params.get('dir');
        if (sort && ['published', 'updated', 'title', 'activity'].includes(sort))
            this.postSort.set(sort as PostSort);
        if (direction === 'asc' || direction === 'desc') this.postSortDirection.set(direction);
        this.presetSearch.set(params.get('formq') ?? '');
        const presetLanguage = params.get('formlanguage');
        if (presetLanguage) this.presetLanguageFilter.set(presetLanguage);
        const presetSort = params.get('formsort');
        const presetDirection = params.get('formdir');
        if (presetSort && ['created', 'name', 'questions'].includes(presetSort))
            this.presetSort.set(presetSort as PresetSort);
        if (presetDirection === 'asc' || presetDirection === 'desc')
            this.presetSortDirection.set(presetDirection);
    }

    private syncCollectionQuery() {
        const selected = this.selectedId();
        void this.router.navigate([], {
            relativeTo: this.route,
            replaceUrl: true,
            queryParamsHandling: 'merge',
            queryParams: {
                tab: this.tab() === 'posts' ? null : this.tab(),
                q: this.search().trim() || null,
                status: this.stateFilter() === 'all' ? null : this.stateFilter(),
                visibility: this.visibilityFilter() === 'all' ? null : this.visibilityFilter(),
                project: this.projectFilter() === 'all' ? null : this.projectFilter(),
                language: this.languageFilter() === 'all' ? null : this.languageFilter(),
                sort: this.postSort() === 'published' ? null : this.postSort(),
                dir: this.postSort() === 'published' && this.postSortDirection() === 'desc'
                    ? null : this.postSortDirection(),
                formq: this.presetSearch().trim() || null,
                formlanguage: this.presetLanguageFilter() === 'all' ? null : this.presetLanguageFilter(),
                formsort: this.presetSort() === 'created' ? null : this.presetSort(),
                formdir: this.presetSort() === 'created' && this.presetSortDirection() === 'desc'
                    ? null : this.presetSortDirection(),
                ...(selected ? { draft: selected } : { draft: null }),
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

const SERIES_COUNT = 6;
const PIE_RADIUS = 46;
const PIE_CENTER = 50;

export interface PieSlice {
    label: string;
    count: number;
    percent: number;
    color: string;
    path: string;
}

function splitMultiAnswer(value: string): string[] {
    const trimmed = (value ?? '').trim();
    if (!trimmed.startsWith('[')) return trimmed ? [trimmed] : [];
    try {
        const parsed = JSON.parse(trimmed);
        return Array.isArray(parsed) ? parsed.map(String).filter(v => v.trim().length > 0) : [trimmed];
    } catch {
        return [trimmed];
    }
}

// A single slice covering the whole circle can't be drawn as an arc (start and end coincide, so
// the path collapses) — it becomes two half-circle arcs instead.
function arcPath(start: number, end: number): string {
    const full = end - start >= Math.PI * 2 - 1e-6;
    if (full) {
        const left = `${PIE_CENTER - PIE_RADIUS} ${PIE_CENTER}`;
        const right = `${PIE_CENTER + PIE_RADIUS} ${PIE_CENTER}`;
        return `M ${left} A ${PIE_RADIUS} ${PIE_RADIUS} 0 1 1 ${right} A ${PIE_RADIUS} ${PIE_RADIUS} 0 1 1 ${left} Z`;
    }
    const x1 = PIE_CENTER + PIE_RADIUS * Math.cos(start);
    const y1 = PIE_CENTER + PIE_RADIUS * Math.sin(start);
    const x2 = PIE_CENTER + PIE_RADIUS * Math.cos(end);
    const y2 = PIE_CENTER + PIE_RADIUS * Math.sin(end);
    const largeArc = end - start > Math.PI ? 1 : 0;
    return `M ${PIE_CENTER} ${PIE_CENTER} L ${x1.toFixed(2)} ${y1.toFixed(2)} `
        + `A ${PIE_RADIUS} ${PIE_RADIUS} 0 ${largeArc} 1 ${x2.toFixed(2)} ${y2.toFixed(2)} Z`;
}
