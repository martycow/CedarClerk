import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DraftsPageComponent } from './drafts.component';
import { DraftMeta, DraftsService, FolderMeta } from '../core/drafts.service';
import { FoldersService } from '../core/folders.service';
import { en } from '../core/i18n/en';

async function settle(fixture: ComponentFixture<unknown>) {
    for (let link = 0; link < 5; link++) await fixture.whenStable();
}

function draft(id: string, over: Partial<DraftMeta> = {}): DraftMeta {
    return {
        id, title: id, createdAt: '2026-08-01T09:00:00', updatedAt: '2026-08-01T09:00:00',
        primaryLanguage: 'ru', blogSlug: null, isBlogPublished: false, blogPublishedAt: null,
        languages: [], tags: '', isArchived: false, lastTelegramMessageId: null,
        lastTelegramUsername: null, staleLanguages: [], scheduled: null, folderId: null,
        seriesId: null, projectId: null, parentDraftId: null, siblingOrder: 0, isPrivate: false, isTemplate: false,
        disableCopy: false, disableReactions: false, disableComments: false,
        viewCount: 0, reactionCount: 0, newViewCount: 0, newReactionCount: 0, ...over,
    };
}

// Three drafts, one archived and one filed. Nothing here is a template, scheduled or published, so
// the tiles that must lose their badge are genuinely at zero — and "All" excludes the archived one,
// which is why its badge and the folder shelf's whole-library tally are allowed to disagree.
const DRAFTS: DraftMeta[] = [
    draft('alpha', { folderId: 'f1' }),
    draft('beta'),
    draft('gamma', { isArchived: true }),
];

const FOLDERS: FolderMeta[] = [{ id: 'f1', name: 'Devlogs', count: 1 }];

class FakeDrafts {
    async list() { return structuredClone(DRAFTS); }
    async listFolders() { return structuredClone(FOLDERS); }
}

describe('drafts page', () => {
    let fixture: ComponentFixture<DraftsPageComponent>;
    const t = en.drafts;

    const el = () => fixture.nativeElement as HTMLElement;
    const strip = (label: string) =>
        [...el().querySelectorAll('app-index-tabs')]
            .find(s => s.getAttribute('aria-label') === label) as HTMLElement | undefined;
    const tiles = (label: string) =>
        [...(strip(label)?.querySelectorAll('.it-tile') ?? [])] as HTMLElement[];
    const tileText = (label: string) =>
        tiles(label).map(x => [x.querySelector('.it-label')?.textContent?.trim(),
                               x.querySelector('.it-badge')?.textContent?.trim() ?? null]);
    const panel = (title: string) =>
        [...el().querySelectorAll('app-shelf-panel')]
            .find(p => p.getAttribute('aria-label') === title) as HTMLElement | undefined;
    const rowTitles = () =>
        [...el().querySelectorAll('.drafts-table .drafts-row:not(.drafts-row-head) .drafts-title')]
            .map(x => x.textContent!.trim());

    async function create() {
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: DraftsService, useClass: FakeDrafts },
                FoldersService,
            ],
        });
        fixture = TestBed.createComponent(DraftsPageComponent);
        fixture.detectChanges();
        // ngOnInit's load is a chain — Promise.all over the draft list and FoldersService, which
        // awaits the folder request inside itself — and whenStable settles one link of it per
        // call, so a single await leaves the page still saying "Loading…".
        await settle(fixture);
        fixture.detectChanges();
    }

    beforeEach(create);

    it('draws the state filter as index tabs whose badges drop at zero', () => {
        expect(tileText(t.stateStrip)).toEqual([
            [t.filters.all, '2'],
            [t.filters.draft, '2'],
            [t.filters.scheduled, null],
            [t.filters.published, null],
            [t.filters.attention, null],
            [t.filters.archived, '1'],
            [t.filters.template, null],
        ]);
    });

    it('a chosen state tile filters the list', () => {
        expect(rowTitles()).toEqual(['alpha', 'beta']);

        tiles(t.stateStrip)[5].click();
        fixture.detectChanges();

        expect(rowTitles()).toEqual(['gamma']);
    });

    it('the folders shelf filters by folder and counts what has none', () => {
        const shelf = panel(t.folders.title)!;
        const rows = [...shelf.querySelectorAll('.folder-row')] as HTMLElement[];
        expect(rows.map(r => [r.querySelector('.folder-name')!.textContent!.trim(),
                              r.querySelector('.folder-count')!.textContent!.trim()]))
            .toEqual([[t.folders.all, '3'], [t.folders.none, '2'], ['Devlogs', '1']]);

        rows[2].click();
        fixture.detectChanges();
        expect(rowTitles()).toEqual(['alpha']);
    });

    it('tree view withdraws both filters — the state strip and the folders shelf', () => {
        tiles(t.viewStrip)[2].click();
        fixture.detectChanges();

        expect(el().querySelector('.drafts-tree')).not.toBeNull();
        expect(strip(t.stateStrip)).toBeUndefined();
        expect(panel(t.folders.title)).toBeUndefined();
    });
});
