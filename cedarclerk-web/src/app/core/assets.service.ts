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
}