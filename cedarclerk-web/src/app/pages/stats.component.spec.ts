import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { StatsComponent } from './stats.component';
import { ChannelsService, StatSourceInfo, StatSourceSeries, StatsSeries } from '../core/channels.service';
import { GrowthChartComponent } from '../bench/worktop/growth-chart.component';
import { en } from '@localization/en';

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

const BLOG_ID = 'blog';
const DEVLOG_ID = 'channel:c1';
const QUIET_ID = 'channel:c2';
const BSKY_ID = 'target:t1';

// ADR-205 — the varying number sits on likeCount, which is the one metric both a blog and a
// channel track: views are the blog's alone, since Telegram reports none to a bot.
const AVAILABLE: StatSourceInfo[] = [
    { id: BLOG_ID, kind: 'blog', network: 'blog', name: 'Blog', tracked: ['viewCount', 'likeCount', 'commentCount'], firstDay: '2026-08-08' },
    { id: DEVLOG_ID, kind: 'channel', network: 'telegram', name: 'Devlog', tracked: ['memberCount', 'likeCount', 'commentCount'], firstDay: '2026-08-09' },
    // Connected, never snapshotted — the honest case for the kit's `dried` leaf.
    { id: QUIET_ID, kind: 'channel', network: 'telegram', name: 'Quiet', tracked: ['memberCount', 'likeCount', 'commentCount'], firstDay: null },
];

const BSKY: StatSourceInfo = { id: BSKY_ID, kind: 'target', network: 'bluesky', name: 'studio.bsky.social', tracked: ['memberCount', 'likeCount', 'commentCount'], firstDay: '2026-08-10' };

// The server's aligned answer for blog + Devlog: the window starts at the channel's first day.
const ALIGNED_DAYS = ['2026-08-09', '2026-08-10', '2026-08-11'];
const BLOG_SERIES: StatSourceSeries = {
    id: BLOG_ID,
    values: { memberCount: null, viewCount: [110, 120, 130], likeCount: [110, 120, 130], commentCount: [1, 1, 1] },
    current: { viewCount: 130, likeCount: 130, commentCount: 1 },
    delta: { viewCount: 20, likeCount: 20, commentCount: 0 },
    publishDays: ['2026-08-10'],
};
const DEVLOG_SERIES: StatSourceSeries = {
    id: DEVLOG_ID,
    values: { memberCount: [410, 410, 440], viewCount: null, likeCount: [10, 10, 40], commentCount: [0, 0, 0] },
    current: { memberCount: 440, likeCount: 40, commentCount: 0 },
    delta: { memberCount: 30, likeCount: 30, commentCount: 0 },
    publishDays: ['2026-08-11'],
};

// With the channel off the server widens the window back to the blog's own first day — a
// different `days` and a different first point, so the tests can tell a redraw from a re-read.
const BLOG_ALONE_DAYS = ['2026-08-08', '2026-08-09', '2026-08-10', '2026-08-11'];
const BLOG_ALONE_SERIES: StatSourceSeries = {
    ...BLOG_SERIES,
    values: { memberCount: null, viewCount: [100, 110, 120, 130], likeCount: [100, 110, 120, 130], commentCount: [1, 1, 1, 1] },
    delta: { viewCount: 30, likeCount: 30, commentCount: 0 },
};

const BSKY_SERIES: StatSourceSeries = {
    id: BSKY_ID,
    values: { memberCount: [12, 15], viewCount: null, likeCount: [3, 5], commentCount: [0, 1] },
    current: { memberCount: 15, likeCount: 5, commentCount: 1 },
    delta: { memberCount: 3, likeCount: 2, commentCount: 1 },
    publishDays: [],
};

const AUDIENCE = { countries: [{ code: 'US', views: 40 }], languages: [{ code: 'en', views: 38 }] };

class ApiStub {
    channels = [
        { id: 'c1', title: 'Devlog', telegramChatId: 1, username: null },
        { id: 'c2', title: 'Quiet', telegramChatId: 2, username: null },
    ];
    available = AVAILABLE;
    calls: { days: number; sources: string[] }[] = [];
    projects: (string | null | undefined)[] = [];
    fail = false;

    list() { return Promise.resolve(this.channels as never); }

    /** Answers the way the server does: only selected sources with readings get a series, over
     *  the window their first days allow. */
    series(days: number, sources: readonly string[], project?: string | null): Promise<StatsSeries> {
        this.calls.push({ days, sources: [...sources] });
        this.projects.push(project);
        if (this.fail) return Promise.reject(new Error('down'));
        const wantsDevlog = sources.includes(DEVLOG_ID);
        const wantsBsky = sources.includes(BSKY_ID) && this.available.includes(BSKY);
        const series: StatSourceSeries[] = [];
        let window = wantsDevlog ? ALIGNED_DAYS : BLOG_ALONE_DAYS;
        if (wantsBsky) window = window.slice(-2);
        if (sources.includes(BLOG_ID)) {
            const blog = wantsDevlog ? BLOG_SERIES : BLOG_ALONE_SERIES;
            series.push(wantsBsky ? { ...blog, values: { ...blog.values, viewCount: [120, 130], likeCount: [120, 130], commentCount: [1, 1] } } : blog);
        }
        if (wantsDevlog) series.push(wantsBsky ? { ...DEVLOG_SERIES, values: { ...DEVLOG_SERIES.values, memberCount: [410, 440], likeCount: [10, 40], commentCount: [0, 0] } } : DEVLOG_SERIES);
        if (wantsBsky) series.push(BSKY_SERIES);
        return Promise.resolve({
            zone: 'America/Los_Angeles',
            requestedDays: days,
            days: series.length ? window : [],
            available: this.available,
            series,
            audience: AUDIENCE,
        });
    }

    seriesCsvUrl(days: number, sources: readonly string[], project?: string | null) {
        return `/api/stats/series.csv?days=${days}&sources=${sources.join(',')}${project ? `&project=${project}` : ''}`;
    }

    // The streak card and the invite-links shelf ask these on init, best-effort.
    publishingStats(project?: string | null) {
        this.projects.push(project);
        return Promise.resolve({ currentStreakWeeks: 2, longestStreakWeeks: 5, weeks: [] } as never);
    }

    listInviteLinks() {
        return Promise.resolve({ links: [], organic: { joins: 0, leaves: 0 } } as never);
    }

    flow: { day: string; inviteLinkId: string | null; joins: number; leaves: number }[] = [];
    memberFlow() {
        return Promise.resolve(this.flow as never);
    }
}

/** A UTC calendar day `n` days before today, in the server's offset-less shape. */
const utcDay = (n: number) => new Date(Date.UTC(
    new Date().getUTCFullYear(), new Date().getUTCMonth(), new Date().getUTCDate() - n)).toISOString().slice(0, 10) + 'T00:00:00';

describe('metrics page', () => {
    let fixture: ComponentFixture<StatsComponent>;
    let api: ApiStub;
    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;

    // The screen's state lands through awaited requests, so a render pass has to come after the
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

        page().toggle(BLOG_ID);
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

    // ADR-279 — the window, the carry-forward and the delta are the server's; the screen draws
    // exactly the days and points the response carries and computes none of them.
    it('draws the aligned window the response carries, point for point', () => {
        expect(page().days()).toEqual(ALIGNED_DAYS);
        expect(page().series().find(s => s.name === 'Blog')!.points).toEqual([110, 120, 130]);
        expect(page().series().find(s => s.name === 'Devlog')!.points).toEqual([10, 10, 40]);
    });

    it('redraws from the new answer when the selection changes the window', async () => {
        expect(page().windowLabel()).toBe('3 points · 08/09 — 08/11');

        page().toggle(DEVLOG_ID);
        await settle();

        expect(page().days()).toEqual(BLOG_ALONE_DAYS);
        expect(page().series().find(s => s.name === 'Blog')!.points).toEqual([100, 110, 120, 130]);
        expect(page().windowLabel()).toBe('4 points · 08/08 — 08/11');
    });

    it('shows the response\'s own current and delta on the tile, so a card and the chart agree', async () => {
        expect(page().readouts().map(r => [r.value, r.delta])).toEqual([[130, 20], [40, 30]]);
        expect(el().querySelector('.ro-delta')!.textContent).toContain(`+20 ${en.stats.readouts.deltaWindow}`);

        page().toggle(DEVLOG_ID);
        await settle();

        expect(page().readouts().map(r => [r.value, r.delta])).toEqual([[130, 30]]);
    });

    it('marks the chart with the publish days the response carries', () => {
        expect(page().publishMarkers()).toEqual([1, 2]);
    });

    it('tells the chart the tail is closed: a running total is complete the moment it is read', () => {
        const chart = fixture.debugElement.query(By.directive(GrowthChartComponent));
        expect((chart.componentInstance as GrowthChartComponent).openTail()).toBe(false);
        expect((chart.componentInstance as GrowthChartComponent).plotted()).toBe(3);
    });

    it('washes the blog and only the blog, however many lines are drawn', async () => {
        expect(page().series().map(s => [s.name, s.wash])).toEqual([['Blog', true], ['Devlog', false]]);

        page().toggle(BLOG_ID);
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
        page().sortTable(DEVLOG_ID);
        page().sortTable(DEVLOG_ID);
        await settle();

        expect(page().tableRows().map(row => row.day))
            .toEqual(['2026-08-11', '2026-08-09', '2026-08-10']);
        const active = el().querySelector('.series-table th[aria-sort="descending"]');
        expect(active?.textContent).toContain('Devlog');
    });

    it('returns table sorting to the day column when its source leaves the visible series', async () => {
        page().setView('table');
        page().sortTable(DEVLOG_ID);
        page().toggle(DEVLOG_ID);
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

    // T-327 — the daily series is summed across links, every day of the window is drawn, and the
    // totals are the figure's name as well as a sentence anyone can read.
    it('draws thirty days of member flow, links summed per day and quiet days at zero', async () => {
        api.flow = [
            { day: utcDay(3), inviteLinkId: 'a', joins: 2, leaves: 0 },
            { day: utcDay(3), inviteLinkId: null, joins: 1, leaves: 1 },
            { day: utcDay(0), inviteLinkId: 'b', joins: 4, leaves: 0 },
        ];
        page().pickInviteChannel('c1');
        await settle();
        const columns = el().querySelectorAll('.flow-bars .flow-day');
        expect(columns.length).toBe(30);
        expect(page().flowDays()![26]).toMatchObject({ day: utcDay(3).slice(0, 10), joins: 3, leaves: 1 });
        expect(page().flowDays()![10]).toMatchObject({ joins: 0, leaves: 0 });
        expect(el().querySelector('.flow-bars')?.getAttribute('aria-label')).toBe(en.stats.inviteLinks.flowSummary(30, 7, 1));
        expect(el().querySelector('.flow-summary')?.textContent).toContain(en.stats.inviteLinks.flowSummary(30, 7, 1));
        expect(el().querySelectorAll('.flow table tbody tr').length).toBe(30);
        expect((columns[29].querySelector('.flow-bar.joins') as HTMLElement).style.height).toBe('100%');
    });

    it('says the window was quiet instead of drawing an empty figure', async () => {
        api.flow = [];
        page().pickInviteChannel('c1');
        await settle();
        expect(el().querySelector('.flow-bars')).toBeNull();
        expect(el().querySelector('.flow-summary')?.textContent).toContain(en.stats.inviteLinks.flowEmpty(30));
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
        page().sortTable(DEVLOG_ID);
        page().setInviteQuery('launch');
        page().setInviteState('revoked');
        page().sortInviteLinks('net');
        page().toggle(BLOG_ID);
        page().toggle(QUIET_ID);
        await settle();

        expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({
            replaceUrl: true,
            queryParamsHandling: 'merge',
            queryParams: expect.objectContaining({
                statsMetric: 'likeCount',
                statsView: 'table',
                statsSources: DEVLOG_ID,
                statsSort: DEVLOG_ID,
                inviteQ: 'launch',
                inviteState: 'revoked',
                inviteSort: 'net',
                inviteDir: 'desc',
            }),
        }));
    });

    it('never offers a readout that is a sum of sources', () => {
        expect(page().readouts().map(r => r.value)).toEqual([130, 40]);
        expect(el().querySelectorAll('.ro-value').length).toBe(2);
    });

    it('says the sources are off rather than drawing an empty chart', async () => {
        page().toggle(BLOG_ID);
        page().toggle(DEVLOG_ID);
        page().toggle(QUIET_ID);
        await settle();

        expect(page().anySelected()).toBe(false);
        expect(fixture.debugElement.query(By.directive(GrowthChartComponent))).toBeNull();
        expect(el().querySelector('.stats-empty')!.textContent).toContain('switched off');

        const showAll = [...el().querySelectorAll<HTMLButtonElement>('.stats-empty button')]
            .find(button => button.textContent?.includes(en.stats.sources.showAll))!;
        showAll.click();
        await settle();

        expect(page().anySelected()).toBe(true);
        expect(page().selected()).toEqual(new Set([BLOG_ID, DEVLOG_ID, QUIET_ID]));
        expect(api.calls.at(-1)!.sources).toEqual([BLOG_ID, DEVLOG_ID, QUIET_ID]);
    });

    it('distinguishes "all off" from filters that hide drawable sources', async () => {
        page().toggle(BLOG_ID);
        page().toggle(DEVLOG_ID);
        await settle();

        expect(page().anySelected()).toBe(true);
        expect(el().querySelector('.stats-empty')!.textContent).toContain(en.stats.sources.filtered);
        expect(el().querySelector('.stats-empty')!.textContent).toContain(en.stats.sources.showAll);
    });

    it('keeps zero-reading sources selected and offers the documents route', async () => {
        api.available = AVAILABLE.map(a => ({ ...a, firstDay: null }));
        api.series = vi.fn().mockImplementation((days: number) => Promise.resolve({
            zone: 'UTC', requestedDays: days, days: [], available: api.available, series: [], audience: AUDIENCE,
        }));
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
        page().toggle(DEVLOG_ID);
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

        expect(page().drawable().map(source => source.id)).toContain(DEVLOG_ID);
    });

    it('offers Subscribers only when a source can answer it', async () => {
        expect(page().metricTabs().map(t => t.id))
            .toEqual(['memberCount', 'viewCount', 'likeCount', 'commentCount']);

        api.channels = [];
        api.available = [AVAILABLE[0]];
        const blogOnly = TestBed.createComponent(StatsComponent);
        await settle(blogOnly);

        expect(blogOnly.componentInstance.metricTabs().map(t => t.id))
            .toEqual(['viewCount', 'likeCount', 'commentCount']);
    });

    // ADR-279 — one request per selection, the selected ids on it; the metric and the view are the
    // client's own and cost nothing.
    it('asks once for every selected source, and refetches only when the selection or range moves', async () => {
        expect(api.calls).toEqual([{ days: 90, sources: [BLOG_ID, DEVLOG_ID, QUIET_ID] }]);

        page().setMetric('likeCount');
        page().setView('table');
        await settle();
        expect(api.calls.length).toBe(1);

        page().toggle(DEVLOG_ID);
        await settle();
        expect(api.calls.at(-1)).toEqual({ days: 90, sources: [BLOG_ID, QUIET_ID] });

        await page().onRangeCommit(30);
        expect(api.calls.at(-1)).toEqual({ days: 30, sources: [BLOG_ID, QUIET_ID] });
    });

    it('keeps the previous answer on the board when a refetch fails', async () => {
        const before = page().series().map(s => s.points);
        api.fail = true;

        page().toggle(DEVLOG_ID);
        await settle();

        expect(page().pending()).toBe(false);
        expect(page().days()).toEqual(ALIGNED_DAYS);
        expect(page().series().map(s => s.points)).toEqual([before[0]]);
    });

    it('keeps the audience shelf when the blog line is off — it rides every response', async () => {
        page().toggle(BLOG_ID);
        await settle();

        expect(page().series().some(s => s.name === 'Blog')).toBe(false);
        expect(page().hasAudience()).toBe(true);
    });

    it('remembers a switched-off source\'s number on its leaf', async () => {
        page().toggle(DEVLOG_ID);
        await settle();

        const devlog = page().leaves().find(l => l.name === 'Devlog')!;
        expect(devlog.state).toBe('idle');
        expect(devlog.note).toBe('40');
    });

    // T-241 — a target is a leaf the moment the server lists it, and a line once it has a reading.
    it('offers an X or Bluesky account as a leaf only once available[] lists it', async () => {
        expect(page().leaves().some(l => l.id === BSKY_ID)).toBe(false);
        expect(el().querySelector('.filter-strip app-brand-icon')).not.toBeNull();

        api.available = [...AVAILABLE, BSKY];
        const withTarget = TestBed.createComponent(StatsComponent);
        await settle(withTarget);
        withTarget.componentInstance.setMetric('likeCount');
        await settle(withTarget);

        const leaf = withTarget.componentInstance.leaves().find(l => l.id === BSKY_ID)!;
        expect(leaf).toMatchObject({ name: 'studio.bsky.social', brand: 'bluesky', network: en.stats.sources.network.bluesky, state: 'active', swatch: 'var(--series-4)' });
        expect(withTarget.componentInstance.series().map(s => s.name)).toEqual(['Blog', 'Devlog', 'studio.bsky.social']);
        expect(withTarget.componentInstance.series().at(-1)!.points).toEqual([3, 5]);
        const strip = withTarget.nativeElement.querySelector('.filter-strip') as HTMLElement;
        expect(strip.textContent).toContain(en.stats.sources.network.bluesky);
        // Two requests on a fresh open: the client could not name the target before the server did.
        expect(api.calls.map(c => c.sources)).toEqual([
            [BLOG_ID, DEVLOG_ID, QUIET_ID], [BLOG_ID, DEVLOG_ID, QUIET_ID], [BLOG_ID, DEVLOG_ID, QUIET_ID, BSKY_ID],
        ]);
    });

    // ADR-316 §1 — a page of its own: the name in its header, and the CSV as the header's action.
    it('is the Metrics page, with the CSV export as its header action', () => {
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        expect(el().querySelector('app-page-header .page-title')?.textContent?.trim()).toBe(en.stats.crumb);
        expect(el().querySelectorAll('app-page-header .page-actions a.export-csv').length).toBe(1);
        expect(el().querySelectorAll('a.export-csv').length).toBe(1);
        expect(el().querySelector('.stats-scroll .stats-tab')).not.toBeNull();
    });

    // T-243 — the same matrix as a file.
    it('links the CSV export to the drawn selection and drops the address when nothing is drawn', async () => {
        const link = () => el().querySelector('app-page-header a.export-csv') as HTMLAnchorElement;
        expect(link().getAttribute('href')).toBe(`/api/stats/series.csv?days=90&sources=${BLOG_ID},${DEVLOG_ID},${QUIET_ID}`);
        expect(link().hasAttribute('download')).toBe(true);
        expect(link().getAttribute('aria-disabled')).toBeNull();
        expect(link().textContent).toContain(en.stats.exportCsv);
        expect(link().querySelector('app-icon')).not.toBeNull();

        page().toggle(BLOG_ID);
        page().toggle(DEVLOG_ID);
        page().toggle(QUIET_ID);
        await settle();

        expect(page().canExport()).toBe(false);
        expect(link().getAttribute('href')).toBeNull();
        expect(link().getAttribute('aria-disabled')).toBe('true');
    });

    it('uses a discrete period picker and names the display timezone', () => {
        const options = [...el().querySelectorAll('.range-picker option')] as HTMLOptionElement[];
        expect(options.map(option => Number(option.value))).toEqual([7, 14, 30, 60, 90, 180]);
        expect(el().querySelector('input[type="range"]')).toBeNull();
        expect(page().updatedAt()).toMatch(/^08\/11 \S+$/);
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

describe('metrics route restoration', () => {
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
                                statsSources: DEVLOG_ID,
                                statsSort: DEVLOG_ID,
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
        expect(page.selected()).toEqual(new Set([DEVLOG_ID]));
        // A saved selection is sent as it is — one request, no guess.
        expect(api.calls).toEqual([{ days: 30, sources: [DEVLOG_ID] }]);
        expect(page.tableSortKey()).toBe(DEVLOG_ID);
        expect(page.tableSortDirection()).toBe('desc');
        expect(page.inviteChannelId()).toBe('c2');
        expect(page.inviteQuery()).toBe('launch');
        expect(page.inviteState()).toBe('revoked');
        expect(page.inviteSortKey()).toBe('net');
        expect(page.inviteSortDirection()).toBe('asc');
    });

    // ADR-322 — the open project reaches every request that is derived from documents.
    it('passes the open project to the series, the streak and the CSV', async () => {
        const api = new ApiStub();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: ChannelsService, useValue: api },
                { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ project: 'p1' }) } } },
            ],
        });
        const scoped = TestBed.createComponent(StatsComponent);
        scoped.detectChanges();
        await new Promise(resolve => setTimeout(resolve, 0));
        await scoped.whenStable();
        scoped.detectChanges();

        expect(api.projects.length).toBeGreaterThan(1);
        expect(api.projects.every(project => project === 'p1')).toBe(true);
        expect(scoped.componentInstance.csvUrl()).toContain('&project=p1');
    });
});
