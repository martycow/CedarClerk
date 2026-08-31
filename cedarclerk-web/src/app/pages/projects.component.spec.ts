import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProjectsComponent } from './projects.component';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
import { RulerService } from '../core/ruler.service';
import { en } from '../core/i18n/en';

const ONE: ProjectSummary = {
    id: 'p1', name: 'Cedar Quest', description: '', projectType: 'fullgame', coverUrl: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null,
    documentCount: 3, openTaskCount: 8, assetCount: 2481, lastActivityAt: '2026-08-19T11:00:00',
};

const TWO: ProjectSummary = {
    ...ONE, id: 'p2', name: 'Night Lanterns',
    documentCount: 1, openTaskCount: 2, assetCount: 19,
};

// Archived and empty: it is what makes the archived count non-zero without moving any sum, so the
// badge test can tell "the count is bound" apart from "the count happens to be zero".
const OLD: ProjectSummary = {
    ...ONE, id: 'p3', name: 'Paper Lanterns', archivedAt: '2026-06-01T09:00:00',
    documentCount: 0, openTaskCount: 0, assetCount: 0,
};

class FakeProjects {
    list_: ProjectSummary[] = [ONE, TWO, OLD];
    async list() { return structuredClone(this.list_); }
}

describe('project index', () => {
    let fixture: ComponentFixture<ProjectsComponent>;
    let projects: FakeProjects;
    const t = en.projects;

    const el = () => fixture.nativeElement as HTMLElement;
    const rows = () => [...el().querySelectorAll('a.row')] as HTMLAnchorElement[];
    const names = () => rows().map(r => r.querySelector('.name')?.textContent?.trim());
    const tabs = () => [...el().querySelectorAll('app-index-tabs .it-tile')] as HTMLElement[];
    const badges = () => tabs().map(x => x.querySelector('.it-badge')?.textContent?.trim() ?? null);
    const shelf = () => el().querySelector('app-shelf-panel.shelf-right') as HTMLElement;
    const specValue = (label: string) =>
        [...shelf().querySelectorAll('app-spec-row')]
            .find(r => r.querySelector('.label')?.textContent?.trim() === label)
            ?.querySelector('.text')?.textContent?.trim();

    async function create() {
        projects = new FakeProjects();
        TestBed.configureTestingModule({
            providers: [provideRouter([]), { provide: ProjectsService, useValue: projects }],
        });
        fixture = TestBed.createComponent(ProjectsComponent);
        fixture.detectChanges();
        for (let i = 0; i < 5; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(create);

    // ADR-163/ADR-168 rule 3. router.navigate had no URL to hand the browser, so middle click,
    // copy link address and the hover preview all died with it.
    it('makes every row a door with a real address', () => {
        expect(rows().map(r => r.getAttribute('href'))).toEqual(['/projects/p1', '/projects/p2', '/projects/p3']);
        expect(el().querySelectorAll('.rows button').length).toBe(0);
    });

    // ADR-168 rule 2 — the hub's dock is the switcher; this screen is the list, and it marks no
    // project as the open one because none is open from here.
    it('marks no row as current, and keeps the search the dock does not have', () => {
        expect(el().querySelectorAll('.rows [aria-current]').length).toBe(0);
        expect(el().querySelector('app-input.search')).toBeTruthy();
    });

    it('filters the list by the search text', async () => {
        fixture.componentInstance.search.set('night');
        fixture.detectChanges();
        expect(names()).toEqual(['Night Lanterns']);
    });

    it('filters the list by the state tile that was picked', () => {
        fixture.componentInstance.pickFilter('archived');
        fixture.detectChanges();
        expect(names()).toEqual(['Paper Lanterns']);

        fixture.componentInstance.pickFilter('active');
        fixture.detectChanges();
        expect(names()).toEqual(['Cedar Quest', 'Night Lanterns']);
    });

    it('counts every state on its own tile', () => {
        const labels = tabs().map(x => x.querySelector('.it-label')?.textContent?.trim());
        expect(labels).toEqual([t.filterAll, t.filterActive, t.filterArchived]);
        expect(badges()).toEqual(['3', '2', '1']);
    });

    // ADR-164 rule 1 — the badge rules arrive with the component: nothing is drawn at zero. The
    // count that disappears there is still printed in full on the shelf, which is why both halves
    // are asserted together.
    it('drops the badge of a state nothing is in, and still prints its zero on the shelf', () => {
        fixture.componentInstance.projects.set([ONE, TWO]);
        fixture.detectChanges();
        expect(badges()).toEqual(['2', '2', null]);
        expect(specValue(t.filterArchived)).toBe('0');
    });

    it('sums the shelf out of the rows it already has', () => {
        expect(specValue(t.filterActive)).toBe('2');
        expect(specValue(t.filterArchived)).toBe('1');
        expect(specValue(t.colDocs)).toBe('4');      // 3 + 1
        expect(specValue(t.colTasks)).toBe('10');    // 8 + 2
        expect(specValue(t.colAssets)).toBe('2500'); // 2481 + 19
    });

    it('publishes the rule while it is open and clears it on the way out', () => {
        const ruler = TestBed.inject(RulerService);
        expect(ruler.label()).toBe(t.title);
        expect(ruler.left().map(r => r.text)).toEqual([t.sub(3, 2)]);

        fixture.destroy();
        expect(ruler.label()).toBe('');
        expect(ruler.left()).toEqual([]);
    });

    it('offers creation when there is no project at all', async () => {
        fixture.componentInstance.projects.set([]);
        fixture.detectChanges();
        const buttons = [...el().querySelectorAll('.empty-state app-button')].map(b => b.textContent?.trim());
        expect(buttons).toEqual([t.newProject]);
    });

    it('offers Blog with a post starter and an explicit name hint', () => {
        fixture.componentInstance.startCreate();
        fixture.detectChanges();
        const types = [...el().querySelectorAll('.type-name')].map(x => x.textContent?.trim());
        expect(types).toEqual(['Empty', 'Blog', 'Game', 'Product']);
        expect(el().querySelector('.type-row:last-child .type-starter')?.textContent)
            .toContain(t.create.startsWith(t.projectTypes.product.starter));
        expect((el().querySelector('#project-name') as HTMLInputElement).placeholder)
            .toBe('Enter project name here');
    });
});
