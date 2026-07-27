import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { CommentsService, AllCommentsComment } from '../core/comments.service';
import { httpErrorMessage } from '../core/http-error.util';
import { LocaleService } from '../core/i18n/locale.service';
import { LucideTrash2 as Trash2 } from '@lucide/angular';

// Rendered as the Posts Manager's "reactions and comments" tab (N7). It no longer owns any page
// chrome — no header, no theme toggle, no back link.
@Component({
    selector: 'app-comments',
    imports: [DatePipe, Trash2],
    templateUrl: 'comments.component.html',
    styleUrls: ['comments.component.css']
})
export class CommentsComponent implements OnInit, OnDestroy {
    private commentsApi = inject(CommentsService);
    t = inject(LocaleService).t;

    loading = signal(true);
    reactions = signal({ likes: 0, dislikes: 0, newLikes: 0, newDislikes: 0 });
    comments = signal<AllCommentsComment[]>([]);
    error = signal('');

    // Highest createdAt the user has actually looked at this session (N8). Held here and flushed
    // once on leave rather than posted per hover — reading a list of twenty new comments would
    // otherwise be twenty requests.
    private pendingSeenAt: string | null = null;

    async ngOnInit() {
        this.loading.set(true);
        try {
            const feedback = await this.commentsApi.listAll();
            this.reactions.set(feedback.reactions);
            this.comments.set(feedback.comments);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().feedback.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    ngOnDestroy() {
        this.flushSeen();
    }

    // Hover is the "I've read this" signal, so the highlight clears under the pointer and the
    // watermark only moves as far as what was actually on screen.
    markSeen(c: AllCommentsComment) {
        if (!c.isNew) return;
        this.comments.update(list => list.map(x => x.id === c.id ? { ...x, isNew: false } : x));
        if (!this.pendingSeenAt || c.createdAt > this.pendingSeenAt) this.pendingSeenAt = c.createdAt;
    }

    // A dismissed reaction count has no per-item hover target, so it clears as one.
    markReactionsSeen() {
        const r = this.reactions();
        if (!r.newLikes && !r.newDislikes) return;
        this.reactions.set({ ...r, newLikes: 0, newDislikes: 0 });
        const now = new Date().toISOString();
        if (!this.pendingSeenAt || now > this.pendingSeenAt) this.pendingSeenAt = now;
    }

    private flushSeen() {
        const seenAt = this.pendingSeenAt;
        if (!seenAt) return;
        this.pendingSeenAt = null;
        // Fire-and-forget: the component is going away, and a failed watermark update only means
        // the highlights come back next time — nothing the user must be told about.
        this.commentsApi.markSeen(seenAt).catch(() => { });
    }

    async deleteComment(id: string) {
        this.error.set('');
        try {
            await this.commentsApi.remove(id);
            this.comments.update(list => list.filter(c => c.id !== id));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().feedback.deleteFailed));
        }
    }
}
