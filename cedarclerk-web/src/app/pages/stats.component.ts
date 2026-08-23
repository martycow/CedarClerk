import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { formatInZone, zoneAbbreviation } from '../core/display-time';
import { ChannelsService, Channel, ChannelStats, BlogStats, AudienceSlice } from '../core/channels.service';
import { LocaleService } from '../core/i18n/locale.service';
import { LeafState, LeafTagComponent } from '../bench/display/leaf-tag.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { GrowthChartComponent, GrowthSeries, SeriesSlot, seriesColor } from '../bench/worktop/growth-chart.component';

type MetricKey = 'memberCount' | 'viewCount' | 'likeCount' | 'commentCount';
type PanelView = 'chart' | 'table';

interface Source {
    id: string;
    name: string;
    slot: SeriesSlot;
    tracked: readonly MetricKey[];
    /** Calendar days in the display zone, ascending. */
    days: readonly string[];
    labels: ReadonlyMap<string, string>;
    values: ReadonlyMap<string, Partial<Record<MetricKey, number>>>;
    current: Partial<Record<MetricKey, number | null>>;
    delta: Partial<Record<MetricKey, number | null>>;
}

interface AudienceRow {
    code: string;
    label: string;
    views: number;
    share: number;
    bar: number;
}

// How many rows a breakdown shows before the tail folds into one «Other» row — a long tail of
// one-view countries is noise on first read, but it's real data, so it folds rather than disappears.
const AUDIENCE_VISIBLE = 8;
const OTHER_CODE = 'other';

// The server's bucket for a view Cloudflare or the browser didn't identify (Consts.General.UnknownGeo).
const UNKNOWN_GEO = '??';

const RANGE_NOTCHES = [7, 14, 30, 60, 90, 180];

const BLOG_ID = 'blog';
const BLOG_METRICS: readonly MetricKey[] = ['viewCount', 'likeCount', 'commentCount'];
const CHANNEL_METRICS: readonly MetricKey[] = ['memberCount', 'viewCount', 'likeCount', 'commentCount'];

// A slot belongs to the entity, not to its place in the list, so switching a source off cannot
// repaint the survivors (ADR-158). The blog holds ink blue; channels take the rest in list order
// and wrap past the palette's capacity, where the chart's end labels are what tells them apart.
const BLOG_SLOT: SeriesSlot = 2;
const CHANNEL_SLOTS: readonly SeriesSlot[] = [1, 3, 4, 5, 6];

const DAY_KEY = 'yyyy-MM-dd';
const DAY_LABEL = 'dd.MM';

const group = (n: number) => String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

interface Normalized {
    days: string[];
    labels: Map<string, string>;
    values: Map<string, Partial<Record<MetricKey, number>>>;
}

// Snapshots are one reading per day, but the blog takes today's on demand while the nightly job
// takes the rest, so two readings can land on one display-zone day; the later one wins.
function normalize(snapshots: readonly unknown[], tracked: readonly MetricKey[]): Normalized {
    const labels = new Map<string, string>();
    const values = new Map<string, Partial<Record<MetricKey, number>>>();
    for (const snapshot of snapshots) {
        const row = snapshot as Record<string, string | number>;
        const day = formatInZone(row['takenAt'] as string, DAY_KEY);
        if (!day) continue;
        labels.set(day, formatInZone(row['takenAt'] as string, DAY_LABEL));
        const point: Partial<Record<MetricKey, number>> = {};
        for (const metric of tracked) {
            const value = row[metric];
            if (typeof value === 'number') point[metric] = value;
        }
        values.set(day, point);
    }
    return { days: [...values.keys()].sort(), labels, values };
}

@Component({
    selector: 'app-stats',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [LeafTagComponent, IndexTabsComponent, ShelfPanelComponent, GrowthChartComponent],
    // The tab body is the reading surface the shell hands over (ADR-154); the two shelves declare
    // their own chrome from inside.
    host: { 'data-surface': 'paper' },
    templateUrl: 'stats.component.html',
    styleUrls: ['stats.component.css'],
})
// Rendered as the Posts Manager's statistics tab (ADR-148) — no page chrome of its own.
export class StatsComponent implements OnInit {
    private channelsApi = inject(ChannelsService);
    private locale = inject(LocaleService);
    t = this.locale.t;

    loading = signal(true);
    pending = signal(false);
    channels = signal<Channel[]>([]);
    blogStats = signal<BlogStats | null>(null);
    channelStats = signal<ReadonlyMap<string, ChannelStats>>(new Map());
    selected = signal<ReadonlySet<string>>(new Set([BLOG_ID]));
    metric = signal<MetricKey>('viewCount');
    view = signal<PanelView>('chart');
    rangeDays = signal(90);

    // Geography only exists for the blog: Telegram's Bot API reports no per-country breakdown,
    // so the shelf names the source it is answering about (ADR-097, ADR-149 item 4).
    countryRows = computed(() => this.audienceRows(this.blogStats()?.countries ?? [], 'region'));
    languageRows = computed(() => this.audienceRows(this.blogStats()?.languages ?? [], 'language'));
    hasAudience = computed(() => this.countryRows().length > 0);

    // The newest reading over every source, so the strip says how fresh the whole board is.
    updatedAt = computed(() => {
        const taken = [
            ...(this.blogStats()?.snapshots ?? []),
            ...[...this.channelStats().values()].flatMap(s => s.snapshots),
        ].map(s => s.takenAt);
        const latest = taken.reduce((max, at) => (at > max ? at : max), '');
        return latest ? `${formatInZone(latest, 'HH:mm')} ${zoneAbbreviation(latest)}` : '';
    });

    readonly group = group;
    readonly otherCode = OTHER_CODE;
    readonly rangeNotches = RANGE_NOTCHES;

    sources = computed<Source[]>(() => {
        const out: Source[] = [];
        const blog = this.blogStats();
        out.push({
            id: BLOG_ID,
            name: this.t().stats.blog,
            slot: BLOG_SLOT,
            tracked: BLOG_METRICS,
            ...normalize(blog?.snapshots ?? [], BLOG_METRICS),
            current: { viewCount: blog?.currentViews ?? null, likeCount: blog?.currentLikes ?? null, commentCount: blog?.currentComments ?? null },
            delta: { viewCount: blog?.deltaWeekViews ?? null, likeCount: blog?.deltaWeekLikes ?? null, commentCount: blog?.deltaWeekComments ?? null },
        });

        const stats = this.channelStats();
        this.channels().forEach((channel, index) => {
            const s = stats.get(channel.id);
            out.push({
                id: channel.id,
                name: channel.title,
                slot: CHANNEL_SLOTS[index % CHANNEL_SLOTS.length],
                tracked: CHANNEL_METRICS,
                ...normalize(s?.snapshots ?? [], CHANNEL_METRICS),
                current: { memberCount: s?.current ?? null, viewCount: s?.currentViews ?? null, likeCount: s?.currentLikes ?? null, commentCount: s?.currentComments ?? null },
                delta: { memberCount: s?.deltaWeek ?? null, viewCount: s?.deltaWeekViews ?? null, likeCount: s?.deltaWeekLikes ?? null, commentCount: s?.deltaWeekComments ?? null },
            });
        });
        return out;
    });

    drawable = computed(() => this.sources().filter(s =>
        this.selected().has(s.id) && s.tracked.includes(this.metric()) && s.days.length > 0));

    anySelected = computed(() => this.sources().some(s => this.selected().has(s.id)));

    /**
     * The days every drawn line has a reading for. It starts at the latest first reading among
     * them, because there is no honest value for a source before it had one, and it moves with the
     * selection — which is why the panel counter states the window instead of leaving it to the
     * axis (ADR-161).
     */
    axis = computed(() => {
        const drawn = this.drawable();
        if (!drawn.length) return { days: [] as string[], labels: [] as string[] };
        const start = drawn.reduce((latest, s) => (s.days[0] > latest ? s.days[0] : latest), drawn[0].days[0]);
        const union = new Set<string>();
        for (const s of drawn) for (const day of s.days) if (day >= start) union.add(day);
        const days = [...union].sort();
        const labels = days.map(day => drawn.find(s => s.labels.has(day))?.labels.get(day) ?? day);
        return { days, labels };
    });

    series = computed<GrowthSeries[]>(() => {
        const drawn = this.drawable();
        const { days } = this.axis();
        const metric = this.metric();
        return drawn.map(source => ({
            slot: source.slot,
            name: source.name,
            // The wash belongs to one entity (ADR-158 clause 6): the blog, first in sources(),
            // keeps it however many lines are drawn and takes it away when it is switched off.
            wash: source.id === BLOG_ID,
            points: this.carryForward(source, days, metric),
        }));
    });

    readouts = computed(() => {
        const metric = this.metric();
        return this.drawable().map(source => ({
            id: source.id,
            name: source.name,
            color: seriesColor(source.slot),
            value: source.current[metric] ?? null,
            delta: source.delta[metric] ?? null,
        }));
    });

    leaves = computed(() => {
        const metric = this.metric();
        const strings = this.t().stats.sources;
        return this.sources().map(source => {
            const tracked = source.tracked.includes(metric);
            const hasReadings = source.days.length > 0;
            const dried = !tracked || !hasReadings;
            const state: LeafState = dried ? 'dried' : this.selected().has(source.id) ? 'active' : 'idle';
            return {
                id: source.id,
                name: source.name,
                state,
                // A dried leaf keeps a swatch so the strip stays one shape, but not the series ink:
                // nothing is drawn in that colour while it is dried.
                swatch: dried ? 'var(--t3)' : seriesColor(source.slot),
                note: !tracked ? strings.notTracked : !hasReadings ? strings.noData : group(source.current[metric] ?? 0),
            };
        });
    });

    metricTabs = computed<IndexTabItem[]>(() => {
        const labels = this.t().stats.metrics;
        return CHANNEL_METRICS
            .filter(metric => this.sources().some(s => s.tracked.includes(metric)))
            .map(metric => ({ id: metric, label: labels[metric] }));
    });

    viewTabs = computed<IndexTabItem[]>(() => [
        { id: 'chart', label: this.t().stats.views.chart },
        { id: 'table', label: this.t().stats.views.table },
    ]);

    windowLabel = computed(() => {
        const { days, labels } = this.axis();
        if (!days.length) return '';
        return this.t().stats.window(days.length, labels[0], labels[labels.length - 1]);
    });

    chartLabel = computed(() => `${this.t().stats.bySource} — ${this.t().stats.metrics[this.metric()]}`);

    tableRows = computed(() => {
        const { labels } = this.axis();
        const series = this.series();
        return labels.map((label, index) => ({ label, values: series.map(s => group(s.points[index])) }));
    });

    async ngOnInit() {
        this.loading.set(true);
        try {
            const channels = await this.channelsApi.list();
            this.channels.set(channels);
            await this.load(this.rangeDays());
            this.selected.set(new Set(this.sources().filter(s => s.days.length > 0).map(s => s.id)));
        } finally {
            this.loading.set(false);
        }
    }

    /**
     * One request per source, in parallel: the API has no multi-source endpoint and this port does
     * not invent one (ADR-161). Blog stats are fetched whether or not the blog line is drawn —
     * the audience shelf is blog data, and it must not blank when the reader looks at a channel.
     */
    private async load(days: number) {
        this.pending.set(true);
        try {
            const channels = this.channels();
            const results = await Promise.allSettled([
                this.channelsApi.getBlogStats(days),
                ...channels.map(c => this.channelsApi.getStats(c.id, days)),
            ]);
            const [blog, ...perChannel] = results;
            if (blog.status === 'fulfilled') this.blogStats.set(blog.value as BlogStats);
            const stats = new Map(this.channelStats());
            perChannel.forEach((result, index) => {
                if (result.status === 'fulfilled') stats.set(channels[index].id, result.value as ChannelStats);
            });
            this.channelStats.set(stats);
        } finally {
            this.pending.set(false);
        }
    }

    toggle(id: string) {
        const next = new Set(this.selected());
        if (!next.delete(id)) next.add(id);
        this.selected.set(next);
    }

    setMetric(id: string) {
        this.metric.set(id as MetricKey);
    }

    setView(id: string) {
        this.view.set(id as PanelView);
    }

    async onRangeCommit(raw: number) {
        const days = RANGE_NOTCHES.includes(raw) ? raw : 90;
        this.rangeDays.set(days);
        await this.load(days);
    }

    rangeLabel(d = this.rangeDays()): string {
        return d % 30 === 0 && d >= 30 ? this.t().stats.months(d / 30) : this.t().stats.days(d);
    }

    /**
     * A day with no snapshot takes the source's previous reading. The series are running totals,
     * so the last reading is what is known until the next one is taken; the window's start is what
     * guarantees there is one to carry (ADR-161).
     */
    private carryForward(source: Source, days: readonly string[], metric: MetricKey): number[] {
        let last = 0;
        for (const day of source.days) {
            if (day > days[0]) break;
            const value = source.values.get(day)?.[metric];
            if (value !== undefined) last = value;
        }
        return days.map(day => {
            const value = source.values.get(day)?.[metric];
            if (value !== undefined) last = value;
            return last;
        });
    }

    // Built per (UI language, kind) rather than per row: the list re-renders on every change
    // detection pass, and constructing an Intl formatter is not free.
    private displayNamesCache = new Map<string, Intl.DisplayNames | null>();

    private displayName(code: string, type: 'region' | 'language'): string {
        const key = `${this.locale.uiLang()}:${type}`;
        if (!this.displayNamesCache.has(key)) {
            try {
                this.displayNamesCache.set(key, new Intl.DisplayNames([this.locale.uiLang()], { type }));
            } catch {
                this.displayNamesCache.set(key, null);
            }
        }
        try {
            return this.displayNamesCache.get(key)?.of(type === 'region' ? code.toUpperCase() : code) ?? code;
        } catch {
            return code;
        }
    }

    // Shares are of the period's total views, so the two lists each add up to ~100% on their own.
    // Bars are scaled to the leader instead, otherwise everything below the top country is a stub.
    private audienceRows(slices: AudienceSlice[], type: 'region' | 'language'): AudienceRow[] {
        const total = slices.reduce((sum, s) => sum + s.views, 0);
        if (total === 0) return [];
        const head = slices.slice(0, AUDIENCE_VISIBLE);
        const rest = slices.slice(AUDIENCE_VISIBLE).reduce((sum, s) => sum + s.views, 0);
        const top = Math.max(...head.map(s => s.views), rest);
        const row = (code: string, label: string, views: number): AudienceRow => ({
            code,
            label,
            views,
            share: Math.round((views / total) * 100),
            bar: Math.max(2, Math.round((views / top) * 100)),
        });
        const rows = head.map(s => row(
            s.code,
            s.code === UNKNOWN_GEO ? this.t().stats.audience.unknown : this.displayName(s.code, type),
            s.views,
        ));
        if (rest > 0) rows.push(row(OTHER_CODE, this.t().stats.audience.other, rest));
        return rows;
    }
}
