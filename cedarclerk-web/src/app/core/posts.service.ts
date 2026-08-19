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
    // ADR-099 — a schedule addresses a publish target now, so a row can be any network. `network`
    // is denormalised on the row; `targetName` is the channel title or the @handle behind it.
    targetId: string | null;
    network: string;
    targetName: string | null;
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

// ADR-096 — 'telegram' | 'blog' plus any network name: every destination the export window offers
// now asks the same "you are about to overwrite a live post" question before it sends.
export type PublishTarget = 'telegram' | 'blog' | 'bluesky' | 'x' | 'discord';

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

    /**
     * ADR-099 — one destination, named the way the caller can name it: `targetId` for any network,
     * `chatId` for the Telegram-shaped call. The server resolves either into a stored target.
     */
    schedule(draftId: string, scheduledAtUtc: string, language: PostLanguage,
             dest: { chatId?: string; targetId?: string }, format: PostFormat = 'Markdown') {
        return firstValueFrom(this.http.post<{ id: string }>(
            '/api/posts/schedule', { draftId, scheduledAtUtc, language, format, ...dest }));
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

    /**
     * T-086 — what a network will do to this document, before it is sent. Codes plus numbers; the
     * wording is the caller's, because Core has no dictionaries and this app has two.
     */
    validate(draftId: string, network: string, language?: string) {
        return firstValueFrom(this.http.post<{
            network: string;
            issues: { code: string; blocking: boolean; actual: number; limit: number }[];
        }>('/api/posts/validate', { draftId, network, language }));
    }
}
