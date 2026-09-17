import { Injectable, computed, signal } from '@angular/core';
import { IconName } from '../shared/icon-data.generated';

export type WorkspaceObjectKind = 'document' | 'asset' | 'task' | 'term' | 'post' | 'project';

export interface WorkspaceObject {
    id: string;
    kind: WorkspaceObjectKind;
    title: string;
    /** Short second line — a type, a language, a status. Drawn as written. */
    detail?: string;
    icon?: IconName;
}

export interface WorkspaceProperty {
    label: string;
    value: string;
    /** A property an AI operation may never rewrite — drawn with a lock and listed in the AI scope. */
    protected?: boolean;
}

export interface WorkspaceContextState {
    /** The screen's own name, for the panel's header. */
    surface: string;
    /** What is open: one object on an editor, none on a list. */
    open: WorkspaceObject | null;
    /** What is ticked. An AI operation's scope is exactly this, or `open` when nothing is ticked. */
    selection: readonly WorkspaceObject[];
    properties: readonly WorkspaceProperty[];
}

const EMPTY: WorkspaceContextState = { surface: '', open: null, selection: [], properties: [] };

// ADR-301 clause 4/5 — the session's scope, published by whichever page owns it and read by the
// inspector rail and the AI panel. The scope an operation runs over is computed here and nowhere
// else, so a panel can never widen it by asking a different question.
@Injectable({ providedIn: 'root' })
export class WorkspaceContextService {
    private readonly state = signal<WorkspaceContextState>(EMPTY);

    readonly surface = computed(() => this.state().surface);
    readonly open = computed(() => this.state().open);
    readonly selection = computed(() => this.state().selection);
    readonly properties = computed(() => this.state().properties);

    /** The objects an operation started now would touch. Never the whole project. */
    readonly scope = computed<readonly WorkspaceObject[]>(() => {
        const selected = this.state().selection;
        if (selected.length) return selected;
        const open = this.state().open;
        return open ? [open] : [];
    });

    readonly protectedFields = computed(() =>
        this.state().properties.filter(property => property.protected).map(property => property.label));

    set(state: Partial<WorkspaceContextState> & Pick<WorkspaceContextState, 'surface'>): void {
        this.state.set({ ...EMPTY, ...state });
    }

    patch(state: Partial<WorkspaceContextState>): void {
        this.state.update(current => ({ ...current, ...state }));
    }

    clear(): void {
        this.state.set(EMPTY);
    }
}
