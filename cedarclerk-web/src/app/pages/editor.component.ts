import {
    AfterViewInit, Component, ElementRef, OnDestroy,
    ViewChild, inject, signal
} from '@angular/core';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Editor } from '@tiptap/core';
import { EditorState, TextSelection } from '@tiptap/pm/state';
import { Node as PMNode, Slice } from '@tiptap/pm/model';
import StarterKit from '@tiptap/starter-kit';
import { AuthService } from '../core/auth.service';
import { DraftsService, DraftMeta, TranslationMeta, AiEditKind } from '../core/drafts.service';
import { DatePipe } from '@angular/common';
import { PostsService, PostFormat, PostLanguage, ScheduledPost } from '../core/posts.service';
import { ChannelsService, Channel, ChannelStats, KnownChat } from '../core/channels.service';
import { Table } from '@tiptap/extension-table';
import { TableRow } from '@tiptap/extension-table-row';
import { TableHeader } from '@tiptap/extension-table-header';
import { TableCell } from '@tiptap/extension-table-cell';
import { TaskList } from '@tiptap/extension-task-list';
import { TaskItem } from '@tiptap/extension-task-item';
import { Mathematics } from '@tiptap/extension-mathematics';
import { AssetsService } from '../core/assets.service';
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
import { PopoverComponent } from '../shared/popover.component';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { ThemeService } from '../core/theme.service';
import { httpErrorMessage } from '../core/http-error.util';
import {
    LucideUndo2 as Undo2, LucideRedo2 as Redo2,
    LucideBold as Bold, LucideItalic as Italic, LucideStrikethrough as Strikethrough, LucideCode as Code,
    LucideList as List, LucideListOrdered as ListOrdered, LucideListTodo as ListTodo,
    LucideQuote as Quote, LucideSquareCode as SquareCode,
    LucideOutdent as Outdent, LucideIndent as Indent,
    LucideTable as TableIcon, LucideSigma as Sigma, LucideSigmaSquare as SigmaSquare,
    LucideImage as ImageIcon, LucideVideo as VideoIcon, LucideAudioLines as AudioLines, LucideImages as Images,
    LucideSend as Send, LucidePlus as Plus, LucideX as X,
    LucideLogOut as LogOut, LucideRadioTower as RadioTower, LucideTrash2 as Trash2,
    LucideEyeOff as EyeOff, LucideLink as LinkIcon, LucideSmile as Smile, LucideUnderline as Underline,
    LucideClock as Clock, LucideListCollapse as ListCollapse, LucideLayoutGrid as LayoutGrid,
    LucideMenu as Menu, LucideSuperscript as Superscript,
    LucideChevronDown as ChevronDown,
    LucideCheck as Check,
    LucideDownload as Download, LucideUpload as Upload,
    LucideMessageSquare as MessageSquare,
    LucideRefreshCw as RefreshCw,
    LucideSettings as Settings, LucideSparkle as Sparkle,
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
const EMPTY_DOC = '{"type":"doc","content":[{"type":"paragraph"}]}';
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
        FormsModule, DatePipe, RouterLink, PopoverComponent, CedarLogoComponent,
        Undo2, Redo2, Bold, Italic, Strikethrough, Code,
        List, ListOrdered, ListTodo, Quote, SquareCode, Outdent, Indent,
        TableIcon, Sigma, SigmaSquare, ImageIcon, VideoIcon, AudioLines, Images,
        Send, Plus, X, LogOut, RadioTower, Trash2,
        EyeOff, LinkIcon, Smile, Underline, Clock, ListCollapse, LayoutGrid, Menu, Superscript,
        ChevronDown, Check, Download, Upload, MessageSquare, RefreshCw,
        Settings, Sparkle,
    ],
    templateUrl: 'editor.component.html',
    styleUrls: ['editor.component.css']
})
export class EditorComponent implements AfterViewInit, OnDestroy {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    private draftsApi = inject(DraftsService);
    private assets = inject(AssetsService);

    @ViewChild('editorHost') editorHost!: ElementRef<HTMLElement>;
    @ViewChild('draftsPopover') draftsPopover!: PopoverComponent;
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

    // Active content language in the editor. 'ru' edits the draft itself (primary version),
    // 'en' edits the DraftTranslation row. Only one editor instance — switching tabs flushes
    // the autosave for the language being left, then loads the other version's content.
    lang = signal<PostLanguage>('ru');
    enMeta = signal<TranslationMeta | null>(null);
    private ruUpdatedAt = signal<string>('');
    // RU title+content captured when entering the EN tab — source for "Copy from Russian"
    private ruSnapshot: { title: string; json: string } | null = null;

    // Tags are per-draft (shared across language versions) and saved through their own endpoint
    // immediately on add/remove — not through the content autosave, which routes per-language.
    tagList = signal<string[]>([]);
    tagInput = '';

    aiEditBusy = signal(false);
    aiEditElapsed = signal(0);
    private aiEditTicker?: ReturnType<typeof setInterval>;
    aiEditError = signal<string | null>(null);
    aiConfirmKind = signal<AiEditKind | null>(null);
    aiToast = signal<string | null>(null);
    private aiToastTimer?: ReturnType<typeof setTimeout>;

    autoTranslating = signal(false);
    autoTranslateElapsed = signal(0);
    private autoTranslateTicker?: ReturnType<typeof setInterval>;
    autoTranslateError = signal<string | null>(null);
    exporting = signal(false);
    exportResult = signal('');
    exportLink = signal<string | null>(null);
    exportError = signal<{ code?: number; message: string } | null>(null);

    importingCedar = signal(false);
    importCedarError = signal<string | null>(null);

    currentBlog = signal<{ slug: string; isPublished: boolean } | null>(null);
    blogBusy = signal(false);
    blogError = signal<string | null>(null);

    zoom = signal(100);

    uploads = signal<UploadItem[]>([]);
    private uploadSeq = 0;

    channels = signal<Channel[]>([]);
    channelStats = signal<Record<string, ChannelStats>>({});
    newChannelChatId = '';
    channelError = signal('');

    knownChats = signal<KnownChat[]>([]);
    knownChatsRefreshing = signal(false);

    scheduledAt = '';
    scheduling = signal(false);
    scheduleResult = signal('');
    scheduledPosts = signal<ScheduledPost[]>([]);

    linkType: 'url' | 'email' | 'phone' | 'mention' = 'url';
    linkValue = '';

    readonly commonEmoji = COMMON_EMOJI;

    dtValue = '';
    dtWeekday = true;
    dtDate = true;
    dtTime = true;

    footnoteText = '';

    saveLabel(): string {
        switch (this.saveState()) {
            case 'saved': return 'Saved';
            case 'saving': return 'Saving…';
            case 'dirty': return 'Unsaved changes';
            case 'error': return 'Sync failed';
        }
    }

    zoomFactor(): number {
        return this.zoom() / 100;
    }

    zoomIn() {
        this.zoom.update(z => Math.min(200, z + 10));
    }

    zoomOut() {
        this.zoom.update(z => Math.max(50, z - 10));
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

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
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

    currentBlockLabel(): string {
        this.tick();
        for (let level = 1; level <= 6; level++) {
            if (this.editor?.isActive('heading', { level })) return `Heading ${level}`;
        }
        return 'Paragraph';
    }

    setBlockType(level: number) {
        if (level === 0) this.cmd(c => c.setParagraph());
        else this.cmd(c => c.toggleHeading({ level: level as 1 | 2 | 3 | 4 | 5 | 6 }));
    }

    retrySave() {
        this.save();
    }

    async ngAfterViewInit() {
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
                Table.configure({ resizable: false }),
                TableRow,
                TableHeader,
                TableCell,
                TaskList,
                TaskItem.configure({ nested: true }),
                Mathematics,
            ],
            content: '',
            onTransaction: () => this.tick.update(v => v + 1),
            onUpdate: () => this.markDirty(),
        });

        const list = await this.draftsApi.list();
        this.drafts.set(list);
        if (list.length > 0) await this.openDraft(list[0].id);
        else await this.newDraft();

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
                await this.draftsApi.update(id, this.title, json);
                this.ruUpdatedAt.set(new Date().toISOString());
                this.refreshMeta(id);
            } else {
                const res = await this.draftsApi.saveTranslation(id, 'en', this.title, json);
                this.enMeta.set({ language: 'en', title: this.title, updatedAt: res.updatedAt });
            }
            this.saveState.set('saved');
        } catch {
            this.saveState.set('error');
        }
    }

    showEnEmptyState(): boolean {
        return this.lang() === 'en' && !this.enMeta();
    }

    translateStatusMessage(): string {
        const i = Math.floor(this.autoTranslateElapsed() / 4) % TRANSLATE_STATUS_MESSAGES.length;
        return TRANSLATE_STATUS_MESSAGES[i];
    }

    // The RU version was edited after the EN translation was last touched — probably needs re-translating
    enStale(): boolean {
        const en = this.enMeta();
        return !!en && this.ruUpdatedAt() > en.updatedAt;
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
            this.editor.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC));
            this.resetHistory();
        } else {
            this.ruSnapshot = { title: this.title, json: JSON.stringify(this.editor.getJSON()) };
            if (this.enMeta()) {
                const tr = await this.draftsApi.getTranslation(id, 'en');
                this.lang.set('en');
                this.title = tr.title;
                this.enMeta.set({ language: 'en', title: tr.title, updatedAt: tr.updatedAt });
                this.editor.setEditable(true);
                this.editor.commands.setContent(JSON.parse(tr.cedarJson || EMPTY_DOC));
                this.resetHistory();
            } else {
                // No EN version yet — show the empty state (Copy from Russian / Start empty)
                this.lang.set('en');
                this.title = this.ruSnapshot.title;
                this.editor.setEditable(false);
                this.editor.commands.setContent(JSON.parse(EMPTY_DOC));
                this.resetHistory();
            }
        }
        this.saveState.set('saved');
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
            this.title = title;
            this.editor.setEditable(true);
            this.editor.commands.setContent(JSON.parse(json));
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

    // Machine-translates the RU version into EN and loads the result into the editor for review.
    async autoTranslateEn() {
        const id = this.currentId();
        if (!id || !this.editor) return;
        if (this.enMeta() && !window.confirm('Replace the current English version with a fresh machine translation?')) return;

        this.autoTranslating.set(true);
        this.autoTranslateElapsed.set(0);
        clearInterval(this.autoTranslateTicker);
        this.autoTranslateTicker = setInterval(() => this.autoTranslateElapsed.update(s => s + 1), 1000);
        this.autoTranslateError.set(null);
        try {
            const tr = await this.draftsApi.autoTranslate(id, 'en');
            this.enMeta.set({ language: 'en', title: tr.title, updatedAt: tr.updatedAt });
            this.drafts.update(list => list.map(d => d.id === id ? { ...d, languages: ['en'] } : d));
            if (this.lang() !== 'en') {
                this.ruSnapshot = { title: this.title, json: JSON.stringify(this.editor.getJSON()) };
                this.lang.set('en');
            }
            this.title = tr.title;
            this.editor.setEditable(true);
            this.editor.commands.setContent(JSON.parse(tr.cedarJson || EMPTY_DOC));
            this.saveState.set('saved');
        } catch (e) {
            this.autoTranslateError.set(httpErrorMessage(e, 'Auto-translate failed — check server logs'));
        } finally {
            this.autoTranslating.set(false);
            clearInterval(this.autoTranslateTicker);
        }
    }

    // Opens the AI confirm dialog (replaces window.confirm — the only native browser dialog
    // in the app otherwise); confirmAiEdit() below actually runs aiEdit() once accepted.
    askAiEdit(kind: AiEditKind) {
        this.aiConfirmKind.set(kind);
    }

    cancelAiConfirm() {
        this.aiConfirmKind.set(null);
    }

    async confirmAiEdit() {
        const kind = this.aiConfirmKind();
        this.aiConfirmKind.set(null);
        if (kind) await this.aiEdit(kind);
    }

    aiConfirmTitle(): string {
        return this.aiConfirmKind() === 'fix-errors' ? 'Fix errors with AI?' : 'Run the Schizo-izer?';
    }

    aiConfirmBody(): string {
        const words = this.wordCount();
        const lang = this.lang().toUpperCase();
        return this.aiConfirmKind() === 'fix-errors'
            ? `Claude will proofread the current ${lang} version (${words} words). The original stays in history.`
            : `Claude will rewrite the current ${lang} version (${words} words) into unhinged schizoposting. The original stays in history.`;
    }

    // Rewrites the current language version in place via an LLM (Pro Plus, daily quota) — grammar
    // fix or "schizoposting" style rewrite. Same persist-then-load pattern as auto-translate, so
    // Ctrl+Z in the editor can still undo the content swap if the user doesn't like the result.
    private async aiEdit(kind: AiEditKind) {
        const id = this.currentId();
        if (!id || !this.editor) return;
        const label = kind === 'fix-errors' ? 'Fix errors' : 'Schizo-izer';

        this.aiEditBusy.set(true);
        this.aiEditElapsed.set(0);
        clearInterval(this.aiEditTicker);
        this.aiEditTicker = setInterval(() => this.aiEditElapsed.update(s => s + 1), 1000);
        this.aiEditError.set(null);
        try {
            const res = await this.draftsApi.aiEdit(id, this.lang(), kind);
            this.title = res.title;
            this.editor.commands.setContent(JSON.parse(res.cedarJson || EMPTY_DOC));
            this.saveState.set('saved');
            if (this.lang() === 'en') {
                this.enMeta.set({ language: 'en', title: res.title, updatedAt: res.updatedAt });
            }
            this.refreshMeta(id);
            this.showAiToast(kind === 'fix-errors' ? 'Fixed your typos. Your voice survived. Moo.' : 'Schizo-izer done. Reality is now optional.');
        } catch (e) {
            this.aiEditError.set(httpErrorMessage(e, `${label} failed`));
        } finally {
            this.aiEditBusy.set(false);
            clearInterval(this.aiEditTicker);
        }
    }

    private showAiToast(text: string) {
        clearTimeout(this.aiToastTimer);
        this.aiToast.set(text);
        this.aiToastTimer = setTimeout(() => this.aiToast.set(null), 3000);
    }

    async deleteEnVersion() {
        const id = this.currentId();
        if (!id || !this.enMeta()) return;
        if (!window.confirm('Delete the English version? This cannot be undone.')) return;
        clearTimeout(this.saveTimer);
        this.saveState.set('saved'); // discard pending EN edits so nothing re-creates the row
        await this.draftsApi.removeTranslation(id, 'en');
        this.enMeta.set(null);
        if (this.exportLang === 'en') this.exportLang = 'ru';
        this.drafts.update(list => list.map(d => d.id === id ? { ...d, languages: [] } : d));
        if (this.lang() === 'en') await this.switchLang('ru');
    }

    private refreshMeta(id: string) {
        this.drafts.update(list => list
            .map(d => d.id === id
                ? { ...d, title: this.title, updatedAt: new Date().toISOString() }
                : d)
            .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)));
    }

    async openDraft(id: string) {
        this.draftsPopover?.close();
        if (id === this.currentId()) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        const draft = await this.draftsApi.get(id);
        this.currentId.set(id);
        this.title = draft.title;
        this.lang.set('ru');
        this.exportLang = 'ru';
        this.ruUpdatedAt.set(draft.updatedAt);
        this.enMeta.set(draft.translations?.find(t => t.language === 'en') ?? null);
        this.ruSnapshot = null;
        this.tagList.set(draft.tags ? draft.tags.split(',').filter(t => t.length > 0) : []);
        this.tagInput = '';
        this.editor?.setEditable(true);
        this.editor?.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC));
        this.resetHistory();
        this.saveState.set('saved');
        this.currentBlog.set(draft.blogSlug ? { slug: draft.blogSlug, isPublished: draft.isBlogPublished } : null);
        this.blogError.set(null);
    }

    async newDraft() {
        this.draftsPopover?.close();
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        const created = await this.draftsApi.create('Untitled', EMPTY_DOC);
        const meta: DraftMeta = {
            id: created.id, title: 'Untitled',
            createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
            blogSlug: null, isBlogPublished: false, blogPublishedAt: null,
            languages: [], tags: '',
        };
        this.drafts.update(l => [meta, ...l]);
        this.currentId.set(created.id);
        this.title = meta.title;
        this.lang.set('ru');
        this.exportLang = 'ru';
        this.ruUpdatedAt.set(meta.updatedAt);
        this.enMeta.set(null);
        this.ruSnapshot = null;
        this.tagList.set([]);
        this.tagInput = '';
        this.editor?.setEditable(true);
        this.editor?.commands.setContent(JSON.parse(EMPTY_DOC));
        this.resetHistory();
        this.saveState.set('saved');
        this.currentBlog.set(null);
        this.blogError.set(null);
        this.editor?.commands.focus();
    }

    async deleteDraft(id: string) {
        if (!window.confirm('Delete this draft? This cannot be undone.')) return;
        await this.draftsApi.remove(id);
        this.drafts.update(list => list.filter(d => d.id !== id));
        if (this.currentId() === id) {
            const remaining = this.drafts();
            clearTimeout(this.saveTimer);
            this.currentId.set(null);
            if (remaining.length) await this.openDraft(remaining[0].id);
            else await this.newDraft();
        }
    }

    async onImportCedarChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const file = input.files?.[0];
        input.value = '';
        if (!file) return;

        this.draftsPopover?.close();
        this.importingCedar.set(true);
        this.importCedarError.set(null);
        try {
            const created = await this.draftsApi.importCedar(file);
            this.drafts.set(await this.draftsApi.list());
            this.currentId.set(null);
            await this.openDraft(created.id);
        } catch (e) {
            this.importCedarError.set(e instanceof HttpErrorResponse && e.error?.error
                ? e.error.error
                : 'Import failed — check the file and try again');
        } finally {
            this.importingCedar.set(false);
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
        this.blogError.set(null);
        try {
            const res = await this.draftsApi.publishToBlog(id);
            this.currentBlog.set({ slug: res.slug, isPublished: true });
        } catch {
            this.blogError.set('Publish failed — check server logs');
        } finally {
            this.blogBusy.set(false);
        }
    }

    async unpublishFromBlog() {
        const id = this.currentId();
        if (!id) return;
        this.blogBusy.set(true);
        this.blogError.set(null);
        try {
            await this.draftsApi.unpublishFromBlog(id);
            this.currentBlog.update(b => b ? { ...b, isPublished: false } : b);
        } catch {
            this.blogError.set('Unpublish failed — check server logs');
        } finally {
            this.blogBusy.set(false);
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

    async exportDraft() {
        const id = this.currentId();
        if (!id) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();
        this.exporting.set(true);
        this.exportResult.set('');
        this.exportLink.set(null);
        this.exportError.set(null);
        try {
            const res = await this.posts.export(id, this.chatId.trim(), this.format, this.exportLang);
            this.exportResult.set(`✓ Published (message #${res.messageId})`);
            this.exportLink.set(this.buildTelegramLink(res.chatId, res.messageId));
        } catch (e) {
            const status = e instanceof HttpErrorResponse ? e.status : undefined;
            const message = status === 503
                ? "The barn door seems closed — Telegram Bot API didn't respond. Your draft is safe; nothing was published."
                : httpErrorMessage(e, 'Error — check the browser console / server logs');
            this.exportError.set({ code: status, message });
        } finally {
            this.exporting.set(false);
        }
    }

    private buildTelegramLink(chatId: string, messageId: number): string | null {
        const trimmed = chatId.trim();
        if (trimmed.startsWith('@')) return `https://t.me/${trimmed.slice(1)}/${messageId}`;
        const username = this.channels().find(c => String(c.telegramChatId) === trimmed)?.username;
        return username ? `https://t.me/${username}/${messageId}` : null;
    }

    async connectChannel(chatId = this.newChannelChatId.trim()) {
        if (!chatId) return;
        this.channelError.set('');
        try {
            const channel = await this.channelsApi.connect(chatId);
            this.channels.update(list => [...list, channel]);
            this.newChannelChatId = '';
            this.knownChats.update(list => list.filter(k => String(k.telegramChatId) !== chatId));
            await this.refreshChannelStats();
        } catch (e: any) {
            this.channelError.set(e?.error?.error ?? 'Failed to connect channel');
        }
    }

    async refreshKnownChats() {
        this.knownChatsRefreshing.set(true);
        this.channelError.set('');
        try {
            await this.channelsApi.refreshKnown();
            this.knownChats.set(await this.channelsApi.listKnown());
        } catch {
            this.channelError.set('Failed to refresh known chats');
        } finally {
            this.knownChatsRefreshing.set(false);
        }
    }

    selectChannel(c: Channel) {
        this.chatId = String(c.telegramChatId);
    }

    async removeChannel(id: string) {
        await this.channelsApi.remove(id);
        this.channels.update(list => list.filter(c => c.id !== id));
        this.channelStats.update(map => {
            const { [id]: _removed, ...rest } = map;
            return rest;
        });
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

    setLinkType(type: 'url' | 'email' | 'phone' | 'mention') {
        this.linkType = type;
    }

    applyLink() {
        const value = this.linkValue.trim();
        if (!value) return;
        const href = this.linkType === 'email' ? `mailto:${value}`
            : this.linkType === 'phone' ? `tel:${value}`
            : this.linkType === 'mention' ? `tg://user?id=${value}`
            : value;

        if (this.editor?.state.selection.empty) {
            this.cmd(c => c.insertContent({ type: 'text', text: value, marks: [{ type: 'link', attrs: { href } }] }));
        } else {
            this.cmd(c => c.setLink({ href }));
        }
        this.linkValue = '';
    }

    removeLink() {
        this.cmd(c => c.unsetLink());
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

    insertFootnote() {
        const text = this.footnoteText.trim();
        if (!text) return;
        this.cmd(c => c.insertContent({ type: 'footnote', attrs: { text } }));
        this.footnoteText = '';
    }

    insertTable() {
        this.cmd(c => c.insertTable({ rows: 3, cols: 3, withHeaderRow: true }));
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
}