import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
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
}

export interface AllCommentsFeedback {
    reactions: { likes: number; dislikes: number };
    comments: AllCommentsComment[];
}

@Injectable({ providedIn: 'root' })
export class CommentsService {
    private http = inject(HttpClient);

    list(draftId: string) {
        return firstValueFrom(this.http.get<DraftFeedback>(`/api/drafts/${draftId}/comments`));
    }

    listAll() {
        return firstValueFrom(this.http.get<AllCommentsFeedback>('/api/comments'));
    }

    remove(commentId: string) {
        return firstValueFrom(this.http.delete(`/api/comments/${commentId}`));
    }
}
