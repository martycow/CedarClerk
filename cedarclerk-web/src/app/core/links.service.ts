import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * Wave 2 item 16 — tracked short links. Clicks are counters, never visit logs (house privacy
 * stance), and every hit counts, bots included — the UI says so rather than pretending otherwise.
 */
export interface TrackedLink {
    id: string;
    code: string;
    url: string;
    draftId: string | null;
    network: string | null;
    createdAt: string;
    clickCount: number;
    lastClickAt: string | null;
    shortUrl?: string;
}

@Injectable({ providedIn: 'root' })
export class LinksService {
    private http = inject(HttpClient);

    /** Reuse-or-create for an identical (url, draftId, network) — creating twice returns one row. */
    create(url: string, draftId?: string, network?: string) {
        return firstValueFrom(this.http.post<{ code: string; shortUrl: string }>(
            '/api/links', { url, draftId: draftId ?? null, network: network ?? null }));
    }

    listForDraft(draftId: string) {
        return firstValueFrom(this.http.get<TrackedLink[]>(`/api/links?draftId=${draftId}`));
    }

    /** The redirect URL for a code — used when a listing row arrives without its absolute form. */
    shortUrlOf(link: { code: string; shortUrl?: string }): string {
        return link.shortUrl ?? `${location.origin}/l/${link.code}`;
    }
}
