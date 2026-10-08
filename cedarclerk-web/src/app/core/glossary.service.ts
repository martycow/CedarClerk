import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// ADR-320 — an entry is one term; each language it is written in is a row of its own. A document
// is matched against the rows of its own language only.
export interface GlossaryEntryLanguage {
    /** The id per-document exclusions and usage counts are kept under. */
    id: string;
    language: string;
    /** Blank means the entry's own name. */
    localizedName: string;
    // Russian inflects, so the canonical form alone would miss most occurrences of a term in a
    // Russian post.
    spellings: string[];
    /** Blank means the entry's own description. */
    localizedDescription: string;
    /**
     * ADR-238 — documents this row appears in. Carried by `GET /api/glossary` and by nothing
     * else, so it is null on an entry that has just come back from a create or an update; a count
     * drawn from an absent one would be a number the screen made up.
     */
    usedInDrafts?: number | null;
    /**
     * Another row that wins this one's spelling. The scan is a single non-overlapping pass, so
     * only one of two terms sharing a spelling is ever credited — without this the loser's `0`
     * would be indistinguishable from "nothing uses this", and the two call for opposite actions.
     */
    shadowedByTermId?: string | null;
}

export interface GlossaryEntry {
    id: string;
    name: string;
    description: string;
    imageUrl: string | null;
    /** Matching ignores case unless this is on — "IT" the industry vs "it" the pronoun. */
    isCaseSensitive: boolean;
    /** The project this entry belongs to, or null for a global one. */
    projectId: string | null;
    updatedAt: string;
    languages: GlossaryEntryLanguage[];
}

export interface GlossaryLanguageInput {
    language: string;
    localizedName: string;
    spellings: string[];
    localizedDescription: string;
}

export interface GlossaryEntryInput {
    name: string;
    description: string;
    imageUrl: string | null;
    isCaseSensitive: boolean;
    projectId?: string | null;
    languages: GlossaryLanguageInput[];
}

/**
 * One entry in one language with the blanks filled from the entry — what the scanner matches and
 * what a reader's tooltip shows. `id` is the language row's.
 */
export interface GlossaryTerm {
    id: string;
    entry: GlossaryEntry;
    term: string;
    description: string;
    spellings: string[];
    imageUrl: string | null;
    language: string;
    isCaseSensitive: boolean;
    projectId: string | null;
    usedInDrafts?: number | null;
    shadowedByTermId?: string | null;
}

export function glossaryTerms(entries: readonly GlossaryEntry[]): GlossaryTerm[] {
    return entries.flatMap(entry => entry.languages.map(row => ({
        id: row.id,
        entry,
        term: row.localizedName.trim() || entry.name,
        description: row.localizedDescription.trim() || entry.description,
        spellings: row.spellings,
        imageUrl: entry.imageUrl,
        language: row.language,
        isCaseSensitive: entry.isCaseSensitive,
        projectId: entry.projectId,
        usedInDrafts: row.usedInDrafts,
        shadowedByTermId: row.shadowedByTermId,
    })));
}

/** Mirrors CreditPacks.AiSmallCost and CreditPacks.AiImageDescribeCost. */
export const GLOSSARY_AI_COST = { text: 1, image: 2 } as const;

/** The spellings field is typed as one comma-separated line and stored as a list. */
export function parseSpellings(text: string): string[] {
    return text.split(',').map(s => s.trim()).filter(Boolean);
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
        return firstValueFrom(this.http.get<GlossaryEntry[]>('/api/glossary'));
    }

    /**
     * What a document in this project renders with: the project's own entries **plus** the global
     * ones. Not "or" — "Unity" is global, "the ferry" is about one game, and an article about that
     * game needs both.
     */
    listForProject(projectId: string) {
        return firstValueFrom(this.http.get<GlossaryEntry[]>(`/api/glossary?projectId=${projectId}`));
    }

    listForDraft(draftId: string, language: string) {
        return firstValueFrom(this.http.get<DraftGlossaryTerm[]>(
            `/api/glossary/for-draft/${draftId}/${language}`));
    }

    setDraftTerm(draftId: string, language: string, termId: string, excluded: boolean) {
        return firstValueFrom(this.http.put<void>(
            `/api/glossary/for-draft/${draftId}/${language}/${termId}`, { excluded }));
    }

    create(input: GlossaryEntryInput) {
        return firstValueFrom(this.http.post<GlossaryEntry>('/api/glossary', input));
    }

    update(id: string, input: GlossaryEntryInput) {
        return firstValueFrom(this.http.put<GlossaryEntry>(`/api/glossary/${id}`, input));
    }

    remove(id: string) {
        return firstValueFrom(this.http.delete(`/api/glossary/${id}`));
    }

    /**
     * ADR-320 clause 4 — the term as `targetLanguage` writes it, with the word forms that
     * language inflects it into. A proposal for the form; nothing is stored. One credit.
     */
    aiTranslate(name: string, description: string, targetLanguage: string) {
        return firstValueFrom(this.http.post<GlossaryLanguageInput>(
            '/api/glossary/ai/translate', { name, description, targetLanguage }));
    }

    /**
     * ADR-320 clause 5 — a description written from the term and, when the entry has one, its
     * image. `credits` is what the call cost: more with an image than without.
     */
    aiDescribe(name: string, language: string, imageUrl: string | null) {
        return firstValueFrom(this.http.post<{ description: string; credits: number }>(
            '/api/glossary/ai/describe', { name, language, imageUrl }));
    }

    /**
     * Every entry that has `sourceLanguage` and lacks `targetLanguage` gains it, for one credit.
     * `skipped` counts entries whose translation came back unusable.
     */
    translateAll(sourceLanguage: string, targetLanguage: string) {
        return firstValueFrom(this.http.post<{ added: number; skipped: number }>(
            '/api/glossary/translate-all', { sourceLanguage, targetLanguage }));
    }

    /** Proposed Russian forms for a term; the author accepts them into the spellings field. */
    suggestForms(term: string, language: string) {
        return firstValueFrom(this.http.post<{ forms: string[] }>('/api/glossary/suggest-forms', { term, language }));
    }
}
