import { Component, ElementRef, NgZone, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { AVATAR_FILL_COUNT } from '../core/avatar-color.util';
import { AssetsService, LibraryAsset } from '../core/assets.service';
import {
    CanvasFramePayload, CanvasGeometry, CanvasImagePayload, CanvasItem, CanvasItemPatch,
    CanvasLinkPayload, CanvasNotePayload, NewCanvasItem,
} from '../core/boards.service';
import { CanvasHubService } from '../core/canvas-hub.service';
import { RailActionsService } from '../core/rail-actions.service';
import { RulerService } from '../core/ruler.service';
import { IconComponent } from '../shared/icon.component';
import { MediaPickerComponent } from '../shared/media-picker.component';
import { ModalComponent } from '../shared/modal.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { StampBadgeComponent, StampTone } from '../bench/display/stamp-badge.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';

export interface View { x: number; y: number; z: number; }

type Gesture =
    | { kind: 'pan'; sx: number; sy: number; ox: number; oy: number }
    | { kind: 'drag'; sx: number; sy: number; moved: boolean; from: Map<string, CanvasGeometry> }
    | { kind: 'resize'; id: string; corner: string; sx: number; sy: number; from: CanvasGeometry }
    | { kind: 'marquee'; sx: number; sy: number; add: boolean };

const MIN_ZOOM = 0.1;
const MAX_ZOOM = 4;
const CELL = 24;
const MIN_SIZE = 32;
/** Longest side, in world px, a picture is laid down at however large the file is. */
const IMAGE_SIZE = 460;
const DEFAULT_SIZE: Record<string, { width: number; height: number }> = {
    note: { width: 220, height: 180 },
    frame: { width: 640, height: 420 },
    link: { width: 300, height: 104 },
    image: { width: IMAGE_SIZE, height: IMAGE_SIZE },
};
/** A drag broadcasts at roughly this rate; the pointer itself fires far faster than anyone reads. */
const WIRE_MS = 33;
const DRAG_SLOP = 3;
/**
 * How long after the last arrow key a nudge is written. Key repeat is a drag made of discrete
 * frames, and one persisted write per repeat is exactly the thirty writes a second the drag path
 * exists to avoid; the keyup normally gets there first and this is the safety net behind it.
 */
const NUDGE_MS = 200;

/**
 * One board. The surface is a `<div>` world layer under a CSS `transform`, with every item an
 * ordinary absolutely-positioned element — not a `<canvas>` and not WebGL. That choice buys three
 * things a painted surface would have to rebuild from nothing: a note's text is real selectable
 * text, an image is an `<img>` the browser decodes and caches for us, and a screen reader walks the
 * same tree as every other screen in the app. What it costs is DOM count, which is why the pointer
 * path below never goes through Angular.
 *
 * The 60fps rule, concretely: pointermove and wheel are bound outside the zone, and while a gesture
 * is running the world's transform and the dragged elements' transforms are written straight to
 * `style`. Signals are set once, on pointer-up (and on a trailing timer after a zoom), so change
 * detection sees one update per gesture rather than one per frame. Peer cursors, peer drag ghosts
 * and the grid are painted the same way — the template never reads those signals at all.
 */
@Component({
    selector: 'app-project-canvas',
    imports: [
        FormsModule, IconComponent, ModalComponent, MediaPickerComponent, WorktopComponent,
        StampBadgeComponent, ButtonComponent, InputComponent,
    ],
    providers: [CanvasHubService],
    templateUrl: 'project-canvas.component.html',
    styleUrls: ['project-canvas.component.css'],
})
export class ProjectCanvasComponent implements OnDestroy {
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private zone = inject(NgZone);
    private assets = inject(AssetsService);
    private ruler = inject(RulerService);
    private rail = inject(RailActionsService);
    hub = inject(CanvasHubService);
    t = inject(LocaleService).t;

    private readonly stageRef = viewChild<ElementRef<HTMLElement>>('stage');
    private readonly worldRef = viewChild<ElementRef<HTMLElement>>('world');
    private readonly bandRef = viewChild<ElementRef<HTMLElement>>('band');

    projectId = signal('');
    boardId = signal('');
    view = signal<View>({ x: 0, y: 0, z: 1 });
    selection = signal<ReadonlySet<string>>(new Set<string>());
    /**
     * Locking is this reader's own, kept beside the viewport rather than on the row: the frozen
     * payload shapes have no field for it and reject unknown properties, so a shared lock would be
     * a schema change, not a UI one. What it is for — not shoving a reference you are drawing over —
     * is per-person anyway.
     */
    locked = signal<ReadonlySet<string>>(new Set<string>());
    editing = signal('');
    editText = signal('');
    loadError = signal('');
    uploadError = signal('');
    mediaOpen = signal(false);
    linkForm = signal<{ id: string; url: string; title: string } | null>(null);

    private gesture: Gesture | null = null;
    private dragEls = new Map<string, HTMLElement>();
    private ghosted = new Set<string>();
    private pending: View = { x: 0, y: 0, z: 1 };
    private spaceHeld = false;
    private lastWire = 0;
    private zoomTimer: ReturnType<typeof setTimeout> | null = null;
    private nudging = new Map<string, CanvasGeometry>();
    private nudged = { x: 0, y: 0 };
    private nudgeTimer: ReturnType<typeof setTimeout> | null = null;

    selectedItems = computed(() => {
        const picked = this.selection();
        return this.hub.items().filter(i => picked.has(i.id));
    });

    /** Item id → the colour of whoever else has it selected. */
    private peerRings = computed(() => {
        const colours = new Map(this.hub.peers().map(p => [p.connectionId, p.colorIndex]));
        const rings = new Map<string, number>();
        for (const [connectionId, ids] of this.hub.selections()) {
            const colour = colours.get(connectionId);
            if (colour === undefined) continue;
            for (const id of ids) rings.set(id, colour);
        }
        return rings;
    });

    zoomPercent = computed(() => Math.round(this.view().z * 100));
    background = computed(() => this.hub.board()?.background ?? 'grid');

    readonly corners: readonly string[] = ['nw', 'ne', 'sw', 'se'];

    constructor() {
        this.route.paramMap.subscribe(params => {
            const projectId = params.get('id') ?? '';
            const boardId = params.get('boardId') ?? '';
            if (!boardId) return;
            this.projectId.set(projectId);
            this.boardId.set(boardId);
            this.locked.set(readSet(`cedar.canvas.${boardId}.locked`));
            this.view.set(readView(`cedar.canvas.${boardId}.view`));
            this.pending = this.view();
            void this.open(boardId);
        });

        // Attached by hand and outside the zone: a template binding on pointermove would run change
        // detection on every frame of every drag, which is the one thing this screen cannot afford.
        effect(onCleanup => {
            const stage = this.stageRef()?.nativeElement;
            if (!stage) return;
            const move = (e: PointerEvent) => this.onMove(e);
            const up = (e: PointerEvent) => this.onUp(e);
            const wheel = (e: WheelEvent) => this.onWheel(e);
            this.zone.runOutsideAngular(() => {
                stage.addEventListener('pointermove', move);
                stage.addEventListener('pointerup', up);
                stage.addEventListener('pointercancel', up);
                stage.addEventListener('wheel', wheel, { passive: false });
            });
            onCleanup(() => {
                stage.removeEventListener('pointermove', move);
                stage.removeEventListener('pointerup', up);
                stage.removeEventListener('pointercancel', up);
                stage.removeEventListener('wheel', wheel);
            });
        });

        effect(() => this.paint(this.view()));
        effect(() => this.paintCursors(this.hub.cursors()));
        effect(() => this.paintGhosts(this.hub.ghosts()));

        effect(() => {
            const t = this.t().projects.canvas;
            const board = this.hub.board();
            this.ruler.publish({
                label: board?.name ?? '',
                left: [
                    { text: t.zoomLevel(this.zoomPercent()) },
                    { text: t.itemCount(this.hub.items().length) },
                ],
                right: [{ text: this.statusWord() }],
            });
            this.rail.publish({
                save: this.hub.status() === 'live' ? { state: this.hub.saving() ? 'forming' : 'set' } : null,
                primary: this.hub.canWrite()
                    ? { label: t.addNote, icon: 'note', run: () => this.addNote() }
                    : null,
            });
        });

        // The board was deleted, or the membership that reached it was withdrawn.
        effect(() => {
            if (this.hub.gone()) void this.router.navigate(['/projects', this.projectId(), 'canvas']);
        });
    }

    ngOnDestroy(): void {
        if (this.zoomTimer) clearTimeout(this.zoomTimer);
        // Before the socket goes: an uncommitted nudge is a move the reader has already seen.
        this.commitNudge();
        this.ruler.clear();
        this.rail.clear();
        void this.hub.leave();
    }

    async open(boardId: string) {
        this.loadError.set('');
        try {
            await this.hub.join(boardId);
            // A first visit has no remembered viewport, and dropping someone at the origin of an
            // endless plane shows them nothing at all.
            if (this.hub.items().length > 0 && isOrigin(this.view())) this.fit();
        } catch (e) {
            // A socket that would not open is a state the surface can sit in and retry from. A Join
            // the caller may not read is the 404 discipline over the wire, and the honest answer to
            // that is the board list — a board that is not there is not a board to stay on.
            if (this.hub.status() === 'offline') return;
            this.loadError.set(httpErrorMessage(e, this.t().projects.canvas.boardNotFound));
            void this.router.navigate(['/projects', this.projectId(), 'canvas']);
        }
    }

    statusWord(): string {
        const t = this.t().projects.canvas.status;
        const status = this.hub.status();
        if (status === 'reconnecting') return t.reconnecting;
        if (status === 'connecting' || status === 'idle') return t.connecting;
        return status === 'live' ? t.live : t.offline;
    }

    /** Rust is the app's failure ink, and a first open is not a failure. */
    statusTone(): StampTone {
        const status = this.hub.status();
        if (status === 'live') return 'pine';
        return status === 'connecting' || status === 'idle' ? 'ink' : 'rust';
    }

    // ——— items ———————————————————————————————————————————————————————————————

    noteOf(item: CanvasItem): CanvasNotePayload {
        const payload = item.payload as Partial<CanvasNotePayload>;
        return { text: payload.text ?? '', align: payload.align === 'center' ? 'center' : 'left' };
    }

    frameOf(item: CanvasItem): CanvasFramePayload {
        return { title: (item.payload as Partial<CanvasFramePayload>).title ?? '' };
    }

    linkOf(item: CanvasItem): CanvasLinkPayload {
        const payload = item.payload as Partial<CanvasLinkPayload>;
        return { url: payload.url ?? '', title: payload.title ?? '', favicon: payload.favicon ?? '' };
    }

    imageOf(item: CanvasItem): CanvasImagePayload {
        const payload = item.payload as Partial<CanvasImagePayload>;
        return {
            url: payload.url ?? '',
            naturalWidth: payload.naturalWidth ?? 0,
            naturalHeight: payload.naturalHeight ?? 0,
            alt: payload.alt ?? '',
        };
    }

    itemLabel(item: CanvasItem): string {
        const t = this.t().projects.canvas;
        const kind = item.kind === 'image' ? t.addImage
            : item.kind === 'note' ? t.addNote
                : item.kind === 'frame' ? t.addFrame : t.addLink;
        const words = item.kind === 'note' ? this.noteOf(item).text
            : item.kind === 'frame' ? this.frameOf(item).title
                : item.kind === 'link' ? (this.linkOf(item).title || this.linkOf(item).url)
                    : this.imageOf(item).alt;
        const state = this.locked().has(item.id) ? ` · ${t.locked}` : '';
        return (words ? `${kind}: ${words}` : kind) + state;
    }

    itemTransform(item: CanvasItem): string {
        return `translate(${item.x}px, ${item.y}px) rotate(${item.rotation}deg)`;
    }

    peerRing(id: string): string | null {
        const colour = this.peerRings().get(id);
        return colour === undefined ? null : peerFill(colour);
    }

    peerFace(colorIndex: number): string {
        return peerFill(colorIndex);
    }

    isSelected(id: string): boolean {
        return this.selection().has(id);
    }

    // ——— adding ——————————————————————————————————————————————————————————————

    addNote() {
        const at = this.centre('note');
        void this.add([{ kind: 'note', ...at, payload: { text: '', align: 'left' } }]).then(id => {
            if (id) this.beginEdit(id, '');
        });
    }

    addFrame() {
        void this.add([{ kind: 'frame', ...this.centre('frame'), payload: { title: '' } }]);
    }

    openLinkForm() {
        this.linkForm.set({ id: '', url: '', title: '' });
    }

    async submitLink() {
        const form = this.linkForm();
        if (!form) return;
        const url = form.url.trim();
        if (!/^https?:\/\//i.test(url)) return;
        const payload: CanvasLinkPayload = { url, title: form.title.trim(), favicon: '' };
        if (form.id) await this.hub.updateItems([{ id: form.id, payload }]);
        else await this.add([{ kind: 'link', ...this.centre('link'), payload }]);
        this.linkForm.set(null);
    }

    async onPicked(asset: LibraryAsset) {
        this.mediaOpen.set(false);
        await this.place(`/media/${asset.localPath}`);
    }

    onDragOver(event: DragEvent) {
        if (!this.hub.canWrite()) return;
        event.preventDefault();
    }

    async onDrop(event: DragEvent) {
        if (!this.hub.canWrite()) return;
        event.preventDefault();
        const files = [...(event.dataTransfer?.files ?? [])].filter(f => f.type.startsWith('image/'));
        if (!files.length) return;
        const at = this.screenToWorld(event.clientX, event.clientY);
        this.uploadError.set('');
        for (const file of files) {
            try {
                const { url } = await this.assets.upload(file);
                await this.place(url, at);
                at.x += 24;
                at.y += 24;
            } catch (e) {
                this.uploadError.set(httpErrorMessage(e, this.t().projects.canvas.uploadFailed));
            }
        }
    }

    /** The natural size is read off the decoded picture, so a portrait does not land in a square. */
    private async place(url: string, at?: { x: number; y: number }) {
        const size = await naturalSize(url);
        const scale = IMAGE_SIZE / Math.max(size.width, size.height, 1);
        const width = Math.round(size.width * scale) || IMAGE_SIZE;
        const height = Math.round(size.height * scale) || IMAGE_SIZE;
        const spot = at ?? this.centreOf(width, height);
        await this.add([{
            kind: 'image', x: spot.x, y: spot.y, width, height,
            payload: { url, naturalWidth: size.width, naturalHeight: size.height, alt: '' },
        }]);
    }

    private async add(items: NewCanvasItem[]): Promise<string> {
        if (!this.hub.canWrite()) return '';
        const before = new Set(this.hub.items().map(i => i.id));
        await this.hub.addItems(items);
        const fresh = this.hub.items().find(i => !before.has(i.id));
        if (fresh) this.setSelection(new Set([fresh.id]));
        return fresh?.id ?? '';
    }

    private centre(kind: string): { x: number; y: number; width: number; height: number } {
        const size = DEFAULT_SIZE[kind] ?? DEFAULT_SIZE['note'];
        return { ...this.centreOf(size.width, size.height), width: size.width, height: size.height };
    }

    private centreOf(width: number, height: number): { x: number; y: number } {
        const stage = this.stageRef()?.nativeElement;
        const rect = stage?.getBoundingClientRect();
        const view = this.view();
        const cx = ((rect?.width ?? 800) / 2 - view.x) / view.z;
        const cy = ((rect?.height ?? 600) / 2 - view.y) / view.z;
        return { x: Math.round(cx - width / 2), y: Math.round(cy - height / 2) };
    }

    // ——— selection and its actions ———————————————————————————————————————————

    setSelection(ids: ReadonlySet<string>) {
        this.selection.set(ids);
        this.hub.select([...ids]);
    }

    selectAll() {
        this.setSelection(new Set(this.hub.items().map(i => i.id)));
    }

    clearSelection() {
        this.setSelection(new Set());
    }

    async deleteSelected() {
        const ids = [...this.selection()].filter(id => !this.locked().has(id));
        if (!ids.length) return;
        this.clearSelection();
        await this.hub.deleteItems(ids);
    }

    async bringToFront() {
        const ids = [...this.selection()];
        if (ids.length) await this.hub.bringToFront(ids);
    }

    toggleLock() {
        const ids = [...this.selection()];
        if (!ids.length) return;
        const next = new Set(this.locked());
        const locking = ids.some(id => !next.has(id));
        for (const id of ids) {
            if (locking) next.add(id);
            else next.delete(id);
        }
        this.locked.set(next);
        writeSet(`cedar.canvas.${this.boardId()}.locked`, next);
    }

    allLocked(): boolean {
        const ids = [...this.selection()];
        return ids.length > 0 && ids.every(id => this.locked().has(id));
    }

    // ——— editing text in place ———————————————————————————————————————————————

    beginEdit(id: string, text: string) {
        if (!this.hub.canWrite() || this.locked().has(id)) return;
        this.editText.set(text);
        this.editing.set(id);
    }

    async commitEdit() {
        const id = this.editing();
        if (!id) return;
        const item = this.hub.items().find(i => i.id === id);
        this.editing.set('');
        if (!item) return;
        const text = this.editText();
        const payload = item.kind === 'frame'
            ? { title: text }
            : { text, align: this.noteOf(item).align };
        await this.hub.updateItems([{ id, payload }]);
    }

    // ——— the surface ————————————————————————————————————————————————————————

    onStageKey(event: KeyboardEvent) {
        const target = event.target as HTMLElement | null;
        if (target && (target.tagName === 'TEXTAREA' || target.tagName === 'INPUT')) return;
        if (event.code === 'Space' && !this.spaceHeld) {
            this.spaceHeld = true;
            this.stageRef()?.nativeElement.classList.add('is-panning');
            event.preventDefault();
            return;
        }
        if (event.key === 'Escape') { this.clearSelection(); return; }
        if (event.key === 'Delete' || event.key === 'Backspace') {
            event.preventDefault();
            void this.deleteSelected();
            return;
        }
        if (event.key === 'a' && (event.ctrlKey || event.metaKey)) { event.preventDefault(); this.selectAll(); return; }
        if (event.key === 'n' || event.key === 'N') { event.preventDefault(); this.addNote(); return; }
        if (event.key === '+' || event.key === '=') { event.preventDefault(); this.zoomBy(1.2); return; }
        if (event.key === '-') { event.preventDefault(); this.zoomBy(1 / 1.2); return; }
        if (event.key === '0') { event.preventDefault(); this.zoomTo(1); return; }
        if (event.key === '1') { event.preventDefault(); this.fit(); }
    }

    onStageKeyUp(event: KeyboardEvent) {
        // An item's keydown stops propagating, its keyup does not — this is where a nudge ends.
        if (event.key.startsWith('Arrow')) { this.commitNudge(); return; }
        if (event.code !== 'Space') return;
        this.spaceHeld = false;
        this.stageRef()?.nativeElement.classList.remove('is-panning');
    }

    // Handled keys stop here: the surface answers the same ones for whoever is standing on it with
    // nothing focused, and both handlers firing would run each action twice.
    onItemKey(event: KeyboardEvent, item: CanvasItem) {
        if (event.key === 'Delete' || event.key === 'Backspace') {
            event.preventDefault();
            event.stopPropagation();
            void this.deleteSelected();
            return;
        }
        if (event.key === 'Enter') {
            event.preventDefault();
            event.stopPropagation();
            if (item.kind === 'note') this.beginEdit(item.id, this.noteOf(item).text);
            else if (item.kind === 'frame') this.beginEdit(item.id, this.frameOf(item).title);
            else if (item.kind === 'link') this.linkForm.set({ id: item.id, ...this.linkOf(item) });
            return;
        }
        const step = event.shiftKey ? 1 : 10;
        const nudge: Record<string, [number, number]> = {
            ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step],
        };
        const delta = nudge[event.key];
        if (!delta) return;
        event.preventDefault();
        event.stopPropagation();
        this.nudge(delta[0], delta[1]);
    }

    /**
     * A held arrow key is a drag whose frames happen to be discrete, and it goes down the same two
     * phases: paint and broadcast now, persist once at the end. Key repeat delivers about thirty
     * events a second, and one hub call each would queue behind SignalR's single in-flight
     * invocation and widen the read-modify-write window every one of them runs in.
     */
    private nudge(dx: number, dy: number) {
        const moving = this.selectedItems().filter(i => !this.locked().has(i.id));
        if (!moving.length) return;
        if (!sameIds(this.nudging, moving)) {
            this.commitNudge();
            this.dragEls.clear();
            for (const item of moving) {
                this.nudging.set(item.id, {
                    id: item.id, x: item.x, y: item.y,
                    width: item.width, height: item.height, rotation: item.rotation,
                });
                const el = this.stageRef()?.nativeElement.querySelector<HTMLElement>(`[data-id="${item.id}"]`);
                if (el) this.dragEls.set(item.id, el);
            }
        }
        this.nudged = { x: this.nudged.x + dx, y: this.nudged.y + dy };

        const live: CanvasGeometry[] = [];
        for (const from of this.nudging.values()) {
            const at = { ...from, x: from.x + this.nudged.x, y: from.y + this.nudged.y };
            const el = this.dragEls.get(from.id);
            if (el) el.style.transform = `translate(${at.x}px, ${at.y}px) rotate(${at.rotation}deg)`;
            live.push(at);
        }
        this.wire(() => this.hub.dragItems(live));

        if (this.nudgeTimer) clearTimeout(this.nudgeTimer);
        this.nudgeTimer = setTimeout(() => this.zone.run(() => this.commitNudge()), NUDGE_MS);
    }

    private commitNudge() {
        if (this.nudgeTimer) clearTimeout(this.nudgeTimer);
        this.nudgeTimer = null;
        const from = [...this.nudging.values()];
        const by = this.nudged;
        this.nudging.clear();
        this.nudged = { x: 0, y: 0 };
        if (!from.length || (!by.x && !by.y)) return;
        this.handBack(from);
        void this.hub.updateItems(from.map<CanvasItemPatch>(g => ({
            id: g.id, x: Math.round(g.x + by.x), y: Math.round(g.y + by.y),
        })));
    }

    onItemFocus(item: CanvasItem) {
        // A pointer press has already put the right set together — shift-click included — and the
        // focus that follows it must not undo that. Only a focus arriving from somewhere else (Tab)
        // decides a selection.
        if (this.selection().has(item.id)) return;
        this.setSelection(new Set([item.id]));
    }

    onStageDown(event: PointerEvent) {
        const stage = this.stageRef()?.nativeElement;
        if (!stage) return;
        stage.setPointerCapture(event.pointerId);
        const target = event.target as HTMLElement | null;
        const handle = target?.closest<HTMLElement>('[data-corner]');
        const host = target?.closest<HTMLElement>('[data-id]');
        const id = host?.dataset['id'] ?? '';

        if (event.button === 1 || this.spaceHeld) {
            const view = this.view();
            this.gesture = { kind: 'pan', sx: event.clientX, sy: event.clientY, ox: view.x, oy: view.y };
            return;
        }
        if (event.button !== 0) return;

        if (handle && id) {
            const item = this.hub.items().find(i => i.id === id);
            if (!item || !this.hub.canWrite() || this.locked().has(id)) return;
            this.dragEls.clear();
            if (host) this.dragEls.set(id, host);
            this.gesture = {
                kind: 'resize', id, corner: handle.dataset['corner'] ?? 'se',
                sx: event.clientX, sy: event.clientY,
                from: { id, x: item.x, y: item.y, width: item.width, height: item.height, rotation: item.rotation },
            };
            return;
        }

        if (id) {
            const picked = new Set(this.selection());
            if (event.shiftKey || event.ctrlKey || event.metaKey) {
                if (picked.has(id)) picked.delete(id);
                else picked.add(id);
            } else if (!picked.has(id)) {
                picked.clear();
                picked.add(id);
            }
            this.setSelection(picked);
            if (!this.hub.canWrite()) return;
            const from = new Map<string, CanvasGeometry>();
            this.dragEls.clear();
            for (const item of this.hub.items()) {
                if (!picked.has(item.id) || this.locked().has(item.id)) continue;
                from.set(item.id, {
                    id: item.id, x: item.x, y: item.y,
                    width: item.width, height: item.height, rotation: item.rotation,
                });
                const el = stage.querySelector<HTMLElement>(`[data-id="${item.id}"]`);
                if (el) this.dragEls.set(item.id, el);
            }
            this.gesture = { kind: 'drag', sx: event.clientX, sy: event.clientY, moved: false, from };
            return;
        }

        if (!event.shiftKey) this.clearSelection();
        this.gesture = { kind: 'marquee', sx: event.clientX, sy: event.clientY, add: event.shiftKey };
        const band = this.bandRef()?.nativeElement;
        if (band) band.hidden = false;
    }

    /** Outside the zone — nothing in here may touch a signal until the gesture ends. */
    private onMove(event: PointerEvent) {
        const gesture = this.gesture;
        if (!gesture) {
            this.wireCursor(event);
            return;
        }
        const dx = event.clientX - gesture.sx;
        const dy = event.clientY - gesture.sy;

        if (gesture.kind === 'pan') {
            this.pending = { ...this.pending, x: gesture.ox + dx, y: gesture.oy + dy };
            this.paint(this.pending);
            return;
        }
        if (gesture.kind === 'marquee') {
            const stage = this.stageRef()?.nativeElement;
            const band = this.bandRef()?.nativeElement;
            if (!stage || !band) return;
            const rect = stage.getBoundingClientRect();
            band.style.left = `${Math.min(gesture.sx, event.clientX) - rect.left}px`;
            band.style.top = `${Math.min(gesture.sy, event.clientY) - rect.top}px`;
            band.style.width = `${Math.abs(dx)}px`;
            band.style.height = `${Math.abs(dy)}px`;
            return;
        }
        if (gesture.kind === 'drag') {
            if (!gesture.moved && Math.abs(dx) + Math.abs(dy) < DRAG_SLOP) return;
            gesture.moved = true;
            const z = this.pending.z;
            const live: CanvasGeometry[] = [];
            for (const [id, from] of gesture.from) {
                const el = this.dragEls.get(id);
                const x = from.x + dx / z;
                const y = from.y + dy / z;
                if (el) el.style.transform = `translate(${x}px, ${y}px) rotate(${from.rotation}deg)`;
                live.push({ ...from, x, y });
            }
            this.wire(() => this.hub.dragItems(live));
            return;
        }
        if (gesture.kind === 'resize') {
            const el = this.dragEls.get(gesture.id) ?? this.stageRef()?.nativeElement
                .querySelector<HTMLElement>(`[data-id="${gesture.id}"]`) ?? null;
            const next = resized(gesture.from, gesture.corner, dx / this.pending.z, dy / this.pending.z);
            if (el) {
                el.style.transform = `translate(${next.x}px, ${next.y}px) rotate(${next.rotation}deg)`;
                el.style.width = `${next.width}px`;
                el.style.height = `${next.height}px`;
            }
            this.wire(() => this.hub.dragItems([next]));
        }
    }

    private onUp(event: PointerEvent) {
        const gesture = this.gesture;
        this.gesture = null;
        if (!gesture) return;
        const band = this.bandRef()?.nativeElement;
        if (band) band.hidden = true;

        // Back inside Angular: this is the one place per gesture where state moves.
        this.zone.run(() => {
            if (gesture.kind === 'pan') {
                this.commitView(this.pending);
                return;
            }
            if (gesture.kind === 'marquee') {
                this.pickWithin(gesture, event);
                return;
            }
            if (gesture.kind === 'drag') {
                if (!gesture.moved) return;
                const dx = (event.clientX - gesture.sx) / this.pending.z;
                const dy = (event.clientY - gesture.sy) / this.pending.z;
                this.handBack([...gesture.from.values()]);
                void this.hub.updateItems([...gesture.from].map<CanvasItemPatch>(([id, from]) => ({
                    id, x: Math.round(from.x + dx), y: Math.round(from.y + dy),
                })));
                return;
            }
            const next = resized(gesture.from, gesture.corner,
                (event.clientX - gesture.sx) / this.pending.z, (event.clientY - gesture.sy) / this.pending.z);
            this.handBack([gesture.from]);
            void this.hub.updateItems([{
                id: next.id, x: Math.round(next.x), y: Math.round(next.y),
                width: Math.round(next.width), height: Math.round(next.height),
            }]);
        });
    }

    /**
     * Puts the element back exactly where the template's binding believes it is, before the write
     * that will move it. Angular writes a style only when the bound value changes, so an element
     * left standing at a position no binding knows about would stay there for good the moment a
     * refused write reconciled the row back to what it already held.
     */
    private handBack(from: readonly CanvasGeometry[]) {
        for (const geometry of from) {
            const el = this.dragEls.get(geometry.id);
            if (!el) continue;
            el.style.transform = `translate(${geometry.x}px, ${geometry.y}px) rotate(${geometry.rotation}deg)`;
            el.style.width = `${geometry.width}px`;
            el.style.height = `${geometry.height}px`;
        }
    }

    private pickWithin(gesture: { sx: number; sy: number; add: boolean }, event: PointerEvent) {
        const a = this.screenToWorld(Math.min(gesture.sx, event.clientX), Math.min(gesture.sy, event.clientY));
        const b = this.screenToWorld(Math.max(gesture.sx, event.clientX), Math.max(gesture.sy, event.clientY));
        if (b.x - a.x < 4 && b.y - a.y < 4) return;
        const picked = gesture.add ? new Set(this.selection()) : new Set<string>();
        for (const item of this.hub.items()) {
            const hit = item.x < b.x && item.x + item.width > a.x && item.y < b.y && item.y + item.height > a.y;
            if (hit) picked.add(item.id);
        }
        this.setSelection(picked);
    }

    private onWheel(event: WheelEvent) {
        event.preventDefault();
        if (event.ctrlKey || event.metaKey) {
            const factor = Math.exp(-event.deltaY / 400);
            this.pending = zoomAt(this.pending, factor, this.local(event.clientX, event.clientY));
            this.paint(this.pending);
            this.settleZoom();
            return;
        }
        this.pending = { ...this.pending, x: this.pending.x - event.deltaX, y: this.pending.y - event.deltaY };
        this.paint(this.pending);
        this.settleZoom();
    }

    /** The transform is live at frame rate; the readout it feeds settles once the gesture stops. */
    private settleZoom() {
        if (this.zoomTimer) clearTimeout(this.zoomTimer);
        this.zoomTimer = setTimeout(() => this.zone.run(() => this.commitView(this.pending)), 120);
    }

    zoomBy(factor: number) {
        const stage = this.stageRef()?.nativeElement;
        const rect = stage?.getBoundingClientRect();
        this.pending = zoomAt(this.pending, factor, { x: (rect?.width ?? 0) / 2, y: (rect?.height ?? 0) / 2 });
        this.commitView(this.pending);
    }

    zoomTo(z: number) {
        const stage = this.stageRef()?.nativeElement;
        const rect = stage?.getBoundingClientRect();
        this.pending = zoomAt(this.pending, z / this.pending.z, { x: (rect?.width ?? 0) / 2, y: (rect?.height ?? 0) / 2 });
        this.commitView(this.pending);
    }

    fit() {
        const items = this.hub.items();
        const stage = this.stageRef()?.nativeElement;
        const rect = stage?.getBoundingClientRect();
        const width = rect?.width ?? 0;
        const height = rect?.height ?? 0;
        if (!items.length || !width || !height) {
            this.commitView({ x: 0, y: 0, z: 1 });
            return;
        }
        const left = Math.min(...items.map(i => i.x));
        const top = Math.min(...items.map(i => i.y));
        const right = Math.max(...items.map(i => i.x + i.width));
        const bottom = Math.max(...items.map(i => i.y + i.height));
        const pad = 64;
        const z = clamp(Math.min((width - pad) / (right - left || 1), (height - pad) / (bottom - top || 1)));
        this.commitView({
            z,
            x: width / 2 - ((left + right) / 2) * z,
            y: height / 2 - ((top + bottom) / 2) * z,
        });
    }

    private commitView(view: View) {
        this.pending = view;
        this.view.set(view);
        writeView(`cedar.canvas.${this.boardId()}.view`, view);
    }

    private screenToWorld(clientX: number, clientY: number): { x: number; y: number } {
        const point = this.local(clientX, clientY);
        return { x: (point.x - this.pending.x) / this.pending.z, y: (point.y - this.pending.y) / this.pending.z };
    }

    private local(clientX: number, clientY: number): { x: number; y: number } {
        const rect = this.stageRef()?.nativeElement.getBoundingClientRect();
        return { x: clientX - (rect?.left ?? 0), y: clientY - (rect?.top ?? 0) };
    }

    private wireCursor(event: PointerEvent) {
        this.wire(() => {
            const at = this.screenToWorld(event.clientX, event.clientY);
            this.hub.cursor(at.x, at.y);
        });
    }

    private wire(send: () => void) {
        const now = performance.now();
        if (now - this.lastWire < WIRE_MS) return;
        this.lastWire = now;
        send();
    }

    // ——— painting, all of it outside the template ————————————————————————————

    private paint(view: View) {
        const world = this.worldRef()?.nativeElement;
        const stage = this.stageRef()?.nativeElement;
        if (!world || !stage) return;
        world.style.transform = `translate(${view.x}px, ${view.y}px) scale(${view.z})`;
        // Anything that must stay one size on screen — a selection ring, a resize handle, a peer's
        // name — multiplies by this instead of being redrawn.
        stage.style.setProperty('--c-inv', String(1 / view.z));
        stage.style.setProperty('--c-cell', `${CELL * view.z}px`);
        stage.style.setProperty('--c-ox', `${view.x}px`);
        stage.style.setProperty('--c-oy', `${view.y}px`);
    }

    private paintCursors(cursors: ReadonlyMap<string, { x: number; y: number }>) {
        const world = this.worldRef()?.nativeElement;
        if (!world) return;
        for (const el of world.querySelectorAll<HTMLElement>('[data-peer]')) {
            const at = cursors.get(el.dataset['peer'] ?? '');
            if (!at) { el.hidden = true; continue; }
            el.hidden = false;
            el.style.transform = `translate(${at.x}px, ${at.y}px)`;
        }
    }

    private paintGhosts(ghosts: ReadonlyMap<string, CanvasGeometry>) {
        const stage = this.stageRef()?.nativeElement;
        if (!stage) return;
        for (const [id, g] of ghosts) {
            const el = stage.querySelector<HTMLElement>(`[data-id="${id}"]`);
            if (!el) continue;
            this.ghosted.add(id);
            el.style.transform = `translate(${g.x}px, ${g.y}px) rotate(${g.rotation}deg)`;
        }
        // A ghost that ends without the row changing leaves Angular's binding holding the value it
        // already wrote, so nothing would put the item back where it belongs but this.
        for (const id of [...this.ghosted]) {
            if (ghosts.has(id)) continue;
            this.ghosted.delete(id);
            const item = this.hub.items().find(i => i.id === id);
            const el = stage.querySelector<HTMLElement>(`[data-id="${id}"]`);
            if (item && el) el.style.transform = this.itemTransform(item);
        }
    }
}

function peerFill(colorIndex: number): string {
    return `var(--avatar-${(Math.abs(colorIndex) % AVATAR_FILL_COUNT) + 1})`;
}

function clamp(z: number): number {
    return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, z));
}

export function zoomAt(view: View, factor: number, at: { x: number; y: number }): View {
    const z = clamp(view.z * factor);
    return { z, x: at.x - ((at.x - view.x) / view.z) * z, y: at.y - ((at.y - view.y) / view.z) * z };
}

export function resized(from: CanvasGeometry, corner: string, dx: number, dy: number): CanvasGeometry {
    const west = corner.includes('w');
    const north = corner.includes('n');
    const width = Math.max(MIN_SIZE, west ? from.width - dx : from.width + dx);
    const height = Math.max(MIN_SIZE, north ? from.height - dy : from.height + dy);
    return {
        id: from.id,
        x: west ? from.x + (from.width - width) : from.x,
        y: north ? from.y + (from.height - height) : from.y,
        width, height, rotation: from.rotation,
    };
}

function sameIds(held: ReadonlyMap<string, unknown>, items: readonly CanvasItem[]): boolean {
    return held.size === items.length && items.every(i => held.has(i.id));
}

function isOrigin(view: View): boolean {
    return view.x === 0 && view.y === 0 && view.z === 1;
}

function naturalSize(url: string): Promise<{ width: number; height: number }> {
    return new Promise(resolve => {
        const probe = new Image();
        probe.onload = () => resolve({ width: probe.naturalWidth, height: probe.naturalHeight });
        probe.onerror = () => resolve({ width: IMAGE_SIZE, height: IMAGE_SIZE });
        probe.src = url;
    });
}

/** The viewport is this reader's own and is never sent anywhere (contract §5.3). */
function readView(key: string): View {
    try {
        const raw = JSON.parse(localStorage.getItem(key) ?? 'null');
        if (raw && typeof raw.x === 'number' && typeof raw.y === 'number' && typeof raw.z === 'number') {
            return { x: raw.x, y: raw.y, z: clamp(raw.z) };
        }
    } catch { /* private mode, or a value from an older shape */ }
    return { x: 0, y: 0, z: 1 };
}

function writeView(key: string, view: View): void {
    try { localStorage.setItem(key, JSON.stringify(view)); } catch { /* private mode */ }
}

function readSet(key: string): ReadonlySet<string> {
    try {
        const raw = JSON.parse(localStorage.getItem(key) ?? '[]');
        return new Set(Array.isArray(raw) ? raw.filter((v): v is string => typeof v === 'string') : []);
    } catch {
        return new Set();
    }
}

function writeSet(key: string, value: ReadonlySet<string>): void {
    try { localStorage.setItem(key, JSON.stringify([...value])); } catch { /* private mode */ }
}
