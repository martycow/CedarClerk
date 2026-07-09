import {
    AfterViewInit, Component, ElementRef, OnDestroy,
    ViewChild, inject, signal
} from '@angular/core';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { AuthService } from '../core/auth.service';
import { DraftsService, DraftMeta } from '../core/drafts.service';
import { DatePipe } from '@angular/common';
import { PostsService, PostFormat, ScheduledPost } from '../core/posts.service';
import { ChannelsService, Channel, ChannelStats } from '../core/channels.service';
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
import { CommentsService, DraftComment } from '../core/comments.service';
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
        FormsModule, DatePipe, PopoverComponent, CedarLogoComponent,
        Undo2, Redo2, Bold, Italic, Strikethrough, Code,
        List, ListOrdered, ListTodo, Quote, SquareCode, Outdent, Indent,
        TableIcon, Sigma, SigmaSquare, ImageIcon, VideoIcon, AudioLines, Images,
        Send, Plus, X, LogOut, RadioTower, Trash2,
        EyeOff, LinkIcon, Smile, Underline, Clock, ListCollapse, LayoutGrid, Menu, Superscript,
        ChevronDown, Check, Download, Upload, MessageSquare,
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
    private editor?: Editor;
    private tick = signal(0);

    drafts = signal<DraftMeta[]>([]);
    currentId = signal<string | null>(null);
    saveState = signal<SaveState>('saved');
    title = '';
    draftsOpen = signal(false);

    private saveTimer?: ReturnType<typeof setTimeout>;

    private posts = inject(PostsService); // + import сверху
    private channelsApi = inject(ChannelsService);
    private commentsApi = inject(CommentsService);

    chatId = '@testingandfun';
    format: PostFormat = 'Html';
    exporting = signal(false);
    exportResult = signal('');
    exportLink = signal<string | null>(null);
    exportError = signal<{ code?: number; message: string } | null>(null);

    importingCedar = signal(false);
    importCedarError = signal<string | null>(null);

    currentBlog = signal<{ slug: string; isPublished: boolean } | null>(null);
    blogBusy = signal(false);
    blogError = signal<string | null>(null);

    annotationsOpen = signal(false);
    draftComments = signal<DraftComment[]>([]);
    commentsLoading = signal(false);

    zoom = signal(100);

    uploads = signal<UploadItem[]>([]);
    private uploadSeq = 0;

    channels = signal<Channel[]>([]);
    channelStats = signal<Record<string, ChannelStats>>({});
    newChannelChatId = '';
    channelError = signal('');

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
        this.editor = new Editor({
            element: this.editorHost.nativeElement,
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
        this.saveState.set('saving');
        try {
            await this.draftsApi.update(id, this.title, JSON.stringify(this.editor.getJSON()));
            this.saveState.set('saved');
            this.refreshMeta(id);
        } catch {
            this.saveState.set('error');
        }
    }

    private refreshMeta(id: string) {
        this.drafts.update(list => list
            .map(d => d.id === id
                ? { ...d, title: this.title, updatedAt: new Date().toISOString() }
                : d)
            .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)));
    }

    async openDraft(id: string) {
        this.draftsOpen.set(false);
        if (id === this.currentId()) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        const draft = await this.draftsApi.get(id);
        this.currentId.set(id);
        this.title = draft.title;
        this.editor?.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC));
        this.saveState.set('saved');
        this.currentBlog.set(draft.blogSlug ? { slug: draft.blogSlug, isPublished: draft.isBlogPublished } : null);
        this.blogError.set(null);
        this.draftComments.set([]);
        if (this.annotationsOpen()) await this.loadComments();
    }

    async newDraft() {
        this.draftsOpen.set(false);
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        const created = await this.draftsApi.create('Untitled', EMPTY_DOC);
        const meta: DraftMeta = {
            id: created.id, title: 'Untitled',
            createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
            blogSlug: null, isBlogPublished: false, blogPublishedAt: null,
        };
        this.drafts.update(l => [meta, ...l]);
        this.currentId.set(created.id);
        this.title = meta.title;
        this.editor?.commands.setContent(JSON.parse(EMPTY_DOC));
        this.saveState.set('saved');
        this.currentBlog.set(null);
        this.blogError.set(null);
        this.draftComments.set([]);
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

        this.draftsOpen.set(false);
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

    async toggleAnnotationsPanel() {
        const opening = !this.annotationsOpen();
        this.annotationsOpen.set(opening);
        if (opening) await this.loadComments();
    }

    async loadComments() {
        const id = this.currentId();
        if (!id) return;
        this.commentsLoading.set(true);
        try {
            this.draftComments.set(await this.commentsApi.list(id));
        } finally {
            this.commentsLoading.set(false);
        }
    }

    async deleteComment(commentId: string) {
        await this.commentsApi.remove(commentId);
        this.draftComments.update(list => list.filter(c => c.id !== commentId));
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
            const res = await this.posts.export(id, this.chatId.trim(), this.format);
            this.exportResult.set(`✓ Published (message #${res.messageId})`);
            this.exportLink.set(this.buildTelegramLink(res.chatId, res.messageId));
        } catch (e) {
            const status = e instanceof HttpErrorResponse ? e.status : undefined;
            const message = status === 503
                ? "The barn door seems closed — Telegram Bot API didn't respond. Your draft is safe; nothing was published."
                : 'Error — check the browser console / server logs';
            this.exportError.set({ code: status, message });
        } finally {
            this.exporting.set(false);
        }
    }

    setFormat(format: PostFormat) {
        this.format = format;
    }

    private buildTelegramLink(chatId: string, messageId: number): string | null {
        const trimmed = chatId.trim();
        if (trimmed.startsWith('@')) return `https://t.me/${trimmed.slice(1)}/${messageId}`;
        const username = this.channels().find(c => String(c.telegramChatId) === trimmed)?.username;
        return username ? `https://t.me/${username}/${messageId}` : null;
    }

    async connectChannel() {
        const chatId = this.newChannelChatId.trim();
        if (!chatId) return;
        this.channelError.set('');
        try {
            const channel = await this.channelsApi.connect(chatId);
            this.channels.update(list => [...list, channel]);
            this.newChannelChatId = '';
            await this.refreshChannelStats();
        } catch (e: any) {
            this.channelError.set(e?.error?.error ?? 'Failed to connect channel');
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
            await this.posts.schedule(id, this.chatId.trim(), scheduledAtUtc, this.format);
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
            this.uploadFilePromise(file).then(url => {
                if (!url) return;
                // .gif needs a <video> tag so Telegram treats it as an animation, not a static photo
                if (file.type === 'image/gif') this.insertNode('video', { src: url });
                else this.editor?.chain().focus().setImage({ src: url }).run();
            });
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