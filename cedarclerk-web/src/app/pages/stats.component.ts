import { ConfirmationService } from '../core/confirmation.service';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { zoneAbbreviation } from '../core/display-time';
import { FormsModule } from '@angular/forms';
import {
    ChannelsService, Channel, AudienceSlice, StatsSeries, StatSourceKind, StatSourceNetwork,
    StatSourceSeries, PublishingStats, ChannelInviteLink, ChannelMemberFlowRow,
} from '../core/channels.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { LeafState, LeafTagComponent } from '../bench/display/leaf-tag.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { IconComponent } from '../shared/icon.component';
import { BrandIconComponent, BrandIconName } from '../shared/brand-icon.component';
import { GrowthChartComponent, GrowthSeries, SeriesSlot, seriesColor } from '../bench/worktop/growth-chart.component';
import { SortHeaderComponent } from '../bench/worktop/sort-header.component';
import { SortDirection, ariaSort } from '../core/collection-query';
import { ActivatedRoute, Router } from '@angular/router';

type MetricKey = 'memberCount' | 'viewCount' | 'likeCount' | 'commentCount';
type PanelView = 'chart' | 'table';
type InviteLinkState = 'all' | 'active' | 'revoked';
type InviteLinkSort = 'name' | 'joins' | 'leaves' | 'net';

/** One entry of the response's `available[]`, with its slot and — when selected and read — its series. */
interface Source {
    id: string;
    name: string;
    kind: StatSourceKind;
    network: StatSourceNetwork;
    slot: SeriesSlot;
    tracked: readonly MetricKey[];
    hasReadings: boolean;
    series: StatSourceSeries | null;
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

// The member-flow window. Days are the server's UTC calendar days, so the window ends on today's
// UTC date and never on the display zone's.
const MEMBER_FLOW_DAYS = 30;

interface FlowDay {
    day: string;
    label: string;
    joins: number;
    leaves: number;
}

const BLOG_ID = 'blog';

// Every metric the strip can offer, in the order it offers them. What a given source answers is
// the server's `tracked` list; the strip is the union over the sources it lists.
const ALL_METRICS: readonly MetricKey[] = ['memberCount', 'viewCount', 'likeCount', 'commentCount'];

// A slot belongs to the entity, not to its place in the list, so switching a source off cannot
// repaint the survivors (ADR-158). The blog holds ink blue; channels, then targets, take the rest
// in `available` order and wrap past the palette's capacity, where the chart's end labels are what
// tells them apart.
const BLOG_SLOT: SeriesSlot = 2;
const CHANNEL_SLOTS: readonly SeriesSlot[] = [1, 3, 4, 5, 6];

// A network's mark on its leaf. The blog is the one source with no brand — it is this app's own.
const BRAND_ICONS: Partial<Record<StatSourceNetwork, BrandIconName>> = { telegram: 'telegram', x: 'twitter', bluesky: 'bluesky' };

const group = (n: number) => String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

// Day keys are `yyyy-MM-dd` calendar days in the display zone already; the label is the same day, shorter.
const dayLabel = (day: string) => `${day.slice(5, 7)}/${day.slice(8, 10)}`;

/**
 * T-338 — a readout tile's sparkline: the metric's readings across the window, as one polyline in a
 * fixed 64x18 box. Returns null below three readings, where a line would say more than the data
 * does — two points always draw a confident straight run whichever way they fell.
 *
 * A flat series is drawn on the middle line rather than at the bottom: the tile answers "which way
 * is this going", and zero movement is a horizon, not a floor.
 */
const SPARK_W = 64;
const SPARK_H = 18;
const SPARK_MIN_POINTS = 3;

function sparkPath(points: readonly number[]): string | null {
    if (points.length < SPARK_MIN_POINTS) return null;

    const min = Math.min(...points);
    const max = Math.max(...points);
    const span = max - min;
    const stepX = SPARK_W / (points.length - 1);

    return points
        .map((value, i) => {
            const x = i * stepX;
            const y = span === 0 ? SPARK_H / 2 : SPARK_H - ((value - min) / span) * SPARK_H;
            return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)}`;
        })
        .join(' ');
}

@Component({
    selector: 'app-stats',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [FormsModule, LeafTagComponent, IndexTabsComponent, GrowthChartComponent, EmptyStateComponent,
              ButtonComponent, IconComponent, BrandIconComponent, SortHeaderComponent],
    // The tab body is the reading surface the shell hands over (ADR-154); the two shelves declare
    // their own chrome from inside.
    host: { 'data-surface': 'paper' },
    templateUrl: 'stats.component.html',
    styleUrls: ['stats.component.css'],
})
// Rendered as the Posts Manager's statistics tab (ADR-148) — no page chrome of its own.
export class StatsComponent implements OnInit {
    private readonly confirmation = inject(ConfirmationService);
    private channelsApi = inject(ChannelsService);
    private locale = inject(LocaleService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    t = this.locale.t;

    loading = signal(true);
    pending = signal(false);
    channels = signal<Channel[]>([]);
    /** The last answer of `GET /api/stats/series`; a failed refetch leaves it exactly as it was. */
    data = signal<StatsSeries | null>(null);
    selected = signal<ReadonlySet<string>>(new Set([BLOG_ID]));
    metric = signal<MetricKey>('viewCount');
    view = signal<PanelView>('chart');
    rangeDays = signal(90);
    tableSortKey = signal('period');
    tableSortDirection = signal<SortDirection>('asc');
    readonly ariaSort = ariaSort;

    // Geography only exists for the blog: Telegram's Bot API reports no per-country breakdown,
    // so the shelf names the source it is answering about (ADR-097, ADR-149 item 4). It rides
    // every series response whether or not the blog line is drawn.
    countryRows = computed(() => this.audienceRows(this.data()?.audience.countries ?? [], 'region'));

    // A reading language is what a browser volunteers in Accept-Language, and a great many clients
    // volunteer nothing — a link preview fetcher, a feed reader, an in-app webview. Ranked among
    // real languages that bucket wins the list and reads as a language people speak, which is the
    // one thing it is not. It is stated underneath instead, as the share of views the question was
    // never answered for.
    languageRows = computed(() => this.audienceRows(
        (this.data()?.audience.languages ?? []).filter(s => s.code !== UNKNOWN_GEO), 'language'));

    unreportedLanguageShare = computed(() => {
        const slices = this.data()?.audience.languages ?? [];
        const total = slices.reduce((sum, s) => sum + s.views, 0);
        if (!total) return 0;
        const unknown = slices.filter(s => s.code === UNKNOWN_GEO).reduce((sum, s) => sum + s.views, 0);
        return Math.round((unknown / total) * 100);
    });

    hasAudience = computed(() => this.countryRows().length > 0);

    /** The aligned window the server answered with — dense, ascending calendar days in its zone. */
    days = computed(() => this.data()?.days ?? []);
    labels = computed(() => this.days().map(dayLabel));

    // The window's last day and the display zone, so the strip says how far the board reads.
    updatedAt = computed(() => {
        const days = this.days();
        return days.length ? `${dayLabel(days[days.length - 1])} ${zoneAbbreviation(new Date())}` : '';
    });

    readonly group = group;
    readonly otherCode = OTHER_CODE;
    readonly rangeNotches = RANGE_NOTCHES;

    // The leaf strip is `available` verbatim: a source is offered only once the server lists it
    // (ADR-161 rule 4), and it is joined to its series when the server drew one.
    sources = computed<Source[]>(() => {
        const data = this.data();
        if (!data) return [];
        const drawn = new Map(data.series.map(s => [s.id, s]));
        let next = 0;
        return data.available.map(a => ({
            id: a.id,
            name: a.kind === 'blog' ? this.t().stats.blog : a.name,
            kind: a.kind,
            network: a.network,
            slot: a.kind === 'blog' ? BLOG_SLOT : CHANNEL_SLOTS[next++ % CHANNEL_SLOTS.length],
            tracked: a.tracked,
            hasReadings: a.firstDay !== null,
            series: drawn.get(a.id) ?? null,
        }));
    });

    /** Selected ids in `available` order — what the next request and the CSV link carry. */
    selectedIds = computed(() => {
        const selected = this.selected();
        const known = this.sources().map(s => s.id);
        return known.length ? known.filter(id => selected.has(id)) : [...selected];
    });

    drawable = computed(() => this.sources().filter(s =>
        this.selected().has(s.id) && s.tracked.includes(this.metric()) && Array.isArray(s.series?.values[this.metric()])));

    anySelected = computed(() => this.sources().some(s => this.selected().has(s.id)));

    private hiddenDrawableSource = computed(() => this.sources().some(s =>
        !this.selected().has(s.id) && s.tracked.includes(this.metric()) && s.hasReadings));

    chartEmptyState = computed(() => {
        const copy = this.t().stats.sources;
        if (this.sources().length && !this.anySelected()) {
            return { title: copy.noneSelectedTitle, text: copy.noneSelected, action: 'show-all' as const };
        }
        if (this.hiddenDrawableSource()) {
            return { title: copy.filteredTitle, text: copy.filtered, action: 'show-all' as const };
        }
        return { title: copy.nothingToDrawTitle, text: copy.nothingToDraw, action: 'documents' as const };
    });

    series = computed<(GrowthSeries & { id: string })[]>(() => {
        const metric = this.metric();
        return this.drawable().map(source => ({
            id: source.id,
            slot: source.slot,
            name: source.name,
            // The wash belongs to one entity (ADR-158 clause 6): the blog, first in sources(),
            // keeps it however many lines are drawn and takes it away when it is switched off.
            wash: source.id === BLOG_ID,
            points: source.series!.values[metric] as number[],
        }));
    });

    // The card and the chart read one answer: `current` is the window's last value and `delta`
    // its last minus its first, both computed by the server over the same aligned days (ADR-279).
    readouts = computed(() => {
        const metric = this.metric();
        return this.drawable().map(source => ({
            id: source.id,
            name: source.name,
            color: seriesColor(source.slot),
            value: source.series!.current[metric] ?? null,
            delta: source.series!.delta[metric] ?? null,
            // T-338 — the tile's own shape of the window it reports. Drawn from the series the
            // chart already holds, so it costs no request and cannot disagree with the chart.
            spark: sparkPath(source.series!.values[metric] as number[]),
        }));
    });

    // A switched-off source is not in the response, so the number its leaf showed while it was
    // drawn is kept here — the current value is the latest reading, which no window changes.
    private lastCurrent = signal<ReadonlyMap<string, Partial<Record<MetricKey, number>>>>(new Map());

    leaves = computed(() => {
        const metric = this.metric();
        const strings = this.t().stats.sources;
        const remembered = this.lastCurrent();
        return this.sources().map(source => {
            const tracked = source.tracked.includes(metric);
            const dried = !tracked || !source.hasReadings;
            const state: LeafState = dried ? 'dried' : this.selected().has(source.id) ? 'active' : 'idle';
            const current = source.series?.current[metric] ?? remembered.get(source.id)?.[metric];
            return {
                id: source.id,
                name: source.name,
                brand: BRAND_ICONS[source.network] ?? null,
                network: source.network === 'x' || source.network === 'bluesky' ? strings.network[source.network] : '',
                state,
                // A dried leaf keeps a swatch so the strip stays one shape, but not the series ink:
                // nothing is drawn in that colour while it is dried.
                swatch: dried ? 'var(--t3)' : seriesColor(source.slot),
                note: !tracked ? strings.notTracked : !source.hasReadings ? strings.noData
                    : current === undefined ? '' : group(current),
            };
        });
    });

    metricTabs = computed<IndexTabItem[]>(() => {
        const labels = this.t().stats.metrics;
        return ALL_METRICS
            .filter(metric => this.sources().some(s => s.tracked.includes(metric)))
            .map(metric => ({ id: metric, label: labels[metric] }));
    });

    /**
     * ADR-205 — the line under the strip that says why a leaf is dried, or where a number starts.
     * One sentence at a time: it explains the metric being looked at, not every metric there is.
     */
    metricNote = computed(() => {
        const strings = this.t().stats.sources;
        const metric = this.metric();
        const anyDried = this.leaves().some(l => l.state === 'dried');
        if (metric === 'viewCount' && anyDried) return strings.notTrackedWhy;
        if (metric === 'likeCount' || metric === 'commentCount') return strings.telegramSince;
        return '';
    });

    viewTabs = computed<IndexTabItem[]>(() => [
        { id: 'chart', label: this.t().stats.views.chart },
        { id: 'table', label: this.t().stats.views.table },
    ]);

    windowLabel = computed(() => {
        const labels = this.labels();
        if (!labels.length) return '';
        return this.t().stats.window(labels.length, labels[0], labels[labels.length - 1]);
    });

    chartLabel = computed(() => `${this.t().stats.bySource} — ${this.t().stats.metrics[this.metric()]}`);

    // T-243 — the same matrix as a file. Disabled while the server drew nothing: an empty
    // selection would download a header row and nothing else.
    canExport = computed(() => (this.data()?.series.length ?? 0) > 0);
    csvUrl = computed(() => this.channelsApi.seriesCsvUrl(this.rangeDays(), this.selectedIds()));

    tableRows = computed(() => {
        const days = this.days();
        const labels = this.labels();
        const series = this.series();
        const rows = labels.map((label, index) => ({
            day: days[index],
            label,
            raw: series.map(s => s.points[index]),
            values: series.map(s => group(s.points[index])),
        }));
        const key = this.tableSortKey();
        const sourceIndex = series.findIndex(s => s.id === key);
        const direction = this.tableSortDirection() === 'asc' ? 1 : -1;
        return rows.sort((a, b) => {
            const compared = key === 'period' || sourceIndex < 0
                ? a.day.localeCompare(b.day)
                : (a.raw[sourceIndex] ?? 0) - (b.raw[sourceIndex] ?? 0);
            return compared * direction || a.day.localeCompare(b.day);
        });
    });

    sortTable(key: string) {
        if (this.tableSortKey() === key) this.tableSortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
        else {
            this.tableSortKey.set(key);
            this.tableSortDirection.set('asc');
        }
        this.syncCollectionQuery();
    }

    private normalizeTableSort(): void {
        const key = this.tableSortKey();
        if (key === 'period' || this.series().some(source => source.id === key)) return;
        this.tableSortKey.set('period');
        this.tableSortDirection.set('asc');
    }

    async ngOnInit() {
        this.restoreCollectionQuery();
        this.loading.set(true);
        try {
            const channels = await this.channelsApi.list();
            this.channels.set(channels);
            const requested = this.route.snapshot.queryParamMap.get('statsSources');
            // The server owns the source list, and it is only known once it has answered. With no
            // saved selection the first request names what the client can — the blog and every
            // channel — and a source it could not name (an X or Bluesky account) is switched on
            // with one more request, so a fresh open still draws every line.
            this.selected.set(requested === 'none'
                ? new Set()
                : requested
                    ? new Set(requested.split(','))
                    : new Set([BLOG_ID, ...channels.map(c => `channel:${c.id}`)]));
            await this.load();
            if (!requested) {
                const all = this.sources().map(source => source.id);
                if (all.some(id => !this.selected().has(id))) {
                    this.selected.set(new Set(all));
                    await this.load();
                }
            }
        } finally {
            this.loading.set(false);
        }
        // The streak card and the invite-links shelf, both best-effort: a failure leaves the
        // board exactly as it was.
        this.channelsApi.publishingStats()
            .then(stats => this.publishing.set(stats))
            .catch(() => this.publishing.set(null));
        const requestedChannel = this.route.snapshot.queryParamMap.get('inviteChannel');
        const first = this.channels().find(channel => channel.id === requestedChannel) ?? this.channels()[0];
        if (first) {
            this.inviteChannelId.set(first.id);
            void this.loadInviteLinks();
        }
    }

    // ─── Publishing streaks (Wave 2 item 13) ──────────────────────────────────────────────────
    publishing = signal<PublishingStats | null>(null);

    /**
     * Publish-event markers for the chart: every drawn series' publish days, as read off the
     * response, mapped onto the axis. The server already clipped them to the window.
     */
    publishMarkers = computed<number[]>(() => {
        const days = this.days();
        if (!days.length) return [];
        const marks = new Set<number>();
        for (const source of this.drawable()) {
            for (const day of source.series!.publishDays) {
                const index = days.indexOf(day);
                if (index >= 0) marks.add(index);
            }
        }
        return [...marks];
    });

    // ─── Invite links (Wave 2 item 15; cut #6 taken — totals table only) ──────────────────────
    inviteChannelId = signal('');
    inviteLinks = signal<ChannelInviteLink[]>([]);
    /** Joins with no named link, plus every leave — Telegram never attributes a leave. */
    inviteOrganic = signal<{ joins: number; leaves: number } | null>(null);
    /** null until the day series answers — and if it never does, the totals table stands alone. */
    memberFlow = signal<ChannelMemberFlowRow[] | null>(null);
    inviteLoading = signal(false);
    inviteBusy = signal(false);
    inviteError = signal('');
    newLinkName = '';
    inviteQuery = signal('');
    inviteState = signal<InviteLinkState>('all');
    inviteSortKey = signal<InviteLinkSort>('name');
    inviteSortDirection = signal<SortDirection>('asc');

    visibleInviteLinks = computed(() => {
        const query = this.inviteQuery().trim().toLocaleLowerCase();
        const state = this.inviteState();
        const key = this.inviteSortKey();
        const direction = this.inviteSortDirection() === 'asc' ? 1 : -1;
        return this.inviteLinks()
            .filter(link => (!query || `${link.name} ${link.inviteLink}`.toLocaleLowerCase().includes(query))
                && (state === 'all' || (state === 'revoked') === Boolean(link.revokedAt)))
            .sort((a, b) => {
                const compared = key === 'name'
                    ? a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
                    : a[key] - b[key];
                return compared * direction || a.id.localeCompare(b.id);
            });
    });

    inviteFiltersActive = computed(() => Boolean(this.inviteQuery().trim()) || this.inviteState() !== 'all');

    sortInviteLinks(key: InviteLinkSort) {
        if (this.inviteSortKey() === key) this.inviteSortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
        else {
            this.inviteSortKey.set(key);
            this.inviteSortDirection.set(key === 'name' ? 'asc' : 'desc');
        }
        this.syncCollectionQuery();
    }

    clearInviteFilters() {
        this.inviteQuery.set('');
        this.inviteState.set('all');
        this.syncCollectionQuery();
    }

    setInviteQuery(value: string) {
        this.inviteQuery.set(value);
        this.syncCollectionQuery();
    }

    setInviteState(value: InviteLinkState) {
        this.inviteState.set(value);
        this.syncCollectionQuery();
    }

    /** The shelf renders once a Telegram source exists — chat_member only arrives where the bot is admin. */
    showInviteLinks = computed(() => this.channels().length > 0);

    async loadInviteLinks() {
        const id = this.inviteChannelId();
        if (!id) return;
        this.inviteLoading.set(true);
        this.inviteError.set('');
        this.channelsApi.memberFlow(id, MEMBER_FLOW_DAYS)
            .then(rows => { if (this.inviteChannelId() === id) this.memberFlow.set(rows); })
            .catch(() => { if (this.inviteChannelId() === id) this.memberFlow.set(null); });
        try {
            const res = await this.channelsApi.listInviteLinks(id);
            if (this.inviteChannelId() === id) {
                this.inviteLinks.set(res.links);
                this.inviteOrganic.set(res.organic);
            }
        } catch {
            // A failure leaves the empty state honest either way.
            this.inviteLinks.set([]);
            this.inviteOrganic.set(null);
        } finally {
            this.inviteLoading.set(false);
        }
    }

    pickInviteChannel(id: string) {
        this.inviteChannelId.set(id);
        this.inviteLinks.set([]);
        this.inviteOrganic.set(null);
        this.memberFlow.set(null);
        this.syncCollectionQuery();
        void this.loadInviteLinks();
    }

    // ─── Member flow (T-327) — the daily series the shelf used to sum away ────────────────────
    readonly memberFlowDays = MEMBER_FLOW_DAYS;

    /** Every day of the window, oldest first, links summed per day and quiet days filled with zeros. */
    flowDays = computed<FlowDay[] | null>(() => {
        const rows = this.memberFlow();
        if (!rows) return null;
        const byDay = new Map<string, { joins: number; leaves: number }>();
        for (const row of rows) {
            const key = row.day.slice(0, 10);
            const sum = byDay.get(key) ?? { joins: 0, leaves: 0 };
            sum.joins += row.joins;
            sum.leaves += row.leaves;
            byDay.set(key, sum);
        }
        const end = new Date();
        end.setUTCHours(0, 0, 0, 0);
        const days: FlowDay[] = [];
        for (let i = MEMBER_FLOW_DAYS - 1; i >= 0; i--) {
            const day = new Date(end.getTime() - i * 86_400_000).toISOString().slice(0, 10);
            const sum = byDay.get(day);
            days.push({ day, label: `${day.slice(5, 7)}/${day.slice(8, 10)}`, joins: sum?.joins ?? 0, leaves: sum?.leaves ?? 0 });
        }
        return days;
    });

    flowTotals = computed(() => {
        const days = this.flowDays() ?? [];
        return days.reduce((t, d) => ({ joins: t.joins + d.joins, leaves: t.leaves + d.leaves }), { joins: 0, leaves: 0 });
    });

    /** The tallest bar's scale — never below one, so a quiet window draws nothing rather than dividing by zero. */
    flowPeak = computed(() => Math.max(1, ...(this.flowDays() ?? []).map(d => Math.max(d.joins, d.leaves))));

    flowHasEvents = computed(() => (this.flowDays() ?? []).some(d => d.joins || d.leaves));

    flowPercent(value: number): number {
        return Math.round(value / this.flowPeak() * 100);
    }

    async createInviteLink() {
        const id = this.inviteChannelId();
        const name = this.newLinkName.trim();
        if (!id || !name || this.inviteBusy()) return;
        this.inviteBusy.set(true);
        this.inviteError.set('');
        try {
            await this.channelsApi.createInviteLink(id, name);
            // The create answers with the bare row — the listing carries the totals.
            await this.loadInviteLinks();
            this.newLinkName = '';
        } catch (e) {
            this.inviteError.set(httpErrorMessage(e, this.t().stats.inviteLinks.createFailed));
        } finally {
            this.inviteBusy.set(false);
        }
    }

    /** Revoked links stay in the table — their joins happened and the row keeps counting them. */
    async revokeInviteLink(link: ChannelInviteLink) {
        const id = this.inviteChannelId();
        if (!id || this.inviteBusy()) return;
        if (!await this.confirmation.confirm({ message: this.t().common.revokeConfirm, confirmLabel: this.t().common.confirm })) return;
        this.inviteBusy.set(true);
        this.inviteError.set('');
        try {
            await this.channelsApi.revokeInviteLink(id, link.id);
            this.inviteLinks.update(list => list.map(l =>
                l.id === link.id ? { ...l, revokedAt: new Date().toISOString() } : l));
        } catch (e) {
            this.inviteError.set(httpErrorMessage(e, this.t().stats.inviteLinks.revokeFailed));
        } finally {
            this.inviteBusy.set(false);
        }
    }

    async copyInviteLink(link: ChannelInviteLink) {
        try {
            await navigator.clipboard.writeText(link.inviteLink);
        } catch { /* the URL is visible in the row's tooltip either way */ }
    }

    private loadSeq = 0;

    /**
     * One request for every selected source over one aligned window (ADR-279): the server owns
     * the window, the carry-forward and the delta, so what arrives is drawn as it is. A failure
     * keeps the previous answer on the board; a stale answer overtaken by a newer request is
     * dropped rather than drawn.
     */
    private async load() {
        const seq = ++this.loadSeq;
        this.pending.set(true);
        try {
            const data = await this.channelsApi.series(this.rangeDays(), this.selectedIds());
            if (seq !== this.loadSeq) return;
            this.data.set(data);
            this.lastCurrent.update(map => {
                const next = new Map(map);
                for (const s of data.series) next.set(s.id, s.current);
                return next;
            });
            this.normalizeTableSort();
        } catch {
            // The board keeps what it had; `pending` clears below either way.
        } finally {
            if (seq === this.loadSeq) this.pending.set(false);
        }
    }

    // Selection reaches the server: a leaf toggling is one cheap request, and the window moves
    // with it on the server's side rather than in here.
    toggle(id: string) {
        const next = new Set(this.selected());
        if (!next.delete(id)) next.add(id);
        this.selected.set(next);
        this.normalizeTableSort();
        this.syncCollectionQuery();
        void this.load();
    }

    showAllSources() {
        this.selected.set(new Set(this.sources().map(source => source.id)));
        this.normalizeTableSort();
        this.syncCollectionQuery();
        void this.load();
    }

    setMetric(id: string) {
        this.metric.set(id as MetricKey);
        this.normalizeTableSort();
        this.syncCollectionQuery();
    }

    setView(id: string) {
        this.view.set(id as PanelView);
        this.syncCollectionQuery();
    }

    async onRangeCommit(raw: number) {
        const days = RANGE_NOTCHES.includes(raw) ? raw : 90;
        this.rangeDays.set(days);
        this.syncCollectionQuery();
        await this.load();
    }

    private restoreCollectionQuery() {
        const params = this.route.snapshot.queryParamMap;
        const metric = params.get('statsMetric');
        if (metric && ALL_METRICS.includes(metric as MetricKey)) this.metric.set(metric as MetricKey);
        const view = params.get('statsView');
        if (view === 'chart' || view === 'table') this.view.set(view);
        const range = Number(params.get('statsDays'));
        if (RANGE_NOTCHES.includes(range)) this.rangeDays.set(range);
        const sort = params.get('statsSort');
        if (sort) this.tableSortKey.set(sort);
        const direction = params.get('statsDir');
        if (direction === 'asc' || direction === 'desc') this.tableSortDirection.set(direction);

        this.inviteQuery.set(params.get('inviteQ') ?? '');
        const state = params.get('inviteState');
        if (state === 'active' || state === 'revoked') this.inviteState.set(state);
        const inviteSort = params.get('inviteSort');
        if (inviteSort && ['name', 'joins', 'leaves', 'net'].includes(inviteSort)) {
            this.inviteSortKey.set(inviteSort as InviteLinkSort);
            this.inviteSortDirection.set(inviteSort === 'name' ? 'asc' : 'desc');
        }
        const inviteDirection = params.get('inviteDir');
        if (inviteDirection === 'asc' || inviteDirection === 'desc')
            this.inviteSortDirection.set(inviteDirection);
    }

    private syncCollectionQuery() {
        const available = this.sources().map(source => source.id).sort();
        const selected = [...this.selected()].filter(id => available.includes(id)).sort();
        const sources = selected.length === available.length && selected.every((id, index) => id === available[index])
            ? null
            : selected.length ? selected.join(',') : 'none';
        const firstChannel = this.channels()[0]?.id;
        void this.router.navigate([], {
            relativeTo: this.route,
            replaceUrl: true,
            queryParamsHandling: 'merge',
            queryParams: {
                statsMetric: this.metric() === 'viewCount' ? null : this.metric(),
                statsView: this.view() === 'chart' ? null : this.view(),
                statsDays: this.rangeDays() === 90 ? null : this.rangeDays(),
                statsSources: sources,
                statsSort: this.tableSortKey() === 'period' ? null : this.tableSortKey(),
                statsDir: this.tableSortDirection() === 'asc' ? null : this.tableSortDirection(),
                inviteChannel: this.inviteChannelId() === firstChannel ? null : this.inviteChannelId() || null,
                inviteQ: this.inviteQuery().trim() || null,
                inviteState: this.inviteState() === 'all' ? null : this.inviteState(),
                inviteSort: this.inviteSortKey() === 'name' ? null : this.inviteSortKey(),
                inviteDir: this.inviteSortKey() === 'name' && this.inviteSortDirection() === 'asc'
                    ? null
                    : this.inviteSortDirection(),
            },
        });
    }

    rangeLabel(d = this.rangeDays()): string {
        return d % 30 === 0 && d >= 30 ? this.t().stats.months(d / 30) : this.t().stats.days(d);
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
