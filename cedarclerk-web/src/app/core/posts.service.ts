import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type PostFormat = 'Html' | 'Markdown';

export interface ScheduledPost {
    id: string;
    draftId: string;
    draftTitle: string;
    chatId: string;
    scheduledAtUtc: string;
    status: 'Pending' | 'Sent' | 'Failed';
    error: string | null;
    messageId: number | null;
    format: PostFormat;
}

@Injectable({ providedIn: 'root' })
export class PostsService {
    private http = inject(HttpClient);

    export(draftId: string, chatId: string, format: PostFormat) {
        return firstValueFrom(this.http.post<{ messageId: number; chatId: string }>(
            '/api/posts/export', { draftId, chatId, format }));
    }

    schedule(draftId: string, chatId: string, scheduledAtUtc: string, format: PostFormat) {
        return firstValueFrom(this.http.post<{ id: string }>(
            '/api/posts/schedule', { draftId, chatId, scheduledAtUtc, format }));
    }

    listScheduled() {
        return firstValueFrom(this.http.get<ScheduledPost[]>('/api/posts/scheduled'));
    }

    cancelScheduled(id: string) {
        return firstValueFrom(this.http.delete(`/api/posts/scheduled/${id}`));
    }
}
