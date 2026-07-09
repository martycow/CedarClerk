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

@Injectable({ providedIn: 'root' })
export class CommentsService {
    private http = inject(HttpClient);

    list(draftId: string) {
        return firstValueFrom(this.http.get<DraftComment[]>(`/api/drafts/${draftId}/comments`));
    }

    remove(commentId: string) {
        return firstValueFrom(this.http.delete(`/api/comments/${commentId}`));
    }
}
