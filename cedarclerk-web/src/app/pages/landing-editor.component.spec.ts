import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LandingEditorComponent } from './landing-editor.component';
import { AdminLanding, AdminService, LandingDocument, LandingItem } from '../core/admin.service';
import { ConfirmationService } from '../core/confirmation.service';
import { en } from '@localization/en';

const L = en.admin.landing;

const style = (id: string, fields: string[], itemFields: string[] = [], extra: Record<string, boolean> = {}) => ({
    id, fields, itemFields, icons: false, files: false, marks: false, entries: false, image: false, url: false, ...extra,
});

const LANDING: AdminLanding = {
    stored: false,
    configuredShowcaseBlog: null,
    files: ['shot_a.png'],
    waitlist: 2,
    schema: {
        layouts: ['stack', 'hero', 'split', 'flow', 'band'],
        marks: ['done', 'doing', 'next'],
        requiredLanguages: ['en', 'ru'],
        languages: [{ code: 'ru', endonym: 'Русский' }, { code: 'en', endonym: 'English' }, { code: 'de', endonym: 'Deutsch' }],
        tokens: ['{languages}', '{networks}'],
        blocks: [
            {
                type: 'text', required: ['title'], requiredItem: [], options: [], single: false,
                styles: [style('hero', ['kicker', 'title', 'body']), style('copy', ['title', 'body', 'linkLabel'], [], { url: true })],
            },
            {
                type: 'subscribe', required: ['button'], requiredItem: [], options: [], single: false,
                styles: [style('form', ['button', 'hint']), style('button', ['button'])],
            },
            { type: 'faq', required: ['title'], requiredItem: ['question', 'answer'], options: [], single: false, styles: [style('default', ['title'], ['question', 'answer'])] },
            { type: 'pricing', required: [], requiredItem: [], options: ['showNotes', 'showComparison'], single: false, styles: [style('default', [])] },
            { type: 'tools', required: ['title'], requiredItem: [], options: [], single: true, styles: [style('default', ['title'])] },
        ],
    },
    document: {
        languages: ['en', 'ru'],
        showcaseBlog: null,
        features: [
            { id: 'editor', icon: 'pencil-simple', title: { en: 'Editor', ru: 'Редактор' }, body: { en: 'Writes.', ru: 'Пишет.' }, shot: null },
            { id: 'tasks', icon: 'kanban', title: { en: 'Tasks', ru: 'Задачи' }, body: { en: 'Plans.', ru: 'Планирует.' }, shot: null },
        ],
        sections: [
            {
                id: 'hero', layout: 'hero', anchor: null, hidden: false, nav: {}, label: {},
                blocks: [
                    { id: 'b1', type: 'text', style: 'hero', hidden: false, text: { kicker: { en: 'Kicker', ru: 'Надзаголовок' }, title: { en: 'One draft', ru: 'Один черновик' } }, items: [], options: {}, url: null, image: null },
                    { id: 'b2', type: 'subscribe', style: 'form', hidden: false, text: { button: { en: 'Join', ru: 'Войти' } }, items: [], options: {}, url: null, image: null },
                ],
            },
            {
                id: 'faq', layout: 'flow', anchor: 'faq', hidden: false, nav: {}, label: {},
                blocks: [
                    { id: 'b3', type: 'faq', style: 'default', hidden: false, text: { title: { en: 'Questions', ru: 'Вопросы' } }, items: [], options: {}, url: null, image: null },
                ],
            },
            { id: 'pricing', layout: 'stack', anchor: 'pricing', hidden: false, nav: { en: 'Pricing', ru: 'Цены' }, label: {}, blocks: [] },
        ],
    },
};

class FakeAdmin {
    saved: LandingDocument | null = null;
    previews: { document: LandingDocument; language: string }[] = [];
    problem: string | null = null;

    async landing() { return structuredClone(LANDING); }
    async saveLanding(document: LandingDocument) { this.saved = structuredClone(document); }
    async previewLanding(document: LandingDocument, language: string) {
        this.previews.push({ document: structuredClone(document), language });
        return { html: `<!doctype html><html lang="${language}"><body><h1>Drawn by the server</h1></body></html>`, language, problem: this.problem };
    }
    async uploadLandingShot() { return { file: 'shot_new.png', url: '/landing-media/shot_new.png' }; }
    async deleteLandingFile() { return {}; }
}

async function settle(fixture: ComponentFixture<unknown>) {
    for (let link = 0; link < 5; link++) {
        await fixture.whenStable();
        fixture.detectChanges();
    }
}

describe('landing block editor (ADR-323)', () => {
    let fixture: ComponentFixture<LandingEditorComponent>;
    let component: LandingEditorComponent;
    let api: FakeAdmin;
    let confirmed = true;

    const el = () => fixture.nativeElement as HTMLElement;
    const sectionIds = () => [...el().querySelectorAll('[data-section]')].map(s => s.getAttribute('data-section'));
    const blockIds = (section: string) =>
        [...el().querySelectorAll(`[data-section="${section}"] [data-block]`)].map(b => b.getAttribute('data-block'));
    const button = (selector: string) => el().querySelector(`${selector} button`) as HTMLButtonElement;
    const type = (input: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement, value: string) => {
        input.value = value;
        input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
    };

    beforeEach(async () => {
        confirmed = true;
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: AdminService, useClass: FakeAdmin },
                { provide: ConfirmationService, useValue: { confirm: async () => confirmed } },
            ],
        });
        fixture = TestBed.createComponent(LandingEditorComponent);
        component = fixture.componentInstance;
        api = TestBed.inject(AdminService) as unknown as FakeAdmin;
        fixture.detectChanges();
        await settle(fixture);
    });

    afterEach(() => vi.useRealTimers());

    it('draws the document as sections of blocks and previews it through the server on load', () => {
        expect(sectionIds()).toEqual(['hero', 'faq', 'pricing']);
        expect(blockIds('hero')).toEqual(['b1', 'b2']);
        expect(el().textContent).toContain(L.notStored);
        expect(api.previews.length).toBe(1);
        expect(api.previews[0].language).toBe('en');

        const frame = el().querySelector('iframe.le-frame') as HTMLIFrameElement;
        expect(frame.getAttribute('sandbox')).toBe('allow-same-origin');
        expect(frame.getAttribute('srcdoc')).toContain('Drawn by the server');
    });

    it('announces the loaded page so the tab strip can show the waitlist', async () => {
        const seen: AdminLanding[] = [];
        component.loaded.subscribe(data => seen.push(data));
        await component.load();
        expect(seen[0].waitlist).toBe(2);
    });

    it('adds a section and a block of a chosen type', async () => {
        component.newLayout = 'band';
        button('[data-act="add-section"]').click();
        await settle(fixture);
        expect(sectionIds().length).toBe(4);
        const added = component.doc()!.sections[3];
        expect(added.layout).toBe('band');

        type(el().querySelector(`[data-section="${added.id}"] > .le-add select`) as HTMLSelectElement, 'pricing');
        await settle(fixture);
        button(`[data-section="${added.id}"] [data-act="add-block"]`).click();
        await settle(fixture);
        expect(added.blocks.map(b => [b.type, b.style, b.options])).toEqual([['pricing', 'default', { showNotes: true, showComparison: true }]]);
        expect(component.dirty()).toBe(true);
        expect(el().textContent).toContain(L.unsaved);
    });

    it('gives a second subscribe block the button presentation and offers a single-use block once', () => {
        const section = component.doc()!.sections[2];
        component.newBlockType[section.id] = 'subscribe';
        component.addBlock(section);
        expect(section.blocks[0].style).toBe('button');

        expect(component.addableTypes().map(s => s.type)).toContain('tools');
        component.newBlockType[section.id] = 'tools';
        component.addBlock(section);
        expect(component.addableTypes().map(s => s.type)).not.toContain('tools');
    });

    it('reorders sections with the buttons and keeps focus on the control that was pressed', async () => {
        button('[data-move="s-faq-up"]').focus();
        button('[data-move="s-faq-up"]').click();
        await settle(fixture);

        expect(sectionIds()).toEqual(['faq', 'hero', 'pricing']);
        expect(component.announcement()).toBe(L.moved(1, 3));
        // It is now the first row, so its Up is disabled and focus lands on its Down.
        expect(document.activeElement).toBe(button('[data-move="s-faq-down"]'));

        button('[data-move="s-faq-down"]').click();
        await settle(fixture);
        expect(sectionIds()).toEqual(['hero', 'faq', 'pricing']);
        expect(document.activeElement).toBe(button('[data-move="s-faq-down"]'));
    });

    it('reorders sections and blocks from the keyboard with Alt and an arrow', async () => {
        const heading = el().querySelector('[data-section="pricing"] .le-title') as HTMLElement;
        heading.focus();
        heading.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', altKey: true, bubbles: true }));
        await settle(fixture);
        expect(sectionIds()).toEqual(['hero', 'pricing', 'faq']);
        expect(document.activeElement).toBe(el().querySelector('[data-section="pricing"] .le-title'));

        heading.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
        await settle(fixture);
        expect(sectionIds()).toEqual(['hero', 'pricing', 'faq']);

        const block = el().querySelector('[data-block="b1"] .le-title') as HTMLElement;
        block.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true }));
        await settle(fixture);
        expect(blockIds('hero')).toEqual(['b2', 'b1']);
    });

    it('hides and shows a section and a block without removing them', async () => {
        button('[data-section="faq"] > .le-head [data-act="hide-section"]').click();
        button('[data-block="b1"] [data-act="hide-block"]').click();
        await settle(fixture);

        const doc = component.doc()!;
        expect(doc.sections.map(s => s.hidden)).toEqual([false, true, false]);
        expect(doc.sections[0].blocks.map(b => b.hidden)).toEqual([true, false]);
        expect(el().querySelector('[data-section="faq"]')!.classList).toContain('is-hidden');
        expect(button('[data-section="faq"] > .le-head [data-act="hide-section"]').textContent!.trim()).toBe(L.show);

        button('[data-section="faq"] > .le-head [data-act="hide-section"]').click();
        await settle(fixture);
        expect(doc.sections[1].hidden).toBe(false);
    });

    it('removes a block and a section only after confirmation', async () => {
        confirmed = false;
        button('[data-block="b2"] [data-act="remove-block"]').click();
        await settle(fixture);
        expect(blockIds('hero')).toEqual(['b1', 'b2']);

        confirmed = true;
        button('[data-block="b2"] [data-act="remove-block"]').click();
        await settle(fixture);
        expect(blockIds('hero')).toEqual(['b1']);

        button('[data-section="pricing"] > .le-head [data-act="remove-section"]').click();
        await settle(fixture);
        expect(sectionIds()).toEqual(['hero', 'faq']);
    });

    it('edits text per language and asks the server to preview the language being edited', async () => {
        const title = () => el().querySelector('[data-block="b1"] [data-field="title"]') as HTMLTextAreaElement;
        expect(title().value).toBe('One draft');

        const russian = [...el().querySelectorAll('.le-bar .le-lang')].find(b => b.textContent!.trim() === 'RU') as HTMLButtonElement;
        russian.click();
        await settle(fixture);
        expect(title().value).toBe('Один черновик');
        expect(api.previews.at(-1)!.language).toBe('ru');

        type(title(), 'Новый заголовок');
        await settle(fixture);
        const text = component.doc()!.sections[0].blocks[0].text['title'];
        expect(text).toEqual({ en: 'One draft', ru: 'Новый заголовок' });
    });

    it('marks a text that still owes Russian or English', async () => {
        const block = component.doc()!.sections[0].blocks[0];
        expect(component.missing(block.text['title'], true)).toEqual([]);
        expect(component.missing(block.text['body'], false)).toEqual([]);
        expect(component.missing(undefined, true)).toEqual(['EN', 'RU']);

        component.setField(block, 'body', 'Only English');
        fixture.detectChanges();
        await settle(fixture);
        expect(component.missing(block.text['body'], false)).toEqual(['RU']);
        expect(el().querySelector('[data-block="b1"]')!.textContent).toContain(L.missing('RU'));

        component.setField(block, 'body', '');
        expect(block.text['body']).toBeUndefined();
    });

    it('adds a language, switches to it, and removes it with its texts', async () => {
        const select = el().querySelector('.le-add-language') as HTMLSelectElement;
        expect([...select.options].map(o => o.value)).toEqual(['', 'de']);
        type(select, 'de');
        await settle(fixture);
        button('[data-act="add-language"]').click();
        await settle(fixture);

        const doc = component.doc()!;
        expect(doc.languages).toEqual(['en', 'ru', 'de']);
        expect(component.lang()).toBe('de');
        expect(el().querySelector('.le-add-language')).toBeNull();

        type(el().querySelector('[data-block="b1"] [data-field="title"]') as HTMLTextAreaElement, 'Ein Entwurf');
        await settle(fixture);
        expect(doc.sections[0].blocks[0].text['title']['de']).toBe('Ein Entwurf');
        // An added language is optional: a text without it falls back and is not flagged.
        expect(component.missing(doc.sections[0].blocks[0].text['kicker'], false)).toEqual([]);

        button('[data-act="remove-language"]').click();
        await settle(fixture);
        expect(doc.languages).toEqual(['en', 'ru']);
        expect(doc.sections[0].blocks[0].text['title']).toEqual({ en: 'One draft', ru: 'Один черновик' });
        expect(component.lang()).toBe('en');
        expect(el().querySelector('[data-act="remove-language"]')).toBeNull();
    });

    it('drops the text a new presentation does not draw', () => {
        const block = component.doc()!.sections[0].blocks[0];
        component.setStyle(block, 'copy');
        expect(Object.keys(block.text)).toEqual(['title']);
        block.url = 'https://blog.example';
        component.setStyle(block, 'hero');
        expect(block.url).toBeNull();
    });

    it('keeps board entries lined up across languages by line', () => {
        const item: LandingItem = { text: {}, icon: null, file: null, mark: 'next', entries: [{ en: 'One', ru: 'Один' }, { en: 'Two' }] };
        component.lang.set('ru');
        expect(component.entries(item)).toBe('Один\n');
        component.setEntries(item, 'Один\nДва\nТри');
        expect(item.entries).toEqual([{ en: 'One', ru: 'Один' }, { en: 'Two', ru: 'Два' }, { ru: 'Три' }]);
        component.setEntries(item, '');
        expect(item.entries).toEqual([{ en: 'One' }, { en: 'Two' }]);
    });

    it('redraws the preview from the server a moment after an edit, with the unsaved document', async () => {
        vi.useFakeTimers();
        const before = api.previews.length;
        component.setField(component.doc()!.sections[0].blocks[0], 'title', 'Unsaved headline');
        component.setField(component.doc()!.sections[0].blocks[0], 'kicker', 'Unsaved kicker');
        expect(api.previews.length).toBe(before);

        api.problem = 'Text is required at sections[1].blocks[1].text.title for RU.';
        await vi.advanceTimersByTimeAsync(600);
        vi.useRealTimers();
        await settle(fixture);

        expect(api.previews.length).toBe(before + 1);
        expect(api.previews.at(-1)!.document.sections[0].blocks[0].text['title']['en']).toBe('Unsaved headline');
        expect(api.saved).toBeNull();
        expect(el().querySelector('.le-preview .le-problem')!.textContent).toContain(L.wouldNotSave(api.problem));
    });

    it('saves the whole document and reloads it', async () => {
        component.setField(component.doc()!.sections[0].blocks[0], 'title', 'Saved headline');
        fixture.detectChanges();
        button('[data-act="save"]').click();
        await settle(fixture);

        expect(api.saved!.sections[0].blocks[0].text['title']['en']).toBe('Saved headline');
        expect(api.saved!.sections.map(s => s.id)).toEqual(['hero', 'faq', 'pricing']);
        expect(component.saved()).toBe(true);
        expect(component.dirty()).toBe(false);
    });

    it('attaches a screenshot to a tool by the tool, so reordering tools moves it too', async () => {
        const doc = component.doc()!;
        doc.features[0].shot = 'shot_a.png';
        button('[data-move="f-editor-down"]').click();
        await settle(fixture);
        expect(doc.features.map(f => [f.id, f.shot])).toEqual([['tasks', null], ['editor', 'shot_a.png']]);
        expect(component.isUsed('shot_a.png')).toBe(true);
    });
});
