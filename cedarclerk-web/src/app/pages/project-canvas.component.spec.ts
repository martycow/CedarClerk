import { signal } from '@angular/core';
import { ConfirmationService } from '../core/confirmation.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectCanvasComponent } from './project-canvas.component';
import { AssetsService } from '../core/assets.service';
import {
    CanvasBoardSummary, CanvasGeometry, CanvasItem, CanvasItemPatch, CanvasPeer, CanvasRole,
    CanvasSnapshot, NewCanvasItem,
} from '../core/boards.service';
import { CanvasHubService, HubStatus } from '../core/canvas-hub.service';
import { en } from '@localization/en';

const BOARD: CanvasBoardSummary = {
    id: 'b1', projectId: 'p1', name: 'References', background: 'dots', itemCount: 0,
    createdAt: '', updatedAt: '', version: 1, canWrite: true,
};

function item(part: Partial<CanvasItem> & { id: string }): CanvasItem {
    return {
        boardId: 'b1', kind: 'note', x: 0, y: 0, width: 220, height: 180, rotation: 0, z: 1,
        color: '', payload: { text: '', align: 'left' },
        createdBy: 'u1', createdAt: '2026-08-27T10:00:00Z',
        updatedBy: 'u1', updatedAt: '2026-08-27T10:00:00Z', version: 1,
        ...part,
    };
}

const THREE: CanvasItem[] = [
    item({ id: 'n1', kind: 'note', payload: { text: 'hello', align: 'left' } }),
    item({ id: 'f1', kind: 'frame', z: 2, width: 640, height: 420, payload: { title: 'Mood' } }),
    item({ id: 'l1', kind: 'link', z: 3, payload: { url: 'https://example.test/a', title: '', favicon: '' } }),
];

/**
 * The hub as the page sees it: the same signals, and a `join` the test decides the outcome of.
 * Writes are recorded rather than sent, and `addItems` grows the list the way the real service's
 * optimistic row does, because the page finds the fresh id by diffing before and after.
 */
class FakeHub {
    status = signal<HubStatus>('idle');
    items = signal<readonly CanvasItem[]>([]);
    peers = signal<readonly CanvasPeer[]>([]);
    cursors = signal<ReadonlyMap<string, { x: number; y: number }>>(new Map());
    selections = signal<ReadonlyMap<string, readonly string[]>>(new Map());
    ghosts = signal<ReadonlyMap<string, CanvasGeometry>>(new Map());
    canWrite = signal(false);
    lastError = signal('');
    board = signal<CanvasBoardSummary | null>(null);
    projectName = signal('');
    role = signal<CanvasRole>('viewer');
    gone = signal(false);
    saving = signal(false);

    snapshot: CanvasSnapshot = { board: BOARD, projectName: 'Cedar Quest', items: [], role: 'owner', canWrite: true, peers: [] };
    joinOutcome: 'live' | 'never' | 'offline' | 'refused' = 'live';
    joins = 0;
    left = 0;
    added: NewCanvasItem[][] = [];
    updated: CanvasItemPatch[][] = [];
    deleted: string[][] = [];
    fronted: string[][] = [];
    selected: string[][] = [];

    async join(): Promise<CanvasSnapshot> {
        this.joins++;
        this.status.set('connecting');
        if (this.joinOutcome === 'never') return new Promise<CanvasSnapshot>(() => { });
        if (this.joinOutcome === 'offline') {
            this.status.set('offline');
            throw new Error('Failed to start the connection');
        }
        if (this.joinOutcome === 'refused') throw new Error('HubException: not yours');
        const s = this.snapshot;
        this.board.set(s.board);
        this.projectName.set(s.projectName);
        this.role.set(s.role);
        this.canWrite.set(s.canWrite);
        this.items.set(s.items);
        this.peers.set(s.peers ?? []);
        this.status.set('live');
        return s;
    }
    async leave() { this.left++; this.status.set('idle'); }
    async addItems(items: readonly NewCanvasItem[]) {
        this.added.push([...items]);
        let n = this.items().length;
        this.items.set([...this.items(), ...items.map(i => item({
            id: i.id ?? `new-${++n}`, kind: i.kind, x: i.x, y: i.y, width: i.width, height: i.height,
            z: n, payload: i.payload, version: 0,
        }))]);
    }
    async updateItems(patches: readonly CanvasItemPatch[]) { this.updated.push([...patches]); }
    async deleteItems(ids: readonly string[]) {
        this.deleted.push([...ids]);
        this.items.set(this.items().filter(i => !ids.includes(i.id)));
    }
    async bringToFront(ids: readonly string[]) { this.fronted.push([...ids]); }
    dragItems() { /* pointer-rate, nothing to record */ }
    cursor() { /* same */ }
    select(ids: readonly string[]) { this.selected.push([...ids]); }
}

function spyNavigate(router: Router) {
    return vi.spyOn(router, 'navigate').mockResolvedValue(true);
}

describe('project canvas', () => {
    let fixture: ComponentFixture<ProjectCanvasComponent>;
    let page: ProjectCanvasComponent;
    let hub: FakeHub;
    let navigate: ReturnType<typeof spyNavigate>;
    const t = en.projects.canvas;

    const el = () => fixture.nativeElement as HTMLElement;
    const text = (selector: string) => el().querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();
    // Every tool carries its word as the button's title; the icon-only ones carry it nowhere else.
    const tools = () => [...el().querySelectorAll('.c-tools button')].map(b => b.getAttribute('title'));
    const headerTag = () => text('app-page-header .tag');
    const items = () => [...el().querySelectorAll('.c-item')] as HTMLElement[];

    async function settle() {
        for (let i = 0; i < 8; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    async function create(setup: (hub: FakeHub) => void = () => { }) {
        hub = new FakeHub();
        setup(hub);
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: AssetsService, useValue: { upload: async () => ({ id: 'a', url: '/media/a.png' }) } },
                { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'p1', boardId: 'b1' })) } },
            ],
        });
        // The page provides its own hub (one board, one socket), so the fake goes in at that level.
        TestBed.overrideComponent(ProjectCanvasComponent, {
            set: { providers: [{ provide: CanvasHubService, useValue: hub }] },
        });
        navigate = spyNavigate(TestBed.inject(Router));
        fixture = TestBed.createComponent(ProjectCanvasComponent);
        page = fixture.componentInstance;
        fixture.detectChanges();
        await settle();
    }

    beforeEach(() => localStorage.clear());

    describe('loading', () => {
        it('says so on the surface and in the header while the socket opens', async () => {
            await create(h => { h.joinOutcome = 'never'; });

            expect(text('.c-hint')).toBe(en.projects.loading);
            expect(headerTag()).toBe(t.status.connecting);
            expect(items()).toEqual([]);
            expect(el().querySelector('.c-banner')).toBeNull();
        });
    });

    describe('empty', () => {
        it('invites the first drop and counts zero', async () => {
            await create();

            expect(text('.c-hint.margin-note')).toBe(t.emptyBoard);
            expect(text('app-page-header .page-title')).toBe('References');
            expect(text('.page-meta')).toContain(t.itemCount(0));
            expect(headerTag()).toBe(t.status.live);
        });
    });

    describe('loaded', () => {
        it('draws one element per item, in its kind, and names it for a reader', async () => {
            await create(h => { h.snapshot.items = THREE; });

            expect(items().map(i => ['is-note', 'is-frame', 'is-link'].find(k => i.classList.contains(k))))
                .toEqual(['is-note', 'is-frame', 'is-link']);
            expect(text('.c-item.is-note .c-note')).toBe('hello');
            expect(text('.c-item.is-frame .c-frame-title')).toBe('Mood');
            expect(el().querySelector<HTMLAnchorElement>('.c-item.is-link a')?.href).toBe('https://example.test/a');
            expect(items()[0].getAttribute('aria-label')).toBe(`${t.addNote}: hello`);
            expect(text('.page-meta')).toContain(t.itemCount(3));
            expect(el().querySelector('.c-hint')).toBeNull();
        });

        it('takes the board background off the snapshot', async () => {
            await create(h => { h.snapshot.items = THREE; });
            expect(el().querySelector('.c-stage')?.classList.contains('is-dots')).toBe(true);
        });

        it('shows who else is here', async () => {
            await create(h => {
                h.snapshot.peers = [
                    { connectionId: 'c1', userId: 'u2', name: 'Ann', colorIndex: 1, role: 'editor' },
                    { connectionId: 'c2', userId: 'u3', name: 'Bo', colorIndex: 2, role: 'viewer' },
                ];
            });

            const faces = [...el().querySelectorAll('.c-peer-face')].map(f => f.textContent?.trim());
            expect(faces).toEqual(['A', 'B']);
            expect(el().querySelector('.c-peers')?.getAttribute('title')).toBe(t.peers(2));
        });

        it('reads "Saving…" while a write is on the wire, and Live once it has landed', async () => {
            await create();
            hub.saving.set(true);
            fixture.detectChanges();
            expect(headerTag()).toBe(en.common.saving);

            hub.saving.set(false);
            fixture.detectChanges();
            expect(headerTag()).toBe(t.status.live);
        });

        it('surfaces a refused write as a banner', async () => {
            await create();
            hub.lastError.set('not yours');
            fixture.detectChanges();
            expect(text('.c-banner.is-bad')).toBe('not yours');
        });

        it('remembers this reader\'s viewport and clamps a zoom from an older shape', async () => {
            localStorage.setItem('cedar.canvas.b1.view', JSON.stringify({ x: 40, y: -12, z: 99 }));
            await create(h => { h.snapshot.items = THREE; });

            expect(page.view()).toEqual({ x: 40, y: -12, z: 4 });

            page.zoomTo(1);
            expect(page.zoomPercent()).toBe(100);
            expect(JSON.parse(localStorage.getItem('cedar.canvas.b1.view') ?? '{}').z).toBe(1);
        });
    });

    describe('error', () => {
        it('a socket that will not open is an overlay with Retry, and the page stays', async () => {
            await create(h => { h.joinOutcome = 'offline'; });

            expect(text('.c-overlay-line')).toBe(t.status.offlineHint);
            expect(text('.c-overlay app-button')).toBe(t.retry);
            expect(page.loadError()).toBe('');
            expect(navigate).not.toHaveBeenCalled();

            hub.joinOutcome = 'live';
            el().querySelector<HTMLButtonElement>('.c-overlay app-button button')?.click();
            await settle();
            expect(hub.joins).toBe(2);
            expect(el().querySelector('.c-overlay')).toBeNull();
            expect(headerTag()).toBe(t.status.live);
        });

        it('a Join the caller may not read goes back to the board list', async () => {
            await create(h => { h.joinOutcome = 'refused'; });

            expect(page.loadError()).toBe(t.boardNotFound);
            expect(navigate).toHaveBeenCalledWith(['/projects', 'p1', 'canvas']);
        });

        it('a board deleted underneath goes back to the board list', async () => {
            await create();
            hub.gone.set(true);
            fixture.detectChanges();
            expect(navigate).toHaveBeenCalledWith(['/projects', 'p1', 'canvas']);
        });
    });

    describe('read-only member', () => {
        const asViewer = (h: FakeHub) => {
            h.snapshot = { ...h.snapshot, items: THREE, role: 'viewer', canWrite: false };
        };

        it('is told so and offered nothing that writes', async () => {
            await create(asViewer);

            expect(text('.c-banner')).toBe(t.readOnly);
            expect(el().querySelector('app-page-header app-button')).toBeNull();
            for (const word of [t.addImage, t.addNote, t.addFrame, t.addLink]) {
                expect(tools()).not.toContain(word);
            }
            expect(tools()).toContain(t.zoomFit);
        });

        it('cannot add, edit or resize, whatever the key or gesture', async () => {
            await create(asViewer);

            page.addNote();
            page.onStageKey(new KeyboardEvent('keydown', { key: 'n' }));
            expect(hub.added).toEqual([]);

            page.beginEdit('n1', 'hello');
            expect(page.editing()).toBe('');

            page.setSelection(new Set(['n1']));
            fixture.detectChanges();
            expect(el().querySelectorAll('.c-handle').length).toBe(0);
            expect(text('.c-count')).toBe(t.selected(1));
            expect(tools()).not.toContain(t.deleteItems);
            expect(tools()).not.toContain(t.bringToFront);

            const drag = new Event('dragover', { cancelable: true }) as DragEvent;
            page.onDragOver(drag);
            expect(drag.defaultPrevented).toBe(false);
        });
    });

    describe('owner', () => {
        it('has the four add tools and the primary Note action', async () => {
            await create();

            expect(text('app-page-header app-button')).toBe(t.addNote);
            for (const word of [t.addImage, t.addNote, t.addFrame, t.addLink]) {
                expect(tools()).toContain(word);
            }

            page.setSelection(new Set(['n1']));
            fixture.detectChanges();
            expect(tools()).toContain(t.deleteItems);
            expect(tools()).toContain(t.bringToFront);
            expect(tools()).toContain(t.lock);

            const drag = new Event('dragover', { cancelable: true }) as DragEvent;
            page.onDragOver(drag);
            expect(drag.defaultPrevented).toBe(true);
        });

        it('adds a note at its default size, selects it and opens it for typing', async () => {
            await create();

            page.addNote();
            await settle();

            expect(hub.added.length).toBe(1);
            expect(hub.added[0][0]).toMatchObject({ kind: 'note', width: 220, height: 180, payload: { text: '', align: 'left' } });
            expect([...page.selection()]).toEqual(['new-1']);
            expect(page.editing()).toBe('new-1');
            expect(hub.selected.at(-1)).toEqual(['new-1']);
        });

        it('shows resize handles on a single selected item and none on a locked one', async () => {
            await create(h => { h.snapshot.items = THREE; });

            page.setSelection(new Set(['n1']));
            fixture.detectChanges();
            expect(el().querySelectorAll('.c-handle').length).toBe(4);

            page.toggleLock();
            fixture.detectChanges();
            expect(el().querySelectorAll('.c-handle').length).toBe(0);
            expect(items()[0].getAttribute('aria-label')).toBe(`${t.addNote}: hello · ${t.locked}`);
            expect(JSON.parse(localStorage.getItem('cedar.canvas.b1.locked') ?? '[]')).toEqual(['n1']);
        });

        it('deletes what is selected, skipping locked items', async () => {
            await create(h => { h.snapshot.items = THREE; });

            page.setSelection(new Set(['n1', 'f1']));
            page.toggleLock();
            page.setSelection(new Set(['n1', 'f1', 'l1']));
            const removing = page.deleteSelected();
            expect(hub.deleted).toEqual([]);
            const confirm = document.querySelector('app-confirmation-dialog .danger') as HTMLButtonElement;
            expect(confirm).not.toBeNull();
            confirm.click();
            await removing;
            fixture.detectChanges();

            expect(hub.deleted).toEqual([['l1']]);
            expect(items().length).toBe(2);
            expect(page.selection().size).toBe(0);
        });

        it('keeps selection and content when deletion is cancelled', async () => {
            await create(h => { h.snapshot.items = THREE; });
            vi.spyOn(TestBed.inject(ConfirmationService), 'confirm').mockResolvedValue(false);
            page.setSelection(new Set(['n1']));
            await page.deleteSelected();
            expect(hub.deleted).toEqual([]);
            expect(page.selection().has('n1')).toBe(true);
            expect(hub.items()).toHaveLength(3);
        });

        it('answers the surface keys', async () => {
            await create(h => { h.snapshot.items = THREE; });
            const key = (key: string, init: KeyboardEventInit = {}) =>
                page.onStageKey(new KeyboardEvent('keydown', { key, ...init }));

            key('a', { ctrlKey: true });
            expect(page.selection().size).toBe(3);
            key('Escape');
            expect(page.selection().size).toBe(0);
            key('n');
            await settle();
            expect(hub.added.length).toBe(1);
            key('0');
            expect(page.zoomPercent()).toBe(100);
            key('+');
            expect(page.zoomPercent()).toBe(120);
        });

        it('commits an edited note through the hub and closes the editor', async () => {
            await create(h => { h.snapshot.items = THREE; });

            page.beginEdit('n1', 'hello');
            page.editText.set('hello again');
            await page.commitEdit();

            expect(page.editing()).toBe('');
            expect(hub.updated).toEqual([[{ id: 'n1', payload: { text: 'hello again', align: 'left' } }]]);
        });

        it('takes a link only with a scheme, and updates rather than adds when editing one', async () => {
            await create(h => { h.snapshot.items = THREE; });

            page.linkForm.set({ id: '', url: 'example.test', title: '' });
            await page.submitLink();
            expect(hub.added).toEqual([]);
            expect(page.linkForm()).not.toBeNull();

            page.linkForm.set({ id: 'l1', url: 'https://example.test/b', title: 'B' });
            await page.submitLink();
            expect(hub.updated).toEqual([[{ id: 'l1', payload: { url: 'https://example.test/b', title: 'B', favicon: '' } }]]);
            expect(page.linkForm()).toBeNull();
        });

        it('leaves the board when the page goes', async () => {
            await create();
            fixture.destroy();
            expect(hub.left).toBe(1);
        });
    });
});
