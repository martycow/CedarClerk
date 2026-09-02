import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { AssetIndexService, AssetPage } from '../core/asset-index.service';
import { AssetSyncService } from '../core/asset-sync.service';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { ProjectAssetsComponent } from './project-assets.component';

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
    async list() { return structuredClone(PAGE); }
    async get(_projectId: string, assetId: string) {
        this.getCalls.push(assetId);
        return { ...ASSET, fullPath: null, sourceMachine: PAGE.sourceMachine };
    }
    async links() { return []; }
    thumbnailUrl(_projectId: string, assetId: string) { return `/thumb/${assetId}`; }
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

    it('announces grid and list selection and opens list rows with Enter and Space', async () => {
        const tile = root().querySelector<HTMLButtonElement>('button.tile')!;
        expect(tile.tagName).toBe('BUTTON');
        expect(tile.getAttribute('aria-pressed')).toBe('false');

        tile.click();
        await fixture.whenStable();
        fixture.detectChanges();
        expect(tile.getAttribute('aria-pressed')).toBe('true');

        fixture.componentInstance.setView('list');
        fixture.detectChanges();
        const table = root().querySelector<HTMLElement>('.asset-table')!;
        const row = table.querySelector<HTMLElement>('.asset-row:not(.head-row)')!;
        expect(table.getAttribute('role')).toBe('grid');
        expect(row.getAttribute('role')).toBe('row');
        expect(row.getAttribute('aria-selected')).toBe('true');

        const before = api.getCalls.length;
        const space = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
        row.dispatchEvent(space);
        await fixture.whenStable();
        expect(space.defaultPrevented).toBe(true);

        const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
        row.dispatchEvent(enter);
        await fixture.whenStable();
        expect(enter.defaultPrevented).toBe(true);
        expect(api.getCalls.length).toBe(before + 2);
    });
});
