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
import { PostsService } from '../core/posts.service';
import { Image } from '@tiptap/extension-image';
import { AssetsService } from '../core/assets.service';

type SaveState = 'saved' | 'saving' | 'dirty';
const EMPTY_DOC = '{"type":"doc","content":[{"type":"paragraph"}]}';

interface UploadItem {
    id: number;
    name: string;
    progress: number;
    error?: string;
}

@Component({
    selector: 'app-editor',
    imports: [FormsModule, DatePipe],
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
    nowTime = new Date().toLocaleTimeString('ru', { hour: '2-digit', minute: '2-digit' });

    private saveTimer?: ReturnType<typeof setTimeout>;

    private posts = inject(PostsService); // + import сверху

    previewHtml = signal('');
    chatId = '@testingandfun';
    exporting = signal(false);
    exportResult = signal('');
    exportLink = signal<string | null>(null);

    uploads = signal<UploadItem[]>([]);
    private uploadSeq = 0;

    saveLabel(): string {
        switch (this.saveState()) {
            case 'saved': return '✓ сохранено';
            case 'saving': return 'сохраняю…';
            case 'dirty': return '● есть изменения';
        }
    }

    async ngAfterViewInit() {
        this.editor = new Editor({
            element: this.editorHost.nativeElement,
            extensions: [StarterKit, Image],
            content: '',
            onTransaction: () => this.tick.update(v => v + 1),
            onUpdate: () => {
                this.markDirty();
                this.refreshPreview();
            },
        });

        const list = await this.draftsApi.list();
        this.drafts.set(list);
        if (list.length > 0) await this.openDraft(list[0].id);
        else await this.newDraft();
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
        this.refreshPreview();
    }

    async newDraft() {
        clearTimeout(this.saveTimer);
        if (this.saveState() !== 'saved') await this.save();

        const created = await this.draftsApi.create('Без названия', EMPTY_DOC);
        const meta: DraftMeta = {
            id: created.id, title: 'Без названия',
            createdAt: new Date().toISOString(), updatedAt: new Date().toISOString()
        };
        this.drafts.update(l => [meta, ...l]);
        this.currentId.set(created.id);
        this.title = meta.title;
        this.editor?.commands.setContent(JSON.parse(EMPTY_DOC));
        this.saveState.set('saved');
        this.refreshPreview();
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

    private refreshPreview() {
        this.previewHtml.set(this.editor?.getHTML() ?? '');
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
            const res = await this.posts.export(id, this.chatId.trim());
            this.exportResult.set(`✓ Опубликовано, message ${res.messageId}`);
            this.exportLink.set(this.buildTelegramLink(res.chatId, res.messageId));
        } catch {
            this.exportResult.set('✗ Ошибка — смотри консоль/логи сервера');
        } finally {
            this.exporting.set(false);
        }
    }

    private buildTelegramLink(chatId: string, messageId: number): string | null {
        return chatId.startsWith('@') ? `https://t.me/${chatId.slice(1)}/${messageId}` : null;
    }

    onFileChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const files = Array.from(input.files ?? []);
        input.value = '';
        for (const file of files) this.uploadFile(file);
    }

    private uploadFile(file: File) {
        const id = ++this.uploadSeq;
        this.uploads.update(list => [...list, { id, name: file.name, progress: 0 }]);
        this.assets.uploadWithProgress(file).subscribe({
            next: event => {
                if (event.type === HttpEventType.UploadProgress && event.total) {
                    const progress = Math.round((event.loaded / event.total) * 100);
                    this.uploads.update(list => list.map(u => u.id === id ? { ...u, progress } : u));
                } else if (event.type === HttpEventType.Response && event.body) {
                    this.editor?.chain().focus().setImage({ src: event.body.url }).run();
                    this.uploads.update(list => list.filter(u => u.id !== id));
                }
            },
            error: () => {
                this.uploads.update(list => list.map(u => u.id === id ? { ...u, error: 'Ошибка загрузки (тип/размер?)' } : u));
                setTimeout(() => this.uploads.update(list => list.filter(u => u.id !== id)), 3000);
            },
        });
    }
}