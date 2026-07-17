import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { ChannelsService, Channel, ChannelStats, ChannelStatSnapshotDto } from '../core/channels.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { LucideArrowLeft as ArrowLeft } from '@lucide/angular';

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

const CHART_WIDTH = 600;
const CHART_HEIGHT = 160;
const PAD_X = 4;
const PAD_TOP = 12;
const PAD_BOTTOM = 4;

@Component({
    selector: 'app-stats',
    imports: [DatePipe, RouterLink, CedarLogoComponent, ArrowLeft],
    templateUrl: 'stats.component.html',
    styleUrls: ['stats.component.css'],
})
export class StatsComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    private channelsApi = inject(ChannelsService);

    loading = signal(true);
    channels = signal<Channel[]>([]);
    selectedChannelId = signal<string | null>(null);
    stats = signal<ChannelStats | null>(null);
    rangeDays = signal(90);

    hover = signal<{ key: MetricKey; index: number } | null>(null);

    metricCards = computed<MetricCard[]>(() => {
        const s = this.stats();
        if (!s) return [];
        const base: { key: MetricKey; label: string; current: number | null; delta: number | null }[] = [
            { key: 'memberCount', label: 'Subscribers', current: s.current, delta: s.deltaWeek },
            { key: 'viewCount', label: 'Views', current: s.currentViews, delta: s.deltaWeekViews },
            { key: 'likeCount', label: 'Likes', current: s.currentLikes, delta: s.deltaWeekLikes },
            { key: 'commentCount', label: 'Comments', current: s.currentComments, delta: s.deltaWeekComments },
        ];
        return base.map(m => ({ ...m, chart: this.buildChart(s.snapshots, m.key) }));
    });

    async ngOnInit() {
        this.loading.set(true);
        try {
            const channels = await this.channelsApi.list();
            this.channels.set(channels);
            if (channels.length > 0) {
                await this.selectChannel(channels[0].id);
            }
        } finally {
            this.loading.set(false);
        }
    }

    async selectChannel(id: string) {
        this.selectedChannelId.set(id);
        this.hover.set(null);
        this.stats.set(await this.channelsApi.getStats(id, this.rangeDays()));
    }

    async selectRange(days: number) {
        this.rangeDays.set(days);
        const id = this.selectedChannelId();
        if (!id) return;
        this.hover.set(null);
        this.stats.set(await this.channelsApi.getStats(id, days));
    }

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
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

    private buildChart(snapshots: ChannelStatSnapshotDto[], key: MetricKey): ChartLayout {
        if (snapshots.length < 2) {
            return { hasData: false, linePath: '', areaPath: '', points: [], ticks: [] };
        }

        const innerW = CHART_WIDTH - PAD_X * 2;
        const innerH = CHART_HEIGHT - PAD_TOP - PAD_BOTTOM;
        const values = snapshots.map(s => s[key]);
        const top = this.niceMax(Math.max(...values, 1));

        const points: ChartPoint[] = snapshots.map((s, i) => ({
            x: PAD_X + (i / (snapshots.length - 1)) * innerW,
            y: PAD_TOP + innerH - (s[key] / top) * innerH,
            value: s[key],
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
