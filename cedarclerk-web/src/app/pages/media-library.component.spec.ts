import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MediaLibraryComponent } from './media-library.component';
import { AssetsService, LibraryAsset, LibraryKind, LibraryPage, LibrarySort } from '../core/assets.service';
import { SortDirection } from '../core/collection-query';
import { en } from '@localization/en';
import { formatBytes } from '../core/asset-index.service';

function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (reason?: unknown) => void;
    const promise = new Promise<T>((ok, fail) => {
        resolve = ok;
        reject = fail;
    });
    return { promise, resolve, reject };
}

function asset(id: string, contentType: string): LibraryAsset {
    return {
        id, fileName: `${id}.bin`, localPath: `x/${id}`, contentType,
        sizeBytes: 1024, createdAt: '2026-08-01T09:00:00', projectId: null,
    };
}

const PAGE: LibraryPage = {
    items: [asset('a1', 'image/png'), asset('a2', 'video/mp4')],
    total: 2,
    // Audio at zero, so its tile is dropped by the page; no single kind past ninety-nine, so the
    // cap can only show up on the "All" tile — and that tile's badge is then a number no kind
    // carries on its own, which is the only way it says "the sum" rather than "one of these".
    counts: { image: 60, video: 45, audio: 0 },
    // One bucket only, so the project strip stays out of the way of the tests below: it is drawn
    // from two buckets up, which is what makes it a control rather than a label (ADR-204).
    buckets: [{ projectId: null, count: 2 }],
    usedBytes: 500, limitBytes: 1000,
};

class FakeAssets {
    page: LibraryPage = PAGE;
    listHandler: ((query: {
        q?: string; type?: LibraryKind | null; project?: string | null;
        sort?: LibrarySort; direction?: SortDirection; skip: number; take: number;
    }) => Promise<LibraryPage>) | null = null;
    queries: {
        q?: string; type?: LibraryKind | null; project?: string | null;
        sort?: LibrarySort; direction?: SortDirection; skip: number; take: number;
    }[] = [];
    async list(query: {
        q?: string; type?: LibraryKind | null; project?: string | null;
        sort?: LibrarySort; direction?: SortDirection; skip: number; take: number;
    }) {
        this.queries.push(query);
        if (this.listHandler) return this.listHandler(query);
        return structuredClone(this.page);
    }
    async remove() { /* nothing here deletes */ }
}

describe('media library', () => {
    let fixture: ComponentFixture<MediaLibraryComponent>;
    let api: FakeAssets;
    const t = en.media;

    const el = () => fixture.nativeElement as HTMLElement;
    const strip = (label: string) =>
        [...el().querySelectorAll('app-index-tabs')]
            .find(s => s.getAttribute('aria-label') === label) as HTMLElement;
    const tiles = (label: string) => [...strip(label).querySelectorAll('.it-tile')] as HTMLElement[];
    const tileText = (label: string) =>
        tiles(label).map(x => [x.querySelector('.it-label')?.textContent?.trim(),
                              x.querySelector('.it-badge')?.textContent?.trim() ?? null]);
    const panel = (title: string) =>
        [...el().querySelectorAll('section.card')]
            .find(p => p.getAttribute('aria-label') === title) as HTMLElement | undefined;

    async function create(page: LibraryPage = PAGE) {
        TestBed.resetTestingModule();
        localStorage.removeItem('cedar.mediaView');
        api = new FakeAssets();
        api.page = page;
        TestBed.configureTestingModule({
            providers: [{ provide: AssetsService, useValue: api }],
        });
        fixture = TestBed.createComponent(MediaLibraryComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
    }

    beforeEach(() => create());

    afterEach(() => localStorage.removeItem('cedar.mediaView'));

    it('draws the type filter as index tabs, hiding a kind with nothing in it and capping at 99+', () => {
        expect(tileText(t.typeStrip)).toEqual([
            [t.all, '99+'],     // 60 + 45, so neither kind's own count could be standing in for it
            [t.images, '60'],
            [t.videos, '45'],
        ]);
    });

    it('a chosen tile becomes the type the server is asked for', async () => {
        api.queries.length = 0;
        tiles(t.typeStrip)[2].click();
        await fixture.whenStable();
        expect(api.queries.at(-1)?.type).toBe('video');
    });

    it('opening a file fills the inspector shelf and leaves the list standing', async () => {
        expect(panel(t.fileDetails)).toBeUndefined();

        (el().querySelector('.tile') as HTMLElement).click();
        fixture.detectChanges();

        // The point of the shelf over the modal: both are on screen at once, and the list says
        // which row is being read.
        expect(panel(t.fileDetails)).toBeDefined();
        expect(panel(t.open)).toBeDefined();
        expect(el().querySelector('app-modal')).toBeNull();
        expect(el().querySelector('.tile.is-on')?.textContent).toContain('a1.bin');
    });

    it('the storage card stands on its own when nothing is selected', () => {
        const storage = panel(t.storage)!;
        expect(storage).toBeDefined();
        expect(storage.querySelector('.usage-bar')?.getAttribute('aria-valuenow')).toBe('50');
    });

    it('the header carries the file count and the quota, and an empty library names the next action', async () => {
        const meta = el().querySelector('app-page-header .page-meta')?.textContent ?? '';
        expect(meta).toContain(t.fileCount(105));
        expect(meta).toContain(t.usage(formatBytes(500), formatBytes(1000)));
        expect(el().querySelector('app-empty-state')).toBeNull();

        await create({ ...PAGE, items: [], total: 0, counts: { image: 0, video: 0, audio: 0 }, buckets: [] });
        const empty = el().querySelector('app-empty-state')!;
        expect(empty).not.toBeNull();
        expect(empty.textContent).toContain(t.empty);
        expect(empty.querySelector('button')).not.toBeNull();
    });

    it('gives search a visible label and declares the standalone operational measure', () => {
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        const search = el().querySelector('app-input.search')!;
        expect(search.querySelector('label')?.textContent?.trim()).toBe(t.searchLabel);
        expect(search.querySelector('label')?.getAttribute('for')).toBe('media-search');
        expect(search.querySelector('input')?.id).toBe('media-search');
    });

    it('draws list headers, an image thumbnail, and an icon fallback in bounded rows', () => {
        fixture.componentInstance.setView('list');
        fixture.detectChanges();

        const head = el().querySelector('.list-head')!;
        expect(head.textContent).toContain(t.fileColumn);
        expect(head.textContent).toContain(t.fileType);
        expect(head.textContent).toContain(t.fileSize);
        expect(head.textContent).toContain(t.added);

        const rows = [...el().querySelectorAll('.list-row:not(.list-head)')] as HTMLElement[];
        expect(rows.length).toBe(2);
        expect(el().querySelector('.asset-list')?.getAttribute('role')).toBe('table');
        expect(el().querySelector('[role="grid"]')).toBeNull();
        expect(head.getAttribute('role')).toBe('row');
        expect(rows.every(row => row.getAttribute('role') === 'row')).toBe(true);
        expect(rows.every(row => row.querySelectorAll('[role="cell"]').length === 5)).toBe(true);
        const openButtons = rows.map(row => row.querySelector<HTMLButtonElement>('button.list-open')!);
        expect(openButtons.every(button => button.tagName === 'BUTTON' && button.getAttribute('role') === null)).toBe(true);
        expect(rows[0].querySelector('.list-thumbnail img')?.getAttribute('src')).toContain('/media/x/a1');
        expect(rows[1].querySelector('.list-thumbnail app-icon')).not.toBeNull();

        openButtons[0].click();
        fixture.detectChanges();
        expect(openButtons[0].getAttribute('aria-pressed')).toBe('true');
    });

    it('sends the selected order before paging and exposes aria-sort on the active header', async () => {
        fixture.componentInstance.setView('list');
        fixture.detectChanges();

        expect(api.queries[0]).toMatchObject({ sort: 'added', direction: 'desc', skip: 0 });
        const fileHeader = () => [...el().querySelectorAll<HTMLElement>('[role="columnheader"]')]
            .find(header => header.textContent?.includes(t.fileColumn))!;
        expect(fileHeader().getAttribute('aria-sort')).toBeNull();

        fileHeader().querySelector<HTMLButtonElement>('button')!.click();
        await fixture.whenStable();
        fixture.detectChanges();
        expect(api.queries.at(-1)).toMatchObject({ sort: 'name', direction: 'asc', skip: 0 });
        expect(fileHeader().getAttribute('aria-sort')).toBe('ascending');

        fileHeader().querySelector<HTMLButtonElement>('button')!.click();
        await fixture.whenStable();
        fixture.detectChanges();
        expect(api.queries.at(-1)).toMatchObject({ sort: 'name', direction: 'desc', skip: 0 });
        expect(fileHeader().getAttribute('aria-sort')).toBe('descending');
    });

    it('keeps the newest list response when an older request finishes last', async () => {
        const older = deferred<LibraryPage>();
        const newer = deferred<LibraryPage>();
        let request = 0;
        api.listHandler = () => request++ === 0 ? older.promise : newer.promise;

        const olderLoad = fixture.componentInstance.load();
        fixture.componentInstance.setType('video');
        expect(request).toBe(2);

        const latestPage = {
            ...PAGE,
            items: [asset('latest', 'video/mp4')],
            total: 1,
        };
        newer.resolve(latestPage);
        await vi.waitFor(() =>
            expect(fixture.componentInstance.page()?.items.map(item => item.id)).toEqual(['latest']));
        expect(fixture.componentInstance.loading()).toBe(false);

        older.resolve({ ...PAGE, items: [asset('stale', 'image/png')], total: 1 });
        await olderLoad;
        fixture.detectChanges();
        expect(fixture.componentInstance.page()?.items.map(item => item.id)).toEqual(['latest']);
        expect(fixture.componentInstance.loadError()).toBeNull();
    });
});
