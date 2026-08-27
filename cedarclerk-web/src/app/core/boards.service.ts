import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// T-301 / ADR-218 — the reference board ("Canvas"). REST owns boards, the read snapshot and the
// export; every *item* write goes over the hub instead (canvas-hub.service.ts), so there is one
// validator and one broadcast point rather than two. The wire shapes here are the frozen contract's
// and mirror Modules/IndieDev/CanvasEndpoints.cs.

export type CanvasRole = 'owner' | 'editor' | 'viewer';
export type CanvasItemKind = 'image' | 'note' | 'frame' | 'link';
export type CanvasBackground = 'grid' | 'dots' | 'blank';

export const CANVAS_BACKGROUNDS: readonly CanvasBackground[] = ['grid', 'dots', 'blank'];

export interface CanvasImagePayload { url: string; naturalWidth: number; naturalHeight: number; alt: string; }
export interface CanvasNotePayload { text: string; align: 'left' | 'center'; }
export interface CanvasFramePayload { title: string; }
export interface CanvasLinkPayload { url: string; title: string; favicon: string; }
export type CanvasPayload =
    CanvasImagePayload | CanvasNotePayload | CanvasFramePayload | CanvasLinkPayload;

export interface CanvasBoardSummary {
    id: string;
    projectId: string;
    name: string;
    background: CanvasBackground;
    itemCount: number;
    createdAt: string;
    updatedAt: string;
    version: number;
    /** Repeated on every response so no screen re-derives who may write. */
    canWrite: boolean;
}

export interface CanvasItem {
    id: string;
    boardId: string;
    kind: CanvasItemKind;
    x: number;
    y: number;
    width: number;
    height: number;
    rotation: number;
    z: number;
    color: string;
    payload: CanvasPayload;
    createdBy: string;
    createdAt: string;
    updatedBy: string;
    updatedAt: string;
    /** The server's own counter. It is not a lock — it is how an out-of-order echo is recognised. */
    version: number;
}

/** What one person is doing on a board right now. Never persisted — see ADR-218. */
export interface CanvasPeer {
    connectionId: string;
    userId: string;
    name: string;
    colorIndex: number;
    role: CanvasRole;
}

export interface CanvasSnapshot {
    board: CanvasBoardSummary;
    /** In here because a member cannot call GET /api/projects/{id} — the rail has no other source. */
    projectName: string;
    items: CanvasItem[];
    role: CanvasRole;
    canWrite: boolean;
    /** Only the hub's Join answers this; the REST snapshot leaves it out. */
    peers?: CanvasPeer[];
}

/** Geometry alone — what a live drag broadcasts and what a resize commits. */
export interface CanvasGeometry {
    id: string;
    x: number;
    y: number;
    width: number;
    height: number;
    rotation: number;
}

export interface CanvasItemPatch {
    id: string;
    x?: number;
    y?: number;
    width?: number;
    height?: number;
    rotation?: number;
    color?: string;
    payload?: CanvasPayload;
}

/** An item on its way to the board. The id is made on this side so the optimistic row and the
    echoed one are the same row — a retried send after a reconnect must not double the note. */
export interface NewCanvasItem {
    id?: string;
    kind: CanvasItemKind;
    x: number;
    y: number;
    width: number;
    height: number;
    rotation?: number;
    color?: string;
    payload: CanvasPayload;
}

@Injectable({ providedIn: 'root' })
export class BoardsService {
    private http = inject(HttpClient);

    list(projectId: string) {
        return firstValueFrom(this.http.get<CanvasBoardSummary[]>(`/api/projects/${projectId}/canvas`));
    }

    create(projectId: string, input: { name: string; background?: CanvasBackground }) {
        return firstValueFrom(this.http.post<CanvasBoardSummary>(`/api/projects/${projectId}/canvas`, input));
    }

    snapshot(boardId: string) {
        return firstValueFrom(this.http.get<CanvasSnapshot>(`/api/canvas/${boardId}`));
    }

    update(boardId: string, input: { name?: string; background?: CanvasBackground }) {
        return firstValueFrom(this.http.put<CanvasBoardSummary>(`/api/canvas/${boardId}`, input));
    }

    remove(boardId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/canvas/${boardId}`));
    }

    /** The read-only escape hatch: a board is never hostage to a socket. */
    exportUrl(boardId: string) {
        return `/api/canvas/${boardId}/export`;
    }
}
