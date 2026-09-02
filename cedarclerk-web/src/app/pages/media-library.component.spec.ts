import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MediaLibraryComponent } from './media-library.component';
import { AssetsService, LibraryAsset, LibraryKind, LibraryPage } from '../core/assets.service';
import { en } from '../core/i18n/en';
import { formatBytes } from '../core/asset-index.service';

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
    queries: { q?: string; type?: LibraryKind | null; project?: string | null; skip: number; take: number }[] = [];
    async list(query: { q?: string; type?: LibraryKind | null; project?: string | null; skip: number; take: number }) {
        this.queries.push(query);
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

        const rows = [...el().querySelectorAll('button.list-row')] as HTMLButtonElement[];
        expect(rows.length).toBe(2);
        expect(rows[0].querySelector('.list-thumbnail img')?.getAttribute('src')).toContain('/media/x/a1');
        expect(rows[1].querySelector('.list-thumbnail app-icon')).not.toBeNull();

        rows[0].click();
        fixture.detectChanges();
        expect(rows[0].getAttribute('aria-pressed')).toBe('true');
    });
});
