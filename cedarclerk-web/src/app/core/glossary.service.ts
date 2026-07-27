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
}
