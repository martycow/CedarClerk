import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';

// Phase 8 Step 8, docs/ROADMAP.md — neither AI provider streams, so there's no way to signal
// real progress; this is purely a "don't let a stuck call hang forever" ceiling, enforced
// client-side (unsubscribing aborts the underlying HTTP request — see editor.component.ts's
// aiEdit()/runAutoTranslate()). Independent of the tighter server-side Consts.Anthropic.RequestTimeout
// (60s) — this is the user-facing cap across whichever provider/path ends up handling the call.
export const AI_OPERATION_TIMEOUT_MS = 180_000;

export interface ScheduledInfo { scheduledAtUtc: string; chatId: string; status: string; error: string | null; }
export interface DraftMeta {
    id: string;
    title: string;
    createdAt: string;
    updatedAt: string;
    blogSlug: string | null;
    isBlogPublished: boolean;
    blogPublishedAt: string | null;
    languages: string[]; // translation languages that exist ("en"), primary (RU) is implicit
    tags: string; // comma-separated lowercase tags, shared across language versions
    isArchived: boolean;
    lastTelegramMessageId: number | null;
    lastTelegramUsername: string | null;
    staleLanguages: string[]; // subset of `languages` whose translation predates the last RU edit
    scheduled: ScheduledInfo | null; // most recent Pending/Failed ScheduledPost row, if any
}
export interface TranslationMeta { language: string; title: string; updatedAt: string; }
export interface TranslationFull extends TranslationMeta { cedarJson: string; sourceSnapshotJson: string | null; }
export interface DraftFull extends DraftMeta { cedarJson: string; translations: TranslationMeta[]; }
export type AiEditKind = 'fix-errors' | 'schizo';
export interface AiEditResult { title: string; cedarJson: string; updatedAt: string; }

@Injectable({ providedIn: 'root' })
export class DraftsService {
    private http = inject(HttpClient);

    list() { 
        return firstValueFrom(this.http.get<DraftMeta[]>('/api/drafts')); 
    }

    get(id: string) { 
        return firstValueFrom(this.http.get<DraftFull>(`/api/drafts/${id}`)); 
    }

    create(title: string, cedarJson: string) {
        return firstValueFrom(this.http.post<{ id: string }>('/api/drafts', { title, cedarJson }));
    }

    update(id: string, title: string, cedarJson: string) {
        return firstValueFrom(this.http.put(`/api/drafts/${id}`, { title, cedarJson }));
    }

    remove(id: string) {
         return firstValueFrom(this.http.delete(`/api/drafts/${id}`));
    }

    archive(id: string) {
        return firstValueFrom(this.http.post<{ isArchived: boolean }>(`/api/drafts/${id}/archive`, {}));
    }

    unarchive(id: string) {
        return firstValueFrom(this.http.post<{ isArchived: boolean }>(`/api/drafts/${id}/unarchive`, {}));
    }

    updateTags(id: string, tags: string) {
        return firstValueFrom(this.http.put<{ tags: string }>(`/api/drafts/${id}/tags`, { tags }));
    }

    listTagUsage() {
        return firstValueFrom(this.http.get<{ tag: string; count: number }[]>('/api/drafts/tags'));
    }

    getTranslation(id: string, lang: string) {
        return firstValueFrom(this.http.get<TranslationFull>(`/api/drafts/${id}/translations/${lang}`));
    }

    saveTranslation(id: string, lang: string, title: string, cedarJson: string) {
        return firstValueFrom(this.http.put<{ language: string; updatedAt: string; sourceSnapshotJson: string | null }>(
            `/api/drafts/${id}/translations/${lang}`, { title, cedarJson }));
    }

    removeTranslation(id: string, lang: string) {
        return firstValueFrom(this.http.delete(`/api/drafts/${id}/translations/${lang}`));
    }

    // Returns the raw Observable (not a Promise) so the caller can unsubscribe to cancel the
    // in-flight request (Step 8's cancel button) — firstValueFrom's underlying subscription isn't
    // reachable for that once wrapped. timeout() below caps how long an unsubscribed caller would
    // otherwise wait indefinitely.
    autoTranslate$(id: string, lang: string) {
        return this.http.post<TranslationFull>(`/api/drafts/${id}/translations/${lang}/auto`, {})
            .pipe(timeout(AI_OPERATION_TIMEOUT_MS));
    }

    aiEdit$(id: string, lang: string, kind: AiEditKind) {
        return this.http.post<AiEditResult>(`/api/drafts/${id}/ai-edit/${lang}/${kind}`, {})
            .pipe(timeout(AI_OPERATION_TIMEOUT_MS));
    }

    importCedar(file: File) {
        const formData = new FormData();
        formData.append('file', file);
        return firstValueFrom(this.http.post<{ id: string }>('/api/drafts/import', formData));
    }

    importMarkdown(file: File) {
        const formData = new FormData();
        formData.append('file', file);
        return firstValueFrom(this.http.post<{ id: string; unmatchedImages: string[] }>('/api/drafts/import-markdown', formData));
    }

    publishToBlog(id: string) {
        return firstValueFrom(this.http.post<{ slug: string; url: string }>(`/api/drafts/${id}/publish-blog`, {}));
    }

    unpublishFromBlog(id: string) {
        return firstValueFrom(this.http.post(`/api/drafts/${id}/unpublish-blog`, {}));
    }
}