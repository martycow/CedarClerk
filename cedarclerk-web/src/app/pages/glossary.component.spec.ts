import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GlossaryComponent } from './glossary.component';
import { GlossaryService, GlossaryTerm } from '../core/glossary.service';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
import { AuthService } from '../core/auth.service';
import { RailActionsService } from '../core/rail-actions.service';
import { RulerService } from '../core/ruler.service';
import { en } from '../core/i18n/en';

const term = (over: Partial<GlossaryTerm>): GlossaryTerm => ({
    id: 'x', term: 'TipTap', description: 'The editor engine.', aliases: '',
    imageUrl: null, language: 'ru', isCaseSensitive: false, sourceTermId: null,
    projectId: null, updatedAt: '2026-08-01T09:00:00', ...over,
});

// One idea in two languages (RU is the root, EN carries sourceTermId), one project-scoped RU term,
// and two EN terms with no translation group. The pairing is what the group switcher is read
// against; the project term is what the scope leaves are read against. The two languages hold
// different numbers on purpose: with two apiece, a tile whose badge counted the other
// language's set would print the same digit as one that counted its own.
const RU_ROOT = term({ id: 'ru1', term: 'Рендерер', description: 'Превращает документ в вывод.', aliases: 'рендерера, рендереру' });
const EN_LEAF = term({ id: 'en1', term: 'Renderer', description: 'Turns a document into output.', language: 'en', sourceTermId: 'ru1' });
const RU_PROJ = term({ id: 'ru2', term: 'Верстак', description: 'Рабочая поверхность.', projectId: 'p1', isCaseSensitive: true });
const EN_ALONE = term({ id: 'en2', term: 'Bench', description: 'The work surface.', language: 'en' });
const EN_SPARE = term({ id: 'en3', term: 'Worktop', description: 'The lit top.', language: 'en' });

const PROJECT: ProjectSummary = {
    id: 'p1', name: 'Cedar Quest', description: '', projectType: 'fullgame', coverUrl: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null,
    documentCount: 0, openTaskCount: 0, assetCount: 0, lastActivityAt: '2026-08-19T11:00:00',
};

class FakeGlossary {
    terms: GlossaryTerm[] = [RU_ROOT, EN_LEAF, RU_PROJ, EN_ALONE, EN_SPARE];
    async list() { return structuredClone(this.terms); }
}

class FakeProjects {
    async list() { return [structuredClone(PROJECT)]; }
}

describe('glossary screen', () => {
    let fixture: ComponentFixture<GlossaryComponent>;
    let ruler: RulerService;
    let rail: RailActionsService;
    const t = en.glossary;

    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;
    const tabs = () => [...el().querySelectorAll('app-index-tabs .it-tile')] as HTMLElement[];
    const cards = () => [...el().querySelectorAll('.term-card')] as HTMLElement[];
    const names = () => cards().map(c => c.querySelector('.term-name')?.textContent?.trim());
    const shelf = () => el().querySelector('app-shelf-panel.inspector') as HTMLElement;
    const rows = () => [...shelf().querySelectorAll('app-spec-row')] as HTMLElement[];
    const rowValue = (label: string) =>
        rows().find(r => r.querySelector('.label')?.textContent?.trim() === label)
            ?.querySelector('.text')?.textContent?.trim();

    async function settle() {
        fixture.detectChanges();
        for (let i = 0; i < 5; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(async () => {
        TestBed.configureTestingModule({
            providers: [
                { provide: GlossaryService, useClass: FakeGlossary },
                { provide: ProjectsService, useClass: FakeProjects },
            ],
        });
        TestBed.inject(AuthService).indieDev.set(true);
        ruler = TestBed.inject(RulerService);
        rail = TestBed.inject(RailActionsService);
        fixture = TestBed.createComponent(GlossaryComponent);
        await settle();
    });

    // ADR-167 clause 7 — the language strip is an index, and the tally on a tile is the size of
    // the set that tile opens.
    it('indexes the terms by language and counts each set on its own tile', async () => {
        expect(tabs().map(x => x.querySelector('.it-label')?.textContent?.trim()).slice(0, 2)).toEqual(['RU', 'EN']);
        expect(tabs()[0].querySelector('.it-badge')?.textContent?.trim()).toBe('2');
        expect(tabs()[1].querySelector('.it-badge')?.textContent?.trim()).toBe('3');
        expect(names()).toEqual(['Рендерер', 'Верстак']);

        tabs()[1].click();
        await settle();

        expect(names()).toEqual(['Renderer', 'Bench', 'Worktop']);
    });

    // T-125 — a project's view deliberately includes the global terms, because that is the set its
    // documents render with.
    it('narrows the sheet by scope, and a project keeps the global terms with it', async () => {
        const leaves = () => [...el().querySelectorAll('.scope-strip app-leaf-tag .lt-pick')] as HTMLElement[];
        expect(leaves().length).toBe(3);

        leaves()[1].click();          // Global only
        await settle();
        expect(names()).toEqual(['Рендерер']);

        leaves()[2].click();          // Cedar Quest
        await settle();
        expect(names()).toEqual(['Рендерер', 'Верстак']);
    });

    // ADR-167 clause 8. The values are what makes this a real assertion: a term-shaped shelf that
    // read the glossary's numbers would still be 'selection'-scoped and would still pass a
    // scope-only check.
    it('describes the picked term on the shelf, and the glossary itself when none is picked', async () => {
        expect(page().inspectorScope()).toBe('document');
        expect(rows().every(r => r.getAttribute('data-scope') === 'document')).toBe(true);
        expect(rowValue(t.inspector.total)).toBe('5');
        expect(rowValue(t.inspector.inLanguage)).toBe('2 · RU');

        cards()[1].click();           // Верстак — project-scoped, case-sensitive
        await settle();

        expect(page().inspectorScope()).toBe('selection');
        expect(rows().every(r => r.getAttribute('data-scope') === 'selection')).toBe(true);
        expect(shelf().querySelector('.glossary-preview-term')?.textContent?.trim()).toBe('Верстак');
        expect(rowValue(t.inspector.scope)).toBe('Cedar Quest');
        expect(rowValue(t.inspector.matching)).toBe(t.inspector.exact);
        expect(rowValue(t.inspector.aliases)).toBe(t.inspector.none);
    });

    // The switcher walks the translation group, not the whole list: a term with no translation
    // gets no switcher at all rather than a row of one.
    it('switches the previewed tooltip between the languages of that term only', async () => {
        cards()[0].click();           // Рендерер — the RU root of a two-language group
        await settle();

        const groupLeaves = () => [...shelf().querySelectorAll('.preview-langs app-leaf-tag .lt-pick')] as HTMLElement[];
        expect(groupLeaves().map(l => l.textContent?.trim())).toEqual(['RU', 'EN']);
        expect(shelf().querySelector('.glossary-preview-desc')?.textContent?.trim()).toBe('Превращает документ в вывод.');

        groupLeaves()[1].click();
        await settle();

        expect(shelf().querySelector('.glossary-preview-term')?.textContent?.trim()).toBe('Renderer');
        expect(rowValue(t.inspector.language)).toContain('EN');

        cards()[1].click();           // Верстак — alone in its group
        await settle();
        expect(shelf().querySelectorAll('.preview-langs app-leaf-tag').length).toBe(0);
    });

    // ADR-167 clause 7 and ADR-159 clause 1: the screen's one command goes to the rail as data,
    // and the rule carries what it measures. Both are cleared when the screen goes away.
    it('publishes its one action and its counts to the shell, and clears them on the way out', () => {
        expect(rail.primary()?.label).toBe(t.newTerm);
        expect(ruler.label()).toBe(t.crumb);
        expect(ruler.right().map(r => r.text)).toEqual([t.rulerTerms(2), t.rulerScopes(1)]);

        rail.primary()!.run();
        fixture.detectChanges();
        expect(page().editing()).toBe(true);

        fixture.destroy();
        expect(rail.primary()).toBeNull();
        expect(ruler.right()).toEqual([]);
    });
});
