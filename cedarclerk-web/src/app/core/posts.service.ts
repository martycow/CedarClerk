import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type PostFormat = 'Html' | 'Markdown';
// NF2 — any content language code; the server validates against Languages.ContentLanguages.
// Was a two-value union back when a post could only be RU or EN.
export type PostLanguage = string;
export type CompressionLevel = 'small' | 'standard' | 'high';

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
    language: PostLanguage;
    channelTitle: string | null;
}

export interface PublishDiff {
    beforeLines: number;
    afterLines: number;
    addedLines: number[];
    removedLines: number[];
    changedLines: number;
    totalChanged: number;
}

// ADR-065 — one per language being published. `fingerprint` names the exact version the owner
// was shown; the server refuses to publish anything else under that confirmation.
export interface UpdatePreview {
    language: string;
    publishedBefore: boolean;
    fingerprint: string;
    diff: PublishDiff | null;
}

export type PublishTarget = 'telegram' | 'blog';

@Injectable({ providedIn: 'root' })
export class PostsService {
    private http = inject(HttpClient);

    export(draftId: string, chatId: string, format: PostFormat, language: PostLanguage, compressionLevel: CompressionLevel = 'standard',
           confirmedFingerprint?: string) {
        return firstValueFrom(this.http.post<{ messageId: number; chatId: string }>(
            '/api/posts/export', { draftId, chatId, format, language, compressionLevel, confirmedFingerprint }));
    }

    updatePreview(draftId: string, kind: PublishTarget, language: PostLanguage, chatId?: string) {
        return firstValueFrom(this.http.post<UpdatePreview>('/api/posts/update-preview', { draftId, kind, language, chatId }));
    }

    schedule(draftId: string, chatId: string, scheduledAtUtc: string, format: PostFormat, language: PostLanguage) {
        return firstValueFrom(this.http.post<{ id: string }>(
            '/api/posts/schedule', { draftId, chatId, scheduledAtUtc, format, language }));
    }

    listScheduled() {
        return firstValueFrom(this.http.get<ScheduledPost[]>('/api/posts/scheduled'));
    }

    cancelScheduled(id: string) {
        return firstValueFrom(this.http.delete(`/api/posts/scheduled/${id}`));
    }

    /**
     * The per-post growth series (8.6). Empty until the nightly job has run twice — nothing
     * recorded these numbers before 01.08.2026, so old posts have no history to show.
     */
    statHistory(draftId: string, days = 30) {
        return firstValueFrom(this.http.get<{
            snapshots: { viewCount: number; likeCount: number; dislikeCount: number; commentCount: number; takenAt: string }[];
        }>(`/api/drafts/${draftId}/stat-history?days=${days}`));
    }
}
