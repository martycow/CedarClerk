import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Wave 1 item 2 — FTS5-backed document search. One hit per (draft, language): a translation whose
// body matches comes back as its own row, carrying the same draft id.
export interface DraftSearchHit {
    id: string;
    title: string;
    snippet: string;
    updatedAt: string;
    documentType: string;
    isBlogPublished: boolean;
}

@Injectable({ providedIn: 'root' })
export class SearchService {
    private http = inject(HttpClient);

    drafts(q: string, limit = 20) {
        return firstValueFrom(this.http.get<DraftSearchHit[]>('/api/search/drafts', {
            params: { q, limit },
        }));
    }
}
