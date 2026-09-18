import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { AuthService } from './auth.service';
import { WorkspaceObject } from './workspace-context.service';

export type AiOperationStatus = 'running' | 'review' | 'applied' | 'failed' | 'cancelled' | 'undone';

/** What the run was asked to do. Matches the AI features that exist today (ADR-301 clause 5). */
export type AiOperationKind = 'edit' | 'translate' | 'glossary' | 'profile';

export interface AiOperationChange {
    objectId: string;
    title: string;
    /** False means the run looked at the object and left it alone — worth showing, not hiding. */
    changed: boolean;
    note?: string;
    /** The revision an Undo would restore; absent means this change cannot be undone. */
    revisionId?: string;
}

export interface AiOperation {
    id: string;
    kind: AiOperationKind;
    /** The user's own words for the task, as the panel showed them before the run. */
    task: string;
    status: AiOperationStatus;
    startedAt: string;
    finishedAt?: string;
    scope: readonly WorkspaceObject[];
    protectedFields: readonly string[];
    changes: readonly AiOperationChange[];
    model?: string;
    credits?: number;
    error?: string;
}

const STORAGE_PREFIX = 'cedar-ai-operations';
const MAX_KEPT = 100;

function storageKey(owner: string | null): string {
    return `${STORAGE_PREFIX}:${owner ?? 'anonymous'}`;
}

// ADR-301 clause 5 — the record every AI run leaves. Client-side for now: the surface has to earn
// its shape before an entity and a migration are worth spending on. Scoped per account so a second
// sign-in on a shared browser never reads the first one's history.
@Injectable({ providedIn: 'root' })
export class AiOperationsService {
    private readonly auth = inject(AuthService);
    private readonly items = signal<readonly AiOperation[]>([]);
    private loadedOwner: string | null | undefined;

    readonly operations = this.items.asReadonly();

    readonly running = computed(() => this.operations().filter(op => op.status === 'running'));

    readonly creditsSpent = computed(() =>
        this.operations().reduce((total, op) => total + (op.credits ?? 0), 0));

    // An effect rather than a read-time load: a signal written inside a `computed` is NG0600, and
    // the owner is the only thing that decides which log is the right one.
    constructor() {
        effect(() => {
            const owner = this.auth.userEmail();
            untracked(() => this.loadFor(owner));
        });
    }

    find(id: string): AiOperation | undefined {
        return this.operations().find(op => op.id === id);
    }

    /** Opens the record before the work starts, so a run that never answers is still visible. */
    start(seed: Omit<AiOperation, 'id' | 'status' | 'startedAt' | 'changes'>
        & Partial<Pick<AiOperation, 'changes'>>): AiOperation {
        const operation: AiOperation = {
            ...seed,
            id: `op-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`,
            status: 'running',
            startedAt: new Date().toISOString(),
            changes: seed.changes ?? [],
        };
        this.items.update(list => [operation, ...list].slice(0, MAX_KEPT));
        this.persist();
        return operation;
    }

    update(id: string, patch: Partial<Omit<AiOperation, 'id'>>): void {
        this.items.update(list => list.map(op => op.id === id ? { ...op, ...patch } : op));
        this.persist();
    }

    finish(id: string, status: AiOperationStatus, patch: Partial<Omit<AiOperation, 'id' | 'status'>> = {}): void {
        this.update(id, { ...patch, status, finishedAt: new Date().toISOString() });
    }

    remove(id: string): void {
        this.items.update(list => list.filter(op => op.id !== id));
        this.persist();
    }

    clear(): void {
        this.items.set([]);
        this.persist();
    }

    /** An operation is undoable only where a real restore target exists (ADR-301 clause 6). */
    canUndo(operation: AiOperation): boolean {
        return operation.status === 'applied'
            && operation.changes.some(change => change.changed && !!change.revisionId);
    }

    private loadFor(owner: string | null): void {
        if (owner === this.loadedOwner) return;
        this.loadedOwner = owner;
        this.items.set(this.read(owner));
    }

    private read(owner: string | null): readonly AiOperation[] {
        try {
            const raw = localStorage.getItem(storageKey(owner));
            const parsed: unknown = raw ? JSON.parse(raw) : [];
            return Array.isArray(parsed) ? parsed as AiOperation[] : [];
        } catch {
            // A corrupt or foreign blob starts an empty log rather than breaking the screen.
            return [];
        }
    }

    private persist(): void {
        try {
            localStorage.setItem(storageKey(this.loadedOwner ?? null), JSON.stringify(this.items()));
        } catch {
            // Storage full or blocked — the signal stays the source of truth for this session.
        }
    }
}
