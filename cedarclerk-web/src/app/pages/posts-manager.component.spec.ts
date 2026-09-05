import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { PostsManagerComponent } from './posts-manager.component';
import { DraftMeta, DraftsService } from '../core/drafts.service';
import { blankFormEdit, FormPreset, FormPresetsService } from '../core/form-presets.service';
import { PostsService } from '../core/posts.service';
import { PublishService } from '../core/publish.service';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AuthService } from '../core/auth.service';
import { en } from '../core/i18n/en';

function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(style => style.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        sheet => Array.from(sheet.cssRules).map(rule => rule.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(text => text.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n').replace(/\/\*[\s\S]*?\*\//g, ' ');
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

// Two live posts (one with a Telegram message and a translation), one private draft that never
// went out, one archived. The three publish states are what the status stamp is read against,
// and the unpublished one is what proves the blog row says so rather than rendering an empty
// link. Four posts, two published, one archived and one pending schedule: every number the
// header and the inspector print is different from every other, so none of them can be standing
// in for a neighbour.
const LIVE = draft('live', {
    title: 'Devlog 12', blogSlug: 'devlog-12', isBlogPublished: true,
    blogPublishedAt: '2026-08-10T09:00:00', languages: ['en'],
    lastTelegramUsername: 'testingandfun', lastTelegramMessageId: 42,
});
const EARLIER = draft('earlier', {
    title: 'Devlog 11', blogSlug: 'devlog-11', isBlogPublished: true,
    blogPublishedAt: '2026-08-03T09:00:00',
});
const DRAFTED = draft('drafted', { title: 'Notes', isPrivate: true });
const OLD = draft('old', { title: 'Retired', isArchived: true });

const PRESET: FormPreset = {
    id: 'preset-1',
    name: 'Game experience',
    formJson: JSON.stringify(blankFormEdit('ru')),
    language: 'ru',
    createdAt: '2026-08-01T09:00:00',
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

const SNAPSHOTS = [
    { viewCount: 100, likeCount: 4, dislikeCount: 0, commentCount: 1, takenAt: '2026-08-09T03:30:00Z' },
    { viewCount: 130, likeCount: 6, dislikeCount: 0, commentCount: 2, takenAt: '2026-08-10T03:30:00Z' },
    { viewCount: 150, likeCount: 9, dislikeCount: 0, commentCount: 2, takenAt: '2026-08-11T03:30:00Z' },
];

const REGISTRATIONS = [
    { id: 'r1', name: 'Alice', nickname: null, email: null, socialLink: null, answersJson: null, createdAt: '2026-08-02T09:00:00', isRevoked: false },
    { id: 'r2', name: null, nickname: 'bob', email: null, socialLink: null, answersJson: null, createdAt: '2026-08-03T09:00:00', isRevoked: true },
];

class FakeDrafts {
    static access: { id: string; revoked: boolean }[] = [];
    static failRevoke = false;
    async list() { return structuredClone([LIVE, EARLIER, DRAFTED, OLD]); }
    async revokeRegistration(_id: string, regId: string) {
        if (FakeDrafts.failRevoke) throw new Error('nope');
        FakeDrafts.access.push({ id: regId, revoked: true });
        return { id: regId, isRevoked: true };
    }
    async restoreRegistration(_id: string, regId: string) {
        FakeDrafts.access.push({ id: regId, revoked: false });
        return { id: regId, isRevoked: false };
    }
    async listFolders() { return []; }
    async get(id: string) {
        return {
            id, articleTitle: '', cedarJson: '{}', formLanguages: ['ru', 'en'],
            registrationFormJson: JSON.stringify({
                v: 2, languages: ['ru', 'en'], requireName: true,
                intro: { ru: 'Русское вступление', en: 'English intro' },
                questions: [{
                    id: 'q1', type: 'choice', required: true,
                    label: { ru: 'Русский вопрос', en: 'English question' },
                    options: [{ id: 'yes', label: { ru: 'Да', en: 'Yes' } }],
                }],
            }),
        } as never;
    }
    async listRegistrations() { return structuredClone(REGISTRATIONS); }
}

class FakePosts {
    async listScheduled() {
        return [{
            id: 's1', draftId: 'live', status: 'Pending', language: 'ru', chatId: '1',
            targetName: 'Testing', scheduledAtUtc: '2026-08-20T18:00:00Z', network: 'telegram', error: null,
        }] as never;
    }
    async statHistory() { return { snapshots: structuredClone(SNAPSHOTS) }; }
}

class FakePublish {
    async published() {
        return {
            posts: [{
                draftId: 'live', network: 'bluesky', language: 'en', targetId: 'b1',
                publicUrl: 'https://bsky.app/p/1', remoteId: '1', partCount: 1,
                finishedAt: '2026-08-10T10:00:00',
            }],
        } as never;
    }
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

class FakeComments {
    newComments = signal(0);
    newReactions = signal(0);
    async refreshNewCount() {}
    async listAll() { return { reactions: { likes: 0, dislikes: 0, newLikes: 0, newDislikes: 0 }, reactionsByDraft: [], comments: [] }; }
}

describe('posts manager', () => {
    let fixture: ComponentFixture<PostsManagerComponent>;
    let feedback: CommentsService;
    let presetsApi: FakePresets;
    const t = en.manager;

    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;
    const tiles = () => [...el().querySelectorAll('.manager-tabs [role="tab"]')] as HTMLElement[];
    const cards = () => [...el().querySelectorAll('.post-card')] as HTMLElement[];
    const card = (title: string) => cards().find(c => c.textContent?.includes(title))!;
    const shelf = () => el().querySelector('.inspector') as HTMLElement;
    const rows = () => [...shelf().querySelectorAll('app-spec-row')] as HTMLElement[];
    const row = (label: string) => rows().find(r => r.querySelector('.label')?.textContent?.trim() === label);
    const rowValue = (label: string) => row(label)?.querySelector('.text')?.textContent?.trim();
    const sheet = () => el().querySelector('.sheet') as HTMLElement;
    const header = () => el().querySelector('app-page-header') as HTMLElement;

    async function settle() {
        fixture.detectChanges();
        for (let i = 0; i < 6; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(async () => {
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: DraftsService, useClass: FakeDrafts },
                { provide: PostsService, useClass: FakePosts },
                { provide: PublishService, useClass: FakePublish },
                { provide: FormPresetsService, useClass: FakePresets },
                { provide: CommentsService, useClass: FakeComments },
            ],
        });
        feedback = TestBed.inject(CommentsService);
        presetsApi = TestBed.inject(FormPresetsService) as unknown as FakePresets;
        TestBed.inject(LocaleService).set('en');
        // A blog lives at its owner's subdomain, and the component asks the server which one.
        TestBed.inject(AuthService).blogUrl.set('https://martycow.cedarclerk.app');
        fixture = TestBed.createComponent(PostsManagerComponent);
        await settle();
    });

    // The strip is a tablist, and the tally it carries is the badge rules' own: nothing at zero,
    // and the count when there is one.
    it('draws the sections as tabs and badges the feedback tally on Posts', async () => {
        expect(tiles().map(x => x.firstChild?.textContent?.trim()))
            .toEqual([t.tabs.posts, t.tabs.stats, t.tabs.forms]);
        expect(tiles()[0].getAttribute('aria-selected')).toBe('true');
        expect(tiles()[0].querySelector('.seg-badge')).toBeNull();

        feedback.newComments.set(3);
        feedback.newReactions.set(4);
        await settle();
        expect(tiles()[0].querySelector('.seg-badge')?.textContent?.trim()).toBe('7');
    });

    it('switches the body with the tab, and keeps the inspector out of the forms tab', async () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        expect(page().tab()).toBe('posts');
        tiles()[2].click();
        await settle();

        expect(page().tab()).toBe('forms');
        expect(el().querySelector('.inspector')).toBeNull();
        expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({
            queryParams: expect.objectContaining({ tab: 'forms' }),
        }));
    });

    it('declares the operational measure and uses the shared fluid pane anatomy', async () => {
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        const postsGrid = el().querySelector('.mg-grid') as HTMLElement;
        expect(postsGrid.classList.contains('split-workspace')).toBe(true);
        expect(postsGrid.classList.contains('is-two')).toBe(false);
        expect([...postsGrid.children].every(child => child.classList.contains('split-pane'))).toBe(true);

        page().presets.set([PRESET]);
        page().presetsLoaded.set(true);
        tiles()[2].click();
        await settle();
        const formsGrid = el().querySelector('.mg-grid') as HTMLElement;
        expect(formsGrid.classList.contains('split-workspace')).toBe(true);
        expect(formsGrid.classList.contains('is-two')).toBe(true);
        expect([...formsGrid.children].every(child => child.classList.contains('split-pane'))).toBe(true);
    });

    // ADR-239 clause 6 — the screen-level counts are the header's meta line, the same on every
    // tab. An empty Forms list owns its creation action; once data exists it moves to the header.
    it('draws the counts and keeps the Forms creation action in exactly one place', async () => {
        expect(header().querySelector('.page-title')?.textContent?.trim()).toBe(t.crumb);
        const meta = [...header().querySelectorAll('.page-meta > span:not(.sep)')].map(s => s.textContent?.trim());
        expect(meta).toEqual([t.rulerPosts(4), t.rulerPublished(2), t.rulerScheduled(1)]);
        expect(header().querySelector('.page-actions .btn')).toBeNull();

        tiles()[2].click();
        await settle();
        expect(header().querySelector('.page-actions .btn')).toBeNull();
        expect(el().querySelectorAll('app-empty-state').length).toBe(1);
        expect(el().querySelector('.is-forms')).toBeNull();
        expect([...el().querySelectorAll('app-button')].filter(x => x.textContent?.trim() === t.forms.newPreset).length).toBe(1);

        page().presets.set([PRESET]);
        await settle();
        expect(header().querySelector('.page-actions .btn')?.textContent?.trim()).toBe(t.forms.newPreset);
        expect([...el().querySelectorAll('app-button')].filter(x => x.textContent?.trim() === t.forms.newPreset).length).toBe(1);
    });

    it('treats a successful empty preset response as loaded instead of fetching it again', async () => {
        expect(presetsApi.listCalls).toBe(1);
        expect(page().presetsLoaded()).toBe(true);

        page().setTab('forms');
        page().setTab('posts');
        page().setTab('forms');
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
        page().selectedId.set('drafted');
        await settle();

        expect(el().querySelector('app-form-ref')).toBeNull();
        expect(el().querySelector('[role="status"]')?.textContent?.trim()).toBe(en.common.loading);

        page().setTab('forms');
        await settle();

        expect(first).toBe(second);
        expect(presetsApi.listCalls).toBe(callsBefore + 1);
        expect(page().presetsLoading()).toBe(true);
        expect(el().querySelector('[role="status"]')?.textContent?.trim()).toBe(en.common.loading);
        expect(el().querySelector('.is-forms')).toBeNull();

        resolveList([PRESET]);
        await first;
        await settle();

        expect(page().presetsLoaded()).toBe(true);
        expect(page().presetsLoading()).toBe(false);
        expect(el().querySelector('.is-forms')).not.toBeNull();
    });

    it('shows one retryable error instead of presenting a failed preset load as an empty library', async () => {
        let rejectList!: (reason: Error) => void;
        presetsApi.nextList = new Promise((_resolve, reject) => { rejectList = reject; });
        page().presets.set([]);
        page().presetsLoaded.set(false);

        const load = page().loadPresets();
        page().setTab('forms');
        rejectList(new Error('offline'));
        await load;
        await settle();

        expect(page().presetsLoaded()).toBe(false);
        expect(page().presetLoadError()).toBe(t.errors.loadPresets);
        expect(el().querySelectorAll('app-empty-state').length).toBe(1);
        expect([...el().querySelectorAll('app-button')]
            .filter(x => x.textContent?.trim() === t.forms.newPreset).length).toBe(0);

        presetsApi.nextList = Promise.resolve([PRESET]);
        const retry = el().querySelector('app-empty-state app-button button') as HTMLButtonElement;
        expect(retry.textContent?.trim()).toBe(en.projects.canvas.retry);
        retry.click();
        await settle();

        expect(page().presetsLoaded()).toBe(true);
        expect(page().presetLoadError()).toBe('');
        expect(el().querySelector('.is-forms')).not.toBeNull();
    });

    // ADR-167 clause 4. The values are the assertion: a shelf that stayed on the library's numbers
    // while a post was picked would still be 'selection'-scoped and would still pass a scope check.
    it('describes the library until a post is picked, then that post and nothing else', async () => {
        expect(page().inspectorScope()).toBe('document');
        expect(rows().every(r => r.getAttribute('data-scope') === 'document')).toBe(true);
        expect(rowValue(t.inspector.posts)).toBe('4');
        expect(rowValue(t.inspector.published)).toBe('2');
        expect(rowValue(t.inspector.archivedCount)).toBe('1');

        card('Devlog 12').click();
        await settle();

        expect(card('Devlog 12').getAttribute('aria-current')).toBe('true');
        expect(page().inspectorScope()).toBe('selection');
        expect(rows().every(r => r.getAttribute('data-scope') === 'selection')).toBe(true);
        expect(row(t.inspector.posts)).toBeUndefined();
        expect(shelf().querySelector('.tag')?.textContent?.trim()).toBe('LIVE');
        expect(rowValue(t.inspector.visibility)).toBe(t.inspector.public);
    });

    // ADR-167 clause 3 — the links are facts about the post, so the shelf is the only place they
    // are drawn; the sheet keeps only what is done to it.
    it('puts every outbound link on the shelf and none of them back on the sheet', async () => {
        card('Devlog 12').click();
        await settle();

        const hrefs = [...shelf().querySelectorAll('a.insp-link')].map(a => a.getAttribute('href'));
        expect(hrefs).toEqual([
            'https://martycow.cedarclerk.app/devlog-12',
            'https://t.me/testingandfun/42',
            'https://bsky.app/p/1',
        ]);
        expect(sheet().querySelectorAll('a[href^="http"]').length).toBe(0);
    });

    // T-108 (ADR-084) — the grant travels with the row: revoke confirms and keeps the entry,
    // restore is one click, and the chip says which rows are out.
    it('revokes a reader behind a confirm, marks the row, and restores in one click', async () => {
        FakeDrafts.access = [];
        card('Notes').click();
        await settle();

        const items = () => [...el().querySelectorAll('.registration-item')] as HTMLElement[];
        expect(items().length).toBe(2);
        expect(items()[0].querySelector('.tag.muted')).toBeNull();
        expect(items()[1].querySelector('.tag.muted')?.textContent?.trim()).toBe(t.forms.revokedTag);
        expect(items()[0].querySelector('.registration-revoke')?.getAttribute('aria-label')).toBe(t.forms.revokeAccess);
        expect(items()[1].querySelector('.registration-revoke')?.getAttribute('aria-label')).toBe(t.forms.restoreAccess);

        (items()[0].querySelector('.registration-revoke') as HTMLButtonElement).click();
        await settle();
        expect(FakeDrafts.access).toEqual([]);
        expect(page().revokeRegistrationTarget()?.id).toBe('r1');
        expect(el().querySelector('app-modal')?.textContent).toContain(t.forms.revokeAccessBody('Alice'));

        await page().confirmRevokeRegistration();
        await settle();
        expect(FakeDrafts.access).toEqual([{ id: 'r1', revoked: true }]);
        expect(page().revokeRegistrationTarget()).toBeNull();
        expect(items()[0].querySelector('.tag.muted')?.textContent?.trim()).toBe(t.forms.revokedTag);
        expect(items()[0].classList.contains('is-revoked')).toBe(true);

        (items()[1].querySelector('.registration-revoke') as HTMLButtonElement).click();
        await settle();
        expect(FakeDrafts.access.at(-1)).toEqual({ id: 'r2', revoked: false });
        expect(items()[1].querySelector('.tag.muted')).toBeNull();
    });

    it('offers revoke or restore inside the submission modal and patches the open row', async () => {
        FakeDrafts.access = [];
        card('Notes').click();
        await settle();

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
            card('Notes').click();
            await settle();
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

    it('resolves submission questions and options in the current UI language', async () => {
        card('Notes').click();
        await settle();
        expect(page().regForm()?.intro).toBe('English intro');
        expect(page().regForm()?.questions[0]?.label).toBe('English question');
        expect(page().regForm()?.questions[0]?.options?.[0]?.label).toBe('Yes');
    });

    // ADR-163/ADR-169 — the one action here that opens something is the header's primary and
    // carries its address, and the address names the picked post: the editor falls back to the
    // newest draft when the query is missing, so a link to the wrong id and a link to none look
    // identical on screen.
    it('opens the picked post in the editor as the header link, not a handler', async () => {
        card('Devlog 12').click();
        await settle();

        const open = header().querySelector('.open-in-editor a') as HTMLAnchorElement;
        expect(open.tagName).toBe('A');
        expect(open.getAttribute('href')).toBe('/editor?draft=live');
        expect([...el().querySelectorAll('a')].filter(a => a.textContent?.includes(t.openInEditor)).length).toBe(1);

        card('Notes').click();
        await settle();
        const other = header().querySelector('.open-in-editor a') as HTMLAnchorElement;
        expect(other.getAttribute('href')).toBe('/editor?draft=drafted');
    });

    it('offers a useful action when no post is selected', async () => {
        const action = sheet().querySelector('app-empty-state app-button button') as HTMLButtonElement;
        expect(action.textContent?.trim()).toBe(t.selectFirstPost);
        action.click();
        await settle();
        expect(page().selectedId()).toBe('live');
    });

    it('keeps the publication journey inside a narrow working pane', () => {
        const css = sheetFor('.publish-journey');
        expect(css).toMatch(/\.sheet-body[^{}]*\{[^{}]*overflow-x\s*:\s*hidden/i);
        expect(css).toMatch(/\.publish-journey[^{}]*\{[^{}]*minmax\(0(?:px)?,\s*auto\)/i);
        expect(css).toMatch(/\.journey-step[^{}]*\{[^{}]*min-width\s*:\s*0(?:px)?/i);
    });

    it('uses one empty-state surface and one action for search misses and an empty post library', async () => {
        page().onPostSearch('not in the library');
        await settle();

        expect(el().querySelector('.post-list app-empty-state')).toBeNull();
        expect(el().querySelectorAll('app-empty-state').length).toBe(1);
        expect([...el().querySelectorAll('app-button')]
            .filter(x => x.textContent?.trim() === t.clearFilters).length).toBe(1);

        page().onPostSearch('');
        page().drafts.set([]);
        page().selectedId.set(null);
        await settle();

        expect(el().querySelector('.post-list app-empty-state')).toBeNull();
        expect(el().querySelectorAll('app-empty-state').length).toBe(1);
        expect([...el().querySelectorAll('app-button')]
            .filter(x => x.textContent?.trim() === t.writeFirst).length).toBe(1);
    });

    it('filters and stably sorts only publishable, non-template posts', async () => {
        page().drafts.set([
            LIVE, EARLIER, DRAFTED, OLD,
            draft('working-note', { title: 'Internal note', documentType: 'note' }),
            draft('template', { title: 'Post template', isTemplate: true }),
            draft('changelog', { title: 'Alpha release', documentType: 'changelog' }),
        ]);
        page().setPostSortValue('title:asc');
        await settle();

        expect(page().postPool().map(d => d.id)).toEqual(['live', 'earlier', 'drafted', 'old', 'changelog']);
        expect(page().visiblePosts().map(d => d.title)).toEqual([
            'Alpha release', 'Devlog 11', 'Devlog 12', 'Notes', 'Retired',
        ]);

        page().setVisibilityFilter('private');
        expect(page().visiblePosts().map(d => d.id)).toEqual(['drafted']);
        page().setVisibilityFilter('all');
        page().setStateFilter('archived');
        expect(page().visiblePosts().map(d => d.id)).toEqual(['old']);
    });

    it('connects the Posts filter trigger to its labelled dialog', () => {
        const trigger = el().querySelector('.post-filter-trigger') as HTMLButtonElement;
        expect(trigger.getAttribute('aria-haspopup')).toBe('dialog');
        expect(trigger.getAttribute('aria-controls')).toBe('post-filter-panel');
        trigger.click();
        fixture.detectChanges();

        const panel = el().querySelector('#post-filter-panel') as HTMLElement;
        expect(panel.getAttribute('role')).toBe('dialog');
        expect(panel.getAttribute('aria-label')).toBe(t.filters);
    });

    it('shows the active sort value and replaces a broken cover with the document type', async () => {
        page().drafts.set([
            { ...LIVE, updatedAt: '2026-08-12T09:00:00', viewCount: 40, reactionCount: 3,
                coverImagePath: 'missing.jpg' },
            { ...EARLIER, updatedAt: '2026-08-11T09:00:00', viewCount: 10, reactionCount: 2 },
        ]);
        page().setPostSortValue('activity:desc');
        await settle();

        expect(cards()[0].textContent).toContain(t.activityMetric(43));
        const image = cards()[0].querySelector('img') as HTMLImageElement;
        image.dispatchEvent(new Event('error'));
        await settle();
        expect(cards()[0].querySelector('img')).toBeNull();
        expect(cards()[0].querySelector('app-icon')).not.toBeNull();

        page().setPostSortValue('updated:desc');
        await settle();
        expect(cards()[0].querySelector('.post-card-meta')?.getAttribute('title')).toBeNull();
        expect(cards()[0].querySelector(`[title="${t.editedOn}"]`)).not.toBeNull();
    });

    it('queries, filters, and sorts the Forms collection independently', async () => {
        const form = blankFormEdit('en');
        form.questions.push({ id: 'q', type: 'text', label: { en: 'Name' }, options: [] });
        const survey: FormPreset = {
            id: 'preset-2', name: 'Alpha survey', formJson: JSON.stringify(form), language: 'en',
            createdAt: '2026-08-03T09:00:00',
        };
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        page().presets.set([PRESET, survey]);
        page().setTab('forms');

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
        page().setTab('forms');
        await page().selectPreset(EDITABLE_PRESET);
        await settle();

        const pills = [...el().querySelectorAll<HTMLButtonElement>('.pill')];
        const selectedPills = pills.filter(button => button.classList.contains('on'));
        expect(pills.length).toBe(6);
        expect(selectedPills.length).toBe(1);
        expect(selectedPills[0].getAttribute('aria-pressed')).toBe('true');
        expect(selectedPills[0].querySelector('.pill-marker.is-visible')).not.toBeNull();
        expect(pills.filter(button => !button.classList.contains('on'))
            .every(button => button.querySelector('.pill-marker.is-visible') === null)).toBe(true);

        const translate = [...el().querySelectorAll<HTMLButtonElement>('.lang-chip-act')]
            .find(button => button.getAttribute('aria-label') === t.forms.translateLang);
        expect(translate).toBeDefined();
        expect(translate?.getAttribute('title')).toBe(t.forms.translateLang);

        const questionLabels = [...el().querySelectorAll('.q-label-row .form-field-label')]
            .map(label => label.textContent?.replace(/\s+/g, ' ').trim());
        expect(questionLabels).toEqual([
            `${t.forms.questionPlaceholder} RU`,
            `${t.forms.questionPlaceholder} EN`,
        ]);

        page().setQuestionType('question-1', 'choice');
        fixture.detectChanges();
        const optionLabels = [...el().querySelectorAll('.option-field .form-field-label')]
            .map(label => label.textContent?.replace(/\s+/g, ' ').trim());
        expect(optionLabels).toEqual([
            `${t.forms.optionPlaceholder} 1 RU`, `${t.forms.optionPlaceholder} 1 EN`,
            `${t.forms.optionPlaceholder} 2 RU`, `${t.forms.optionPlaceholder} 2 EN`,
        ]);
    });

    it('resets the Forms sheet to its first field after tab, selection, and creation changes', async () => {
        page().presets.set([PRESET]);
        page().setTab('forms');
        await settle();
        await page().selectPreset(PRESET);
        await settle();

        const body = el().querySelector('.sheet-body') as HTMLElement;
        body.scrollTop = 240;
        await page().selectPreset(PRESET);
        expect(body.scrollTop).toBe(0);

        body.scrollTop = 180;
        page().setTab('forms');
        expect(body.scrollTop).toBe(0);

        body.scrollTop = 120;
        await page().newPreset();
        expect(body.scrollTop).toBe(0);
    });

    it('says a post is not published rather than drawing an empty link', async () => {
        card('Notes').click();
        await settle();

        expect(rowValue(t.blog)).toBeUndefined();
        expect(shelf().querySelector('.insp-none')?.textContent?.trim()).toBe(t.inspector.notPublished);
        expect(shelf().querySelectorAll('a.insp-link').length).toBe(0);
    });

    // ADR-167 clause 6 — three named lines on their own slots, and the leaf strip filters them.
    it('draws the nightly snapshots as one chart per metric, and a leaf switches a line off', async () => {
        card('Devlog 12').click();
        await settle();
        // Through the real path: the fetch is tied to the group being opened, and a test that
        // called loadHistory directly would pass over a group that never opens.
        const growth = [...sheet().querySelectorAll('details.detail-group')]
            .find(d => d.querySelector('summary')?.textContent?.trim() === t.groups.growth) as HTMLDetailsElement;
        growth.open = true;
        growth.dispatchEvent(new Event('toggle'));
        await settle();

        expect(page().growthSeries()!.map(s => `${s.name}:${s.slot}`))
            .toEqual([t.groups.views + ':1', t.groups.likes + ':2', t.groups.comments + ':3']);
        expect(page().growthSeries()![0].points).toEqual([100, 130, 150]);
        expect(page().growthLabels().length).toBe(3);

        page().toggleGrowthMetric('likeCount');
        expect(page().growthSeries()!.map(s => s.name)).toEqual([t.groups.views, t.groups.comments]);
    });
});
