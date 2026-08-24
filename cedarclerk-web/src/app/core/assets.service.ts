import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

export interface DraftAsset {
    id: string;
    fileName: string;
    localPath: string;
    contentType: string;
    sizeBytes: number;
    hasTelegramDerivative: boolean;
    telegramSizeBytes: number | null;
}

// ADR-127 — the owner-wide library behind /media.
export type LibraryKind = 'image' | 'video' | 'audio';
export interface LibraryAsset {
    id: string;
    fileName: string;
    localPath: string;
    contentType: string;
    sizeBytes: number;
    createdAt: string;
    /** ADR-204 — the project whose documents use this file; null means it belongs to none. */
    projectId: string | null;
}

/** One row per bucket, counted over every file the owner has rather than over the filtered page. */
export interface LibraryBucket {
    projectId: string | null;
    count: number;
}
export interface LibraryPage {
    items: LibraryAsset[];
    total: number;
    counts: Record<LibraryKind, number>;
    buckets: LibraryBucket[];
    usedBytes: number;
    limitBytes: number;
}

@Injectable({ providedIn: 'root' })
export class AssetsService {
    private http = inject(HttpClient);

    uploadWithProgress(file: File): Observable<HttpEvent<{ id: string; url: string }>> {
        const fd = new FormData();
        fd.append('file', file);
        return this.http.post<{ id: string; url: string }>('/api/assets', fd, {
            reportProgress: true,
            observe: 'events',
        });
    }

    // Plain upload for callers that don't draw a progress bar (IF1's avatar picker) — the editor
    // keeps using uploadWithProgress, where a large video makes progress worth showing.
    upload(file: File) {
        const fd = new FormData();
        fd.append('file', file);
        return firstValueFrom(this.http.post<{ id: string; url: string }>('/api/assets', fd));
    }

    listForDraft(draftId: string) {
        return firstValueFrom(this.http.get<DraftAsset[]>(`/api/drafts/${draftId}/assets`));
    }

    /** `project`: a project id, the literal 'none' for the unfiled bucket, or null for every one. */
    list(query: { q?: string; type?: LibraryKind | null; project?: string | null; skip: number; take: number }) {
        const params: Record<string, string | number> = { skip: query.skip, take: query.take };
        if (query.q) params['q'] = query.q;
        if (query.type) params['type'] = query.type;
        if (query.project) params['project'] = query.project;
        return firstValueFrom(this.http.get<LibraryPage>('/api/assets', { params }));
    }

    // 409 means "still referenced" — the body carries usedBy: [{draftId, title}] for the modal.
    remove(id: string) {
        return firstValueFrom(this.http.delete(`/api/assets/${id}`));
    }
}