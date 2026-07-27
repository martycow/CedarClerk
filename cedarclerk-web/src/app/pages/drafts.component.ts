import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { DraftsService, DraftMeta } from '../core/drafts.service';
import { FoldersService } from '../core/folders.service';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { LocaleService } from '../core/i18n/locale.service';
import { Dict } from '../core/i18n/en';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { ModalComponent } from '../shared/modal.component';
import { PopoverComponent } from '../shared/popover.component';
import { AccountMenuComponent } from '../shared/account-menu.component';
import { httpErrorMessage } from '../core/http-error.util';
import {
    LucidePlus as Plus,
    LucideArchive as Archive, LucideArchiveRestore as ArchiveRestore, LucideTrash2 as Trash2,
    LucideRefreshCw as RefreshCw, LucideLayoutGrid as LayoutGrid, LucideList as List,
    LucideFolder as Folder,
    LucideLock as Lock, LucideFileUp as FileUp, LucideUpload as Upload,
    LucideEye as Eye, LucideHeart as Heart,
} from '@lucide/angular';

type FilterKey = 'all' | 'draft' | 'scheduled' | 'published' | 'attention' | 'archived';
export type SortKey = 'title' | 'state' | 'languages' | 'folder' | 'tags' | 'activity' | 'updated' | 'created';

// Widths of the six fixed columns between Title (1fr) and the actions column (N1). Title keeps
// the leftover space, so it isn't in here — dragging any handle grows/shrinks Title, which is
// what makes the table feel like it resizes rather than scrolls.
// DB2.3 — Title is the 1fr column, so it soaked up all the slack and started far wider than a
// post title ever needs. Widening the fixed columns is the safe way to give it less without
// restructuring the grid (and each is still individually resizable).
const DEFAULT_COL_WIDTHS = [200, 120, 170, 190, 140, 140];
const MIN_COL_WIDTH = 60;
const COL_STORAGE_KEY = 'cedar-drafts-cols';

function loadColWidths(): number[] {
    try {
        const raw = JSON.parse(localStorage.getItem(COL_STORAGE_KEY) ?? '');
        if (Array.isArray(raw) && raw.length === DEFAULT_COL_WIDTHS.length && raw.every(n => typeof n === 'number' && n >= MIN_COL_WIDTH)) {
            return raw;
        }
    } catch { /* a corrupt/foreign blob just falls back to the defaults */ }
    return [...DEFAULT_COL_WIDTHS];
}
export type DraftStatusTone = 'default' | 'danger' | 'ok';
export interface DraftStatus { label: string; tone: DraftStatusTone; detail: string; }

// Matches ADR-035's scoping: only status badges honestly derivable from persisted state.
// "Unsaved"/"Publishing" from the original mockup are session-local (meaningful only for
// whichever draft is open in *this* tab right now) and are deliberately not shown here.
function computeStatus(d: DraftMeta, t: Dict): DraftStatus {
    const s = t.drafts.status;
    if (d.isArchived) return { label: s.archived, tone: 'default', detail: '' };
    if (d.scheduled?.status === 'Failed') {
        return { label: s.publishFailed, tone: 'danger', detail: d.scheduled.error ?? '' };
    }
    if (d.scheduled?.status === 'Pending') {
        const when = new Date(d.scheduled.scheduledAtUtc).toLocaleString();
        return { label: s.scheduled, tone: 'default', detail: `${when} · ${d.scheduled.chatId}` };
    }
    if (d.staleLanguages.length > 0) {
        return { label: s.translationIncomplete, tone: 'default', detail: s.translationBehind(d.staleLanguages.map(l => l.toUpperCase()).join(', ')) };
    }
    if (d.isBlogPublished || d.lastTelegramMessageId) {
        const where = [d.isBlogPublished ? s.blog : null, d.lastTelegramMessageId ? s.telegram : null].filter(Boolean).join(' · ');
        return { label: s.published, tone: 'ok', detail: where };
    }
    return { label: s.draft, tone: 'default', detail: '' };
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
        DatePipe, FormsModule, CedarLogoComponent, ModalComponent, PopoverComponent, AccountMenuComponent,
        FolderPickerComponent,
        Plus, Archive, ArchiveRestore, Trash2, RefreshCw, LayoutGrid, List,
        Folder, Lock, FileUp, Upload, Eye, Heart,
    ],
    templateUrl: 'drafts.component.html',
    styleUrls: ['drafts.component.css'],
})
export class DraftsPageComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    t = inject(LocaleService).t;
    private draftsApi = inject(DraftsService);
    private foldersApi = inject(FoldersService);
    private router = inject(Router);

    loading = signal(true);
    drafts = signal<DraftMeta[]>([]);
    search = '';
    filter = signal<FilterKey>('all');
    view = signal<'table' | 'grid'>('table');
    busyId = signal<string | null>(null);
    deleteConfirmId = signal<string | null>(null);
    error = signal('');

    // Sorting + column widths (N1). Both are per-browser view state, not account data — the same
    // treatment ThemeService gives the theme, and not worth a profile round-trip.
    // DB2.2 — creation date is the default: it's the one order that never changes under you,
    // unlike 'updated', which reshuffles the list every time you touch a draft.
    sortKey = signal<SortKey>('created');
    sortDir = signal<'asc' | 'desc'>('desc');
    colWidths = signal<number[]>(loadColWidths());

    // Folders (Phase "Cedar Clerk 0.9.0" idea #19, see the ADR following ADR-038,
    // docs/DECISIONS.md) — 'all' = no folder filter, 'none' = unfiled drafts only, else a folder id.
    // The list itself is shared (FoldersService) so a folder created or deleted in any picker
    // is reflected here without a reload; this page only owns which folder is being filtered on.
    folders = this.foldersApi.folders;
    selectedFolder = signal<'all' | 'none' | string>('all');

    // Both imports live here now (B22) — the editor topbar is Export/theme/profile only.
    importingCedar = signal(false);
    importCedarError = signal<string | null>(null);
    importingMarkdown = signal(false);
    importMarkdownError = signal<string | null>(null);
    importMarkdownWarning = signal<string | null>(null);

    async ngOnInit() {
        try {
            const [drafts] = await Promise.all([this.draftsApi.list(), this.foldersApi.ensureLoaded()]);
            this.drafts.set(drafts);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.load));
        } finally {
            this.loading.set(false);
        }
    }

    status(d: DraftMeta): DraftStatus {
        return computeStatus(d, this.t());
    }

    // A draft that was never on the blog can't have activity — show a dash rather than "0 0" (B23).
    hasActivity(d: DraftMeta): boolean {
        return d.isBlogPublished || d.viewCount > 0 || d.reactionCount > 0;
    }

    filterCount(key: FilterKey): number {
        return this.drafts().filter(d => matchesFilter(d, key)).length;
    }

    filteredDrafts(): DraftMeta[] {
        const q = this.search.trim().toLowerCase();
        const folder = this.selectedFolder();
        const dir = this.sortDir() === 'asc' ? 1 : -1;
        const key = this.sortKey();
        return this.drafts()
            .filter(d => matchesFilter(d, this.filter()))
            .filter(d => folder === 'all' || (folder === 'none' ? d.folderId === null : d.folderId === folder))
            .filter(d => !q || d.title.toLowerCase().includes(q) || d.tags.toLowerCase().includes(q))
            .sort((a, b) => dir * this.compare(a, b, key));
    }

    private compare(a: DraftMeta, b: DraftMeta, key: SortKey): number {
        switch (key) {
            case 'title': return (a.title || '').localeCompare(b.title || '');
            case 'state': return this.status(a).label.localeCompare(this.status(b).label);
            case 'languages': return a.languages.length - b.languages.length;
            case 'folder': return this.folderName(a.folderId).localeCompare(this.folderName(b.folderId));
            case 'tags': return a.tags.localeCompare(b.tags);
            // Views and reactions are one column, so they sort as one number.
            case 'activity': return (a.viewCount + a.reactionCount) - (b.viewCount + b.reactionCount);
            case 'updated': return a.updatedAt.localeCompare(b.updatedAt);
            default: return a.createdAt.localeCompare(b.createdAt);
        }
    }

    // Clicking the active column flips direction; a new column starts descending, since "newest
    // / most / last touched first" is what every one of these columns is usually asked for.
    sortBy(key: SortKey) {
        if (this.sortKey() === key) {
            this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
        } else {
            this.sortKey.set(key);
            this.sortDir.set('desc');
        }
    }

    sortMark(key: SortKey): string {
        if (this.sortKey() !== key) return '';
        return this.sortDir() === 'asc' ? '↑' : '↓';
    }

    gridTemplate(): string {
        return `1fr ${this.colWidths().map(w => `${w}px`).join(' ')} 80px`;
    }

    // Pointer events (not mouse) so a drag works with a trackpad, a pen and an iPad finger alike;
    // setPointerCapture keeps the drag alive when the pointer leaves the 5px handle.
    startColResize(index: number, ev: PointerEvent) {
        ev.preventDefault();
        ev.stopPropagation();
        const handle = ev.target as HTMLElement;
        const startX = ev.clientX;
        const startWidth = this.colWidths()[index];
        handle.setPointerCapture(ev.pointerId);

        const onMove = (move: PointerEvent) => {
            const width = Math.max(MIN_COL_WIDTH, Math.round(startWidth + (move.clientX - startX)));
            this.colWidths.update(list => list.map((w, i) => i === index ? width : w));
        };
        const onUp = () => {
            handle.releasePointerCapture(ev.pointerId);
            handle.removeEventListener('pointermove', onMove);
            handle.removeEventListener('pointerup', onUp);
            localStorage.setItem(COL_STORAGE_KEY, JSON.stringify(this.colWidths()));
        };
        handle.addEventListener('pointermove', onMove);
        handle.addEventListener('pointerup', onUp);
    }

    resetColWidths() {
        this.colWidths.set([...DEFAULT_COL_WIDTHS]);
        localStorage.removeItem(COL_STORAGE_KEY);
    }

    folderName(id: string | null): string {
        if (id === null) return this.t().drafts.folders.none;
        return this.folders().find(f => f.id === id)?.name ?? this.t().drafts.folders.none;
    }

    selectedFolderLabel(): string {
        const f = this.selectedFolder();
        if (f === 'all') return this.t().drafts.folders.all;
        if (f === 'none') return this.t().drafts.folders.none;
        return this.folderName(f);
    }

    async assignFolder(d: DraftMeta, folderId: string | null) {
        if (d.folderId === folderId) return;
        try {
            await this.draftsApi.setDraftFolder(d.id, folderId);
            this.drafts.update(list => list.map(x => x.id === d.id ? { ...x, folderId } : x));
            this.foldersApi.reload();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.move));
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
            this.importCedarError.set(httpErrorMessage(e, this.t().drafts.errors.import));
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
                this.importMarkdownWarning.set(this.t().drafts.errors.importUnmatched(created.unmatchedImages.length, created.unmatchedImages.join(', ')));
                this.drafts.set(await this.draftsApi.list());
            } else {
                // Nothing to report — go straight to the freshly imported draft.
                this.router.navigate(['/editor'], { queryParams: { draft: created.id } });
            }
        } catch (e) {
            this.importMarkdownError.set(httpErrorMessage(e, this.t().drafts.errors.import));
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
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.update));
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
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.delete));
        } finally {
            this.busyId.set(null);
        }
    }
}
