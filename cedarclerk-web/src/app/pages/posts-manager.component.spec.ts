import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { PostsManagerComponent } from './posts-manager.component';
import { DraftMeta, DraftsService } from '../core/drafts.service';
import { blankFormEdit, FormPreset, FormPresetsService } from '../core/form-presets.service';
import { PostsService } from '../core/posts.service';
import { PublishEvent, PublishService } from '../core/publish.service';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AuthService } from '../core/auth.service';
import { WorkspaceContextService } from '../core/workspace-context.service';
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

// LIVE is on the blog, in Telegram (two sends) and on Bluesky, so X is its first unreached
// destination. Its updatedAt is later than every send: a time read from the draft would show.
const LIVE = draft('live', {
    title: 'Devlog 12', blogSlug: 'devlog-12', isBlogPublished: true,
    blogPublishedAt: '2026-08-10T09:00:00', languages: ['en'], updatedAt: '2026-08-20T09:00:00',
    lastTelegramUsername: 'testingandfun', lastTelegramMessageId: 42, projectId: 'p1',
});
const EARLIER = draft('earlier', {
    title: 'Devlog 11', blogSlug: 'devlog-11', isBlogPublished: true,
    blogPublishedAt: '2026-08-03T09:00:00', projectId: 'p2',
});
const DRAFTED = draft('drafted', { title: 'Notes', isPrivate: true, projectId: 'p1' });
const OLD = draft('old', { title: 'Retired', isArchived: true });
const PLANNED = draft('planned', { title: 'Next stream' });

const PRESET: FormPreset = {
    id: 'preset-1', name: 'Game experience', formJson: JSON.stringify(blankFormEdit('ru')),
    language: 'ru', createdAt: '2026-08-01T09:00:00',
};

const SNAPSHOTS = [
    { viewCount: 100, likeCount: 4, dislikeCount: 0, commentCount: 1, takenAt: '2026-08-09T03:30:00Z' },
    { viewCount: 130, likeCount: 6, dislikeCount: 0, commentCount: 2, takenAt: '2026-08-10T03:30:00Z' },
    { viewCount: 150, likeCount: 9, dislikeCount: 0, commentCount: 2, takenAt: '2026-08-11T03:30:00Z' },
];

const send = (over: Partial<PublishEvent>): PublishEvent => ({
    draftId: 'live', draftTitle: 'Devlog 12', network: 'telegram', targetName: 'Testing',
    publishedAt: '2026-08-12T08:00:00', publicUrl: 'https://t.me/testingandfun/42', partCount: 1,
    scheduled: false, ...over,
});

class FakeDrafts {
    async list() { return structuredClone([LIVE, EARLIER, DRAFTED, OLD, PLANNED]); }
    async listFolders() { return []; }
    async get(id: string) {
        return {
            id, articleTitle: '', cedarJson: '{}', formLanguages: ['ru'],
            registrationFormJson: JSON.stringify({ v: 2, languages: ['ru'], requireName: true, intro: {}, questions: [] }),
        } as never;
    }
}

class FakePosts {
    projects: (string | null | undefined)[] = [];
    async listScheduled(project?: string | null) {
        this.projects.push(project);
        return [{
            id: 's1', draftId: 'planned', status: 'Pending', language: 'ru', chatId: '1',
            targetName: 'Testing', scheduledAtUtc: '2026-08-20T18:00:00Z', network: 'telegram', error: null,
        }] as never;
    }
    async statHistory() { return { snapshots: structuredClone(SNAPSHOTS) }; }
}

class FakePublish {
    projects: (string | null | undefined)[] = [];
    async published() {
        return {
            posts: [{
                draftId: 'live', network: 'bluesky', language: 'en', targetId: 'b1',
                publicUrl: 'https://bsky.app/p/1', remoteId: '1', partCount: 1,
                finishedAt: '2026-08-10T10:00:00',
            }],
        } as never;
    }
    async events(project?: string | null) {
        this.projects.push(project);
        return {
            events: [
                send({}),
                send({ publishedAt: '2026-08-05T08:00:00', publicUrl: 'https://t.me/testingandfun/30' }),
            ],
        };
    }
}

class FakePresets {
    async list() { return [PRESET]; }
}

class FakeComments {
    newComments = signal(0);
    newReactions = signal(0);
    async refreshNewCount() {}
    async listAll() {
        return {
            reactions: { likes: 0, dislikes: 0, newLikes: 0, newDislikes: 0 }, reactionsByDraft: [],
            comments: [{ draftId: 'live', isNew: true }, { draftId: 'live', isNew: true }],
        } as never;
    }
}

describe('publishing manager', () => {
    let fixture: ComponentFixture<PostsManagerComponent>;
    const t = en.manager;

    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;
    const cards = () => [...el().querySelectorAll('.post-card')] as HTMLElement[];
    const card = (title: string) => cards().find(c => c.textContent?.includes(title))!;
    const sheet = () => el().querySelector('.sheet') as HTMLElement;
    const header = () => el().querySelector('app-page-header') as HTMLElement;
    const chips = () => [...el().querySelectorAll('.post-chips button')] as HTMLButtonElement[];
    const tabs = () => [...sheet().querySelectorAll('app-index-tabs [role="tab"]')] as HTMLButtonElement[];
    const text = (selector: string) => el().querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();

    async function settle(target: ComponentFixture<PostsManagerComponent> = fixture) {
        target.detectChanges();
        for (let i = 0; i < 8; i++) await Promise.resolve();
        target.detectChanges();
    }

    async function open(title: string, tab?: string) {
        card(title).click();
        await settle();
        if (tab) {
            page().setDetailTab(tab);
            await settle();
        }
    }

    function configure(queryParams: Record<string, string> = {}) {
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: DraftsService, useClass: FakeDrafts },
                { provide: PostsService, useClass: FakePosts },
                { provide: PublishService, useClass: FakePublish },
                { provide: FormPresetsService, useClass: FakePresets },
                { provide: CommentsService, useClass: FakeComments },
                ...(Object.keys(queryParams).length
                    ? [{ provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } } }]
                    : []),
            ],
        });
        TestBed.inject(LocaleService).set('en');
        // A blog lives at its owner's subdomain, and the component asks the server which one.
        TestBed.inject(AuthService).blogUrl.set('https://martycow.cedarclerk.app');
    }

    beforeEach(async () => {
        configure();
        fixture = TestBed.createComponent(PostsManagerComponent);
        await settle();
    });

    it('treats a post sent only to Telegram as live, like the Documents screen does', () => {
        const telegramOnly = draft('tg', { lastTelegramMessageId: 7, lastTelegramUsername: 'chan' });
        expect(page().publishState(telegramOnly)).toBe('live');
        expect(page().publishState(draft('never'))).toBe('draft');
        expect(page().publishState(PLANNED)).toBe('scheduled');
        expect(page().publishState(draft('old', { isArchived: true, lastTelegramMessageId: 7 }))).toBe('archived');
    });

    // ADR-317 §1 and §5 — the name, the counts, and "+ New post" as the header's one action.
    it('is the Publishing Manager, with the counts and New post in its header', () => {
        expect(header().querySelector('.page-title')?.textContent?.trim()).toBe('Publishing Manager');
        const meta = [...header().querySelectorAll('.page-meta > span:not(.sep)')].map(s => s.textContent?.trim());
        expect(meta).toEqual([t.rulerPosts(5), t.rulerPublished(2), t.rulerScheduled(1)]);

        const create = header().querySelector('.new-post a') as HTMLAnchorElement;
        expect(create.textContent).toContain(t.newPost);
        expect(create.getAttribute('href')).toBe('/drafts?new=1');
        expect(header().querySelectorAll('.page-actions a, .page-actions button').length).toBe(1);
    });

    // ADR-317 §2 and ADR-316 §3 — two panes, and no tab strip left to hold Stats or Forms.
    it('lays out the list and the selected post as two panes with no section tabs', () => {
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        const grid = el().querySelector('.mg-grid') as HTMLElement;
        expect(grid.classList.contains('split-workspace')).toBe(true);
        expect(grid.classList.contains('is-two')).toBe(true);
        expect(grid.children.length).toBe(2);
        expect([...grid.children].every(child => child.classList.contains('split-pane'))).toBe(true);
        expect(el().querySelector('.manager-tabs')).toBeNull();
        expect(el().querySelector('.inspector')).toBeNull();
        expect(el().querySelector('app-stats')).toBeNull();
        expect((page() as unknown as Record<string, unknown>)['setTab']).toBeUndefined();
    });

    it('filters the list with the All, Published, Scheduled and Drafts chips', async () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        expect(chips().map(c => c.textContent?.trim())).toEqual([t.filterAll, t.chipPublished, t.scheduled, t.chipDrafts]);
        expect(chips()[0].getAttribute('aria-pressed')).toBe('true');
        expect(el().querySelector('.post-filter-trigger')).toBeNull();

        chips()[1].click();
        await settle();
        expect(cards().map(c => c.querySelector('.post-card-title-text')?.textContent)).toEqual(['Devlog 12', 'Devlog 11']);
        expect(chips()[1].getAttribute('aria-pressed')).toBe('true');
        expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({
            queryParams: expect.objectContaining({ status: 'live' }),
        }));

        chips()[2].click();
        await settle();
        expect(page().visiblePosts().map(d => d.id)).toEqual(['planned']);

        chips()[3].click();
        await settle();
        expect(page().visiblePosts().map(d => d.id)).toEqual(['drafted']);

        chips()[0].click();
        await settle();
        expect(page().visiblePosts().length).toBe(5);
        expect(text('.post-result-count')).toBe(t.resultCount(5, 5));
    });

    it('searches and stably sorts only publishable, non-template posts', async () => {
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
        page().onPostSearch('devlog');
        expect(page().visiblePosts().map(d => d.id)).toEqual(['earlier', 'live']);
    });

    it('shows the active sort value and replaces a broken cover with the document type', async () => {
        page().drafts.set([
            { ...LIVE, viewCount: 40, reactionCount: 3, coverImagePath: 'missing.jpg' },
            { ...EARLIER, viewCount: 10, reactionCount: 2 },
        ]);
        page().setPostSortValue('activity:desc');
        await settle();

        expect(cards()[0].textContent).toContain(t.activityMetric(43));
        (cards()[0].querySelector('img') as HTMLImageElement).dispatchEvent(new Event('error'));
        await settle();
        expect(cards()[0].querySelector('img')).toBeNull();
        expect(cards()[0].querySelector('app-icon')).not.toBeNull();
    });

    // ADR-317 §3 — the selected post: its header, then Overview · Publishing · Engagement · Details.
    it('heads the selected post with its state and opens on Overview of four tabs', async () => {
        await open('Devlog 12');

        expect(card('Devlog 12').getAttribute('aria-current')).toBe('true');
        expect(sheet().querySelector('.post-head h2')?.textContent?.trim()).toBe('Devlog 12');
        expect(sheet().querySelector('.post-state')?.textContent?.trim()).toBe(t.liveChip);
        expect(text('.post-head .post-card-lang')).toBe('RU · EN');
        expect(text('.post-head-visibility')).toBe(t.inspector.public);
        expect(tabs().map(tab => tab.firstChild?.textContent?.trim()))
            .toEqual([t.detailTabs.overview, t.detailTabs.publishing, t.detailTabs.engagement, t.detailTabs.details]);
        expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
        expect([...sheet().querySelectorAll('.ov-card h3')].map(h => h.textContent?.trim())).toEqual([
            t.studio.destinations, t.studio.performance, t.studio.recentActivity, t.studio.postDetails,
        ]);
    });

    // The editor falls back to the newest draft when the query is missing, so the link names the post.
    it('opens the picked post in the editor through a link that names it', async () => {
        await open('Devlog 12');
        const link = sheet().querySelector('.open-in-editor a') as HTMLAnchorElement;
        expect(link.getAttribute('href')).toBe('/editor?draft=live');

        await open('Notes');
        expect((sheet().querySelector('.open-in-editor a') as HTMLAnchorElement).getAttribute('href')).toBe('/editor?draft=drafted');
        expect([...el().querySelectorAll('a')].filter(a => a.textContent?.includes(t.openInEditor)).length).toBe(1);
    });

    it('counts the destinations reached and names the first unreached one as the next step', async () => {
        await open('Devlog 12');

        expect(page().destinations(LIVE).map(row => [row.key, row.reached])).toEqual([
            ['blog', true], ['telegram', true], ['x', false], ['bluesky', true],
        ]);
        expect(text('.reached-count')).toBe(t.studio.reachedCount(3, 4));
        expect(text('.next-step strong')).toBe(t.studio.nextStep('X'));
        expect(text('.next-step p')).toBe(t.studio.nextStepBody(3, 'X'));

        (el().querySelector('.next-step app-button button') as HTMLButtonElement).click();
        await settle();
        expect(page().detailTab()).toBe('publishing');

        expect(page().nextStep(draft('fresh'))?.key).toBe('blog');
        expect(t.studio.nextStepBody(0, 'Blog')).toContain('not been published anywhere');
    });

    // ADR-317 §4 — the draft's UpdatedAt (20 Aug) and its LastTelegram* columns are not the source.
    it('takes Telegram times and links from the channel send records, not from the draft', async () => {
        await open('Devlog 12');

        const telegram = page().destinations(LIVE).find(row => row.key === 'telegram')!;
        expect(telegram.state).toBe(t.studio.sentOn('12 Aug 2026'));
        expect(telegram.url).toBe('https://t.me/testingandfun/42');

        const activity = page().activity(LIVE);
        expect(activity.map(item => [item.label, item.at])).toEqual([
            [t.studio.publishedTo('Telegram'), '2026-08-12T08:00:00'],
            [t.studio.publishedTo('Bluesky'), '2026-08-10T10:00:00'],
            [t.studio.publishedTo('Blog'), '2026-08-10T09:00:00'],
            [t.studio.publishedTo('Telegram'), '2026-08-05T08:00:00'],
            [t.studio.draftPrepared, '2026-08-01T09:00:00'],
        ]);
        expect(activity.some(item => item.at === LIVE.updatedAt)).toBe(false);
        expect(el().querySelector('.studio-activity')?.textContent).not.toContain('20 Aug');

        const legacy = draft('legacy', { lastTelegramMessageId: 7, lastTelegramUsername: 'chan' });
        const row = page().destinations(legacy).find(r => r.key === 'telegram')!;
        expect([row.reached, row.state, row.url]).toEqual([true, t.studio.published, null]);
    });

    it('lists every Telegram send and the other networks on the Publishing tab', async () => {
        await open('Devlog 12', 'publishing');

        const sends = [...sheet().querySelectorAll('.telegram-sends .link-row')];
        expect(sends.length).toBe(2);
        expect(sends.map(row => row.querySelector('a')?.getAttribute('href')))
            .toEqual(['https://t.me/testingandfun/42', 'https://t.me/testingandfun/30']);
        expect(sends[0].textContent).toContain('12 Aug 2026');
        expect([...sheet().querySelectorAll('a.insp-link')].map(a => a.getAttribute('href'))).toContain('https://bsky.app/p/1');
        expect(sheet().querySelector('.unpublish')).not.toBeNull();
    });

    it('says a post is not published rather than drawing an empty link', async () => {
        await open('Notes');

        expect(text('.reached-count')).toBe(t.studio.reachedCount(0, 4));
        expect(sheet().querySelectorAll('.dest-list a').length).toBe(0);
        expect(sheet().querySelectorAll('.dest-review').length).toBe(4);
        expect(sheet().querySelector('.ov-details .insp-none')?.textContent?.trim()).toBe(t.inspector.notPublished);

        page().setDetailTab('publishing');
        await settle();
        expect(sheet().querySelector('.telegram-sends')?.textContent).toContain(t.studio.noTelegramSends);
    });

    // ADR-317 Consequences — the tab states the gap instead of leaving an empty space for it.
    it('says on the Engagement tab that X and Bluesky per-post engagement is unavailable', async () => {
        await open('Devlog 12', 'engagement');

        expect(text('.social-unavailable')).toBe(t.studio.socialUnavailable);
        expect(t.studio.socialUnavailable).toContain('X and Bluesky');
        expect(t.studio.socialUnavailable).toContain('unavailable');
        expect(sheet().querySelector('app-comments')).not.toBeNull();
    });

    it('draws the nightly snapshots as one line per metric, and a leaf switches a line off', async () => {
        await open('Devlog 12', 'engagement');

        expect(page().growthSeries()!.map(s => `${s.name}:${s.slot}`))
            .toEqual([t.groups.views + ':1', t.groups.likes + ':2', t.groups.comments + ':3']);
        expect(page().growthSeries()![0].points).toEqual([100, 130, 150]);
        expect(page().overviewSeries()!.map(s => s.name)).toEqual([t.groups.views]);

        page().toggleGrowthMetric('likeCount');
        expect(page().growthSeries()!.map(s => s.name)).toEqual([t.groups.views, t.groups.comments]);
    });

    it('badges the Engagement tab with the feedback that arrived for the selected post', async () => {
        await open('Devlog 12');
        expect(tabs()[2].querySelector('.it-badge, [class*="badge"]')?.textContent?.trim()).toBe('2');
        await open('Notes');
        expect(tabs()[2].querySelector('.it-badge, [class*="badge"]')).toBeNull();
    });

    // ADR-316 §2 — the form is picked here and authored on /forms, which every link points at.
    it('keeps a private post’s form on Details and links its authoring and submissions to /forms', async () => {
        await open('Notes', 'details');

        const ref = sheet().querySelector('app-form-ref') as HTMLElement;
        expect(ref).not.toBeNull();
        expect((ref.querySelector('.form-ref-manage a') as HTMLAnchorElement).getAttribute('href')).toBe('/forms');
        expect((sheet().querySelector('.view-submissions a') as HTMLAnchorElement).getAttribute('href'))
            .toBe('/forms?view=submissions&post=drafted');
        expect(sheet().querySelector('.registration-item')).toBeNull();
        expect(sheet().querySelector('.form-editor')).toBeNull();

        await open('Devlog 12', 'details');
        expect(sheet().querySelector('app-form-ref')).toBeNull();
        expect(sheet().querySelector('#post-title')).not.toBeNull();
    });

    it('keeps privacy, archive and delete behind More', async () => {
        await open('Devlog 12');
        expect(sheet().querySelector('.more-menu')).toBeNull();

        (sheet().querySelector('.more-trigger button') as HTMLButtonElement).click();
        fixture.detectChanges();
        const items = [...el().querySelectorAll('.more-menu app-button')].map(b => b.textContent?.trim());
        expect(items).toEqual([t.makePrivate, t.archive, en.common.delete]);
    });

    it('offers a useful action when no post is selected', async () => {
        const action = sheet().querySelector('app-empty-state app-button button') as HTMLButtonElement;
        expect(action.textContent?.trim()).toBe(t.selectFirstPost);
        action.click();
        await settle();
        expect(page().selectedId()).toBe('live');
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

        expect(el().querySelectorAll('app-empty-state').length).toBe(1);
        expect([...el().querySelectorAll('app-button')]
            .filter(x => x.textContent?.trim() === t.writeFirst).length).toBe(1);
    });

    // ADR-317 Consequences — the shell is told the surface, and draws nothing about the post.
    it('does not hand the selected post to the shell inspector', async () => {
        const workspace = TestBed.inject(WorkspaceContextService);
        const set = vi.spyOn(workspace, 'set');
        await open('Devlog 12');

        expect(set.mock.calls.every(([context]) => !('open' in context) && !('properties' in context))).toBe(true);
    });
});

describe('publishing manager inside a project', () => {
    it('shows that project’s posts only and asks the server for its schedule and sends', async () => {
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: DraftsService, useClass: FakeDrafts },
                { provide: PostsService, useClass: FakePosts },
                { provide: PublishService, useClass: FakePublish },
                { provide: FormPresetsService, useClass: FakePresets },
                { provide: CommentsService, useClass: FakeComments },
                { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ project: 'p1', tab: 'stats', draft: 'drafted' }) } } },
            ],
        });
        TestBed.inject(LocaleService).set('en');
        vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const fixture = TestBed.createComponent(PostsManagerComponent);
        fixture.detectChanges();
        for (let i = 0; i < 8; i++) await Promise.resolve();
        fixture.detectChanges();

        const page = fixture.componentInstance;
        expect(page.postPool().map(d => d.id)).toEqual(['live', 'drafted']);
        expect(page.selectedId()).toBe('drafted');
        expect((TestBed.inject(PostsService) as unknown as FakePosts).projects).toEqual(['p1']);
        expect((TestBed.inject(PublishService) as unknown as FakePublish).projects).toEqual(['p1']);
        const create = (fixture.nativeElement as HTMLElement).querySelector('.new-post a') as HTMLAnchorElement;
        expect(create.getAttribute('href')).toBe('/drafts?new=1&project=p1');
        // The retired ?tab= is not read: the page is the post list whatever it says.
        expect((fixture.nativeElement as HTMLElement).querySelector('.mg-grid')).not.toBeNull();
    });
});
