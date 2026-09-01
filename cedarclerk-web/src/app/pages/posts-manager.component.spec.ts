import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PostsManagerComponent } from './posts-manager.component';
import { DraftMeta, DraftsService } from '../core/drafts.service';
import { FormPresetsService } from '../core/form-presets.service';
import { PostsService } from '../core/posts.service';
import { PublishService } from '../core/publish.service';
import { CommentsService } from '../core/comments.service';
import { RulerService } from '../core/ruler.service';
import { RailActionsService } from '../core/rail-actions.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AuthService } from '../core/auth.service';
import { en } from '../core/i18n/en';

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
// rule and the shelf print is different from every other, so none of them can be standing in
// for a neighbour.
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

const SNAPSHOTS = [
    { viewCount: 100, likeCount: 4, dislikeCount: 0, commentCount: 1, takenAt: '2026-08-09T03:30:00Z' },
    { viewCount: 130, likeCount: 6, dislikeCount: 0, commentCount: 2, takenAt: '2026-08-10T03:30:00Z' },
    { viewCount: 150, likeCount: 9, dislikeCount: 0, commentCount: 2, takenAt: '2026-08-11T03:30:00Z' },
];

class FakeDrafts {
    async list() { return structuredClone([LIVE, EARLIER, DRAFTED, OLD]); }
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
    async listRegistrations() { return []; }
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
    async list() { return []; }
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
    let ruler: RulerService;
    let rail: RailActionsService;
    const t = en.manager;

    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;
    const tiles = () => [...el().querySelectorAll('app-index-tabs .it-tile')] as HTMLElement[];
    const cards = () => [...el().querySelectorAll('.post-card')] as HTMLElement[];
    const card = (title: string) => cards().find(c => c.textContent?.includes(title))!;
    const shelf = () => el().querySelector('app-shelf-panel.inspector') as HTMLElement;
    const rows = () => [...shelf().querySelectorAll('app-spec-row')] as HTMLElement[];
    const row = (label: string) => rows().find(r => r.querySelector('.label')?.textContent?.trim() === label);
    const rowValue = (label: string) => row(label)?.querySelector('.text')?.textContent?.trim();
    const sheet = () => el().querySelector('app-worktop.sheet') as HTMLElement;

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
        ruler = TestBed.inject(RulerService);
        rail = TestBed.inject(RailActionsService);
        TestBed.inject(LocaleService).set('en');
        // A blog lives at its owner's subdomain, and the component asks the server which one.
        TestBed.inject(AuthService).blogUrl.set('https://martycow.cedarclerk.app');
        fixture = TestBed.createComponent(PostsManagerComponent);
        await settle();
    });

    // ADR-167 clause 1 — the strip is IndexTabs, and the tally it carries is the badge rules'
    // own: nothing at zero, and the count when there is one.
    it('draws the sections as index tiles and badges the feedback tally on Posts', async () => {
        expect(tiles().map(x => x.querySelector('.it-label')?.textContent?.trim()))
            .toEqual([t.tabs.posts, t.tabs.stats, t.tabs.forms]);
        expect(tiles()[0].querySelector('.it-badge')).toBeNull();

        feedback.newComments.set(3);
        feedback.newReactions.set(4);
        await settle();
        expect(tiles()[0].querySelector('.it-badge')?.textContent?.trim()).toBe('7');
    });

    it('switches the body with the tile, and keeps the sheet out of the forms tab until one is picked', async () => {
        expect(page().tab()).toBe('posts');
        tiles()[2].click();
        await settle();

        expect(page().tab()).toBe('forms');
        expect(el().querySelector('app-shelf-panel.inspector')).toBeNull();
    });

    // ADR-167 clause 2 — every command here belongs to one tab, so the rail gets none of them and
    // the rule takes the screen-level counts instead.
    it('publishes counts to the rule and no primary action to the rail', () => {
        expect(rail.primary()).toBeNull();
        expect(ruler.label()).toBe(t.crumb);
        expect(ruler.right().map(r => r.text))
            .toEqual([t.rulerPosts(4), t.rulerPublished(2), t.rulerScheduled(1)]);

        fixture.destroy();
        expect(ruler.right()).toEqual([]);
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

        expect(page().inspectorScope()).toBe('selection');
        expect(rows().every(r => r.getAttribute('data-scope') === 'selection')).toBe(true);
        expect(row(t.inspector.posts)).toBeUndefined();
        expect(shelf().querySelector('app-stamp-badge')?.textContent?.trim()).toBe('LIVE');
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

    it('resolves submission questions and options in the current UI language', async () => {
        card('Notes').click();
        await settle();
        expect(page().regForm()?.intro).toBe('English intro');
        expect(page().regForm()?.questions[0]?.label).toBe('English question');
        expect(page().regForm()?.questions[0]?.options?.[0]?.label).toBe('Yes');
    });

    // ADR-163/ADR-169 — the one action here that opens something carries its address, and the
    // address names the picked post: the editor falls back to the newest draft when the query is
    // missing, so a link to the wrong id and a link to none look identical on screen.
    it('opens the picked post in the editor as a link, not a handler', async () => {
        card('Devlog 12').click();
        await settle();

        const open = [...sheet().querySelectorAll('.detail-actions .btn-ghost')]
            .find(x => x.textContent?.includes(t.openInEditor)) as HTMLAnchorElement;
        expect(open.tagName).toBe('A');
        expect(open.getAttribute('href')).toBe('/editor?draft=live');

        card('Notes').click();
        await settle();
        const other = [...sheet().querySelectorAll('.detail-actions .btn-ghost')]
            .find(x => x.textContent?.includes(t.openInEditor)) as HTMLAnchorElement;
        expect(other.getAttribute('href')).toBe('/editor?draft=drafted');
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
