import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface DraftComment {
    id: string;
    annotationId: string | null;
    authorName: string | null;
    text: string;
    createdAt: string;
}

export interface DraftFeedback {
    reactions: { likes: number; dislikes: number };
    comments: DraftComment[];
}

export interface AllCommentsComment extends DraftComment {
    draftId: string;
    draftTitle: string;
    isNew: boolean; // created after the owner's feedback watermark (N8)
}

export interface AllCommentsFeedback {
    reactions: { likes: number; dislikes: number; newLikes: number; newDislikes: number };
    comments: AllCommentsComment[];
}

@Injectable({ providedIn: 'root' })
export class CommentsService {
    private http = inject(HttpClient);

    // Shared by every attention badge (N3) — root-provided, so the editor's account menu and the
    // Posts Manager's tab strip read one number instead of each counting for itself.
    readonly newComments = signal(0);
    readonly newReactions = signal(0);

    async refreshNewCount(): Promise<void> {
        try {
            const res = await firstValueFrom(this.http.get<{ newComments: number; newReactions: number }>('/api/comments/new-count'));
            this.newComments.set(res.newComments);
            this.newReactions.set(res.newReactions);
        } catch {
            // A badge is decoration — a failed count must never surface as an error anywhere.
            this.newComments.set(0);
            this.newReactions.set(0);
        }
    }

    list(draftId: string) {
        return firstValueFrom(this.http.get<DraftFeedback>(`/api/drafts/${draftId}/comments`));
    }

    listAll() {
        return firstValueFrom(this.http.get<AllCommentsFeedback>('/api/comments'));
    }

    remove(commentId: string) {
        return firstValueFrom(this.http.delete(`/api/comments/${commentId}`));
    }

    // Moves the "seen up to" watermark forward (N8). The server ignores a timestamp older than
    // the one it already has, so an out-of-order call can't resurrect old highlights.
    markSeen(seenAt: string) {
        return firstValueFrom(this.http.post<{ feedbackSeenAt: string | null }>('/api/comments/seen', { seenAt }));
    }
}
