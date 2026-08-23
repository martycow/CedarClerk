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
    /** Matching ignores case unless this is on — "IT" the industry vs "it" the pronoun. */
    isCaseSensitive: boolean;
    /** The root of the translation group: every language version of one idea shares it. */
    sourceTermId: string | null;
    /** T-125 — the project this term belongs to, or null for a global one. */
    projectId: string | null;
    updatedAt: string;
}

export interface GlossaryTermInput {
    term: string;
    description: string;
    aliases: string;
    imageUrl: string | null;
    language: string;
    isCaseSensitive: boolean;
    /** Null keeps the term global, which is what every term written before T-125 is. */
    projectId?: string | null;
}

export interface DraftGlossaryTerm {
    id: string;
    term: string;
    excluded: boolean;
}

@Injectable({ providedIn: 'root' })
export class GlossaryService {
    private http = inject(HttpClient);

    list() {
        return firstValueFrom(this.http.get<GlossaryTerm[]>('/api/glossary'));
    }

    /**
     * What a document in this project renders with: the project's own terms **plus** the global
     * ones. Not "or" — "Unity" is global, "the ferry" is about one game, and an article about that
     * game needs both.
     */
    listForProject(projectId: string) {
        return firstValueFrom(this.http.get<GlossaryTerm[]>(`/api/glossary?projectId=${projectId}`));
    }

    listForDraft(draftId: string, language: string) {
        return firstValueFrom(this.http.get<DraftGlossaryTerm[]>(
            `/api/glossary/for-draft/${draftId}/${language}`));
    }

    setDraftTerm(draftId: string, language: string, termId: string, excluded: boolean) {
        return firstValueFrom(this.http.put<void>(
            `/api/glossary/for-draft/${draftId}/${language}/${termId}`, { excluded }));
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

    /** T-040 — proposed Russian forms for a term; the author accepts them into the alias field. */
    suggestForms(term: string, language: string) {
        return firstValueFrom(this.http.post<{ forms: string[] }>('/api/glossary/suggest-forms', { term, language }));
    }
}
