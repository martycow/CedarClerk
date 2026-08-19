import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { IconName } from '../shared/icon-data.generated';

// Phase 13 / T-123 — the task tracker (ADR-106). A task is its own entity, not a document type:
// its content is a set of fields that get filtered, sorted and counted, and the text is only one
// of them. Mirrors Modules/IndieDev/TaskEndpoints.cs.

/** One of CedarClerk.Core.TaskStatuses — also the board's left-to-right column order. */
export type TaskStatus = 'backlog' | 'planned' | 'in_progress' | 'done';
export const TASK_STATUSES: TaskStatus[] = ['backlog', 'planned', 'in_progress', 'done'];

/** 1–3, shown as P1/P2/P3. Sorted by, hence a number: "high/medium/low" sorts high after low. */
export type TaskPriority = 1 | 2 | 3;
export const TASK_PRIORITIES: TaskPriority[] = [1, 2, 3];

/** One of CedarClerk.Core.LinkTargets. */
export type LinkTarget = 'document' | 'asset' | 'task';

export const LINK_TARGET_ICONS: Record<LinkTarget, IconName> = {
    document: 'file-text',
    asset: 'image',
    task: 'check-square',
};

export interface TaskLink {
    type: LinkTarget;
    id: string;
    /** Empty when the target is gone — the chip says so rather than rendering blank. */
    label: string;
}

export interface GameTask {
    id: string;
    projectId: string;
    title: string;
    status: TaskStatus;
    priority: TaskPriority;
    description: string;
    assignee: string;
    /** T-124 — the sprint this task is planned into, or null. */
    sprintId: string | null;
    /** T-126 — the version this task shipped in, or null. */
    buildId: string | null;
    dueAt: string | null;
    /** T-159 (ADR-134) — ticked tasks appear on the project's public showcase roadmap. */
    isPublicRoadmap: boolean;
    createdAt: string;
    updatedAt: string;
    completedAt: string | null;
    archivedAt: string | null;
    links: TaskLink[];
}

export interface CreateTaskInput {
    title: string;
    description?: string;
    status?: TaskStatus;
    priority?: TaskPriority;
    assignee?: string;
    dueAt?: string | null;
}

/**
 * Nullable everywhere: a field left out is a field left alone. The two that can be *cleared* need
 * their own flags — without them "no due date" and "don't touch the due date" are the same request.
 */
export interface UpdateTaskInput {
    title?: string;
    description?: string;
    status?: TaskStatus;
    priority?: TaskPriority;
    assignee?: string;
    dueAt?: string;
    clearDueAt?: boolean;
    sprintId?: string;
    clearSprint?: boolean;
    buildId?: string;
    clearBuild?: boolean;
    archived?: boolean;
    isPublicRoadmap?: boolean;
}

@Injectable({ providedIn: 'root' })
export class TasksService {
    private http = inject(HttpClient);

    /**
     * The whole board in one request. Tasks come in tens per project, and a paged kanban column
     * would be a column that lies about its own count.
     */
    list(projectId: string, includeArchived = false) {
        const query = includeArchived ? '?archived=true' : '';
        return firstValueFrom(this.http.get<GameTask[]>(`/api/projects/${projectId}/tasks${query}`));
    }

    create(projectId: string, input: CreateTaskInput) {
        return firstValueFrom(this.http.post<GameTask>(`/api/projects/${projectId}/tasks`, input));
    }

    get(id: string) {
        return firstValueFrom(this.http.get<GameTask>(`/api/tasks/${id}`));
    }

    update(id: string, input: UpdateTaskInput) {
        return firstValueFrom(this.http.put<GameTask>(`/api/tasks/${id}`, input));
    }

    remove(id: string) {
        return firstValueFrom(this.http.delete<void>(`/api/tasks/${id}`));
    }

    link(taskId: string, type: LinkTarget, id: string) {
        return firstValueFrom(this.http.post<void>(`/api/tasks/${taskId}/links`, { type, id }));
    }

    unlink(taskId: string, type: LinkTarget, id: string) {
        return firstValueFrom(this.http.delete<void>(`/api/tasks/${taskId}/links/${type}/${id}`));
    }
}

/**
 * Whether a deadline has passed. Compared by calendar day, not by instant: a task due today is not
 * overdue at 09:00 merely because it was stored as midnight.
 */
export function isOverdue(task: Pick<GameTask, 'dueAt' | 'status'>, now = new Date()): boolean {
    if (!task.dueAt || task.status === 'done') return false;
    const due = new Date(task.dueAt);
    const dueDay = new Date(due.getFullYear(), due.getMonth(), due.getDate());
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    return dueDay.getTime() < today.getTime();
}
