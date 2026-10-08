import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { AssetDetail, AssetEntry, AssetIndexService, AssetPage, AssetQuery, LinkedDocument } from '../core/asset-index.service';
import { AssetSyncService } from '../core/asset-sync.service';
import { AuthService } from '../core/auth.service';
import { en } from '@localization/en';
import { LocaleService } from '../core/i18n/locale.service';
import { ru } from '@localization/ru';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { ProjectAssetsComponent } from './project-assets.component';

function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (reason?: unknown) => void;
    const promise = new Promise<T>((ok, fail) => {
        resolve = ok;
        reject = fail;
    });
    return { promise, resolve, reject };
}

const ASSET = {
    id: 'a1', relativePath: 'Art/hero.png', fileName: 'hero.png', extension: '.png', kind: 'image' as const,
    sizeBytes: 2048, modifiedAt: '2026-08-11T12:00:00Z', indexedAt: '2026-08-11T12:05:00Z',
    missingSince: null, width: 640, height: 360, durationMs: null, sampleRate: null,
    hasThumbnail: false, canHaveThumbnail: true,
};

const PAGE: AssetPage = {
    rootPath: 'D:\\Game', sourceMachine: { id: 'machine-1', name: 'DEV-PC' },
    indexedAt: '2026-08-11T12:05:00Z', total: 1, totalIndexed: 1, missingCount: 0,
    thumbnailsPending: 1, byKind: { image: 1 }, items: [ASSET],
};

class FakeIndex {
    getCalls: string[] = [];
    queries: AssetQuery[] = [];
    listHandler: ((_projectId: string, query: AssetQuery) => Promise<AssetPage>) | null = null;
    getHandler: ((_projectId: string, assetId: string) => Promise<AssetDetail>) | null = null;
    linksHandler: ((_projectId: string, assetId: string) => Promise<LinkedDocument[]>) | null = null;
    async list(_projectId: string, query: AssetQuery = {}) {
        this.queries.push(query);
        if (this.listHandler) return this.listHandler(_projectId, query);
        return structuredClone(PAGE);
    }
    async get(_projectId: string, assetId: string) {
        this.getCalls.push(assetId);
        if (this.getHandler) return this.getHandler(_projectId, assetId);
        return { ...ASSET, fullPath: null, sourceMachine: PAGE.sourceMachine };
    }
    async links(projectId: string, assetId: string) {
        if (this.linksHandler) return this.linksHandler(projectId, assetId);
        return [];
    }
    thumbnailUrl(_projectId: string, assetId: string) { return `/thumb/${assetId}`; }
}

// The virtual viewport renders its window a microtask after the data arrives, so a view switch
// needs a settle before the rows exist.
async function settle(fixture: ComponentFixture<unknown>) {
    for (let tick = 0; tick < 3; tick++) await fixture.whenStable();
    fixture.detectChanges();
}

function assetPage(from: number, count: number, total: number): AssetPage {
    const items = Array.from({ length: Math.max(0, Math.min(count, total - from)) }, (_, i) => ({
        ...ASSET, id: `a${from + i}`, fileName: `file-${from + i}.png`, relativePath: `Art/file-${from + i}.png`,
    }));
    return { ...PAGE, total, totalIndexed: total, byKind: { image: total }, items };
}

describe('project assets', () => {
    let fixture: ComponentFixture<ProjectAssetsComponent>;
    let api: FakeIndex;
    const root = () => fixture.nativeElement as HTMLElement;

    beforeEach(async () => {
        localStorage.removeItem('cedar.assetView');
        api = new FakeIndex();
        TestBed.configureTestingModule({
            providers: [
                { provide: AssetIndexService, useValue: api },
                {
                    provide: AssetSyncService,
                    useValue: {
                        running: signal(false), indexPercent: signal(0), progress: signal(null),
                        cancel: vi.fn(), run: vi.fn().mockResolvedValue(undefined),
                    },
                },
                { provide: AuthService, useValue: {} },
                {
                    provide: ProjectsService,
                    useValue: {
                        get: async () => ({ id: 'p1', name: 'Cedar Quest', documents: [] } as unknown as ProjectDetail),
                        refileAssets: async () => ({ filed: 0 }),
                    },
                },
                { provide: LocaleService, useValue: new LocaleService() },
                { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'p1' })) } },
            ],
        });

        fixture = TestBed.createComponent(ProjectAssetsComponent);
        fixture.componentInstance.source.set('disk');
        fixture.detectChanges();
        await fixture.componentInstance.load();
        fixture.detectChanges();
    });

    it('wraps the non-tabular source strip instead of creating horizontal scrolling', () => {
        const strip = root().querySelector<HTMLElement>('app-index-tabs.source-strip')!;
        const style = getComputedStyle(strip);

        expect(style.flexWrap).toBe('wrap');
        expect(style.maxWidth).toBe('100%');
        expect(['auto', 'scroll']).not.toContain(style.overflowX);
    });

    it('keeps grid and list selection on native buttons and gives the list table semantics', async () => {
        const tile = root().querySelector<HTMLButtonElement>('button.tile')!;
        expect(tile.tagName).toBe('BUTTON');
        expect(tile.getAttribute('aria-pressed')).toBe('false');

        tile.click();
        await fixture.whenStable();
        fixture.detectChanges();
        expect(tile.getAttribute('aria-pressed')).toBe('true');

        fixture.componentInstance.setView('list');
        await settle(fixture);
        const table = root().querySelector<HTMLElement>('.asset-table')!;
        const row = table.querySelector<HTMLElement>('.asset-row:not(.head-row)')!;
        const openButton = row.querySelector<HTMLButtonElement>('button.asset-open')!;
        expect(table.getAttribute('role')).toBe('table');
        expect(table.getAttribute('aria-rowcount')).toBe('2');
        expect(root().querySelector('[role="grid"]')).toBeNull();
        expect(row.getAttribute('role')).toBe('row');
        expect(row.getAttribute('aria-rowindex')).toBe('2');
        expect(row.querySelectorAll('[role="cell"]').length).toBe(5);
        expect(openButton.tagName).toBe('BUTTON');
        expect(openButton.getAttribute('role')).toBeNull();
        expect(openButton.getAttribute('aria-pressed')).toBe('true');

        const before = api.getCalls.length;
        openButton.click();
        await fixture.whenStable();
        expect(api.getCalls.length).toBe(before + 1);
    });

    it('sorts the full server collection before paging and marks the active column', async () => {
        fixture.componentInstance.setView('list');
        fixture.detectChanges();

        expect(api.queries.at(-1)).toMatchObject({ sort: 'path', direction: 'asc', skip: 0 });
        const pathHeader = root().querySelector<HTMLElement>('[role="columnheader"][aria-sort="ascending"]')!;
        expect(pathHeader.textContent).toContain(fixture.componentInstance.t().projects.assets.colFile);

        const modifiedHeader = [...root().querySelectorAll<HTMLElement>('[role="columnheader"]')]
            .find(header => header.textContent?.includes(fixture.componentInstance.t().projects.assets.colModified))!;
        modifiedHeader.querySelector<HTMLButtonElement>('button')!.click();
        await fixture.whenStable();
        fixture.detectChanges();

        expect(api.queries.at(-1)).toMatchObject({ sort: 'modified', direction: 'desc', skip: 0 });
        expect(modifiedHeader.getAttribute('aria-sort')).toBe('descending');
    });

    // T-142 — no pager: the list keeps the server's page size and appends the next page behind the
    // scroll, so the row count a reader hears is the index's, not the page's.
    it('appends the next server page behind the scroll instead of paging', async () => {
        api.listHandler = async (_projectId, query) => assetPage(query.skip ?? 0, 60, 130);
        await fixture.componentInstance.load();
        await settle(fixture);

        expect(fixture.componentInstance.items().length).toBe(60);
        expect(root().querySelector('.pager')).toBeNull();
        expect(root().querySelector('.asset-grid')?.getAttribute('role')).toBe('list');
        expect(root().querySelector('[role="listitem"]')?.getAttribute('aria-setsize')).toBe('130');
        expect(fixture.componentInstance.rangeLabel()).toBe('60 / 130');

        await fixture.componentInstance.loadMore();
        expect(api.queries.at(-1)).toMatchObject({ skip: 60, take: 60 });
        expect(fixture.componentInstance.items().map(a => a.id).slice(58, 62)).toEqual(['a58', 'a59', 'a60', 'a61']);

        // A short page is the end even though the count promised more.
        api.listHandler = async (_projectId, query) => assetPage(query.skip ?? 0, 5, 130);
        await fixture.componentInstance.loadMore();
        expect(fixture.componentInstance.items().length).toBe(125);
        expect(fixture.componentInstance.hasMore()).toBe(false);
        await fixture.componentInstance.loadMore();
        expect(fixture.componentInstance.items().length).toBe(125);

        // A new filter starts over from the first page.
        api.listHandler = async (_projectId, query) => assetPage(query.skip ?? 0, 60, 90);
        const before = api.queries.length;
        fixture.componentInstance.setKind('image');
        await settle(fixture);
        expect(api.queries[before]).toMatchObject({ kind: 'image', skip: 0 });
        expect(fixture.componentInstance.items()[0].id).toBe('a0');
        // The test window has no height, so the fill check keeps asking until the count is met.
        await vi.waitFor(() => expect(fixture.componentInstance.items().length).toBe(90));
    });

    it('draws the list through a fixed-height virtual viewport that counts every row', async () => {
        api.listHandler = async (_projectId, query) => assetPage(query.skip ?? 0, 60, 130);
        await fixture.componentInstance.load();
        fixture.componentInstance.setView('list');
        await settle(fixture);

        const viewport = root().querySelector<HTMLElement>('cdk-virtual-scroll-viewport.asset-rows')!;
        expect(viewport).not.toBeNull();
        expect(viewport.getAttribute('role')).toBe('rowgroup');
        expect(root().querySelector('.asset-table')?.getAttribute('aria-rowcount')).toBe('131');
        const rendered = [...viewport.querySelectorAll<HTMLElement>('.asset-row')];
        expect(rendered.length).toBeGreaterThan(0);
        expect(rendered.length).toBeLessThan(60);
        expect(rendered[0].getAttribute('aria-rowindex')).toBe('2');
    });

    it('does not promise localized alphabet order for the canonical type sort', () => {
        expect(en.projects.assets.sortTypeAsc).toBe('Type: ascending');
        expect(en.projects.assets.sortTypeDesc).toBe('Type: descending');
        expect(ru.projects.assets.sortTypeAsc).toBe('Тип: по возрастанию');
        expect(ru.projects.assets.sortTypeDesc).toBe('Тип: по убыванию');
    });

    it('ignores an older failed list request after a newer filter response arrives', async () => {
        const older = deferred<AssetPage>();
        const newer = deferred<AssetPage>();
        let request = 0;
        api.listHandler = () => request++ === 0 ? older.promise : newer.promise;

        const olderLoad = fixture.componentInstance.load();
        fixture.componentInstance.setKind('audio');
        expect(request).toBe(2);
        newer.resolve({
            ...PAGE,
            items: [{ ...ASSET, id: 'latest', kind: 'audio', fileName: 'latest.wav', relativePath: 'Audio/latest.wav' }],
        });
        await vi.waitFor(() =>
            expect(fixture.componentInstance.page()?.items.map(item => item.id)).toEqual(['latest']));
        expect(fixture.componentInstance.loading()).toBe(false);

        older.reject(new Error('stale request'));
        await olderLoad;
        fixture.detectChanges();
        expect(fixture.componentInstance.page()?.items.map(item => item.id)).toEqual(['latest']);
        expect(fixture.componentInstance.loadError()).toBeNull();
    });

    it('keeps the most recently opened asset when detail responses finish out of order', async () => {
        const firstAsset: AssetEntry = { ...ASSET, id: 'first', relativePath: 'Art/first.png', fileName: 'first.png' };
        const secondAsset: AssetEntry = { ...ASSET, id: 'second', relativePath: 'Art/second.png', fileName: 'second.png' };
        const firstDetail = deferred<AssetDetail>();
        const secondDetail = deferred<AssetDetail>();
        const firstLinks = deferred<LinkedDocument[]>();
        const secondLinks = deferred<LinkedDocument[]>();
        api.getHandler = (_projectId, assetId) => assetId === 'first' ? firstDetail.promise : secondDetail.promise;
        api.linksHandler = (_projectId, assetId) => assetId === 'first' ? firstLinks.promise : secondLinks.promise;

        const firstOpen = fixture.componentInstance.open(firstAsset);
        const secondOpen = fixture.componentInstance.open(secondAsset);
        secondDetail.resolve({ ...secondAsset, fullPath: null, sourceMachine: PAGE.sourceMachine });
        secondLinks.resolve([{ id: 'd2', title: 'Second document', documentType: 'post' }]);
        await secondOpen;
        expect(fixture.componentInstance.selected()?.id).toBe('second');
        expect(fixture.componentInstance.links().map(link => link.id)).toEqual(['d2']);

        firstDetail.resolve({ ...firstAsset, fullPath: null, sourceMachine: PAGE.sourceMachine });
        firstLinks.resolve([{ id: 'd1', title: 'First document', documentType: 'post' }]);
        await firstOpen;
        expect(fixture.componentInstance.selected()?.id).toBe('second');
        expect(fixture.componentInstance.links().map(link => link.id)).toEqual(['d2']);
    });
    it('keeps two areas on screen and says so when no file is chosen', async () => {
        const t = en.projects.assets;
        const grid = root().querySelector<HTMLElement>('.assets-grid')!;
        const pane = () => grid.querySelector<HTMLElement>('aside.inspector')!;

        expect(grid.querySelector('section.index')).not.toBeNull();
        expect(pane().classList.contains('is-empty')).toBe(true);
        expect(pane().textContent).toContain(t.noSelectionTitle);
        expect(pane().querySelector('.preview-pane')).toBeNull();

        root().querySelector<HTMLButtonElement>('button.tile')!.click();
        await fixture.whenStable();
        fixture.detectChanges();

        expect(pane().classList.contains('is-empty')).toBe(false);
        expect(pane().querySelector('.preview-pane')).not.toBeNull();
        expect(pane().querySelector('.asset-name')?.textContent).toContain('hero.png');
        expect(grid.querySelector('section.index')).not.toBeNull();
    });

    it('names the scope of a local file and where it comes from', async () => {
        const t = en.projects.assets;
        root().querySelector<HTMLButtonElement>('button.tile')!.click();
        await fixture.whenStable();
        fixture.detectChanges();

        const pane = root().querySelector<HTMLElement>('aside.inspector')!;
        expect(pane.querySelector('.scope-tag')?.textContent?.trim()).toBe(t.scopeLocal);
        const from = pane.querySelector<HTMLElement>('app-spec-row.from-row')!;
        expect(from.querySelector('.label')?.textContent?.trim()).toBe(t.fromLabel);
        expect(from.querySelector('.value')?.textContent?.trim()).toBe('DEV-PC · D:\\Game');
    });

    it('labels the two scopes and explains the one on screen', () => {
        const t = en.projects.assets;
        const labels = [...root().querySelectorAll('app-index-tabs.source-strip .it-label')]
            .map(node => node.textContent?.trim());
        expect(labels).toEqual([t.sourceUploaded, t.sourceDisk]);
        expect(labels).toEqual(['Project files', 'Local files']);
        expect(root().querySelector('.scope-note')?.textContent?.trim()).toBe(t.scopeLocalNote);
        expect(ru.projects.assets.sourceUploaded).toBe('Файлы проекта');
        expect(ru.projects.assets.sourceDisk).toBe('Локальные файлы');
    });

    it('sends a browser without the desktop bridge to the download page', async () => {
        api.listHandler = async () => ({ ...PAGE, rootPath: null, sourceMachine: null, total: 0, totalIndexed: 0, items: [] });
        await fixture.componentInstance.load();
        fixture.detectChanges();

        const link = root().querySelector<HTMLAnchorElement>('.pick-card app-button.get-desktop a')!;
        expect(link.getAttribute('href')).toBe('/download');
    });
});
