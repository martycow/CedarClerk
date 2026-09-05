import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DraftsPageComponent, TreeRow, treeDropTarget } from './drafts.component';
import { DraftMeta, DraftsService, FolderMeta, SeriesMeta } from '../core/drafts.service';
import { FoldersService } from '../core/folders.service';
import { SeriesService } from '../core/series.service';
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
        disableCopy: false, disableReactions: false, disableComments: false, documentType: 'post',
        viewCount: 0, reactionCount: 0, newViewCount: 0, newReactionCount: 0, coverImagePath: null, ...over,
    };
}

// Three drafts, one archived and one filed. Nothing here is a template, scheduled or published, so
// the tiles that must lose their badge are genuinely at zero — and "All" excludes the archived one,
// which is why its badge and the folder shelf's whole-library tally are allowed to disagree.
const DRAFTS: DraftMeta[] = [
    draft('alpha', { folderId: 'f1', seriesId: 's1', tags: 'devlog,art' }),
    draft('beta'),
    draft('gamma', { isArchived: true }),
];

const FOLDERS: FolderMeta[] = [{ id: 'f1', name: 'Devlogs', count: 1 }];
const SERIES: SeriesMeta[] = [{ id: 's1', name: 'Season one', slug: 'season-one', description: null, count: 1 }];

class FakeDrafts {
    moves: { id: string; parentId: string | null; beforeId?: string }[] = [];
    async list() { return structuredClone(DRAFTS); }
    async listFolders() { return structuredClone(FOLDERS); }
    async listSeries() { return structuredClone(SERIES); }
    async setDraftParent(id: string, parentId: string | null, beforeId?: string) {
        this.moves.push({ id, parentId, beforeId });
        return { parentDraftId: parentId, siblingOrder: 0 };
    }
}

// ADR-283 — the drop math over a visible tree with the dragged row already taken out.
describe('tree drop target', () => {
    const row = (id: string, depth: number): TreeRow => ({ d: draft(id), depth, hasChildren: false });
    // A, its child A1, then C at the top level.
    const rows = [row('A', 0), row('A1', 1), row('C', 0)];

    it('lands under the row above when the pointer asks for one level deeper', () => {
        expect(treeDropTarget(rows, 2, 1)).toEqual({ depth: 1, parentId: 'A', beforeId: undefined });
    });

    it('goes no deeper than one level under the row above', () => {
        expect(treeDropTarget(rows, 2, 5)).toEqual({ depth: 2, parentId: 'A1', beforeId: undefined });
        expect(treeDropTarget(rows, 0, 3)).toEqual({ depth: 0, parentId: null, beforeId: 'A' });
    });

    it('goes no shallower than the row below', () => {
        expect(treeDropTarget(rows, 1, 0)).toEqual({ depth: 1, parentId: 'A', beforeId: 'A1' });
    });

    it('at the top level the next root row is the sibling to go before', () => {
        expect(treeDropTarget(rows, 2, 0)).toEqual({ depth: 0, parentId: null, beforeId: 'C' });
        expect(treeDropTarget(rows, 3, 0)).toEqual({ depth: 0, parentId: null, beforeId: undefined });
    });
});

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
        [...el().querySelectorAll('section.card')]
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
                SeriesService,
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

    it('exposes the active table order through sortable column headers', () => {
        expect(el().querySelector('.drafts-table')?.getAttribute('role')).toBe('table');
        expect(el().querySelector('[role="grid"]')).toBeNull();
        expect(el().querySelector('.drafts-row-head')?.getAttribute('role')).toBe('row');
        expect([...el().querySelectorAll('.drafts-row:not(.drafts-row-head)')]
            .every(row => row.getAttribute('role') === 'row' && !!row.querySelector('[role="cell"]'))).toBe(true);
        expect([...el().querySelectorAll<HTMLAnchorElement>('.draft-link')]
            .every(link => link.tagName === 'A' && link.getAttribute('role') === null)).toBe(true);
        const title = [...el().querySelectorAll<HTMLElement>('[role="columnheader"]')]
            .find(header => header.textContent?.includes(t.columns.title))!;
        expect(title.getAttribute('aria-sort')).toBeNull();

        title.querySelector<HTMLButtonElement>('button')!.click();
        fixture.detectChanges();
        expect(title.getAttribute('aria-sort')).toBe('descending');
        expect(rowTitles()).toEqual(['beta', 'alpha']);

        title.querySelector<HTMLButtonElement>('button')!.click();
        fixture.detectChanges();
        expect(title.getAttribute('aria-sort')).toBe('ascending');
        expect(rowTitles()).toEqual(['alpha', 'beta']);
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

    it('the header carries the library counts and the one primary action', () => {
        const header = el().querySelector('app-page-header')!;
        expect(header.querySelector('.page-title')!.textContent!.trim()).toBe(t.title);
        expect(header.querySelector('.page-meta')!.textContent).toContain(t.postsCount(3));
        expect(header.querySelector('[primary] .btn')!.textContent!.trim()).toBe(t.newDraft);
        expect(el().querySelector('app-shelf-panel')).toBeNull();
    });

    it('a filter that matches nothing offers the way back to everything', () => {
        tiles(t.stateStrip)[2].click(); // Scheduled — nothing is
        fixture.detectChanges();

        const empty = el().querySelector('app-empty-state')!;
        expect(empty.querySelector('.es-title')!.textContent!.trim()).toBe(t.empty.noMatchTitle);
        (empty.querySelector('button') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(el().querySelector('app-empty-state')).toBeNull();
        expect(rowTitles()).toEqual(['alpha', 'beta']);
    });

    // ---- T-256, the draft shelf ---------------------------------------------------------------

    const rows = () =>
        [...el().querySelectorAll('.drafts-table .drafts-row:not(.drafts-row-head)')] as HTMLElement[];
    const describeButton = (index: number) =>
        rows()[index].querySelector('.row-actions .mini') as HTMLButtonElement;
    const specRows = () =>
        [...(panel(t.inspector.title)?.querySelectorAll('app-spec-row') ?? [])]
            .map(r => [r.querySelector('.label')!.textContent!.trim(),
                       r.querySelector('.value')!.textContent!.replace(/\s+/g, ' ').trim()]);

    it('the describe control fills the shelf, and the shelf is exclusive with the folder filter', () => {
        expect(panel(t.folders.title)).toBeDefined();
        expect(panel(t.inspector.title)).toBeUndefined();

        describeButton(0).click();
        fixture.detectChanges();

        // Exclusive (ADR-167): the picked document replaces the folder filter, never joins it.
        expect(panel(t.inspector.title)).toBeDefined();
        expect(panel(t.folders.title)).toBeUndefined();
        expect(panel(t.inspector.title)!.querySelector('.insp-name')!.textContent!.trim()).toBe('alpha');
        expect(specRows()).toContainEqual([t.inspector.folder, 'Devlogs']);
        expect(specRows()).toContainEqual([t.inspector.series, 'Season one']);
        expect(specRows()).toContainEqual([t.inspector.tags, '#devlog#art']);
    });

    it('the control toggles, and the draft destination remains a native link', () => {
        const link = rows()[0].querySelector<HTMLAnchorElement>('.draft-link')!;
        expect(link.getAttribute('href')).toBe('/editor?draft=alpha');

        describeButton(0).click();
        fixture.detectChanges();
        expect(link.getAttribute('href')).toBe('/editor?draft=alpha');

        describeButton(0).click();
        fixture.detectChanges();
        expect(panel(t.inspector.title)).toBeUndefined();
        expect(panel(t.folders.title)).toBeDefined();
    });

    it('a picked draft that leaves the filtered list stops being described', () => {
        describeButton(0).click();
        fixture.detectChanges();
        expect(panel(t.inspector.title)).toBeDefined();

        tiles(t.stateStrip)[5].click(); // Archived — alpha is not in it
        fixture.detectChanges();

        expect(panel(t.inspector.title)).toBeUndefined();
        expect(panel(t.folders.title)).toBeDefined();
    });

    it('tree rows drag from a grip and a drop calls the same move the menu uses', async () => {
        const drafts = TestBed.inject(DraftsService) as unknown as FakeDrafts;
        tiles(t.viewStrip)[2].click();
        fixture.detectChanges();

        const treeRows = [...el().querySelectorAll<HTMLElement>('.drafts-tree .tree-row')];
        expect(el().querySelector('.drafts-tree')!.hasAttribute('cdkdroplist')).toBe(true);
        expect(treeRows.map(r => r.querySelector('.tree-title')!.textContent!.trim())).toEqual(['alpha', 'beta']);
        expect(treeRows.every(r => r.classList.contains('cdk-drag') && !!r.querySelector('.tree-grip'))).toBe(true);
        expect(treeRows[1].style.paddingLeft).toContain('* 0');

        // Beta, dragged to just below alpha and an indent to the right, becomes alpha's child.
        const page = fixture.componentInstance;
        const beta = page.treeRows()[1];
        page.onTreeDragStart(beta);
        expect(page.dropDepth()).toBe(0);
        await page.onTreeDrop({ currentIndex: 1, distance: { x: 30, y: 0 } } as never);
        await settle(fixture);
        fixture.detectChanges();

        expect(drafts.moves).toEqual([{ id: 'beta', parentId: 'alpha', beforeId: undefined }]);
        const after = [...el().querySelectorAll<HTMLElement>('.drafts-tree .tree-row')];
        expect(after[1].style.paddingLeft).toContain('* 1');
        expect(after[0].querySelector('.tree-caret')).not.toBeNull();

        // Dropping back where it already sits sends nothing.
        page.onTreeDragStart(page.treeRows()[1]);
        await page.onTreeDrop({ currentIndex: 1, distance: { x: 0, y: 0 } } as never);
        expect(drafts.moves.length).toBe(1);
    });

    it('tree view keeps no shelf even with a draft picked', () => {
        describeButton(0).click();
        fixture.detectChanges();

        tiles(t.viewStrip)[2].click();
        fixture.detectChanges();

        expect(panel(t.inspector.title)).toBeUndefined();
        expect(panel(t.folders.title)).toBeUndefined();
    });
});
