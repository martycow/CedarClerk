import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { ChannelsService, Channel, ChannelStats, ChannelStatSnapshotDto, BlogStats, BlogStatSnapshotDto, AudienceSlice } from '../core/channels.service';
import { LocaleService } from '../core/i18n/locale.service';

type MetricKey = 'memberCount' | 'viewCount' | 'likeCount' | 'commentCount';

interface ChartPoint {
    x: number;
    y: number;
    value: number;
    date: string;
}

interface ChartTick {
    y: number;
    label: string;
}

interface ChartLayout {
    hasData: boolean;
    linePath: string;
    areaPath: string;
    points: ChartPoint[];
    ticks: ChartTick[];
}

interface MetricCard {
    key: MetricKey;
    label: string;
    current: number | null;
    delta: number | null;
    chart: ChartLayout;
}

interface AudienceRow {
    code: string;
    label: string;
    flag: string;
    views: number;
    share: number;
    bar: number;
}

// How many rows a breakdown shows before the "show all" toggle — a long tail of one-view
// countries is noise on first read, but it's real data, so it folds rather than disappears.
const AUDIENCE_VISIBLE = 8;

// The server's bucket for a view Cloudflare or the browser didn't identify (Consts.General.UnknownGeo).
const UNKNOWN_GEO = '??';

// 'DE' -> 🇩🇪. Regional indicators are just the letters offset into their own block, so any
// alpha-2 gets a flag without a sprite sheet or an icon dependency.
function flagOf(code: string): string {
    if (code === UNKNOWN_GEO) return '🌐';
    return String.fromCodePoint(...[...code.toUpperCase()].map(c => 0x1f1a5 + c.charCodeAt(0)));
}

// Stats range (N9): a week to half a year, with the ranges people actually ask for as magnets.
const RANGE_MIN = 7;
const RANGE_MAX = 180;
const RANGE_NOTCHES = [7, 14, 30, 60, 90, 180];
const NOTCH_PULL_DAYS = 4;

function snapToNotch(days: number): number {
    const value = Math.min(RANGE_MAX, Math.max(RANGE_MIN, Math.round(days)));
    const nearest = RANGE_NOTCHES.reduce((best, n) =>
        Math.abs(n - value) < Math.abs(best - value) ? n : best, RANGE_NOTCHES[0]);
    return Math.abs(nearest - value) <= NOTCH_PULL_DAYS ? nearest : value;
}

const CHART_WIDTH = 600;
const CHART_HEIGHT = 160;
const PAD_X = 4;
const PAD_TOP = 12;
const PAD_BOTTOM = 4;

@Component({
    selector: 'app-stats',
    imports: [ZonedDatePipe],
    templateUrl: 'stats.component.html',
    styleUrls: ['stats.component.css'],
})
// Rendered as the Posts Manager's statistics tab (N7) — no page chrome of its own any more.
export class StatsComponent implements OnInit {
    private channelsApi = inject(ChannelsService);
    private locale = inject(LocaleService);
    t = this.locale.t;

    loading = signal(true);
    channels = signal<Channel[]>([]);
    selectedView = signal<'blog' | 'channel'>('blog');
    selectedChannelId = signal<string | null>(null);
    stats = signal<ChannelStats | null>(null);
    blogStats = signal<BlogStats | null>(null);
    rangeDays = signal(90);

    hover = signal<{ key: MetricKey; index: number } | null>(null);
    countriesExpanded = signal(false);
    languagesExpanded = signal(false);

    // Geography only exists for the blog: Telegram's Bot API reports no per-country breakdown,
    // so a channel tab would have nothing to put here (see ADR-097).
    countryRows = computed(() => this.audienceRows(this.blogStats()?.countries ?? [], 'region'));
    languageRows = computed(() => this.audienceRows(this.blogStats()?.languages ?? [], 'language'));
    hasAudience = computed(() => this.countryRows().length > 0);

    metricCards = computed<MetricCard[]>(() => {
        if (this.selectedView() === 'blog') {
            const s = this.blogStats();
            if (!s) return [];
            const base: { key: MetricKey; label: string; current: number | null; delta: number | null }[] = [
                { key: 'viewCount', label: this.t().stats.metrics.viewCount, current: s.currentViews, delta: s.deltaWeekViews },
                { key: 'likeCount', label: this.t().stats.metrics.likeCount, current: s.currentLikes, delta: s.deltaWeekLikes },
                { key: 'commentCount', label: this.t().stats.metrics.commentCount, current: s.currentComments, delta: s.deltaWeekComments },
            ];
            return base.map(m => ({ ...m, chart: this.buildChart(s.snapshots, m.key) }));
        }

        const s = this.stats();
        if (!s) return [];
        const base: { key: MetricKey; label: string; current: number | null; delta: number | null }[] = [
            { key: 'memberCount', label: this.t().stats.metrics.memberCount, current: s.current, delta: s.deltaWeek },
            { key: 'viewCount', label: this.t().stats.metrics.viewCount, current: s.currentViews, delta: s.deltaWeekViews },
            { key: 'likeCount', label: this.t().stats.metrics.likeCount, current: s.currentLikes, delta: s.deltaWeekLikes },
            { key: 'commentCount', label: this.t().stats.metrics.commentCount, current: s.currentComments, delta: s.deltaWeekComments },
        ];
        return base.map(m => ({ ...m, chart: this.buildChart(s.snapshots, m.key) }));
    });

    async ngOnInit() {
        this.loading.set(true);
        try {
            const channels = await this.channelsApi.list();
            this.channels.set(channels);
            await this.selectBlog();
        } finally {
            this.loading.set(false);
        }
    }

    async selectBlog() {
        this.selectedView.set('blog');
        this.hover.set(null);
        this.blogStats.set(await this.channelsApi.getBlogStats(this.rangeDays()));
    }

    async selectChannel(id: string) {
        this.selectedView.set('channel');
        this.selectedChannelId.set(id);
        this.hover.set(null);
        this.stats.set(await this.channelsApi.getStats(id, this.rangeDays()));
    }

    async selectRange(days: number) {
        this.rangeDays.set(days);
        this.hover.set(null);
        if (this.selectedView() === 'blog') {
            this.blogStats.set(await this.channelsApi.getBlogStats(days));
        } else if (this.selectedChannelId()) {
            this.stats.set(await this.channelsApi.getStats(this.selectedChannelId()!, days));
        }
    }

    // Free 7…180-day slider with the common ranges as magnets (N9). Dragging updates the label
    // live; the fetch waits for the drag to end, so one drag is one request, not sixty.
    onRangeInput(raw: number) {
        this.rangeDays.set(snapToNotch(raw));
    }

    async onRangeCommit(raw: number) {
        await this.selectRange(snapToNotch(raw));
    }

    rangeLabel(): string {
        const d = this.rangeDays();
        return d % 30 === 0 && d >= 30 ? this.t().stats.months(d / 30) : this.t().stats.days(d);
    }

    readonly rangeNotches = RANGE_NOTCHES;
    readonly rangeMin = RANGE_MIN;
    readonly rangeMax = RANGE_MAX;

    // Percentage along the track, so a notch tick lines up with the value it snaps to.
    notchOffset(days: number): number {
        return ((days - RANGE_MIN) / (RANGE_MAX - RANGE_MIN)) * 100;
    }

    onHover(event: PointerEvent, key: MetricKey, pointCount: number) {
        if (pointCount < 2) return;
        const rect = (event.currentTarget as Element).getBoundingClientRect();
        const ratio = (event.clientX - rect.left) / rect.width;
        const index = Math.min(Math.max(Math.round(ratio * (pointCount - 1)), 0), pointCount - 1);
        this.hover.set({ key, index });
    }

    onLeave(key: MetricKey) {
        if (this.hover()?.key === key) this.hover.set(null);
    }

    hoveredPoint(card: MetricCard): ChartPoint | null {
        const h = this.hover();
        if (!h || h.key !== card.key) return null;
        return card.chart.points[h.index] ?? null;
    }

    visibleCountries = computed(() => this.slice(this.countryRows(), this.countriesExpanded()));
    visibleLanguages = computed(() => this.slice(this.languageRows(), this.languagesExpanded()));

    private slice(rows: AudienceRow[], expanded: boolean): AudienceRow[] {
        return expanded ? rows : rows.slice(0, AUDIENCE_VISIBLE);
    }

    hiddenCount(rows: AudienceRow[]): number {
        return Math.max(0, rows.length - AUDIENCE_VISIBLE);
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
        const top = Math.max(...slices.map(s => s.views));
        return slices.map(s => ({
            code: s.code,
            label: s.code === UNKNOWN_GEO ? this.t().stats.audience.unknown : this.displayName(s.code, type),
            flag: type === 'region' ? flagOf(s.code) : '',
            views: s.views,
            share: Math.round((s.views / total) * 100),
            bar: Math.max(2, Math.round((s.views / top) * 100)),
        }));
    }

    private niceMax(max: number): number {
        if (max <= 0) return 1;
        const magnitude = Math.pow(10, Math.floor(Math.log10(max)));
        for (const step of [1, 2, 2.5, 5, 10]) {
            const candidate = step * magnitude;
            if (candidate >= max) return candidate;
        }
        return 10 * magnitude;
    }

    private formatTick(value: number): string {
        if (value >= 1000) return `${+(value / 1000).toFixed(1)}K`;
        return `${Math.round(value)}`;
    }

    private buildChart(snapshots: (ChannelStatSnapshotDto | BlogStatSnapshotDto)[], key: MetricKey): ChartLayout {
        if (snapshots.length < 2) {
            return { hasData: false, linePath: '', areaPath: '', points: [], ticks: [] };
        }

        const innerW = CHART_WIDTH - PAD_X * 2;
        const innerH = CHART_HEIGHT - PAD_TOP - PAD_BOTTOM;
        const values = snapshots.map(s => (s as unknown as Record<string, number>)[key]);
        const top = this.niceMax(Math.max(...values, 1));

        const points: ChartPoint[] = snapshots.map((s, i) => ({
            x: PAD_X + (i / (snapshots.length - 1)) * innerW,
            y: PAD_TOP + innerH - ((s as unknown as Record<string, number>)[key] / top) * innerH,
            value: (s as unknown as Record<string, number>)[key],
            date: s.takenAt,
        }));

        const linePath = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' ');
        const baseline = PAD_TOP + innerH;
        const areaPath = `${linePath} L${points[points.length - 1].x.toFixed(2)},${baseline.toFixed(2)} `
            + `L${points[0].x.toFixed(2)},${baseline.toFixed(2)} Z`;

        const ticks: ChartTick[] = [0, 0.5, 1].map(fraction => ({
            y: PAD_TOP + innerH - fraction * innerH,
            label: this.formatTick(top * fraction),
        }));

        return { hasData: true, linePath, areaPath, points, ticks };
    }
}
