import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { FormsComponent } from './forms.component';
import { DraftMeta, DraftsService } from '../core/drafts.service';
import { blankFormEdit, FormPreset, FormPresetsService } from '../core/form-presets.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AuthService } from '../core/auth.service';
import { en } from '@localization/en';

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

const GATED = draft('gated', { title: 'Playtest sign-up', isPrivate: true, projectId: 'p1' });
const OTHER_GATED = draft('other', { title: 'Another project', isPrivate: true, projectId: 'p2', updatedAt: '2026-08-05T09:00:00' });
const PUBLIC = draft('public', { title: 'Devlog', projectId: 'p1' });

const PRESET: FormPreset = {
    id: 'preset-1', name: 'Game experience', formJson: JSON.stringify(blankFormEdit('ru')),
    language: 'ru', createdAt: '2026-08-01T09:00:00',
};

const EDITABLE_PRESET: FormPreset = (() => {
    const form = blankFormEdit('ru');
    form.languages.push('en');
    form.questions.push({
        id: 'question-1', type: 'text', required: false,
        label: { ru: 'Имя', en: 'Name' }, options: [],
    });
    return { ...PRESET, formJson: JSON.stringify(form) };
})();

const REGISTRATIONS = [
    { id: 'r1', name: 'Alice', nickname: null, email: null, socialLink: null, answersJson: JSON.stringify({ q1: 'yes' }), createdAt: '2026-08-02T09:00:00', isRevoked: false },
    { id: 'r2', name: null, nickname: 'bob', email: null, socialLink: null, answersJson: JSON.stringify({ q1: 'no' }), createdAt: '2026-08-03T09:00:00', isRevoked: true },
];

class FakeDrafts {
    static access: { id: string; revoked: boolean }[] = [];
    static failRevoke = false;
    async list() { return structuredClone([GATED, OTHER_GATED, PUBLIC]); }
    async revokeRegistration(_id: string, regId: string) {
        if (FakeDrafts.failRevoke) throw new Error('nope');
        FakeDrafts.access.push({ id: regId, revoked: true });
        return { id: regId, isRevoked: true };
    }
    async restoreRegistration(_id: string, regId: string) {
        FakeDrafts.access.push({ id: regId, revoked: false });
        return { id: regId, isRevoked: false };
    }
    async get(id: string) {
        return {
            id, articleTitle: '', cedarJson: '{}', formLanguages: ['ru', 'en'],
            registrationFormJson: JSON.stringify({
                v: 2, languages: ['ru', 'en'], requireName: true,
                intro: { ru: 'Русское вступление', en: 'English intro' },
                questions: [{
                    id: 'q1', type: 'choice', required: true,
                    label: { ru: 'Русский вопрос', en: 'English question' },
                    options: [{ id: 'yes', label: { ru: 'Да', en: 'Yes' } }, { id: 'no', label: { ru: 'Нет', en: 'No' } }],
                }],
            }),
        } as never;
    }
    async listRegistrations() { return structuredClone(REGISTRATIONS); }
}

class FakePresets {
    listCalls = 0;
    nextList: Promise<FormPreset[]> | null = null;

    list() {
        this.listCalls++;
        return this.nextList ?? Promise.resolve([]);
    }

    async create(name: string, formJson: string, language: string) {
        return { id: 'preset-new', name, formJson, language, createdAt: '2026-08-02T09:00:00' };
    }
}

describe('forms page', () => {
    let fixture: ComponentFixture<FormsComponent>;
    let presetsApi: FakePresets;
    const t = en.manager;

    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;
    const header = () => el().querySelector('app-page-header') as HTMLElement;
    const views = () => [...header().querySelectorAll('.forms-views button')] as HTMLButtonElement[];
    const items = () => [...el().querySelectorAll('.registration-item')] as HTMLElement[];

    async function settle() {
        fixture.detectChanges();
        for (let i = 0; i < 8; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    async function create(queryParams: Record<string, string> = {}) {
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: DraftsService, useClass: FakeDrafts },
                { provide: FormPresetsService, useClass: FakePresets },
                ...(Object.keys(queryParams).length
                    ? [{ provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } } }]
                    : []),
            ],
        });
        presetsApi = TestBed.inject(FormPresetsService) as unknown as FakePresets;
        TestBed.inject(LocaleService).set('en');
        vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        fixture = TestBed.createComponent(FormsComponent);
        await settle();
    }

    async function openSubmissions() {
        await page().setView('submissions');
        await settle();
        (el().querySelector('.post-card') as HTMLElement).click();
        await settle();
    }

    describe('presets', () => {
        beforeEach(() => create());

        // ADR-316 §2 — a page of its own: its header, its two views, its creation action.
        it('is a page with its own header and keeps the creation action in exactly one place', async () => {
            expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
            expect(header().querySelector('.page-title')?.textContent?.trim()).toBe(t.forms.crumb);
            expect(views().map(v => v.textContent?.trim())).toEqual([t.forms.presets, t.forms.submissions]);
            expect(views()[0].getAttribute('aria-pressed')).toBe('true');

            expect(header().querySelector('.page-actions app-button')).toBeNull();
            expect(el().querySelectorAll('app-empty-state').length).toBe(1);
            expect([...el().querySelectorAll('app-button')].filter(x => x.textContent?.trim() === t.forms.newPreset).length).toBe(1);

            page().presets.set([PRESET]);
            await settle();
            expect(header().querySelector('.page-actions app-button')?.textContent?.trim()).toBe(t.forms.newPreset);
            expect([...el().querySelectorAll('app-button')].filter(x => x.textContent?.trim() === t.forms.newPreset).length).toBe(1);
            const grid = el().querySelector('.mg-grid') as HTMLElement;
            expect(grid.classList.contains('is-two')).toBe(true);
            expect([...grid.children].every(child => child.classList.contains('split-pane'))).toBe(true);
        });

        it('treats a successful empty preset response as loaded instead of fetching it again', async () => {
            expect(presetsApi.listCalls).toBe(1);
            expect(page().presetsLoaded()).toBe(true);

            await page().loadPresets();
            await page().setView('submissions');
            await page().setView('presets');
            await settle();

            expect(presetsApi.listCalls).toBe(1);
        });

        it('deduplicates an in-flight preset request and shows its loading state', async () => {
            let resolveList!: (value: FormPreset[]) => void;
            presetsApi.nextList = new Promise(resolve => { resolveList = resolve; });
            page().presets.set([]);
            page().presetsLoaded.set(false);
            const callsBefore = presetsApi.listCalls;

            const first = page().loadPresets();
            const second = page().loadPresets();
            await settle();

            expect(first).toBe(second);
            expect(presetsApi.listCalls).toBe(callsBefore + 1);
            expect(el().querySelector('[role="status"]')?.textContent?.trim()).toBe(en.common.loading);
            expect(el().querySelector('.is-forms')).toBeNull();

            resolveList([PRESET]);
            await first;
            await settle();

            expect(page().presetsLoading()).toBe(false);
            expect(el().querySelector('.is-forms')).not.toBeNull();
        });

        it('shows one retryable error instead of presenting a failed preset load as an empty library', async () => {
            let rejectList!: (reason: Error) => void;
            presetsApi.nextList = new Promise((_resolve, reject) => { rejectList = reject; });
            page().presets.set([]);
            page().presetsLoaded.set(false);

            const load = page().loadPresets();
            rejectList(new Error('offline'));
            await load;
            await settle();

            expect(page().presetLoadError()).toBe(t.errors.loadPresets);
            expect(el().querySelectorAll('app-empty-state').length).toBe(1);
            expect([...el().querySelectorAll('app-button')]
                .filter(x => x.textContent?.trim() === t.forms.newPreset).length).toBe(0);

            presetsApi.nextList = Promise.resolve([PRESET]);
            const retry = el().querySelector('app-empty-state app-button button') as HTMLButtonElement;
            expect(retry.textContent?.trim()).toBe(en.projects.canvas.retry);
            retry.click();
            await settle();

            expect(page().presetLoadError()).toBe('');
            expect(el().querySelector('.is-forms')).not.toBeNull();
        });

        it('queries, filters, and sorts the preset library, and keeps it in the URL', async () => {
            const form = blankFormEdit('en');
            form.questions.push({ id: 'q', type: 'text', label: { en: 'Name' }, options: [] });
            const survey: FormPreset = {
                id: 'preset-2', name: 'Alpha survey', formJson: JSON.stringify(form), language: 'en',
                createdAt: '2026-08-03T09:00:00',
            };
            const navigate = TestBed.inject(Router).navigate as ReturnType<typeof vi.fn>;
            page().presets.set([PRESET, survey]);

            page().setPresetSortValue('name:asc');
            expect(page().visiblePresets().map(p => p.name)).toEqual(['Alpha survey', 'Game experience']);
            page().setPresetLanguageFilter('en');
            expect(page().visiblePresets().map(p => p.id)).toEqual(['preset-2']);
            page().onPresetSearch('missing');
            await settle();

            expect(page().visiblePresets()).toEqual([]);
            expect(el().querySelectorAll('app-empty-state').length).toBe(1);
            expect([...el().querySelectorAll('app-button')]
                .filter(x => x.textContent?.trim() === t.clearFilters).length).toBe(1);
            expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({
                queryParams: expect.objectContaining({ formq: 'missing', formlanguage: 'en', formsort: 'name', formdir: 'asc' }),
            }));
        });

        it('names the translate icon and marks the selected question type without colour alone', async () => {
            TestBed.inject(AuthService).planTier.set('Pro');
            page().presets.set([EDITABLE_PRESET]);
            page().presetsLoaded.set(true);
            await page().selectPreset(EDITABLE_PRESET);
            await settle();

            const pills = [...el().querySelectorAll<HTMLButtonElement>('.pill')];
            const selectedPills = pills.filter(button => button.classList.contains('on'));
            expect(pills.length).toBe(6);
            expect(selectedPills.length).toBe(1);
            expect(selectedPills[0].getAttribute('aria-pressed')).toBe('true');
            expect(selectedPills[0].querySelector('.pill-marker.is-visible')).not.toBeNull();

            const translate = [...el().querySelectorAll<HTMLButtonElement>('.lang-chip-act')]
                .find(button => button.getAttribute('aria-label') === t.forms.translateLang);
            expect(translate?.getAttribute('title')).toBe(t.forms.translateLang);

            const questionLabels = [...el().querySelectorAll('.q-label-row .form-field-label')]
                .map(label => label.textContent?.replace(/\s+/g, ' ').trim());
            expect(questionLabels).toEqual([`${t.forms.questionPlaceholder} RU`, `${t.forms.questionPlaceholder} EN`]);

            page().setQuestionType('question-1', 'choice');
            fixture.detectChanges();
            const optionLabels = [...el().querySelectorAll('.option-field .form-field-label')]
                .map(label => label.textContent?.replace(/\s+/g, ' ').trim());
            expect(optionLabels).toEqual([
                `${t.forms.optionPlaceholder} 1 RU`, `${t.forms.optionPlaceholder} 1 EN`,
                `${t.forms.optionPlaceholder} 2 RU`, `${t.forms.optionPlaceholder} 2 EN`,
            ]);
        });

        it('resets the editor to its first field after selection and creation', async () => {
            page().presets.set([PRESET]);
            await settle();
            await page().selectPreset(PRESET);
            await settle();

            const body = el().querySelector('.sheet-body') as HTMLElement;
            body.scrollTop = 240;
            await page().selectPreset(PRESET);
            expect(body.scrollTop).toBe(0);

            body.scrollTop = 120;
            await page().newPreset();
            expect(body.scrollTop).toBe(0);
        });
    });

    describe('submissions', () => {
        beforeEach(async () => {
            FakeDrafts.access = [];
            await create();
        });

        it('lists the private posts and draws the picked one’s answers and their distribution', async () => {
            await page().setView('submissions');
            await settle();

            expect(views()[1].getAttribute('aria-pressed')).toBe('true');
            expect([...el().querySelectorAll('.post-card-title-text')].map(x => x.textContent))
                .toEqual(['Another project', 'Playtest sign-up']);
            expect(el().querySelector('app-empty-state')?.textContent).toContain(t.forms.pickPost);

            (el().querySelectorAll('.post-card')[1] as HTMLElement).click();
            await settle();

            expect(page().selectedPostId()).toBe('gated');
            expect(items().length).toBe(2);
            expect(el().querySelectorAll('.chart-block').length).toBe(1);
            expect(page().distribution(page().chartQuestions()[0]).map(s => [s.label, s.count]))
                .toEqual([['Yes', 1], ['No', 1]]);
            expect(page().regForm()?.intro).toBe('English intro');
            expect(page().registrationAnswers(page().registrations()[0])).toEqual([{ label: 'English question', value: 'Yes' }]);
        });

        // T-108 (ADR-084) — the grant travels with the row: revoke confirms and keeps the entry,
        // restore is one click, and the chip says which rows are out.
        it('revokes a reader behind a confirm, marks the row, and restores in one click', async () => {
            await openSubmissions();

            expect(items()[0].querySelector('.tag.muted')).toBeNull();
            expect(items()[1].querySelector('.tag.muted')?.textContent?.trim()).toBe(t.forms.revokedTag);
            expect(items()[0].querySelector('.registration-revoke')?.getAttribute('aria-label')).toBe(t.forms.revokeAccess);
            expect(items()[1].querySelector('.registration-revoke')?.getAttribute('aria-label')).toBe(t.forms.restoreAccess);

            (items()[0].querySelector('.registration-revoke') as HTMLButtonElement).click();
            await settle();
            expect(FakeDrafts.access).toEqual([]);
            expect(el().querySelector('app-modal')?.textContent).toContain(t.forms.revokeAccessBody('Alice'));

            await page().confirmRevokeRegistration();
            await settle();
            expect(FakeDrafts.access).toEqual([{ id: 'r1', revoked: true }]);
            expect(page().revokeRegistrationTarget()).toBeNull();
            expect(items()[0].classList.contains('is-revoked')).toBe(true);

            (items()[1].querySelector('.registration-revoke') as HTMLButtonElement).click();
            await settle();
            expect(FakeDrafts.access.at(-1)).toEqual({ id: 'r2', revoked: false });
            expect(items()[1].querySelector('.tag.muted')).toBeNull();
        });

        it('offers revoke or restore inside the submission modal and patches the open row', async () => {
            await openSubmissions();

            page().selectedRegistration.set(page().registrations()[1]);
            await settle();
            const access = () => el().querySelector('app-modal .registration-access') as HTMLElement;
            expect(access().textContent?.trim()).toBe(t.forms.restoreAccess);

            await page().restoreRegistration(page().registrations()[1]);
            await settle();
            expect(page().selectedRegistration()?.isRevoked).toBe(false);
            expect(access().textContent?.trim()).toBe(t.forms.revokeAccess);
        });

        it('keeps the confirm open and names the failure when revoking is refused', async () => {
            FakeDrafts.failRevoke = true;
            try {
                await openSubmissions();
                page().revokeRegistrationTarget.set(page().registrations()[0]);
                await page().confirmRevokeRegistration();
                await settle();
                expect(page().revokeRegistrationTarget()?.id).toBe('r1');
                expect(page().registrations()[0].isRevoked).toBe(false);
                expect(page().error()).toBe(t.forms.revokeFailed);
            } finally {
                FakeDrafts.failRevoke = false;
            }
        });
    });

    // ADR-322 and the manager's "View submissions" link.
    it('opens on the asked post’s submissions and lists only the open project’s private posts', async () => {
        await create({ project: 'p1', view: 'submissions', post: 'gated' });
        await settle();

        expect(page().view()).toBe('submissions');
        expect(page().posts().map(p => p.id)).toEqual(['gated']);
        expect(page().selectedPostId()).toBe('gated');
        expect(items().length).toBe(2);
    });
});
