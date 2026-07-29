import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription, TimeoutError } from 'rxjs';
import { AuthService } from '../core/auth.service';
import {
    DraftsService, DraftMeta, DRAFT_TITLE_MAX, EMPTY_DOC, NewDraftTemplate, NEW_DRAFT_TEMPLATES,
    CLOUDFLARE_UPLOAD_LIMIT_BYTES,
} from '../core/drafts.service';
import { FoldersService } from '../core/folders.service';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { TagPickerComponent } from '../shared/tag-picker.component';
import { LocaleService } from '../core/i18n/locale.service';
import { Dict } from '../core/i18n/en';
import { PageHeaderComponent } from '../shared/page-header.component';
import { ModalComponent } from '../shared/modal.component';
import { PopoverComponent } from '../shared/popover.component';
import { httpErrorMessage } from '../core/http-error.util';
import {
    LucidePlus as Plus,
    LucideArchive as Archive, LucideArchiveRestore as ArchiveRestore, LucideTrash2 as Trash2,
    LucideLayoutTemplate as LayoutTemplate,
    LucideRefreshCw as RefreshCw, LucideLayoutGrid as LayoutGrid, LucideList as List,
    LucideFolder as Folder,
    LucideLock as Lock, LucideFileUp as FileUp, LucideUpload as Upload,
    LucideEye as Eye, LucideHeart as Heart, LucideTriangleAlert as TriangleAlert,
} from '@lucide/angular';

type FilterKey = 'all' | 'draft' | 'scheduled' | 'published' | 'attention' | 'archived' | 'template';
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
        // NF1 — a template is never a "real" draft/scheduled/published/attention/archived row;
        // it only ever shows under its own tab, so it doesn't double-count elsewhere.
        case 'template': return d.isTemplate;
        case 'draft': return !d.isTemplate && !d.isArchived && !d.scheduled && !d.isBlogPublished && !d.lastTelegramMessageId;
        case 'scheduled': return !d.isTemplate && !d.isArchived && d.scheduled?.status === 'Pending';
        case 'published': return !d.isTemplate && !d.isArchived && (d.isBlogPublished || !!d.lastTelegramMessageId) && d.scheduled?.status !== 'Failed';
        case 'attention': return !d.isTemplate && !d.isArchived && (d.scheduled?.status === 'Failed' || d.staleLanguages.length > 0);
        case 'archived': return !d.isTemplate && d.isArchived;
        default: return !d.isTemplate && !d.isArchived;
    }
}

@Component({
    selector: 'app-drafts',
    imports: [
        DatePipe, FormsModule, PageHeaderComponent, ModalComponent, PopoverComponent,
        FolderPickerComponent, TagPickerComponent,
        Plus, Archive, ArchiveRestore, Trash2, RefreshCw, LayoutGrid, List,
        Folder, Lock, FileUp, Upload, Eye, Heart, LayoutTemplate, TriangleAlert,
    ],
    templateUrl: 'drafts.component.html',
    styleUrls: ['drafts.component.css'],
})
export class DraftsPageComponent implements OnInit, OnDestroy {
    auth = inject(AuthService);
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
    // Real byte-level upload progress (not the AI operations' pseudo-progress — an upload's
    // percentage is genuine) — null until the browser reports the first chunk.
    importMarkdownProgress = signal<number | null>(null);
    private importMarkdownSub?: Subscription;

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

    ngOnDestroy() {
        this.importMarkdownSub?.unsubscribe();
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

    // The dialog used to just navigate to /editor?new=1 and let the editor page create the draft
    // and open there — that put the browser on /editor mid-creation, with nothing in it yet, and
    // any creation failure landed on a blank editor rather than back here. Creation now happens
    // here, on /drafts; navigation to the editor only fires once the draft actually exists
    // (Marty, 28.07.2026) — same shape as onImportCedarChosen below, which already worked this way.
    readonly draftTitleMax = DRAFT_TITLE_MAX;
    newDraftOpen = signal(false);
    newDraftExpanded = signal(false);
    newDraftTitle = '';
    newDraftLanguages: 'ru' | 'en' | 'both' = 'ru';
    newDraftTagList = signal<string[]>([]);
    newDraftTemplate: NewDraftTemplate = 'blank';
    // Not persisted into newDraftDefaultsJson (unlike languages/tags/template) — "private" and
    // a target folder are per-draft intent, not a preference to repeat on every new draft.
    newDraftPrivate = false;
    newDraftFolderId = signal<string | null>(null);
    creatingDraft = signal(false);
    newDraftError = signal<string | null>(null);

    openNewDraftDialog() {
        let defaults: { languages?: 'ru' | 'en' | 'both'; tags?: string[]; template?: NewDraftTemplate } = {};
        try {
            defaults = JSON.parse(this.auth.newDraftDefaultsJson() ?? '{}');
        } catch { /* ignore a corrupt/foreign blob, fall back to built-in defaults */ }

        this.newDraftTitle = '';
        this.newDraftLanguages = defaults.languages ?? 'ru';
        this.newDraftTagList.set(defaults.tags ?? []);
        this.newDraftTemplate = defaults.template ?? 'blank';
        this.newDraftPrivate = false;
        this.newDraftFolderId.set(null);
        this.newDraftExpanded.set(false);
        this.newDraftError.set(null);
        this.newDraftOpen.set(true);
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
        if (this.creatingDraft() || !this.newDraftTitleValid()) return;
        const title = this.newDraftTitle.trim();
        const tagList = this.newDraftTagList().map(t => t.trim().toLowerCase()).filter(t => t.length > 0);
        const tags = tagList.join(',');
        const languages = this.newDraftLanguages;
        const template = this.newDraftTemplate;
        const isPrivate = this.newDraftPrivate;
        const folderId = this.newDraftFolderId();

        this.auth.saveNewDraftDefaults(JSON.stringify({
            languages,
            tags: tagList,
            template,
        })).catch(() => { /* best-effort — not worth blocking draft creation over */ });

        this.creatingDraft.set(true);
        this.newDraftError.set(null);
        try {
            const created = await this.draftsApi.create(title, NEW_DRAFT_TEMPLATES[template]);
            // Same follow-up-call shape as tags on the main list row: create first, then apply
            // the extras the create endpoint doesn't take.
            if (tags) await this.draftsApi.updateTags(created.id, tags);
            if (isPrivate) await this.draftsApi.setDraftPrivate(created.id, true);
            if (folderId) await this.draftsApi.setDraftFolder(created.id, folderId);
            if (languages === 'both') await this.draftsApi.saveTranslation(created.id, 'en', title, EMPTY_DOC);

            this.closeNewDraftDialog();
            this.router.navigate(['/editor'], { queryParams: { draft: created.id } });
        } catch (e) {
            this.newDraftError.set(httpErrorMessage(e, this.t().drafts.errors.create));
        } finally {
            this.creatingDraft.set(false);
        }
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

    onImportMarkdownChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const file = input.files?.[0];
        input.value = '';
        if (!file || this.importingMarkdown()) return;

        // Fail fast instead of letting a doomed upload run for a minute before Cloudflare's edge
        // rejects it anyway (see CLOUDFLARE_UPLOAD_LIMIT_BYTES) — same message either way.
        if (file.size > CLOUDFLARE_UPLOAD_LIMIT_BYTES) {
            this.importMarkdownError.set(this.t().drafts.errors.importTooLarge);
            return;
        }

        this.importingMarkdown.set(true);
        this.importMarkdownError.set(null);
        this.importMarkdownWarning.set(null);
        this.importMarkdownProgress.set(0);

        this.importMarkdownSub = this.draftsApi.importMarkdown$(file).subscribe({
            next: event => {
                if (event.type === HttpEventType.UploadProgress && event.total) {
                    this.importMarkdownProgress.set(Math.round((event.loaded / event.total) * 100));
                } else if (event.type === HttpEventType.Response && event.body) {
                    const created = event.body;
                    if (created.unmatchedImages.length > 0) {
                        this.importMarkdownWarning.set(this.t().drafts.errors.importUnmatched(created.unmatchedImages.length, created.unmatchedImages.join(', ')));
                        this.draftsApi.list().then(list => this.drafts.set(list));
                    } else {
                        // Nothing to report — go straight to the freshly imported draft.
                        this.router.navigate(['/editor'], { queryParams: { draft: created.id } });
                    }
                }
            },
            error: e => {
                // 413 here means a proxy/tunnel in front of the server rejected the body outright
                // (confirmed 28.07.2026 against a real oversized upload — Kestrel's own limit is
                // 200MB and would surface as our own JSON {error}, not a bare 413) — worth naming
                // explicitly rather than falling through to the generic import-failed message.
                if (e instanceof HttpErrorResponse && e.status === 413) {
                    this.importMarkdownError.set(this.t().drafts.errors.importTooLarge);
                } else {
                    this.importMarkdownError.set(e instanceof TimeoutError
                        ? this.t().drafts.errors.importStalled
                        : httpErrorMessage(e, this.t().drafts.errors.import));
                }
                this.finishImportMarkdown();
            },
            complete: () => this.finishImportMarkdown(),
        });
    }

    private finishImportMarkdown() {
        this.importingMarkdown.set(false);
        this.importMarkdownProgress.set(null);
        this.importMarkdownSub = undefined;
    }

    // User-initiated cancel — unsubscribing aborts the underlying HTTP request, same reasoning as
    // cancelAutoTranslate/cancelAiEdit in the editor.
    cancelImportMarkdown() {
        this.importMarkdownSub?.unsubscribe();
        this.finishImportMarkdown();
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

    // NF1 — post templates.
    async toggleTemplate(d: DraftMeta, ev: Event) {
        ev.stopPropagation();
        if (this.busyId()) return;
        this.busyId.set(d.id);
        this.error.set('');
        try {
            const res = await this.draftsApi.setDraftTemplate(d.id, !d.isTemplate);
            this.drafts.update(list => list.map(x => x.id === d.id ? { ...x, isTemplate: res.isTemplate } : x));
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
