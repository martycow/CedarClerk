import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { DraftsService, DraftMeta, FolderMeta } from '../core/drafts.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { ModalComponent } from '../shared/modal.component';
import { PopoverComponent } from '../shared/popover.component';
import { httpErrorMessage } from '../core/http-error.util';
import {
    LucidePlus as Plus,
    LucideArchive as Archive, LucideArchiveRestore as ArchiveRestore, LucideTrash2 as Trash2,
    LucideRefreshCw as RefreshCw, LucideLayoutGrid as LayoutGrid, LucideList as List,
    LucideFolder as Folder, LucideX as X, LucidePencil as Pencil,
    LucideLock as Lock, LucideFileUp as FileUp, LucideUpload as Upload,
} from '@lucide/angular';

type FilterKey = 'all' | 'draft' | 'scheduled' | 'published' | 'attention' | 'archived';
export type DraftStatusTone = 'default' | 'danger' | 'ok';
export interface DraftStatus { label: string; tone: DraftStatusTone; detail: string; }

// Matches ADR-035's scoping: only status badges honestly derivable from persisted state.
// "Unsaved"/"Publishing" from the original mockup are session-local (meaningful only for
// whichever draft is open in *this* tab right now) and are deliberately not shown here.
function computeStatus(d: DraftMeta): DraftStatus {
    if (d.isArchived) return { label: 'Archived', tone: 'default', detail: '' };
    if (d.scheduled?.status === 'Failed') {
        return { label: 'Publish failed', tone: 'danger', detail: d.scheduled.error ?? '' };
    }
    if (d.scheduled?.status === 'Pending') {
        const when = new Date(d.scheduled.scheduledAtUtc).toLocaleString();
        return { label: 'Scheduled', tone: 'default', detail: `${when} · ${d.scheduled.chatId}` };
    }
    if (d.staleLanguages.length > 0) {
        return { label: 'Translation incomplete', tone: 'default', detail: `${d.staleLanguages.map(l => l.toUpperCase()).join(', ')} behind` };
    }
    if (d.isBlogPublished || d.lastTelegramMessageId) {
        const where = [d.isBlogPublished ? 'Blog' : null, d.lastTelegramMessageId ? 'Telegram' : null].filter(Boolean).join(' · ');
        return { label: 'Published', tone: 'ok', detail: where };
    }
    return { label: 'Draft', tone: 'default', detail: '' };
}

function matchesFilter(d: DraftMeta, key: FilterKey): boolean {
    switch (key) {
        case 'draft': return !d.isArchived && !d.scheduled && !d.isBlogPublished && !d.lastTelegramMessageId;
        case 'scheduled': return !d.isArchived && d.scheduled?.status === 'Pending';
        case 'published': return !d.isArchived && (d.isBlogPublished || !!d.lastTelegramMessageId) && d.scheduled?.status !== 'Failed';
        case 'attention': return !d.isArchived && (d.scheduled?.status === 'Failed' || d.staleLanguages.length > 0);
        case 'archived': return d.isArchived;
        default: return !d.isArchived;
    }
}

@Component({
    selector: 'app-drafts',
    imports: [
        DatePipe, FormsModule, CedarLogoComponent, ModalComponent, PopoverComponent,
        Plus, Archive, ArchiveRestore, Trash2, RefreshCw, LayoutGrid, List,
        Folder, X, Pencil, Lock, FileUp, Upload,
    ],
    templateUrl: 'drafts.component.html',
    styleUrls: ['drafts.component.css'],
})
export class DraftsPageComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    private draftsApi = inject(DraftsService);
    private router = inject(Router);

    loading = signal(true);
    drafts = signal<DraftMeta[]>([]);
    search = '';
    filter = signal<FilterKey>('all');
    view = signal<'table' | 'grid'>('table');
    busyId = signal<string | null>(null);
    deleteConfirmId = signal<string | null>(null);
    error = signal('');

    // Folders (Phase "Cedar Clerk 0.9.0" idea #19, see the ADR following ADR-038,
    // docs/DECISIONS.md) — 'all' = no folder filter, 'none' = unfiled drafts only, else a folder id.
    folders = signal<FolderMeta[]>([]);
    selectedFolder = signal<'all' | 'none' | string>('all');
    newFolderName = '';
    folderBusy = signal(false);
    folderError = signal('');
    editingFolderId = signal<string | null>(null);
    editingFolderName = '';
    deleteFolderConfirmId = signal<string | null>(null);

    // Both imports live here now (B22) — the editor topbar is Export/theme/profile only.
    importingCedar = signal(false);
    importCedarError = signal<string | null>(null);
    importingMarkdown = signal(false);
    importMarkdownError = signal<string | null>(null);
    importMarkdownWarning = signal<string | null>(null);

    async ngOnInit() {
        try {
            const [drafts, folders] = await Promise.all([this.draftsApi.list(), this.draftsApi.listFolders()]);
            this.drafts.set(drafts);
            this.folders.set(folders);
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to load drafts'));
        } finally {
            this.loading.set(false);
        }
    }

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }

    status(d: DraftMeta): DraftStatus {
        return computeStatus(d);
    }

    filterCount(key: FilterKey): number {
        return this.drafts().filter(d => matchesFilter(d, key)).length;
    }

    filteredDrafts(): DraftMeta[] {
        const q = this.search.trim().toLowerCase();
        const folder = this.selectedFolder();
        return this.drafts()
            .filter(d => matchesFilter(d, this.filter()))
            .filter(d => folder === 'all' || (folder === 'none' ? d.folderId === null : d.folderId === folder))
            .filter(d => !q || d.title.toLowerCase().includes(q) || d.tags.toLowerCase().includes(q))
            .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
    }

    folderName(id: string | null): string {
        if (id === null) return 'No folder';
        return this.folders().find(f => f.id === id)?.name ?? 'No folder';
    }

    selectedFolderLabel(): string {
        const f = this.selectedFolder();
        if (f === 'all') return 'All folders';
        if (f === 'none') return 'No folder';
        return this.folderName(f);
    }

    async assignFolder(d: DraftMeta, folderId: string | null) {
        if (d.folderId === folderId) return;
        try {
            await this.draftsApi.setDraftFolder(d.id, folderId);
            this.drafts.update(list => list.map(x => x.id === d.id ? { ...x, folderId } : x));
            this.folders.set(await this.draftsApi.listFolders());
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to move draft'));
        }
    }

    async addFolder() {
        const name = this.newFolderName.trim();
        if (!name || this.folderBusy()) return;
        this.folderBusy.set(true);
        this.folderError.set('');
        try {
            const created = await this.draftsApi.createFolder(name);
            this.folders.update(list => [...list, { ...created, count: 0 }].sort((a, b) => a.name.localeCompare(b.name)));
            this.newFolderName = '';
        } catch (e) {
            this.folderError.set(httpErrorMessage(e, 'Failed to create folder'));
        } finally {
            this.folderBusy.set(false);
        }
    }

    startRenameFolder(f: FolderMeta, ev: Event) {
        ev.stopPropagation();
        this.editingFolderId.set(f.id);
        this.editingFolderName = f.name;
    }

    cancelRenameFolder() {
        this.editingFolderId.set(null);
    }

    async commitRenameFolder() {
        const id = this.editingFolderId();
        const name = this.editingFolderName.trim();
        if (!id || !name || this.folderBusy()) { this.editingFolderId.set(null); return; }
        this.folderBusy.set(true);
        this.folderError.set('');
        try {
            const renamed = await this.draftsApi.renameFolder(id, name);
            this.folders.update(list => list.map(f => f.id === id ? { ...f, name: renamed.name } : f).sort((a, b) => a.name.localeCompare(b.name)));
            this.editingFolderId.set(null);
        } catch (e) {
            this.folderError.set(httpErrorMessage(e, 'Failed to rename folder'));
        } finally {
            this.folderBusy.set(false);
        }
    }

    askDeleteFolder(f: FolderMeta, ev: Event) {
        ev.stopPropagation();
        this.deleteFolderConfirmId.set(f.id);
    }

    cancelDeleteFolder() {
        this.deleteFolderConfirmId.set(null);
    }

    deleteFolderConfirmTarget(): FolderMeta | null {
        const id = this.deleteFolderConfirmId();
        return id ? this.folders().find(f => f.id === id) ?? null : null;
    }

    async confirmDeleteFolder() {
        const id = this.deleteFolderConfirmId();
        if (!id || this.folderBusy()) return;
        this.folderBusy.set(true);
        this.deleteFolderConfirmId.set(null);
        try {
            await this.draftsApi.deleteFolder(id);
            this.folders.update(list => list.filter(f => f.id !== id));
            this.drafts.update(list => list.map(d => d.folderId === id ? { ...d, folderId: null } : d));
            if (this.selectedFolder() === id) this.selectedFolder.set('all');
        } catch (e) {
            this.folderError.set(httpErrorMessage(e, 'Failed to delete folder'));
        } finally {
            this.folderBusy.set(false);
        }
    }

    openDraft(id: string) {
        this.router.navigate(['/editor'], { queryParams: { draft: id } });
    }

    newDraft() {
        this.router.navigate(['/editor'], { queryParams: { new: 1 } });
    }

    async onImportCedarChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const file = input.files?.[0];
        input.value = '';
        if (!file || this.importingCedar()) return;

        this.importingCedar.set(true);
        this.importCedarError.set(null);
        try {
            const created = await this.draftsApi.importCedar(file);
            this.router.navigate(['/editor'], { queryParams: { draft: created.id } });
        } catch (e) {
            this.importCedarError.set(httpErrorMessage(e, 'Import failed — check the file and try again'));
        } finally {
            this.importingCedar.set(false);
        }
    }

    async onImportMarkdownChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const file = input.files?.[0];
        input.value = '';
        if (!file || this.importingMarkdown()) return;

        this.importingMarkdown.set(true);
        this.importMarkdownError.set(null);
        this.importMarkdownWarning.set(null);
        try {
            const created = await this.draftsApi.importMarkdown(file);
            if (created.unmatchedImages.length > 0) {
                this.importMarkdownWarning.set(`Imported, but ${created.unmatchedImages.length} image(s) could not be matched: ${created.unmatchedImages.join(', ')}`);
                this.drafts.set(await this.draftsApi.list());
            } else {
                // Nothing to report — go straight to the freshly imported draft.
                this.router.navigate(['/editor'], { queryParams: { draft: created.id } });
            }
        } catch (e) {
            this.importMarkdownError.set(httpErrorMessage(e, 'Import failed — check the file and try again'));
        } finally {
            this.importingMarkdown.set(false);
        }
    }

    async toggleArchive(d: DraftMeta, ev: Event) {
        ev.stopPropagation();
        if (this.busyId()) return;
        this.busyId.set(d.id);
        this.error.set('');
        try {
            const res = d.isArchived ? await this.draftsApi.unarchive(d.id) : await this.draftsApi.archive(d.id);
            this.drafts.update(list => list.map(x => x.id === d.id ? { ...x, isArchived: res.isArchived } : x));
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to update draft'));
        } finally {
            this.busyId.set(null);
        }
    }

    askDelete(d: DraftMeta, ev: Event) {
        ev.stopPropagation();
        this.deleteConfirmId.set(d.id);
    }

    cancelDelete() {
        this.deleteConfirmId.set(null);
    }

    async confirmDelete() {
        const id = this.deleteConfirmId();
        if (!id || this.busyId()) return;
        this.busyId.set(id);
        this.deleteConfirmId.set(null);
        this.error.set('');
        try {
            await this.draftsApi.remove(id);
            this.drafts.update(list => list.filter(d => d.id !== id));
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to delete draft'));
        } finally {
            this.busyId.set(null);
        }
    }
}
