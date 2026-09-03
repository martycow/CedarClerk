import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { StatsComponent } from './stats.component';
import { ChannelsService } from '../core/channels.service';
import { GrowthChartComponent } from '../bench/worktop/growth-chart.component';
import { en } from '../core/i18n/en';

// The component's own stylesheet, read back out of the document. Two claims this screen makes are
// claims about CSS — paper's floor holds every size on it, and no colour is written as a literal —
// and the only way to hold the port to them is to read what actually shipped. The length assertion
// is the control: without it a renamed class would make every rule below pass over an empty string.
function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n').replace(/\/\*[\s\S]*?\*\//g, ' ');
}

const day = (d: string) => `2026-08-${d}T12:00:00Z`;

// ADR-205 — the varying number sits on likeCount, which is the one metric both a blog and a
// channel track: views are the blog's alone, since Telegram reports none to a bot.
const blogSnapshot = (d: string, n: number) =>
    ({ takenAt: day(d), viewCount: n, likeCount: n, commentCount: 1 });

const channelSnapshot = (d: string, n: number) =>
    ({ takenAt: day(d), memberCount: 400 + n, viewCount: 0, likeCount: n, commentCount: 0 });

// The blog has read since the 8th; the channel only since the 9th, and it missed the 10th. Both
// facts are load-bearing: the first is what the window's start rule exists for, the second what
// carry-forward answers.
const BLOG = {
    currentViews: 130, deltaWeekViews: 30,
    currentLikes: 130, deltaWeekLikes: 30,
    currentComments: 1, deltaWeekComments: 0,
    snapshots: [blogSnapshot('08', 100), blogSnapshot('09', 110), blogSnapshot('10', 120), blogSnapshot('11', 130)],
    countries: [], languages: [],
};

const DEVLOG = {
    current: 440, deltaWeek: 30,
    currentViews: 0, deltaWeekViews: 0,
    currentLikes: 40, deltaWeekLikes: 30,
    currentComments: 0, deltaWeekComments: 0,
    snapshots: [channelSnapshot('09', 10), channelSnapshot('11', 40)],
};

// Connected, never snapshotted — the honest case for the kit's `dried` leaf.
const QUIET = {
    current: null, deltaWeek: null,
    currentViews: null, deltaWeekViews: null,
    currentLikes: null, deltaWeekLikes: null,
    currentComments: null, deltaWeekComments: null,
    snapshots: [],
};

class ApiStub {
    channels = [
        { id: 'c1', title: 'Devlog', telegramChatId: 1, username: null },
        { id: 'c2', title: 'Quiet', telegramChatId: 2, username: null },
    ];
    blogCalls: number[] = [];
    channelCalls: string[] = [];

    list() { return Promise.resolve(this.channels as never); }

    getBlogStats(days: number) {
        this.blogCalls.push(days!);
        return Promise.resolve(BLOG as never);
    }

    getStats(id: string, days: number) {
        this.channelCalls.push(`${id}:${days}`);
        return Promise.resolve((id === 'c1' ? DEVLOG : QUIET) as never);
    }

    // Wave 2 — the streak card and the invite-links shelf ask these on init, best-effort.
    publishingStats() {
        return Promise.resolve({ currentStreakWeeks: 2, longestStreakWeeks: 5, weeks: [] } as never);
    }

    listInviteLinks() {
        return Promise.resolve({ links: [], organic: { joins: 0, leaves: 0 } } as never);
    }
}

describe('stats screen (Posts Manager tab)', () => {
    let fixture: ComponentFixture<StatsComponent>;
    let api: ApiStub;
    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;

    // The screen's state lands through an awaited fan-out, so a render pass has to come after the
    // microtask queue drains — not merely after the fixture calls itself stable.
    async function settle(target: ComponentFixture<StatsComponent> = fixture) {
        target.detectChanges();
        await new Promise(resolve => setTimeout(resolve, 0));
        await target.whenStable();
        target.detectChanges();
    }

    beforeEach(async () => {
        api = new ApiStub();
        TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: ChannelsService, useValue: api }] });
        fixture = TestBed.createComponent(StatsComponent);
        await settle();
        // Every test below that is about the axis, the ink or the wash needs a metric both kinds of
        // source track; the screen opens on the blog's own headline number instead (ADR-205).
        fixture.componentInstance.setMetric('likeCount');
        await settle();
    });

    it('is paper, and says so where the density lint and the touch carve-out can read it', () => {
        expect(el().getAttribute('data-surface')).toBe('paper');
    });

    it('paints a source by its own slot, so switching one off cannot repaint the others', async () => {
        const before = page().readouts().map(r => `${r.name}:${r.color}`);
        expect(before).toEqual(['Blog:var(--series-2)', 'Devlog:var(--series-1)']);

        page().toggle('blog');
        await settle();

        expect(page().readouts().map(r => `${r.name}:${r.color}`)).toEqual(['Devlog:var(--series-1)']);
    });

    it('dries a connected source that has never been snapshotted, and never draws it', () => {
        const quiet = page().leaves().find(l => l.name === 'Quiet')!;
        expect(quiet.state).toBe('dried');
        expect(quiet.note).toBe('no data yet');
        expect(page().series().some(s => s.name === 'Quiet')).toBe(false);
    });

    it('dries a channel under views, because Telegram reports none to a bot', async () => {
        page().setMetric('viewCount');
        await settle();

        const devlog = page().leaves().find(l => l.name === 'Devlog')!;
        expect(devlog.state).toBe('dried');
        expect(devlog.note).toBe('not tracked');
        expect(page().series().map(s => s.name)).toEqual(['Blog']);
        expect(page().metricNote()).toBe(en.stats.sources.notTrackedWhy);
    });

    it('dries a source that does not track the picked metric instead of blanking a card', async () => {
        page().setMetric('memberCount');
        await settle();

        const blog = page().leaves().find(l => l.name === 'Blog')!;
        expect(blog.state).toBe('dried');
        expect(blog.note).toBe('not tracked');
        expect(page().series().map(s => s.name)).toEqual(['Devlog']);
    });

    it('a dried leaf carries no series ink — nothing is drawn in that colour while it is dried', async () => {
        page().setMetric('memberCount');
        await settle();

        expect(page().leaves().find(l => l.name === 'Blog')!.swatch).toBe('var(--t3)');
        expect(page().leaves().find(l => l.name === 'Devlog')!.swatch).toBe('var(--series-1)');
    });

    it('starts the axis where every drawn line has a reading, and draws nothing before it', () => {
        expect(page().axis().days).toEqual(['2026-08-09', '2026-08-10', '2026-08-11']);
        expect(page().series().find(s => s.name === 'Blog')!.points).toEqual([110, 120, 130]);
    });

    it('carries the last reading through a day with no snapshot rather than dropping to zero', () => {
        expect(page().series().find(s => s.name === 'Devlog')!.points).toEqual([10, 10, 40]);
    });

    it('states the window rather than leaving it to the axis, because it moves with the selection', async () => {
        expect(page().windowLabel()).toBe('3 points · 08/09 — 08/11');

        page().toggle('c1');
        await settle();

        expect(page().axis().days.length).toBe(4);
        expect(page().windowLabel()).toBe('4 points · 08/08 — 08/11');
    });

    it('tells the chart the tail is closed: a running total is complete the moment it is read', () => {
        const chart = fixture.debugElement.query(By.directive(GrowthChartComponent));
        expect((chart.componentInstance as GrowthChartComponent).openTail()).toBe(false);
        expect((chart.componentInstance as GrowthChartComponent).plotted()).toBe(3);
    });

    it('washes the blog and only the blog, however many lines are drawn', async () => {
        expect(page().series().map(s => [s.name, s.wash])).toEqual([['Blog', true], ['Devlog', false]]);

        page().toggle('blog');
        await settle();

        expect(page().series().map(s => s.wash)).toEqual([false]);
    });

    it('rests the readout slip on the latest day', () => {
        const chart = fixture.debugElement.query(By.directive(GrowthChartComponent));
        expect((chart.componentInstance as GrowthChartComponent).markerIndex()).toBe(2);
    });

    it('gives the table the same numbers the chart is drawn from', async () => {
        page().setView('table');
        await settle();

        const rows = [...el().querySelectorAll('.series-table tbody tr')]
            .map(tr => [...tr.querySelectorAll('td')].map(td => td.textContent!.trim()));
        expect(rows).toEqual([['110', '10'], ['120', '10'], ['130', '40']]);

        const drawn = page().series().map(s => s.points);
        expect(rows.map(r => r.map(Number))).toEqual(drawn[0].map((_, i) => drawn.map(p => p[i])));
    });

    it('sorts the complete series table and exposes aria-sort on its active header', async () => {
        page().setView('table');
        page().sortTable('c1');
        page().sortTable('c1');
        await settle();

        expect(page().tableRows().map(row => row.day))
            .toEqual(['2026-08-11', '2026-08-09', '2026-08-10']);
        const active = el().querySelector('.series-table th[aria-sort="descending"]');
        expect(active?.textContent).toContain('Devlog');
    });

    it('returns table sorting to the day column when its source leaves the visible series', async () => {
        page().setView('table');
        page().sortTable('c1');
        page().toggle('c1');
        await settle();

        expect(page().tableSortKey()).toBe('period');
        const dayHeader = el().querySelector('.series-table th[aria-sort="ascending"]')!;
        expect(dayHeader.textContent).toContain(en.stats.chart.period);
        expect(dayHeader.querySelector('button')?.getAttribute('aria-label'))
            .toBe(`${en.common.sortBy}: ${en.stats.chart.period}, ${en.common.ascending}`);
    });

    it('filters and sorts invite links before rendering a filtered empty state', async () => {
        page().inviteLinks.set([
            { id: 'b', name: 'Launch', inviteLink: 'https://t.me/+b', createdAt: day('10'), revokedAt: null, joins: 3, leaves: 1, net: 2 },
            { id: 'a', name: 'Archive', inviteLink: 'https://t.me/+a', createdAt: day('09'), revokedAt: day('11'), joins: 8, leaves: 2, net: 6 },
        ]);
        page().sortInviteLinks('net');
        await settle();

        expect(page().visibleInviteLinks().map(link => link.name)).toEqual(['Archive', 'Launch']);
        page().inviteState.set('active');
        page().inviteQuery.set('missing');
        await settle();

        expect(page().visibleInviteLinks()).toEqual([]);
        expect(el().querySelector('.invite-shelf app-empty-state')?.textContent).toContain('No links match');
    });

    it('keeps organic invite totals in a summary footer outside the sorted rows', async () => {
        page().inviteLinks.set([
            { id: 'named', name: 'Launch', inviteLink: 'https://t.me/+named', createdAt: day('10'),
                revokedAt: null, joins: 3, leaves: 1, net: 2 },
        ]);
        page().inviteOrganic.set({ joins: 7, leaves: 2 });
        await settle();

        expect(el().querySelector('.invite-table tbody')?.textContent).not.toContain(en.stats.inviteLinks.organic);
        expect(el().querySelector('.invite-table tfoot')?.textContent).toContain(en.stats.inviteLinks.organic);
    });

    it('writes statistics and invite-link criteria into the manager route', async () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        page().setView('table');
        page().sortTable('c1');
        page().setInviteQuery('launch');
        page().setInviteState('revoked');
        page().sortInviteLinks('net');
        page().toggle('blog');
        page().toggle('c2');
        await settle();

        expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({
            replaceUrl: true,
            queryParamsHandling: 'merge',
            queryParams: expect.objectContaining({
                statsMetric: 'likeCount',
                statsView: 'table',
                statsSources: 'c1',
                statsSort: 'c1',
                inviteQ: 'launch',
                inviteState: 'revoked',
                inviteSort: 'net',
                inviteDir: 'desc',
            }),
        }));
    });

    it('never offers a readout that is a sum of sources', () => {
        expect(page().readouts().map(r => r.value)).toEqual([130, 40]);
        expect(page().readouts().map(r => r.delta)).toEqual([30, 30]);
        expect(el().querySelectorAll('.ro-value').length).toBe(2);
    });

    it('says the sources are off rather than drawing an empty chart', async () => {
        page().toggle('blog');
        page().toggle('c1');
        page().toggle('c2');
        await settle();

        expect(page().anySelected()).toBe(false);
        expect(fixture.debugElement.query(By.directive(GrowthChartComponent))).toBeNull();
        expect(el().querySelector('.stats-empty')!.textContent).toContain('switched off');

        const showAll = [...el().querySelectorAll<HTMLButtonElement>('.stats-empty button')]
            .find(button => button.textContent?.includes(en.stats.sources.showAll))!;
        showAll.click();
        await settle();

        expect(page().anySelected()).toBe(true);
        expect(page().selected()).toEqual(new Set(['blog', 'c1', 'c2']));
    });

    it('distinguishes "all off" from filters that hide drawable sources', async () => {
        page().toggle('blog');
        page().toggle('c1');
        await settle();

        expect(page().anySelected()).toBe(true);
        expect(el().querySelector('.stats-empty')!.textContent).toContain(en.stats.sources.filtered);
        expect(el().querySelector('.stats-empty')!.textContent).toContain(en.stats.sources.showAll);
    });

    it('keeps zero-snapshot sources selected and offers the documents route', async () => {
        api.getBlogStats = vi.fn().mockResolvedValue({ ...BLOG, snapshots: [] } as never);
        api.getStats = vi.fn().mockResolvedValue(QUIET as never);
        const empty = TestBed.createComponent(StatsComponent);
        await settle(empty);

        expect(empty.componentInstance.anySelected()).toBe(true);
        expect(empty.componentInstance.drawable()).toEqual([]);
        const state = empty.nativeElement.querySelector('.stats-empty') as HTMLElement;
        expect(state.textContent).toContain(en.stats.sources.nothingToDraw);
        expect(state.textContent).toContain(en.stats.sources.nothingToDrawAction);
        expect(state.textContent).not.toContain(en.stats.sources.showAll);
    });

    it('offers to restore a hidden source that can draw the current metric', async () => {
        page().toggle('c1');
        page().setMetric('memberCount');
        await settle();

        expect(page().anySelected()).toBe(true);
        expect(page().drawable()).toEqual([]);
        const state = el().querySelector('.stats-empty') as HTMLElement;
        expect(state.textContent).toContain(en.stats.sources.filtered);
        const showAll = [...state.querySelectorAll<HTMLButtonElement>('button')]
            .find(button => button.textContent?.includes(en.stats.sources.showAll))!;
        showAll.click();
        await settle();

        expect(page().drawable().map(source => source.id)).toContain('c1');
    });

    it('offers Subscribers only when a source can answer it', async () => {
        expect(page().metricTabs().map(t => t.id))
            .toEqual(['memberCount', 'viewCount', 'likeCount', 'commentCount']);

        api.channels = [];
        const blogOnly = TestBed.createComponent(StatsComponent);
        await settle(blogOnly);

        expect(blogOnly.componentInstance.metricTabs().map(t => t.id))
            .toEqual(['viewCount', 'likeCount', 'commentCount']);
    });

    it('fans out once per source per range, and a filter costs no request at all', async () => {
        expect(api.blogCalls).toEqual([90]);
        expect(api.channelCalls).toEqual(['c1:90', 'c2:90']);

        page().toggle('c1');
        page().setMetric('likeCount');
        page().setView('table');
        await settle();

        expect(api.blogCalls).toEqual([90]);
        expect(api.channelCalls).toEqual(['c1:90', 'c2:90']);
    });

    it('fetches the blog even when its line is off — the audience shelf is blog data', async () => {
        page().toggle('blog');
        await page().onRangeCommit(30);
        await settle();

        expect(api.blogCalls).toEqual([90, 30]);
        expect(page().series().some(s => s.name === 'Blog')).toBe(false);
    });

    it('uses a discrete period picker and names the display timezone', () => {
        const options = [...el().querySelectorAll('.range-picker option')] as HTMLOptionElement[];
        expect(options.map(option => Number(option.value))).toEqual([7, 14, 30, 60, 90, 180]);
        expect(el().querySelector('input[type="range"]')).toBeNull();
        expect(page().updatedAt()).toMatch(/^\d{2}:\d{2} \S+$/);
    });

    it('names the source the audience card is answering about', () => {
        const audience = el().querySelector('.audience-shelf')!;
        expect(audience.querySelector('.card-head .label')!.textContent!.trim()).toBe('Audience');
        expect(audience.querySelector('.card-head .card-count')!.textContent!.trim()).toBe('Blog');
    });

    it('holds every size on the sheet to paper\'s floor, and writes no colour as a literal', () => {
        const css = sheetFor('.range-picker');

        const sizes = [...css.matchAll(/font-size\s*:\s*([^;}]+)/g)].map(m => m[1].trim());
        expect(sizes.length).toBeGreaterThan(0);
        for (const size of sizes) {
            expect(size, `${size} is not a token from the paper-legal set`)
                .toMatch(/^var\(--(fs-ui|fs-body|fs-read|text-readout)\)$/);
        }

        expect(css).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
        expect(css).not.toMatch(/\brgba?\(/);
    });
});

describe('stats manager route restoration', () => {
    it('restores the full working view before the collection is shown', async () => {
        const api = new ApiStub();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: ChannelsService, useValue: api },
                {
                    provide: ActivatedRoute,
                    useValue: {
                        snapshot: {
                            queryParamMap: convertToParamMap({
                                statsMetric: 'likeCount',
                                statsView: 'table',
                                statsDays: '30',
                                statsSources: 'c1',
                                statsSort: 'c1',
                                statsDir: 'desc',
                                inviteChannel: 'c2',
                                inviteQ: 'launch',
                                inviteState: 'revoked',
                                inviteSort: 'net',
                                inviteDir: 'asc',
                            }),
                        },
                    },
                },
            ],
        });
        const restored = TestBed.createComponent(StatsComponent);
        await restored.whenStable();
        restored.detectChanges();
        await new Promise(resolve => setTimeout(resolve, 0));
        await restored.whenStable();
        restored.detectChanges();

        const page = restored.componentInstance;
        expect(page.metric()).toBe('likeCount');
        expect(page.view()).toBe('table');
        expect(page.rangeDays()).toBe(30);
        expect(page.selected()).toEqual(new Set(['c1']));
        expect(page.tableSortKey()).toBe('c1');
        expect(page.tableSortDirection()).toBe('desc');
        expect(page.inviteChannelId()).toBe('c2');
        expect(page.inviteQuery()).toBe('launch');
        expect(page.inviteState()).toBe('revoked');
        expect(page.inviteSortKey()).toBe('net');
        expect(page.inviteSortDirection()).toBe('asc');
    });
});
