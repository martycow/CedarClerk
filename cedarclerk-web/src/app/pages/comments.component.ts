import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { CommentsService, AllCommentsComment, DraftReactions } from '../core/comments.service';
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
    reactionsByDraft = signal<DraftReactions[]>([]);
    comments = signal<AllCommentsComment[]>([]);
    error = signal('');

    // Feedback is grouped per post now: an undifferentiated stream answered "what's new" but not
    // "what happened to this post", which is the question this tab exists for. Each group shows a
    // few comments until expanded, so a busy post can't push every other one off the screen.
    private readonly collapsedCount = 3;
    private expanded = signal<Set<string>>(new Set());

    // Highest createdAt the user has actually looked at this session (N8). Held here and flushed
    // once on leave rather than posted per hover — reading a list of twenty new comments would
    // otherwise be twenty requests.
    private pendingSeenAt: string | null = null;

    async ngOnInit() {
        this.loading.set(true);
        try {
            const feedback = await this.commentsApi.listAll();
            this.reactions.set(feedback.reactions);
            this.reactionsByDraft.set(feedback.reactionsByDraft ?? []);
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

    // One row per post that has any feedback at all, newest activity first. Reaction-only posts
    // still get a row — a post with 20 likes and no comments is exactly as worth seeing.
    groups(): { draftId: string; draftTitle: string; comments: AllCommentsComment[]; reactions: DraftReactions | null }[] {
        const byDraft = new Map<string, AllCommentsComment[]>();
        for (const c of this.comments()) {
            const list = byDraft.get(c.draftId);
            if (list) list.push(c);
            else byDraft.set(c.draftId, [c]);
        }
        const reactions = new Map(this.reactionsByDraft().map(r => [r.draftId, r]));
        const ids = new Set([...byDraft.keys(), ...reactions.keys()]);

        return [...ids]
            .map(draftId => {
                const comments = byDraft.get(draftId) ?? [];
                const r = reactions.get(draftId) ?? null;
                return {
                    draftId,
                    draftTitle: comments[0]?.draftTitle ?? r?.draftTitle ?? '',
                    comments,
                    reactions: r,
                };
            })
            // comments arrive newest-first from the server, so the first one dates the group.
            .sort((a, b) => (b.comments[0]?.createdAt ?? '').localeCompare(a.comments[0]?.createdAt ?? ''));
    }

    visibleComments(g: { draftId: string; comments: AllCommentsComment[] }): AllCommentsComment[] {
        return this.expanded().has(g.draftId) ? g.comments : g.comments.slice(0, this.collapsedCount);
    }

    hiddenCount(g: { draftId: string; comments: AllCommentsComment[] }): number {
        return this.expanded().has(g.draftId) ? 0 : Math.max(0, g.comments.length - this.collapsedCount);
    }

    isExpanded(draftId: string): boolean {
        return this.expanded().has(draftId);
    }

    toggleExpanded(draftId: string) {
        this.expanded.update(set => {
            const next = new Set(set);
            if (next.has(draftId)) next.delete(draftId);
            else next.add(draftId);
            return next;
        });
    }

    // A per-post reaction count has no individual row to hover, so it clears as one — same
    // treatment the single global card used to get.
    markDraftReactionsSeen(r: DraftReactions | null) {
        if (!r || (!r.newLikes && !r.newDislikes)) return;
        this.reactionsByDraft.update(list => list.map(x =>
            x.draftId === r.draftId ? { ...x, newLikes: 0, newDislikes: 0 } : x));
        const now = new Date().toISOString();
        if (!this.pendingSeenAt || now > this.pendingSeenAt) this.pendingSeenAt = now;
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
