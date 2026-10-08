import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GlossaryTermFormComponent } from './glossary-term-form.component';
import { GLOSSARY_AI_COST, GlossaryEntry, GlossaryService } from '../core/glossary.service';
import { AssetsService, LibraryAsset } from '../core/assets.service';
import { AuthService } from '../core/auth.service';
import { en } from '@localization/en';

const ENTRY: GlossaryEntry = {
    id: 'e1', name: 'Renderer', description: 'Draws frames.', imageUrl: '/media/asset_1.png',
    isCaseSensitive: true, projectId: 'p1', updatedAt: '2026-08-01T09:00:00',
    languages: [
        { id: 'ru1', language: 'ru', localizedName: 'рендерер', spellings: ['рендерера', 'рендереру'], localizedDescription: 'Рисует кадры.' },
        { id: 'en1', language: 'en', localizedName: '', spellings: [], localizedDescription: '' },
    ],
};

class FakeGlossary {
    translated: [string, string, string][] = [];
    described: [string, string, string | null][] = [];
    failOn: string | null = null;
    async aiTranslate(name: string, description: string, targetLanguage: string) {
        this.translated.push([name, description, targetLanguage]);
        if (this.failOn === targetLanguage) throw new HttpErrorResponse({ status: 402, error: { error: 'Not enough credits' } });
        return { language: targetLanguage, localizedName: `${name}-${targetLanguage}`,
            spellings: [`${name}-a`, `${name}-b`], localizedDescription: `${description}-${targetLanguage}` };
    }
    async aiDescribe(name: string, language: string, imageUrl: string | null) {
        this.described.push([name, language, imageUrl]);
        return { description: 'Written by AI.', credits: imageUrl ? 2 : 1 };
    }
    async suggestForms() { return { forms: ['рендерера', 'рендерером'] }; }
}

class FakeAssets {
    async list() { return { items: [], total: 0 }; }
}

describe('glossary entry form', () => {
    let fixture: ComponentFixture<GlossaryTermFormComponent>;
    const t = en.glossary;
    const form = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;
    const api = () => TestBed.inject(GlossaryService) as unknown as FakeGlossary;
    const blocks = () => [...el().querySelectorAll('fieldset.term-lang')] as HTMLElement[];
    const pick = (language: string) => el().querySelector(`.lang-pick input[data-language="${language}"]`) as HTMLInputElement;
    const button = (cls: string) => el().querySelector(`app-button.${cls} button`) as HTMLButtonElement;

    async function settle() {
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
    }

    async function create(inputs: Partial<GlossaryTermFormComponent> = {}, plan: string | null = 'Pro') {
        TestBed.configureTestingModule({
            providers: [
                { provide: GlossaryService, useClass: FakeGlossary },
                { provide: AssetsService, useClass: FakeAssets },
            ],
        });
        TestBed.inject(AuthService).planTier.set(plan);
        fixture = TestBed.createComponent(GlossaryTermFormComponent);
        Object.assign(fixture.componentInstance, inputs);
        await settle();
    }

    it('starts a new entry with the name it was handed and the one language being written', async () => {
        await create({ initialName: 'Ferry', initialLanguage: 'en' });

        expect(form().name).toBe('Ferry');
        expect(blocks().map(b => b.getAttribute('data-language'))).toEqual(['en']);
        expect(pick('en').checked).toBe(true);
        expect(pick('ru').checked).toBe(false);
        expect(form().canSave()).toBe(false);

        form().description = 'A boat.';
        expect(form().canSave()).toBe(true);
        expect(form().value()).toEqual({
            name: 'Ferry', description: 'A boat.', imageUrl: null, isCaseSensitive: false, projectId: null,
            languages: [{ language: 'en', localizedName: '', spellings: [], localizedDescription: '' }],
        });
    });

    it('shows every language of an existing entry, with its name, spellings and description', async () => {
        await create({ entry: ENTRY });

        expect(blocks().map(b => b.getAttribute('data-language'))).toEqual(['ru', 'en']);
        const russian = blocks()[0];
        expect((russian.querySelector('input[data-field="name"]') as HTMLInputElement).value).toBe('рендерер');
        expect((russian.querySelector('input[data-field="spellings"]') as HTMLInputElement).value).toBe('рендерера, рендереру');
        expect((russian.querySelector('textarea') as HTMLTextAreaElement).value).toBe('Рисует кадры.');
        expect((el().querySelector('input[name="glossary-case"]') as HTMLInputElement).checked).toBe(true);
        expect(el().querySelector('.term-image-row img')?.getAttribute('src')).toBe('/media/asset_1.png');
        expect(form().value().projectId).toBe('p1');
        expect(form().value().languages[0].spellings).toEqual(['рендерера', 'рендереру']);
    });

    it('adds and removes a language with its tick, and cannot be saved with none', async () => {
        await create({ entry: ENTRY });

        pick('de').click();
        await settle();
        expect(blocks().map(b => b.getAttribute('data-language'))).toEqual(['ru', 'en', 'de']);

        pick('en').click();
        pick('de').click();
        pick('ru').click();
        await settle();
        expect(blocks()).toEqual([]);
        expect(form().canSave()).toBe(false);
    });

    it('locks a language the plan does not include, but keeps one the entry already has', async () => {
        await create({ entry: ENTRY }, null);

        expect(pick('ru').disabled).toBe(false);
        expect(pick('de').disabled).toBe(true);
        form().toggleLanguage('de');
        expect(form().hasLanguage('de')).toBe(false);
        expect(pick('ja').disabled).toBe(false);
    });

    it('splits the spellings line into a list and drops the blanks', async () => {
        await create({ initialName: 'Unity', initialLanguage: 'ru' });
        form().description = 'd';
        form().languages()[0].spellings = ' юнити ,, Юнити 3D , ';

        expect(form().value().languages[0].spellings).toEqual(['юнити', 'Юнити 3D']);
    });

    it('merges suggested Russian word forms into what the author already wrote', async () => {
        await create({ entry: ENTRY });
        const russian = form().languages().find(l => l.language === 'ru')!;
        expect(form().canSuggest(russian)).toBe(true);
        expect(form().canSuggest(form().languages().find(l => l.language === 'en')!)).toBe(false);

        await form().suggestForms(russian);

        expect(russian.spellings).toBe('рендерера, рендереру, рендерером');
    });

    it('fills only the languages nobody has written yet, one call each', async () => {
        await create({ entry: ENTRY });
        pick('de').click();
        await settle();

        button('ai-translate').click();
        await settle();

        expect(api().translated).toEqual([['Renderer', 'Draws frames.', 'en'], ['Renderer', 'Draws frames.', 'de']]);
        const value = form().value();
        expect(value.languages.find(l => l.language === 'de')).toEqual({
            language: 'de', localizedName: 'Renderer-de', spellings: ['Renderer-a', 'Renderer-b'],
            localizedDescription: 'Draws frames.-de',
        });
        expect(value.languages.find(l => l.language === 'ru')!.localizedName).toBe('рендерер');
        expect(form().canAiTranslate()).toBe(false);
    });

    it('stops at the first language that fails and says why', async () => {
        await create({ initialName: 'Ferry', initialLanguage: 'en' });
        pick('de').click();
        pick('ru').click();
        await settle();
        api().failOn = 'en';

        await form().aiTranslate();
        await settle();

        expect(api().translated.map(c => c[2])).toEqual(['ru', 'en']);
        expect(el().querySelector('.channel-error')?.textContent).toContain('Not enough credits');
        expect(form().languages().find(l => l.language === 'ru')!.localizedName).toBe('Ferry-ru');
        expect(form().languages().find(l => l.language === 'de')!.localizedName).toBe('');
    });

    it('writes the description from the name, and from the image when the entry has one', async () => {
        await create({ initialName: 'Ferry', initialLanguage: 'en' });
        expect(el().querySelector('.ai-row .field-hint-inline')?.textContent).toContain(t.aiDescribeHint(GLOSSARY_AI_COST.text));

        button('ai-describe').click();
        await settle();
        expect(api().described).toEqual([['Ferry', 'en', null]]);
        expect(form().description).toBe('Written by AI.');

        form().pickedImage({ localPath: 'asset_9.png' } as LibraryAsset);
        await settle();
        expect(el().querySelector('.ai-row .field-hint-inline')?.textContent).toContain(t.aiDescribeHintImage(GLOSSARY_AI_COST.image));
        await form().aiDescribe();
        expect(api().described[1]).toEqual(['Ferry', 'en', '/media/asset_9.png']);
    });

    it('describes from an image alone, and from nothing not at all', async () => {
        await create({ initialLanguage: 'ru' });
        expect(form().canAiDescribe()).toBe(false);
        expect(button('ai-describe').disabled).toBe(true);

        form().pickedImage({ localPath: 'asset_9.png' } as LibraryAsset);
        expect(form().canAiDescribe()).toBe(true);
    });

    it('wears the plan lock on both AI actions when the plan has no AI', async () => {
        await create({ initialName: 'Ferry', initialLanguage: 'en' }, null);

        expect(button('ai-describe').disabled).toBe(true);
        expect(button('ai-translate').disabled).toBe(true);
        expect(el().querySelectorAll('.ai-row app-plan-lock').length).toBe(2);
        await form().aiTranslate();
        await form().aiDescribe();
        expect(api().translated).toEqual([]);
        expect(api().described).toEqual([]);
    });

    it('opens the asset library picker for the image and can take the image away', async () => {
        await create({ entry: ENTRY });

        button('image-pick').click();
        await settle();
        expect(el().querySelector('app-media-picker')).toBeTruthy();

        form().pickedImage({ localPath: 'asset_2.png' } as LibraryAsset);
        await settle();
        expect(el().querySelector('app-media-picker')).toBeNull();
        expect(form().value().imageUrl).toBe('/media/asset_2.png');

        button('image-remove').click();
        await settle();
        expect(form().value().imageUrl).toBeNull();
    });
});
