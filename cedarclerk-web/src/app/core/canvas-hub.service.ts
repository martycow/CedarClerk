import { Injectable, OnDestroy, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import {
    CanvasBoardSummary, CanvasGeometry, CanvasItem, CanvasItemPatch, CanvasPeer, CanvasRole,
    CanvasSnapshot, NewCanvasItem,
} from './boards.service';

export type HubStatus = 'idle' | 'connecting' | 'live' | 'reconnecting' | 'offline';

/** Render order, and it is total: two rows may share a z under concurrency and still sort the same
    way in every client. */
export function sortCanvasItems(items: readonly CanvasItem[]): CanvasItem[] {
    return [...items].sort((a, b) =>
        a.z - b.z || a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id));
}

/**
 * Last-writer-wins, resolved by the server's counter. An echo older than the row already held is
 * an out-of-order delivery, or our own optimistic row that a newer answer has already overtaken —
 * dropping it is the whole reason `version` is on the wire.
 */
export function mergeCanvasItems(
    current: readonly CanvasItem[], incoming: readonly CanvasItem[]): CanvasItem[] {
    const byId = new Map(current.map(i => [i.id, i]));
    for (const next of incoming) {
        const held = byId.get(next.id);
        if (!held || next.version >= held.version) byId.set(next.id, next);
    }
    return sortCanvasItems([...byId.values()]);
}

/**
 * How long a peer's ghost outlives the last frame it was painted from. A drag broadcasts about
 * thirty times a second, so a gap this long can only mean the drag has ended — and it does not
 * always end in a message: a pointer-up whose write is refused (a role withdrawn mid-drag, an
 * archived project) broadcasts nothing at all, and the item would otherwise stay painted at a
 * position that exists on no other screen and in no row.
 */
const GHOST_IDLE_MS = 1000;

function newId(): string {
    return crypto.randomUUID
        ? crypto.randomUUID()
        // ng serve reached over a LAN address is not a secure context, and randomUUID is missing
        // there. The id only has to be unique on one board.
        : `${Date.now().toString(16)}-${Math.random().toString(16).slice(2, 10)}`;
}

// One board's live connection, provided by the board page rather than at the root: the page owns
// its lifetime, and a second board opened in a second tab is a second connection by construction.
//
// Presence signals (cursors, selections, ghosts) are written at pointer rate. Nothing in the board
// template reads them — the page picks them up in an effect and writes the DOM itself — so a peer
// moving a mouse never re-renders two thousand items.
@Injectable()
export class CanvasHubService implements OnDestroy {
    private connection: HubConnection | null = null;
    private boardId = '';
    /** Which peer put which ghost on the board, so a departure clears exactly its own. */
    private ghostOwners = new Map<string, readonly string[]>();
    private ghostTimers = new Map<string, ReturnType<typeof setTimeout>>();

    readonly status = signal<HubStatus>('idle');
    readonly items = signal<readonly CanvasItem[]>([]);
    readonly peers = signal<readonly CanvasPeer[]>([]);
    readonly cursors = signal<ReadonlyMap<string, { x: number; y: number }>>(new Map());
    readonly selections = signal<ReadonlyMap<string, readonly string[]>>(new Map());
    readonly ghosts = signal<ReadonlyMap<string, CanvasGeometry>>(new Map());
    readonly canWrite = signal(false);
    readonly lastError = signal('');
    readonly board = signal<CanvasBoardSummary | null>(null);
    readonly projectName = signal('');
    readonly role = signal<CanvasRole>('viewer');
    /** The board was deleted under us — the page routes back to the list. */
    readonly gone = signal(false);
    /** At least one write is in flight. Counted, not flagged: the first of two to land must not
        report "saved" over the second still on the wire, nor wipe the second's refusal. */
    readonly saving = signal(false);
    private inFlight = 0;
    private failedInFlight = false;

    ngOnDestroy(): void {
        this.forgetGhosts();
        void this.leave();
    }

    async join(boardId: string): Promise<CanvasSnapshot> {
        if (this.boardId && this.boardId !== boardId) await this.leaveGroup();
        this.boardId = boardId;
        const connection = this.connection ?? this.build();
        if (connection.state === HubConnectionState.Disconnected) {
            this.status.set('connecting');
            try {
                await connection.start();
            } catch (e) {
                this.status.set('offline');
                throw e;
            }
        }
        return this.enter(boardId);
    }

    async leave(): Promise<void> {
        await this.leaveGroup();
        const connection = this.connection;
        this.connection = null;
        this.boardId = '';
        this.status.set('idle');
        if (connection) await connection.stop().catch(() => { });
    }

    async addItems(items: readonly NewCanvasItem[]): Promise<void> {
        if (!items.length) return;
        const now = new Date().toISOString();
        let z = this.items().reduce((top, i) => Math.max(top, i.z), 0);
        const optimistic: CanvasItem[] = items.map(i => ({
            id: i.id ?? newId(),
            boardId: this.boardId,
            kind: i.kind,
            x: i.x, y: i.y, width: i.width, height: i.height, rotation: i.rotation ?? 0,
            z: ++z,
            color: i.color ?? '',
            payload: i.payload,
            createdBy: '', createdAt: now, updatedBy: '', updatedAt: now,
            // Below every answer the server can give, so its echo always wins the merge.
            version: 0,
        }));
        this.items.set(sortCanvasItems([...this.items(), ...optimistic]));
        await this.write('AddItems', optimistic.map(i => ({
            id: i.id, kind: i.kind, x: i.x, y: i.y, width: i.width, height: i.height,
            rotation: i.rotation, color: i.color, payload: i.payload,
        })));
    }

    async updateItems(patches: readonly CanvasItemPatch[]): Promise<void> {
        if (!patches.length) return;
        const byId = new Map(patches.map(p => [p.id, p]));
        this.items.set(this.items().map(item => {
            const patch = byId.get(item.id);
            return patch ? { ...item, ...strip(patch) } : item;
        }));
        await this.write('UpdateItems', patches);
    }

    /** Fire and forget: a drag broadcasts at pointer rate and persists nothing. The pointer-up
        sends one updateItems — see the contract's §4, this is what makes the feature affordable. */
    dragItems(items: readonly CanvasGeometry[]): void {
        if (!items.length) return;
        void this.connection?.send('DragItems', this.boardId, items).catch(() => { });
    }

    async deleteItems(ids: readonly string[]): Promise<void> {
        if (!ids.length) return;
        const doomed = new Set(ids);
        this.items.set(this.items().filter(i => !doomed.has(i.id)));
        await this.write('DeleteItems', ids);
    }

    async bringToFront(ids: readonly string[]): Promise<void> {
        if (!ids.length) return;
        await this.write('BringToFront', ids);
    }

    cursor(x: number, y: number): void {
        void this.connection?.send('Cursor', this.boardId, x, y).catch(() => { });
    }

    select(ids: readonly string[]): void {
        void this.connection?.send('Select', this.boardId, ids).catch(() => { });
    }

    private async write(method: string, payload: unknown): Promise<void> {
        if (!this.connection) return;
        this.inFlight++;
        this.saving.set(true);
        try {
            await this.connection.invoke(method, this.boardId, payload);
        } catch (e) {
            this.failedInFlight = true;
            this.lastError.set(hubMessage(e));
            // One reconciliation path, not two: a refused write and a reconnect both end with the
            // snapshot replacing local state, so an optimistic row that never landed disappears
            // by the same mechanism that repairs a dropped socket.
            await this.reconcile();
        } finally {
            if (--this.inFlight === 0) {
                if (!this.failedInFlight) this.lastError.set('');
                this.failedInFlight = false;
                this.saving.set(false);
            }
        }
    }

    private async reconcile(): Promise<void> {
        if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
        try {
            this.apply(await this.connection.invoke<CanvasSnapshot>('Join', this.boardId));
        } catch {
            this.gone.set(true);
        }
    }

    private async enter(boardId: string): Promise<CanvasSnapshot> {
        const snapshot = await this.connection!.invoke<CanvasSnapshot>('Join', boardId);
        this.apply(snapshot);
        this.status.set('live');
        return snapshot;
    }

    private async leaveGroup(): Promise<void> {
        if (!this.boardId || this.connection?.state !== HubConnectionState.Connected) return;
        await this.connection.invoke('Leave', this.boardId).catch(() => { });
    }

    private apply(snapshot: CanvasSnapshot): void {
        this.board.set(snapshot.board);
        this.projectName.set(snapshot.projectName);
        this.role.set(snapshot.role);
        this.canWrite.set(snapshot.canWrite);
        this.items.set(sortCanvasItems(snapshot.items ?? []));
        this.peers.set(snapshot.peers ?? []);
        this.cursors.set(new Map());
        this.selections.set(new Map());
        this.ghosts.set(new Map());
        this.forgetGhosts();
        this.gone.set(false);
    }

    private build(): HubConnection {
        const connection = new HubConnectionBuilder()
            .withUrl('/hubs/canvas')
            .withAutomaticReconnect()
            .build();

        connection.on('peerJoined', (peer: CanvasPeer) => {
            this.peers.set([...this.peers().filter(p => p.connectionId !== peer.connectionId), peer]);
        });
        connection.on('peerLeft', (connectionId: string) => {
            this.peers.set(this.peers().filter(p => p.connectionId !== connectionId));
            const cursors = new Map(this.cursors());
            if (cursors.delete(connectionId)) this.cursors.set(cursors);
            const selections = new Map(this.selections());
            if (selections.delete(connectionId)) this.selections.set(selections);
            this.dropGhosts(connectionId);
        });
        connection.on('cursor', (connectionId: string, x: number, y: number) => {
            const next = new Map(this.cursors());
            next.set(connectionId, { x, y });
            this.cursors.set(next);
        });
        connection.on('selection', (connectionId: string, itemIds: string[]) => {
            const next = new Map(this.selections());
            next.set(connectionId, itemIds ?? []);
            this.selections.set(next);
        });
        connection.on('itemsAdded', (items: CanvasItem[]) => {
            this.items.set(mergeCanvasItems(this.items(), items ?? []));
        });
        connection.on('itemsChanged', (items: CanvasItem[]) => {
            this.items.set(mergeCanvasItems(this.items(), items ?? []));
            this.settle((items ?? []).map(i => i.id));
        });
        connection.on('itemsDragging', (connectionId: string, geometry: CanvasGeometry[]) => {
            const next = new Map(this.ghosts());
            for (const g of geometry ?? []) next.set(g.id, g);
            this.ghostOwners.set(connectionId, (geometry ?? []).map(g => g.id));
            this.ghosts.set(next);
            this.holdGhosts(connectionId);
        });
        connection.on('itemsDeleted', (ids: string[]) => {
            const doomed = new Set(ids ?? []);
            this.items.set(this.items().filter(i => !doomed.has(i.id)));
            this.settle(ids ?? []);
        });
        connection.on('boardChanged', (board: CanvasBoardSummary) => this.board.set(board));
        connection.on('boardDeleted', () => this.gone.set(true));

        connection.onreconnecting(() => this.status.set('reconnecting'));
        connection.onreconnected(() => void this.enter(this.boardId).catch(() => this.gone.set(true)));
        connection.onclose(() => this.status.set('offline'));

        this.connection = connection;
        return connection;
    }

    /** A ghost outlives its drag only until the authoritative row arrives. */
    private settle(ids: readonly string[]): void {
        if (!ids.length || !this.ghosts().size) return;
        const next = new Map(this.ghosts());
        for (const id of ids) next.delete(id);
        this.ghosts.set(next);
    }

    /** Restarts the peer's idle clock. The frames are the heartbeat; the last one is the deadline. */
    private holdGhosts(connectionId: string): void {
        clearTimeout(this.ghostTimers.get(connectionId));
        this.ghostTimers.set(connectionId, setTimeout(() => this.dropGhosts(connectionId), GHOST_IDLE_MS));
    }

    private dropGhosts(connectionId: string): void {
        clearTimeout(this.ghostTimers.get(connectionId));
        this.ghostTimers.delete(connectionId);
        const owned = this.ghostOwners.get(connectionId);
        this.ghostOwners.delete(connectionId);
        if (!owned?.length) return;
        this.settle(owned);
    }

    private forgetGhosts(): void {
        for (const timer of this.ghostTimers.values()) clearTimeout(timer);
        this.ghostTimers.clear();
        this.ghostOwners.clear();
    }
}

/** A patch leaves untouched fields alone, so undefined members must not overwrite anything. */
function strip(patch: CanvasItemPatch): Partial<CanvasItem> {
    const out: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(patch)) {
        if (key !== 'id' && value !== undefined) out[key] = value;
    }
    return out as Partial<CanvasItem>;
}

function hubMessage(e: unknown): string {
    const raw = e instanceof Error ? e.message : String(e ?? '');
    // A HubException arrives as "…HubException: <the localized sentence>" — the sentence is the
    // half a reader can act on.
    const marker = raw.lastIndexOf(':');
    return marker >= 0 && marker < raw.length - 1 ? raw.slice(marker + 1).trim() : raw;
}
