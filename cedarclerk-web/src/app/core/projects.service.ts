import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { IconName } from '../shared/icon-data.generated';
import { GameTask, TaskStatus } from './tasks.service';
import { Sprint } from './sprints.service';

// Phase 13 / T-120 — the indie-gamedev module. A project is a game; documents, tasks and assets
// live inside it. Tasks live in tasks.service.ts and the index in asset-index.service.ts; what is
// here is the project itself and its documents.
//
// Mirrors the module's server-side shape (Modules/IndieDev/ProjectEndpoints.cs). The whole module
// is behind a flag: `auth.modules().indieDev` decides whether any of this is reachable, and the
// endpoints simply are not mapped when it is off.

/** One of CedarClerk.Core.ProjectTypes — decides the project's starter document and nothing else. */
export type ProjectType = 'fullgame' | 'jam' | 'prototype' | 'released' | 'blog';
export const PROJECT_TYPES: ProjectType[] = ['fullgame', 'jam', 'prototype', 'released', 'blog'];

/** One of CedarClerk.Core.DocumentTypes. `post` is what every draft written before the module is. */
export type DocumentType = 'post' | 'design' | 'script' | 'plot' | 'changelog' | 'note';
export const DOCUMENT_TYPES: DocumentType[] = ['post', 'design', 'script', 'plot', 'changelog', 'note'];

// The type shows as an icon wherever a document is listed — never as a colour label (the design
// handoff makes this a product rule, so the mapping lives in one place).
export const DOCUMENT_TYPE_ICONS: Record<DocumentType, IconName> = {
    post: 'newspaper',
    design: 'book-open',
    script: 'film-slate',
    plot: 'tree-structure',
    changelog: 'list-numbers',
    note: 'note',
};

export const PROJECT_TYPE_ICONS: Record<ProjectType, IconName> = {
    fullgame: 'game-controller',
    jam: 'timer',
    prototype: 'flask',
    released: 'rocket-launch',
    blog: 'newspaper',
};

/** Which document type a project of each type starts with — mirrors ProjectTypes.StarterDocumentType. */
export const STARTER_DOCUMENT_TYPE: Record<ProjectType, DocumentType> = {
    fullgame: 'design',
    jam: 'design',
    prototype: 'note',
    released: 'changelog',
    blog: 'post',
};

export interface ProjectSummary {
    id: string;
    name: string;
    description: string;
    projectType: ProjectType;
    coverUrl: string | null;
    createdAt: string;
    archivedAt: string | null;
    documentCount: number;
    /** Open tasks, not every task ever written — see ProjectEndpoints for why. */
    openTaskCount: number;
    assetCount: number;
    /** Newest edit to any of the project's documents — the project row itself never moves. */
    lastActivityAt: string;
}

export interface ProjectDocument {
    id: string;
    title: string;
    documentType: DocumentType;
    updatedAt: string;
    isArchived: boolean;
    isBlogPublished: boolean;
}

export interface ProjectDetail extends Omit<ProjectSummary, 'documentCount' | 'openTaskCount' | 'assetCount' | 'lastActivityAt'> {
    /** T-159 (ADR-134) — null means no public page. */
    showcaseSlug: string | null;
    /** One `Label|https://url` per line. */
    showcaseLinks: string;
    /** ADR-216 — gallery images, one `/media/...` path per line, at most 12. */
    showcaseGallery: string;
    /** A YouTube link, or null. */
    showcaseTrailerUrl: string | null;
    /** T-300 — the project's own domain, serving the showcase at its root. Null = subdomain only. */
    customDomain: string | null;
    // Wave 1 item 6 — the /press page's facts. All optional; an empty field is a section the
    // page simply omits. FactsheetRows is newline-separated "Label: value" lines.
    pressContactEmail: string | null;
    pressPrice: string | null;
    pressEngine: string | null;
    pressGenre: string | null;
    pressFactsheetRows: string | null;
    documents: ProjectDocument[];
    /** T-123 — the dashboard's right rail, already sorted by urgency on the server. */
    upNext: GameTask[];
    /** Open tasks per status; a status with none is simply absent. */
    taskCounts: Partial<Record<TaskStatus, number>>;
    /** T-124 — the sprint covering today, or null when none does. */
    currentSprint: Sprint | null;
    openTaskCount: number;
}

export interface ShowcaseInput {
    enabled: boolean;
    slug: string | null;
    links: string;
    gallery: string;
    trailerUrl: string | null;
    customDomain: string | null;
    pressContactEmail: string | null;
    pressPrice: string | null;
    pressEngine: string | null;
    pressGenre: string | null;
    pressFactsheetRows: string | null;
}

export interface ShowcaseStats {
    days: number;
    views: { day: string; count: number }[];
    viewTotal: number;
    /** One number per store-link label — "which link works", not "when". */
    clicks: { label: string; count: number }[];
    followerCount: number;
    pendingFollowerCount: number;
}

export interface CreateProjectInput {
    name: string;
    description?: string;
    projectType: ProjectType;
    /** Optional override; without it the server derives the type from projectType. */
    documentType?: DocumentType;
    /** The starter document's language — its skeleton headings follow it (ADR-133). */
    language?: string;
    /** The starter document's title. Sent by the client because the server has no second language. */
    documentTitle?: string;
}

@Injectable({ providedIn: 'root' })
export class ProjectsService {
    private http = inject(HttpClient);

    list(includeArchived = false) {
        const query = includeArchived ? '?archived=true' : '';
        return firstValueFrom(this.http.get<ProjectSummary[]>(`/api/projects${query}`));
    }

    get(id: string) {
        return firstValueFrom(this.http.get<ProjectDetail>(`/api/projects/${id}`));
    }

    create(input: CreateProjectInput) {
        return firstValueFrom(this.http.post<{ id: string; name: string; documentId: string }>('/api/projects', input));
    }

    /**
     * T-160 (ADR-133) — "Cedar Quest", a filled example: tasks across the board, a current sprint,
     * a released build and a devlog written from them. On demand from the empty state, never seeded.
     */
    createExample(language: string) {
        return firstValueFrom(this.http.post<{ id: string; name: string }>('/api/projects/example', { language }));
    }

    /** T-159 (ADR-134) — the public game page's switch; the server slugifies and answers the URL. */
    setShowcase(id: string, input: ShowcaseInput) {
        return firstValueFrom(this.http.put<{ showcaseSlug: string | null; url: string | null; customDomain: string | null }>(
            `/api/projects/${id}/showcase`, input));
    }

    /** T-296/T-297 — the public page's own counters, for the owner. */
    showcaseStats(id: string) {
        return firstValueFrom(this.http.get<ShowcaseStats>(`/api/projects/${id}/showcase/stats`));
    }

    update(id: string, name: string, description: string, coverUrl: string | null) {
        return firstValueFrom(this.http.put<ProjectSummary>(`/api/projects/${id}`, { name, description, coverUrl }));
    }

    setArchived(id: string, archived: boolean) {
        return firstValueFrom(this.http.post<{ id: string; archivedAt: string | null }>(`/api/projects/${id}/archive`, { archived }));
    }

    /** Documents are detached, not deleted — see ProjectEndpoints. */
    remove(id: string) {
        return firstValueFrom(this.http.delete<void>(`/api/projects/${id}`));
    }

    createDocument(projectId: string, documentType: DocumentType, title: string) {
        return firstValueFrom(
            this.http.post<{ id: string; title: string; documentType: DocumentType }>(
                `/api/projects/${projectId}/documents`, { documentType, title }));
    }

    /** Refused with 409 when it would leave the project with no documents at all (ADR-103). */
    detachDocument(projectId: string, draftId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/projects/${projectId}/documents/${draftId}`));
    }

    /** ADR-204 — files every unfiled picture this project's documents use. Idempotent. */
    refileAssets(projectId: string) {
        return firstValueFrom(this.http.post<{ filed: number }>(`/api/projects/${projectId}/assets/refile`, {}));
    }

    attachDocument(projectId: string, draftId: string) {
        return firstValueFrom(this.http.put<{ id: string; projectId: string }>(`/api/projects/${projectId}/documents/${draftId}`, {}));
    }

    setDocumentType(draftId: string, documentType: DocumentType) {
        return firstValueFrom(this.http.put<{ id: string; documentType: DocumentType }>(`/api/documents/${draftId}/type`, { documentType }));
    }
}

/**
 * Two uppercase initials for the striped cover placeholder — the design's stand-in until a project
 * has real cover art. Falls back to the first two letters of a single word, and to "?" for a name
 * that has no letters at all (an emoji-only name is a real thing a person will try).
 */
export function projectInitials(name: string): string {
    const words = name.trim().split(/\s+/).filter(w => w.length > 0);
    const letters = words.map(w => [...w][0]).filter(c => /\p{L}|\p{N}/u.test(c));
    if (letters.length >= 2) return (letters[0] + letters[1]).toUpperCase();
    const bare = [...name].filter(c => /\p{L}|\p{N}/u.test(c));
    if (bare.length >= 2) return (bare[0] + bare[1]).toUpperCase();
    return bare.length === 1 ? bare[0].toUpperCase() : '?';
}
