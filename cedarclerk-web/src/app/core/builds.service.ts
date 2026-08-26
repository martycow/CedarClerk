import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Phase 13 / T-126 — build and version records (ADR-112).
//
// A build knows nothing about git: no repository tags, no CI, no artefacts. It is a record the
// author keeps of which version exists and what went into it. Mirrors
// Modules/IndieDev/BuildEndpoints.cs.

export interface BuildDocument {
    id: string;
    title: string;
}

export interface Build {
    id: string;
    projectId: string;
    /** Whatever the author calls it — "0.4.2", "Demo 3", "Steam Next Fest build". */
    version: string;
    notes: string;
    /** Null = planned but not out yet. Unreleased builds are listed first. */
    releasedAt: string | null;
    createdAt: string;
    released: boolean;
    /** T-299 — offered for download on the project's showcase. */
    isPublic: boolean;
    /** Where the file is; a link the author hosts, never bytes we keep. */
    downloadUrl: string | null;
    taskCount: number;
    doneCount: number;
    /** Documents attached to this version — its changelog, a devlog about it. */
    documents: BuildDocument[];
}

export interface SaveBuildInput {
    version: string;
    notes: string;
    releasedAt: string | null;
    isPublic: boolean;
    downloadUrl: string | null;
}

@Injectable({ providedIn: 'root' })
export class BuildsService {
    private http = inject(HttpClient);

    list(projectId: string) {
        return firstValueFrom(this.http.get<Build[]>(`/api/projects/${projectId}/builds`));
    }

    create(projectId: string, input: SaveBuildInput) {
        return firstValueFrom(this.http.post<Build>(`/api/projects/${projectId}/builds`, input));
    }

    update(id: string, input: SaveBuildInput) {
        return firstValueFrom(this.http.put<Build>(`/api/builds/${id}`, input));
    }

    /** Tasks and documents survive it — deleting the record of a version deletes no work. */
    remove(id: string) {
        return firstValueFrom(this.http.delete<void>(`/api/builds/${id}`));
    }

    /**
     * Produces a real changelog **document** from the build's finished tasks (ADR-112), which then
     * lives an ordinary document's life: edited, translated, published, versioned.
     */
    createChangelog(id: string, title?: string) {
        return firstValueFrom(this.http.post<{ documentId: string; title: string; taskCount: number }>(
            `/api/builds/${id}/changelog`, { title: title ?? null }));
    }
}
