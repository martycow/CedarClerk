import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Phase 13 / T-124 — the development planner (ADR-106/111). A sprint is a stretch of days with a
// number and a name; tasks join it through the `sprintId` they already carried.
//
// Mirrors Modules/IndieDev/SprintEndpoints.cs.

/**
 * Derived from the dates on every read, never stored (ADR-111). `finished` means the days have run
 * out — **not** that everything in it is done, and the planner keeps showing what is left.
 */
export type SprintState = 'current' | 'planned' | 'finished';

export interface Sprint {
    id: string;
    projectId: string;
    /** Sequential within the project, never reused — this is what the `S14` chip shows. */
    number: number;
    name: string;
    startsAt: string;
    endsAt: string;
    state: SprintState;
    taskCount: number;
    doneCount: number;
    /** Tasks inside it that are past their date. A sprint is never itself "overdue". */
    overdueCount: number;
}

export interface SaveSprintInput {
    name: string;
    startsAt: string;
    endsAt: string;
}

@Injectable({ providedIn: 'root' })
export class SprintsService {
    private http = inject(HttpClient);

    list(projectId: string) {
        return firstValueFrom(this.http.get<Sprint[]>(`/api/projects/${projectId}/sprints`));
    }

    create(projectId: string, input: SaveSprintInput) {
        return firstValueFrom(this.http.post<Sprint>(`/api/projects/${projectId}/sprints`, input));
    }

    update(id: string, input: SaveSprintInput) {
        return firstValueFrom(this.http.put<Sprint>(`/api/sprints/${id}`, input));
    }

    /** The tasks come loose rather than going with it (ADR-111). */
    remove(id: string) {
        return firstValueFrom(this.http.delete<void>(`/api/sprints/${id}`));
    }

    /**
     * T-158 (ADR-132) — the sprint's finished work assembled into a devlog draft: a real post the
     * author edits and publishes everywhere, same generator shape as the build's changelog.
     */
    createDevlog(id: string, title?: string) {
        return firstValueFrom(this.http.post<{ documentId: string; title: string; doneCount: number }>(
            `/api/sprints/${id}/devlog`, { title: title ?? null }));
    }
}

/** How far through its tasks a sprint is, 0–100. A sprint with no tasks reads as 0, not as done. */
export function sprintProgress(sprint: Pick<Sprint, 'taskCount' | 'doneCount'>): number {
    if (!sprint.taskCount) return 0;
    return Math.round((sprint.doneCount / sprint.taskCount) * 100);
}

/** `S14` — the chip on a task card. Short on purpose: it sits beside a due date on a small card. */
export function sprintChip(sprint: Pick<Sprint, 'number'>): string {
    return `S${sprint.number}`;
}
