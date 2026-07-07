import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface ScheduledPost {
    id: string;
    draftId: string;
    draftTitle: string;
    chatId: string;
    scheduledAtUtc: string;
    status: 'Pending' | 'Sent' | 'Failed';
    error: string | null;
    messageId: number | null;
}

@Injectable({ providedIn: 'root' })
export class PostsService {
    private http = inject(HttpClient);

    export(draftId: string, chatId: string) {
        return firstValueFrom(this.http.post<{ messageId: number; chatId: string }>(
            '/api/posts/export', { draftId, chatId }));
    }

    schedule(draftId: string, chatId: string, scheduledAtUtc: string) {
        return firstValueFrom(this.http.post<{ id: string }>(
            '/api/posts/schedule', { draftId, chatId, scheduledAtUtc }));
    }

    listScheduled() {
        return firstValueFrom(this.http.get<ScheduledPost[]>('/api/posts/scheduled'));
    }

    cancelScheduled(id: string) {
        return firstValueFrom(this.http.delete(`/api/posts/scheduled/${id}`));
    }
}
