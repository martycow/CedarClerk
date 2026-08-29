import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, booleanAttribute, computed, effect, inject, input, isDevMode, output, signal } from '@angular/core';

/** A slot in the app's categorical palette. Six is the capacity; a seventh source folds into one. */
export type SeriesSlot = 1 | 2 | 3 | 4 | 5 | 6;

export interface GrowthSeries {
    /** The entity's fixed slot, which is what paints it. Never its position in the array (ADR-158). */
    slot: SeriesSlot;
    name: string;
    points: readonly number[];
    /** A pencilled comparison line — the previous period. */
    dashed?: boolean;
    /** A pale wash down to the baseline under this line. */
    wash?: boolean;
}

export interface GrowthEvent {
    index: number;
    label: string;
    anchor?: 'start' | 'middle' | 'end';
}

export interface GrowthReadoutRow {
    slot: SeriesSlot;
    name: string;
    value: number;
}

export interface GrowthReadout {
    index: number;
    label: string;
    rows: GrowthReadoutRow[];
}

export interface GrowthChartStrings {
    /** Header of the table alternative's first column. */
    period: string;
    empty: string;
    /** Suffix on the table row for a bucket that has not closed yet. */
    inProgress: string;
    hint: string;
}

const DEFAULT_STRINGS: GrowthChartStrings = {
    period: 'Period',
    empty: 'No data for this period',
    inProgress: 'in progress',
    hint: 'Arrow keys move the readout between points.',
};

/**
 * The one place a slot becomes a colour. The LeafTag strip above the chart is its legend and its
 * filter (ADR-149), so the swatch on the leaf and the ink of the line have to come from here or
 * they are two mappings that can disagree.
 */
export function seriesColor(slot: SeriesSlot): string {
    return `var(--series-${slot})`;
}

const PAD_L = 54, PAD_T = 16, PAD_R = 16;
const PAD_B = 32, PAD_B_EVENTS = 60;
// 11px mono, rounded up. Reserving the gutter from an estimate rather than a measurement is
// deliberate — see ADR-158's consequence.
const CH = 7;
const NAME_CAP = 12;
const LABEL_GAP = 14;

const group = (n: number) => String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

// The 2.5 rung is dropped below a decade so that a small range cannot produce a fractional tick:
// counts are whole, and an axis reading 2.5 is a number the data can never take.
function niceStep(raw: number): number {
    const exp = Math.pow(10, Math.floor(Math.log10(raw)));
    const f = raw / exp;
    const ladder = exp >= 10 ? [1, 2, 2.5, 5, 10] : [1, 2, 5, 10];
    return Math.max(1, (ladder.find(c => f <= c + 1e-9) ?? 10) * exp);
}

const clip = (name: string) => (name.length > NAME_CAP ? name.slice(0, NAME_CAP - 1) + '…' : name);

interface Geom {
    w: number; h: number;
    x0: number; x1: number; y0: number; y1: number;
    n: number; max: number; step: number;
}

const xAt = (g: Geom, i: number) => g.x0 + i * g.step;
const yAt = (g: Geom, v: number) => g.y1 - (Math.min(Math.max(v, 0), g.max) / g.max) * (g.y1 - g.y0);

let nextId = 0;

// A page of the log book on graph paper: pen lines over the ruled ground, brass pencil ticks on
// the axis for events, and a slightly-turned paper slip for the readout. What it may not do is
// claim a number the curve does not draw — the slip is read out of the series, and a period whose
// last bucket is still open is not plotted at all (ADR-158).
@Component({
    selector: 'app-growth-chart',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        // An instrument, not a reading surface: mono numerals at chrome's size, on paper's ground.
        'data-surface': 'chrome',
        tabindex: '0',
        role: 'group',
        '[attr.aria-label]': 'label()',
        '[attr.aria-describedby]': 'hintId',
        '[class.is-pending]': 'pending()',
        '(pointermove)': 'onPointerMove($event)',
        '(pointerleave)': 'clearHover()',
        '(blur)': 'clearHover()',
        '(keydown)': 'onKeydown($event)',
    },
    template: `
        <svg class="gc-plot" aria-hidden="true" focusable="false"
             [attr.width]="geom().w" [attr.height]="geom().h"
             [attr.viewBox]="'0 0 ' + geom().w + ' ' + geom().h">
            @for (g of yGrid(); track g.v) {
                <line class="gc-rule" [class.gc-base]="g.base"
                      [attr.x1]="geom().x0" [attr.y1]="g.y" [attr.x2]="geom().x1" [attr.y2]="g.y" />
                <text class="gc-ax" text-anchor="end" [attr.x]="geom().x0 - 8" [attr.y]="g.y + 4">{{ g.text }}</text>
            }

            @for (a of washes(); track a.slot) {
                <polygon class="gc-wash" [attr.points]="a.points" [style.fill]="a.color" />
            }

            @for (l of lines(); track l.slot) {
                <polyline class="gc-line" [class.gc-dashed]="l.dashed"
                          [attr.points]="l.points" [style.stroke]="l.color" />
            }

            @for (e of endMarks(); track e.slot) {
                <circle class="gc-dot" [attr.cx]="e.x" [attr.cy]="e.y" r="4" [style.fill]="e.color" />
                <text class="gc-end" [attr.x]="e.x + 10" [attr.y]="e.labelY">{{ e.text }}</text>
            }

            @for (t of xTicks(); track t.i) {
                <text class="gc-ax" text-anchor="middle" [attr.x]="t.x" [attr.y]="geom().y1 + 18">{{ t.text }}</text>
            }

            @for (ev of eventMarks(); track $index) {
                <line class="gc-tick" [attr.x1]="ev.x" [attr.y1]="geom().y1 - 4" [attr.x2]="ev.x" [attr.y2]="geom().y1 + 4" />
                <text class="gc-ev" [attr.x]="ev.x" [attr.y]="geom().y1 + 40" [attr.text-anchor]="ev.anchor">{{ ev.label }}</text>
            }

            @for (m of markerMarks(); track m.i) {
                <line class="gc-tick" [attr.x1]="m.x" [attr.y1]="geom().y1 - 4" [attr.x2]="m.x" [attr.y2]="geom().y1 + 4" />
                <circle class="gc-mark" [attr.cx]="m.x" [attr.cy]="geom().y1" r="2.5" />
            }

            @if (slip(); as s) {
                <line class="gc-cross" [attr.x1]="s.x" [attr.y1]="geom().y0" [attr.x2]="s.x" [attr.y2]="geom().y1" />
                @for (r of s.rows; track r.slot) {
                    <circle class="gc-dot" [attr.cx]="s.x" [attr.cy]="r.y" r="4" [style.fill]="r.color" />
                }
                <g [attr.transform]="'rotate(-0.7 ' + s.bx + ' ' + s.by + ')'">
                    <rect class="gc-slip" rx="2" [attr.x]="s.bx" [attr.y]="s.by" [attr.width]="s.bw" [attr.height]="s.bh" />
                    <text class="gc-slip-title" [attr.x]="s.bx + 12" [attr.y]="s.by + 20">{{ s.title }}</text>
                    @for (r of s.rows; track r.slot) {
                        <rect class="gc-swatch" rx="1" width="8" height="8"
                              [attr.x]="s.bx + 12" [attr.y]="r.rowY - 8" [style.fill]="r.color" />
                        <text class="gc-slip-name" [attr.x]="s.bx + 26" [attr.y]="r.rowY">{{ r.name }}</text>
                        <text class="gc-slip-value" text-anchor="end" [attr.x]="s.bx + s.bw - 12" [attr.y]="r.rowY">{{ r.text }}</text>
                    }
                </g>
            }
        </svg>

        <p class="gc-off" [id]="hintId">{{ strings().hint }}</p>
        <p class="gc-off" aria-live="polite">{{ liveText() }}</p>

        <table class="gc-off">
            <caption>{{ label() }}</caption>
            <thead>
                <tr>
                    <th scope="col">{{ strings().period }}</th>
                    @for (s of series(); track s.slot) { <th scope="col">{{ s.name }}</th> }
                </tr>
            </thead>
            <tbody>
                @for (row of tableRows(); track row.i) {
                    <tr>
                        <th scope="row">{{ row.label }}</th>
                        @for (v of row.values; track $index) { <td>{{ v }}</td> }
                    </tr>
                } @empty {
                    <tr><td [attr.colspan]="series().length + 1">{{ strings().empty }}</td></tr>
                }
            </tbody>
        </table>
    `,
    styles: [`
        :host {
            position: relative;
            display: block;
            min-width: 0;
            box-sizing: border-box;
            overflow: hidden;
            cursor: crosshair;
            /* The ruled ground is the token the checker scores ink over. A rule drawn here instead
               would be a surface nothing measures. */
            background-color: var(--sheet);
            background-image: var(--grid-graph), var(--tex-paper);
        }

        /* ADR-140 spends the app's one box-shadow on the focus halo, and a second on the same
           element replaces it rather than joining it. */
        :host(:not(:focus-visible)) { box-shadow: var(--shadow-sheet-inset); }

        /* A refetch holds the previous render instead of flashing a skeleton — no layout jump. */
        :host(.is-pending) .gc-plot { opacity: .6; }

        .gc-plot { display: block; }

        .gc-rule { stroke: var(--rule-ink); stroke-width: 1; }
        .gc-base { stroke-width: 2; }
        .gc-line { fill: none; stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
        .gc-dashed { stroke-dasharray: 5 4; }
        .gc-wash { fill-opacity: .12; }
        /* The ring is the ground the dots lie on, so two lines crossing stay two lines. */
        .gc-dot { stroke: var(--surface); stroke-width: 2; }
        .gc-tick { stroke: var(--brass-ink, var(--brass-lo)); stroke-width: 1; }
        .gc-mark { fill: var(--brass-ink, var(--brass-lo)); }
        .gc-cross { stroke: var(--t2); stroke-width: 1; stroke-dasharray: 3 3; }
        .gc-slip { fill: var(--paper-bright); stroke: var(--paper-edge); }

        .gc-ax, .gc-end, .gc-slip-name { fill: var(--t2); }
        .gc-slip-title, .gc-slip-value { fill: var(--text); }
        .gc-ev { fill: var(--brass-ink, var(--brass-lo)); font-weight: 700; letter-spacing: .04em; }

        .gc-ax, .gc-end, .gc-slip-name { font-family: var(--font-mono); }
        /* The slip is the chart's instrument reading and the one text here with a stated 11px,
           so it is the one that takes the readout face (ADR-180 clause 2). The axis and the end
           labels carry no size of their own — they inherit whatever the page hands the SVG — and
           a pixel face at an unknown size is exactly what that clause refuses. */
        .gc-slip-value { font-family: var(--font-readout); }
        .gc-slip-title, .gc-ev { font-family: var(--font-sans); }
        .gc-slip-title { font-weight: 700; }

        /* The surface owns the sizes, so the lint can score them (ADR-138). */
        :host([data-surface="chrome"]) .gc-ax,
        :host([data-surface="chrome"]) .gc-end,
        :host([data-surface="chrome"]) .gc-ev,
        :host([data-surface="chrome"]) .gc-slip-name,
        :host([data-surface="chrome"]) .gc-slip-value { font-size: var(--text-chrome-sm); }
        :host([data-surface="chrome"]) .gc-slip-title { font-size: var(--text-chrome); }

        /* The numbers are in the page, not only in the picture. */
        .gc-off {
            position: absolute;
            width: 1px;
            height: 1px;
            margin: -1px;
            padding: 0;
            border: 0;
            overflow: hidden;
            white-space: nowrap;
            clip-path: inset(50%);
        }
    `],
})
export class GrowthChartComponent implements OnDestroy {
    /** The chart's accessible name, and the caption of its table alternative. */
    readonly label = input.required<string>();
    readonly series = input.required<readonly GrowthSeries[]>();
    /**
     * Does the last point cover a period that has not finished yet? There is no default: a partial
     * bucket plots low and reads as a collapse, and a full one silently deleted is the same lie
     * mirrored, so the call site answers (ADR-158).
     */
    readonly openTail = input.required<boolean, unknown>({ transform: booleanAttribute });
    /** One label per point; '' skips a tick. */
    readonly xLabels = input<readonly string[]>([]);
    /** Pencil marks on the axis: a build cut, a post published. */
    readonly events = input<readonly GrowthEvent[]>([]);
    /**
     * Wave 2 item 13 — unlabelled publish-event markers: a brass tick and dot at these point
     * indexes. Lighter than `events` (no caption, no reserved bottom band); an absent input
     * renders exactly as before it existed.
     */
    readonly markers = input<readonly number[]>([]);
    /** A readout pinned by the page. Hover and the arrow keys override it while they are active. */
    readonly markerIndex = input<number | null>(null);
    /** Y-axis top; null rounds up from the data. */
    readonly max = input<number | null>(null);
    readonly yTicks = input(4);
    /** Fallbacks only — the rendered box is measured off the host where the platform allows it. */
    readonly width = input(856);
    readonly height = input(430);
    /** Hold the previous render at reduced opacity while the next slice is fetched. */
    readonly pending = input(false, { transform: booleanAttribute });
    readonly strings = input<GrowthChartStrings>(DEFAULT_STRINGS);

    /** The active point, with the values read out of the series at that index. */
    readonly readoutChange = output<GrowthReadout | null>();

    readonly hintId = `gc-hint-${nextId++}`;

    private readonly host = inject(ElementRef).nativeElement as HTMLElement;
    private readonly box = signal<{ w: number; h: number } | null>(null);
    private readonly hover = signal<number | null>(null);
    private readonly observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => this.measure());

    /** Points the caller handed over, before the tail rule. */
    private readonly rawCount = computed(() => {
        const lens = this.series().map(s => s.points.length);
        return lens.length ? Math.min(...lens) : 0;
    });

    /** Points that may be drawn: an open tail is not one of them. */
    readonly plotted = computed(() => Math.max(0, this.rawCount() - (this.openTail() ? 1 : 0)));

    readonly geom = computed<Geom>(() => {
        const measured = this.box();
        const w = measured?.w || this.width();
        const h = measured?.h || this.height();
        const n = this.plotted();
        const names = this.series().map(s => clip(s.name).length);
        const gutter = n > 0 && names.length ? Math.min(Math.ceil(Math.max(...names) * CH) + 16, Math.round(w / 3)) : 0;
        const x0 = PAD_L, x1 = w - PAD_R - gutter;
        const y0 = PAD_T, y1 = h - (this.events().length ? PAD_B_EVENTS : PAD_B);
        return { w, h, x0, x1, y0, y1, n, max: this.axisMax(), step: n > 1 ? (x1 - x0) / (n - 1) : 0 };
    });

    private readonly axisMax = computed(() => {
        const fixed = this.max();
        if (fixed && fixed > 0) return fixed;
        const n = this.plotted(), ticks = Math.max(1, this.yTicks());
        let top = 0;
        for (const s of this.series()) for (let i = 0; i < n; i++) top = Math.max(top, s.points[i]);
        if (top <= 0) return ticks;
        return niceStep(top / ticks) * ticks;
    });

    readonly yGrid = computed(() => {
        const g = this.geom(), ticks = Math.max(1, this.yTicks());
        return Array.from({ length: ticks + 1 }, (_, i) => {
            const v = (g.max / ticks) * i;
            return { v, y: yAt(g, v), text: group(v), base: i === 0 };
        });
    });

    private readonly drawn = computed(() => {
        const g = this.geom();
        if (!g.n) return [];
        return this.series().map(s => ({
            slot: s.slot,
            name: s.name,
            dashed: !!s.dashed,
            wash: !!s.wash,
            color: seriesColor(s.slot),
            xy: Array.from({ length: g.n }, (_, i) => ({ x: xAt(g, i), y: yAt(g, s.points[i]) })),
        }));
    });

    readonly lines = computed(() => this.drawn()
        .filter(s => s.xy.length > 1)
        .map(s => ({ slot: s.slot, dashed: s.dashed, color: s.color, points: s.xy.map(p => `${p.x},${p.y}`).join(' ') })));

    readonly washes = computed(() => {
        const g = this.geom();
        return this.drawn().filter(s => s.wash && s.xy.length > 1).map(s => ({
            slot: s.slot,
            color: s.color,
            points: `${s.xy.map(p => `${p.x},${p.y}`).join(' ')} ${s.xy[s.xy.length - 1].x},${g.y1} ${s.xy[0].x},${g.y1}`,
        }));
    });

    /**
     * The line's own name at its end, beside its own dot. The palette fails the CVD and the
     * normal-vision separation checks in both themes (ADR-158), so this is the encoding that
     * carries identity, not a decoration — and the dot is what wears the colour, never the text.
     */
    readonly endMarks = computed(() => {
        const g = this.geom();
        const marks = this.drawn().map(s => {
            const last = s.xy[s.xy.length - 1];
            return { slot: s.slot, color: s.color, x: last.x, y: last.y, labelY: last.y + 4, text: clip(s.name) };
        });
        const order = [...marks].sort((a, b) => a.labelY - b.labelY);
        for (let i = 1; i < order.length; i++) {
            order[i].labelY = Math.max(order[i].labelY, order[i - 1].labelY + LABEL_GAP);
        }
        const floor = g.y1;
        for (let i = order.length - 1; i >= 0; i--) {
            order[i].labelY = Math.min(order[i].labelY, floor - (order.length - 1 - i) * LABEL_GAP);
        }
        return marks;
    });

    // Thinned here and not in the labels handed over: the table rows and the slip title read every
    // entry, and only the axis runs out of room. Seven ticks is the kit's cadence.
    readonly xTicks = computed(() => {
        const g = this.geom(), labels = this.xLabels();
        const widest = Math.max(0, ...labels.slice(0, g.n).map(l => l.length)) * CH + LABEL_GAP;
        const fit = g.step > 0 ? Math.ceil(widest / g.step) : 1;
        const every = Math.max(1, fit, Math.ceil(g.n / 7));
        const out: { i: number; x: number; text: string }[] = [];
        for (let i = 0; i < g.n && i < labels.length; i += every) {
            if (labels[i]) out.push({ i, x: xAt(g, i), text: labels[i] });
        }
        return out;
    });

    readonly eventMarks = computed(() => {
        const g = this.geom();
        return this.events()
            .filter(e => e.index >= 0 && e.index < g.n)
            .map(e => ({ x: xAt(g, e.index), label: e.label, anchor: e.anchor ?? 'middle' }));
    });

    readonly markerMarks = computed(() => {
        const g = this.geom();
        return [...new Set(this.markers())]
            .filter(i => i >= 0 && i < g.n)
            .map(i => ({ i, x: xAt(g, i) }));
    });

    readonly activeIndex = computed(() => {
        const n = this.plotted();
        if (!n) return null;
        const i = this.hover() ?? this.markerIndex();
        return i === null || i < 0 || i >= n ? null : i;
    });

    /**
     * The one place a readout is built. Rows are read out of the series at the index, so the slip,
     * the crosshair dots, the live region and the emitted event cannot disagree with the curve —
     * there is no input through which a caller could state otherwise (ADR-158).
     */
    readonly readout = computed<GrowthReadout | null>(() => {
        const i = this.activeIndex();
        if (i === null) return null;
        return {
            index: i,
            label: this.xLabels()[i] || `#${i + 1}`,
            rows: this.series().map(s => ({ slot: s.slot, name: s.name, value: s.points[i] })),
        };
    });

    readonly slip = computed(() => {
        const r = this.readout();
        if (!r) return null;
        const g = this.geom();
        const x = xAt(g, r.index);
        const widest = Math.max(r.label.length + 2, ...r.rows.map(row => clip(row.name).length + group(row.value).length + 4));
        const bw = Math.min(Math.max(widest * CH + 40, 150), 280);
        const bh = 28 + r.rows.length * 18;
        const bx = Math.min(Math.max(x > (g.x0 + g.x1) / 2 ? x - bw - 14 : x + 14, 4), Math.max(4, g.w - bw - 4));
        const by = Math.min(g.y0 + 8, Math.max(g.y0, g.y1 - bh));
        return {
            x, bx, by, bw, bh,
            title: r.label,
            rows: r.rows.map((row, i) => ({
                slot: row.slot,
                color: seriesColor(row.slot),
                name: clip(row.name),
                text: group(row.value),
                y: yAt(g, row.value),
                rowY: by + 38 + i * 18,
            })),
        };
    });

    readonly liveText = computed(() => {
        const r = this.readout();
        return r ? `${r.label}: ${r.rows.map(row => `${row.name} ${group(row.value)}`).join(', ')}` : '';
    });

    /** Every point, the open tail included and said to be open — the numbers are never hidden. */
    readonly tableRows = computed(() => {
        const total = this.rawCount(), open = this.openTail(), labels = this.xLabels(), s = this.strings();
        return Array.from({ length: total }, (_, i) => ({
            i,
            label: (labels[i] || `#${i + 1}`) + (open && i === total - 1 ? ` (${s.inProgress})` : ''),
            values: this.series().map(series => group(series.points[i])),
        }));
    });

    constructor() {
        this.observer?.observe(this.host);
        effect(() => {
            if (!isDevMode()) return;
            const list = this.series();
            const slots = new Set(list.map(s => s.slot));
            if (slots.size !== list.length)
                console.warn('app-growth-chart: two series share a slot — one entity, one colour');
            const lens = new Set(list.map(s => s.points.length));
            if (lens.size > 1)
                console.warn('app-growth-chart: series of unequal length — drawing the shortest');
        });
    }

    ngOnDestroy(): void {
        this.observer?.disconnect();
    }

    private measure(): void {
        const w = this.host.clientWidth, h = this.host.clientHeight;
        const now = this.box();
        if (w > 0 && h > 0 && (now?.w !== w || now?.h !== h)) this.box.set({ w, h });
    }

    protected onPointerMove(event: PointerEvent): void {
        const g = this.geom();
        if (!g.n) return;
        const x = event.clientX - this.host.getBoundingClientRect().left;
        const i = g.step ? Math.round((x - g.x0) / g.step) : 0;
        this.setHover(Math.min(Math.max(i, 0), g.n - 1));
    }

    protected clearHover(): void {
        if (this.hover() === null) return;
        this.hover.set(null);
        this.readoutChange.emit(this.readout());
    }

    protected onKeydown(event: KeyboardEvent): void {
        const n = this.plotted();
        if (!n) return;
        const from = this.activeIndex() ?? n - 1;
        let next: number | null = null;
        switch (event.key) {
            case 'ArrowLeft': case 'ArrowDown': next = Math.max(0, from - 1); break;
            case 'ArrowRight': case 'ArrowUp': next = Math.min(n - 1, from + 1); break;
            case 'Home': next = 0; break;
            case 'End': next = n - 1; break;
            case 'Escape': this.clearHover(); return;
            default: return;
        }
        event.preventDefault();
        this.setHover(next);
    }

    private setHover(i: number): void {
        if (this.hover() === i) return;
        this.hover.set(i);
        this.readoutChange.emit(this.readout());
    }
}
