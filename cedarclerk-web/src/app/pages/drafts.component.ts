import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { DraftsService, DraftMeta } from '../core/drafts.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { ModalComponent } from '../shared/modal.component';
import { httpErrorMessage } from '../core/http-error.util';
import {
    LucideArrowLeft as ArrowLeft, LucidePlus as Plus,
    LucideArchive as Archive, LucideArchiveRestore as ArchiveRestore, LucideTrash2 as Trash2,
    LucideRefreshCw as RefreshCw, LucideLayoutGrid as LayoutGrid, LucideList as List,
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
        DatePipe, FormsModule, RouterLink, CedarLogoComponent, ModalComponent,
        ArrowLeft, Plus, Archive, ArchiveRestore, Trash2, RefreshCw, LayoutGrid, List,
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

    async ngOnInit() {
        try {
            this.drafts.set(await this.draftsApi.list());
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
        return this.drafts()
            .filter(d => matchesFilter(d, this.filter()))
            .filter(d => !q || d.title.toLowerCase().includes(q) || d.tags.toLowerCase().includes(q))
            .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
    }

    openDraft(id: string) {
        this.router.navigate(['/editor'], { queryParams: { draft: id } });
    }

    newDraft() {
        this.router.navigate(['/editor'], { queryParams: { new: 1 } });
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
