import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { SortDirection } from './collection-query';

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
export type LibrarySort = 'name' | 'type' | 'size' | 'added';
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

/**
 * ADR-238 — one file's facts, asked for when a writer selects one picture. `width`/`height` are
 * null for audio, for video and for anything whose header would not read; they are never `0`.
 */
export interface AssetMeta {
    id: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
    width: number | null;
    height: number | null;
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
    list(query: {
        q?: string;
        type?: LibraryKind | null;
        project?: string | null;
        sort?: LibrarySort;
        direction?: SortDirection;
        skip: number;
        take: number;
    }) {
        const params: Record<string, string | number> = { skip: query.skip, take: query.take };
        if (query.q) params['q'] = query.q;
        if (query.type) params['type'] = query.type;
        if (query.project) params['project'] = query.project;
        if (query.sort) params['sort'] = query.sort;
        if (query.direction) params['direction'] = query.direction;
        return firstValueFrom(this.http.get<LibraryPage>('/api/assets', { params }));
    }

    /**
     * By asset id, or by `Asset.LocalPath` for the documents written before nodes carried an id.
     * A miss answers null rather than throwing: an unknown file is a fact the inspector omits, and
     * a media node may legitimately point somewhere this owner has no asset for at all.
     */
    meta(query: { id?: string | null; path?: string | null }): Promise<AssetMeta | null> {
        const params: Record<string, string> = {};
        if (query.id) params['id'] = query.id;
        else if (query.path) params['path'] = query.path;
        if (!params['id'] && !params['path']) return Promise.resolve(null);
        return firstValueFrom(this.http.get<AssetMeta>('/api/assets/meta', { params }))
            .catch(() => null);
    }

    // 409 means "still referenced" — the body carries usedBy: [{draftId, title}] for the modal.
    remove(id: string) {
        return firstValueFrom(this.http.delete(`/api/assets/${id}`));
    }
}
