import {
    AfterViewInit, Component, ElementRef, HostListener, OnDestroy,
    ViewChild, inject, signal
} from '@angular/core';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Editor } from '@tiptap/core';
import { EditorState, TextSelection } from '@tiptap/pm/state';
import { Node as PMNode, Slice } from '@tiptap/pm/model';
import StarterKit from '@tiptap/starter-kit';
import { AuthService } from '../core/auth.service';
import {
    DraftsService, DraftMeta, TranslationMeta, AiEditKind, PostInvite,
    RegistrationForm, parseRegistrationForm, WATERMARK_MAX_LENGTH,
} from '../core/drafts.service';
import { FormPresetsService, FormPreset } from '../core/form-presets.service';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AccountMenuComponent } from '../shared/account-menu.component';
import { CountBadgeComponent } from '../shared/count-badge.component';
import { AppearancePanelComponent } from '../shared/appearance-panel.component';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { PostsService, PostFormat, PostLanguage, CompressionLevel, ScheduledPost } from '../core/posts.service';
import { ChannelsService, Channel, ChannelStats, KnownChat } from '../core/channels.service';
import { Table } from '@tiptap/extension-table';
import { TableRow } from '@tiptap/extension-table-row';
import { TableHeader } from '@tiptap/extension-table-header';
import { TableCell } from '@tiptap/extension-table-cell';
import { TaskList } from '@tiptap/extension-task-list';
import { TaskItem } from '@tiptap/extension-task-item';
import { Mathematics } from '@tiptap/extension-mathematics';
import { AssetsService, DraftAsset } from '../core/assets.service';
import { VideoNode } from '../tiptap-extensions/video-node';
import { AudioNode } from '../tiptap-extensions/audio-node';
import { CarouselNode } from '../tiptap-extensions/carousel-node';
import { CollageNode } from '../tiptap-extensions/collage-node';
import { SpoilerMark } from '../tiptap-extensions/spoiler-mark';
import { DateTimeNode } from '../tiptap-extensions/datetime-node';
import { ToggleNode } from '../tiptap-extensions/toggle-node';
import { ImageNode } from '../tiptap-extensions/image-node';
import { FootnoteNode } from '../tiptap-extensions/footnote-node';
import { AnnotationNode } from '../tiptap-extensions/annotation-node';
import { TableOfContentsNode } from '../tiptap-extensions/table-of-contents-node';
import { YoutubeNode, extractYouTubeId } from '../tiptap-extensions/youtube-node';
import { PopoverComponent } from '../shared/popover.component';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { ModalComponent } from '../shared/modal.component';
import { ThemeService } from '../core/theme.service';
import { AppearanceService, SHEET_WIDTH_PX, TYPEFACE_STACK, MAX_TABLE_SIZE } from '../core/appearance.service';
import { ToolbarLayoutService } from '../core/toolbar-layout.service';
import { httpErrorMessage } from '../core/http-error.util';
import { pseudoProgress } from '../core/pseudo-progress.util';
import { Subscription, TimeoutError } from 'rxjs';
import {
    LucideUndo2 as Undo2, LucideRedo2 as Redo2,
    LucideBold as Bold, LucideItalic as Italic, LucideStrikethrough as Strikethrough, LucideCode as Code,
    LucideList as List, LucideListOrdered as ListOrdered, LucideListTodo as ListTodo,
    LucideQuote as Quote, LucideSquareCode as SquareCode,
    LucideOutdent as Outdent, LucideIndent as Indent,
    LucideTable as TableIcon, LucideSigma as Sigma, LucideSigmaSquare as SigmaSquare,
    LucideImage as ImageIcon, LucideVideo as VideoIcon, LucideAudioLines as AudioLines, LucideImages as Images,
    LucideSend as Send, LucidePlus as Plus, LucideX as X,
    LucideTrash2 as Trash2,
    LucideEyeOff as EyeOff, LucideLink as LinkIcon, LucideSmile as Smile, LucideUnderline as Underline,
    LucideClock as Clock, LucideListCollapse as ListCollapse, LucideLayoutGrid as LayoutGrid,
    LucideFileStack as FileStack, LucideSuperscript as Superscript,
    LucideChevronDown as ChevronDown,
    LucideCheck as Check,
    LucideDownload as Download,
    LucideMessageSquare as MessageSquare,
    LucideDroplets as Droplets,
    LucideMaximize as Maximize, LucideMinimize as Minimize,
    LucideLineChart as LineChart,
    LucideRefreshCw as RefreshCw,
    LucideSettings as Settings, LucideShieldCheck as ShieldCheck, LucideSparkle as Sparkle,
    LucideTableOfContents as TableOfContentsIcon,
    LucideSeparatorHorizontal as DividerIcon,
    LucideAtSign as AtSign, LucideCloud as Cloud, LucideMessageSquareShare as MessageSquareShare,
    LucideFileText as FileText, LucideHeart as Heart, LucideNotebook as Notebook, LucideFile as FileIcon,
    LucideThumbsUp as ThumbsUp,
    LucideFolder as Folder, LucideLock as Lock,
} from '@lucide/angular';

const CHANNEL_COLORS = ['#C98A3B', '#5B6E46', '#3E7A4E', '#B4452C', '#6EB2F0', '#8A6FBF'];

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

type SaveState = 'saved' | 'saving' | 'dirty' | 'error';
// DB2.6 — matches the maxlength on the title inputs.
export const DRAFT_TITLE_MAX = 64;

const EMPTY_DOC = '{"type":"doc","content":[{"type":"paragraph"}]}';
const BLOG_HOST = 'blog.mooexe.dev';

type NewDraftTemplate = 'blank' | 'devlog' | 'photodump';
const NEW_DRAFT_TEMPLATES: Record<NewDraftTemplate, string> = {
    blank: EMPTY_DOC,
    devlog: JSON.stringify({
        type: 'doc',
        content: [
            { type: 'paragraph', content: [{ type: 'text', text: 'What happened this week…' }] },
            {
                type: 'bulletList',
                content: [
                    { type: 'listItem', content: [{ type: 'paragraph' }] },
                    { type: 'listItem', content: [{ type: 'paragraph' }] },
                ],
            },
            { type: 'paragraph', content: [{ type: 'text', text: "What's next." }] },
        ],
    }),
    photodump: JSON.stringify({
        type: 'doc',
        content: [
            { type: 'paragraph', content: [{ type: 'text', text: 'A few photos from…' }] },
            { type: 'paragraph' },
        ],
    }),
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

const COMMON_EMOJI = [
    '😀', '😂', '😅', '😉', '😊', '😍', '🤔', '😎', '😢', '😡',
    '👍', '👎', '👏', '🙏', '💪', '🤝', '👋', '✌️', '🤞', '🫡',
    '❤️', '🔥', '✨', '🎉', '🚀', '⭐', '💯', '⚡', '🌟', '💡',
    '✅', '❌', '⚠️', '❓', '❗', '📌', '📎', '🔗', '📷', '🎬',
];

interface UploadItem {
    id: number;
    name: string;
    progress: number;
    error?: string;
}

@Component({
    selector: 'app-editor',
    imports: [
        FormsModule, DatePipe, NgTemplateOutlet, RouterLink, PopoverComponent, CedarLogoComponent, ModalComponent,
        AccountMenuComponent, AppearancePanelComponent,
        Undo2, Redo2, Bold, Italic, Strikethrough, Code,
        List, ListOrdered, ListTodo, Quote, SquareCode, Outdent, Indent,
        TableIcon, Sigma, SigmaSquare, ImageIcon, VideoIcon, AudioLines, Images,
        Send, Plus, X, Trash2,
        EyeOff, LinkIcon, Smile, Underline, Clock, ListCollapse, LayoutGrid, FileStack, Superscript,
        ChevronDown, Check, Download, MessageSquare, Droplets, LineChart, RefreshCw, Maximize, Minimize,
        Settings, ShieldCheck, Sparkle, TableOfContentsIcon, DividerIcon,
        CountBadgeComponent,
        AtSign, Cloud, MessageSquareShare, FileText, Heart, Notebook, FileIcon, ThumbsUp,
        Folder, Lock,
    ],
    templateUrl: 'editor.component.html',
    styleUrls: ['editor.component.css']
})
export class EditorComponent implements AfterViewInit, OnDestroy {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    appearance = inject(AppearanceService);
    toolbarLayout = inject(ToolbarLayoutService);
    private draftsApi = inject(DraftsService);
    private presetsApi = inject(FormPresetsService);
    feedback = inject(CommentsService);
    t = inject(LocaleService).t;
    private route = inject(ActivatedRoute);
    private assets = inject(AssetsService);

    @ViewChild('editorHost') editorHost!: ElementRef<HTMLElement>;
    private editor?: Editor;
    private tick = signal(0);

    drafts = signal<DraftMeta[]>([]);
    currentId = signal<string | null>(null);
    saveState = signal<SaveState>('saved');
    title = '';

    private saveTimer?: ReturnType<typeof setTimeout>;

    private posts = inject(PostsService); // + import сверху
    private channelsApi = inject(ChannelsService);

    chatId = '@testingandfun';
    // Telegram export is Markdown-only — the Rich Message HTML mode needs exact custom tag
    // names (<photo>, <tg-slideshow>, ...) and has repeatedly broken in practice; Markdown uses
    // plain, well-tested syntax for the same underlying rich-block output. HTML stays in use for
    // the blog (CedarToBlogHtmlRenderer, a separate/unrelated renderer).
    readonly format: PostFormat = 'Markdown';
    exportLang: PostLanguage = 'ru';
    compressionLevel: CompressionLevel = 'standard';

    // Active content language in the editor. 'ru' edits the draft itself (primary version),
    // 'en' edits the DraftTranslation row. Only one editor instance — switching tabs flushes
    // the autosave for the language being left, then loads the other version's content.
    lang = signal<PostLanguage>('ru');
    enMeta = signal<TranslationMeta | null>(null);
    private ruUpdatedAt = signal<string>('');
    // RU title+content captured when entering the EN tab — source for "Copy from Russian"
    private ruSnapshot: { title: string; json: string } | null = null;

    // RU CedarJson as it was the last time the EN translation was synced (created/saved/
    // auto-translated) — lets the RU tab show a gutter of what's changed structurally since,
    // instead of just the boolean enStale() flag. Null when there's no EN version, or an older
    // translation predating the SourceSnapshotJson column that hasn't been resynced since.
    private enSourceSnapshot = signal<string | null>(null);
    ruDiffMarkers = signal<{ top: number; height: number; kind: 'added' | 'changed' | 'removed' }[]>([]);
    private ruDiffTimer?: ReturnType<typeof setTimeout>;

    // Tags are per-draft (shared across language versions) and saved through their own endpoint
    // immediately on add/remove — not through the content autosave, which routes per-language.
    tagList = signal<string[]>([]);
    // "Cloud" tag picker (ADR-035) — usage counts across every draft, loaded once on first open.
    tagUsage = signal<{ tag: string; count: number }[]>([]);
    private tagUsageLoaded = false;
    tagInput = '';

    // At most one folder per draft (see the ADR following ADR-038, docs/DECISIONS.md) — mirrors
    // the tag-cloud's lazy-load-on-first-open pattern above.
    currentFolderId = signal<string | null>(null);
    folders = signal<{ id: string; name: string; count: number }[]>([]);
    private foldersLoaded = false;

    aiEditBusy = signal(false);
    aiEditElapsed = signal(0);
    // Pseudo-progress (0-100, Phase 8 Step 8) — neither AI provider streams a response, so this
    // is an asymptotic estimate (pseudo-progress.util.ts), not a real percentage.
    aiEditProgress = signal(0);
    private aiEditTicker?: ReturnType<typeof setInterval>;
    private aiEditSub?: Subscription;
    aiEditError = signal<string | null>(null);
    aiConfirmKind = signal<AiEditKind | null>(null);
    aiToast = signal<string | null>(null);
    private aiToastTimer?: ReturnType<typeof setTimeout>;

    autoTranslating = signal(false);
    autoTranslateElapsed = signal(0);
    autoTranslateProgress = signal(0);
    private autoTranslateTicker?: ReturnType<typeof setInterval>;
    private autoTranslateSub?: Subscription;
    autoTranslateError = signal<string | null>(null);
    translateConfirmOpen = signal(false);
    exportModalOpen = signal(false);
    draftAssets = signal<DraftAsset[]>([]);
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

    // Watermark (I7) — drawn on the blog page only, never in the editor. watermarkText() is the
    // saved value (what the state strip reports); watermarkInput is the unsaved field content.
    watermarkText = signal<string | null>(null);
    watermarkInput = '';
    watermarkBusy = signal(false);
    watermarkError = signal<string | null>(null);
    readonly watermarkMaxLength = WATERMARK_MAX_LENGTH;

    uploads = signal<UploadItem[]>([]);
    private uploadSeq = 0;

    channels = signal<Channel[]>([]);
    channelStats = signal<Record<string, ChannelStats>>({});
    newChannelChatId = '';
    // N4 — manual @username/id entry is a fallback behind a disclosure, not part of the normal
    // flow: the discovered-chats list is empty for an account with no linked Telegram.
    manualChannelOpen = signal(false);
    channelError = signal('');

    knownChats = signal<KnownChat[]>([]);
    knownChatsRefreshing = signal(false);

    // Guards openDraft/newDraft — both mutate `currentId`/`drafts` and must not race each other
    // (e.g. a double-click while a draft is still loading). Deletion lives on /drafts now.
    draftsBusy = signal(false);
    channelBusy = signal(false);

    // New Draft dialog (ADR-035) — minimal title+Enter, expandable to languages/tags/template.
    // Channels/schedule-at-creation from the mockup were deliberately dropped: Cedar Clerk has no
    // draft-to-channel relationship at creation time, only at export (see ADR-035).
    newDraftOpen = signal(false);
    newDraftExpanded = signal(false);
    newDraftTitle = '';
    readonly draftTitleMax = DRAFT_TITLE_MAX;
    newDraftLanguages: 'ru' | 'en' | 'both' = 'ru';
    newDraftTags = '';
    newDraftTemplate: NewDraftTemplate = 'blank';
    // Not persisted into newDraftDefaultsJson (unlike languages/tags/template) — "private" and
    // a target folder are per-draft intent, not a preference to repeat on every new draft.
    newDraftPrivate = false;
    newDraftFolderId: string | null = null;

    scheduledAt = '';
    scheduling = signal(false);
    scheduleResult = signal('');
    scheduledPosts = signal<ScheduledPost[]>([]);

    readonly commonEmoji = COMMON_EMOJI;

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

    saveLabel(): string {
        switch (this.saveState()) {
            case 'saved': return 'Saved';
            case 'saving': return this.t().editor.saving;
            case 'dirty': return 'Unsaved changes';
            case 'error': return 'Sync failed';
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

    isSelectedChannel(c: Channel): boolean {
        return this.chatId.trim() === String(c.telegramChatId);
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
        this.save();
    }

    async ngAfterViewInit() {
        // Feeds the badge on the Posts Manager link (N3) — fire-and-forget, never blocks setup.
        this.feedback.refreshNewCount();
        // IB6: the folder list used to load only when the picker popover was first opened, but
        // folderName() needs it to resolve the *label* too — so a draft in a folder read as "no
        // folder" on every fresh editor load (most visibly when coming back from Settings) until
        // you clicked the picker. Same fire-and-forget treatment as the badge above.
        this.ensureFoldersLoaded();
        const mediaNodeTypes = new Set(['image', 'video', 'audio', 'carousel', 'collage']);
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
                DateTimeNode,
                ToggleNode,
                AnnotationNode,
                TableOfContentsNode,
                YoutubeNode,
                Table.configure({ resizable: false }),
                TableRow,
                TableHeader,
                TableCell,
                TaskList,
                TaskItem.configure({ nested: true }),
                Mathematics,
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

        const list = await this.draftsApi.list();
        this.drafts.set(list);
        // /drafts links here as /editor?draft=<id> — fall back to the most recent draft (previous
        // behavior) if the id is missing/stale (e.g. deleted from another tab).
        const requestedId = this.route.snapshot.queryParamMap.get('draft');
        const targetId = requestedId && list.some(d => d.id === requestedId) ? requestedId : list[0]?.id;
        if (targetId) await this.openDraft(targetId);
        else await this.newDraft();

        // /drafts' "New draft" button links here as /editor?new=1
        if (this.route.snapshot.queryParamMap.has('new')) this.openNewDraftDialog();

        this.channels.set(await this.channelsApi.list());
        await this.refreshScheduledPosts();
        await this.refreshChannelStats();
        this.knownChats.set(await this.channelsApi.listKnown());
    }

    private async refreshChannelStats() {
        const entries = await Promise.all(this.channels().map(async c => {
            try {
                return [c.id, await this.channelsApi.getStats(c.id)] as const;
            } catch {
                return [c.id, null] as const;
            }
        }));
        this.channelStats.set(Object.fromEntries(entries.filter((e): e is [string, ChannelStats] => e[1] !== null)));
    }

    sparklinePoints(snapshots: { takenAt: string; memberCount: number }[]): string {
        if (snapshots.length < 2) return '';
        const values = snapshots.map(s => s.memberCount);
        const min = Math.min(...values);
        const max = Math.max(...values);
        const range = max - min || 1;
        const w = 60, h = 20;
        return values
            .map((v, i) => `${(i / (values.length - 1) * w).toFixed(1)},${(h - (v - min) / range * h).toFixed(1)}`)
            .join(' ');
    }

    ngOnDestroy() {
        clearTimeout(this.saveTimer);
        clearTimeout(this.aiToastTimer);
        clearInterval(this.aiEditTicker);
        clearInterval(this.autoTranslateTicker);
        clearInterval(this.exportTicker);
        clearInterval(this.blogTicker);
        clearTimeout(this.ruDiffTimer);
        this.aiEditSub?.unsubscribe();
        this.autoTranslateSub?.unsubscribe();
        this.editor?.destroy();
    }

    markDirty() {
        this.saveState.set('dirty');
        clearTimeout(this.saveTimer);
        this.saveTimer = setTimeout(() => this.save(), 1200);
    }

    private async save() {
        const id = this.currentId();
        if (!id || !this.editor) return;
        // EN tab with no version created yet — nothing to save (editor is read-only there anyway)
        if (this.lang() === 'en' && !this.enMeta()) {
            this.saveState.set('saved');
            return;
        }
        this.saveState.set('saving');
        try {
            const json = JSON.stringify(this.editor.getJSON());
            if (this.lang() === 'ru') {
                const res = await this.draftsApi.update(id, this.title, json);
                this.ruUpdatedAt.set(res.updatedAt);
                this.refreshMeta(id, res.updatedAt);
            } else {
                const res = await this.draftsApi.saveTranslation(id, 'en', this.title, json);
                this.enMeta.set({ language: 'en', title: this.title, updatedAt: res.updatedAt });
                this.enSourceSnapshot.set(res.sourceSnapshotJson);
            }
            this.saveState.set('saved');
        } catch {
            this.saveState.set('error');
        }
    }

    showEnEmptyState(): boolean {
        return this.lang() === 'en' && !this.enMeta();
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

    // The RU version was edited after the EN translation was last touched — probably needs re-translating
    enStale(): boolean {
        const en = this.enMeta();
        if (!en) return false;
        // Compared as instants, not as strings (IB3): the two timestamps reach the client through
        // different endpoints, and a string compare would flip on nothing more than a difference
        // in fractional-second digits or a trailing Z.
        const ru = Date.parse(this.ruUpdatedAt());
        const tr = Date.parse(en.updatedAt);
        if (!Number.isFinite(ru) || !Number.isFinite(tr)) return this.ruUpdatedAt() > en.updatedAt;
        return ru > tr;
    }

    async switchLang(target: PostLanguage) {
        const id = this.currentId();
        if (target === this.lang() || !id || !this.editor) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        if (target === 'ru') {
            const draft = await this.draftsApi.get(id);
            this.lang.set('ru');
            this.title = draft.title;
            this.ruUpdatedAt.set(draft.updatedAt);
            this.editor.setEditable(true);
            this.editor.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC), { emitUpdate: false });
            this.resetHistory();
        } else {
            this.ruSnapshot = { title: this.title, json: JSON.stringify(this.editor.getJSON()) };
            if (this.enMeta()) {
                const tr = await this.draftsApi.getTranslation(id, 'en');
                this.lang.set('en');
                this.title = tr.title;
                this.enMeta.set({ language: 'en', title: tr.title, updatedAt: tr.updatedAt });
                this.enSourceSnapshot.set(tr.sourceSnapshotJson);
                this.editor.setEditable(true);
                this.editor.commands.setContent(JSON.parse(tr.cedarJson || EMPTY_DOC), { emitUpdate: false });
                this.resetHistory();
            } else {
                // No EN version yet — show the empty state (Copy from Russian / Start empty)
                this.lang.set('en');
                this.title = this.ruSnapshot.title;
                this.editor.setEditable(false);
                this.editor.commands.setContent(JSON.parse(EMPTY_DOC), { emitUpdate: false });
                this.resetHistory();
            }
        }
        this.saveState.set('saved');
        this.scheduleRuDiffRecompute();
    }

    async startEnVersion(copyFromRu: boolean) {
        const id = this.currentId();
        if (!id || !this.editor) return;
        // Use the title as currently shown/edited, not the RU snapshot — the title field stays
        // live in the "no EN version yet" empty state, so the user may already have renamed it
        // for the English version before clicking either button here.
        const title = this.title;
        const json = copyFromRu ? (this.ruSnapshot?.json ?? EMPTY_DOC) : EMPTY_DOC;
        try {
            const res = await this.draftsApi.saveTranslation(id, 'en', title, json);
            this.enMeta.set({ language: 'en', title, updatedAt: res.updatedAt });
            this.enSourceSnapshot.set(res.sourceSnapshotJson);
            this.title = title;
            this.editor.setEditable(true);
            this.editor.commands.setContent(JSON.parse(json), { emitUpdate: false });
            this.resetHistory();
            this.editor.commands.focus();
            this.saveState.set('saved');
            this.drafts.update(list => list.map(d => d.id === id ? { ...d, languages: ['en'] } : d));
        } catch {
            this.saveState.set('error');
        }
    }

    async addTag() {
        const t = this.tagInput.trim().replace(/^#/, '').replace(/,/g, '').toLowerCase();
        this.tagInput = '';
        if (!t || this.tagList().includes(t)) return;
        this.tagList.update(l => [...l, t]);
        await this.persistTags();
    }

    async removeTag(tag: string) {
        this.tagList.update(l => l.filter(t => t !== tag));
        await this.persistTags();
    }

    async toggleTag(tag: string) {
        if (this.tagList().includes(tag)) {
            await this.removeTag(tag);
        } else {
            this.tagList.update(l => [...l, tag]);
            await this.persistTags();
        }
    }

    async ensureTagUsageLoaded() {
        if (this.tagUsageLoaded) return;
        this.tagUsageLoaded = true;
        try {
            this.tagUsage.set(await this.draftsApi.listTagUsage());
        } catch {
            this.tagUsageLoaded = false; // allow a retry next time the popover opens
        }
    }

    private async persistTags() {
        const id = this.currentId();
        if (!id) return;
        try {
            const res = await this.draftsApi.updateTags(id, this.tagList().join(','));
            this.drafts.update(list => list.map(d => d.id === id ? { ...d, tags: res.tags } : d));
        } catch {
            this.saveState.set('error');
        }
    }

    async ensureFoldersLoaded() {
        if (this.foldersLoaded) return;
        this.foldersLoaded = true;
        try {
            this.folders.set(await this.draftsApi.listFolders());
        } catch {
            this.foldersLoaded = false; // allow a retry next time the popover opens
        }
    }

    folderName(id: string | null): string {
        if (id === null) return this.t().editor.tags.noFolder;
        const known = this.folders().find(f => f.id === id);
        if (known) return known.name;
        // The draft says it's filed but the list hasn't arrived yet — say nothing rather than
        // say "no folder", which is the false claim IB6 was about.
        return this.folders().length === 0 ? '…' : this.t().editor.tags.noFolder;
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

    // Machine-translates the RU version into EN and loads the result into the editor for review.
    // Replacing an existing translation goes through a confirm modal first (see confirmTranslate()).
    autoTranslateEn() {
        if (!this.currentId() || !this.editor) return;
        if (this.enMeta()) {
            this.translateConfirmOpen.set(true);
            return;
        }
        this.runAutoTranslate();
    }

    cancelTranslateConfirm() {
        this.translateConfirmOpen.set(false);
    }

    confirmTranslate() {
        this.translateConfirmOpen.set(false);
        this.runAutoTranslate();
    }

    private runAutoTranslate() {
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

        this.autoTranslateSub = this.draftsApi.autoTranslate$(id, 'en').subscribe({
            next: tr => {
                this.autoTranslateProgress.set(100);
                this.enMeta.set({ language: 'en', title: tr.title, updatedAt: tr.updatedAt });
                this.enSourceSnapshot.set(tr.sourceSnapshotJson);
                this.drafts.update(list => list.map(d => d.id === id ? { ...d, languages: ['en'] } : d));
                if (this.lang() !== 'en') {
                    this.ruSnapshot = { title: this.title, json: JSON.stringify(editor.getJSON()) };
                    this.lang.set('en');
                }
                this.title = tr.title;
                editor.setEditable(true);
                editor.commands.setContent(JSON.parse(tr.cedarJson || EMPTY_DOC), { emitUpdate: false });
                this.saveState.set('saved');
            },
            error: e => {
                this.autoTranslateError.set(e instanceof TimeoutError
                    ? 'Auto-translate timed out after 3 minutes'
                    : httpErrorMessage(e, this.t().editor.errors.autoTranslate));
                this.finishAutoTranslate();
            },
            complete: () => this.finishAutoTranslate(),
        });
    }

    private finishAutoTranslate() {
        this.autoTranslating.set(false);
        clearInterval(this.autoTranslateTicker);
        this.autoTranslateSub = undefined;
    }

    // User-initiated cancel (Step 8) — unsubscribing aborts the underlying HTTP request; no
    // error/toast shown since this wasn't a failure, the user just changed their mind.
    cancelAutoTranslate() {
        this.autoTranslateSub?.unsubscribe();
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
    private aiEdit(kind: AiEditKind) {
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

        this.aiEditSub = this.draftsApi.aiEdit$(id, this.lang(), kind).subscribe({
            next: res => {
                this.aiEditProgress.set(100);
                this.title = res.title;
                editor.commands.setContent(JSON.parse(res.cedarJson || EMPTY_DOC), { emitUpdate: false });
                this.saveState.set('saved');
                if (this.lang() === 'en') {
                    this.enMeta.set({ language: 'en', title: res.title, updatedAt: res.updatedAt });
                }
                this.refreshMeta(id);
                this.showAiToast(kind === 'fix-errors' ? 'Fixed your typos. Your voice survived. Moo.' : 'Schizo-izer done. Reality is now optional.');
            },
            error: e => {
                this.aiEditError.set(e instanceof TimeoutError
                    ? `${label} timed out after 3 minutes`
                    : httpErrorMessage(e, `${label} failed`));
                this.finishAiEdit();
            },
            complete: () => this.finishAiEdit(),
        });
    }

    private finishAiEdit() {
        this.aiEditBusy.set(false);
        clearInterval(this.aiEditTicker);
        this.aiEditSub = undefined;
    }

    // User-initiated cancel (Step 8) — see cancelAutoTranslate() above for the same reasoning.
    cancelAiEdit() {
        this.aiEditSub?.unsubscribe();
        this.finishAiEdit();
    }

    private showAiToast(text: string) {
        clearTimeout(this.aiToastTimer);
        this.aiToast.set(text);
        this.aiToastTimer = setTimeout(() => this.aiToast.set(null), 3000);
    }

    async deleteEnVersion() {
        const id = this.currentId();
        if (!id || !this.enMeta()) return;
        if (!window.confirm(this.t().editor.lang.deleteEnglishConfirm)) return;
        clearTimeout(this.saveTimer);
        this.saveState.set('saved'); // discard pending EN edits so nothing re-creates the row
        await this.draftsApi.removeTranslation(id, 'en');
        this.enMeta.set(null);
        this.enSourceSnapshot.set(null);
        this.ruDiffMarkers.set([]);
        if (this.exportLang === 'en') this.exportLang = 'ru';
        this.drafts.update(list => list.map(d => d.id === id ? { ...d, languages: [] } : d));
        if (this.lang() === 'en') await this.switchLang('ru');
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
            this.lang.set('ru');
            this.exportLang = 'ru';
            this.ruUpdatedAt.set(draft.updatedAt);
            this.enMeta.set(draft.translations?.find(t => t.language === 'en') ?? null);
            this.enSourceSnapshot.set(null);
            this.ruSnapshot = null;
            this.tagList.set(draft.tags ? draft.tags.split(',').filter(t => t.length > 0) : []);
            this.tagInput = '';
            this.currentFolderId.set(draft.folderId);
            this.isPrivate.set(draft.isPrivate);
            this.watermarkText.set(draft.watermarkText);
            this.watermarkInput = draft.watermarkText ?? '';
            this.watermarkError.set(null);
            this.invites.set([]);
            this.regForm.set(parseRegistrationForm(draft.registrationFormJson));
            this.editor?.setEditable(true);
            this.editor?.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC), { emitUpdate: false });
            this.resetHistory();
            this.saveState.set('saved');
            this.currentBlog.set(draft.blogSlug ? { slug: draft.blogSlug, isPublished: draft.isBlogPublished } : null);
            this.blogError.set(null);

            // Fetch the EN translation's source snapshot in the background (not blocking open)
            // just to populate the RU-tab diff gutter — the list endpoint only returns TranslationMeta.
            if (this.enMeta()) {
                this.draftsApi.getTranslation(id, 'en')
                    .then(tr => { this.enSourceSnapshot.set(tr.sourceSnapshotJson); this.scheduleRuDiffRecompute(); })
                    .catch(() => {});
            }
        } finally {
            this.draftsBusy.set(false);
        }
    }

    // opts is used by the New Draft dialog (openNewDraftDialog/confirmNewDraft below); the two
    // silent fallback call sites (empty draft list on load, deleting the last remaining draft)
    // call this with no args and get exactly the old blank-"Untitled" behavior.
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
                blogSlug: null, isBlogPublished: false, blogPublishedAt: null,
                languages, tags: tags.join(','),
                isArchived: false, lastTelegramMessageId: null, lastTelegramUsername: null,
                staleLanguages: [], scheduled: null, folderId, isPrivate,
                viewCount: 0, reactionCount: 0, newViewCount: 0, newReactionCount: 0,
            };
            this.drafts.update(l => [meta, ...l]);
            this.currentId.set(created.id);
            this.title = title;
            this.lang.set('ru');
            this.exportLang = 'ru';
            this.ruUpdatedAt.set(meta.updatedAt);
            this.enMeta.set(languages.includes('en') ? { language: 'en', title, updatedAt: meta.updatedAt } : null);
            this.enSourceSnapshot.set(null);
            this.ruDiffMarkers.set([]);
            this.ruSnapshot = null;
            this.tagList.set(tags);
            this.tagInput = '';
            this.currentFolderId.set(folderId);
            this.isPrivate.set(isPrivate);
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

    openNewDraftDialog() {
        let defaults: { languages?: 'ru' | 'en' | 'both'; tags?: string[]; template?: NewDraftTemplate } = {};
        try {
            defaults = JSON.parse(this.auth.newDraftDefaultsJson() ?? '{}');
        } catch { /* ignore a corrupt/foreign blob, fall back to built-in defaults */ }

        this.newDraftTitle = '';
        this.newDraftLanguages = defaults.languages ?? 'ru';
        this.newDraftTags = (defaults.tags ?? []).join(', ');
        this.newDraftTemplate = defaults.template ?? 'blank';
        this.newDraftPrivate = false;
        this.newDraftFolderId = null;
        this.newDraftExpanded.set(false);
        this.newDraftOpen.set(true);
        this.ensureFoldersLoaded();
        // N2 — the dialog picks tags from what's already in use, not just free text.
        this.ensureTagUsageLoaded();
    }

    // The dialog keeps tags as the comma string the create call wants; these two just let the
    // pill row read and write that string without a second source of truth.
    newDraftTagList(): string[] {
        return this.newDraftTags.split(',').map(t => t.trim()).filter(t => t.length > 0);
    }

    toggleNewDraftTag(tag: string) {
        const list = this.newDraftTagList();
        const next = list.includes(tag) ? list.filter(t => t !== tag) : [...list, tag];
        this.newDraftTags = next.join(', ');
    }

    closeNewDraftDialog() {
        this.newDraftOpen.set(false);
    }

    // DB2.6 — a draft with no name is unfindable in the list, and an overlong one breaks every
    // row it appears in. Bounds checked here as well as on the input's maxlength, because the
    // dialog also submits on Enter.
    newDraftTitleValid(): boolean {
        const len = this.newDraftTitle.trim().length;
        return len >= 1 && len <= DRAFT_TITLE_MAX;
    }

    async confirmNewDraft() {
        if (this.draftsBusy() || !this.newDraftTitleValid()) return;
        const title = this.newDraftTitle.trim();
        const tags = this.newDraftTags;
        const languages = this.newDraftLanguages;
        const template = this.newDraftTemplate;
        const isPrivate = this.newDraftPrivate;
        const folderId = this.newDraftFolderId;

        this.closeNewDraftDialog();
        this.auth.saveNewDraftDefaults(JSON.stringify({
            languages,
            tags: tags.split(',').map(t => t.trim().toLowerCase()).filter(t => t.length > 0),
            template,
        })).catch(() => { /* best-effort — not worth blocking draft creation over */ });

        await this.newDraft({ title, cedarJson: NEW_DRAFT_TEMPLATES[template], tags, languages, isPrivate, folderId });
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
            const res = await this.draftsApi.publishToBlog(id);
            this.currentBlog.set({ slug: res.slug, isPublished: true });
        } catch (e) {
            this.blogError.set(httpErrorMessage(e, this.t().editor.errors.publish));
        } finally {
            this.blogBusy.set(false);
            clearInterval(this.blogTicker);
        }
    }

    async unpublishFromBlog() {
        const id = this.currentId();
        if (!id) return;
        this.blogBusy.set(true);
        this.blogElapsed.set(0);
        clearInterval(this.blogTicker);
        this.blogTicker = setInterval(() => this.blogElapsed.update(s => s + 1), 1000);
        this.blogError.set(null);
        try {
            await this.draftsApi.unpublishFromBlog(id);
            this.currentBlog.update(b => b ? { ...b, isPublished: false } : b);
        } catch (e) {
            this.blogError.set(httpErrorMessage(e, this.t().editor.errors.unpublish));
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
    private async persistRegForm() {
        const id = this.currentId();
        if (!id) return;
        this.regBusy.set(true);
        try {
            const form = this.regForm();
            await this.draftsApi.setRegistrationForm(id, form ? JSON.stringify(form) : null);
        } catch (e) {
            this.inviteError.set(httpErrorMessage(e, this.t().editor.errors.saveForm));
        } finally {
            this.regBusy.set(false);
        }
    }

    // Applying a preset copies its definition onto the draft (N12, ADR-047) — the full editor
    // for a form lives in the Posts Manager; this modal only makes the pre-publish choice.
    async applyFormPreset(preset: FormPreset) {
        this.regForm.set(parseRegistrationForm(preset.formJson));
        await this.persistRegForm();
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

    canPublishAll(): boolean {
        if (this.publishingAll() || this.blogBusy() || this.exporting()) return false;
        if (!this.destBlog() && !this.destTelegram()) return false;
        // Telegram needs a target channel; the blog doesn't need anything extra.
        return !this.destTelegram() || this.chatId.trim().length > 0;
    }

    // One Publish for every ticked destination (B5). Run sequentially rather than in parallel so
    // each destination's own busy/progress state stays readable, and so a Telegram failure
    // doesn't get visually tangled with the blog's.
    async publishAll() {
        if (!this.canPublishAll()) return;
        this.publishingAll.set(true);
        try {
            if (this.destBlog()) await this.publishToBlog();
            if (this.destTelegram()) await this.exportDraft();
        } finally {
            this.publishingAll.set(false);
        }
    }

    async exportDraft() {
        const id = this.currentId();
        if (!id) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();
        this.exporting.set(true);
        this.exportElapsed.set(0);
        clearInterval(this.exportTicker);
        this.exportTicker = setInterval(() => this.exportElapsed.update(s => s + 1), 1000);
        this.exportResult.set('');
        this.exportLink.set(null);
        this.exportError.set(null);
        try {
            const res = await this.posts.export(id, this.chatId.trim(), this.format, this.exportLang, this.compressionLevel);
            this.exportResult.set(`✓ Published (message #${res.messageId})`);
            this.exportLink.set(this.buildTelegramLink(res.chatId, res.messageId));
        } catch (e) {
            const status = e instanceof HttpErrorResponse ? e.status : undefined;
            const serverMessage = httpErrorMessage(e, '');
            const message = status === 503
                ? `The barn door seems closed — Telegram Bot API didn't respond. Your draft is safe; nothing was published.${serverMessage ? ` (${serverMessage})` : ''}`
                : serverMessage || 'Error — check the browser console / server logs';
            this.exportError.set({ code: status, message });
        } finally {
            this.exporting.set(false);
            clearInterval(this.exportTicker);
        }
    }

    private buildTelegramLink(chatId: string, messageId: number): string | null {
        const trimmed = chatId.trim();
        if (trimmed.startsWith('@')) return `https://t.me/${trimmed.slice(1)}/${messageId}`;
        const username = this.channels().find(c => String(c.telegramChatId) === trimmed)?.username;
        return username ? `https://t.me/${username}/${messageId}` : null;
    }

    async connectChannel(chatId = this.newChannelChatId.trim()) {
        if (!chatId || this.channelBusy()) return;
        this.channelError.set('');
        this.channelBusy.set(true);
        try {
            const channel = await this.channelsApi.connect(chatId);
            this.channels.update(list => [...list, channel]);
            this.newChannelChatId = '';
            this.knownChats.update(list => list.filter(k => String(k.telegramChatId) !== chatId));
            await this.refreshChannelStats();
        } catch (e: any) {
            this.channelError.set(e?.error?.error ?? this.t().editor.errors.connectChannel);
        } finally {
            this.channelBusy.set(false);
        }
    }

    async refreshKnownChats() {
        this.knownChatsRefreshing.set(true);
        this.channelError.set('');
        try {
            await this.channelsApi.refreshKnown();
            this.knownChats.set(await this.channelsApi.listKnown());
        } catch {
            this.channelError.set(this.t().editor.errors.refreshChats);
        } finally {
            this.knownChatsRefreshing.set(false);
        }
    }

    selectChannel(c: Channel) {
        this.chatId = String(c.telegramChatId);
    }

    async removeChannel(id: string) {
        if (this.channelBusy()) return;
        this.channelBusy.set(true);
        try {
            await this.channelsApi.remove(id);
            this.channels.update(list => list.filter(c => c.id !== id));
            this.channelStats.update(map => {
                const { [id]: _removed, ...rest } = map;
                return rest;
            });
        } finally {
            this.channelBusy.set(false);
        }
    }

    async schedulePost() {
        const id = this.currentId();
        if (!id || !this.scheduledAt) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();
        this.scheduling.set(true);
        this.scheduleResult.set('');
        try {
            const scheduledAtUtc = new Date(this.scheduledAt).toISOString();
            await this.posts.schedule(id, this.chatId.trim(), scheduledAtUtc, this.format, this.exportLang);
            this.scheduleResult.set('✓ Scheduled');
            this.scheduledAt = '';
            await this.refreshScheduledPosts();
        } catch {
            this.scheduleResult.set('✗ Scheduling failed');
        } finally {
            this.scheduling.set(false);
        }
    }

    scheduleOpen = signal(false);

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
        this.scheduledAt = toDatetimeLocalValue(target);
    }

    selectedChannel(): Channel | undefined {
        return this.channels().find(c => String(c.telegramChatId) === this.chatId.trim());
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

    async cancelScheduled(id: string) {
        await this.posts.cancelScheduled(id);
        this.scheduledPosts.update(list => list.filter(p => p.id !== id));
    }

    private async refreshScheduledPosts() {
        this.scheduledPosts.set(await this.posts.listScheduled());
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
    }

    insertToggle() {
        this.cmd(c => c.insertContent({
            type: 'toggle',
            attrs: { summary: 'Details' },
            content: [{ type: 'paragraph' }],
        }));
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
        const snapshot = this.enSourceSnapshot();
        if (!this.editor || this.lang() !== 'ru' || !snapshot) {
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