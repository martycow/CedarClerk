import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { DraftsService, DraftMeta, FolderMeta, PostRegistration } from '../core/drafts.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { ModalComponent } from '../shared/modal.component';
import { CommentsComponent } from './comments.component';
import { StatsComponent } from './stats.component';
import {
    LucideTrash2 as Trash2, LucideArchive as Archive, LucideArchiveRestore as ArchiveRestore,
    LucidePenLine as PenLine, LucideLock as Lock, LucideExternalLink as ExternalLink,
    LucideRefreshCw as RefreshCw,
} from '@lucide/angular';

export type ManagerTab = 'posts' | 'feedback' | 'stats' | 'forms';

// N7 — the Posts Manager. Comments/reactions and stats used to be two separate top-level pages
// with their own headers; they are now tab bodies here (their routes redirect), so there is one
// place that answers "what happened to my posts". The forms tab is intentionally read-only for
// now — editing, per-question breakdowns and the pie chart are N10, presets are N12.
@Component({
    selector: 'app-posts-manager',
    imports: [
        DatePipe, FormsModule, CedarLogoComponent, ModalComponent, CommentsComponent, StatsComponent,
        Trash2, Archive, ArchiveRestore, PenLine, Lock, ExternalLink, RefreshCw,
    ],
    templateUrl: 'posts-manager.component.html',
    styleUrls: ['posts-manager.component.css'],
})
export class PostsManagerComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    private draftsApi = inject(DraftsService);
    private router = inject(Router);

    tab = signal<ManagerTab>('posts');
    loading = signal(true);
    error = signal('');
    busy = signal(false);

    drafts = signal<DraftMeta[]>([]);
    folders = signal<FolderMeta[]>([]);
    selectedId = signal<string | null>(null);

    // Minimal edits (Marty's wording) — everything here changes metadata only. Body text stays
    // the editor's job; the rename below still has to round-trip cedarJson because the save
    // endpoint takes title and body together.
    editTitle = '';
    editTags = '';
    renaming = signal(false);
    deleteConfirmId = signal<string | null>(null);

    registrations = signal<PostRegistration[]>([]);
    registrationsLoading = signal(false);

    async ngOnInit() {
        try {
            const [drafts, folders] = await Promise.all([this.draftsApi.list(), this.draftsApi.listFolders()]);
            this.drafts.set(drafts);
            this.folders.set(folders);
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to load posts'));
        } finally {
            this.loading.set(false);
        }
    }

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }

    setTab(tab: ManagerTab) {
        this.tab.set(tab);
        if (tab === 'forms' && this.selected()?.isPrivate) this.loadRegistrations();
    }

    // The forms tab only ever deals with private posts — that's the whole point of it.
    privatePosts(): DraftMeta[] {
        return this.drafts().filter(d => d.isPrivate);
    }

    selected(): DraftMeta | null {
        const id = this.selectedId();
        return id ? this.drafts().find(d => d.id === id) ?? null : null;
    }

    select(d: DraftMeta) {
        this.selectedId.set(d.id);
        this.editTitle = d.title;
        this.editTags = d.tags;
        this.registrations.set([]);
        if (this.tab() === 'forms' && d.isPrivate) this.loadRegistrations();
    }

    blogUrl(d: DraftMeta): string | null {
        return d.blogSlug ? `https://blog.mooexe.dev/${d.blogSlug}` : null;
    }

    telegramUrl(d: DraftMeta): string | null {
        return d.lastTelegramUsername && d.lastTelegramMessageId
            ? `https://t.me/${d.lastTelegramUsername}/${d.lastTelegramMessageId}`
            : null;
    }

    openInEditor(d: DraftMeta) {
        this.router.navigate(['/editor'], { queryParams: { draft: d.id } });
    }

    private patch(id: string, patch: Partial<DraftMeta>) {
        this.drafts.update(list => list.map(d => d.id === id ? { ...d, ...patch } : d));
    }

    async saveMeta() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        this.renaming.set(true);
        this.error.set('');
        try {
            const title = this.editTitle.trim() || 'Untitled';
            if (title !== d.title) {
                // The save endpoint replaces title and body together, so the body has to be
                // fetched and handed back unchanged — a rename must not touch content.
                const full = await this.draftsApi.get(d.id);
                await this.draftsApi.update(d.id, title, full.cedarJson);
            }
            const tags = this.editTags.trim();
            if (tags !== d.tags) await this.draftsApi.updateTags(d.id, tags);
            this.patch(d.id, { title, tags });
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to save changes'));
        } finally {
            this.busy.set(false);
            this.renaming.set(false);
        }
    }

    async assignFolder(folderId: string | null) {
        const d = this.selected();
        if (!d || d.folderId === folderId) return;
        try {
            await this.draftsApi.setDraftFolder(d.id, folderId);
            this.patch(d.id, { folderId });
            this.folders.set(await this.draftsApi.listFolders());
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to move post'));
        }
    }

    async togglePrivate() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        try {
            await this.draftsApi.setDraftPrivate(d.id, !d.isPrivate);
            this.patch(d.id, { isPrivate: !d.isPrivate });
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to change privacy'));
        } finally {
            this.busy.set(false);
        }
    }

    async toggleArchive() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        try {
            const res = d.isArchived ? await this.draftsApi.unarchive(d.id) : await this.draftsApi.archive(d.id);
            this.patch(d.id, { isArchived: res.isArchived });
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to update post'));
        } finally {
            this.busy.set(false);
        }
    }

    async confirmDelete() {
        const id = this.deleteConfirmId();
        if (!id) return;
        this.deleteConfirmId.set(null);
        this.busy.set(true);
        try {
            await this.draftsApi.remove(id);
            this.drafts.update(list => list.filter(d => d.id !== id));
            if (this.selectedId() === id) this.selectedId.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to delete post'));
        } finally {
            this.busy.set(false);
        }
    }

    async loadRegistrations() {
        const d = this.selected();
        if (!d || !d.isPrivate) return;
        this.registrationsLoading.set(true);
        try {
            this.registrations.set(await this.draftsApi.listRegistrations(d.id));
        } catch (e) {
            this.error.set(httpErrorMessage(e, 'Failed to load submissions'));
        } finally {
            this.registrationsLoading.set(false);
        }
    }

    // Answers are a client-authored blob keyed by question id (ADR-042); the labels live in the
    // form definition, so a submission alone can only honestly show "question id → answer".
    registrationAnswers(r: PostRegistration): { label: string; value: string }[] {
        if (!r.answersJson) return [];
        try {
            const parsed = JSON.parse(r.answersJson) as Record<string, string>;
            return Object.entries(parsed).map(([label, value]) => ({ label, value: String(value) }));
        } catch {
            return [];
        }
    }
}
