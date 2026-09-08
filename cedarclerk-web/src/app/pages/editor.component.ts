import { ConfirmationService } from '../core/confirmation.service';
import {
    AfterViewInit, Component, ElementRef, OnDestroy,
    ViewChild, computed, effect, inject, signal, untracked
} from '@angular/core';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { Editor } from '@tiptap/core';
import { EditorState, NodeSelection, PluginKey, TextSelection } from '@tiptap/pm/state';
import Suggestion from '@tiptap/suggestion';
import { Node as PMNode, Slice } from '@tiptap/pm/model';
import StarterKit from '@tiptap/starter-kit';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { AuthService } from '../core/auth.service';
import {
    DraftsService, DraftMeta, TranslationMeta, TranslationFull, AiEditKind, AiEditResult, PostInvite,
    RegistrationForm, parseRegistrationForm, WATERMARK_MAX_LENGTH,
    DRAFT_TITLE_MAX, EMPTY_DOC, AI_OPERATION_TIMEOUT_MS, AUTO_TRANSLATE_TIMEOUT_MS,
    SaveGuards, SaveRefusal, saveRefusalOf,
    CtaButton, parseCtaButtons, CTA_BUTTON_MAX, CTA_TEXT_MAX,
} from '../core/drafts.service';
import { FormPresetsService, FormPreset } from '../core/form-presets.service';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { GlossaryTermFormComponent } from '../shared/glossary-term-form.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { PlanLockComponent } from '../shared/plan-lock.component';
import { LocationInputComponent } from '../shared/location-input.component';
import { LanguageMenuComponent } from '../shared/language-menu.component';
import { DraftGlossaryTerm, GlossaryService, GlossaryTermInput } from '../core/glossary.service';
import { NgTemplateOutlet } from '@angular/common';
import { PostsService, PostFormat, CompressionLevel, UpdatePreview, PreflightLanguage } from '../core/posts.service';
import { PublishService, PublishAccount, PublishCapabilities, PublishJob, ThreadPart } from '../core/publish.service';
import { DocumentKindCounts, documentKinds } from '../core/document-kinds';
import { PublishMatrixComponent } from '../shared/publish-matrix.component';
import { PreviewCheck, PreviewChecksComponent } from './editor-preview/preview-checks.component';
import { DestinationState } from './editor-preview/state-tag.component';
import { DestinationCardComponent } from './editor-publish/destination-card.component';
import { PublishStep, PublishStepState, PublishStepperComponent, publishStepStates } from './editor-publish/publish-stepper.component';
import { matrixWarningCount } from './editor-publish/publish-readiness';
import { LinksService } from '../core/links.service';
import { BillingService } from '../core/billing.service';
import { DraftRevision, DraftRevisionDetail, RevisionDiff } from '../core/drafts.service';
import { plainTextOf } from '../core/cedar-text.util';
import { DEFAULT_PRIMARY_LANGUAGE, CONTENT_LANGUAGES, endonymOf } from '../core/languages';
import { ChannelsService, Channel, BestTimeSlot } from '../core/channels.service';
import { Table } from '@tiptap/extension-table';
import { TableRow } from '@tiptap/extension-table-row';
import { TableHeader } from '@tiptap/extension-table-header';
import { TableCell } from '@tiptap/extension-table-cell';
import { TaskList } from '@tiptap/extension-task-list';
import { TaskItem } from '@tiptap/extension-task-item';
import { Mathematics } from '@tiptap/extension-mathematics';
import { TextAlign } from '@tiptap/extension-text-align';
import { AssetMeta, AssetsService, DraftAsset } from '../core/assets.service';
import { VideoNode } from '../tiptap-extensions/video-node';
import { AudioNode } from '../tiptap-extensions/audio-node';
import { CarouselNode } from '../tiptap-extensions/carousel-node';
import { CollageNode } from '../tiptap-extensions/collage-node';
import { SpoilerMark } from '../tiptap-extensions/spoiler-mark';
import { DateTimeNode } from '../tiptap-extensions/datetime-node';
import { ToggleNode } from '../tiptap-extensions/toggle-node';
import { PollNode } from '../tiptap-extensions/poll-node';
import { ImageNode } from '../tiptap-extensions/image-node';
import { FootnoteNode } from '../tiptap-extensions/footnote-node';
import { WikiLinkNode, WIKILINK_OPEN_EVENT } from '../tiptap-extensions/wikilink-node';
import { AnnotationNode } from '../tiptap-extensions/annotation-node';
import { TableOfContentsNode } from '../tiptap-extensions/table-of-contents-node';
import { YoutubeNode, extractYouTubeId } from '../tiptap-extensions/youtube-node';
import { LayoutShortcuts } from '../tiptap-extensions/layout-shortcuts';
import { ExpandableBlockquote } from '../tiptap-extensions/expandable-blockquote';
import { PopoverComponent } from '../shared/popover.component';
import { ModalComponent } from '../shared/modal.component';
import { AppearanceService, SHEET_WIDTH_PX, TYPEFACE_STACK, MAX_TABLE_SIZE } from '../core/appearance.service';
import { EMOJI_GROUPS, EmojiGroup, searchEmoji } from '../core/emoji';
import { TagUsageService } from '../core/tag-usage.service';
import { TagPickerComponent } from '../shared/tag-picker.component';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { SeriesPickerComponent } from '../shared/series-picker.component';
import { MediaPickerComponent } from '../shared/media-picker.component';
import { LibraryAsset } from '../core/assets.service';
import { FormRefComponent } from '../shared/form-ref.component';
import { Preset, PresetsService, parseExportConfig } from '../core/presets.service';
import { httpErrorMessage } from '../core/http-error.util';
import { pseudoProgress } from '../core/pseudo-progress.util';
import { BrandIconComponent, BrandIconName } from '../shared/brand-icon.component';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';
import { avatarFill, avatarInitial } from '../core/avatar-color.util';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';
import {
    ProjectsService, ProjectSummary, DocumentType, DOCUMENT_TYPES, DOCUMENT_TYPE_ICONS, isPublishableType,
} from '../core/projects.service';
import { CurrentProjectService } from '../core/current-project.service';
import { PreviewService } from '../core/preview.service';
import { DocumentFrameComponent, DocumentTab, DocumentTabItem } from '../shell/document-frame.component';
import { HeaderMeta } from '../shell/page-header.component';
import { EditorPreviewComponent, PreviewDraftFacts } from './editor-preview/editor-preview.component';
import { STRIP_GROUP_IDS } from '../core/toolbar-layout';
import { ToolbarFit, fitToolbar } from '../core/toolbar-fit';
import { NodeLike, SelectionKind, SelectionSpec, describeSelection, mediaPathOf } from '../core/selection-spec';
import { OutlineEntry, OutlineNodeLike, buildOutline, topLevelStart } from '../core/document-outline';
import { DocumentOutlineComponent } from '../shared/document-outline.component';

// FI2.11 — how long the "published" confirmation with its links stays up.

// Rounds a date up to the next boundary of `minutes` (e.g. 05:27 + 5min -> 05:30)
function ceilToMinutes(date: Date, minutes: number): Date {
    const ms = minutes * 60_000;
    return new Date(Math.ceil((date.getTime() + 1) / ms) * ms);
}

// Formats a Date as the local "YYYY-MM-DDTHH:mm" string <input type="datetime-local"> expects
function toDatetimeLocalValue(date: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

// ADR-098 — which channel each language was last sent to. A browser preference, not draft state:
// it saves re-picking the same two channels on every post, and a stale entry costs nothing because
// a channel that is no longer in the connected list simply doesn't match any picker button.
const TELEGRAM_CHANNEL_PREFS_KEY = 'cedar.tgChannelByLang';

function loadTelegramChannelPrefs(): Record<string, string> {
    try {
        const parsed = JSON.parse(localStorage.getItem(TELEGRAM_CHANNEL_PREFS_KEY) ?? 'null');
        if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {};
        return Object.fromEntries(
            Object.entries(parsed as Record<string, unknown>).filter(([, v]) => typeof v === 'string')) as Record<string, string>;
    } catch {
        return {};
    }
}

function saveTelegramChannelPrefs(map: Record<string, string>) {
    try {
        localStorage.setItem(TELEGRAM_CHANNEL_PREFS_KEY, JSON.stringify(map));
    } catch { /* private mode / quota — the mapping just doesn't survive the tab */ }
}

type SaveState = 'saved' | 'saving' | 'dirty' | 'error';

// ─── The publish progress checklist ───────────────────────────────────────────────────────────
// One modal that watches a publication like a test run: every step (save, blog, each Telegram
// language) is a row with a live status, a thread unfolds into numbered part chips, a failure
// shows the FIRST broken part's error — the root cause — rather than the last cascade message.
type PublishRunStatus = 'waiting' | 'running' | 'done' | 'failed';

/** The networks that derive a short post rather than taking the document (ADR-077). */
type MicroNetwork = 'bluesky' | 'x' | 'discord';
/** T-318 — stores with no publish API: the document renders to their own markup and goes out
 *  through the clipboard, never through a PublishJob. */
type CopyTarget = 'steam' | 'itch';
type UnsupportedDestination = 'instagram' | 'threads' | 'youtube';
type ExportDestination = 'blog' | 'telegram' | MicroNetwork | CopyTarget | UnsupportedDestination;
/** ADR-096 — an announcement carrying a link, or the document itself as a reply chain. */
type MicroMode = 'link' | 'thread';

interface PublishRunPart {
    index: number;
    status: PublishRunStatus;
}

interface PublishRunStep {
    id: string;
    label: string;
    status: PublishRunStatus;
    error?: string;
    link?: { label: string; url: string };
    /** Thread parts (T-106), present only when this step sends more than one message. */
    parts?: PublishRunPart[];
}

const AUTOSAVE_DEBOUNCE_MS = 1200;
// The structure shelf rebuilds once a burst of typing settles, on the same interval and for the
// same reason as the RU diff gutter beside it (ADR-162 clause 2).
const OUTLINE_DEBOUNCE_MS = 200;
// T-018.6 — widening gaps, then the manual "retry" button takes over rather than hammering on.
const SAVE_RETRY_DELAYS_MS = [2000, 5000, 15000];
// T-018.2 — the browser caps a keepalive body at 64KB; Cyrillic is 2 bytes per character, so this
// stays comfortably under it in the worst case rather than at the theoretical edge.
const KEEPALIVE_MAX_CHARS = 30_000;

// Distinguishes "the client gave up polling" from any other rejection in pollAiJob's callers,
// same role TimeoutError used to play for the old RxJS-based autoTranslate$/aiEdit$.
class AiJobTimeoutError extends Error {}


// The glyph the block chip leads with for a picked node; the paragraph/heading pair is derived.
const BLOCK_CHIP_ICONS: Partial<Record<SelectionKind, IconName>> = {
    image: 'image', carousel: 'images', collage: 'images', video: 'video-camera', audio: 'waveform',
    youtube: 'video-camera', table: 'table', codeBlock: 'code-block', poll: 'check-square',
    toggle: 'list-dashes', annotation: 'chat-teardrop', footnote: 'text-superscript',
    wikilink: 'link', datetime: 'clock',
};

// Extra timezones shown alongside the local time when scheduling a post; will move to user settings later
const EXTRA_TIMEZONES: { label: string; zone: string }[] = [
    { label: 'MSK', zone: 'Europe/Moscow' },
    { label: 'PT', zone: 'America/Los_Angeles' },
];

// Cycled (by elapsed seconds, not a separate timer) while auto-translate is running, so the
// empty-EN-state screen shows visible progress instead of a static "Translating…" label.
const TRANSLATE_STATUS_MESSAGES = [
    'Reading your draft…',
    'Translating…',
    'Adapting tone and idioms…',
    'Double-checking terminology…',
    'Polishing the phrasing…',
    'Almost there…',
];

// Same cycling-caption idea as TRANSLATE_STATUS_MESSAGES, applied to the two other genuinely
// multi-step "long" async actions in the export flow — a bare spinner doesn't tell you whether
// a slow publish (compressing large camera photos, then talking to Telegram) is stuck or working.
const EXPORT_STATUS_MESSAGES = [
    'Preparing post…',
    'Compressing large photos…',
    'Sending to Telegram…',
    'Almost done…',
];

const BLOG_STATUS_MESSAGES = [
    'Rendering page…',
    'Publishing…',
    'Almost done…',
];

interface UploadItem {
    id: number;
    name: string;
    progress: number;
    error?: string;
}

const DETAILS_KEY = 'cedar-editor-details';

function readDetailsPreference(): boolean {
    try { return localStorage.getItem(DETAILS_KEY) === '1'; } catch { return false; }
}

@Component({
    selector: 'app-editor',
    imports: [IconComponent, BrandIconComponent, FormsModule, ZonedDatePipe, NgTemplateOutlet, RouterLink, PopoverComponent, ModalComponent, TagPickerComponent, FolderPickerComponent, SeriesPickerComponent, MediaPickerComponent, FormRefComponent, GlossaryTermFormComponent,
        WorktopComponent, ShelfPanelComponent, SpecRowComponent, LeafTagComponent, StampBadgeComponent,
        DocumentOutlineComponent, PlanLockComponent, LocationInputComponent, LanguageMenuComponent,
        DocumentFrameComponent, EditorPreviewComponent, PublishMatrixComponent, PreviewChecksComponent,
        DestinationCardComponent, PublishStepperComponent, ButtonComponent],
    templateUrl: 'editor.component.html',
    styleUrls: ['editor.component.css', 'editor-toolbar.css', 'editor-workspace.css', 'editor-publish.css', 'editor-dialogs.css']
})
export class EditorComponent implements AfterViewInit, OnDestroy {
    private readonly confirmation = inject(ConfirmationService);
    auth = inject(AuthService);
    appearance = inject(AppearanceService);
    private draftsApi = inject(DraftsService);
    private presetsApi = inject(FormPresetsService);
    // T-331 — the three-kind preset store; this screen reads only its export kind.
    private docPresetsApi = inject(PresetsService);
    feedback = inject(CommentsService);
    t = inject(LocaleService).t;
    private route = inject(ActivatedRoute);
    private assets = inject(AssetsService);
    private tagUsageApi = inject(TagUsageService);
    private router = inject(Router);
    private previewApi = inject(PreviewService);
    private currentProject = inject(CurrentProjectService);

    // ─── The document frame (ADR-239 clause 9, CONTRACT §D3) ─────────────────────────────────
    // One document, three tabs, addressed by ?tab= (ADR-242): Write is the bare URL, Preview and
    // Publish / Export are real selected states a link can land on.
    private readonly tabParam = toSignal(this.route.queryParamMap.pipe(map(p => p.get('tab'))),
        { initialValue: this.route.snapshot.queryParamMap.get('tab') });
    readonly tab = computed<DocumentTab>(() => {
        const param = this.tabParam();
        return param === 'preview' || param === 'publish' ? param : 'write';
    });
    readonly frameTabs = computed<DocumentTabItem[]>(() => {
        const t = this.t().editor.tabs;
        const compact = this.t().editor.tabsCompact;
        return [
            { id: 'write', label: t.write, compactLabel: compact.write, icon: 'pencil-simple' },
            { id: 'preview', label: t.preview, compactLabel: compact.preview, icon: 'eye' },
            { id: 'publish', label: t.publish, compactLabel: compact.publish, icon: 'upload-simple' },
        ];
    });
    inspectorOpen = signal(readDetailsPreference());

    toggleDetails() {
        this.inspectorOpen.update(open => !open);
        try { localStorage.setItem(DETAILS_KEY, this.inspectorOpen() ? '1' : '0'); } catch { /* private mode */ }
    }
    /** Bumped after every successful save, so the Preview tab follows the stored document. */
    savedVersion = signal(0);
    private lastSavedAt = signal<number | null>(null);
    /** Ticks so the footer's "saved N min ago" moves without a save. */
    private clock = signal(Date.now());
    private clockTimer?: ReturnType<typeof setInterval>;
    projectSummaries = signal<ProjectSummary[]>([]);
    testSendBusy = signal(false);

    setTab(id: DocumentTab) {
        void this.router.navigate([], {
            relativeTo: this.route,
            queryParams: { tab: id === 'write' ? null : id },
            queryParamsHandling: 'merge',
        });
    }

    private currentMeta(): DraftMeta | undefined {
        return this.drafts().find(d => d.id === this.currentId());
    }

    private draftProjectSummary(): ProjectSummary | null {
        const id = this.currentMeta()?.projectId || this.currentProject.id();
        return id ? this.projectSummaries().find(p => p.id === id) ?? null : null;
    }

    frameKicker(): string {
        const summary = this.draftProjectSummary();
        if (!summary) return '';
        const kind = this.t().projects.projectTypes[summary.createdFromPreset]?.name ?? '';
        return [summary.name, kind, this.t().editor.frame.documents(summary.documentCount)].filter(Boolean).join(' · ');
    }

    statusTag(): HeaderMeta {
        const t = this.t().editor;
        if (this.isLive()) return { text: t.state.live, tone: 'ok' };
        if (this.currentMeta()?.scheduled) return { text: t.frame.scheduled, tone: 'warn' };
        return { text: t.frame.draft, tone: 'muted' };
    }

    frameSaveState(): 'saved' | 'saving' | 'error' {
        switch (this.saveState()) {
            case 'saved': return 'saved';
            case 'error': return 'error';
            default: return 'saving';
        }
    }

    dateLabel(): string {
        const iso = this.currentMeta()?.updatedAt;
        if (!iso) return '';
        const date = new Date(iso);
        if (!Number.isFinite(date.getTime())) return '';
        return date.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
    }

    footerText(): string {
        const t = this.t().editor;
        const parts: string[] = [];
        const saved = this.lastSavedAt();
        if (this.saveState() === 'saved' && saved !== null) {
            const minutes = Math.floor((this.clock() - saved) / 60_000);
            parts.push(minutes < 1 ? t.frame.savedJustNow
                : minutes < 60 ? t.frame.savedMinutesAgo(minutes)
                    : t.frame.savedHoursAgo(Math.floor(minutes / 60)));
        }
        if (this.appearance.prefs().showWordCount) {
            parts.push(t.words(this.wordCount()));
            parts.push(t.chars(this.charCount()));
        }
        return parts.join(' · ');
    }

    /** What the Preview tab is told; a computed so the tab refetches on a change, not on every tick. */
    readonly previewFacts = computed<PreviewDraftFacts | null>(() => {
        const id = this.currentId();
        if (!id) return null;
        this.savedVersion();
        const meta = this.drafts().find(d => d.id === id);
        const languages = Object.keys(this.translations());
        return {
            id,
            title: this.title,
            typeName: this.t().projects.docTypes[this.documentType()].name,
            isWorkingMaterial: this.isWorkingMaterial(),
            primaryLanguage: this.primaryLanguage,
            languages,
            staleLanguages: languages.filter(l => this.isStale(l)),
            coverImagePath: meta?.coverImagePath ?? null,
            blog: this.currentBlog(),
            isPrivate: this.isPrivate(),
            scheduled: meta?.scheduled ?? null,
            kinds: this.documentKindCounts(),
        };
    });

    /** The one channel a test send may go to (`.claude/rules/telegram-bot.md`); absent = no button. */
    readonly testChannel = computed(() => this.channels().find(c => c.username?.toLowerCase() === 'testingandfun') ?? null);

    async sendTest() {
        const id = this.currentId();
        const channel = this.testChannel();
        if (!id || !channel || this.testSendBusy()) return;
        this.testSendBusy.set(true);
        try {
            if (this.saveState() !== 'saved') await this.save();
            await this.posts.export(id, String(channel.telegramChatId), this.format, this.lang(), this.compressionLevel);
            this.showAiToast(this.t().editor.frame.testSent);
        } catch (e) {
            this.showAiToast(httpErrorMessage(e, this.t().editor.frame.testFailed));
        } finally {
            this.testSendBusy.set(false);
        }
    }

    /** Share preview: the existing link if there is one, a new one otherwise, copied either way. */
    async sharePreview() {
        const id = this.currentId();
        if (!id || this.previewLinkBusy()) return;
        if (!this.previewLinkUrl()) {
            try {
                const existing = await this.previewApi.previewLink(id);
                if (existing) this.previewLinkUrl.set(existing.url);
            } catch { /* fall through to creating one */ }
        }
        if (!this.previewLinkUrl()) await this.createPreviewLink();
        if (!this.previewLinkUrl()) { this.showAiToast(this.t().editor.frame.linkFailed); return; }
        await this.copyPreviewLink();
        this.showAiToast(this.t().editor.frame.linkCopied);
    }

    @ViewChild('editorHost') editorHost!: ElementRef<HTMLElement>;
    @ViewChild('toolStrip') toolStrip?: ElementRef<HTMLElement>;
    private editor?: Editor;

    // ─── The tool strip fits itself (ADR-150) ─────────────────────────────────────────────────
    // Group order is the catalogue's own; the fit only decides how many of them stay on the first
    // row and whether the captions are drawn. Nothing here is stored, and nothing is a width
    // breakpoint — ADR-147 forbids inventing one before the narrow screens are commissioned.
    protected readonly stripGroups = STRIP_GROUP_IDS;
    readonly toolbarFit = signal<ToolbarFit>({ rows: 1, captions: true, firstRow: STRIP_GROUP_IDS.length });
    readonly row1Groups = computed(() => this.stripGroups.slice(0, this.toolbarFit().firstRow));
    readonly row2Groups = computed(() =>
        this.toolbarFit().rows === 2 ? this.stripGroups.slice(this.toolbarFit().firstRow) : []);

    private stripObserver?: ResizeObserver;

    // Captions are measured by flipping the class on the element rather than through the signal:
    // both states have to be read in one layout pass, and a signal would put a change-detection
    // round trip between them.
    private measureToolbar() {
        const strip = this.toolStrip?.nativeElement;
        const line = strip?.querySelector<HTMLElement>('.tb-line');
        const lead = strip?.querySelector<HTMLElement>('.tb-lead');
        const trail = strip?.querySelector<HTMLElement>('.tb-trail');
        if (!strip || !line || !lead || !trail) return;

        const available = line.clientWidth;
        if (available === 0) return;

        const widths = () => this.stripGroups.map(id =>
            strip.querySelector<HTMLElement>(`[data-tb-group="${id}"]`)?.getBoundingClientRect().width ?? 0);

        const suppressed = strip.classList.contains('no-captions');
        strip.classList.remove('no-captions');
        const withCaptions = widths();
        strip.classList.add('no-captions');
        const withoutCaptions = widths();
        strip.classList.toggle('no-captions', suppressed);

        const next = fitToolbar({
            available,
            gap: parseFloat(getComputedStyle(line).columnGap) || 0,
            lead: lead.getBoundingClientRect().width,
            trail: trail.getBoundingClientRect().width,
            withCaptions,
            withoutCaptions,
        });

        // Two rows are taller than one, so committing an unchanged fit would re-enter through the
        // observer that watches the strip's own box.
        const now = this.toolbarFit();
        if (next.rows === now.rows && next.captions === now.captions && next.firstRow === now.firstRow) return;
        this.toolbarFit.set(next);
    }

    // A caption is translated, so its width moves with the locale and nothing about the strip's
    // own box changes when it does.
    private readonly stripLocale = effect(() => {
        this.t();
        setTimeout(() => this.measureToolbar());
    });

    // A deep link and a click arrive at the same state: whatever the Publish tab needs is loaded
    // when it becomes the selected one, for the draft that is open then.
    private readonly publishEntry = effect(() => {
        const entered = this.tab() === 'publish';
        const id = this.currentId();
        if (entered && id) untracked(() => void this.enterPublish());
    });

    // ─── Waiting on the publish queue (T-090) ─────────────────────────────────────────────────
    /**
     * Polls until every queued job has finished. A publish is no longer one HTTP request that
     * waits for a network to download media from us (ADR-080), so "is it done" is a question with
     * its own answer now — and one the browser can stop asking without cancelling anything.
     */
    private async awaitJobs(draftId: string, jobIds: string[],
                            onProgress?: (jobs: PublishJob[]) => void): Promise<PublishJob[]> {
        const pending = new Set(jobIds);
        const finished: PublishJob[] = [];
        // Ten minutes of polling: past that the sweeper owns the job, and the answer is on /posts.
        for (let i = 0; i < 400 && pending.size > 0; i++) {
            await new Promise(resolve => setTimeout(resolve, 1500));
            const { jobs } = await this.publishApi.jobs(draftId);
            // Every poll, not just the finishes — the progress checklist shows a thread's parts
            // ticking over one by one, which is the whole point of watching.
            onProgress?.(jobs);
            for (const job of jobs) {
                if (!pending.has(job.id)) continue;
                if (job.status === 'Pending' || job.status === 'Running') continue;
                pending.delete(job.id);
                finished.push(job);
            }
        }
        return finished;
    }

    // ─── The publish progress checklist ───────────────────────────────────────────────────────
    // Opened by publishAllConfirmed(), fed by exportDraft()/awaitJobs() as jobs move. Null-safe on
    // purpose: every updater is a no-op when the modal is not up.
    publishRun = signal<{ steps: PublishRunStep[]; finished: boolean } | null>(null);

    private runStart(steps: PublishRunStep[]) {
        this.publishRun.set({ steps, finished: false });
    }

    private runUpdate(id: string, patch: Partial<PublishRunStep>) {
        this.publishRun.update(run => run && {
            ...run,
            steps: run.steps.map(s => s.id === id ? { ...s, ...patch } : s),
        });
    }

    private runFinish() {
        this.publishRun.update(run => run && { ...run, finished: true });
    }

    runHasFailure(run: { steps: PublishRunStep[] }): boolean {
        return run.steps.some(s => s.status === 'failed');
    }

    runDoneParts(step: PublishRunStep): number {
        return step.parts?.filter(p => p.status === 'done').length ?? 0;
    }

    /**
     * The Telegram account row behind each ticked version's chosen chat (ADR-098). One networks
     * fetch for the whole publish rather than one per language — the answer is the same either way.
     */
    private async telegramTargetIds(langs: string[]): Promise<Record<string, string | null>> {
        const networks = await this.publishApi.networks();
        const telegram = networks.find(n => n.network === 'telegram');
        const resolve = (chatId: string): string | null => {
            const byRemoteId = telegram?.accounts.find(a => a.remoteId === chatId);
            if (byRemoteId) return byRemoteId.id;
            // The window also accepts "@name", while a target row is keyed by the numeric chat id.
            const channel = this.channels().find(c => '@' + (c.username ?? '') === chatId);
            return channel
                ? telegram?.accounts.find(a => a.remoteId === String(channel.telegramChatId))?.id ?? null
                : null;
        };
        return Object.fromEntries(langs.map(l => [l, resolve(this.chatIdFor(l))]));
    }

    // ─── Short-post networks: X and Bluesky (T-087/T-089/T-110, ADR-077/093/094/096) ──────────
    // The two behave identically from this window's side — an account, a mode, and per-language
    // text — so they are one block with the network as a parameter rather than two near-copies.
    // Connecting is not here at all any more: it lives in Settings → Integrations (ADR-095).
    private publishApi = inject(PublishService);
    private billingApi = inject(BillingService);
    readonly microNetworks: MicroNetwork[] = ['bluesky', 'x', 'discord'];
    // T-318 — Steam and itch.io left the unsupported list: their store editors take pasted
    // BBCode/HTML, so the window renders the text and hands it to the clipboard instead.
    readonly copyTargets: { id: CopyTarget; name: string; icon: BrandIconName }[] = [
        { id: 'steam', name: 'Steam', icon: 'steam' },
        { id: 'itch', name: 'itch.io', icon: 'itch' },
    ];
    readonly unsupportedDestinations: { id: UnsupportedDestination; name: string; icon: BrandIconName }[] = [
        { id: 'instagram', name: 'Instagram', icon: 'instagram' },
        { id: 'threads', name: 'Threads', icon: 'threads' },
        { id: 'youtube', name: 'YouTube', icon: 'youtube' },
    ];
    activeExportDestination = signal<ExportDestination>('blog');
    readonly microLimits: Record<MicroNetwork, number> = { bluesky: 300, x: 280, discord: 2000 };
    readonly microLabels: Record<MicroNetwork, string> = { bluesky: 'Bluesky', x: 'X', discord: 'Discord' };
    /** Discord never threads (ADR-131) — a webhook message has no reply structure to chain. */
    readonly microThreadable: Record<MicroNetwork, boolean> = { bluesky: true, x: true, discord: false };

    destBluesky = signal(false);
    destX = signal(false);
    destDiscord = signal(false);
    /** The last short-post failure, so the success toast stays honest about a partial publish. */
    microError = signal('');
    blueskyAccount = signal<PublishAccount | null>(null);
    xAccount = signal<PublishAccount | null>(null);
    discordAccount = signal<PublishAccount | null>(null);
    xCredits = signal<number | null>(null);

    /** Every network's capabilities as the server describes them — what the publish matrix reads (ADR-241). */
    networkCaps = signal<PublishCapabilities[]>([]);

    readonly connectedNetworks = computed<string[]>(() => {
        const list: string[] = [];
        if (this.channels().length) list.push('telegram');
        if (this.xAccount()) list.push('x');
        if (this.blueskyAccount()) list.push('bluesky');
        if (this.discordAccount()) list.push('discord');
        return list;
    });

    /** What the open document holds, re-counted after every save. */
    readonly documentKindCounts = computed<DocumentKindCounts>(() => {
        this.savedVersion();
        return documentKinds(this.editor ? JSON.stringify(this.editor.getJSON()) : '{}');
    });

    /**
     * ADR-096 — "announcement plus a link" and "the whole post as a thread" are two different
     * publications, not one with an option, so the window asks which rather than offering a
     * checkbox beside a text field the thread mode does not even read.
     */
    microMode = signal<Record<MicroNetwork, MicroMode>>({ bluesky: 'link', x: 'link', discord: 'link' });
    /** Parts per network for the currently previewed language; 0 while unknown. */
    microParts = signal<Record<MicroNetwork, number>>({ bluesky: 0, x: 0, discord: 0 });
    microCounting = signal<Record<MicroNetwork, boolean>>({ bluesky: false, x: false, discord: false });

    // Per network, per language. One field for "the current language" silently sent the same text
    // to every ticked version, which is the one thing a per-language override must not do.
    private microTexts: Record<MicroNetwork, Record<string, string>> = { bluesky: {}, x: {}, discord: {} };
    private microDirty: Record<MicroNetwork, Set<string>> = { bluesky: new Set(), x: new Set(), discord: new Set() };
    /** Which language's text the panel is editing — its own tab row, shown only when >1 is ticked. */
    microTextLang = signal<Record<MicroNetwork, string>>({ bluesky: DEFAULT_PRIMARY_LANGUAGE, x: DEFAULT_PRIMARY_LANGUAGE, discord: DEFAULT_PRIMARY_LANGUAGE });

    /**
     * ADR-100 — which versions actually go out on this network, a subset of the window's ticked
     * ones. Empty means "all of them", which is both the default and what the window did before:
     * one account with a bilingual audience is a real case, two tweets nobody asked for is not.
     */
    microLangs = signal<Record<MicroNetwork, string[]>>({ bluesky: [], x: [], discord: [] });

    microLangsOf(network: MicroNetwork): string[] {
        const picked = this.microLangs()[network].filter(l => this.exportLangs().includes(l));
        return picked.length ? picked : this.exportLangs();
    }

    isMicroLang(network: MicroNetwork, lang: string): boolean {
        return this.microLangsOf(network).includes(lang);
    }

    toggleMicroLang(network: MicroNetwork, lang: string) {
        const current = this.microLangsOf(network);
        const next = current.includes(lang) ? current.filter(l => l !== lang) : [...current, lang];
        // A ticked destination that sends nothing is not a choice the window can act on.
        if (!next.length) return;
        this.microLangs.update(map => ({ ...map, [network]: next }));
        this.syncMicroTextLang(network);
        if (this.microMode()[network] === 'thread') this.countMicroParts(network);
    }

    anyDestination(): boolean {
        return this.destBlog() || this.destTelegram() || this.destBluesky() || this.destX() || this.destDiscord();
    }

    /** The counter carved beside step 2's title (ADR-190). Shown as written, zero included. */
    tickedDestinationCount(): number {
        return [this.destBlog(), this.destTelegram(), this.destBluesky(), this.destX(), this.destDiscord()]
            .filter(Boolean).length;
    }

    account(network: MicroNetwork): PublishAccount | null {
        switch (network) {
            case 'x': return this.xAccount();
            case 'discord': return this.discordAccount();
            default: return this.blueskyAccount();
        }
    }

    // T-331 — one pick fills step 2. Languages the post does not have are ignored rather than
    // added: a preset saved when the post had three translations must not tick a fourth that was
    // never written. An empty language list in the preset means "leave the ticks alone".
    applyExportPreset(presetId: string) {
        const preset = this.exportPresets().find(p => p.id === presetId);
        if (!preset) return;
        const config = parseExportConfig(preset.configJson);

        this.destBlog.set(config.destinations.includes('blog'));
        this.destTelegram.set(config.destinations.includes('telegram'));
        for (const network of this.microNetworks) {
            this.destination(network).set(config.destinations.includes(network));
        }

        const available = [this.primaryLanguage, ...this.existingLanguages()];
        const langs = config.languages.filter(l => available.includes(l));
        if (langs.length) this.exportLangs.set(langs);
    }

    private async loadExportPresets() {
        try { this.exportPresets.set(await this.docPresetsApi.list('export')); }
        catch { this.exportPresets.set([]); }
    }

    destination(network: MicroNetwork) {
        switch (network) {
            case 'x': return this.destX;
            case 'discord': return this.destDiscord;
            default: return this.destBluesky;
        }
    }

    // ─── The Publish / Export workspace (ADR-242) ─────────────────────────────────────────────
    // Everything below is read off the ticks, the accounts and the checks the window already
    // held; nothing here is a second source of truth about what can publish.
    readonly publishDestinationIds: ExportDestination[] = ['blog', 'telegram', 'bluesky', 'x', 'discord'];

    isIncluded(destination: ExportDestination): boolean {
        switch (destination) {
            case 'blog': return this.destBlog();
            case 'telegram': return this.destTelegram();
            case 'bluesky': case 'x': case 'discord': return this.destination(destination)();
            default: return false;
        }
    }

    /** The checkbox's job and nothing else — though ticking a destination while none is inspected brings its settings forward. */
    includeDestination(destination: ExportDestination, on: boolean) {
        switch (destination) {
            case 'blog': this.destBlog.set(on); break;
            case 'telegram': this.destTelegram.set(on); break;
            case 'bluesky': case 'x': case 'discord': this.destination(destination).set(on); break;
            default: return;
        }
        if (on && !this.isIncluded(this.activeExportDestination())) this.activeExportDestination.set(destination);
    }

    destinationState(destination: ExportDestination): DestinationState | null {
        if (destination === 'steam' || destination === 'itch') return null;
        if (destination === 'instagram' || destination === 'threads' || destination === 'youtube') return 'unavailable';
        if (destination === 'blog') return this.isWorkingMaterial() ? 'blocking' : 'ready';
        if (destination === 'telegram') {
            if (!this.channels().length) return 'setup';
            if (this.isWorkingMaterial()) return 'blocking';
            if (this.destTelegram() && this.langsMissingChannel().length) return 'blocking';
            if (this.destTelegram() && this.visiblePublishIssues().some(i => i.blocking)) return 'blocking';
            return this.visiblePublishIssues().length ? 'warn' : 'ready';
        }
        const network = destination as MicroNetwork;
        const account = this.account(network);
        if (!account) return 'setup';
        if (this.isWorkingMaterial()) return 'blocking';
        if (this.destination(network)() && this.microOverLimit(network)) return 'blocking';
        if (network === 'x' && this.destX() && this.xCreditsShort()) return 'blocking';
        return account.lastError ? 'warn' : 'ready';
    }

    destinationMeta(destination: ExportDestination): string {
        const tx = this.t().editor;
        if (destination === 'blog') {
            return `${this.t().projects.docTypes[this.documentType()].name} · ${this.isPrivate() ? tx.state.private : tx.state.public}`;
        }
        if (destination === 'telegram') {
            if (!this.channels().length) return tx.exportModal.notConnected;
            const chosen = this.exportLangs().map(l => this.selectedChannelFor(l)?.title).filter(Boolean);
            return chosen.length ? [...new Set(chosen)].join(' · ') : this.channels()[0].title;
        }
        const account = this.account(destination as MicroNetwork);
        return account ? account.displayName : tx.exportModal.notConnected;
    }

    readyDestinationIds(): ExportDestination[] {
        return this.publishDestinationIds.filter(d => this.destinationState(d) === 'ready');
    }

    allReadySelected(): boolean {
        const ready = this.readyDestinationIds();
        return ready.length > 0 && ready.every(d => this.isIncluded(d));
    }

    selectAllReady(on: boolean) {
        for (const destination of this.readyDestinationIds()) this.includeDestination(destination, on);
    }

    /** The settings column opens on the first ticked destination that can run, else the first that can. */
    private settleActiveDestination() {
        const active = this.activeExportDestination();
        const isPublish = (this.publishDestinationIds as string[]).includes(active);
        if (isPublish && this.isIncluded(active)) return;
        if (this.activeCopyTarget() || this.activeUnsupportedDestination()) return;
        const ticked = this.publishDestinationIds.find(d => this.isIncluded(d) && this.destinationState(d) === 'ready')
            ?? this.publishDestinationIds.find(d => this.isIncluded(d));
        this.activeExportDestination.set(ticked ?? this.readyDestinationIds()[0] ?? 'blog');
    }

    activeDestinationTitle(): string {
        const active = this.activeExportDestination();
        const tx = this.t().editor;
        if (active === 'blog') return tx.publishTab.settingsFor(tx.exportModal.destinationBlog);
        if (active === 'telegram') return tx.publishTab.settingsFor(tx.exportModal.destinationTelegram);
        const micro = this.activeMicroNetwork();
        if (micro) return tx.publishTab.settingsFor(this.microLabels[micro]);
        const copy = this.activeCopyTarget();
        if (copy) return tx.publishTab.settingsFor(copy.name);
        const unavailable = this.activeUnsupportedDestination();
        return unavailable ? tx.publishTab.settingsFor(unavailable.name) : tx.exportModal.stepParams;
    }

    tickedNetworks(): string[] {
        const list: string[] = [];
        if (this.destTelegram()) list.push('telegram');
        for (const network of this.microNetworks) if (this.destination(network)()) list.push(network);
        return list;
    }

    matrixWarnings(): number {
        return matrixWarningCount(this.documentKindCounts(), this.networkCaps(), this.tickedNetworks(), this.t().matrix.notes);
    }

    publishSteps(): PublishStepState[] {
        return publishStepStates({
            languages: this.exportLangs().length,
            anyDestination: this.anyDestination(),
            settingsComplete: this.publishSettingsComplete(),
        });
    }

    /** The stepper is navigation: the region it names takes focus and scrolls into view. */
    focusPublishStep(step: PublishStep) {
        this.focusPublishRegion(`pub-${step}`);
    }

    private focusPublishRegion(id: string) {
        const el = document.getElementById(id);
        if (!el) return;
        el.scrollIntoView({ block: 'start', behavior: 'smooth' });
        el.focus({ preventScroll: true });
    }

    /** A check's own way out, where one exists: the inspector's Type row, the rack, a destination's settings. */
    fixCheck(id: string) {
        if (id === 'type') {
            if (!this.inspectorOpen()) this.toggleDetails();
            this.setTab('write');
            return;
        }
        if (id === 'destinations') { this.focusPublishRegion('pub-destinations'); return; }
        const destination = id.split(':')[0] as ExportDestination;
        if ((this.publishDestinationIds as string[]).includes(destination)) {
            this.activeExportDestination.set(destination);
            this.focusPublishRegion('pub-settings');
        }
    }

    // One list under the four headings both tabs share. Blocking is exactly what the contract
    // already refuses — the button reads publishSettingsComplete(), and these rows say why.
    publishChecks(): PreviewCheck[] {
        const tx = this.t().editor;
        const words = tx.publishTab;
        const settings = { label: tx.previewTab.checks.fix, route: '/settings', query: { tab: 'integrations' } };
        const rows: PreviewCheck[] = [];

        if (this.isWorkingMaterial()) {
            rows.push({
                id: 'type', label: tx.inspector.type, tone: 'blocking', fix: { label: tx.previewTab.checks.fix },
                detail: tx.exportModal.checkWorkingMaterial(this.t().projects.docTypes[this.documentType()].name),
            });
        }
        if (!this.anyDestination()) {
            rows.push({ id: 'destinations', label: words.noDestinationCheck, detail: words.noDestinationDetail, tone: 'blocking', fix: { label: tx.previewTab.checks.fix } });
        }
        if (this.destTelegram() && this.channels().length && this.langsMissingChannel().length) {
            rows.push({
                id: 'telegram:channel', label: words.channelMissing(this.missingChannelLabel()), detail: words.channelMissingDetail,
                tone: 'blocking', fix: { label: tx.previewTab.checks.fix },
            });
        }
        for (const network of this.microNetworks) {
            if (!this.destination(network)()) continue;
            if (!this.account(network)) {
                rows.push({ id: `${network}:account`, label: words.notConnected(this.microLabels[network]), detail: words.notConnectedDetail, tone: 'blocking', fix: settings });
            } else if (this.microOverLimit(network)) {
                rows.push({ id: `${network}:limit`, label: words.overLimit(this.microLabels[network]), detail: words.overLimitDetail, tone: 'blocking', fix: { label: tx.previewTab.checks.fix } });
            }
        }
        if (this.destX() && this.xCreditsShort()) {
            rows.push({ id: 'x:credits', label: words.creditsShort, detail: tx.exportModal.creditsShort(this.xCreditCost(), this.xCredits() ?? 0), tone: 'blocking' });
        }
        if (this.destTelegram()) {
            for (const issue of this.visiblePublishIssues()) {
                rows.push({
                    id: `telegram:${issue.code}`, label: 'Telegram', detail: this.publishIssueText(issue),
                    tone: issue.blocking ? 'blocking' : 'warn', fix: issue.blocking ? { label: tx.previewTab.checks.fix } : undefined,
                });
            }
        }
        for (const check of this.preflightWarnings()) {
            const lang = tx.exportModal.checksLanguage(check.language.toUpperCase());
            if (check.emptyVersion) rows.push({ id: `lang:${check.language}:empty`, label: lang, detail: tx.exportModal.checkEmptyVersion, tone: 'warn' });
            for (const dead of check.deadLinks) {
                rows.push({ id: `lang:${check.language}:${dead.url}`, label: lang, detail: tx.exportModal.checkDeadLink(dead.url, dead.status), tone: 'warn' });
            }
        }

        if (this.destBlog() && !this.isWorkingMaterial()) {
            rows.push({ id: 'blog:ready', label: words.readyBlog(this.isPrivate() ? tx.state.private : tx.state.public), detail: '', tone: 'ok' });
        }
        if (this.destTelegram() && this.destinationState('telegram') === 'ready') {
            rows.push({ id: 'telegram:ready', label: words.readyTelegram(this.destinationMeta('telegram')), detail: '', tone: 'ok' });
        }
        for (const network of this.microNetworks) {
            const account = this.account(network);
            if (this.destination(network)() && account && this.destinationState(network) === 'ready') {
                rows.push({ id: `${network}:ready`, label: words.readyNetwork(this.microLabels[network], account.displayName), detail: '', tone: 'ok' });
            }
        }

        if (!this.channels().length) rows.push({ id: 'telegram:setup', label: words.noChannels, detail: words.notConnectedDetail, tone: 'setup', fix: settings });
        for (const network of this.microNetworks) {
            if (!this.account(network)) rows.push({ id: `${network}:setup`, label: words.notConnected(this.microLabels[network]), detail: words.notConnectedDetail, tone: 'setup', fix: settings });
        }
        for (const unsupported of this.unsupportedDestinations) {
            rows.push({ id: `${unsupported.id}:unavailable`, label: words.unavailable(unsupported.name), detail: '', tone: 'unavailable' });
        }
        return rows;
    }

    publishWarningCount(): number {
        return this.publishChecks().filter(c => c.tone === 'warn').length;
    }

    exportLangsLabel(): string {
        return this.exportLangs().map(l => l.toUpperCase()).join(' + ');
    }

    scheduleSummary(): string {
        if (!this.schedulingActive()) return this.t().editor.publishTab.publishNow;
        const date = new Date(this.scheduledAt);
        return Number.isFinite(date.getTime())
            ? date.toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })
            : this.scheduledAt;
    }

    activeMicroNetwork(): MicroNetwork | null {
        const active = this.activeExportDestination();
        return this.microNetworks.includes(active as MicroNetwork) ? active as MicroNetwork : null;
    }

    activeUnsupportedDestination() {
        const active = this.activeExportDestination();
        return this.unsupportedDestinations.find(destination => destination.id === active) ?? null;
    }

    // ─── Copy destinations: Steam BBCode and itch.io HTML (T-318) ─────────────────────────────
    // Not a publish: the server renders the document into the store's own markup and the author
    // pastes it there. No checkbox, no PublishJob, never counted into anyDestination().
    copyTargetLang = signal<string>(DEFAULT_PRIMARY_LANGUAGE);
    copyText = signal('');
    copyTextLoading = signal(false);
    copyTextError = signal('');
    copyCopied = signal(false);
    private copyCopiedTimer?: ReturnType<typeof setTimeout>;
    private copyTextRequest = 0;

    activeCopyTarget() {
        const active = this.activeExportDestination();
        return this.copyTargets.find(target => target.id === active) ?? null;
    }

    /** The panel follows the window's ticked versions, like the short-post panels (ADR-100). */
    copyLangEffective(): string {
        const langs = this.exportLangs();
        return langs.includes(this.copyTargetLang()) ? this.copyTargetLang() : langs[0] ?? this.primaryLanguage;
    }

    selectCopyTarget(target: CopyTarget) {
        this.activeExportDestination.set(target);
        void this.loadCopyText();
    }

    setCopyTargetLang(lang: string) {
        this.copyTargetLang.set(lang);
        void this.loadCopyText();
    }

    private async loadCopyText() {
        const id = this.currentId();
        const target = this.activeCopyTarget();
        if (!id || !target) return;
        const request = ++this.copyTextRequest;
        this.copyTextLoading.set(true);
        this.copyTextError.set('');
        try {
            const res = await this.draftsApi.exportText(id, target.id, this.copyLangEffective());
            if (request === this.copyTextRequest) this.copyText.set(res.text);
        } catch (e) {
            if (request === this.copyTextRequest) {
                this.copyText.set('');
                this.copyTextError.set(httpErrorMessage(e, this.t().editor.exportModal.copyFailed));
            }
        } finally {
            if (request === this.copyTextRequest) this.copyTextLoading.set(false);
        }
    }

    async copyExportText() {
        const text = this.copyText();
        if (!text) return;
        try {
            await navigator.clipboard.writeText(text);
            this.copyCopied.set(true);
            clearTimeout(this.copyCopiedTimer);
            this.copyCopiedTimer = setTimeout(() => this.copyCopied.set(false), 2000);
        } catch {
            // No clipboard permission — the rendered text is on screen and selectable.
        }
    }

    /** The brand mark for a short-post network — X's is still served under the `twitter` key. */
    brandOf(network: MicroNetwork): 'twitter' | 'bluesky' | 'discord' {
        return network === 'x' ? 'twitter' : network;
    }

    microText(network: MicroNetwork): string {
        return this.microTexts[network][this.microTextLang()[network]] ?? '';
    }

    setMicroText(network: MicroNetwork, text: string) {
        const lang = this.microTextLang()[network];
        this.microTexts[network][lang] = text;
        this.microDirty[network].add(lang);
    }

    setMicroTextLang(network: MicroNetwork, lang: string) {
        this.microTextLang.update(map => ({ ...map, [network]: lang }));
    }

    setMicroMode(network: MicroNetwork, mode: MicroMode) {
        this.microMode.update(map => ({ ...map, [network]: mode }));
        if (mode === 'thread') this.countMicroParts(network);
    }

    /**
     * How many messages the thread would be. Counted for the first ticked version: the parts of a
     * translation differ by a few, and quoting a number per language would turn a cost estimate
     * into a table nobody reads.
     */
    private async countMicroParts(network: MicroNetwork) {
        const id = this.currentId();
        if (!id) return;
        this.microCounting.update(map => ({ ...map, [network]: true }));
        try {
            const preview = await this.publishApi.threadPreview(id, network, this.microLangsOf(network)[0]);
            this.microParts.update(map => ({ ...map, [network]: preview.parts.length }));
        } catch {
            // Without a count the mode is still valid — the server splits it either way; only the
            // "N posts, N credits" line goes missing.
            this.microParts.update(map => ({ ...map, [network]: 0 }));
        } finally {
            this.microCounting.update(map => ({ ...map, [network]: false }));
        }
    }

    /** Graphemes for Bluesky, t.co-weighted units for X, plain length for Discord. */
    microLength(network: MicroNetwork): number {
        switch (network) {
            case 'x': return this.xWeighted();
            case 'discord': return this.microText('discord').length;
            default: return this.blueskyGraphemes();
        }
    }

    microOverLimit(network: MicroNetwork): boolean {
        return this.microMode()[network] === 'link' && this.microLength(network) > this.microLimits[network];
    }

    /**
     * Credits an X publication would cost: one per post, per version X is actually sending
     * (ADR-092/094). Counted off this network's own language set, not the window's — before
     * ADR-100 the two were the same number and the estimate was wrong whenever they shouldn't be.
     */
    xCreditCost(): number {
        if (!this.destX()) return 0;
        const perLanguage = this.microMode()['x'] === 'thread' ? Math.max(1, this.microParts()['x']) : 1;
        return perLanguage * this.microLangsOf('x').length;
    }

    xCreditsShort(): boolean {
        const have = this.xCredits();
        return have !== null && this.xCreditCost() > have;
    }

    /**
     * Bluesky counts graphemes, not UTF-16 units — an emoji is one character to it. Intl.Segmenter
     * is what the browser has for that; without it the count would be wrong in exactly the posts
     * people care about.
     */
    blueskyGraphemes(): number {
        const text = this.microText('bluesky');
        if (!text) return 0;
        const segmenter = (Intl as unknown as { Segmenter?: new (l?: string, o?: object) => { segment(s: string): Iterable<unknown> } }).Segmenter;
        if (!segmenter) return [...text].length;
        return [...new segmenter(undefined, { granularity: 'grapheme' }).segment(text)].length;
    }

    private async loadShortPostTargets() {
        try {
            const networks = await this.publishApi.networks();
            this.networkCaps.set(networks.map(n => n.capabilities));
            this.blueskyAccount.set(networks.find(n => n.network === 'bluesky')?.accounts[0] ?? null);
            this.xAccount.set(networks.find(n => n.network === 'x')?.accounts[0] ?? null);
            this.discordAccount.set(networks.find(n => n.network === 'discord')?.accounts[0] ?? null);
        } catch {
            this.blueskyAccount.set(null);
            this.xAccount.set(null);
            this.discordAccount.set(null);
        }
        // A network that is no longer connected must not stay ticked — Publish would queue against
        // an account that is gone and report it as a failure of the post rather than of the setup.
        if (!this.blueskyAccount()) this.destBluesky.set(false);
        if (!this.xAccount()) this.destX.set(false);
        if (!this.discordAccount()) this.destDiscord.set(false);

        try {
            this.xCredits.set((await this.billingApi.credits()).balance);
        } catch { /* the note simply doesn't render */ }

        const id = this.currentId();
        if (!id) return;
        this.microTexts = { bluesky: {}, x: {}, discord: {} };
        this.microDirty = { bluesky: new Set(), x: new Set(), discord: new Set() };
        try {
            const { texts } = await this.publishApi.texts(id);
            for (const entry of texts) {
                if (entry.network === 'x' || entry.network === 'bluesky' || entry.network === 'discord')
                    this.microTexts[entry.network][entry.language] = entry.text;
            }
        } catch { /* no overrides loaded means every version falls back to its teaser */ }
        // The tabs follow the ticked versions, so the field cannot be left editing a language the
        // author has just unticked.
        for (const network of this.microNetworks) this.syncMicroTextLang(network);
    }

    private syncMicroTextLang(network: MicroNetwork) {
        const langs = this.microLangsOf(network);
        if (!langs.includes(this.microTextLang()[network]))
            this.setMicroTextLang(network, langs[0] ?? DEFAULT_PRIMARY_LANGUAGE);
    }

    /** Saves whatever the author typed before anything is queued (ADR-077's override contract). */
    private async saveMicroTexts(network: MicroNetwork) {
        const id = this.currentId();
        if (!id) return;
        for (const lang of [...this.microDirty[network]]) {
            await this.publishApi.saveText(id, network, lang, this.microTexts[network][lang] ?? '');
            this.microDirty[network].delete(lang);
        }
    }

    /**
     * X counts weighted units, not characters — mirrors XPostBuilder (Core): any URL is 23 after
     * the t.co rewrite, Latin/Cyrillic/general punctuation weigh 1, everything else 2, and an
     * emoji sequence is one element of 2. The two counters must agree, or the field would refuse
     * a post the server happily sends (or the reverse).
     */
    xWeighted(): number {
        let text = this.microText('x').normalize('NFC');
        if (!text) return 0;
        let total = 0;
        text = text.replace(/https?:\/\/\S+/g, () => { total += 23; return ''; });

        const segmenter = (Intl as unknown as { Segmenter?: new (l?: string, o?: object) => { segment(s: string): Iterable<{ segment: string }> } }).Segmenter;
        const graphemes = segmenter
            ? [...new segmenter(undefined, { granularity: 'grapheme' }).segment(text)].map(s => s.segment)
            : [...text];
        for (const grapheme of graphemes) {
            let weight = 0;
            let emoji = false;
            for (const ch of grapheme) {
                const cp = ch.codePointAt(0)!;
                if (cp >= 0x1F000 || (cp >= 0x2600 && cp <= 0x27BF) || cp === 0xFE0F || cp === 0x200D) { emoji = true; break; }
                weight += (cp <= 4351 || (cp >= 8192 && cp <= 8205) || (cp >= 8208 && cp <= 8223) || (cp >= 8242 && cp <= 8247)) ? 1 : 2;
            }
            total += emoji ? 2 : weight;
        }
        return total;
    }

    // ─── File exports (T-042) ─────────────────────────────────────────────────────────────────
    downloading = signal<'cedar' | 'zip' | null>(null);
    downloadError = signal('');

    /**
     * Fetches the export, then hands the browser a finished blob. The plain <a download> this
     * replaces was silent for as long as the server took to package the media — which for a post
     * with a hundred images is not a moment. The whole file passes through memory, which is the
     * price of knowing when it is ready; the alternative is a link that cannot say anything.
     */
    async downloadExport(kind: 'cedar' | 'zip') {
        const id = this.currentId();
        if (!id || this.downloading()) return;
        this.downloading.set(kind);
        this.downloadError.set('');
        try {
            const response = await fetch(`/api/drafts/${id}/${kind === 'cedar' ? 'cedar' : 'export-zip'}`);
            if (!response.ok) throw new Error(String(response.status));

            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            // The server names the file in Content-Disposition; falling back to the draft title
            // keeps a download from landing as "download".
            link.download = this.fileNameFrom(response.headers.get('content-disposition'))
                ?? `${this.title || 'draft'}.${kind === 'cedar' ? 'cedar' : 'zip'}`;
            link.click();
            // Revoked on the next tick: revoking synchronously can cancel the download in Safari.
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch {
            this.downloadError.set(this.t().editor.errors.publish);
        } finally {
            this.downloading.set(null);
        }
    }

    private fileNameFrom(header: string | null): string | null {
        const match = header?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
        return match ? decodeURIComponent(match[1]) : null;
    }

    // ─── Pre-send validation (T-086) ──────────────────────────────────────────────────────────
    publishIssues = signal<{ code: string; blocking: boolean; actual: number; limit: number }[]>([]);

    /** Asked when the export window opens and whenever the language changes — not per keystroke. */
    async refreshPublishIssues() {
        const id = this.currentId();
        if (!id) { this.publishIssues.set([]); return; }
        try {
            const res = await this.posts.validate(id, 'telegram', this.lang());
            this.publishIssues.set(res.issues);
            // The document may have changed since the last look — the parts are recomputed on demand.
            this.threadParts.set([]);
        } catch {
            // A failed check must never stand between the author and publishing: the network's own
            // refusal is still there as the backstop.
            this.publishIssues.set([]);
        }
    }

    // ─── Pre-publish content checks (Wave 2 item 14) ──────────────────────────────────────────
    // One warnings panel for the whole modal: the Telegram capability issues above and these
    // per-language content checks (empty version, dead links, missing alt) read as one checklist.
    // Warnings NEVER block — canPublishAll() deliberately never reads any of this.
    private linksApi = inject(LinksService);
    preflightResults = signal<PreflightLanguage[]>([]);
    preflightLoading = signal(false);

    async refreshPreflight() {
        const id = this.currentId();
        if (!id) { this.preflightResults.set([]); return; }
        const langs = this.exportLangs();
        this.preflightLoading.set(true);
        try {
            const res = await this.posts.preflight(id, langs);
            this.preflightResults.set(res.perLanguage ?? []);
        } catch {
            // Best-effort by contract — a failed check must never stand between the author and
            // publishing. 404 until the server lane lands is the expected shape of this catch.
            this.preflightResults.set([]);
        } finally {
            this.preflightLoading.set(false);
        }
    }

    /** Languages with at least one content warning — what the checklist actually lists. */
    preflightWarnings(): PreflightLanguage[] {
        return this.preflightResults().filter(p => p.emptyVersion || p.deadLinks.length > 0);
    }

    hasAnyWarnings(): boolean {
        return this.isWorkingMaterial() || this.visiblePublishIssues().length > 0 || this.preflightWarnings().length > 0;
    }

    // ─── Best-time hint (Wave 2 item 12) ──────────────────────────────────────────────────────
    bestTimes = signal<BestTimeSlot[]>([]);

    /** Asked for the first ticked version's chosen channel; empty hides the hint entirely. */
    async loadBestTimes() {
        const channel = this.exportLangs().map(l => this.selectedChannelFor(l)).find(c => !!c);
        if (!channel) { this.bestTimes.set([]); return; }
        try {
            this.bestTimes.set(await this.channelsApi.bestTimes(channel.id));
        } catch {
            this.bestTimes.set([]);
        }
    }

    /** Top hours converted to the browser zone: "18:00, 21:00" — or '' when there is no data. */
    bestTimeHint(): string {
        const top = this.bestTimes().slice(0, 3);
        if (!top.length) return '';
        const offsetMinutes = -new Date().getTimezoneOffset();
        const hours = top.map(s => {
            const local = (((s.hour * 60 + offsetMinutes) % 1440) + 1440) % 1440;
            return `${String(Math.floor(local / 60)).padStart(2, '0')}:${String(local % 60).padStart(2, '0')}`;
        });
        return this.t().editor.exportModal.bestTimeHint(hours.join(', '));
    }

    // ─── Telegram send options: silent + pin (Wave 2 item 11) ─────────────────────────────────
    // Carried on the scheduled row (ScheduleRequest.Silent/Pin); also sent with the queue request
    // so an immediate send picks them up once the server reads them there.
    exportSilent = signal(false);
    exportPin = signal(false);

    // ─── CTA buttons (Wave 2 item 17) ─────────────────────────────────────────────────────────
    // A per-post setting, not an editor node: up to 3 url buttons appended to the LAST Telegram
    // message at the wire level. Edited here because the export modal is where the Telegram send
    // is configured (ui-changes.md rule 1 — the modal is the home of send parameters).
    readonly ctaButtonMax = CTA_BUTTON_MAX;
    readonly ctaTextMax = CTA_TEXT_MAX;
    ctaButtons = signal<CtaButton[]>([]);
    ctaBusy = signal(false);
    ctaError = signal('');
    ctaSaved = signal(false);
    private ctaSavedTimer?: ReturnType<typeof setTimeout>;

    addCtaButton() {
        if (this.ctaButtons().length >= CTA_BUTTON_MAX) return;
        this.ctaButtons.update(list => [...list, { text: '', url: '' }]);
    }

    async removeCtaButton(index: number) {
        if (!await this.confirmation.confirm({ message: this.t().common.removeAuthoredContentConfirm })) return;
        this.ctaButtons.update(list => list.filter((_, i) => i !== index));
    }

    setCtaText(index: number, text: string) {
        this.ctaButtons.update(list => list.map((b, i) => i === index ? { ...b, text } : b));
    }

    setCtaUrl(index: number, url: string) {
        this.ctaButtons.update(list => list.map((b, i) => i === index ? { ...b, url } : b));
    }

    ctaButtonValid(b: CtaButton): boolean {
        return b.text.trim().length > 0 && b.text.trim().length <= CTA_TEXT_MAX && /^https?:\/\/\S+$/.test(b.url.trim());
    }

    /** Every row valid — an empty list is valid too (it clears the buttons). */
    ctaAllValid(): boolean {
        return this.ctaButtons().every(b => this.ctaButtonValid(b));
    }

    async saveCtaButtons() {
        const id = this.currentId();
        if (!id || this.ctaBusy() || !this.ctaAllValid()) return;
        this.ctaBusy.set(true);
        this.ctaError.set('');
        try {
            const buttons = this.ctaButtons().map(b => ({ text: b.text.trim(), url: b.url.trim() }));
            await this.draftsApi.setCtaButtons(id, buttons);
            this.ctaSaved.set(true);
            clearTimeout(this.ctaSavedTimer);
            this.ctaSavedTimer = setTimeout(() => this.ctaSaved.set(false), 2000);
        } catch (e) {
            this.ctaError.set(httpErrorMessage(e, this.t().editor.exportModal.ctaSaveFailed));
        } finally {
            this.ctaBusy.set(false);
        }
    }

    private async loadCtaButtons(id: string) {
        try {
            const full = await this.draftsApi.get(id);
            if (this.currentId() === id) this.ctaButtons.set(parseCtaButtons(full.ctaButtonsJson));
        } catch {
            this.ctaButtons.set([]);
        }
    }

    // ─── Tracked short links (Wave 2 item 16) ─────────────────────────────────────────────────
    trackedLinkBusy = signal(false);
    trackedLinkError = signal('');
    /** Which row just landed on the clipboard — the language code, or 'custom'. */
    trackedLinkCopied = signal<string | null>(null);
    private trackedCopiedTimer?: ReturnType<typeof setTimeout>;
    trackedCustomUrl = '';

    /** Creates (or reuses) the short link for one language's public blog URL and copies it. */
    async copyTrackedBlogLink(lang: string) {
        const base = this.blogUrl();
        if (!base) return;
        const url = lang === this.primaryLanguage ? base : `${base}?lang=${lang}`;
        await this.copyTrackedLink(url, lang);
    }

    async copyTrackedCustomLink() {
        const url = this.trackedCustomUrl.trim();
        if (!/^https?:\/\/\S+$/.test(url)) return;
        await this.copyTrackedLink(url, 'custom');
    }

    private async copyTrackedLink(url: string, key: string) {
        const id = this.currentId();
        if (!id || this.trackedLinkBusy()) return;
        this.trackedLinkBusy.set(true);
        this.trackedLinkError.set('');
        try {
            const res = await this.linksApi.create(url, id);
            await navigator.clipboard.writeText(res.shortUrl ?? `${location.origin}/l/${res.code}`);
            this.trackedLinkCopied.set(key);
            clearTimeout(this.trackedCopiedTimer);
            this.trackedCopiedTimer = setTimeout(() => this.trackedLinkCopied.set(null), 2000);
        } catch (e) {
            this.trackedLinkError.set(httpErrorMessage(e, this.t().editor.exportModal.trackedLinkFailed));
        } finally {
            this.trackedLinkBusy.set(false);
        }
    }

    // ─── Publishing as a thread (T-106) ───────────────────────────────────────────────────────
    // Never automatic: eight messages to a channel is a loud act, and the author has to ask for it
    // and see where the cuts land first.
    splitIntoThread = signal(false);
    threadParts = signal<ThreadPart[]>([]);

    /** True when the post is too big for one message in a way that splitting actually solves. */
    canSplitIntoThread(): boolean {
        return this.publishIssues().some(i => i.blocking && (i.code === 'too-long' || i.code === 'too-many-media'));
    }

    /** The issues left once the thread has answered the ones it answers. */
    visiblePublishIssues() {
        if (!this.splitIntoThread()) return this.publishIssues();
        return this.publishIssues().filter(i => i.code !== 'too-long' && i.code !== 'too-many-media');
    }

    async toggleThread(on: boolean) {
        this.splitIntoThread.set(on);
        if (!on || this.threadParts().length > 0) return;

        const id = this.currentId();
        if (!id) return;
        try {
            const { parts } = await this.publishApi.threadPreview(id, 'telegram', this.lang());
            this.threadParts.set(parts);
        } catch {
            // Without a preview the switch is a promise nobody can check — so it goes back off.
            this.threadParts.set([]);
            this.splitIntoThread.set(false);
        }
    }

    threadPartLabel(part: ThreadPart): string {
        return part.startsWith?.trim() || this.t().editor.exportModal.threadPartUntitled;
    }

    publishIssueText(issue: { code: string; actual: number; limit: number }): string {
        const texts = this.t().editor.exportModal.issues as Record<string, unknown>;
        const entry = texts[issue.code];
        if (typeof entry === 'function') {
            return (entry as (a: string, b: string) => string)(
                this.formatIssueNumber(issue.code, issue.actual),
                this.formatIssueNumber(issue.code, issue.limit));
        }
        return typeof entry === 'string' ? entry : issue.code;
    }

    private formatIssueNumber(code: string, value: number): string {
        // Byte-shaped codes read as sizes, the rest as plain counts.
        return code === 'image-too-large' || code === 'slow-media'
            ? this.formatFileSize(value)
            : value.toLocaleString();
    }

    // ─── Glossary term from a selection (Marty, 01.08.2026) ───────────────────────────────────
    private glossaryApi = inject(GlossaryService);
    termMenu = signal<{ x: number; y: number; hasSelection: boolean } | null>(null);
    termDraft = signal<{ term: string; language: string } | null>(null);
    termBusy = signal(false);
    termError = signal('');
    draftGlossaryTerms = signal<DraftGlossaryTerm[]>([]);
    draftGlossaryBusy = signal(false);

    // ADR-187 — the sheet's menu, not the selection's: the AI entries are always there and the
    // glossary entry appears only when there is a selection to make a term out of. It used to
    // bail without a selection, because its one entry needed one.
    onSheetContextMenu(event: MouseEvent) {
        event.preventDefault();
        // T-341 — raw pointer coordinates near an edge push the fixed menu off-screen; the
        // clamp reserves the menu's own box (min-width 220 plus its tallest entry set).
        this.termMenu.set({
            x: Math.max(8, Math.min(event.clientX, window.innerWidth - 240)),
            y: Math.max(8, Math.min(event.clientY, window.innerHeight - 260)),
            hasSelection: !!this.selectedText(),
        });
    }

    private selectedText(): string {
        const state = this.editor?.state;
        if (!state || state.selection.empty) return '';
        return state.doc.textBetween(state.selection.from, state.selection.to, ' ').trim();
    }

    openTermFromSelection() {
        const term = this.selectedText();
        this.termMenu.set(null);
        if (!term) return;
        this.termError.set('');
        // The term belongs to the language being written, which is what the blog will scan it
        // against — not to the UI language, and not to the draft's primary one.
        this.termDraft.set({ term, language: this.lang() });
    }

    async saveTerm(input: GlossaryTermInput) {
        if (this.termBusy()) return;
        this.termBusy.set(true);
        this.termError.set('');
        try {
            await this.glossaryApi.create(input);
            this.termDraft.set(null);
            await this.loadDraftGlossary();
        } catch (e) {
            this.termError.set(httpErrorMessage(e, this.t().glossary.saveFailed));
        } finally {
            this.termBusy.set(false);
        }
    }

    private async loadDraftGlossary() {
        const id = this.currentId();
        if (!id || this.showEmptyState()) { this.draftGlossaryTerms.set([]); return; }
        const language = this.lang();
        try {
            const terms = await this.glossaryApi.listForDraft(id, language);
            if (this.currentId() === id && this.lang() === language) this.draftGlossaryTerms.set(terms);
        } catch {
            this.draftGlossaryTerms.set([]);
        }
    }

    async toggleDraftGlossaryTerm(term: DraftGlossaryTerm) {
        const id = this.currentId();
        if (!id || this.draftGlossaryBusy()) return;
        const excluded = !term.excluded;
        this.draftGlossaryBusy.set(true);
        try {
            await this.glossaryApi.setDraftTerm(id, this.lang(), term.id, excluded);
            this.draftGlossaryTerms.update(list => list.map(item => item.id === term.id ? { ...item, excluded } : item));
        } finally {
            this.draftGlossaryBusy.set(false);
        }
    }
    private tick = signal(0);

    drafts = signal<DraftMeta[]>([]);
    currentId = signal<string | null>(null);
    saveState = signal<SaveState>('saved');
    title = '';

    private saveTimer?: ReturnType<typeof setTimeout>;
    private saveRetryTimer?: ReturnType<typeof setTimeout>;
    private saveAttempt = 0;
    // T-018 — set when the server refused a save; the editor asks the author instead of retrying.
    saveRefusal = signal<SaveRefusal | null>(null);

    private posts = inject(PostsService); // + import сверху
    private channelsApi = inject(ChannelsService);

    // ADR-098 — the Telegram destination is a (language, channel) pair, not a channel: a post with
    // an English translation goes to the English channel in the same publish, not in a second one.
    // Seeded from the browser's remembered mapping, which is a preference of the author's and not
    // a property of the draft (a channel that is no longer connected simply doesn't preselect).
    telegramChatIds = signal<Record<string, string>>(loadTelegramChannelPrefs());
    // Telegram export is Markdown-only — the Rich Message HTML mode needs exact custom tag
    // names (<photo>, <tg-slideshow>, ...) and has repeatedly broken in practice; Markdown uses
    // plain, well-tested syntax for the same underlying rich-block output. HTML stays in use for
    // the blog (CedarToBlogHtmlRenderer, a separate/unrelated renderer).
    readonly format: PostFormat = 'Markdown';
    // FI2.2 — languages are ticked, not picked: a post can go to Telegram in several at once,
    // one message per language. Never empty, since publishing to no language is not a request.
    exportLangs = signal<string[]>([DEFAULT_PRIMARY_LANGUAGE]);

    // T-331 — saved sets of destinations, offered at the top of step 2. Loaded when the modal
    // opens rather than with the editor: most sessions never export.
    exportPresets = signal<Preset[]>([]);
    compressionLevel: CompressionLevel = 'standard';

    // Active content language in the editor. 'ru' edits the draft itself (primary version),
    // 'en' edits the DraftTranslation row. Only one editor instance — switching tabs flushes
    // the autosave for the language being left, then loads the other version's content.
    lang = signal<string>(DEFAULT_PRIMARY_LANGUAGE);
    // NF2 - one entry per existing translation, keyed by language code. Was a single enMeta
    // signal back when "a translation" could only mean English.
    translations = signal<Record<string, TranslationMeta>>({});
    private ruUpdatedAt = signal<string>('');
    // RU title+content captured when entering the EN tab — source for "Copy from Russian"
    private ruSnapshot: { title: string; json: string } | null = null;

    // RU CedarJson as it was the last time the EN translation was synced (created/saved/
    // auto-translated) — lets the RU tab show a gutter of what's changed structurally since,
    // instead of just the boolean enStale() flag. Null when there's no EN version, or an older
    // translation predating the SourceSnapshotJson column that hasn't been resynced since.
    // Snapshot of the primary doc as of the ACTIVE translation's last sync - the diff gutter
    // compares against whichever translation tab you last opened, since "what changed since
    // translating" has no single answer once there are several.
    private activeSourceSnapshot = signal<string | null>(null);
    ruDiffMarkers = signal<{ top: number; height: number; kind: 'added' | 'changed' | 'removed' }[]>([]);
    private ruDiffTimer?: ReturnType<typeof setTimeout>;

    // Tags are per-draft (shared across language versions) and saved through their own endpoint
    // immediately on add/remove — not through the content autosave, which routes per-language.
    tagList = signal<string[]>([]);

    // At most one folder per draft (see the ADR following ADR-038, docs/DECISIONS.md). The list
    // itself and the cloud of tags in use live in shared services now (FI3.2/FI3.3) — this page
    // only holds which folder and tags *this* draft has.
    currentFolderId = signal<string | null>(null);
    currentSeriesId = signal<string | null>(null);

    // ADR-102 — what kind of document this is. Working material (design/script/plot/note) never
    // publishes, so the flag is surfaced here and in the export modal instead of as a server 400.
    documentType = signal<DocumentType>('post');
    isWorkingMaterial = computed(() => !isPublishableType(this.documentType()));
    documentTypeError = signal<string | null>(null);
    /** T-239 — the server's refusal to take a typed blog address, shown under the slug row. */
    slugError = signal<string | null>(null);
    readonly docTypes = DOCUMENT_TYPES;
    readonly docTypeIcons = DOCUMENT_TYPE_ICONS;
    readonly isPublishableType = isPublishableType;
    private projectsApi = inject(ProjectsService);

    aiEditBusy = signal(false);
    aiEditElapsed = signal(0);
    // Pseudo-progress (0-100, Phase 8 Step 8) — neither AI provider streams a response, so this
    // is an asymptotic estimate (pseudo-progress.util.ts), not a real percentage.
    aiEditProgress = signal(0);
    private aiEditTicker?: ReturnType<typeof setInterval>;
    private aiEditJobId: string | null = null;
    private aiEditCancelled = false;
    aiEditError = signal<string | null>(null);
    aiConfirmKind = signal<AiEditKind | null>(null);
    aiToast = signal<string | null>(null);
    private aiToastTimer?: ReturnType<typeof setTimeout>;

    autoTranslating = signal(false);
    autoTranslateElapsed = signal(0);
    autoTranslateProgress = signal(0);
    private autoTranslateTicker?: ReturnType<typeof setInterval>;
    private autoTranslateJobId: string | null = null;
    private autoTranslateCancelled = false;
    autoTranslateError = signal<string | null>(null);
    translateConfirmOpen = signal(false);
    draftAssets = signal<DraftAsset[]>([]);
    // Sorting the file list (Marty, 01.08.2026). Kept in the component rather than sorting the
    // fetched array in place: the order is a view preference, and re-fetching must not silently
    // reset it. Size descending is the default because the question anyone actually brings to this
    // list is "what is making this post heavy".
    assetSort = signal<{ key: 'name' | 'type' | 'size'; desc: boolean }>({ key: 'size', desc: true });

    sortAssetsBy(key: 'name' | 'type' | 'size') {
        this.assetSort.update(current => current.key === key
            ? { key, desc: !current.desc }
            : { key, desc: key === 'size' });
    }

    sortedDraftAssets(): DraftAsset[] {
        const { key, desc } = this.assetSort();
        const direction = desc ? -1 : 1;
        return [...this.draftAssets()].sort((a, b) => {
            const compared = key === 'size'
                ? a.sizeBytes - b.sizeBytes
                // localeCompare, not <: file names are routinely Cyrillic here, and codepoint
                // order puts every one of them after every Latin name.
                : (key === 'type' ? a.contentType.localeCompare(b.contentType) : 0)
                    || a.fileName.localeCompare(b.fileName);
            return compared * direction;
        });
    }

    assetSortMark(key: 'name' | 'type' | 'size'): IconName | null {
        const { key: active, desc } = this.assetSort();
        return active === key ? (desc ? 'arrow-down' : 'arrow-up') : null;
    }

    draftAssetsLoading = signal(false);

    // Export destinations (B5) — tick a destination to unfold its settings; one Publish button
    // at the bottom fires every ticked one in sequence.
    destBlog = signal(false);
    destTelegram = signal(false);
    // Blog-wide subscriber mail (Wave 1 item 7) — only the FIRST publish can notify; the server
    // ignores the flag on a republish, and the panel hides the toggle then for the same reason.
    notifySubscribers = signal(false);
    publishingAll = signal(false);
    exporting = signal(false);
    exportElapsed = signal(0);
    private exportTicker?: ReturnType<typeof setInterval>;
    exportResult = signal('');
    exportLink = signal<string | null>(null);
    exportError = signal<{ code?: number; message: string } | null>(null);
    // FI2.11 — what the last publish produced, with its links. Cleared after 10s.
    publishSuccess = signal<{ links: { label: string; url: string }[] } | null>(null);



    currentBlog = signal<{ slug: string; isPublished: boolean } | null>(null);
    blogBusy = signal(false);
    blogElapsed = signal(0);
    private blogTicker?: ReturnType<typeof setInterval>;
    blogError = signal<string | null>(null);

    // Shareable draft preview link (Wave 1 item 8) — one revocable read-only URL per draft.
    // The API never reads the token back, so this holds only what this session created; an
    // earlier session's link stays valid but invisible until rotated or revoked.
    previewLinkUrl = signal<string | null>(null);
    previewLinkBusy = signal(false);
    previewLinkError = signal<string | null>(null);
    previewLinkCopied = signal(false);
    private previewLinkCopiedTimer?: ReturnType<typeof setTimeout>;

    async createPreviewLink() {
        const id = this.currentId();
        if (!id || this.previewLinkBusy()) return;
        this.previewLinkBusy.set(true);
        this.previewLinkError.set(null);
        try {
            const res = await this.draftsApi.createPreviewLink(id);
            this.previewLinkUrl.set(res.url);
        } catch (e) {
            this.previewLinkError.set(httpErrorMessage(e, this.t().editor.preview.failed));
        } finally {
            this.previewLinkBusy.set(false);
        }
    }

    async revokePreviewLink() {
        const id = this.currentId();
        if (!id || this.previewLinkBusy()) return;
        if (!await this.confirmation.confirm({ message: this.t().common.revokeConfirm, confirmLabel: this.t().common.confirm })) return;
        this.previewLinkBusy.set(true);
        this.previewLinkError.set(null);
        try {
            await this.draftsApi.revokePreviewLink(id);
            this.previewLinkUrl.set(null);
        } catch (e) {
            this.previewLinkError.set(httpErrorMessage(e, this.t().editor.preview.failed));
        } finally {
            this.previewLinkBusy.set(false);
        }
    }

    async copyPreviewLink() {
        const url = this.previewLinkUrl();
        if (!url) return;
        try {
            await navigator.clipboard.writeText(url);
            this.previewLinkCopied.set(true);
            clearTimeout(this.previewLinkCopiedTimer);
            this.previewLinkCopiedTimer = setTimeout(() => this.previewLinkCopied.set(false), 2000);
        } catch {
            // No clipboard permission — the URL is on screen and selectable.
        }
    }

    // Private posts (see the ADR following ADR-040, docs/DECISIONS.md) — email invite list,
    // only meaningful once the draft is blog-published.
    isPrivate = signal(false);
    invites = signal<PostInvite[]>([]);
    invitesLoading = signal(false);
    inviteEmailInput = '';
    inviteBusy = signal(false);
    inviteError = signal<string | null>(null);

    // Registration form (B3) — null means uninvited visitors keep getting the plain 404.
    regForm = signal<RegistrationForm | null>(null);
    regBusy = signal(false);
    formPresets = signal<FormPreset[]>([]);
    // FI4.1 — languages this post actually has a registration form for, primary first.
    formLanguages = signal<string[]>([]);
    // Semi-public: a private post that still appears in the blog index, carrying a lock. Changes
    // what is advertised, never what is readable — the gate is untouched.
    isListedWhilePrivate = signal(false);
    // Copy protection on the blog page — selection, copy/cut and the context menu are blocked
    // on the rendered post. Private posts only, same deterrent family as the watermark.
    disableCopy = signal(false);

    // Watermark (I7) — drawn on the blog page only, never in the editor. watermarkText() is the
    // saved value (what the state strip reports); watermarkInput is the unsaved field content.
    watermarkText = signal<string | null>(null);
    // T-345 — the document's own location; blank falls back to the profile at render.
    locationText = signal<string>('');
    watermarkInput = '';
    watermarkBusy = signal(false);
    watermarkError = signal<string | null>(null);
    readonly watermarkMaxLength = WATERMARK_MAX_LENGTH;

    uploads = signal<UploadItem[]>([]);
    libraryOpen = signal(false);

    // ADR-128 — who links to the open document; refreshed on open, best-effort.
    backlinks = signal<{ id: string; title: string }[]>([]);

    // ADR-128 — the `[[` suggester's popover state; the pending command comes from the plugin.
    wikiSuggest = signal<{ x: number; y: number; items: { id: string; title: string }[]; index: number } | null>(null);
    private wikiSuggestCommand: ((attrs: { draftId: string; label: string }) => void) | null = null;
    private readonly onWikilinkOpen = (ev: Event) => {
        const draftId = (ev as CustomEvent).detail?.draftId as string | undefined;
        if (draftId && this.drafts().some(d => d.id === draftId)) void this.openDraft(draftId);
    };
    private uploadSeq = 0;

    // Connecting, disconnecting and the discovered-chats list moved to Settings → Integrations
    // (ADR-095). What the editor still needs is the list itself, to pick one from.
    channels = signal<Channel[]>([]);

    // Guards openDraft/newDraft — both mutate `currentId`/`drafts` and must not race each other
    // (e.g. a double-click while a draft is still loading). Deletion lives on /drafts now.
    draftsBusy = signal(false);

    // New Draft dialog (ADR-035) — minimal title+Enter, expandable to languages/tags/template.
    // The New Draft dialog itself now lives on /drafts (28.07.2026) — creating used to navigate
    // to /editor first and open the dialog there, leaving the editor page rendered with nothing
    // in it yet mid-creation. newDraft(opts) below stays here: it's still used by the two silent
    // fallbacks (empty draft list on load, deleting the last remaining draft) and needs the live
    // TipTap editor instance, which only exists on this page.
    readonly draftTitleMax = DRAFT_TITLE_MAX;

    scheduledAt = '';
    scheduling = signal(false);
    scheduleResult = signal('');

    emojiQuery = signal('');

    /** The set the modal draws: every group, or what the query leaves of them. */
    emojiGroups(): EmojiGroup[] {
        return searchEmoji(EMOJI_GROUPS, this.emojiQuery());
    }

    openEmojiModal() {
        this.emojiQuery.set('');
        this.emojiOpen.set(true);
    }

    // B13 - reveals where the content actually is: spaces, tabs and paragraph ends. A pure
    // display toggle, nothing about the document changes.

    dtValue = '';
    dtWeekday = true;
    dtDate = true;
    dtTime = true;

    footnoteText = '';
    footnoteOpen = signal(false);


    // Unified Insert modal — replaces the separate Link and YouTube popovers (ADR-035): "Auto"
    // detects YouTube vs a generic link from the pasted value, the rail lets you override it.
    insertOpen = signal(false);
    insertType: 'auto' | 'url' | 'email' | 'phone' | 'mention' | 'youtube' = 'auto';
    insertValue = '';
    insertCaption = '';
    insertError = signal('');

    // Emoji/datetime moved from app-popover to app-modal (Marty, 28.07.2026) — the emoji panel's
    // 120-emoji grid genuinely scrolls internally (.emoji-popover, max-height:320px), and
    // PopoverComponent closes on ANY document-level scroll event (it can't distinguish the panel's
    // own scroll from the page's), so scrolling the panel closed it instead — same root cause
    // ADR-057 already fixed for the Appearance panel. Datetime moved alongside it for consistency,
    // per Marty's own ask, even though its content is too short to hit the same bug independently.
    emojiOpen = signal(false);
    datetimeOpen = signal(false);

    saveLabel(): string {
        switch (this.saveState()) {
            case 'saved': return this.t().editor.saved;
            case 'saving': return this.t().editor.saving;
            case 'dirty': return this.t().editor.unsaved;
            case 'error': return this.t().editor.syncFailed;
        }
    }

    // Writing-sheet preferences (ADR-035, Settings → Appearance) — blog-unaffected, editor-only.
    editorFocused = signal(false);

    sheetMaxWidthPx(): number | null {
        return SHEET_WIDTH_PX[this.appearance.prefs().sheetWidth];
    }

    // Draft state strip above the language tabs (B22/B25) — the Telegram link comes from the
    // drafts list rather than its own signal, since PublishAsync already records it there.
    telegramPostUrl(): string | null {
        const meta = this.drafts().find(d => d.id === this.currentId());
        return meta?.lastTelegramUsername && meta?.lastTelegramMessageId
            ? `https://t.me/${meta.lastTelegramUsername}/${meta.lastTelegramMessageId}`
            : null;
    }

    // T-180 — the message in the channel is edited in place, never re-sent (ADR-278). The result
    // line is pinned to the draft *and* the edit it answered, so the next autosave retires it and
    // the stale hint can come back on its own.
    telegramSyncBusy = signal(false);
    telegramSyncNote = signal<{ id: string; updatedAt: string; tone: 'ok' | 'danger'; text: string } | null>(null);

    telegramStale(): boolean {
        const meta = this.currentMeta();
        if (!meta?.lastTelegramMessageId || !meta.lastTelegramSentAt) return false;
        return Date.parse(meta.updatedAt) > Date.parse(meta.lastTelegramSentAt);
    }

    telegramSyncLine(): { tone: 'ok' | 'warn' | 'danger'; text: string } | null {
        const meta = this.currentMeta();
        if (!meta?.lastTelegramMessageId) return null;
        const note = this.telegramSyncNote();
        if (note && note.id === meta.id && note.updatedAt === meta.updatedAt) return note;
        return this.telegramStale() ? { tone: 'warn', text: this.t().editor.telegramSync.stale } : null;
    }

    async syncTelegram() {
        const meta = this.currentMeta();
        if (!meta || this.telegramSyncBusy()) return;
        const copy = this.t().editor.telegramSync;
        // Pinned to the edit that was sent: an autosave that lands mid-flight retires the answer.
        const note = (tone: 'ok' | 'danger', text: string) =>
            this.telegramSyncNote.set({ id: meta.id, updatedAt: meta.updatedAt, tone, text });
        this.telegramSyncBusy.set(true);
        this.telegramSyncNote.set(null);
        try {
            const res = await this.posts.syncTelegram(meta.id, this.lang());
            this.drafts.update(list => list.map(d => d.id === meta.id
                ? { ...d, lastTelegramMessageId: res.messageId, lastTelegramSentAt: res.syncedAt }
                : d));
            note('ok', res.unchanged ? copy.unchanged : copy.done);
        } catch (e) {
            // 409 is Telegram's verdict (a thread, or a post that is gone) and the server already
            // says which in words; every other status carries its own reason the same way.
            note('danger', httpErrorMessage(e, copy.failed));
        } finally {
            this.telegramSyncBusy.set(false);
        }
    }

    isLive(): boolean {
        return !!this.currentBlog()?.isPublished || this.telegramPostUrl() !== null;
    }

    sheetTypefaceStack(): string {
        return TYPEFACE_STACK[this.appearance.prefs().typeface];
    }

    focusModeActive(): boolean {
        return this.appearance.prefs().focusModeHideToolbar && this.editorFocused();
    }

    wordCount(): number {
        this.tick();
        const text = this.editor?.getText() ?? '';
        return text.trim() ? text.trim().split(/\s+/).length : 0;
    }

    charCount(): number {
        this.tick();
        return this.editor?.getText().length ?? 0;
    }


    blockCount(): number {
        this.tick();
        return this.editor?.state.doc.childCount ?? 0;
    }

    /** 1-based, so the rule reads the way a page number does. 0 only while there is no document. */
    blockIndex(): number {
        this.tick();
        const state = this.editor?.state;
        if (!state || state.doc.childCount === 0) return 0;
        return state.selection.$from.index(0) + 1;
    }

    // ─── The structure shelf (ADR-162) ────────────────────────────────────────────────────────
    // The panel is a view of the document and holds nothing: `outline()` is the list, `blockIndex()`
    // above is the lit row, and both are read off ProseMirror. A click comes back through
    // goToBlock, which dispatches a transaction — so the round trip ends in the same state the
    // highlight is derived from, and there is no second copy to loop through.
    private outlineVersion = signal(0);
    private outlineTimer?: ReturnType<typeof setTimeout>;
    readonly documentSelected = signal(false);

    readonly outline = computed<OutlineEntry[]>(() => {
        this.outlineVersion();
        return buildOutline(this.outlineNodes());
    });

    /** -1 while the document has no blocks, so no row lights on an empty sheet. */
    outlineActive(): number {
        return this.documentSelected() ? -1 : this.blockIndex() - 1;
    }

    private scheduleOutlineRebuild() {
        clearTimeout(this.outlineTimer);
        this.outlineTimer = setTimeout(() => this.outlineVersion.update(v => v + 1), OUTLINE_DEBOUNCE_MS);
    }

    private outlineNodes(): OutlineNodeLike[] {
        const doc = this.editor?.state.doc;
        if (!doc) return [];
        const nodes: OutlineNodeLike[] = [];
        doc.forEach(node => nodes.push({
            typeName: node.type.name,
            attrs: node.attrs,
            childCount: node.childCount,
            text: node.textContent,
            selectableAtom: node.isAtom && NodeSelection.isSelectable(node),
        }));
        return nodes;
    }

    goToBlock(entry: OutlineEntry) {
        this.documentSelected.set(false);
        const view = this.editor?.view;
        if (!view) return;
        const { doc } = view.state;
        const sizes: number[] = [];
        doc.forEach(node => sizes.push(node.nodeSize));
        // The list is debounced, so a row on screen can outlive the block it describes; resolving a
        // position past the end throws rather than missing quietly (ADR-162 clause 3).
        const pos = topLevelStart(sizes, entry.index);
        if (pos === null) return;

        const node = doc.child(entry.index);
        // A NodeSelection is the only thing the inspector counts as a selected object (ADR-159
        // clause 5), so picking a media block fills the shelf beside it; everything else gets a
        // caret to keep typing from.
        const selection = node.isAtom && NodeSelection.isSelectable(node)
            ? NodeSelection.create(doc, pos)
            : TextSelection.near(doc.resolve(pos + 1));
        view.dispatch(view.state.tr.setSelection(selection));
        view.focus();

        // ProseMirror's own scrollIntoView() moves the sheet by the least it can, which lands the
        // picked block against whichever edge it came from — on a long document that reads as "the
        // list jumped somewhere" rather than "here it is". The DOM's own centring is what shows it.
        const dom = view.nodeDOM(pos) ?? view.domAtPos(pos + 1).node;
        const box = dom instanceof HTMLElement ? dom : dom?.parentElement ?? null;
        box?.scrollIntoView({ block: 'center', inline: 'nearest' });
    }

    selectDocument() {
        this.documentSelected.set(true);
        this.editor?.commands.blur();
    }

    onWorktopClick(event: MouseEvent) {
        const target = event.target as HTMLElement;
        if (target.closest('.sheet, button, input, textarea, select, a')) return;
        this.selectDocument();
    }

    syncWord(): string {
        const t = this.t().editor;
        switch (this.saveState()) {
            case 'saved': return t.synced;
            case 'error': return t.syncFailed;
            default: return t.syncing;
        }
    }

    channelColor(id: string): string {
        return avatarFill(id);
    }

    channelInitial(title: string): string {
        return avatarInitial(title);
    }

    // ─── Telegram: a channel per version (ADR-098) ────────────────────────────────────────────
    chatIdFor(lang: string): string {
        return (this.telegramChatIds()[lang] ?? '').trim();
    }

    isSelectedChannel(lang: string, c: Channel): boolean {
        return this.chatIdFor(lang) === String(c.telegramChatId);
    }

    selectedChannelFor(lang: string): Channel | undefined {
        const chatId = this.chatIdFor(lang);
        return chatId ? this.channels().find(c => String(c.telegramChatId) === chatId) : undefined;
    }

    selectChannel(lang: string, c: Channel) {
        this.setChatId(lang, String(c.telegramChatId));
    }

    clearChannel(lang: string) {
        this.setChatId(lang, '');
    }

    private setChatId(lang: string, chatId: string) {
        this.telegramChatIds.update(map => ({ ...map, [lang]: chatId }));
        saveTelegramChannelPrefs(this.telegramChatIds());
        // The best-time hint answers about the chosen channel, so it follows the choice.
        void this.loadBestTimes();
    }

    /** Ticked versions with no channel behind them — what Publish is waiting on. */
    langsMissingChannel(): string[] {
        return this.exportLangs().filter(l => !this.chatIdFor(l));
    }

    missingChannelLabel(): string {
        return this.langsMissingChannel().map(l => l.toUpperCase()).join(', ');
    }

    // I3 — the shortcut a button also answers to, appended to its tooltip. Every entry was read
    // off TipTap's actual key bindings in the installed packages rather than assumed: a tooltip
    // promising a shortcut that doesn't fire is worse than no tooltip. Buttons with no binding
    // (media, tables, insert…) are deliberately absent and render unchanged.
    private readonly shortcuts: Record<string, string> = {
        bold: 'Mod+B', italic: 'Mod+I', underline: 'Mod+U', strike: 'Mod+Shift+S',
        inlineCode: 'Mod+E', codeBlock: 'Mod+Alt+C', blockquote: 'Mod+Shift+B',
        bulletList: 'Mod+Shift+8', orderedList: 'Mod+Shift+7', taskList: 'Mod+Shift+9',
        undo: 'Mod+Z', redo: 'Mod+Y',
    };

    // TipTap's "Mod" is Cmd on Apple hardware and Ctrl everywhere else, so the tooltip has to
    // resolve it the same way the binding does.
    private readonly modKey = /Mac|iPhone|iPad|iPod/i.test(navigator.platform || navigator.userAgent) ? '⌘' : 'Ctrl';

    // ─── The inspector (ADR-159) ──────────────────────────────────────────────────────────────
    // One subject at a time. Only a ProseMirror NodeSelection counts as an object being selected;
    // a caret inside a paragraph is the document being written, not a block being inspected.
    private selectedNode(): NodeLike | null {
        this.tick();
        const state = this.editor?.state;
        if (!state || !(state.selection instanceof NodeSelection)) return null;
        const node = state.selection.node;
        return { typeName: node.type.name, attrs: node.attrs, childCount: node.childCount };
    }

    selectionSpec(): SelectionSpec | null {
        return describeSelection(this.selectedNode());
    }

    // T-240 — the file behind the selected media node. Held here rather than on the node: a
    // resolution or a byte count copied into the document goes stale the moment the bytes change,
    // and a document that quietly asserts a wrong resolution is worse than one that says nothing.
    assetMeta = signal<AssetMeta | null>(null);
    private assetMetaKey = '';

    /**
     * Runs on every transaction, so it is keyed rather than debounced: selecting the same picture
     * twice asks once, and typing asks not at all. The path is the second key — it is what lets a
     * document written before `assetId` existed answer too.
     */
    private syncAssetMeta() {
        const spec = this.selectionSpec();
        const isMedia = spec?.kind === 'image' || spec?.kind === 'video' || spec?.kind === 'audio';
        const id = isMedia ? spec?.assetId ?? null : null;
        const path = isMedia ? mediaPathOf(this.selectedNode()?.attrs['src'] as string | undefined) : null;
        const key = id ? `id:${id}` : path ? `path:${path}` : '';
        if (key === this.assetMetaKey) return;

        this.assetMetaKey = key;
        this.assetMeta.set(null);
        if (!key) return;
        void this.assets.meta({ id, path }).then(meta => {
            if (this.assetMetaKey === key) this.assetMeta.set(meta);
        });
    }

    /**
     * Whose properties the shelf shows. Most specific first: a selected object, then the table the
     * caret stands in, then a run of selected text. The document is the fallback, and it used to be
     * the answer for the middle two — selecting a sentence described the whole document instead.
     */
    inspectorScope(): 'selection' | 'table' | 'text' | 'document' {
        if (this.documentSelected()) return 'document';
        if (this.selectionSpec()) return 'selection';
        if (this.isActive('table')) return 'table';
        return this.hasTextSelection() ? 'text' : 'document';
    }

    inspectorScopeWord(): string {
        const t = this.t().editor.inspector;
        switch (this.inspectorScope()) {
            case 'selection': return t.selected;
            case 'table': return t.tableTitle.toLowerCase();
            case 'text': return t.textScope;
            default: return t.documentScope;
        }
    }

    hasTextSelection(): boolean {
        this.tick();
        const state = this.editor?.state;
        return !!state && state.selection instanceof TextSelection && !state.selection.empty;
    }

    /** The block the caret is in, named the way the block dropdown names it. */
    blockTypeLabel(): string {
        const t = this.t().editor.blocks;
        const level = this.currentBlockLevel();
        return level === 0 ? t.paragraph : t.heading(level);
    }

    /** Every mark actually applied to the selection, in the order the strip draws them. */
    activeMarks(): string {
        this.tick();
        const tb = this.t().editor.tb;
        const marks: [string, string][] = [
            ['bold', tb.bold], ['italic', tb.italic], ['underline', tb.underline],
            ['strike', tb.strike], ['code', tb.inlineCode], ['spoiler', tb.spoiler],
        ];
        const on = marks.filter(([name]) => this.editor?.isActive(name)).map(([, label]) => label);
        return on.length ? on.join(' · ') : this.t().editor.inspector.plain;
    }

    /** The href under the caret, when there is one. */
    activeLinkHref(): string {
        this.tick();
        const href = this.editor?.getAttributes('link')?.['href'];
        return typeof href === 'string' ? href : '';
    }

    selectedWordCount(): number {
        this.tick();
        const text = this.selectedText();
        return text ? text.split(/\s+/).filter(Boolean).length : 0;
    }

    selectedCharCount(): number {
        this.tick();
        return this.selectedText().length;
    }

    /** Rows and columns of the table the caret is in. Read off the node, never off a preference. */
    tableSize(): { rows: number; cols: number } {
        this.tick();
        const state = this.editor?.state;
        if (!state) return { rows: 0, cols: 0 };
        for (let depth = state.selection.$from.depth; depth > 0; depth--) {
            const node = state.selection.$from.node(depth);
            if (node.type.name !== 'table') continue;
            return { rows: node.childCount, cols: node.firstChild?.childCount ?? 0 };
        }
        return { rows: 0, cols: 0 };
    }

    /** Writes one attribute onto the selected node, leaving the rest of them alone. */
    setSelectedAttr(name: string, value: string) {
        const view = this.editor?.view;
        if (!view || !(view.state.selection instanceof NodeSelection)) return;
        const { from, node } = view.state.selection;
        const trimmed = value.trim();
        view.dispatch(view.state.tr.setNodeMarkup(from, undefined, { ...node.attrs, [name]: trimmed || null }));
    }

    selectionKindLabel(): string {
        const spec = this.selectionSpec();
        return spec ? this.t().editor.inspector.kinds[spec.kind] : '';
    }

    blockChipIcon(): IconName {
        const spec = this.selectionSpec();
        if (spec) return BLOCK_CHIP_ICONS[spec.kind] ?? 'cube';
        return this.currentBlockLevel() === 0 ? 'text-align-left' : 'text-h';
    }

    /**
     * Every row the selected block can answer, and no row it cannot. Resolution, byte size and the
     * originating file come from the asset lookup (T-240) and stand down entirely when it found
     * nothing — a blank row would assert the property exists and is unfilled.
     */
    selectionRows(): { label: string; value: string; warn?: boolean }[] {
        const spec = this.selectionSpec();
        if (!spec) return [];
        const i = this.t().editor.inspector;
        const rows: { label: string; value: string; warn?: boolean }[] = [];

        // Plain ink: the source is the file itself and cannot be typed over (ADR-238 clause 1).
        if (spec.source) rows.push({ label: i.source, value: spec.source });
        // Alt is drawn by the template instead: it is the one property here the writer has to be
        // able to change, and a read-only row saying "not filled in" is a complaint with no fix.
        if (spec.caption) rows.push({ label: i.caption, value: spec.caption });
        if (spec.text) rows.push({ label: i.summary, value: spec.text });
        if (spec.count !== undefined) rows.push({ label: this.countLabel(spec.kind), value: String(spec.count) });

        const meta = this.assetMeta();
        if (meta) {
            // Null width/height is audio, video, or a header that would not read — not a zero.
            if (meta.width && meta.height) {
                rows.push({ label: i.resolution, value: `${meta.width} × ${meta.height}` });
            }
            rows.push({ label: i.fileSize, value: this.formatFileSize(meta.sizeBytes) });
            rows.push({ label: i.asset, value: meta.fileName });
        }

        rows.push({ label: i.node, value: spec.typeName });
        return rows;
    }

    private countLabel(kind: SelectionKind): string {
        const i = this.t().editor.inspector;
        if (kind === 'poll') return i.options;
        if (kind === 'table') return i.rows;
        return i.frames;
    }

    tip(label: string, id: string): string {
        const combo = this.shortcuts[id];
        return combo ? `${label} (${combo.replace('Mod', this.modKey)})` : label;
    }

    currentBlockLevel(): number {
        this.tick();
        for (let level = 1; level <= 6; level++) {
            if (this.editor?.isActive('heading', { level })) return level;
        }
        return 0;
    }

    setBlockType(level: number) {
        if (level === 0) this.cmd(c => c.setParagraph());
        else this.cmd(c => c.toggleHeading({ level: level as 1 | 2 | 3 | 4 | 5 | 6 }));
    }

    retrySave() {
        this.saveAttempt = 0;
        this.saveRefusal.set(null);
        this.save();
    }

    async ngAfterViewInit() {
        if (this.toolStrip && typeof ResizeObserver !== 'undefined') {
            this.stripObserver = new ResizeObserver(() => this.measureToolbar());
            this.stripObserver.observe(this.toolStrip.nativeElement);
        }
        document.addEventListener('visibilitychange', this.onVisibilityChange);
        window.addEventListener('pagehide', this.onPageHide);

        // Refreshes the sidebar's tally on the way in — fire-and-forget, never blocks setup.
        this.feedback.refreshNewCount();
        this.clockTimer = setInterval(() => this.clock.set(Date.now()), 30_000);
        if (this.auth.indieDev()) {
            this.projectsApi.list().then(list => this.projectSummaries.set(list)).catch(() => { /* the kicker stays empty */ });
        }
        const mediaNodeTypes = new Set(['image', 'video', 'audio', 'carousel', 'collage']);
        // Mirrors AssetEndpoints.Allowed — anything else is left to the browser's default handling.
        const uploadTypes = new Set(['image/jpeg', 'image/png', 'image/gif', 'image/webp', 'video/mp4', 'audio/mpeg', 'audio/ogg']);
        this.editor = new Editor({
            element: this.editorHost.nativeElement,
            editorProps: {
                // Bug fix: with a media node selected (click on an image/video), typing a character
                // used to REPLACE the node (default ProseMirror behavior). Instead, put the caret
                // into a paragraph right below the node and let the character land there.
                handleKeyDown: (view, event) => {
                    const sel: any = view.state.selection;
                    if (!sel?.node || !mediaNodeTypes.has(sel.node.type?.name)) return false;
                    if (event.key.length !== 1 || event.ctrlKey || event.metaKey || event.altKey) return false;

                    const insertPos = sel.$to.pos;
                    const paragraph = view.state.schema.nodes['paragraph'].createAndFill();
                    if (!paragraph) return false;
                    let tr = view.state.tr.insert(insertPos, paragraph);
                    tr = tr.setSelection(TextSelection.create(tr.doc, insertPos + 1));
                    view.dispatch(tr);
                    return false; // let the typed character be inserted into the new paragraph
                },
                // Copying a whole line (e.g. triple-click, or Home/Shift+Down/End) hands the
                // browser a selection that includes the paragraph boundary on either side, so the
                // pasted slice arrives with an extra empty paragraph above and/or below the real
                // content — pasting then visibly adds blank lines around it. Only trim when the
                // slice's cut edges land on whole nodes (openStart/openEnd 0); a partial slice
                // (e.g. pasting mid-sentence) legitimately may start/end with an "empty" node.
                transformPasted: slice => {
                    if (slice.openStart !== 0 || slice.openEnd !== 0) return slice;
                    const isEmptyParagraph = (n: PMNode | null) => !!n && n.type.name === 'paragraph' && n.content.size === 0;
                    let content = slice.content;
                    while (content.childCount > 1 && isEmptyParagraph(content.firstChild)) {
                        content = content.cut(content.firstChild!.nodeSize);
                    }
                    while (content.childCount > 1 && isEmptyParagraph(content.lastChild)) {
                        content = content.cut(0, content.size - content.lastChild!.nodeSize);
                    }
                    return content === slice.content ? slice : new Slice(content, 0, 0);
                },
                // ADR-127 — a screenshot in the clipboard or files dragged onto the sheet go
                // through the same upload-with-progress path as the toolbar buttons. Only claimed
                // when there are acceptable files; plain text/HTML paste stays default.
                handlePaste: (_view, event) => {
                    const files = Array.from(event.clipboardData?.files ?? []).filter(f => uploadTypes.has(f.type));
                    if (!files.length) return false;
                    event.preventDefault();
                    for (const file of files) this.uploadAndInsert(file);
                    return true;
                },
                handleDrop: (view, event) => {
                    const files = Array.from(event.dataTransfer?.files ?? []).filter(f => uploadTypes.has(f.type));
                    if (!files.length) return false;
                    event.preventDefault();
                    // Land at the drop point, not wherever the caret happened to be.
                    const pos = view.posAtCoords({ left: event.clientX, top: event.clientY });
                    if (pos) view.dispatch(view.state.tr.setSelection(TextSelection.near(view.state.doc.resolve(pos.pos))));
                    for (const file of files) this.uploadAndInsert(file);
                    return true;
                },
            },
            extensions: [
                StarterKit,
                ImageNode,
                VideoNode,
                AudioNode,
                CarouselNode,
                CollageNode,
                SpoilerMark,
                FootnoteNode,
                WikiLinkNode,
                DateTimeNode,
                ToggleNode,
                PollNode,
                AnnotationNode,
                TableOfContentsNode,
                YoutubeNode,
                // T-100 — Ctrl+B and friends on a Cyrillic layout, where ProseMirror's own keymap
                // never sees them. See the extension for why this is not intermittent at all.
                LayoutShortcuts,
                Table.configure({ resizable: false }),
                TableRow,
                TableHeader,
                TableCell,
                TaskList,
                TaskItem.configure({ nested: true }),
                Mathematics,
                // Blog-only in intent (Marty, 28.07.2026) — Telegram has no alignment concept, so
                // CedarToTelegramHtmlRenderer/CedarToTelegramBlocksRenderer simply never read the
                // attr this adds. Scoped to paragraph/heading, matching the two block types
                // CedarToBlogHtmlRenderer emits with straightforward single-tag HTML.
                TextAlign.configure({ types: ['paragraph', 'heading'] }),
                // Wave 2 item 17 — the blockquote's `expandable` attr; Telegram-only in effect.
                ExpandableBlockquote,
            ],
            content: '',
            onTransaction: ({ transaction }) => {
                this.tick.update(v => v + 1);
                this.syncAssetMeta();
                this.scheduleRuDiffRecompute();
                // Two clocks (ADR-162 clause 2): the lit outline row rides `tick` because it is one
                // integer off the selection, while the list itself only rebuilds when the document
                // actually changed, and then only once the typing settles.
                if (transaction.docChanged) this.scheduleOutlineRebuild();
            },
            onUpdate: () => this.markDirty(),
            onFocus: () => {
                this.documentSelected.set(false);
                this.editorFocused.set(true);
            },
            onBlur: () => this.editorFocused.set(false),
        });

        // ADR-128 — `[[` opens the wiki-link suggester. The popover is hand-rolled off
        // props.clientRect (a signal-driven fixed-position div in the template); candidates are a
        // client-side filter over the already-loaded drafts list — no server search yet (T-176).
        this.editor.registerPlugin(Suggestion<{ id: string; title: string }, { draftId: string; label: string }>({
            editor: this.editor,
            pluginKey: new PluginKey('wikilink-suggest'),
            char: '[[',
            allowSpaces: true,
            items: ({ query }) => this.wikiCandidates(query),
            command: ({ editor, range, props }) => {
                editor.chain().focus()
                    .insertContentAt(range, [{ type: 'wikilink', attrs: props }, { type: 'text', text: ' ' }])
                    .run();
            },
            render: () => ({
                onStart: props => this.showWikiSuggest(props),
                onUpdate: props => this.showWikiSuggest(props),
                onExit: () => this.wikiSuggest.set(null),
                onKeyDown: ({ event }) => this.wikiSuggestKey(event),
            }),
        }));
        this.editor.view.dom.addEventListener(WIKILINK_OPEN_EVENT, this.onWikilinkOpen);

        const list = await this.draftsApi.list();
        this.drafts.set(list);
        // /drafts links here as /editor?draft=<id> — fall back to the most recent draft (previous
        // behavior) if the id is missing/stale (e.g. deleted from another tab).
        const requestedId = this.route.snapshot.queryParamMap.get('draft');
        const targetId = requestedId && list.some(d => d.id === requestedId) ? requestedId : list[0]?.id;
        if (targetId) await this.openDraft(targetId);
        else await this.newDraft();

        this.channels.set(await this.channelsApi.list());
    }

    ngOnDestroy() {
        document.removeEventListener('visibilitychange', this.onVisibilityChange);
        window.removeEventListener('pagehide', this.onPageHide);
        clearInterval(this.clockTimer);
        this.stripObserver?.disconnect();
        clearTimeout(this.saveTimer);
        clearTimeout(this.saveRetryTimer);
        clearTimeout(this.aiToastTimer);
        clearInterval(this.aiEditTicker);
        clearInterval(this.autoTranslateTicker);
        clearInterval(this.exportTicker);
        clearInterval(this.blogTicker);
        clearTimeout(this.ruDiffTimer);
        clearTimeout(this.outlineTimer);
        this.aiEditCancelled = true;
        this.autoTranslateCancelled = true;
        this.editor?.destroy();
    }

    markDirty() {
        this.saveState.set('dirty');
        clearTimeout(this.saveTimer);
        this.saveTimer = setTimeout(() => this.save(), AUTOSAVE_DEBOUNCE_MS);
    }

    // The version of the active language as the server last reported it — the token that makes a
    // save conditional (T-018.3). Empty means "no baseline", and the server then skips the check.
    private expectedUpdatedAt(): string | null {
        return this.lang() === this.primaryLanguage
            ? this.ruUpdatedAt() || null
            : this.translationOf(this.lang())?.updatedAt ?? null;
    }

    private async save(opts: { confirmShrink?: boolean; ignoreExpected?: boolean } = {}) {
        const id = this.currentId();
        if (!id || !this.editor) return;
        // A translation tab with no version yet - nothing to save (read-only there anyway)
        if (this.lang() !== this.primaryLanguage && !this.translationOf(this.lang())) {
            this.saveState.set('saved');
            return;
        }
        clearTimeout(this.saveTimer);
        clearTimeout(this.saveRetryTimer);
        this.saveState.set('saving');
        const guards: SaveGuards = {
            expectedUpdatedAt: opts.ignoreExpected ? null : this.expectedUpdatedAt(),
            confirmShrink: opts.confirmShrink ?? false,
        };
        try {
            const json = JSON.stringify(this.editor.getJSON());
            if (this.lang() === this.primaryLanguage) {
                const res = await this.draftsApi.update(id, this.title, json, guards);
                this.ruUpdatedAt.set(res.updatedAt);
                this.refreshMeta(id, res.updatedAt);
            } else {
                const lang = this.lang();
                const res = await this.draftsApi.saveTranslation(id, lang, this.title, json, guards);
                this.setTranslation(lang, { language: lang, title: this.title, updatedAt: res.updatedAt });
                this.activeSourceSnapshot.set(res.sourceSnapshotJson);
            }
            this.saveAttempt = 0;
            this.saveState.set('saved');
            this.lastSavedAt.set(Date.now());
            this.savedVersion.update(v => v + 1);
            void this.loadDraftGlossary();
        } catch (e) {
            // A refusal is not a failure to reach the server — the server understood and said no.
            // Retrying it would just fail identically; the author decides (T-018.1/T-018.3).
            const refusal = saveRefusalOf(e);
            if (refusal) {
                this.saveRefusal.set(refusal);
                this.saveState.set('dirty');
                return;
            }
            this.saveState.set('error');
            this.scheduleSaveRetry();
        }
    }

    // T-018.6 — a failed autosave used to leave "Sync failed" on screen and wait for the next
    // keystroke to try again. Retries on its own with a widening gap, then stops and leaves the
    // manual button; the state stays 'error' throughout, so the bar never lies about being saved.
    private scheduleSaveRetry() {
        if (this.saveAttempt >= SAVE_RETRY_DELAYS_MS.length) return;
        const delay = SAVE_RETRY_DELAYS_MS[this.saveAttempt++];
        clearTimeout(this.saveRetryTimer);
        this.saveRetryTimer = setTimeout(() => this.save(), delay);
    }

    // The author agreed to a save the shrink guard stopped.
    confirmRefusedSave() {
        const refusal = this.saveRefusal();
        this.saveRefusal.set(null);
        this.saveAttempt = 0;
        this.save(refusal?.code === 'stale' ? { ignoreExpected: true } : { confirmShrink: true });
    }

    // "Give me back what's stored" — the recovery the 29.07 wipe had no button for. Deliberately
    // not openDraft(), which refuses to reopen the draft already open and would flush the very
    // save being discarded.
    async reloadStoredVersion() {
        const id = this.currentId();
        this.saveRefusal.set(null);
        clearTimeout(this.saveTimer);
        clearTimeout(this.saveRetryTimer);
        this.saveAttempt = 0;
        if (!id || !this.editor) return;
        const lang = this.lang();
        if (lang === this.primaryLanguage) {
            const draft = await this.draftsApi.get(id);
            this.title = draft.title;
            this.ruUpdatedAt.set(draft.updatedAt);
            this.editor.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC), { emitUpdate: false });
        } else {
            const translation = await this.draftsApi.getTranslation(id, lang);
            this.title = translation.title;
            this.setTranslation(lang, { language: lang, title: translation.title, updatedAt: translation.updatedAt });
            this.activeSourceSnapshot.set(translation.sourceSnapshotJson);
            this.editor.commands.setContent(JSON.parse(translation.cedarJson || EMPTY_DOC), { emitUpdate: false });
        }
        this.resetHistory();
        this.saveState.set('saved');
    }

    // T-018.2 — iOS kills a backgrounded tab's timers, so a pending 1.2s autosave simply never
    // fires and the edits die with the tab (the 29.07 incident's second half). Both events still
    // run while the page is alive; `keepalive` lets the request outlive it.
    private onVisibilityChange = () => {
        if (document.visibilityState === 'hidden') this.flushPendingSave();
    };
    private onPageHide = () => this.flushPendingSave();

    private flushPendingSave() {
        if (this.saveState() !== 'dirty' || !this.editor) return;
        const id = this.currentId();
        if (!id) return;
        const lang = this.lang();
        if (lang !== this.primaryLanguage && !this.translationOf(lang)) return;

        clearTimeout(this.saveTimer);
        const body = JSON.stringify({
            title: this.title,
            cedarJson: JSON.stringify(this.editor.getJSON()),
            expectedUpdatedAt: this.expectedUpdatedAt(),
        });
        // A keepalive body is capped at 64KB by the browser and a rejected request saves nothing
        // at all, so past the cap the ordinary save is the best effort available — it usually wins
        // the race anyway, since the page is still running when this fires.
        if (body.length > KEEPALIVE_MAX_CHARS) { void this.save(); return; }

        const url = lang === this.primaryLanguage
            ? `/api/drafts/${id}` : `/api/drafts/${id}/translations/${lang}`;
        // Raw fetch rather than HttpClient: only fetch can set `keepalive`. It bypasses the debug
        // console's interceptor, which is the price of the request surviving the page.
        void fetch(url, {
            method: 'PUT', keepalive: true, credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' }, body,
        }).then(res => { if (res.ok) this.saveState.set('saved'); }).catch(() => { });
    }

    showEmptyState(): boolean {
        return this.lang() !== this.primaryLanguage && !this.translationOf(this.lang());
    }

    // Cycled by elapsed seconds (no separate timer) so a long action shows visible progress
    // instead of a static label. The message lists live in the dictionaries — see editor.statuses.
    translateStatusMessage(): string {
        const list = this.t().editor.statuses.translate;
        return list[Math.floor(this.autoTranslateElapsed() / 4) % list.length];
    }

    exportStatusMessage(): string {
        const list = this.t().editor.statuses.export;
        return list[Math.floor(this.exportElapsed() / 2) % list.length];
    }

    blogStatusMessage(): string {
        const list = this.t().editor.statuses.blog;
        return list[Math.floor(this.blogElapsed() / 2) % list.length];
    }

    translationOf(lang: string): TranslationMeta | null {
        return this.translations()[lang] ?? null;
    }

    readonly contentLanguages = CONTENT_LANGUAGES;
    // Per-draft, never a UI/global preference. Existing drafts arrive as `ru` from the migration.
    primaryLanguage = DEFAULT_PRIMARY_LANGUAGE;
    revisions = signal<DraftRevision[]>([]);
    revisionsOpen = signal(false);
    // Every already-live language the pending publish would overwrite — all of them, because one
    // click publishes them all and a single language's diff can hide changes in the others.
    updateConfirm = signal<UpdatePreview[] | null>(null);
    // language → the version the owner was shown; travels with the publish so the server can
    // refuse anything else (ADR-065).
    private confirmedFingerprints: Record<string, string> = {};

    endonym(lang: string): string {
        return endonymOf(lang);
    }

    // Translation tabs actually present on this draft, in the canonical order rather than
    // whatever order the server happened to return them in.
    existingLanguages(): string[] {
        const have = this.translations();
        return CONTENT_LANGUAGES.filter(l => l !== this.primaryLanguage && have[l]);
    }

    // Languages with no version yet - offered by the "add a translation" control.
    missingLanguages(): string[] {
        const have = this.translations();
        return CONTENT_LANGUAGES.filter(l => l !== this.primaryLanguage && !have[l]);
    }

    /** For the export window's one-line note — nine of these as chips drowned the two real ones. */
    missingLanguagesLabel(): string {
        return this.missingLanguages().map(l => l.toUpperCase()).join(', ');
    }

    /** T-350 — every content language for the scalable menu: which have a version, which are stale. */
    langMenuItems(): { code: string; hasContent: boolean; stale: boolean }[] {
        const have = this.translations();
        return CONTENT_LANGUAGES.map(code => ({
            code,
            hasContent: code === this.primaryLanguage || !!have[code],
            stale: code !== this.primaryLanguage && !!have[code] && this.isStale(code),
        }));
    }

    // The primary version was edited after this translation was last touched - probably needs
    // re-translating.
    isStale(lang: string): boolean {
        const en = this.translationOf(lang);
        if (!en) return false;
        // Compared as instants, not as strings (IB3): the two timestamps reach the client through
        // different endpoints, and a string compare would flip on nothing more than a difference
        // in fractional-second digits or a trailing Z.
        const ru = Date.parse(this.ruUpdatedAt());
        const tr = Date.parse(en.updatedAt);
        if (!Number.isFinite(ru) || !Number.isFinite(tr)) return this.ruUpdatedAt() > en.updatedAt;
        return ru > tr;
    }

    async switchLang(target: string) {
        const id = this.currentId();
        if (target === this.lang() || !id || !this.editor) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        if (target === this.primaryLanguage) {
            const draft = await this.draftsApi.get(id);
            this.lang.set(this.primaryLanguage);
            this.title = draft.title;
            this.ruUpdatedAt.set(draft.updatedAt);
            this.editor.setEditable(true);
            this.editor.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC), { emitUpdate: false });
            this.resetHistory();
        } else {
            this.ruSnapshot = { title: this.title, json: JSON.stringify(this.editor.getJSON()) };
            if (this.translationOf(target)) {
                const tr = await this.draftsApi.getTranslation(id, target);
                this.lang.set(target);
                this.title = tr.title;
                this.setTranslation(target, { language: target, title: tr.title, updatedAt: tr.updatedAt });
                this.activeSourceSnapshot.set(tr.sourceSnapshotJson);
                this.editor.setEditable(true);
                this.editor.commands.setContent(JSON.parse(tr.cedarJson || EMPTY_DOC), { emitUpdate: false });
                this.resetHistory();
            } else {
                // No version in this language yet - empty state (Copy from primary / Start empty)
                this.lang.set(target);
                this.title = this.ruSnapshot.title;
                this.activeSourceSnapshot.set(null);
                this.editor.setEditable(false);
                this.editor.commands.setContent(JSON.parse(EMPTY_DOC), { emitUpdate: false });
                this.resetHistory();
            }
        }
        this.saveState.set('saved');
        this.scheduleRuDiffRecompute();
        await this.loadDraftGlossary();
    }

    async makePrimary(language: string) {
        const id = this.currentId();
        if (!id || language === this.primaryLanguage) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();
        const result = await this.draftsApi.setPrimaryLanguage(id, language);
        this.primaryLanguage = result.primaryLanguage;
        const fresh = await this.draftsApi.get(id);
        this.translations.set(Object.fromEntries((fresh.translations ?? []).map(t => [t.language, t])));
        this.drafts.update(list => list.map(d => d.id === id
            ? { ...d, primaryLanguage: this.primaryLanguage, title: fresh.title, updatedAt: fresh.updatedAt }
            : d));
        this.lang.set(this.primaryLanguage);
        this.title = fresh.title;
        // Without this the stale indicator keeps comparing against the *old* primary's timestamp
        // and every language tab reads dirty after what is a pure relabeling.
        this.ruUpdatedAt.set(fresh.updatedAt);
        this.activeSourceSnapshot.set(null);
        this.ruDiffMarkers.set([]);
        this.editor?.commands.setContent(JSON.parse(fresh.cedarJson || EMPTY_DOC), { emitUpdate: false });
        this.resetHistory();
    }

    revisionKindLabel(kind: string): string {
        const v = this.t().editor.versions;
        return kind === 'telegram' ? v.kindTelegram : kind === 'blog' ? v.kindBlog
            : kind === 'restore' ? v.kindRestore : v.kindSave;
    }

    async openRevisions() {
        const id = this.currentId();
        if (!id) return;
        this.selectedRevision.set(null);
        this.compareWith.set('current');
        this.revisions.set(await this.draftsApi.revisions(id, this.lang()));
        this.revisionsOpen.set(true);
    }

    // T-016/T-017 — the row that's open, what it's being compared against, and the resulting diff.
    // 'current' is not a revision id: it means whatever is stored for this language right now.
    selectedRevision = signal<DraftRevisionDetail | null>(null);
    compareWith = signal<string>('current');
    revisionDiff = signal<RevisionDiff | null>(null);
    revisionBusy = signal(false);
    revisionError = signal<string | null>(null);

    async openRevision(revisionId: string) {
        const id = this.currentId();
        if (!id) return;
        this.revisionBusy.set(true);
        this.revisionError.set(null);
        try {
            const detail = await this.draftsApi.revision(id, this.lang(), revisionId);
            this.selectedRevision.set(detail);
            this.compareWith.set('current');
            this.revisionDiff.set(detail.diffToCurrent);
        } catch {
            this.revisionError.set(this.t().editor.versions.loadFailed);
        } finally {
            this.revisionBusy.set(false);
        }
    }

    // The other end of the comparison — any older row, or the stored version.
    async compareRevisionWith(other: string) {
        const id = this.currentId();
        const selected = this.selectedRevision();
        if (!id || !selected) return;
        this.compareWith.set(other);
        this.revisionBusy.set(true);
        try {
            const res = await this.draftsApi.revisionDiff(id, this.lang(), selected.id, other);
            this.revisionDiff.set(res.diff);
        } catch {
            this.revisionError.set(this.t().editor.versions.loadFailed);
        } finally {
            this.revisionBusy.set(false);
        }
    }

    // Text of the opened version, for reading it before deciding to restore. Rendering it through
    // a second TipTap instance would buy formatting at the cost of a whole editor — the question
    // here is "is this the version I lost", which the words answer.
    revisionPreviewText(): string {
        const json = this.selectedRevision()?.cedarJson;
        return json ? plainTextOf(json) : '';
    }

    async restoreRevision() {
        const id = this.currentId();
        const selected = this.selectedRevision();
        if (!id || !selected || !this.editor) return;
        this.revisionBusy.set(true);
        try {
            const res = await this.draftsApi.restoreRevision(id, selected.id);
            this.title = res.title;
            if (res.language === this.primaryLanguage) {
                this.ruUpdatedAt.set(res.updatedAt);
                this.refreshMeta(id, res.updatedAt);
            } else {
                this.setTranslation(res.language, { language: res.language, title: res.title, updatedAt: res.updatedAt });
            }
            this.editor.commands.setContent(JSON.parse(res.cedarJson || EMPTY_DOC), { emitUpdate: false });
            this.resetHistory();
            this.saveState.set('saved');
            this.revisionsOpen.set(false);
            this.selectedRevision.set(null);
        } catch {
            this.revisionError.set(this.t().editor.versions.restoreFailed);
        } finally {
            this.revisionBusy.set(false);
        }
    }

    // Keeps the drafts list's language badges in step with what actually exists.
    private setTranslation(lang: string, meta: TranslationMeta | null) {
        this.translations.update(map => {
            const next = { ...map };
            if (meta) next[lang] = meta;
            else delete next[lang];
            return next;
        });
        const id = this.currentId();
        if (id) {
            const languages = Object.keys(this.translations());
            this.drafts.update(list => list.map(d => d.id === id ? { ...d, languages } : d));
        }
    }

    async startVersion(copyFromRu: boolean) {
        const id = this.currentId();
        const lang = this.lang();
        if (!id || !this.editor || lang === this.primaryLanguage) return;
        // Use the title as currently shown/edited, not the RU snapshot — the title field stays
        // live in the "no EN version yet" empty state, so the user may already have renamed it
        // for the English version before clicking either button here.
        const title = this.title;
        const json = copyFromRu ? (this.ruSnapshot?.json ?? EMPTY_DOC) : EMPTY_DOC;
        try {
            const res = await this.draftsApi.saveTranslation(id, lang, title, json);
            this.setTranslation(lang, { language: lang, title, updatedAt: res.updatedAt });
            this.activeSourceSnapshot.set(res.sourceSnapshotJson);
            this.title = title;
            this.editor.setEditable(true);
            this.editor.commands.setContent(JSON.parse(json), { emitUpdate: false });
            this.resetHistory();
            this.editor.commands.focus();
            this.saveState.set('saved');
        } catch {
            this.saveState.set('error');
        }
    }

    // The shared tag picker reports the whole list rather than one add/remove at a time, so the
    // save is one call regardless of what changed.
    async setTags(tags: string[]) {
        this.tagList.set(tags.map(t => t.replace(/^#/, '').toLowerCase()));
        await this.persistTags();
    }

    private async persistTags() {
        const id = this.currentId();
        if (!id) return;
        try {
            const res = await this.draftsApi.updateTags(id, this.tagList().join(','));
            this.drafts.update(list => list.map(d => d.id === id ? { ...d, tags: res.tags } : d));
            // A tag invented here should be offered to the next draft, not stay on this one.
            this.tagUsageApi.refresh();
        } catch {
            this.saveState.set('error');
        }
    }

    async assignFolder(folderId: string | null) {
        const id = this.currentId();
        if (!id || this.currentFolderId() === folderId) return;
        try {
            await this.draftsApi.setDraftFolder(id, folderId);
            this.currentFolderId.set(folderId);
            this.drafts.update(list => list.map(d => d.id === id ? { ...d, folderId } : d));
        } catch {
            this.saveState.set('error');
        }
    }

    async assignSeries(seriesId: string | null) {
        const id = this.currentId();
        if (!id || this.currentSeriesId() === seriesId) return;
        try {
            await this.draftsApi.setDraftSeries(id, seriesId);
            this.currentSeriesId.set(seriesId);
            this.drafts.update(list => list.map(d => d.id === id ? { ...d, seriesId } : d));
        } catch {
            this.saveState.set('error');
        }
    }

    async assignDocumentType(documentType: DocumentType) {
        const id = this.currentId();
        if (!id || this.documentType() === documentType) return;
        this.documentTypeError.set(null);
        try {
            await this.projectsApi.setDocumentType(id, documentType);
            this.documentType.set(documentType);
            this.drafts.update(list => list.map(d => d.id === id ? { ...d, documentType } : d));
        } catch (e) {
            // The one refusal with a story: a blog-published post cannot become working material.
            this.documentTypeError.set(httpErrorMessage(e, this.t().editor.inspector.typeChangeFailed));
        }
    }

    /**
     * T-239 — the blog address, retyped. Every rule stays on the server (slugify, refuse empty,
     * refuse taken, refuse before the post has a slug at all), so the field shows the stored answer
     * back: what came home after a save, or what was there before a refusal. Never the keystrokes,
     * which are the one value the document does not hold.
     */
    async assignBlogSlug(input: HTMLInputElement) {
        const id = this.currentId();
        const blog = this.currentBlog();
        if (!id || !blog) return;
        const typed = input.value.trim();
        if (!typed || typed === blog.slug) { input.value = blog.slug; return; }
        this.slugError.set(null);
        try {
            const res = await this.draftsApi.setBlogSlug(id, typed);
            this.currentBlog.set({ ...blog, slug: res.blogSlug });
            input.value = res.blogSlug;
        } catch (e) {
            this.slugError.set(httpErrorMessage(e, this.t().editor.inspector.slugChangeFailed));
            input.value = blog.slug;
        }
    }

    // Machine-translates the RU version into EN and loads the result into the editor for review.
    // Replacing an existing translation goes through a confirm modal first (see confirmTranslate()).
    autoTranslate() {
        if (!this.currentId() || !this.editor) return;
        if (this.lang() === this.primaryLanguage) return;
        if (this.translationOf(this.lang())) {
            this.translateConfirmOpen.set(true);
            return;
        }
        this.runAutoTranslate();
    }

    cancelTranslateConfirm() {
        this.translateConfirmOpen.set(false);
    }

    // T-014 — translate the primary version into every ticked language in one press. Modelled on
    // the glossary's batch translate (ADR-062): a checkbox list, one existing per-language call
    // after another, and a stop on the first failure with the rest still ticked.
    translateAllOpen = signal(false);
    translateAllTargets = signal<string[]>([]);
    translateAllBusy = signal(false);
    translateAllCurrent = signal<string | null>(null);
    translateAllDone = signal<string[]>([]);
    translateAllError = signal<string | null>(null);

    translateAllCandidates(): string[] {
        return this.contentLanguages.filter(l => l !== this.primaryLanguage);
    }

    openTranslateAll() {
        if (!this.currentId()) return;
        this.translateAllTargets.set([]);
        this.translateAllDone.set([]);
        this.translateAllError.set(null);
        this.translateAllOpen.set(true);
    }

    toggleTranslateAllTarget(lang: string) {
        this.translateAllTargets.update(list =>
            list.includes(lang) ? list.filter(l => l !== lang) : [...list, lang]);
    }

    // Ticked = still to do, so the count is also what the run will cost in daily AI calls.
    translateAllCost(): number {
        return this.translateAllTargets().length;
    }

    async runTranslateAll() {
        const id = this.currentId();
        if (!id || this.translateAllBusy()) return;
        const targets = this.translateAllCandidates().filter(l => this.translateAllTargets().includes(l));
        if (!targets.length) return;

        // The server translates what is *stored*, so a pending edit has to land first — otherwise
        // the whole batch is a translation of the previous version (same defect ADR-065 fixed
        // for publishing).
        if (this.saveState() !== 'saved') await this.save();

        this.translateAllBusy.set(true);
        this.translateAllError.set(null);
        try {
            for (const target of targets) {
                this.translateAllCurrent.set(target);
                const { jobId } = await this.draftsApi.startAutoTranslate(id, target);
                const translation = await this.pollAiJob<TranslationFull>(jobId, () => false, AUTO_TRANSLATE_TIMEOUT_MS);
                if (!translation) break;
                this.setTranslation(target, { language: target, title: translation.title, updatedAt: translation.updatedAt });
                this.translateAllDone.update(list => [...list, target]);
                // Untick as we go: whatever stays ticked is exactly the work left after a failure.
                this.translateAllTargets.update(list => list.filter(l => l !== target));
            }
        } catch (e) {
            this.translateAllError.set(e instanceof AiJobTimeoutError
                ? this.t().editor.errors.autoTranslateTimeout
                : httpErrorMessage(e, this.t().editor.errors.autoTranslate));
        } finally {
            this.translateAllBusy.set(false);
            this.translateAllCurrent.set(null);
            // The open tab's content is stale if this run rewrote it.
            if (this.translateAllDone().includes(this.lang())) await this.reloadStoredVersion();
        }
    }

    confirmTranslate() {
        this.translateConfirmOpen.set(false);
        this.runAutoTranslate();
    }

    // ADR-058-follow-up (29.07.2026) — used to be one held-open POST; a large document's
    // translation can legitimately outrun Cloudflare Tunnel's own edge timeout, which then 502s
    // the browser even though the server finishes and saves the result seconds later (confirmed
    // directly against a real ~360-line document). Starts a background job and polls for it
    // instead, so no single request needs to stay open longer than a couple of seconds.
    private async runAutoTranslate() {
        const id = this.currentId();
        const editor = this.editor;
        if (!id || !editor) return;

        this.autoTranslating.set(true);
        this.autoTranslateElapsed.set(0);
        this.autoTranslateProgress.set(0);
        clearInterval(this.autoTranslateTicker);
        this.autoTranslateTicker = setInterval(() => {
            this.autoTranslateElapsed.update(s => s + 1);
            this.autoTranslateProgress.set(pseudoProgress(this.autoTranslateElapsed()));
        }, 1000);
        this.autoTranslateError.set(null);
        this.autoTranslateCancelled = false;

        const target = this.lang();
        try {
            const { jobId } = await this.draftsApi.startAutoTranslate(id, target);
            this.autoTranslateJobId = jobId;
            const tr = await this.pollAiJob<TranslationFull>(jobId, () => this.autoTranslateCancelled, AUTO_TRANSLATE_TIMEOUT_MS);
            if (tr === null) return; // cancelled — no error, the user just changed their mind

            this.autoTranslateProgress.set(100);
            this.setTranslation(target, { language: target, title: tr.title, updatedAt: tr.updatedAt });
            this.activeSourceSnapshot.set(tr.sourceSnapshotJson);
            if (this.lang() !== target) {
                this.ruSnapshot = { title: this.title, json: JSON.stringify(editor.getJSON()) };
                this.lang.set(target);
            }
            this.title = tr.title;
            editor.setEditable(true);
            editor.commands.setContent(JSON.parse(tr.cedarJson || EMPTY_DOC), { emitUpdate: false });
            this.saveState.set('saved');
        } catch (e) {
            this.autoTranslateError.set(e instanceof AiJobTimeoutError
                ? this.t().editor.errors.autoTranslateTimeout
                : httpErrorMessage(e, this.t().editor.errors.autoTranslate));
        } finally {
            this.finishAutoTranslate();
        }
    }

    // Shared by auto-translate and AI-edit — polls GET /api/ai-jobs/{jobId} until it completes,
    // fails, is cancelled (returns null; not an error), or the client gives up waiting. Defaults to
    // AI_OPERATION_TIMEOUT_MS (10 min, matching the backend's AI-edit cap); auto-translate passes
    // its own longer AUTO_TRANSLATE_TIMEOUT_MS (20 min) explicitly.
    private async pollAiJob<T>(jobId: string, isCancelled: () => boolean, timeoutMs = AI_OPERATION_TIMEOUT_MS, intervalMs = 2000): Promise<T | null> {
        const deadline = Date.now() + timeoutMs;
        while (true) {
            if (isCancelled()) return null;
            if (Date.now() > deadline) throw new AiJobTimeoutError();
            await new Promise(resolve => setTimeout(resolve, intervalMs));
            if (isCancelled()) return null;
            const job = await this.draftsApi.getAiJob<T>(jobId);
            if (job.status === 'completed') return job.result as T;
            if (job.status === 'failed') throw new Error(job.error ?? 'Job failed');
        }
    }

    private finishAutoTranslate() {
        this.autoTranslating.set(false);
        clearInterval(this.autoTranslateTicker);
        this.autoTranslateJobId = null;
    }

    // User-initiated cancel (Step 8) — cancels the background job server-side and stops polling;
    // no error/toast shown since this wasn't a failure, the user just changed their mind.
    cancelAutoTranslate() {
        this.autoTranslateCancelled = true;
        if (this.autoTranslateJobId) this.draftsApi.cancelAiJob(this.autoTranslateJobId).catch(() => {});
        this.finishAutoTranslate();
    }

    // Opens the AI confirm dialog (replaces window.confirm — the only native browser dialog
    // in the app otherwise); confirmAiEdit() below actually runs aiEdit() once accepted.
    askAiEdit(kind: AiEditKind) {
        this.aiConfirmKind.set(kind);
    }

    cancelAiConfirm() {
        this.aiConfirmKind.set(null);
    }

    confirmAiEdit() {
        const kind = this.aiConfirmKind();
        this.aiConfirmKind.set(null);
        if (kind) this.aiEdit(kind);
    }

    aiConfirmTitle(): string {
        return this.aiConfirmKind() === 'fix-errors' ? this.t().editor.ai.fixTitle : this.t().editor.ai.schizoTitle;
    }

    aiConfirmBody(): string {
        const words = this.wordCount();
        const lang = this.lang().toUpperCase();
        return this.aiConfirmKind() === 'fix-errors'
            ? this.t().editor.ai.fixBody(lang, words)
            : this.t().editor.ai.schizoBody(lang, words);
    }

    // Rewrites the current language version in place via an LLM (Pro Plus, daily quota) — grammar
    // fix or "schizoposting" style rewrite. Same persist-then-load pattern as auto-translate, so
    // Ctrl+Z in the editor can still undo the content swap if the user doesn't like the result.
    // Job/poll shape (ADR-058-follow-up) — see runAutoTranslate()'s comment for why.
    private async aiEdit(kind: AiEditKind) {
        const id = this.currentId();
        const editor = this.editor;
        if (!id || !editor) return;
        const label = kind === 'fix-errors' ? this.t().editor.ai.fixLabel : this.t().editor.ai.schizoLabel;

        this.aiEditBusy.set(true);
        this.aiEditElapsed.set(0);
        this.aiEditProgress.set(0);
        clearInterval(this.aiEditTicker);
        this.aiEditTicker = setInterval(() => {
            this.aiEditElapsed.update(s => s + 1);
            this.aiEditProgress.set(pseudoProgress(this.aiEditElapsed()));
        }, 1000);
        this.aiEditError.set(null);
        this.aiEditCancelled = false;

        try {
            const { jobId } = await this.draftsApi.startAiEdit(id, this.lang(), kind);
            this.aiEditJobId = jobId;
            const res = await this.pollAiJob<AiEditResult>(jobId, () => this.aiEditCancelled);
            if (res === null) return; // cancelled — no error, the user just changed their mind

            this.aiEditProgress.set(100);
            this.title = res.title;
            editor.commands.setContent(JSON.parse(res.cedarJson || EMPTY_DOC), { emitUpdate: false });
            this.saveState.set('saved');
            if (this.lang() === 'en') {
                this.setTranslation(this.lang(), { language: this.lang(), title: res.title, updatedAt: res.updatedAt });
            }
            this.refreshMeta(id);
            this.showAiToast(kind === 'fix-errors' ? 'Fixed your typos. Your voice survived. Moo.' : 'Schizo-izer done. Reality is now optional.');
        } catch (e) {
            this.aiEditError.set(e instanceof AiJobTimeoutError
                ? `${label} timed out after 10 minutes`
                : httpErrorMessage(e, `${label} failed`));
        } finally {
            this.finishAiEdit();
        }
    }

    private finishAiEdit() {
        this.aiEditBusy.set(false);
        clearInterval(this.aiEditTicker);
        this.aiEditJobId = null;
    }

    // User-initiated cancel (Step 8) — see cancelAutoTranslate() above for the same reasoning.
    cancelAiEdit() {
        this.aiEditCancelled = true;
        if (this.aiEditJobId) this.draftsApi.cancelAiJob(this.aiEditJobId).catch(() => {});
        this.finishAiEdit();
    }

    private showAiToast(text: string) {
        clearTimeout(this.aiToastTimer);
        this.aiToast.set(text);
        this.aiToastTimer = setTimeout(() => this.aiToast.set(null), 3000);
    }

    async deleteVersion() {
        const id = this.currentId();
        const lang = this.lang();
        if (!id || lang === this.primaryLanguage || !this.translationOf(lang)) return;
        if (!await this.confirmation.confirm(this.t().editor.lang.deleteEnglishConfirm)) return;
        clearTimeout(this.saveTimer);
        this.saveState.set('saved'); // discard pending EN edits so nothing re-creates the row
        await this.draftsApi.removeTranslation(id, lang);
        this.setTranslation(lang, null);
        this.activeSourceSnapshot.set(null);
        this.ruDiffMarkers.set([]);
        this.exportLangs.update(list => {
            const next = list.filter(l => l !== lang);
            return next.length ? next : [this.primaryLanguage];
        });
        await this.switchLang(this.primaryLanguage);
    }

    private refreshMeta(id: string, updatedAt = new Date().toISOString()) {
        this.drafts.update(list => list
            .map(d => d.id === id
                ? { ...d, title: this.title, updatedAt }
                : d)
            .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)));
    }

    async openDraft(id: string) {
        if (this.draftsBusy() || id === this.currentId()) return;
        this.draftsBusy.set(true);
        try {
            clearTimeout(this.saveTimer);
            if (this.saveState() !== 'saved') await this.save();

            const draft = await this.draftsApi.get(id);
            this.currentId.set(id);
            this.title = draft.title;
            this.primaryLanguage = draft.primaryLanguage || DEFAULT_PRIMARY_LANGUAGE;
            this.lang.set(this.primaryLanguage);
            this.exportLangs.set([this.primaryLanguage]);
            this.ruUpdatedAt.set(draft.updatedAt);
            this.translations.set(Object.fromEntries((draft.translations ?? []).map(t => [t.language, t])));
            this.activeSourceSnapshot.set(null);
            this.ruSnapshot = null;
            this.tagList.set(draft.tags ? draft.tags.split(',').filter(t => t.length > 0) : []);
            this.currentFolderId.set(draft.folderId);
            this.currentSeriesId.set(draft.seriesId);
            this.backlinks.set([]);
            this.draftsApi.getBacklinks(id).then(list => {
                if (this.currentId() === id) this.backlinks.set(list);
            }).catch(() => { /* best-effort — the chip simply stays hidden */ });
            this.isPrivate.set(draft.isPrivate);
            this.documentType.set(draft.documentType ?? 'post');
            this.documentTypeError.set(null);
            this.slugError.set(null);
            this.watermarkText.set(draft.watermarkText);
            this.watermarkInput = draft.watermarkText ?? '';
            this.locationText.set(draft.locationText ?? '');
            this.watermarkError.set(null);
            this.invites.set([]);
            this.previewLinkUrl.set(null);
            // 404 is "no link" — the inspector simply stays without one.
            this.previewApi.previewLink(id).then(r => { if (r && this.currentId() === id) this.previewLinkUrl.set(r.url); }).catch(() => {});
            this.previewLinkError.set(null);
            this.notifySubscribers.set(false);
            this.copyText.set('');
            this.copyTextError.set('');
            this.regForm.set(parseRegistrationForm(draft.registrationFormJson));
            this.formLanguages.set(draft.formLanguages ?? []);
            this.isListedWhilePrivate.set(draft.isListedWhilePrivate ?? false);
            this.disableCopy.set(draft.disableCopy ?? false);
            this.editor?.setEditable(true);
            this.editor?.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC), { emitUpdate: false });
            this.resetHistory();
            this.saveState.set('saved');
            this.lastSavedAt.set(Number.isFinite(Date.parse(draft.updatedAt)) ? Date.parse(draft.updatedAt) : null);
            this.currentBlog.set(draft.blogSlug ? { slug: draft.blogSlug, isPublished: draft.isBlogPublished } : null);
            this.blogError.set(null);
            void this.loadDraftGlossary();

            // Fetch the EN translation's source snapshot in the background (not blocking open)
            // just to populate the RU-tab diff gutter — the list endpoint only returns TranslationMeta.
            // The gutter needs one translation's snapshot to diff against; with several, the
            // first is as good a default as any and switching tabs replaces it.
            const firstTranslation = Object.keys(this.translations())[0];
            if (firstTranslation) {
                this.draftsApi.getTranslation(id, firstTranslation)
                    .then(tr => { this.activeSourceSnapshot.set(tr.sourceSnapshotJson); this.scheduleRuDiffRecompute(); })
                    .catch(() => {});
            }
        } finally {
            this.draftsBusy.set(false);
        }
    }

    // opts was used by the New Draft dialog before it moved to /drafts (28.07.2026); the two
    // silent fallback call sites left here (empty draft list on load, deleting the last remaining
    // draft) call this with no args and get exactly the old blank-"Untitled" behavior.
    async newDraft(opts?: { title?: string; cedarJson?: string; tags?: string; languages?: 'ru' | 'en' | 'both'; isPrivate?: boolean; folderId?: string | null }) {
        if (this.draftsBusy()) return;
        this.draftsBusy.set(true);
        try {
            clearTimeout(this.saveTimer);
            if (this.saveState() !== 'saved') await this.save();

            const title = opts?.title?.trim() || 'Untitled';
            const cedarJson = opts?.cedarJson ?? EMPTY_DOC;
            const tags = (opts?.tags ?? '').split(',').map(t => t.trim().toLowerCase()).filter(t => t.length > 0);
            const isPrivate = opts?.isPrivate ?? false;
            const folderId = opts?.folderId ?? null;

            const created = await this.draftsApi.create(title, cedarJson);
            // Same follow-up-call shape as tags: create first, then apply the extras the
            // create endpoint doesn't take.
            if (tags.length) await this.draftsApi.updateTags(created.id, tags.join(','));
            if (isPrivate) await this.draftsApi.setDraftPrivate(created.id, true);
            if (folderId) await this.draftsApi.setDraftFolder(created.id, folderId);

            let languages: string[] = [];
            if (opts?.languages === 'both') {
                await this.draftsApi.saveTranslation(created.id, 'en', title, EMPTY_DOC);
                languages = ['en'];
            }

            const stored = await this.draftsApi.get(created.id);
            const meta: DraftMeta = {
                id: created.id, title,
                createdAt: stored.createdAt, updatedAt: stored.updatedAt,
                primaryLanguage: stored.primaryLanguage,
                blogSlug: null, isBlogPublished: false, blogPublishedAt: null,
                languages, tags: tags.join(','),
                isArchived: false, lastTelegramMessageId: null, lastTelegramUsername: null,
                staleLanguages: [], scheduled: null, folderId, seriesId: null, projectId: null, parentDraftId: null, siblingOrder: 0,
                isPrivate, isTemplate: false, disableCopy: false,
                disableReactions: false, disableComments: false, documentType: 'post',
                viewCount: 0, reactionCount: 0, newViewCount: 0, newReactionCount: 0,
                coverImagePath: null,
            };
            this.drafts.update(l => [meta, ...l]);
            this.currentId.set(created.id);
            this.title = title;
            this.primaryLanguage = stored.primaryLanguage || DEFAULT_PRIMARY_LANGUAGE;
            this.lang.set(this.primaryLanguage);
            this.exportLangs.set([this.primaryLanguage]);
            this.ruUpdatedAt.set(meta.updatedAt);
            this.translations.set(Object.fromEntries((stored.translations ?? []).map(translation => [translation.language, translation])));
            this.activeSourceSnapshot.set(null);
            this.ruDiffMarkers.set([]);
            this.ruSnapshot = null;
            this.tagList.set(tags);
            this.currentFolderId.set(folderId);
            this.currentSeriesId.set(null);
            this.documentType.set('post');
            this.documentTypeError.set(null);
            this.slugError.set(null);
            this.isPrivate.set(isPrivate);
            this.disableCopy.set(false);
            this.watermarkText.set(null);
            this.locationText.set('');
            this.watermarkInput = '';
            this.watermarkError.set(null);
            this.invites.set([]);
            this.previewLinkUrl.set(null);
            this.previewLinkError.set(null);
            this.notifySubscribers.set(false);
            this.copyText.set('');
            this.copyTextError.set('');
            this.regForm.set(null);
            this.editor?.setEditable(true);
            this.editor?.commands.setContent(JSON.parse(cedarJson), { emitUpdate: false });
            this.resetHistory();
            this.saveState.set('saved');
            this.currentBlog.set(null);
            this.blogError.set(null);
            this.editor?.commands.focus();
        } finally {
            this.draftsBusy.set(false);
        }
    }



    blogUrl(): string | null {
        const b = this.currentBlog();
        const base = this.auth.blogUrl();
        return b && base ? `${base}/${b.slug}` : null;
    }

    blogHost(): string {
        return this.auth.blogUrl()?.replace(/^https?:\/\//, '') ?? '';
    }

    telegramUsername(): string | null {
        return this.drafts().find(d => d.id === this.currentId())?.lastTelegramUsername ?? null;
    }

    async publishToBlog() {
        const id = this.currentId();
        if (!id) return;
        this.blogBusy.set(true);
        this.blogElapsed.set(0);
        clearInterval(this.blogTicker);
        this.blogTicker = setInterval(() => this.blogElapsed.update(s => s + 1), 1000);
        this.blogError.set(null);
        try {
            const notify = this.notifySubscribers() && !this.currentBlog()?.isPublished;
            const res = await this.draftsApi.publishToBlog(id, this.confirmedFingerprints, notify);
            this.currentBlog.set({ slug: res.slug, isPublished: true });
        } catch (e) {
            const status = e instanceof HttpErrorResponse ? e.status : undefined;
            if (!(status === 409 && this.reopenConfirmFrom(e)))
                this.blogError.set(httpErrorMessage(e, this.t().editor.errors.publish));
        } finally {
            this.blogBusy.set(false);
            clearInterval(this.blogTicker);
        }
    }

    async removeTableContent(action: 'deleteRow' | 'deleteColumn' | 'deleteTable') {
        if (!this.editor?.isActive('table')) return;
        if (!await this.confirmation.confirm({ title: this.t().editor.tableMenu[action], message: this.t().common.removeEditorContentConfirm })) return;
        this.cmd(chain => chain[action]());
    }

    cmd(fn: (chain: any) => any) {
        if (this.editor) 
            fn(this.editor.chain().focus()).run();
    }

    isActive(name: string, attrs?: Record<string, any>): boolean {
        this.tick();
        return this.editor?.isActive(name, attrs) ?? false;
    }

    // Blog-only (see the TextAlign.configure comment above) — 'left' is the extension's own
    // default, so setAlign('left') both sets it explicitly and clears a non-default value.
    setAlign(align: 'left' | 'center' | 'right' | 'justify') {
        this.cmd(c => c.setTextAlign(align));
    }

    // Wave 2 item 17 — flips the caret's blockquote between plain and Telegram-expandable.
    toggleExpandableQuote() {
        const expandable = !this.editor?.getAttributes('blockquote')['expandable'];
        this.cmd(c => c.updateAttributes('blockquote', { expandable }));
    }

    canUndo(): boolean {
        this.tick();
        return this.editor?.can().undo() ?? false;
    }

    canRedo(): boolean {
        this.tick();
        return this.editor?.can().redo() ?? false;
    }

    // setContent() alone doesn't touch the undo/redo stack, so switching drafts or language
    // tabs used to leave the previous document's history sitting there — Ctrl+Z right after
    // opening a draft could undo edits from whatever was open before. Reinitializing the
    // ProseMirror state (same doc/selection/plugins) resets every plugin's state, history
    // included, without recreating the whole Editor instance.
    private resetHistory() {
        if (!this.editor) return;
        const { state } = this.editor;
        this.editor.view.updateState(EditorState.create({
            doc: state.doc,
            selection: state.selection,
            plugins: state.plugins,
        }));
    }

    async enterPublish() {
        this.settleActiveDestination();
        // Presets are the only form control left here (N12) — a failed load just means no preset
        // chips, never a blocked export.
        this.presetsApi.list().then(p => this.formPresets.set(p)).catch(() => this.formPresets.set([]));
        // T-086 — asked once per opening, not per keystroke: it reads the stored document, which is
        // what would be sent anyway.
        this.refreshPublishIssues();
        this.loadShortPostTargets();
        // Wave 2 — the content checklist, the best-time hint and the CTA buttons ride the same
        // opening; each is best-effort and none of them gates the modal.
        void this.refreshPreflight();
        void this.loadBestTimes();
        void this.loadExportPresets();
        this.exportSilent.set(false);
        this.exportPin.set(false);
        this.trackedLinkError.set('');
        this.trackedCustomUrl = '';
        // A copy target left active from the last opening re-renders — the document moved since.
        if (this.activeCopyTarget()) void this.loadCopyText();
        const id = this.currentId();
        if (!id) return;
        void this.loadCtaButtons(id);
        this.draftAssetsLoading.set(true);
        try {
            this.draftAssets.set(await this.assets.listForDraft(id));
        } catch {
            this.draftAssets.set([]);
        } finally {
            this.draftAssetsLoading.set(false);
        }

        // Invites need a blog slug (the invite URL points at the post page), which exists from
        // the first publish onward — but privacy itself can be toggled before that.
        if (this.currentBlog()) {
            this.invitesLoading.set(true);
            try {
                this.invites.set(await this.draftsApi.listInvites(id));
            } catch {
                this.invites.set([]);
            } finally {
                this.invitesLoading.set(false);
            }
        }
    }


    async toggleListedWhilePrivate() {
        const id = this.currentId();
        if (!id) return;
        const next = !this.isListedWhilePrivate();
        this.isListedWhilePrivate.set(next);
        try {
            await this.draftsApi.setDraftListed(id, next);
        } catch (e) {
            this.isListedWhilePrivate.set(!next);
            this.inviteError.set(httpErrorMessage(e, this.t().editor.errors.saveForm));
        }
    }

    async togglePrivate() {
        const id = this.currentId();
        if (!id) return;
        const next = !this.isPrivate();
        try {
            const res = await this.draftsApi.setDraftPrivate(id, next);
            this.isPrivate.set(res.isPrivate);
        } catch {
            this.inviteError.set(this.t().editor.errors.privacy);
        }
    }

    async toggleDisableCopy() {
        const id = this.currentId();
        if (!id) return;
        const next = !this.disableCopy();
        this.disableCopy.set(next);
        try {
            await this.draftsApi.setDraftDisableCopy(id, next);
        } catch (e) {
            this.disableCopy.set(!next);
            this.inviteError.set(httpErrorMessage(e, this.t().editor.errors.privacy));
        }
    }

    async saveWatermark() {
        const id = this.currentId();
        if (!id) return;
        this.watermarkBusy.set(true);
        this.watermarkError.set(null);
        try {
            const res = await this.draftsApi.setDraftWatermark(id, this.watermarkInput.trim());
            this.watermarkText.set(res.watermarkText);
            this.watermarkInput = res.watermarkText ?? '';
        } catch (e) {
            this.watermarkError.set(httpErrorMessage(e, this.t().editor.errors.saveWatermark));
        } finally {
            this.watermarkBusy.set(false);
        }
    }

    /** T-345 — committed straight from the inspector row; blank clears back to the profile's. */
    async setLocation(value: string) {
        const id = this.currentId();
        this.locationText.set(value);
        if (!id) return;
        try {
            const res = await this.draftsApi.setDraftLocation(id, value.trim());
            this.locationText.set(res.locationText ?? '');
        } catch { /* the next save retries; the field keeps what was typed */ }
    }

    async addInvite() {
        const id = this.currentId();
        const email = this.inviteEmailInput.trim();
        if (!id || !email || this.inviteBusy()) return;
        this.inviteBusy.set(true);
        this.inviteError.set(null);
        try {
            const invite = await this.draftsApi.addInvite(id, email);
            this.invites.update(list => [...list, invite]);
            this.inviteEmailInput = '';
        } catch (e) {
            this.inviteError.set(httpErrorMessage(e, this.t().editor.errors.addInvite));
        } finally {
            this.inviteBusy.set(false);
        }
    }

    async revokeInvite(inviteId: string) {
        const id = this.currentId();
        if (!id || this.inviteBusy()) return;
        if (!await this.confirmation.confirm({ message: this.t().common.revokeConfirm, confirmLabel: this.t().common.confirm })) return;
        this.inviteBusy.set(true);
        try {
            await this.draftsApi.revokeInvite(id, inviteId);
            this.invites.update(list => list.filter(i => i.id !== inviteId));
        } catch {
            this.inviteError.set(this.t().editor.errors.revokeInvite);
        } finally {
            this.inviteBusy.set(false);
        }
    }

    async resendInvite(inviteId: string) {
        const id = this.currentId();
        if (!id || this.inviteBusy()) return;
        this.inviteBusy.set(true);
        try {
            await this.draftsApi.resendInvite(id, inviteId);
        } catch {
            this.inviteError.set(this.t().editor.errors.resendInvite);
        } finally {
            this.inviteBusy.set(false);
        }
    }

    // Registration form (B3). The whole definition is persisted as one JSON blob on every edit —
    // it's small and always fully in hand, so incremental patching would only add moving parts.
    // FI4.1 — writes into one language's slot. The editor always shows the primary-language
    // form; attaching a preset written in another language fills that language's slot instead,
    // which is why applyFormPreset passes the preset's own language rather than assuming.
    // Takes the raw blob rather than the parsed view — re-serializing the single-language
    // projection would silently strip a v2 preset's other languages (ADR-060). The displayed
    // form always comes back from the server's response, whatever shape was written.
    private async persistRegFormJson(formJson: string | null, language = this.primaryLanguage) {
        const id = this.currentId();
        if (!id) return;
        this.regBusy.set(true);
        try {
            const res = await this.draftsApi.setRegistrationForm(id, formJson, language);
            this.formLanguages.set(res.formLanguages ?? []);
            this.regForm.set(parseRegistrationForm(res.registrationFormJson));
        } catch (e) {
            this.inviteError.set(httpErrorMessage(e, this.t().editor.errors.saveForm));
        } finally {
            this.regBusy.set(false);
        }
    }

    // Applying a preset copies its definition onto the draft (N12, ADR-047) — the full editor
    // for a form lives in the Posts Manager; this modal only makes the pre-publish choice.
    // FI2.3 — the dropdown reports an id, or the sentinel for "no form".
    async pickFormPreset(value: string) {
        if (!value) return;
        if (value === '__none') {
            await this.persistRegFormJson(null);
            return;
        }
        const preset = this.formPresets().find(p => p.id === value);
        if (preset) await this.applyFormPreset(preset);
    }

    // A v2 preset carries every language in one blob, so one pick attaches them all (ADR-060);
    // a legacy v1 preset still fills only its own language's slot.
    async applyFormPreset(preset: FormPreset) {
        await this.persistRegFormJson(preset.formJson, preset.language || this.primaryLanguage);
    }

    async copyInviteLink(url: string) {
        try {
            await navigator.clipboard.writeText(url);
        } catch {
            // Clipboard API unavailable (e.g. non-HTTPS context) — silently ignored, the
            // link is still visible in the UI to copy by hand.
        }
    }

    formatFileSize(bytes: number): string {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    }

    async removeAssetFromDraft(asset: DraftAsset) {
        if (!this.editor) return;
        if (!await this.confirmation.confirm({ title: this.t().editor.exportModal.detachAsset, message: this.t().common.removeEditorMediaConfirm })) return;
        const targetSrc = '/media/' + asset.localPath;
        const mediaNodeTypes = new Set(['image', 'video', 'audio']);
        const { state } = this.editor;
        const positions: number[] = [];
        state.doc.descendants((node, pos) => {
            if (mediaNodeTypes.has(node.type.name) && node.attrs['src'] === targetSrc)
                positions.push(pos);
            return true;
        });
        if (positions.length === 0) {
            this.draftAssets.update(list => list.filter(a => a.id !== asset.id));
            return;
        }

        // Delete highest position first — ProseMirror positions below an edited range are
        // unaffected by edits above it, so working in reverse keeps every collected position valid.
        let tr = state.tr;
        for (const pos of positions.slice().reverse()) {
            const node = tr.doc.nodeAt(pos);
            if (node) tr = tr.delete(pos, pos + node.nodeSize);
        }
        this.editor.view.dispatch(tr);
        this.markDirty();
        this.draftAssets.update(list => list.filter(a => a.id !== asset.id));
    }

    totalAssetsBytes(): number {
        return this.draftAssets().reduce((sum, a) => sum + a.sizeBytes, 0);
    }

    isExportLang(lang: string): boolean {
        return this.exportLangs().includes(lang);
    }

    toggleExportLang(lang: string) {
        this.exportLangs.update(list => {
            if (!list.includes(lang)) return [...list, lang];
            const next = list.filter(l => l !== lang);
            // Unticking the last one would leave Publish with nothing to send.
            return next.length ? next : list;
        });
        // ADR-096 — the version choice is the window's, so everything keyed off it follows: which
        // language each short-post panel is editing, and how many parts a thread would be.
        for (const network of this.microNetworks) {
            this.syncMicroTextLang(network);
            if (this.microMode()[network] === 'thread') this.countMicroParts(network);
        }
        if (this.activeCopyTarget()) void this.loadCopyText();
        // The content checklist and the best-time hint both answer per ticked version.
        void this.refreshPreflight();
        void this.loadBestTimes();
    }

    /** Everything a schedule can apply to — the blog is not a publish target (ADR-099). */
    anyNetworkDestination(): boolean {
        return this.destTelegram() || this.destBluesky() || this.destX() || this.destDiscord();
    }

    /** True when pressing Publish will schedule the networks rather than send them now. */
    schedulingActive(): boolean {
        return !!this.scheduledAt && this.anyNetworkDestination();
    }

    // FI2.6/FI2.7 — the button says what pressing it does: schedule if a time is set, update if
    // the blog page is already live, publish otherwise.
    publishButtonLabel(): string {
        const tx = this.t().editor.exportModal;
        if (this.publishingAll()) return tx.publishing;
        if (this.schedulingActive()) return tx.scheduleAndPublish;
        if (this.destBlog() && this.currentBlog()?.isPublished) return tx.update;
        const count = this.tickedDestinationCount();
        return count ? this.t().editor.frame.publishTo(count) : tx.publish;
    }

    canPublishAll(): boolean {
        if (this.publishingAll() || this.blogBusy() || this.exporting()) return false;
        return this.publishSettingsComplete();
    }

    /** Every ticked destination can run — the same contract as the button, minus the busy states. */
    publishSettingsComplete(): boolean {
        if (!this.destBlog() && !this.destTelegram() && !this.destBluesky() && !this.destX() && !this.destDiscord()) return false;
        // Each ticked destination must be able to run. The blog needs nothing extra; Telegram needs
        // a chosen channel for EVERY ticked version (ADR-098) — "RU picked, EN not" is an
        // incomplete request, not one with a default; a short-post network needs a connected
        // account, a post that fits when it is being sent as one, and — for X — its credits.
        if (this.destTelegram() && this.langsMissingChannel().length) return false;
        for (const network of this.microNetworks) {
            if (!this.destination(network)()) continue;
            if (!this.account(network) || this.microOverLimit(network)) return false;
        }
        return !this.destX() || !this.xCreditsShort();
    }

    // One Publish for every ticked destination (B5). Run sequentially rather than in parallel so
    // each destination's own busy/progress state stays readable, and so a Telegram failure
    // doesn't get visually tangled with the blog's.
    async publishAll() {
        if (!this.canPublishAll()) return;
        const id = this.currentId();
        if (!id) return;

        // ADR-065 — the pending autosave is flushed BEFORE the diff is calculated. Previewing the
        // last-saved version and then publishing a newer one is the same defect as not previewing
        // at all, and the debounce makes it the normal case: type, hit Update, and the paragraph
        // just typed is not in the diff the server was asked about.
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        // Whether a target is already live is the server's answer, not ours: the old condition
        // keyed off a public post URL, which a channel with no @username never has — so exactly
        // the private-channel case published over a live post with no confirmation at all.
        const previews: UpdatePreview[] = [];
        try {
            if (this.destBlog() && this.currentBlog()?.isPublished) {
                for (const language of this.blogLanguages())
                    previews.push(await this.posts.updatePreview(id, 'blog', language));
            }
            if (this.destTelegram() && !this.scheduledAt) {
                // ADR-098 — asked per version about ITS channel. One chat id for the whole window
                // meant the EN question was answered about the RU channel.
                for (const language of this.exportLangs()) {
                    const chatId = this.chatIdFor(language);
                    if (chatId) previews.push(await this.posts.updatePreview(id, 'telegram', language, chatId));
                }
            }
            // A short-post network keys its revisions by the target row's id — the same string the
            // publish queue uses as the destination, so the two ask about the same thing.
            // Skipped while scheduling, same as Telegram above: nothing is being overwritten yet,
            // and the confirmation belongs to the send, which happens later.
            for (const network of this.microNetworks) {
                const account = this.account(network);
                if (this.scheduledAt || !this.destination(network)() || !account) continue;
                for (const language of this.microLangsOf(network))
                    previews.push(await this.posts.updatePreview(id, network, language, account.id));
            }
        } catch {
            // A preview that can't be fetched must not block publishing outright — the server
            // enforces the same rule again and answers 409 if this really was an unseen overwrite.
        }

        // One row per language, not per destination-and-language: the diff being confirmed is the
        // document's, and with four destinations ticked the same diff was listed four times over.
        const live = previews.filter(p => p.publishedBefore)
            .filter((p, i, all) => all.findIndex(o => o.language === p.language) === i);
        if (live.length > 0) {
            this.updateConfirm.set(live);
            this.confirmedFingerprints = Object.fromEntries(previews.map(p => [p.language, p.fingerprint]));
            return;
        }
        this.confirmedFingerprints = Object.fromEntries(previews.map(p => [p.language, p.fingerprint]));
        await this.publishAllConfirmed();
    }

    // Every language the blog would republish: the primary plus each translation that exists.
    private blogLanguages(): string[] {
        return [this.primaryLanguage, ...this.existingLanguages()];
    }

    // Turns the server's 409 body back into the confirmation modal. Both publish endpoints answer
    // with a freshly-calculated preview precisely so the owner can decide on what is true now.
    private reopenConfirmFrom(e: unknown): boolean {
        const body = e instanceof HttpErrorResponse ? e.error : null;
        const previews: UpdatePreview[] | null = body?.previews ?? (body?.preview ? [body.preview] : null);
        if (!previews?.length) return false;
        for (const p of previews) this.confirmedFingerprints[p.language] = p.fingerprint;
        this.updateConfirm.set(previews);
        return true;
    }

    async publishAllConfirmed() {
        this.updateConfirm.set(null);
        this.publishingAll.set(true);
        this.publishSuccess.set(null);
        this.microError.set('');

        // The checklist mirrors what pressing the button will actually do — one row per phase,
        // built before anything runs so the author sees the whole plan tick over.
        const pr = this.t().editor.publishRun;
        const scheduling = this.schedulingActive();
        const steps: PublishRunStep[] = [{ id: 'save', label: pr.saving, status: 'waiting' }];
        // The blog is never scheduled (ADR-099) — it is not a publish target, so a ticked blog
        // goes out now even when the networks are being queued for later.
        if (this.destBlog()) steps.push({ id: 'blog', label: pr.blog, status: 'waiting' });
        if (scheduling) {
            // One row for the whole schedule: nothing is sent, so a row per network per language
            // would be a list of identical "queued" lines.
            steps.push({ id: 'schedule', label: pr.scheduling, status: 'waiting' });
        } else {
            if (this.destTelegram())
                for (const lang of this.exportLangs())
                    steps.push({ id: 'tg-' + lang, label: pr.telegram(lang.toUpperCase()), status: 'waiting' });
            // ADR-096 — X and Bluesky lost their own Publish buttons; the checklist is where four
            // networks going out at once becomes readable, one row per network per version.
            for (const network of this.microNetworks) {
                if (!this.destination(network)()) continue;
                for (const lang of this.microLangsOf(network))
                    steps.push({
                        id: `${network}-${lang}`,
                        label: pr.network(this.microLabels[network], lang.toUpperCase()),
                        status: 'waiting',
                    });
            }
        }
        this.runStart(steps);

        const links: { label: string; url: string }[] = [];
        try {
            this.runUpdate('save', { status: 'running' });
            clearTimeout(this.saveTimer);
            if (this.saveState() !== 'saved') await this.save();
            if (this.saveState() !== 'saved') {
                // Publishing an unsaved sheet would send yesterday's text; stop at the first row.
                this.runUpdate('save', { status: 'failed', error: this.t().editor.syncFailed });
                return;
            }
            this.runUpdate('save', { status: 'done' });

            if (this.destBlog()) {
                this.runUpdate('blog', { status: 'running' });
                await this.publishToBlog();
                // A 409 reopened the update-confirmation — that dialog takes over; this run is void.
                if (this.updateConfirm()) { this.publishRun.set(null); return; }
                const url = this.blogUrl();
                const blogErr = this.blogError();
                if (blogErr) this.runUpdate('blog', { status: 'failed', error: blogErr });
                else {
                    const link = url ? { label: this.t().editor.exportModal.openBlog, url } : undefined;
                    this.runUpdate('blog', { status: 'done', link });
                    if (link) links.push(link);
                }
            }
            // FI2.7/ADR-099 — a set time makes this the same button, scheduling rather than
            // sending, and now for every ticked network at once rather than Telegram alone.
            if (scheduling) {
                this.runUpdate('schedule', { status: 'running' });
                await this.schedulePost();
                const ok = this.scheduleResult().startsWith('✓');
                this.runUpdate('schedule', ok
                    ? { status: 'done' }
                    : { status: 'failed', error: this.scheduleResult() });
            } else {
                if (this.destTelegram()) {
                    links.push(...await this.exportDraft());
                    if (this.updateConfirm()) { this.publishRun.set(null); return; }
                }
                for (const network of this.microNetworks) {
                    if (!this.destination(network)()) continue;
                    links.push(...await this.publishMicroNetwork(network));
                    if (this.updateConfirm()) { this.publishRun.set(null); return; }
                }
            }
        } finally {
            this.publishingAll.set(false);
            this.runFinish();
        }
        if (!this.blogError() && !this.exportError() && !this.microError()) this.showPublishSuccess(links);
    }

    closePublishRun() {
        this.publishRun.set(null);
    }

    /**
     * The result of a publish, with the links it produced. **It does not expire** (11.08.2026):
     * it lives inside the export modal, the progress checklist stacks over it, and it used to
     * delete itself ten seconds later — so watching a publish to its end and then closing the
     * checklist reliably showed nothing at all. Ten seconds is not long enough to read a link;
     * it is barely long enough to notice one.
     */
    private showPublishSuccess(links: { label: string; url: string }[]) {
        this.publishSuccess.set({ links });
    }

    dismissPublishSuccess() {
        this.publishSuccess.set(null);
    }

    async exportDraft(): Promise<{ label: string; url: string }[]> {
        const id = this.currentId();
        if (!id) return [];
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();
        this.exporting.set(true);
        this.exportElapsed.set(0);
        clearInterval(this.exportTicker);
        this.exportTicker = setInterval(() => this.exportElapsed.update(s => s + 1), 1000);
        this.exportResult.set('');
        this.exportLink.set(null);
        this.exportError.set(null);
        const links: { label: string; url: string }[] = [];
        try {
            // T-090 — queued, not awaited over HTTP. The old shape held one request open while
            // Telegram downloaded every media file from this server, which is how a 30MB post
            // returned a proxy's 502 about a publish that was still running (ADR-080).
            // ADR-098 — one target per version, resolved from that version's own channel.
            const targetIds = await this.telegramTargetIds(this.exportLangs());
            if (this.exportLangs().every(l => !targetIds[l])) {
                this.exportError.set({ code: 403, message: this.t().editor.errors.connectChannel });
                this.failTelegramSteps(this.t().editor.errors.connectChannel);
                return links;
            }

            // One job per ticked language (FI2.2): a failure part-way through still leaves the
            // languages already sent visibly sent, and now says so per language rather than
            // stopping at the first one.
            const queuedByLang = new Map<string, PublishJob[]>();
            for (const lang of this.exportLangs()) {
                const targetId = targetIds[lang];
                // A version whose channel could not be resolved fails on its own row — the others
                // still go out, which is the whole reason these are separate jobs.
                if (!targetId) {
                    const message = this.t().editor.errors.connectChannel;
                    if (!this.exportError()) this.exportError.set({ code: 403, message });
                    this.runUpdate('tg-' + lang, { status: 'failed', error: message });
                    continue;
                }
                const { jobs } = await this.publishApi.queue(id, [targetId], lang, this.confirmedFingerprints[lang], this.splitIntoThread(),
                    { silent: this.exportSilent(), pin: this.exportPin() });
                queuedByLang.set(lang, jobs);
                // A thread unfolds into its part chips the moment it is queued (T-106).
                const parts = jobs.length > 1
                    ? this.sortedByPart(jobs).map(j => ({ index: j.partIndex ?? 0, status: 'waiting' as PublishRunStatus }))
                    : undefined;
                this.runUpdate('tg-' + lang, { status: 'running', parts });
            }

            this.exportResult.set(this.t().editor.exportModal.queued);
            const allIds = [...queuedByLang.values()].flat().map(j => j.id);
            const byStep = new Map([...queuedByLang].map(([lang, jobs]) => ['tg-' + lang, jobs] as const));
            const finished = await this.awaitJobs(id, allIds, polled => this.reflectParts(byStep, polled));

            for (const [lang, queuedJobs] of queuedByLang) {
                const byId = new Map(finished.map(j => [j.id, j]));
                const jobs = this.sortedByPart(queuedJobs).map(q => byId.get(q.id)).filter((j): j is PublishJob => !!j);
                if (jobs.length < queuedJobs.length) {
                    // Still running after the polling window — the sweeper owns it now, and the
                    // Posts Manager is where its outcome shows up.
                    this.exportResult.set(this.t().editor.exportModal.stillRunning);
                    this.runUpdate('tg-' + lang, { error: this.t().editor.exportModal.stillRunning });
                    continue;
                }
                // The FIRST failed part carries the root cause; the later ones only say they were
                // held back because of it. Reporting the last one buried the actual error.
                const firstFailed = jobs.find(j => j.status !== 'Succeeded');
                if (firstFailed) {
                    const message = firstFailed.error ?? firstFailed.status;
                    if (!this.exportError()) this.exportError.set({ code: undefined, message });
                    this.runUpdate('tg-' + lang, { status: 'failed', error: message });
                    continue;
                }
                // A thread's public link is its head — every later part is a reply hanging off it.
                const head = jobs[0];
                this.exportResult.set(`✓ Published (message #${head.remoteId})`);
                const url = head.publicUrl ?? this.buildTelegramLink(this.chatIdFor(lang), Number(head.remoteId));
                this.exportLink.set(url);
                const link = url ? { label: `${this.t().editor.exportModal.openTelegram} ${lang.toUpperCase()}`, url } : undefined;
                this.runUpdate('tg-' + lang, { status: 'done', link });
                if (link) links.push(link);
            }
        } catch (e) {
            const status = e instanceof HttpErrorResponse ? e.status : undefined;
            // ADR-065 — the server refused because the post moved since the diff was shown (a
            // second tab, a slow save). Re-open the confirmation on the fresh diff instead of
            // reporting it as a failure: nothing is wrong, the owner just has to look again.
            if (status === 409 && this.reopenConfirmFrom(e)) return links;
            const serverMessage = httpErrorMessage(e, '');
            const message = status === 503
                ? `The barn door seems closed — Telegram Bot API didn't respond. Your draft is safe; nothing was published.${serverMessage ? ` (${serverMessage})` : ''}`
                : serverMessage || 'Error — check the browser console / server logs';
            this.exportError.set({ code: status, message });
            this.failTelegramSteps(message);
        } finally {
            this.exporting.set(false);
            clearInterval(this.exportTicker);
        }
        return links;
    }

    /**
     * One short-post network, every ticked version (ADR-096). Same queue as Telegram — the author's
     * override is saved first so what goes out is what the field shows, and a thread is the same
     * `splitIntoThread` flag the queue already understands (ADR-094).
     */
    private async publishMicroNetwork(network: MicroNetwork): Promise<{ label: string; url: string }[]> {
        const id = this.currentId();
        const account = this.account(network);
        const links: { label: string; url: string }[] = [];
        if (!id || !account) return links;

        const asThread = this.microMode()[network] === 'thread';
        for (const lang of this.microLangsOf(network)) {
            const step = `${network}-${lang}`;
            this.runUpdate(step, { status: 'running' });
            try {
                // Only in link mode: a thread reads the document, never the override, so saving the
                // field here would write a text nothing is going to send.
                if (!asThread) await this.saveMicroTexts(network);

                const { jobs } = await this.publishApi.queue(
                    id, [account.id], lang, this.confirmedFingerprints[lang], asThread);
                const parts = jobs.length > 1
                    ? this.sortedByPart(jobs).map(j => ({ index: j.partIndex ?? 0, status: 'waiting' as PublishRunStatus }))
                    : undefined;
                if (parts) this.runUpdate(step, { parts });

                // The callback is the whole fix for "a thread with no progress": without it the
                // chips are drawn once, as waiting, and never touched again.
                const byStep = new Map([[step, jobs]]);
                const finished = await this.awaitJobs(
                    id, jobs.map(j => j.id), polled => this.reflectParts(byStep, polled));
                if (finished.length < jobs.length) {
                    this.runUpdate(step, { error: this.t().editor.exportModal.stillRunning });
                    this.microError.set(this.t().editor.exportModal.stillRunning);
                    continue;
                }
                // The first failed part carries the root cause; the ones after it were only held
                // back by it — same rule as the Telegram path above.
                const firstFailed = this.sortedByPart(finished).find(j => j.status !== 'Succeeded');
                if (firstFailed) {
                    const message = firstFailed.error ?? firstFailed.status;
                    this.runUpdate(step, { status: 'failed', error: message });
                    this.microError.set(message);
                    continue;
                }
                const url = this.sortedByPart(finished)[0]?.publicUrl ?? null;
                const link = url
                    ? { label: `${this.microLabels[network]} ${lang.toUpperCase()}`, url }
                    : undefined;
                this.runUpdate(step, { status: 'done', link });
                if (link) links.push(link);
            } catch (e) {
                // ADR-065 — the document moved since the diff was shown. The confirmation dialog
                // takes over; this run is void, exactly as on the Telegram path.
                if (e instanceof HttpErrorResponse && e.status === 409 && this.reopenConfirmFrom(e)) return links;
                const message = httpErrorMessage(e, this.t().editor.errors.publish);
                this.runUpdate(step, { status: 'failed', error: message });
                this.microError.set(message);
            }
        }
        // Balance and "last published" both moved; re-reading is cheaper than tracking them.
        await this.loadShortPostTargets();
        return links;
    }

    private sortedByPart(jobs: PublishJob[]): PublishJob[] {
        return [...jobs].sort((a, b) => (a.partIndex ?? 0) - (b.partIndex ?? 0));
    }

    /**
     * Every poll of the queue repaints one step's part chips — the "test run" effect.
     *
     * Keyed by step id rather than by language: X and Bluesky threads went without this for their
     * whole existence (found 11.08.2026 — a 25-part X thread showed 25 grey chips for seventeen
     * seconds and then flipped to done), because only the Telegram path passed the callback and
     * the helper could only address `tg-<lang>` rows anyway.
     */
    private reflectParts(queued: Map<string, PublishJob[]>, polled: PublishJob[]) {
        const byId = new Map(polled.map(j => [j.id, j]));
        for (const [step, queuedJobs] of queued) {
            if (queuedJobs.length <= 1) continue;
            const parts = this.sortedByPart(queuedJobs).map(q => {
                const now = byId.get(q.id);
                const status: PublishRunStatus =
                    !now || now.status === 'Pending' ? 'waiting'
                    : now.status === 'Running' ? 'running'
                    : now.status === 'Succeeded' ? 'done' : 'failed';
                return { index: q.partIndex ?? 0, status };
            });
            this.runUpdate(step, { parts });
        }
    }

    /** One error before any job was queued fails every Telegram row at once. */
    private failTelegramSteps(message: string) {
        for (const lang of this.exportLangs())
            this.runUpdate('tg-' + lang, { status: 'failed', error: message });
    }

    private buildTelegramLink(chatId: string, messageId: number): string | null {
        const trimmed = chatId.trim();
        if (trimmed.startsWith('@')) return `https://t.me/${trimmed.slice(1)}/${messageId}`;
        const username = this.channels().find(c => String(c.telegramChatId) === trimmed)?.username;
        return username ? `https://t.me/${username}/${messageId}` : null;
    }

    /**
     * ADR-099 — every ticked network, one scheduled row per destination and version. Partial
     * failure is reported as such: a row that could not be scheduled must not be hidden behind
     * the ones that could.
     */
    async schedulePost() {
        const id = this.currentId();
        if (!id || !this.scheduledAt) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();
        this.scheduling.set(true);
        this.scheduleResult.set('');

        const scheduledAtUtc = new Date(this.scheduledAt).toISOString();
        const tx = this.t().editor.exportModal;
        let scheduled = 0;
        const failures: string[] = [];
        const attempt = async (label: string, run: () => Promise<unknown>) => {
            try {
                await run();
                scheduled++;
            } catch (e) {
                failures.push(`${label}: ${httpErrorMessage(e, tx.scheduleFailed)}`);
            }
        };

        try {
            if (this.destTelegram()) {
                for (const lang of this.exportLangs()) {
                    const chatId = this.chatIdFor(lang);
                    if (!chatId) { failures.push(`Telegram ${lang.toUpperCase()}: ${this.t().editor.errors.connectChannel}`); continue; }
                    await attempt(`Telegram ${lang.toUpperCase()}`,
                        () => this.posts.schedule(id, scheduledAtUtc, lang, { chatId }, this.format,
                            { silent: this.exportSilent(), pin: this.exportPin() }));
                }
            }
            for (const network of this.microNetworks) {
                const account = this.account(network);
                if (!this.destination(network)() || !account) continue;
                // The override has to exist server-side before a send that happens without this
                // page open — the scheduled job reads the stored text, never the field.
                await this.saveMicroTexts(network);
                for (const lang of this.microLangsOf(network)) {
                    await attempt(`${this.microLabels[network]} ${lang.toUpperCase()}`,
                        () => this.posts.schedule(id, scheduledAtUtc, lang, { targetId: account.id }));
                }
            }

            if (failures.length === 0 && scheduled > 0) {
                this.scheduleResult.set(`✓ ${tx.scheduledCount(scheduled)}`);
                this.scheduledAt = '';
            } else {
                this.scheduleResult.set(`✗ ${failures.join(' · ')}`);
            }
        } finally {
            this.scheduling.set(false);
        }
    }

    /**
     * A time is set. Threads are not schedulable (ADR-099) and the toggles that promise one are
     * reset here rather than left ticked-but-ignored, which is what the old Telegram-only path did.
     */
    onScheduledAtChange(value: string) {
        this.scheduledAt = value;
        if (!value) return;
        this.splitIntoThread.set(false);
        this.microMode.update(map => Object.fromEntries(
            Object.entries(map).map(([n, mode]) => [n, mode === 'thread' ? 'link' : mode])) as Record<MicroNetwork, MicroMode>);
    }

    quickSchedule(preset: '1m' | '5m' | '1h' | '6h' | '12h' | 'tomorrow') {
        const now = new Date();
        let target: Date;
        switch (preset) {
            case '1m': target = ceilToMinutes(now, 1); break;
            case '5m': target = ceilToMinutes(now, 5); break;
            case '1h': target = ceilToMinutes(now, 60); break;
            case '6h': target = ceilToMinutes(new Date(now.getTime() + 6 * 3600_000), 60); break;
            case '12h': target = ceilToMinutes(new Date(now.getTime() + 12 * 3600_000), 60); break;
            case 'tomorrow':
                target = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1, 9, 0, 0, 0);
                break;
        }
        this.onScheduledAtChange(toDatetimeLocalValue(target));
    }

    utcDate(iso: string): Date {
        // SQLite не хранит DateTimeKind, сервер отдаёт UTC без 'Z' — без него браузер счёл бы время местным
        return new Date(/Z|[+-]\d{2}:\d{2}$/.test(iso) ? iso : iso + 'Z');
    }

    zonesHint(date: Date): string {
        if (isNaN(date.getTime())) return '';
        return EXTRA_TIMEZONES
            .map(tz => `${tz.label} ${date.toLocaleString('en-GB', {
                timeZone: tz.zone,
                day: 'numeric', month: 'short',
                hour: '2-digit', minute: '2-digit',
            })}`)
            .join(' · ');
    }

    pickerZonesHint(): string {
        return this.scheduledAt ? this.zonesHint(new Date(this.scheduledAt)) : '';
    }

    onFileChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        for (const file of files) {
            this.uploadFilePromise(file).then(a => { if (a) this.insertNode('image', { src: a.url, assetId: a.id }); });
        }
    }

    onVideoChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        for (const file of files) {
            this.uploadFilePromise(file).then(a => { if (a) this.insertNode('video', { src: a.url, assetId: a.id }); });
        }
    }

    // .gif needs a <video> tag so Telegram treats it as an animation, not a static photo
    onGifChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        for (const file of files) {
            this.uploadFilePromise(file).then(a => { if (a) this.insertNode('video', { src: a.url, assetId: a.id }); });
        }
    }

    onAudioChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        for (const file of files) {
            this.uploadFilePromise(file).then(a => { if (a) this.insertNode('audio', { src: a.url, assetId: a.id }); });
        }
    }

    onCarouselChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        if (!files.length) return;
        Promise.all(files.map(f => this.uploadFilePromise(f))).then(uploaded => {
            // A gallery holds an array of URLs and no per-frame attrs, so it carries no asset link.
            const images = uploaded.filter(a => !!a).map(a => a!.url);
            if (images.length) this.insertNode('carousel', { images });
        });
    }

    private wikiCandidates(query: string): { id: string; title: string }[] {
        const q = query.trim().toLowerCase();
        return this.drafts()
            .filter(d => d.id !== this.currentId() && !d.isArchived)
            .filter(d => !q || d.title.toLowerCase().includes(q))
            .slice(0, 8)
            .map(d => ({ id: d.id, title: d.title }));
    }

    private showWikiSuggest(props: {
        items: { id: string; title: string }[];
        command: (attrs: { draftId: string; label: string }) => void;
        clientRect?: (() => DOMRect | null) | null;
    }) {
        const rect = props.clientRect?.();
        if (!rect) { this.wikiSuggest.set(null); return; }
        this.wikiSuggestCommand = attrs => props.command(attrs);
        const prev = this.wikiSuggest();
        // T-341 — the caret can sit within the suggester's own box of the screen edges; the panel
        // is max-width 320 / max-height 240, so the clamp reserves that plus a margin.
        this.wikiSuggest.set({
            x: Math.max(8, Math.min(rect.left, window.innerWidth - 332)),
            y: Math.max(8, Math.min(rect.bottom + 4, window.innerHeight - 252)),
            items: props.items,
            index: Math.min(prev?.index ?? 0, Math.max(0, props.items.length - 1)),
        });
    }

    wikiSuggestKey(event: KeyboardEvent): boolean {
        const ws = this.wikiSuggest();
        if (!ws) return false;
        const count = Math.max(1, ws.items.length);
        if (event.key === 'ArrowDown') { this.wikiSuggest.set({ ...ws, index: (ws.index + 1) % count }); return true; }
        if (event.key === 'ArrowUp') { this.wikiSuggest.set({ ...ws, index: (ws.index - 1 + count) % count }); return true; }
        if (event.key === 'Enter' || event.key === 'Tab') {
            const item = ws.items[ws.index];
            if (item) { this.wikiPick(item); return true; }
            return false;
        }
        if (event.key === 'Escape') { this.wikiSuggest.set(null); return true; }
        return false;
    }

    wikiPick(item: { id: string; title: string }) {
        this.wikiSuggestCommand?.({ draftId: item.id, label: item.title });
        this.wikiSuggest.set(null);
    }

    // ADR-128 — the current document's ancestor chain, oldest first, from the already-loaded
    // list. Empty for a root document; capped at the tree's depth limit.
    breadcrumbs(): { id: string; title: string }[] {
        const id = this.currentId();
        if (!id) return [];
        const byId = new Map(this.drafts().map(d => [d.id, d]));
        const crumbs: { id: string; title: string }[] = [];
        let cursor = byId.get(id)?.parentDraftId ?? null;
        for (let hops = 0; cursor && hops < 10; hops++) {
            const d = byId.get(cursor);
            if (!d) break;
            crumbs.unshift({ id: d.id, title: d.title });
            cursor = d.parentDraftId;
        }
        return crumbs;
    }

    // ADR-127 — insert an already-uploaded file: same node mapping as the upload paths above,
    // including the GIF quirk (a .gif goes in as <video> so Telegram treats it as an animation).
    onLibraryPicked(asset: LibraryAsset) {
        this.libraryOpen.set(false);
        this.insertMediaUrl(`/media/${asset.localPath}`, asset.contentType, asset.id);
    }

    private insertMediaUrl(url: string, contentType: string, assetId: string | null = null) {
        if (contentType === 'image/gif' || contentType.startsWith('video/')) {
            this.insertNode('video', { src: url, assetId });
        } else if (contentType.startsWith('audio/')) {
            this.insertNode('audio', { src: url, assetId });
        } else {
            this.insertNode('image', { src: url, assetId });
        }
    }

    private uploadAndInsert(file: File) {
        void this.uploadFilePromise(file).then(a => { if (a) this.insertMediaUrl(a.url, file.type, a.id); });
    }

    onCollageChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        if (!files.length) return;
        Promise.all(files.map(f => this.uploadFilePromise(f))).then(uploaded => {
            const images = uploaded.filter(a => !!a).map(a => a!.url);
            if (images.length) this.insertNode('collage', { images });
        });
    }

    // "Auto" resolves to youtube when the pasted value parses as a YouTube URL, otherwise a
    // plain link — explicit rail choices (url/email/phone/mention/youtube) always win.
    effectiveInsertType(): 'url' | 'email' | 'phone' | 'mention' | 'youtube' {
        if (this.insertType !== 'auto') return this.insertType;
        return extractYouTubeId(this.insertValue) ? 'youtube' : 'url';
    }

    async openInsertModal(presetType: 'auto' | 'youtube' = 'auto') {
        this.insertOpen.set(true);
        this.insertType = presetType;
        this.insertValue = '';
        this.insertCaption = '';
        this.insertError.set('');
        try {
            const text = (await navigator.clipboard.readText()).trim();
            if (/^https?:\/\//i.test(text)) this.insertValue = text;
        } catch {
            // Clipboard read denied/unsupported — the user can still paste manually.
        }
    }

    closeInsertModal() {
        this.insertOpen.set(false);
    }

    applyInsert() {
        const value = this.insertValue.trim();
        if (!value) return;
        const type = this.effectiveInsertType();

        if (type === 'youtube') {
            const videoId = extractYouTubeId(value);
            if (!videoId) {
                this.insertError.set(this.t().editor.errors.notYoutube);
                return;
            }
            this.insertNode('youtube', { videoId, caption: this.insertCaption.trim() || null });
            this.closeInsertModal();
            return;
        }

        const href = type === 'email' ? `mailto:${value}`
            : type === 'phone' ? `tel:${value}`
            : type === 'mention' ? `tg://user?id=${value}`
            : value;

        if (this.editor?.state.selection.empty) {
            this.cmd(c => c.insertContent({ type: 'text', text: value, marks: [{ type: 'link', attrs: { href } }] }));
        } else {
            this.cmd(c => c.setLink({ href }));
        }
        this.closeInsertModal();
    }

    removeLink() {
        this.cmd(c => c.unsetLink());
        this.closeInsertModal();
    }

    insertEmoji(emoji: string) {
        this.cmd(c => c.insertContent(emoji));
    }

    insertDateTime() {
        if (!this.dtValue) return;
        const unix = Math.floor(new Date(this.dtValue).getTime() / 1000);
        const format = (this.dtWeekday ? 'w' : '') + (this.dtDate ? 'D' : '') + (this.dtTime ? 'T' : '');
        this.cmd(c => c.insertContent({ type: 'datetime', attrs: { unix, format: format || 'wDT' } }));
        this.dtValue = '';
        this.datetimeOpen.set(false);
    }

    insertToggle() {
        this.cmd(c => c.insertContent({
            type: 'toggle',
            attrs: { summary: 'Details' },
            content: [{ type: 'paragraph' }],
        }));
    }

    // NF5 — id is assigned by the node view on first mount (PollNode), not here; question/options
    // are edited inline via the node's own inputs, same interaction model as the toggle above.
    insertPoll() {
        this.cmd(c => c.insertContent({ type: 'poll', attrs: { question: '', options: ['', ''] } }));
    }

    // Content is auto-generated from the document's headings at render/export time (Core's
    // HeadingOutline) — this just drops a marker at the chosen spot, nothing to author here.
    insertTableOfContents() {
        this.cmd(c => c.insertContent({ type: 'tableOfContents' }));
    }

    insertDivider() {
        this.cmd(c => c.setHorizontalRule());
    }

    openFootnoteModal() {
        this.footnoteText = '';
        this.footnoteOpen.set(true);
    }

    insertFootnote() {
        const text = this.footnoteText.trim();
        if (!text) return;
        this.footnoteOpen.set(false);
        this.cmd(c => c.insertContent({ type: 'footnote', attrs: { text } }));
        this.footnoteText = '';
    }

    tableOpen = signal(false);
    tableRows = 3;
    tableCols = 3;
    tableHeaderRow = true;
    readonly maxTableSize = MAX_TABLE_SIZE;

    formulaOpen = signal(false);
    formulaText = '';

    /** The Appearance size is the dialog's starting point, not the insert itself (I5). */
    openTableModal() {
        this.tableRows = clampTableSize(this.appearance.prefs().tableRows);
        this.tableCols = clampTableSize(this.appearance.prefs().tableCols);
        this.tableHeaderRow = true;
        this.tableOpen.set(true);
    }

    insertTable() {
        // Clamped on read as well as on write: the preference blob is user-editable through the
        // API, and so is a number field.
        const rows = clampTableSize(this.tableRows);
        const cols = clampTableSize(this.tableCols);
        this.tableOpen.set(false);
        this.cmd(c => c.insertTable({ rows, cols, withHeaderRow: this.tableHeaderRow }));
    }

    openFormulaModal() {
        this.formulaText = '';
        this.formulaOpen.set(true);
    }

    insertFormula(where: 'inline' | 'block') {
        const latex = this.formulaText.trim();
        if (!latex) return;
        this.formulaOpen.set(false);
        this.cmd(c => (where === 'inline' ? c.insertInlineMath({ latex }) : c.insertBlockMath({ latex })));
    }

    canAnnotate(): boolean {
        this.tick();
        return !!this.editor && !this.editor.state.selection.empty;
    }

    insertAnnotation() {
        if (this.canAnnotate()) this.cmd(c => c.wrapIn('annotation', { id: crypto.randomUUID() }));
    }

    indent() {
        if (!this.editor) return;
        this.editor.chain().focus().sinkListItem(this.listItemType()).run();
    }

    outdent() {
        if (!this.editor) return;
        this.editor.chain().focus().liftListItem(this.listItemType()).run();
    }

    /**
     * Whether the two indent controls would do anything from where the caret is. Outside a list
     * neither can, and the first item of a list cannot sink — ProseMirror's own `can()` answers
     * both, so the buttons stop being lit over a command that silently does nothing.
     */
    canIndent(): boolean {
        this.tick();
        if (!this.editor) return false;
        return this.editor.can().sinkListItem(this.listItemType());
    }

    canOutdent(): boolean {
        this.tick();
        if (!this.editor) return false;
        return this.editor.can().liftListItem(this.listItemType());
    }

    private listItemType(): string {
        return this.editor?.isActive('taskItem') ? 'taskItem' : 'listItem';
    }

    private insertNode(type: string, attrs: Record<string, any>) {
        this.editor?.chain().focus().insertContent({ type, attrs }).run();
    }

    // Resolves the uploaded Asset, not just its URL: the id is what a media node keeps so the
    // inspector can ask the server for the file's own facts later (ADR-238 clause 3).
    private uploadFilePromise(file: File): Promise<{ id: string; url: string } | null> {
        const id = ++this.uploadSeq;
        this.uploads.update(list => [...list, { id, name: file.name, progress: 0 }]);
        return new Promise(resolve => {
            this.assets.uploadWithProgress(file).subscribe({
                next: event => {
                    if (event.type === HttpEventType.UploadProgress && event.total) {
                        const progress = Math.round((event.loaded / event.total) * 100);
                        this.uploads.update(list => list.map(u => u.id === id ? { ...u, progress } : u));
                    } else if (event.type === HttpEventType.Response && event.body) {
                        this.uploads.update(list => list.filter(u => u.id !== id));
                        resolve(event.body);
                    }
                },
                error: () => {
                    this.uploads.update(list => list.map(u => u.id === id ? { ...u, error: 'Upload failed (type/size?)' } : u));
                    setTimeout(() => this.uploads.update(list => list.filter(u => u.id !== id)), 3000);
                    resolve(null);
                },
            });
        });
    }

    // Debounced from onTransaction (every keystroke) — measuring the DOM on every single
    // transaction would be wasteful, and diffing only matters once typing settles for a moment.
    private scheduleRuDiffRecompute() {
        clearTimeout(this.ruDiffTimer);
        this.ruDiffTimer = setTimeout(() => this.recomputeRuDiff(), 200);
    }

    private recomputeRuDiff() {
        const snapshot = this.activeSourceSnapshot();
        // The gutter belongs to whichever language owns the canonical document — comparing against
        // a literal 'ru' left it dead on a draft whose primary is another language, and drew a
        // full-document diff on the RU tab once RU became a translation (ADR-065).
        if (!this.editor || this.lang() !== this.primaryLanguage || !snapshot) {
            this.ruDiffMarkers.set([]);
            return;
        }

        let oldBlocks: any[];
        try {
            oldBlocks = JSON.parse(snapshot)?.content ?? [];
        } catch {
            this.ruDiffMarkers.set([]);
            return;
        }
        const newBlocks: any[] = this.editor.getJSON()?.content ?? [];
        const ops = diffTopLevelBlocks(oldBlocks, newBlocks);

        const pmRoot = this.editorHost?.nativeElement?.querySelector('.ProseMirror') as HTMLElement | null;
        if (!pmRoot) {
            this.ruDiffMarkers.set([]);
            return;
        }
        // IB7/B11: the markers are absolutely positioned inside .sheet-wrap, so they must be
        // measured from .sheet-wrap's top — not .ProseMirror's. Measuring from .ProseMirror drew
        // every bar .sheet's 28px top padding too high (40px with the ruler on), which is the
        // "sits above the line it belongs to" the report describes.
        const wrap = this.editorHost.nativeElement.parentElement as HTMLElement | null;
        const containerTop = (wrap ?? pmRoot).getBoundingClientRect().top;
        const markers: { top: number; height: number; kind: 'added' | 'changed' | 'removed' }[] = [];
        for (const op of ops) {
            const child = pmRoot.children[op.newIndex] as HTMLElement | undefined;
            if (op.kind === 'removed') {
                // A deletion past the last surviving block has no element to sit next to, so it
                // marks the end of the document — also in .sheet-wrap coordinates.
                const top = (child ?? pmRoot).getBoundingClientRect()[child ? 'top' : 'bottom'] - containerTop;
                markers.push({ top, height: 3, kind: 'removed' });
                continue;
            }
            if (!child) continue;
            const rect = child.getBoundingClientRect();
            markers.push({ top: rect.top - containerTop, height: rect.height, kind: op.kind });
        }
        this.ruDiffMarkers.set(markers);
    }
}

interface BlockDiffOp {
    kind: 'added' | 'changed' | 'removed';
    newIndex: number;
}

// Classic LCS-based diff over top-level TipTap document blocks (paragraphs, headings, images,
// etc.), keyed by exact JSON equality — treats a run of consecutive deletions immediately
// alongside a run of insertions as pairwise "changed" blocks (matching how a text diff usually
// reads: a modified line is a delete+insert pair), leftover deletions become a thin "removed
// here" marker at the boundary since there's no surviving block position to attach them to.
function diffTopLevelBlocks(oldBlocks: any[], newBlocks: any[]): BlockDiffOp[] {
    const oldKeys = oldBlocks.map(b => JSON.stringify(b));
    const newKeys = newBlocks.map(b => JSON.stringify(b));
    const n = oldKeys.length, m = newKeys.length;

    const dp: number[][] = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
    for (let i = n - 1; i >= 0; i--) {
        for (let j = m - 1; j >= 0; j--) {
            dp[i][j] = oldKeys[i] === newKeys[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
        }
    }

    type RawOp = 'equal' | 'delete' | 'insert';
    const rawOps: RawOp[] = [];
    let i = 0, j = 0;
    while (i < n && j < m) {
        if (oldKeys[i] === newKeys[j]) { rawOps.push('equal'); i++; j++; }
        else if (dp[i + 1][j] >= dp[i][j + 1]) { rawOps.push('delete'); i++; }
        else { rawOps.push('insert'); j++; }
    }
    while (i < n) { rawOps.push('delete'); i++; }
    while (j < m) { rawOps.push('insert'); j++; }

    const result: BlockDiffOp[] = [];
    let newIndex = 0;
    let k = 0;
    while (k < rawOps.length) {
        if (rawOps[k] === 'equal') {
            newIndex++;
            k++;
            continue;
        }
        let deletes = 0, inserts = 0;
        while (k < rawOps.length && rawOps[k] !== 'equal') {
            if (rawOps[k] === 'delete') deletes++; else inserts++;
            k++;
        }
        const paired = Math.min(deletes, inserts);
        for (let p = 0; p < paired; p++) { result.push({ kind: 'changed', newIndex }); newIndex++; }
        for (let p = 0; p < inserts - paired; p++) { result.push({ kind: 'added', newIndex }); newIndex++; }
        if (deletes > paired) result.push({ kind: 'removed', newIndex });
    }
    return result;
}

function clampTableSize(n: number): number {
    return Math.min(Math.max(Math.round(n) || 1, 1), MAX_TABLE_SIZE);
}
