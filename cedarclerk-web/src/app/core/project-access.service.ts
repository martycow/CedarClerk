import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CanvasRole } from './boards.service';

export interface ProjectAccess {
    role: CanvasRole;
    canWrite: boolean;
    archived: boolean;
}

/**
 * T-301 — what the signed-in account is to a project: its owner, or somebody it was shared with.
 * The shell needs this before it can draw a wall of tools, because a member's wall is not the
 * owner's — every screen but the canvas is still the owner's alone, and drawing all eight for a
 * member is what made seven of them answer 404.
 *
 * Cached per project id for the session. The answer only moves when somebody's role is changed,
 * which is not something this side can see happen; a stale "editor" costs a refused write, not a
 * granted one, because the server resolves access again on every call it serves.
 */
@Injectable({ providedIn: 'root' })
export class ProjectAccessService {
    private http = inject(HttpClient);

    /** Null while unknown — the shell draws nothing project-specific until an answer arrives. */
    private readonly cache = signal<ReadonlyMap<string, ProjectAccess | null>>(new Map());
    private readonly inFlight = new Map<string, Promise<ProjectAccess | null>>();

    accessFor(projectId: string): ProjectAccess | null {
        return this.cache().get(projectId) ?? null;
    }

    /** True once this project has been asked about, whatever the answer was. */
    knows(projectId: string): boolean {
        return this.cache().has(projectId);
    }

    /** Asks once per project id. Safe to call from a computed's dependency-free caller. */
    ensure(projectId: string) {
        if (projectId) void this.resolve(projectId);
    }

    /** The same answer as a promise, for a page that must know before it chooses its next call. */
    resolve(projectId: string): Promise<ProjectAccess | null> {
        if (!projectId) return Promise.resolve(null);
        if (this.cache().has(projectId)) return Promise.resolve(this.accessFor(projectId));
        let pending = this.inFlight.get(projectId);
        if (!pending) {
            pending = this.load(projectId);
            this.inFlight.set(projectId, pending);
        }
        return pending;
    }

    private async load(projectId: string): Promise<ProjectAccess | null> {
        let access: ProjectAccess | null = null;
        try {
            access = await firstValueFrom(this.http.get<ProjectAccess>(`/api/projects/${projectId}/access`));
        } catch {
            // A 404 is a real answer: this account is nothing to this project. Stored as null so
            // the question is not asked again on every navigation.
        } finally {
            this.inFlight.delete(projectId);
            this.cache.update(map => new Map(map).set(projectId, access));
        }
        return access;
    }

    /** After accepting an invitation, the old answer for that project is a lie. */
    forget(projectId: string) {
        this.cache.update(map => {
            const next = new Map(map);
            next.delete(projectId);
            return next;
        });
    }
}
