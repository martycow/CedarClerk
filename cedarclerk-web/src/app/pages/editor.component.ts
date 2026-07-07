import {
    AfterViewInit, Component, ElementRef, OnDestroy,
    ViewChild, inject, signal
} from '@angular/core';
import { HttpEventType } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { AuthService } from '../core/auth.service';
import { DraftsService, DraftMeta } from '../core/drafts.service';
import { DatePipe } from '@angular/common';
import { PostsService, PostFormat, ScheduledPost } from '../core/posts.service';
import { ChannelsService, Channel } from '../core/channels.service';
import { Image } from '@tiptap/extension-image';
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
import { PopoverComponent } from '../shared/popover.component';
import {
    LucideHeading1 as Heading1, LucideHeading2 as Heading2, LucideHeading3 as Heading3,
    LucideHeading4 as Heading4, LucideHeading5 as Heading5, LucideHeading6 as Heading6,
    LucideUndo2 as Undo2, LucideRedo2 as Redo2,
    LucideBold as Bold, LucideItalic as Italic, LucideStrikethrough as Strikethrough, LucideCode as Code,
    LucideList as List, LucideListOrdered as ListOrdered, LucideListTodo as ListTodo,
    LucideQuote as Quote, LucideSquareCode as SquareCode,
    LucideOutdent as Outdent, LucideIndent as Indent,
    LucideTable as TableIcon, LucideSigma as Sigma, LucideSigmaSquare as SigmaSquare,
    LucideImage as ImageIcon, LucideVideo as VideoIcon, LucideAudioLines as AudioLines, LucideImages as Images,
    LucideSend as Send, LucideRadioTower as RadioTower, LucidePlus as Plus, LucideX as X,
    LucideUserCircle as UserCircle, LucideLogOut as LogOut,
} from '@lucide/angular';

type SaveState = 'saved' | 'saving' | 'dirty';
const EMPTY_DOC = '{"type":"doc","content":[{"type":"paragraph"}]}';

// Extra timezones shown alongside the local time when scheduling a post; will move to user settings later
const EXTRA_TIMEZONES: { label: string; zone: string }[] = [
    { label: 'MSK', zone: 'Europe/Moscow' },
    { label: 'PT', zone: 'America/Los_Angeles' },
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
        FormsModule, DatePipe, PopoverComponent,
        Heading1, Heading2, Heading3, Heading4, Heading5, Heading6,
        Undo2, Redo2, Bold, Italic, Strikethrough, Code,
        List, ListOrdered, ListTodo, Quote, SquareCode, Outdent, Indent,
        TableIcon, Sigma, SigmaSquare, ImageIcon, VideoIcon, AudioLines, Images,
        Send, RadioTower, Plus, X, UserCircle, LogOut,
    ],
    templateUrl: 'editor.component.html',
    styleUrls: ['editor.component.css']
})
export class EditorComponent implements AfterViewInit, OnDestroy {
    auth = inject(AuthService);
    private draftsApi = inject(DraftsService);
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
    format: PostFormat = 'Html';
    exporting = signal(false);
    exportResult = signal('');
    exportLink = signal<string | null>(null);

    uploads = signal<UploadItem[]>([]);
    private uploadSeq = 0;

    channels = signal<Channel[]>([]);
    newChannelChatId = '';
    channelError = signal('');

    scheduledAt = '';
    scheduling = signal(false);
    scheduleResult = signal('');
    scheduledPosts = signal<ScheduledPost[]>([]);

    saveLabel(): string {
        switch (this.saveState()) {
            case 'saved': return '✓ Saved';
            case 'saving': return 'Saving…';
            case 'dirty': return '● Unsaved changes';
        }
    }

    async ngAfterViewInit() {
        this.editor = new Editor({
            element: this.editorHost.nativeElement,
            extensions: [
                StarterKit,
                Image,
                VideoNode,
                AudioNode,
                CarouselNode,
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
            this.saveState.set('dirty');
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
        if (id === this.currentId()) return;
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        const draft = await this.draftsApi.get(id);
        this.currentId.set(id);
        this.title = draft.title;
        this.editor?.commands.setContent(JSON.parse(draft.cedarJson || EMPTY_DOC));
        this.saveState.set('saved');
    }

    async newDraft() {
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        const created = await this.draftsApi.create('Untitled', EMPTY_DOC);
        const meta: DraftMeta = {
            id: created.id, title: 'Untitled',
            createdAt: new Date().toISOString(), updatedAt: new Date().toISOString()
        };
        this.drafts.update(l => [meta, ...l]);
        this.currentId.set(created.id);
        this.title = meta.title;
        this.editor?.commands.setContent(JSON.parse(EMPTY_DOC));
        this.saveState.set('saved');
        this.editor?.commands.focus();
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
        try {
            const res = await this.posts.export(id, this.chatId.trim(), this.format);
            this.exportResult.set(`✓ Published, message ${res.messageId}`);
            this.exportLink.set(this.buildTelegramLink(res.chatId, res.messageId));
        } catch {
            this.exportResult.set('✗ Error — check the browser console / server logs');
        } finally {
            this.exporting.set(false);
        }
    }

    setFormat(format: PostFormat) {
        this.format = format;
    }

    private buildTelegramLink(chatId: string, messageId: number): string | null {
        return chatId.startsWith('@') ? `https://t.me/${chatId.slice(1)}/${messageId}` : null;
    }

    async connectChannel() {
        const chatId = this.newChannelChatId.trim();
        if (!chatId) return;
        this.channelError.set('');
        try {
            const channel = await this.channelsApi.connect(chatId);
            this.channels.update(list => [...list, channel]);
            this.newChannelChatId = '';
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

    insertTable() {
        this.cmd(c => c.insertTable({ rows: 3, cols: 3, withHeaderRow: true }));
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