import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { formatInZone } from '../core/display-time';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subscription, TimeoutError } from 'rxjs';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { AuthService } from '../core/auth.service';
import {
    DraftsService, DraftMeta, DRAFT_TITLE_MAX, EMPTY_DOC,
    CLOUDFLARE_UPLOAD_LIMIT_BYTES,
} from '../core/drafts.service';
import { FoldersService } from '../core/folders.service';
import { SeriesService } from '../core/series.service';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { TagPickerComponent } from '../shared/tag-picker.component';
import { SeriesPickerComponent } from '../shared/series-picker.component';
import { CONTENT_LANGUAGES, DEFAULT_PRIMARY_LANGUAGE } from '../core/languages';
import { LocaleService } from '../core/i18n/locale.service';
import { Dict } from '../core/i18n/en';
import { ModalComponent } from '../shared/modal.component';
import { PopoverComponent } from '../shared/popover.component';
import { httpErrorMessage } from '../core/http-error.util';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';
import { ButtonComponent } from '../bench/forms/button.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';
import { InputComponent } from '../bench/forms/input.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { SortHeaderComponent } from '../bench/worktop/sort-header.component';
import { ariaSort } from '../core/collection-query';

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

// The title track has a floor now. It used to be a bare `1fr`, and a grid gives a fractional track
// whatever is left after the fixed ones — which on an iPad is nothing: the fixed columns, the seven
// gaps and the padding already need 1156px, and the row's stated min-width said 1020px. The result
// was a table with no titles in it and two column headers drawn on top of each other.
const TITLE_MIN_WIDTH = 200;
// These two mirror the global .row (gap 14px, padding 0 --space-4 = 16 on each side). They are
// duplicated here because rowMinWidth() has to add them up in TypeScript, and the 0.9.19 bug was
// exactly this pair being written down once and then left to drift — so if .row moves, these move
// with it.
const ROW_GAP = 14;
const ROW_PADDING = 32;
// Five controls at the paper box plus the gaps between them (describe, evergreen, template,
// archive, delete). It was 80 — narrower than the buttons it holds — so the group overflowed left
// and printed over the UPDATED column beside it.
const ACTIONS_WIDTH = 5 * 38 + 4 * 4;

// Below this the row would have to scroll sideways to show everything, so it stops showing
// everything instead: Tags and Activity are the two columns you can lose and still recognise a
// post. iPad landscape (1180px) is the case this exists for — Marty reads the list there daily.
const COMPACT_MAX_WIDTH = 1280;
// Indices into DEFAULT_COL_WIDTHS: state, languages, folder, updated. Tags (3) and activity (4)
// are the ones dropped.
const COMPACT_COLUMNS = [0, 1, 2, 5];

// A second tier for iPad portrait (820px) and phones. Even the compact set leaves the row wider
// than the viewport there, and what falls off the right edge is the actions column — so archive and
// delete became unreachable without a sideways scroll inside the table. Folder and Updated go; State
// and Languages stay, because those are the two a person scans the list *for*.
const TIGHT_MAX_WIDTH = 900;
const TIGHT_COLUMNS = [0, 1];

// Phone. The tight set still needs 644px of row (title 200 + state 200 + languages 120 + actions 80
// + gaps + padding) against 390px of iPhone, so a third tier drops to State alone — and overrides
// its stored width, because 200px for a one-word badge is a desktop measurement that survives into
// a place it makes no sense. The title floor drops with it: on a phone a truncated title you can
// read half of beats a title column you have to scroll to.
const PHONE_MAX_WIDTH = 560;
const PHONE_COL_WIDTHS = [80];
const PHONE_TITLE_MIN_WIDTH = 130;

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
        const when = formatInZone(d.scheduled.scheduledAtUtc);
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
        IconComponent, ZonedDatePipe, FormsModule, NgTemplateOutlet, ModalComponent, PopoverComponent,
        FolderPickerComponent, TagPickerComponent, SeriesPickerComponent, IndexTabsComponent,
        SpecRowComponent, InputComponent, ButtonComponent, LeafTagComponent, PaperCardComponent,
        PageHeaderComponent, EmptyStateComponent, RouterLink,
        SortHeaderComponent,
    ],
    templateUrl: 'drafts.component.html',
    styleUrls: ['drafts.component.css'],
})
export class DraftsPageComponent implements OnInit, OnDestroy {
    auth = inject(AuthService);
    private locale = inject(LocaleService);
    t = this.locale.t;
    private draftsApi = inject(DraftsService);
    private foldersApi = inject(FoldersService);
    private seriesApi = inject(SeriesService);
    private router = inject(Router);

    loading = signal(true);
    drafts = signal<DraftMeta[]>([]);
    search = '';
    filter = signal<FilterKey>('all');
    view = signal<'table' | 'grid' | 'tree'>('table');
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
    // Narrow enough that the full column set no longer fits — see COMPACT_MAX_WIDTH.
    compact = signal(window.innerWidth <= COMPACT_MAX_WIDTH);
    tight = signal(window.innerWidth <= TIGHT_MAX_WIDTH);
    phone = signal(window.innerWidth <= PHONE_MAX_WIDTH);
    private readonly onResize = () => {
        this.compact.set(window.innerWidth <= COMPACT_MAX_WIDTH);
        this.tight.set(window.innerWidth <= TIGHT_MAX_WIDTH);
        this.phone.set(window.innerWidth <= PHONE_MAX_WIDTH);
    };

    // Folders (Phase "Cedar Clerk 0.9.0" idea #19, see the ADR following ADR-038,
    // docs/DECISIONS.md) — 'all' = no folder filter, 'none' = unfiled drafts only, else a folder id.
    // The list itself is shared (FoldersService) so a folder created or deleted in any picker
    // is reflected here without a reload; this page only owns which folder is being filtered on.
    folders = this.foldersApi.folders;
    selectedFolder = signal<'all' | 'none' | string>('all');

    // T-256 — which row the shelf describes. A row's own click is the door to the editor
    // (ADR-163), so this is only ever set by the explicit control in .row-actions.
    selectedId = signal<string | null>(null);

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
        window.addEventListener('resize', this.onResize);
        try {
            const [drafts] = await Promise.all([
                this.draftsApi.list(), this.foldersApi.ensureLoaded(), this.seriesApi.ensureLoaded(),
            ]);
            this.drafts.set(drafts);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.load));
        } finally {
            this.loading.set(false);
        }
    }

    ngOnDestroy() {
        this.importMarkdownSub?.unsubscribe();
        window.removeEventListener('resize', this.onResize);
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

    readonly filterKeys: FilterKey[] = ['all', 'draft', 'scheduled', 'published', 'attention', 'archived', 'template'];

    stateTabs(): IndexTabItem[] {
        const labels = this.t().drafts.filters;
        return this.filterKeys.map(key => ({ id: key, label: labels[key], badge: this.filterCount(key) }));
    }

    viewTabs(): IndexTabItem[] {
        const labels = this.t().drafts;
        return [
            { id: 'table', label: labels.viewTable },
            { id: 'grid', label: labels.viewGrid },
            { id: 'tree', label: labels.tree.view },
        ];
    }

    /** The header's meta line: what the library holds, in the states a writer scans it for. */
    headerMeta(): HeaderMeta[] {
        const t = this.t().drafts;
        const attention = this.filterCount('attention');
        return [
            { text: t.postsCount(this.drafts().length) },
            { text: t.meta.published(this.filterCount('published')) },
            ...(this.filterCount('scheduled') ? [{ text: t.meta.scheduled(this.filterCount('scheduled')) }] : []),
            ...(attention ? [{ text: t.meta.attention(attention), tag: true, tone: 'warn' as const }] : []),
        ];
    }

    /** The empty state's way back: every filter off, so the list shows the whole library again. */
    showAll() {
        this.filter.set('all');
        this.selectedFolder.set('all');
        this.search = '';
    }

    /** The shelf's "No folder" tally. The server counts folders, not the absence of one. */
    unfiledCount(): number {
        return this.drafts().filter(d => d.folderId === null).length;
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
            .sort((a, b) => dir * this.compare(a, b, key) || a.id.localeCompare(b.id));
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

    sortMark(key: SortKey): IconName | '' {
        if (this.sortKey() !== key) return '';
        return this.sortDir() === 'asc' ? 'arrow-up' : 'arrow-down';
    }

    columnSort(key: SortKey): 'ascending' | 'descending' | null {
        return ariaSort(this.sortKey() === key, this.sortDir());
    }

    // Which columns are actually drawn. The template, the row's min-width and the `@if`s in the
    // markup all read this one signal, so they cannot disagree about how many tracks exist.
    private visibleColWidths(): number[] {
        const all = this.colWidths();
        if (this.phone()) return PHONE_COL_WIDTHS;
        if (this.tight()) return TIGHT_COLUMNS.map(i => all[i]);
        return this.compact() ? COMPACT_COLUMNS.map(i => all[i]) : all;
    }

    private titleMinWidth(): number {
        return this.phone() ? PHONE_TITLE_MIN_WIDTH : TITLE_MIN_WIDTH;
    }

    gridTemplate(): string {
        const cols = this.visibleColWidths();
        return `minmax(${this.titleMinWidth()}px, 1fr) ${cols.map(w => `${w}px`).join(' ')} ${ACTIONS_WIDTH}px`;
    }

    // Computed rather than written down: the old hardcoded 1020px was 136px short of the truth,
    // which is what let the title collapse instead of the table scrolling.
    rowMinWidth(): string {
        const cols = this.visibleColWidths();
        const fixed = cols.reduce((sum, w) => sum + w, 0) + ACTIONS_WIDTH;
        const gaps = (cols.length + 1) * ROW_GAP;
        return `${fixed + gaps + ROW_PADDING + this.titleMinWidth()}px`;
    }

    tableColumnCount(): number {
        return this.visibleColWidths().length + 2;
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

    // ---- the draft shelf (T-256, ADR-238 clauses 6-7) ------------------------------------------
    // Read-only, and exclusive with the folder filter: the shelf shows the picked draft or the
    // folders, never both (ADR-167). Everything below reads DraftMeta and the two shared lists —
    // picking a row asks the server nothing.

    /** Resolved against the filtered list, so a draft filtered off screen stops being described. */
    selectedDraft(): DraftMeta | null {
        const id = this.selectedId();
        return id ? this.filteredDrafts().find(d => d.id === id) ?? null : null;
    }

    toggleSelected(d: DraftMeta, ev: Event) {
        ev.stopPropagation();
        this.selectedId.update(id => id === d.id ? null : d.id);
    }

    seriesName(id: string | null): string {
        if (id === null) return this.t().drafts.series.none;
        return this.seriesApi.find(id)?.name ?? this.t().drafts.series.none;
    }

    /** Primary first, then the translations — the row badges show the same set. */
    allLanguages(d: DraftMeta): string[] {
        return [d.primaryLanguage, ...d.languages.filter(l => l !== d.primaryLanguage)];
    }

    tagList(d: DraftMeta): string[] {
        return d.tags.split(',').map(tag => tag.trim()).filter(tag => tag.length > 0);
    }

    parentTitle(d: DraftMeta): string {
        if (!d.parentDraftId) return this.t().drafts.tree.root;
        const parent = this.drafts().find(x => x.id === d.parentDraftId);
        return parent?.title || this.t().drafts.untitled;
    }

    childCount(d: DraftMeta): number {
        return this.drafts().filter(x => x.parentDraftId === d.id).length;
    }

    // ---- document tree (ADR-128) --------------------------------------------------------------
    // The tree is structure, not a filtered list: the status tabs and the folder filter do not
    // apply here (a parent that fails a filter would take its whole visible subtree with it).
    // Search still works, showing matches with their ancestor path.

    collapsed = signal<ReadonlySet<string>>(new Set());

    toggleExpanded(id: string, ev: Event) {
        ev.stopPropagation();
        this.collapsed.update(set => {
            const next = new Set(set);
            if (next.has(id)) next.delete(id); else next.add(id);
            return next;
        });
    }

    isCollapsed(id: string) {
        return this.collapsed().has(id);
    }

    private treePool(): DraftMeta[] {
        return this.drafts().filter(d => !d.isArchived);
    }

    private treeChildrenOf(pool: DraftMeta[], parentId: string | null): DraftMeta[] {
        return pool.filter(d => d.parentDraftId === parentId)
            .sort((a, b) => a.siblingOrder - b.siblingOrder || b.updatedAt.localeCompare(a.updatedAt));
    }

    // Search keeps a node visible when it or any descendant matches, plus the ancestors of every
    // match — a hit deep in the tree arrives with its path, not floating alone.
    private treeVisibleIds(pool: DraftMeta[]): Set<string> | null {
        const q = this.search.trim().toLowerCase();
        if (!q) return null;
        const byId = new Map(pool.map(d => [d.id, d]));
        const visible = new Set<string>();
        for (const d of pool) {
            if (!d.title.toLowerCase().includes(q) && !d.tags.toLowerCase().includes(q)) continue;
            for (let cursor: DraftMeta | undefined = d, hops = 0; cursor && hops < 12; hops++) {
                visible.add(cursor.id);
                cursor = cursor.parentDraftId ? byId.get(cursor.parentDraftId) : undefined;
            }
        }
        return visible;
    }

    treeRows(): { d: DraftMeta; depth: number; hasChildren: boolean }[] {
        const pool = this.treePool();
        const visible = this.treeVisibleIds(pool);
        const rows: { d: DraftMeta; depth: number; hasChildren: boolean }[] = [];
        const walk = (parentId: string | null, depth: number) => {
            if (depth > 12) return;
            for (const d of this.treeChildrenOf(pool, parentId)) {
                if (visible && !visible.has(d.id)) continue;
                const children = this.treeChildrenOf(pool, d.id);
                rows.push({ d, depth, hasChildren: children.length > 0 });
                if (!this.isCollapsed(d.id)) walk(d.id, depth + 1);
            }
        };
        walk(null, 0);
        // A child of an archived (hidden) parent would vanish from every walk above — surface
        // such orphans at root level rather than losing them.
        const seen = new Set(rows.map(r => r.d.id));
        for (const d of pool) {
            if (seen.has(d.id)) continue;
            if (visible && !visible.has(d.id)) continue;
            rows.push({ d, depth: 0, hasChildren: false });
        }
        return rows;
    }

    // Candidates for "move under…": everything except the node itself and its own subtree.
    moveTargets(d: DraftMeta): DraftMeta[] {
        const pool = this.treePool();
        const excluded = new Set<string>([d.id]);
        let grew = true;
        while (grew) {
            grew = false;
            for (const x of pool) {
                if (x.parentDraftId && excluded.has(x.parentDraftId) && !excluded.has(x.id)) {
                    excluded.add(x.id);
                    grew = true;
                }
            }
        }
        return pool.filter(x => !excluded.has(x.id)).sort((a, b) => a.title.localeCompare(b.title));
    }

    private async applyMove(id: string, parentId: string | null, beforeId?: string) {
        if (this.busyId()) return;
        this.busyId.set(id);
        this.error.set('');
        try {
            await this.draftsApi.setDraftParent(id, parentId, beforeId);
            // Mirror the server's renumbering locally (same ordering rule) instead of refetching:
            // GET /api/drafts advances the "new since last visit" baselines as a side effect.
            this.drafts.update(list => {
                const moved = list.find(d => d.id === id);
                if (!moved) return list;
                const siblings = list.filter(d => d.parentDraftId === parentId && d.id !== id)
                    .sort((a, b) => a.siblingOrder - b.siblingOrder || b.updatedAt.localeCompare(a.updatedAt));
                let at = siblings.length;
                if (beforeId) {
                    const i = siblings.findIndex(d => d.id === beforeId);
                    if (i >= 0) at = i;
                }
                siblings.splice(at, 0, moved);
                const orders = new Map(siblings.map((d, i) => [d.id, i]));
                return list.map(d => orders.has(d.id)
                    ? { ...d, siblingOrder: orders.get(d.id)!, parentDraftId: d.id === id ? parentId : d.parentDraftId }
                    : d);
            });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.move));
        } finally {
            this.busyId.set(null);
        }
    }

    moveUnder(d: DraftMeta, parentId: string | null, ev: Event) {
        ev.stopPropagation();
        if (d.parentDraftId === parentId) return;
        void this.applyMove(d.id, parentId);
    }

    moveUp(d: DraftMeta, ev: Event) {
        ev.stopPropagation();
        const siblings = this.treeChildrenOf(this.treePool(), d.parentDraftId);
        const i = siblings.findIndex(x => x.id === d.id);
        if (i <= 0) return;
        void this.applyMove(d.id, d.parentDraftId, siblings[i - 1].id);
    }

    moveDown(d: DraftMeta, ev: Event) {
        ev.stopPropagation();
        const siblings = this.treeChildrenOf(this.treePool(), d.parentDraftId);
        const i = siblings.findIndex(x => x.id === d.id);
        if (i < 0 || i >= siblings.length - 1) return;
        const after = siblings[i + 2];
        void this.applyMove(d.id, d.parentDraftId, after?.id);
    }

    // The dialog used to just navigate to /editor?new=1 and let the editor page create the draft
    // and open there — that put the browser on /editor mid-creation, with nothing in it yet, and
    // any creation failure landed on a blank editor rather than back here. Creation now happens
    // here, on /drafts; navigation to the editor only fires once the draft actually exists
    // (Marty, 28.07.2026) — same shape as onImportCedarChosen below, which already worked this way.
    readonly draftTitleMax = DRAFT_TITLE_MAX;
    newDraftOpen = signal(false);
    newDraftTitle = '';
    /** Every content language the account may write in; the dialog offers all of them. */
    readonly contentLanguages = CONTENT_LANGUAGES;
    /** Picked languages, in pick order — the first is the draft's primary. */
    newDraftLanguages = signal<string[]>([DEFAULT_PRIMARY_LANGUAGE]);
    newDraftTagList = signal<string[]>([]);
    newDraftSeriesId = signal<string | null>(null);
    // Not persisted into newDraftDefaultsJson (unlike languages/tags/template) — "private" and
    // a target folder are per-draft intent, not a preference to repeat on every new draft.
    newDraftPrivate = false;
    newDraftFolderId = signal<string | null>(null);
    creatingDraft = signal(false);
    newDraftError = signal<string | null>(null);

    openNewDraftDialog() {
        let defaults: { languages?: unknown; tags?: string[] } = {};
        try {
            defaults = JSON.parse(this.auth.newDraftDefaultsJson() ?? '{}');
        } catch { /* ignore a corrupt/foreign blob, fall back to built-in defaults */ }

        this.newDraftTitle = '';
        this.newDraftLanguages.set(readLanguages(defaults.languages));
        // At least one tag on show: with nothing remembered, the tag the account uses most is the
        // one it would have picked anyway, and an empty tag row taught nobody the field exists.
        const remembered = defaults.tags ?? [];
        this.newDraftTagList.set(remembered.length ? remembered : this.mostUsedTag());
        this.newDraftPrivate = false;
        this.newDraftFolderId.set(null);
        this.newDraftSeriesId.set(null);
        this.newDraftError.set(null);
        this.newDraftOpen.set(true);
    }

    /** Pick order matters: the first language picked is the one the draft is written in. */
    toggleNewDraftLanguage(code: string) {
        const picked = this.newDraftLanguages();
        this.newDraftLanguages.set(picked.includes(code) ? picked.filter(c => c !== code) : [...picked, code]);
    }

    /** The single tag carried by most drafts, as a one-element list; empty when there are none. */
    private mostUsedTag(): string[] {
        const counts = new Map<string, number>();
        for (const d of this.drafts()) {
            for (const tag of (d.tags ?? '').split(',')) {
                const clean = tag.trim().toLowerCase();
                if (clean) counts.set(clean, (counts.get(clean) ?? 0) + 1);
            }
        }
        let best = '';
        let bestCount = 0;
        for (const [tag, count] of counts) {
            if (count > bestCount) { best = tag; bestCount = count; }
        }
        return best ? [best] : [];
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
        const languages = this.newDraftLanguages();
        if (this.creatingDraft() || !this.newDraftTitleValid() || !languages.length) return;
        const title = this.newDraftTitle.trim();
        const tagList = this.newDraftTagList().map(t => t.trim().toLowerCase()).filter(t => t.length > 0);
        const tags = tagList.join(',');
        const isPrivate = this.newDraftPrivate;
        const folderId = this.newDraftFolderId();
        const seriesId = this.newDraftSeriesId();

        this.auth.saveNewDraftDefaults(JSON.stringify({
            languages,
            tags: tagList,
        })).catch(() => { /* best-effort — not worth blocking draft creation over */ });

        this.creatingDraft.set(true);
        this.newDraftError.set(null);
        try {
            const created = await this.draftsApi.create(title, EMPTY_DOC);
            // Same follow-up-call shape as tags on the main list row: create first, then apply
            // the extras the create endpoint doesn't take.
            if (tags) await this.draftsApi.updateTags(created.id, tags);
            if (isPrivate) await this.draftsApi.setDraftPrivate(created.id, true);
            if (folderId) await this.draftsApi.setDraftFolder(created.id, folderId);
            if (seriesId) await this.draftsApi.setDraftSeries(created.id, seriesId);
            // The first picked language is the one the draft is created in; each of the rest gets
            // an empty version so the language tabs are there from the start.
            for (const code of languages.slice(1)) {
                await this.draftsApi.saveTranslation(created.id, code, title, EMPTY_DOC);
            }

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

    // ---- evergreen recycling (Wave 2 item 10) -------------------------------------------------
    // A row action beside mark-template: the flag plus its bounds (category, max sends, until) in
    // one small dialog, saved through the one-endpoint PATCH shape (ADR-056's /template pattern).
    evergreenTarget = signal<DraftMeta | null>(null);
    evIsEvergreen = false;
    evCategory = '';
    evMaxSends = '';
    evUntil = '';
    evBusy = signal(false);
    evError = signal<string | null>(null);

    openEvergreenDialog(d: DraftMeta, ev: Event) {
        ev.stopPropagation();
        this.evIsEvergreen = !!d.isEvergreen;
        this.evCategory = d.evergreenCategory ?? '';
        this.evMaxSends = d.evergreenMaxSends != null ? String(d.evergreenMaxSends) : '';
        this.evUntil = d.evergreenUntil ? d.evergreenUntil.slice(0, 10) : '';
        this.evError.set(null);
        this.evergreenTarget.set(d);
    }

    async saveEvergreen() {
        const d = this.evergreenTarget();
        if (!d || this.evBusy()) return;
        const maxSends = this.evMaxSends.trim() ? Math.max(1, Math.floor(Number(this.evMaxSends))) : null;
        const until = this.evUntil ? new Date(this.evUntil + 'T23:59:59').toISOString() : null;
        this.evBusy.set(true);
        this.evError.set(null);
        try {
            const res = await this.draftsApi.setEvergreen(d.id, {
                isEvergreen: this.evIsEvergreen,
                category: this.evCategory.trim().toLowerCase(),
                maxSends,
                until,
            });
            this.drafts.update(list => list.map(x => x.id === d.id
                ? { ...x, isEvergreen: res.isEvergreen, evergreenCategory: res.category, evergreenMaxSends: res.maxSends, evergreenUntil: res.until }
                : x));
            this.evergreenTarget.set(null);
        } catch (e) {
            this.evError.set(httpErrorMessage(e, this.t().drafts.errors.update));
        } finally {
            this.evBusy.set(false);
        }
    }

    // ---- new from template (Wave 2 item 18) ---------------------------------------------------
    templatePickerOpen = signal(false);
    starterTemplates = signal<{ id: string; name: string; description: string }[]>([]);
    starterLoading = signal(false);
    templateError = signal<string | null>(null);
    creatingFromTemplate = signal(false);

    /** The caller's own IsTemplate drafts — the picker's second section. */
    myTemplates(): DraftMeta[] {
        return this.drafts().filter(d => d.isTemplate)
            .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
    }

    async openTemplatePicker() {
        this.templateError.set(null);
        this.templatePickerOpen.set(true);
        if (this.starterTemplates().length) return;
        this.starterLoading.set(true);
        try {
            this.starterTemplates.set(await this.draftsApi.templateLibrary());
        } catch (e) {
            // 404 until the server lane lands — "Your templates" still works without the library.
            this.templateError.set(httpErrorMessage(e, this.t().drafts.templates.loadFailed));
        } finally {
            this.starterLoading.set(false);
        }
    }

    async createFromStarter(libraryId: string) {
        await this.createFromTemplate({ libraryId, language: this.locale.uiLang() });
    }

    async createFromOwnTemplate(draftId: string) {
        await this.createFromTemplate({ draftId });
    }

    private async createFromTemplate(input: { libraryId?: string; draftId?: string; language?: string }) {
        if (this.creatingFromTemplate()) return;
        this.creatingFromTemplate.set(true);
        this.templateError.set(null);
        try {
            const created = await this.draftsApi.createFromTemplate(input);
            this.templatePickerOpen.set(false);
            this.router.navigate(['/editor'], { queryParams: { draft: created.id } });
        } catch (e) {
            this.templateError.set(httpErrorMessage(e, this.t().drafts.errors.create));
        } finally {
            this.creatingFromTemplate.set(false);
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

/**
 * The remembered language choice. It used to be one of three words — 'ru' | 'en' | 'both' — and a
 * stored blob still carries one, so both shapes read back into the list the dialog now works with.
 */
function readLanguages(raw: unknown): string[] {
    if (Array.isArray(raw)) {
        const picked = raw.filter((c): c is string => typeof c === 'string' && CONTENT_LANGUAGES.includes(c));
        if (picked.length) return picked;
    }
    if (raw === 'both') return ['ru', 'en'];
    if (typeof raw === 'string' && CONTENT_LANGUAGES.includes(raw)) return [raw];
    return [DEFAULT_PRIMARY_LANGUAGE];
}
