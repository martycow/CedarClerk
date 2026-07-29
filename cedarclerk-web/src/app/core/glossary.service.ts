import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Idea #11 — terms the owner defines once and has explained wherever they appear on the blog.
// Per content language: the same word needs a different explanation depending on which language's
// version of a post the reader is on.
export interface GlossaryTerm {
    id: string;
    term: string;
    description: string;
    // Comma-separated other spellings. Russian inflects, so the canonical form alone would miss
    // most occurrences of a term in a Russian post.
    aliases: string;
    imageUrl: string | null;
    language: string;
    updatedAt: string;
}

export interface GlossaryTermInput {
    term: string;
    description: string;
    aliases: string;
    imageUrl: string | null;
    language: string;
}

@Injectable({ providedIn: 'root' })
export class GlossaryService {
    private http = inject(HttpClient);

    list() {
        return firstValueFrom(this.http.get<GlossaryTerm[]>('/api/glossary'));
    }

    create(input: GlossaryTermInput) {
        return firstValueFrom(this.http.post<GlossaryTerm>('/api/glossary', input));
    }

    update(id: string, input: GlossaryTermInput) {
        return firstValueFrom(this.http.put<GlossaryTerm>(`/api/glossary/${id}`, input));
    }

    remove(id: string) {
        return firstValueFrom(this.http.delete(`/api/glossary/${id}`));
    }

    // ADR-061 — machine-translates term+description into `targetLanguage` server-side; returns
    // the created (or refreshed) term in that language. Pro Plus + daily AI quota, like forms.
    translate(id: string, targetLanguage: string) {
        return firstValueFrom(this.http.post<GlossaryTerm>(`/api/glossary/${id}/translate`, { targetLanguage }));
    }

    // ADR-062 — every term of sourceLanguage into targetLanguage in one call (one quota call for
    // the whole language). `skipped` counts terms whose translation came back unusable.
    translateAll(sourceLanguage: string, targetLanguage: string) {
        return firstValueFrom(this.http.post<{ terms: GlossaryTerm[]; skipped: number }>(
            '/api/glossary/translate-all', { sourceLanguage, targetLanguage }));
    }
}
