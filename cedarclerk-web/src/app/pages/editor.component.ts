import {
    AfterViewInit, Component, ElementRef, HostListener, OnDestroy,
    ViewChild, inject, signal
} from '@angular/core';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Editor } from '@tiptap/core';
import { EditorState, PluginKey, TextSelection } from '@tiptap/pm/state';
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
} from '../core/drafts.service';
import { FormPresetsService, FormPreset } from '../core/form-presets.service';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AccountMenuComponent } from '../shared/account-menu.component';
import { CountBadgeComponent } from '../shared/count-badge.component';
import { GlossaryTermFormComponent } from '../shared/glossary-term-form.component';
import { GlossaryService, GlossaryTermInput } from '../core/glossary.service';
import { AppearancePanelComponent } from '../shared/appearance-panel.component';
import { NgTemplateOutlet } from '@angular/common';
import { PostsService, PostFormat, CompressionLevel, UpdatePreview } from '../core/posts.service';
import { PublishService, PublishAccount, PublishJob, ThreadPart } from '../core/publish.service';
import { BillingService } from '../core/billing.service';
import { DraftRevision, DraftRevisionDetail, RevisionDiff } from '../core/drafts.service';
import { plainTextOf } from '../core/cedar-text.util';
import { DEFAULT_PRIMARY_LANGUAGE, CONTENT_LANGUAGES, endonymOf } from '../core/languages';
import { ChannelsService, Channel } from '../core/channels.service';
import { Table } from '@tiptap/extension-table';
import { TableRow } from '@tiptap/extension-table-row';
import { TableHeader } from '@tiptap/extension-table-header';
import { TableCell } from '@tiptap/extension-table-cell';
import { TaskList } from '@tiptap/extension-task-list';
import { TaskItem } from '@tiptap/extension-task-item';
import { Mathematics } from '@tiptap/extension-mathematics';
import { TextAlign } from '@tiptap/extension-text-align';
import { AssetsService, DraftAsset } from '../core/assets.service';
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
import { PopoverComponent } from '../shared/popover.component';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { ModalComponent } from '../shared/modal.component';
import { ThemeService } from '../core/theme.service';
import { VersionService } from '../core/version.service';
import { AppearanceService, SHEET_WIDTH_PX, TYPEFACE_STACK, MAX_TABLE_SIZE } from '../core/appearance.service';
import { ToolbarLayoutService } from '../core/toolbar-layout.service';
import { DebugLogService } from '../core/debug-log.service';
import { TagUsageService } from '../core/tag-usage.service';
import { TagPickerComponent } from '../shared/tag-picker.component';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { SeriesPickerComponent } from '../shared/series-picker.component';
import { MediaPickerComponent } from '../shared/media-picker.component';
import { LibraryAsset } from '../core/assets.service';
import { FormRefComponent } from '../shared/form-ref.component';
import { httpErrorMessage } from '../core/http-error.util';
import { pseudoProgress } from '../core/pseudo-progress.util';
import { BrandIconComponent } from '../shared/brand-icon.component';
import { IconComponent } from '../shared/icon.component';

const CHANNEL_COLORS = ['#C98A3B', '#5B6E46', '#3E7A4E', '#B4452C', '#6EB2F0', '#8A6FBF'];

// Must match .status-bar's height and the breakpoint that hides it in editor.component.css — the
// debug console slides out on top of that bar and needs to know it's there.
const STATUS_BAR_HEIGHT_PX = 27;
// FI2.11 — how long the "published" confirmation with its links stays up.
const STATUS_BAR_HIDDEN_MQ = '(max-width: 768px)';

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
// T-018.6 — widening gaps, then the manual "retry" button takes over rather than hammering on.
const SAVE_RETRY_DELAYS_MS = [2000, 5000, 15000];
// T-018.2 — the browser caps a keepalive body at 64KB; Cyrillic is 2 bytes per character, so this
// stays comfortably under it in the worst case rather than at the theoretical edge.
const KEEPALIVE_MAX_CHARS = 30_000;

// Distinguishes "the client gave up polling" from any other rejection in pollAiJob's callers,
// same role TimeoutError used to play for the old RxJS-based autoTranslate$/aiEdit$.
class AiJobTimeoutError extends Error {}

const BLOG_HOST = 'blog.mooexe.dev';

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

// B9 - 40 emoji in one unlabelled grid was both too few to find anything in and too wide for
// the popover, which overflowed to the right. Grouped and much longer now; the popover scrolls
// rather than growing, and each group is captioned so scanning has something to aim at.
// Deliberately a hand-picked set rather than a full Unicode table: a picker with every emoji in
// it needs search, and search needs names in six UI languages.
const EMOJI_GROUPS: { key: string; emoji: string[]; wide?: boolean }[] = [
    {
        key: 'faces',
        emoji: [
            '😀', '😃', '😄', '😁', '😅', '😂', '🙂', '😉', '😊', '😇',
            '😍', '😘', '😋', '😜', '🤪', '🤨', '🧐', '😎', '🥳', '🤩',
            '😏', '😒', '😞', '😢', '😭', '😤', '😡', '🤯', '😱', '😳',
            '🥺', '😬', '🙄', '😴', '🤒', '🤢', '🤠', '🥸', '🤖', '👻',
        ],
    },
    {
        key: 'gestures',
        emoji: [
            '👍', '👎', '👌', '✌️', '🤞', '🤙', '👋', '🤝', '🙏', '👏',
            '💪', '🫡', '🤷', '🤦', '🙌', '👀', '🧠', '🫶', '✍️', '🤌',
        ],
    },
    {
        key: 'symbols',
        emoji: [
            '❤️', '🧡', '💛', '💚', '💙', '💜', '🖤', '💔', '💯', '🔥',
            '✨', '⭐', '🌟', '⚡', '💥', '🎉', '🎊', '🚀', '💡', '🏆',
            '✅', '❌', '⚠️', '❓', '❗', '➡️', '⬅️', '🔁', '🔒', '🔓',
        ],
    },
    {
        key: 'objects',
        emoji: [
            '📌', '📎', '🔗', '📷', '🎬', '🎧', '🎮', '📚', '📝', '📅',
            '💻', '🖱️', '⌨️', '🗂️', '📦', '🛠️', '🧪', '🧭', '☕', '🍺',
            '🐮', '🌲', '🏔️', '🌊', '🌧️', '❄️', '🌙', '☀️', '🕹️', '🎲',
        ],
    },
    {
        key: 'flags',
        emoji: [
            '🇺🇦', '🇧🇾', '🇬🇪', '🇷🇺', '🇦🇲', '🇦🇿', '🇰🇿', '🇰🇬', '🇺🇿', '🇹🇯',
            '🇹🇲', '🇲🇩', '🇱🇻', '🇱🇹', '🇪🇪', '🇵🇱', '🇩🇪', '🇫🇷', '🇬🇧', '🇺🇸',
            '🇮🇹', '🇪🇸', '🇵🇹', '🇳🇱', '🇨🇿', '🇸🇰', '🇷🇸', '🇹🇷', '🇮🇱', '🇬🇷',
            '🇫🇮', '🇸🇪', '🇳🇴', '🇩🇰', '🇨🇭', '🇨🇳', '🇯🇵', '🇰🇷', '🇮🇳', '🇧🇷',
            '🇨🇦', '🇦🇺', '🇪🇺', '🇺🇳', '🏳️', '🏴', '🏁', '🚩', '🏳️‍🌈', '🏴‍☠️',
        ],
    },
    // Flags Unicode never got: the white-red-white flag and the 1991–1993 Russian tricolour with
    // its lighter blue. No codepoint exists for either, and how 🇷🇺 renders is the READER'S
    // platform font's choice, not ours — so these are the colour sequences people actually use in
    // Telegram, inserted as one button. Plain text end to end: editor, Telegram and blog all pass
    // them through untouched.
    {
        key: 'flagSequences',
        wide: true,
        emoji: ['⚪️🔴⚪️', '🤍❤️🤍', '🤍💙❤️', '💙💛'],
    },
];

interface UploadItem {
    id: number;
    name: string;
    progress: number;
    error?: string;
}

@Component({
    selector: 'app-editor',
    imports: [IconComponent, BrandIconComponent, FormsModule, ZonedDatePipe, NgTemplateOutlet, RouterLink, PopoverComponent, CedarLogoComponent, ModalComponent, AccountMenuComponent, AppearancePanelComponent, TagPickerComponent, FolderPickerComponent, SeriesPickerComponent, MediaPickerComponent, FormRefComponent, CountBadgeComponent, GlossaryTermFormComponent],
    templateUrl: 'editor.component.html',
    styleUrls: ['editor.component.css']
})
export class EditorComponent implements AfterViewInit, OnDestroy {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    version = inject(VersionService);
    appearance = inject(AppearanceService);
    toolbarLayout = inject(ToolbarLayoutService);
    private draftsApi = inject(DraftsService);
    private presetsApi = inject(FormPresetsService);
    feedback = inject(CommentsService);
    t = inject(LocaleService).t;
    private route = inject(ActivatedRoute);
    private assets = inject(AssetsService);
    debugLog = inject(DebugLogService);
    private tagUsageApi = inject(TagUsageService);

    // The status bar hosts the debug console's toggle, so the console is told how tall that bar
    // is — and that below the mobile breakpoint it isn't rendered at all, where the console goes
    // back to its own floating tab.
    private statusBarMq = window.matchMedia(STATUS_BAR_HIDDEN_MQ);
    private syncStatusBarHeight = () =>
        this.debugLog.hostBarHeight.set(this.statusBarMq.matches ? 0 : STATUS_BAR_HEIGHT_PX);

    @ViewChild('editorHost') editorHost!: ElementRef<HTMLElement>;
    private editor?: Editor;

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

    account(network: MicroNetwork): PublishAccount | null {
        switch (network) {
            case 'x': return this.xAccount();
            case 'discord': return this.discordAccount();
            default: return this.blueskyAccount();
        }
    }

    destination(network: MicroNetwork) {
        switch (network) {
            case 'x': return this.destX;
            case 'discord': return this.destDiscord;
            default: return this.destBluesky;
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
    termMenu = signal<{ x: number; y: number } | null>(null);
    termDraft = signal<{ term: string; language: string } | null>(null);
    termBusy = signal(false);
    termError = signal('');

    onSheetContextMenu(event: MouseEvent) {
        const selected = this.selectedText();
        // No selection: leave the browser's own menu alone. Replacing it with one disabled item
        // would take away spellcheck, copy and paste to offer nothing.
        if (!selected) return;
        event.preventDefault();
        this.termMenu.set({ x: event.clientX, y: event.clientY });
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
        } catch (e) {
            this.termError.set(httpErrorMessage(e, this.t().glossary.saveFailed));
        } finally {
            this.termBusy.set(false);
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
    exportModalOpen = signal(false);
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

    assetSortMark(key: 'name' | 'type' | 'size'): string {
        const { key: active, desc } = this.assetSort();
        return active === key ? (desc ? '↓' : '↑') : '';
    }

    draftAssetsLoading = signal(false);

    // Export destinations (B5) — tick a destination to unfold its settings; one Publish button
    // at the bottom fires every ticked one in sequence.
    destBlog = signal(false);
    destTelegram = signal(false);
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

    readonly emojiGroups = EMOJI_GROUPS;

    // B13 - reveals where the content actually is: spaces, tabs and paragraph ends. A pure
    // display toggle, nothing about the document changes.
    showInvisibles = signal(false);

    dtValue = '';
    dtWeekday = true;
    dtDate = true;
    dtTime = true;

    footnoteText = '';

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

    sheetMaxWidthPx(): number {
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

    channelColor(id: string): string {
        let hash = 0;
        for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
        return CHANNEL_COLORS[hash % CHANNEL_COLORS.length];
    }

    channelInitial(title: string): string {
        return (title?.[0] ?? '?').toUpperCase();
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

    // I13. Kept in sync with a listener rather than just toggled, because Esc and the browser's
    // own chrome can leave fullscreen without going through this button.
    isFullscreen = signal(!!document.fullscreenElement);

    @HostListener('document:fullscreenchange')
    onFullscreenChange() {
        this.isFullscreen.set(!!document.fullscreenElement);
    }

    async toggleFullscreen() {
        try {
            if (document.fullscreenElement) await document.exitFullscreen();
            else await document.documentElement.requestFullscreen();
        } catch {
            // Denied by the browser (permissions policy, or not a user gesture) — nothing to
            // report, the button simply doesn't take effect.
        }
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
        this.syncStatusBarHeight();
        this.statusBarMq.addEventListener('change', this.syncStatusBarHeight);
        document.addEventListener('visibilitychange', this.onVisibilityChange);
        window.addEventListener('pagehide', this.onPageHide);

        // Feeds the badge on the Posts Manager link (N3) — fire-and-forget, never blocks setup.
        this.feedback.refreshNewCount();
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
            ],
            content: '',
            onTransaction: () => {
                this.tick.update(v => v + 1);
                this.scheduleRuDiffRecompute();
            },
            onUpdate: () => this.markDirty(),
            onFocus: () => this.editorFocused.set(true),
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
        this.statusBarMq.removeEventListener('change', this.syncStatusBarHeight);
        document.removeEventListener('visibilitychange', this.onVisibilityChange);
        window.removeEventListener('pagehide', this.onPageHide);
        this.debugLog.hostBarHeight.set(0);
        clearTimeout(this.saveTimer);
        clearTimeout(this.saveRetryTimer);
        clearTimeout(this.aiToastTimer);
        clearInterval(this.aiEditTicker);
        clearInterval(this.autoTranslateTicker);
        clearInterval(this.exportTicker);
        clearInterval(this.blogTicker);
        clearTimeout(this.ruDiffTimer);
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
        if (!window.confirm(this.t().editor.lang.deleteEnglishConfirm)) return;
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
            this.watermarkText.set(draft.watermarkText);
            this.watermarkInput = draft.watermarkText ?? '';
            this.watermarkError.set(null);
            this.invites.set([]);
            this.regForm.set(parseRegistrationForm(draft.registrationFormJson));
            this.formLanguages.set(draft.formLanguages ?? []);
            this.isListedWhilePrivate.set(draft.isListedWhilePrivate ?? false);
            this.disableCopy.set(draft.disableCopy ?? false);
            this.editor?.setEditable(true);
            this.editor?.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC), { emitUpdate: false });
            this.resetHistory();
            this.saveState.set('saved');
            this.currentBlog.set(draft.blogSlug ? { slug: draft.blogSlug, isPublished: draft.isBlogPublished } : null);
            this.blogError.set(null);

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

            const meta: DraftMeta = {
                id: created.id, title,
                createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
                primaryLanguage: this.primaryLanguage,
                blogSlug: null, isBlogPublished: false, blogPublishedAt: null,
                languages, tags: tags.join(','),
                isArchived: false, lastTelegramMessageId: null, lastTelegramUsername: null,
                staleLanguages: [], scheduled: null, folderId, seriesId: null, parentDraftId: null, siblingOrder: 0,
                isPrivate, isTemplate: false, disableCopy: false,
                disableReactions: false, disableComments: false,
                viewCount: 0, reactionCount: 0, newViewCount: 0, newReactionCount: 0,
            };
            this.drafts.update(l => [meta, ...l]);
            this.currentId.set(created.id);
            this.title = title;
            this.primaryLanguage = DEFAULT_PRIMARY_LANGUAGE;
            this.lang.set(this.primaryLanguage);
            this.exportLangs.set([this.primaryLanguage]);
            this.ruUpdatedAt.set(meta.updatedAt);
            this.translations.set(Object.fromEntries(
                languages.filter(l => l !== DEFAULT_PRIMARY_LANGUAGE).map(l => [l, { language: l, title, updatedAt: meta.updatedAt }])));
            this.activeSourceSnapshot.set(null);
            this.ruDiffMarkers.set([]);
            this.ruSnapshot = null;
            this.tagList.set(tags);
            this.currentFolderId.set(folderId);
            this.currentSeriesId.set(null);
            this.isPrivate.set(isPrivate);
            this.disableCopy.set(false);
            this.watermarkText.set(null);
            this.watermarkInput = '';
            this.watermarkError.set(null);
            this.invites.set([]);
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
        return b ? `https://${BLOG_HOST}/${b.slug}` : null;
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
            const res = await this.draftsApi.publishToBlog(id, this.confirmedFingerprints);
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

    async openExportModal() {
        this.exportModalOpen.set(true);
        // Presets are the only form control left in this modal (N12) — a failed load just means
        // no preset chips, never a blocked export.
        this.presetsApi.list().then(p => this.formPresets.set(p)).catch(() => this.formPresets.set([]));
        // T-086 — asked once per opening, not per keystroke: it reads the stored document, which is
        // what would be sent anyway.
        this.refreshPublishIssues();
        this.loadShortPostTargets();
        const id = this.currentId();
        if (!id) return;
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

    // "Remove from draft" only detaches the media node(s) from the current document — it does not
    // delete the underlying Asset/file (no delete-asset endpoint exists; the same upload could in
    // principle be referenced elsewhere), so this is a safe, reversible-via-undo edit rather than
    // a destructive storage operation.
    removeAssetFromDraft(asset: DraftAsset) {
        if (!this.editor) return;
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
        return tx.publish;
    }

    canPublishAll(): boolean {
        if (this.publishingAll() || this.blogBusy() || this.exporting()) return false;
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
                const { jobs } = await this.publishApi.queue(id, [targetId], lang, this.confirmedFingerprints[lang], this.splitIntoThread());
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
                        () => this.posts.schedule(id, scheduledAtUtc, lang, { chatId }, this.format));
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
            this.uploadFilePromise(file).then(url => { if (url) this.editor?.chain().focus().setImage({ src: url }).run(); });
        }
    }

    onVideoChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        for (const file of files) {
            this.uploadFilePromise(file).then(url => { if (url) this.insertNode('video', { src: url }); });
        }
    }

    // .gif needs a <video> tag so Telegram treats it as an animation, not a static photo
    onGifChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        for (const file of files) {
            this.uploadFilePromise(file).then(url => { if (url) this.insertNode('video', { src: url }); });
        }
    }

    onAudioChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        for (const file of files) {
            this.uploadFilePromise(file).then(url => { if (url) this.insertNode('audio', { src: url }); });
        }
    }

    onCarouselChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        if (!files.length) return;
        Promise.all(files.map(f => this.uploadFilePromise(f))).then(urls => {
            const images = urls.filter((u): u is string => !!u);
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
        this.wikiSuggest.set({
            x: rect.left,
            y: rect.bottom + 4,
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
        this.insertMediaUrl(`/media/${asset.localPath}`, asset.contentType);
    }

    private insertMediaUrl(url: string, contentType: string) {
        if (contentType === 'image/gif' || contentType.startsWith('video/')) {
            this.insertNode('video', { src: url });
        } else if (contentType.startsWith('audio/')) {
            this.insertNode('audio', { src: url });
        } else {
            this.editor?.chain().focus().setImage({ src: url }).run();
        }
    }

    private uploadAndInsert(file: File) {
        void this.uploadFilePromise(file).then(url => { if (url) this.insertMediaUrl(url, file.type); });
    }

    onCollageChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        if (!files.length) return;
        Promise.all(files.map(f => this.uploadFilePromise(f))).then(urls => {
            const images = urls.filter((u): u is string => !!u);
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

    insertFootnote() {
        const text = this.footnoteText.trim();
        if (!text) return;
        this.cmd(c => c.insertContent({ type: 'footnote', attrs: { text } }));
        this.footnoteText = '';
    }

    insertTable() {
        // I5 — the size comes from Appearance now instead of a hardcoded 3×3. Clamped on read as
        // well as on write, since the preference blob is user-editable via the API.
        const rows = Math.min(Math.max(this.appearance.prefs().tableRows, 1), MAX_TABLE_SIZE);
        const cols = Math.min(Math.max(this.appearance.prefs().tableCols, 1), MAX_TABLE_SIZE);
        this.cmd(c => c.insertTable({ rows, cols, withHeaderRow: true }));
    }

    canAnnotate(): boolean {
        this.tick();
        return !!this.editor && !this.editor.state.selection.empty;
    }

    insertAnnotation() {
        if (this.canAnnotate()) this.cmd(c => c.wrapIn('annotation', { id: crypto.randomUUID() }));
    }

    insertInlineMath() {
        const latex = window.prompt('Formula (LaTeX), e.g.: E = mc^2');
        if (latex) this.cmd(c => c.insertInlineMath({ latex }));
    }

    insertBlockMath() {
        const latex = window.prompt('Formula (LaTeX), block, e.g.: \\int_0^1 x^2\\,dx');
        if (latex) this.cmd(c => c.insertBlockMath({ latex }));
    }

    indent() {
        if (!this.editor) return;
        const type = this.editor.isActive('taskItem') ? 'taskItem' : 'listItem';
        this.editor.chain().focus().sinkListItem(type).run();
    }

    outdent() {
        if (!this.editor) return;
        const type = this.editor.isActive('taskItem') ? 'taskItem' : 'listItem';
        this.editor.chain().focus().liftListItem(type).run();
    }

    private insertNode(type: string, attrs: Record<string, any>) {
        this.editor?.chain().focus().insertContent({ type, attrs }).run();
    }

    private uploadFilePromise(file: File): Promise<string | null> {
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
                        resolve(event.body.url);
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
