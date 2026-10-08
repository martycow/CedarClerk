import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ProjectsComponent } from './projects.component';
import { CreateProjectInput, ProjectSummary, ProjectsService } from '../core/projects.service';
import { Preset, PresetsService } from '../core/presets.service';
import { en } from '@localization/en';

const ONE: ProjectSummary = {
    id: 'p1', name: 'Cedar Quest', description: '', createdFromPreset: 'fullgame', modules: {}, coverUrl: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null,
    documentCount: 3, openTaskCount: 8, assetCount: 2481, buildCount: 0, latestBuildVersion: null, lastPublishedAt: null, engine: '', targetPlatforms: [], lastActivityAt: '2026-08-19T11:00:00',
};

const TWO: ProjectSummary = {
    ...ONE, id: 'p2', name: 'Night Lanterns',
    documentCount: 1, openTaskCount: 2, assetCount: 19, buildCount: 0, latestBuildVersion: null, lastPublishedAt: null, engine: '', targetPlatforms: [],
};

// Archived and empty: it is what makes the archived count non-zero without moving any sum.
const OLD: ProjectSummary = {
    ...ONE, id: 'p3', name: 'Paper Lanterns', archivedAt: '2026-06-01T09:00:00',
    documentCount: 0, openTaskCount: 0, assetCount: 0, buildCount: 0, latestBuildVersion: null, lastPublishedAt: null, engine: '', targetPlatforms: [],
};

class FakeProjects {
    list_: ProjectSummary[] = [ONE, TWO, OLD];
    async list() { return structuredClone(this.list_); }
    created: CreateProjectInput[] = [];
    async create(input: CreateProjectInput) {
        this.created.push(input);
        return { id: 'new', name: input.name, documentId: null };
    }
}

class FakePresets {
    presets: Preset[] = [];
    async list() { return structuredClone(this.presets); }
}

describe('project index', () => {
    let fixture: ComponentFixture<ProjectsComponent>;
    let projects: FakeProjects;
    let presets: FakePresets;
    const t = en.projects;

    const el = () => fixture.nativeElement as HTMLElement;
    const rows = () => [...el().querySelectorAll('a.project-card')] as HTMLAnchorElement[];
    const names = () => rows().map(r => r.querySelector('.card-name')?.textContent?.trim());
    const tabs = () => [...el().querySelectorAll('app-index-tabs .it-tile')] as HTMLElement[];
    const meta = () => [...el().querySelectorAll('app-page-header .page-meta > span:not(.sep)')]
        .map(x => x.textContent?.trim());

    async function create() {
        projects = new FakeProjects();
        presets = new FakePresets();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: ProjectsService, useValue: projects },
                { provide: PresetsService, useValue: presets },
            ],
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

    it('declares the operational measure and keeps creation in one place for each data state', () => {
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        expect(el().querySelectorAll('app-button').length).toBe(1);
        expect(el().querySelector('app-page-header app-button')?.textContent).toContain(t.newProject);
        expect(el().querySelector('.cards app-empty-state.new-card')).toBeNull();

        fixture.componentInstance.projects.set([]);
        fixture.detectChanges();
        expect(rows().length).toBe(0);
        expect(el().querySelector('app-page-header app-button')).toBeNull();
        const buttons = [...el().querySelectorAll('app-button')].map(b => b.textContent?.trim());
        expect(buttons).toEqual([t.newProject]);
        expect(el().querySelector('.cards app-empty-state.new-card')?.textContent).toContain(t.startNew);
    });

    const modal = () => document.querySelector('app-modal') as HTMLElement;
    const startEmptyBox = () => modal().querySelector('#project-start-empty') as HTMLInputElement;

    it('offers the six types as compact cards and asks for the name and description first', () => {
        fixture.componentInstance.startCreate();
        fixture.detectChanges();
        const types = [...modal().querySelectorAll('.type-name')].map(x => x.textContent?.trim());
        expect(types).toEqual(['Empty', 'Blog', 'Game', 'Product', 'Work', 'Vault']);
        const starters = [...modal().querySelectorAll('.type-starter')].map(x => x.textContent?.trim());
        expect(starters[0]).toBe(t.create.noDocuments);
        expect(starters[5]).toBe(t.projectTypes.vault.starter);
        expect(modal().querySelector('.type-card.selected .type-name')?.textContent?.trim()).toBe('Empty');
        expect((modal().querySelector('#project-name') as HTMLInputElement).placeholder).toBe('Enter project name here');
        expect(modal().querySelector('#project-description')).not.toBeNull();
        const labels = [...modal().querySelectorAll('.step-label')].map(x => x.textContent?.trim());
        expect(labels).toEqual([t.create.stepDetails, t.create.stepAppearance, t.create.stepStart, t.create.livePreview]);
        expect([...modal().querySelectorAll('.image-tile .tile-name')].map(x => x.textContent?.trim()))
            .toEqual([t.create.addLogo, t.create.addBanner]);
    });

    // ADR-318 — the Empty type holds "Start without documents" checked; any other type leaves it to the reader.
    it('previews an empty project for the Empty type and a starter document for the others', () => {
        const c = fixture.componentInstance;
        c.startCreate();
        fixture.detectChanges();
        expect(startEmptyBox().checked).toBe(true);
        expect(startEmptyBox().disabled).toBe(true);
        expect(modal().querySelector('.tree-none')?.textContent).toContain(t.create.noDocumentsYet);
        expect(modal().querySelector('.tree-doc')).toBeNull();
        expect(modal().querySelector('.detail-section-meta')?.textContent?.trim()).toBe(t.documentCount(0));
        expect(modal().querySelector('.foot-note')?.textContent?.trim()).toBe(t.create.footEmpty);

        c.pickType('fullgame');
        fixture.detectChanges();
        expect(startEmptyBox().checked).toBe(false);
        expect(startEmptyBox().disabled).toBe(false);
        expect(modal().querySelector('.detail-name')?.textContent?.trim()).toBe('Game');
        expect(modal().querySelector('.tree-doc .tree-label')?.textContent?.trim()).toBe(t.projectTypes.fullgame.starter);
        const headings = [...modal().querySelectorAll('.tree-heading .tree-label')].map(x => x.textContent?.trim());
        expect(headings).toEqual(t.create.outline.design);
        expect(modal().querySelector('.foot-note')?.textContent?.trim())
            .toBe(t.create.startsWith(t.projectTypes.fullgame.starter));

        startEmptyBox().click();
        fixture.detectChanges();
        expect(modal().querySelector('.tree-none')).not.toBeNull();
        expect(modal().querySelector('.foot-note')?.textContent?.trim()).toBe(t.create.footEmpty);
    });

    it('reads the name, description, logo and banner back in the live preview', () => {
        const c = fixture.componentInstance;
        c.startCreate();
        c.createName.set('Cedar Field Notes');
        c.createDescription.set('A space for ideas.');
        fixture.detectChanges();
        expect(modal().querySelector('.preview-name')?.textContent?.trim()).toBe('Cedar Field Notes');
        expect(modal().querySelector('.preview-logo')?.textContent?.trim()).toBe('CF');
        const facts = () => [...modal().querySelectorAll('.preview-facts dd')].map(x => x.textContent?.trim());
        expect(facts()).toEqual(['Empty', 'A space for ideas.', t.create.atCreation(0), t.create.appearanceDefault]);

        c.createPicker.set('logo');
        c.pickedImage({ localPath: 'logo.png' } as never);
        c.createPicker.set('banner');
        c.pickedImage({ localPath: 'banner.png' } as never);
        fixture.detectChanges();
        expect(c.createPicker()).toBeNull();
        expect(modal().querySelector('.preview-logo img')?.getAttribute('src')).toBe('/media/logo.png');
        expect(modal().querySelector('.is-banner .image-tile img')?.getAttribute('src')).toBe('/media/banner.png');
        expect(facts()[3]).toBe(t.create.appearanceBoth);

        (modal().querySelector('.is-logo .tile-remove') as HTMLButtonElement).click();
        fixture.detectChanges();
        expect(facts()[3]).toBe(t.create.appearanceBanner);
    });

    it('sends the whole form, and whether the project starts without a document', async () => {
        const c = fixture.componentInstance;
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        c.startCreate();
        c.createName.set('  Field Notes ');
        c.createDescription.set(' About ');
        c.createBannerUrl.set('/media/banner.png');
        await c.create();
        expect(projects.created.at(-1)).toEqual(expect.objectContaining({
            name: 'Field Notes', description: 'About', projectType: 'empty',
            startWithoutDocuments: true, coverUrl: null, bannerUrl: '/media/banner.png',
        }));

        c.startCreate();
        c.createName.set('Blog');
        c.pickType('blog');
        await c.create();
        expect(projects.created.at(-1)).toEqual(expect.objectContaining({
            projectType: 'blog', startWithoutDocuments: false, description: undefined, bannerUrl: null,
            documentTitle: t.projectTypes.blog.starter,
        }));

        c.startCreate();
        c.createName.set('Blog');
        c.pickType('blog');
        c.startEmpty.set(true);
        await c.create();
        expect(projects.created.at(-1)).toEqual(expect.objectContaining({ projectType: 'blog', startWithoutDocuments: true }));
        expect(navigate).toHaveBeenLastCalledWith(['/projects', 'new']);
    });

    it('keeps the starter document of a saved preset built on the Empty type', async () => {
        presets.presets = [
            { id: 'pr1', name: 'Notebook', kind: 'project', configJson: JSON.stringify({ projectType: 'empty', documentTitle: 'Inbox' }) } as Preset,
        ];
        const c = fixture.componentInstance;
        c.startCreate();
        for (let i = 0; i < 5; i++) await Promise.resolve();
        c.pickSource('mine');
        c.pickPreset(c.projectPresets()[0]);
        fixture.detectChanges();
        expect(c.createsNoDocument()).toBe(false);
        expect(startEmptyBox().disabled).toBe(false);
        expect(modal().querySelector('.tree-doc .tree-label')?.textContent?.trim()).toBe('Inbox');
    });

    it('keeps the presets on their own shelf and says when there are none', () => {
        fixture.componentInstance.startCreate();
        fixture.componentInstance.pickSource('mine');
        fixture.detectChanges();
        expect(modal().querySelectorAll('.type-card').length).toBe(0);
        expect(modal().querySelector('.choice-note')?.textContent).toContain(t.create.noPresets);
        expect(modal().querySelector('app-button[link="/presets"]')).not.toBeNull();
    });
});
