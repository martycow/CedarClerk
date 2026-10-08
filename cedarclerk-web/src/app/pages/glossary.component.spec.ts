import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GlossaryComponent } from './glossary.component';
import { GlossaryEntry, GlossaryEntryInput, GlossaryEntryLanguage, GlossaryService } from '../core/glossary.service';
import { ProjectSummary, ProjectsService } from '../core/projects.service';
import { AuthService } from '../core/auth.service';
import { CommandsService } from '../core/commands.service';
import { WorkspaceContextService } from '../core/workspace-context.service';
import { en } from '@localization/en';

const lang = (id: string, language: string, over: Partial<GlossaryEntryLanguage> = {}): GlossaryEntryLanguage => ({
    id, language, localizedName: '', spellings: [], localizedDescription: '', ...over,
});

const entry = (over: Partial<GlossaryEntry>): GlossaryEntry => ({
    id: 'e', name: 'TipTap', description: 'The editor engine.', imageUrl: null, isCaseSensitive: false,
    projectId: null, updatedAt: '2026-08-01T09:00:00', languages: [lang('x', 'ru')], ...over,
});

// One entry in two languages, one project-scoped RU entry, and two EN-only entries. The pairing
// is what the language switcher is read against; the project entry is what the scope leaves are
// read against. The two languages hold different numbers on purpose: with two apiece, a tile whose
// badge counted the other language's set would print the same digit as one that counted its own.
const RENDERER = entry({
    id: 'e1', name: 'Рендерер', description: 'Превращает документ в вывод.',
    languages: [
        lang('ru1', 'ru', { spellings: ['рендерера', 'рендереру'] }),
        lang('en1', 'en', { localizedName: 'Renderer', localizedDescription: 'Turns a document into output.' }),
    ],
});
const BENCH_RU = entry({ id: 'e2', name: 'Верстак', description: 'Рабочая поверхность.', projectId: 'p1',
    isCaseSensitive: true, languages: [lang('ru2', 'ru')] });
const BENCH_EN = entry({ id: 'e3', name: 'Bench', description: 'The work surface.', languages: [lang('en2', 'en')] });
const WORKTOP = entry({ id: 'e4', name: 'Worktop', description: 'The lit top.', languages: [lang('en3', 'en')] });

const PROJECT: ProjectSummary = {
    id: 'p1', name: 'Cedar Quest', description: '', createdFromPreset: 'fullgame', modules: {}, coverUrl: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null,
    documentCount: 0, openTaskCount: 0, assetCount: 0, buildCount: 0, latestBuildVersion: null, lastPublishedAt: null, engine: '', targetPlatforms: [], lastActivityAt: '2026-08-19T11:00:00',
};

class FakeGlossary {
    entries: GlossaryEntry[] = [RENDERER, BENCH_RU, BENCH_EN, WORKTOP];
    updated: { id: string; input: GlossaryEntryInput }[] = [];
    created: GlossaryEntryInput[] = [];
    removed: string[] = [];
    swept: [string, string][] = [];
    async list() { return structuredClone(this.entries); }
    async update(id: string, input: GlossaryEntryInput) {
        this.updated.push({ id, input });
        return entry({ id, ...input, projectId: input.projectId ?? null,
            languages: input.languages.map((l, i) => lang(`${id}-${i}`, l.language, l)) });
    }
    async create(input: GlossaryEntryInput) {
        this.created.push(input);
        return entry({ id: 'new', ...input, projectId: input.projectId ?? null,
            languages: input.languages.map((l, i) => lang(`new-${i}`, l.language, l)) });
    }
    async remove(id: string) {
        this.removed.push(id);
        this.entries = this.entries.filter(e => e.id !== id);
    }
    async translateAll(source: string, target: string) {
        this.swept.push([source, target]);
        return { added: 1, skipped: 0 };
    }
}

class FakeProjects {
    async list() { return [structuredClone(PROJECT)]; }
}

describe('glossary screen', () => {
    let fixture: ComponentFixture<GlossaryComponent>;
    const t = en.glossary;

    const page = () => fixture.componentInstance;
    const api = () => TestBed.inject(GlossaryService) as unknown as FakeGlossary;
    const card = (name: string) => cards().find(c => c.querySelector('.term-name')?.textContent?.trim() === name)!;
    const el = () => fixture.nativeElement as HTMLElement;
    const tabs = () => [...el().querySelectorAll('app-index-tabs .it-tile')] as HTMLElement[];
    const cards = () => [...el().querySelectorAll('.term-card')] as HTMLElement[];
    const names = () => cards().map(c => c.querySelector('.term-name')?.textContent?.trim());
    const shelf = () => el().querySelector('.inspector') as HTMLElement;
    const rows = () => [...shelf().querySelectorAll('app-spec-row')] as HTMLElement[];
    const row = (label: string) =>
        rows().find(r => r.querySelector('.label')?.textContent?.trim() === label);
    const rowValue = (label: string) => row(label)?.querySelector('.text')?.textContent?.trim();

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
        fixture = TestBed.createComponent(GlossaryComponent);
        await settle();
    });

    // ADR-167 clause 7 — the language strip is an index, and the tally on a tile is the size of
    // the set that tile opens.
    it('indexes the terms by language and counts each set on its own tile', async () => {
        expect(tabs().map(x => x.querySelector('.it-label')?.textContent?.trim()).slice(0, 2)).toEqual(['RU', 'EN']);
        expect(tabs()[0].querySelector('.it-badge')?.textContent?.trim()).toBe('2');
        expect(tabs()[1].querySelector('.it-badge')?.textContent?.trim()).toBe('3');
        expect(names()).toEqual(['Верстак', 'Рендерер']);

        tabs()[1].click();
        await settle();

        expect(names()).toEqual(['Bench', 'Renderer', 'Worktop']);
    });

    // T-125 — a project's view deliberately includes the global terms, because that is the set its
    // documents render with.
    it('narrows the sheet by scope, and a project keeps the global terms with it', async () => {
        const leaves = () => [...el().querySelectorAll('.scope-strip button')] as HTMLElement[];
        expect(leaves().length).toBe(3);

        leaves()[1].click();          // Global only
        await settle();
        expect(names()).toEqual(['Рендерер']);

        leaves()[2].click();          // Cedar Quest
        await settle();
        expect(names()).toEqual(['Верстак', 'Рендерер']);
    });

    // ADR-167 clause 8. The values are what makes this a real assertion: a term-shaped shelf that
    // read the glossary's numbers would still be 'selection'-scoped and would still pass a
    // scope-only check.
    it('describes the picked term on the shelf, and the glossary itself when none is picked', async () => {
        expect(page().inspectorScope()).toBe('document');
        expect(rows().every(r => r.getAttribute('data-scope') === 'document')).toBe(true);
        expect(rowValue(t.inspector.total)).toBe('5');
        expect(card('Рендерер').querySelectorAll('.term-alias').length).toBe(2);
        expect(rowValue(t.inspector.inLanguage)).toBe('2 · RU');

        card('Верстак').click();      // project-scoped, case-sensitive
        await settle();

        expect(page().inspectorScope()).toBe('selection');
        expect(rows().every(r => r.getAttribute('data-scope') === 'selection')).toBe(true);
        expect(shelf().querySelector('.glossary-preview-term')?.textContent?.trim()).toBe('Верстак');
        expect(rowValue(t.inspector.scope)).toBe('Cedar Quest');
        expect(rowValue(t.inspector.matching)).toBe(t.inspector.exact);
        expect(rowValue(t.inspector.aliases)).toBe(t.inspector.none);
    });

    // The switcher walks the languages of the entry: an entry in one language gets no switcher at
    // all rather than a row of one. A blank localized field reads as the entry's own.
    it('switches the previewed tooltip between the languages of that term only', async () => {
        card('Рендерер').click();     // an entry in two languages
        await settle();

        const groupLeaves = () => [...shelf().querySelectorAll('.preview-langs button')] as HTMLElement[];
        expect(groupLeaves().map(l => l.textContent?.trim())).toEqual(['RU', 'EN']);
        expect(shelf().querySelector('.glossary-preview-desc')?.textContent?.trim()).toBe('Превращает документ в вывод.');

        groupLeaves()[1].click();
        await settle();

        expect(shelf().querySelector('.glossary-preview-term')?.textContent?.trim()).toBe('Renderer');
        expect(rowValue(t.inspector.language)).toContain('EN');

        card('Верстак').click();      // one language only
        await settle();
        expect(shelf().querySelectorAll('.preview-langs button').length).toBe(0);
    });

    // T-260. Only GET /api/glossary carries the count, so a term that has come back from a save
    // and not from the list has none — and a nought drawn there would be a number the screen made
    // up, indistinguishable from the real "nothing uses this".
    it('says nothing about usage until the list has answered for that term', async () => {
        card('Рендерер').click();
        await settle();
        expect(row(t.inspector.usedIn)).toBeUndefined();
    });

    it('counts the documents a term is used in, and names whatever takes its spelling', async () => {
        page().entries.set([
            entry({ id: 'e1', name: 'Рендерер', languages: [lang('ru1', 'ru', { usedInDrafts: 4, shadowedByTermId: null })] }),
            // Shadowed from another scope: a project term beating a global one is the T-125
            // override working, so the row informs and does not accuse.
            entry({ id: 'e2', name: 'Верстак', projectId: 'p1',
                languages: [lang('ru2', 'ru', { usedInDrafts: 2, shadowedByTermId: 'ru1' })] }),
            // Shadowed inside its own scope: this term will never mark anything, and nothing else
            // on the screen would tell its author so.
            entry({ id: 'e3', name: 'Стапель', languages: [lang('ru3', 'ru', { usedInDrafts: 0, shadowedByTermId: 'ru1' })] }),
        ]);
        await settle();

        card('Рендерер').click();
        await settle();
        expect(rowValue(t.inspector.usedIn)).toBe(t.inspector.usedInDrafts(4));
        expect(row(t.inspector.usedIn)!.classList.contains('warn')).toBe(false);

        card('Верстак').click();
        await settle();
        // The count is not zeroed by the shadow — the global term is still used everywhere the
        // project term does not reach — so the note has to read beside a real number too.
        expect(rowValue(t.inspector.usedIn))
            .toBe(`${t.inspector.usedInDrafts(2)} · ${t.inspector.shadowedBy('Рендерер')}`);
        expect(row(t.inspector.usedIn)!.classList.contains('warn')).toBe(false);

        card('Стапель').click();
        await settle();
        expect(rowValue(t.inspector.usedIn))
            .toBe(`${t.inspector.usedInDrafts(0)} · ${t.inspector.shadowedBy('Рендерер')}`);
        expect(row(t.inspector.usedIn)!.classList.contains('warn')).toBe(true);
    });

    it('shows each term translation coverage and pages a large language set', async () => {
        expect(card('Рендерер').querySelector('.term-languages')?.textContent?.replace(/\s/g, '')).toBe('RUEN');

        page().entries.set(Array.from({ length: 45 }, (_, i) => entry({
            id: `e-${i}`, name: `Термин ${String(i).padStart(2, '0')}`, languages: [lang(`ru-${i}`, 'ru')],
        })));
        fixture.detectChanges();

        expect(cards().length).toBe(20);
        expect(page().pageCount()).toBe(3);
        page().setPage(3);
        fixture.detectChanges();
        expect(cards().length).toBe(5);
        expect(el().querySelector('.glossary-pager')?.textContent).toContain(t.page(3, 3));
    });

    // ADR-239 clause 6 — the create action is the page header's primary and the counts are its meta line.
    it('keeps New term in the page header and draws its counts on the meta line', () => {
        const newTerm = [...el().querySelectorAll('app-page-header app-button')]
            .find(b => b.textContent?.includes(t.newTerm)) as HTMLElement;
        expect(newTerm).toBeTruthy();
        const meta = el().querySelector('.page-meta')?.textContent ?? '';
        expect(meta).toContain('RU');
        expect(meta).toContain(t.rulerTerms(2));
        expect(meta).toContain(t.rulerScopes(1));

        (newTerm.querySelector('button') as HTMLButtonElement).click();
        fixture.detectChanges();
        expect(page().editing()).toBe(true);
    });

    // ADR-320 — the pencil on a row opens the whole entry, every language of it, in place of
    // that row; saving writes the entry, not the row.
    it('edits the whole entry from the row that was clicked', async () => {
        tabs()[1].click();
        await settle();
        (card('Renderer').querySelector('button.mini') as HTMLButtonElement).click();
        await settle();

        const form = el().querySelector('app-glossary-term-form') as HTMLElement;
        expect(page().editEntry()?.id).toBe('e1');
        expect([...form.querySelectorAll('fieldset.term-lang')].map(f => f.getAttribute('data-language'))).toEqual(['ru', 'en']);
        expect(names()).toEqual(['Bench', 'Worktop']);

        const save = [...el().querySelectorAll('.form-actions button')].at(-1) as HTMLButtonElement;
        save.click();
        await settle();

        expect(api().updated.map(u => u.id)).toEqual(['e1']);
        expect(api().updated[0].input.languages.map(l => l.language)).toEqual(['ru', 'en']);
        expect(api().updated[0].input.languages[0].spellings).toEqual(['рендерера', 'рендереру']);
        expect(page().editing()).toBe(false);
    });

    it('creates an entry in the language and scope on screen', async () => {
        page().selectScope('p1');
        tabs()[1].click();
        await settle();
        page().startNew();
        await settle();

        const form = el().querySelector('app-glossary-term-form') as HTMLElement;
        expect([...form.querySelectorAll('fieldset.term-lang')].map(f => f.getAttribute('data-language'))).toEqual(['en']);

        await page().save({ name: 'Ferry', description: 'A boat.', imageUrl: null, isCaseSensitive: false,
            languages: [{ language: 'en', localizedName: '', spellings: [], localizedDescription: '' }] });

        expect(api().created[0]).toMatchObject({ name: 'Ferry', projectId: 'p1' });
    });

    it('deletes the entry, and with it every language row on the sheet', async () => {
        page().deleteConfirmId.set('ru1');
        await settle();
        await page().confirmDelete();
        await settle();

        expect(api().removed).toEqual(['e1']);
        expect(names()).toEqual(['Верстак']);
        tabs()[1].click();
        await settle();
        expect(names()).toEqual(['Bench', 'Worktop']);
    });

    it('sweeps the listed language into each checked one and re-reads the glossary', async () => {
        TestBed.inject(AuthService).planTier.set('Pro');
        page().openTranslateAll();
        page().toggleTranslateLang('de');
        page().toggleTranslateLang('en');
        await page().runTranslateAll();

        expect(api().swept).toEqual([['ru', 'en'], ['ru', 'de']]);
        expect(page().translateAllOpen()).toBe(false);
    });

    // ADR-301 clause 4/5 — T-386. What the inspector rail and the AI panel read off this screen.
    describe('workspace context', () => {
        const workspace = () => TestBed.inject(WorkspaceContextService);
        const commands = () => TestBed.inject(CommandsService);

        it('names the surface and publishes nothing else until a term is picked', () => {
            expect(workspace().surface()).toBe(en.shell.context.glossary);
            expect(workspace().open()).toBeNull();
            expect(workspace().scope()).toEqual([]);
        });

        // Clicking a card opens the preview; `selectedId` only ever names the term an edit form is
        // open on. Reading that one alone left the rail empty for the ordinary case.
        it('follows the card the user clicked, not only the one being edited', async () => {
            card('Рендерер').click();
            await settle();

            expect(page().previewId()).toBe('ru1');
            expect(workspace().open()).toMatchObject({ id: 'ru1', title: 'Рендерер' });
        });

        it('publishes the picked term, its scope and the spelling no AI run may rewrite', async () => {
            page().selectedId.set('ru2');
            await settle();

            expect(workspace().open()).toMatchObject({ id: 'ru2', kind: 'term', title: 'Верстак' });
            expect(workspace().scope().map(o => o.id)).toEqual(['ru2']);
            const props = workspace().properties();
            expect(props.find(p => p.label === en.shell.context.scope)?.value).toBe('Cedar Quest');
            expect(workspace().protectedFields()).toEqual([en.shell.context.identifier]);
        });

        it('calls a global term global rather than printing a null project id', async () => {
            page().selectedId.set('ru1');
            await settle();
            expect(workspace().properties().find(p => p.label === en.shell.context.scope)?.value)
                .toBe(en.shell.context.scopeGlobal);
        });

        it('registers the whole-language translate run as an AI command, gated on the plan', async () => {
            const ids = commands().all().filter(c => c.ai).map(c => c.id);
            expect(ids).toEqual(['glossary.ai.translateAll']);

            const auth = TestBed.inject(AuthService);
            auth.planTier.set(null);
            await settle();
            expect(commands().all().filter(c => c.ai).every(c => !commands().isEnabled(c))).toBe(true);

            auth.planTier.set('Pro');
            await settle();
            expect(commands().isEnabled(commands().find('glossary.ai.translateAll')!)).toBe(true);
        });

        it('lets go of the context when the screen does', async () => {
            page().selectedId.set('ru1');
            await settle();
            fixture.destroy();

            expect(workspace().open()).toBeNull();
            expect(commands().all().filter(c => c.ai)).toEqual([]);
        });
    });
});
