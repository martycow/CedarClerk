import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MediaLibraryComponent } from './media-library.component';
import { AssetsService, LibraryAsset, LibraryKind, LibraryPage } from '../core/assets.service';
import { en } from '../core/i18n/en';

function asset(id: string, contentType: string): LibraryAsset {
    return {
        id, fileName: `${id}.bin`, localPath: `x/${id}`, contentType,
        sizeBytes: 1024, createdAt: '2026-08-01T09:00:00',
    };
}

const PAGE: LibraryPage = {
    items: [asset('a1', 'image/png'), asset('a2', 'video/mp4')],
    total: 2,
    // Audio at zero, so its tile is dropped by the page; no single kind past ninety-nine, so the
    // cap can only show up on the "All" tile — and that tile's badge is then a number no kind
    // carries on its own, which is the only way it says "the sum" rather than "one of these".
    counts: { image: 60, video: 45, audio: 0 },
    usedBytes: 500, limitBytes: 1000,
};

class FakeAssets {
    page: LibraryPage = PAGE;
    queries: { q?: string; type?: LibraryKind | null; skip: number; take: number }[] = [];
    async list(query: { q?: string; type?: LibraryKind | null; skip: number; take: number }) {
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
        [...el().querySelectorAll('app-shelf-panel')]
            .find(p => p.getAttribute('aria-label') === title) as HTMLElement | undefined;

    async function create() {
        api = new FakeAssets();
        TestBed.configureTestingModule({
            providers: [{ provide: AssetsService, useValue: api }],
        });
        fixture = TestBed.createComponent(MediaLibraryComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
    }

    beforeEach(create);

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

    it('the storage shelf stands on its own when nothing is selected', () => {
        const storage = panel(t.storage)!;
        expect(storage).toBeDefined();
        expect(storage.querySelector('.usage-bar')?.getAttribute('aria-valuenow')).toBe('50');
    });
});
