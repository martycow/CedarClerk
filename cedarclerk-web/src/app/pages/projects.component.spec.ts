import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProjectsComponent } from './projects.component';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
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

// Archived and empty: it is what makes the archived count non-zero without moving any sum.
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
    const rows = () => [...el().querySelectorAll('a.project-card')] as HTMLAnchorElement[];
    const names = () => rows().map(r => r.querySelector('.card-name')?.textContent?.trim());
    const tabs = () => [...el().querySelectorAll('app-index-tabs .it-tile')] as HTMLElement[];
    const meta = () => [...el().querySelectorAll('app-page-header .page-meta > span:not(.sep)')]
        .map(x => x.textContent?.trim());

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
    it('makes every card a door with a real address', () => {
        expect(rows().map(r => r.getAttribute('href'))).toEqual(['/projects/p1', '/projects/p2', '/projects/p3']);
        expect(el().querySelectorAll('.cards button:not(app-button button)').length).toBe(0);
    });

    // ADR-168 rule 2 — the sidebar's switcher is the switch; this screen is the list, and it marks
    // no project as the open one because none is open from here.
    it('marks no card as current, and keeps the search the switcher does not have', () => {
        expect(el().querySelectorAll('.cards [aria-current]').length).toBe(0);
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

    it('offers the four states on one strip', () => {
        const labels = tabs().map(x => x.querySelector('.it-label')?.textContent?.trim());
        expect(labels).toEqual([t.filterAll, t.filterActive, t.filterArchived, t.filterShared]);
    });

    // ADR-239 clause 6 — the rule's readout is the header's meta line now, zeros included.
    it('counts active and archived in the header, and prints the zero', () => {
        expect(meta()).toEqual([t.activeCount(2), t.archivedCount(1)]);

        fixture.componentInstance.projects.set([ONE, TWO]);
        fixture.detectChanges();
        expect(meta()).toEqual([t.activeCount(2), t.archivedCount(0)]);
    });

    it('draws each card\'s three counts and its last edit', () => {
        const stats = rows()[0].querySelector('.card-stats')?.textContent ?? '';
        expect(stats).toContain(t.documentCount(3));
        expect(stats).toContain(t.openTaskCount(8));
        expect(stats).toContain(t.assetCount(2481));
        expect(stats).toContain(t.edited);
    });

    // ADR-239 clause 8 — the last cell is the invitation to start the next project, and it is the
    // whole screen when there is no project at all.
    it('ends the grid with the start-a-project cell, and offers creation there when the list is empty', () => {
        expect(el().querySelector('.cards app-empty-state.new-card')?.textContent).toContain(t.startNew);

        fixture.componentInstance.projects.set([]);
        fixture.detectChanges();
        expect(rows().length).toBe(0);
        const buttons = [...el().querySelectorAll('.cards app-empty-state app-button')].map(b => b.textContent?.trim());
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
