import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { GrowthChartComponent, GrowthReadout, GrowthSeries, seriesColor } from './growth-chart.component';

// The component's own stylesheet, read back out of the document — half of what this port owes is a
// CSS rule, and the only way to hold it to one is to read what shipped. The length assertion is the
// control: without it a renamed class would make every rule below pass over an empty string.
function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n')
        .replace(/\[_ngcontent-[^\]]+\]/g, '')
        .replace(/\[_nghost-[^\]]+\]/g, ':host')
        .replace(/(\[[a-z-]+)="([^"]*)"\]/g, '$1=$2]');
}

const TELEGRAM: GrowthSeries = { slot: 1, name: 'Telegram', points: [120, 180, 240, 300, 90], wash: true };
const BLOG: GrowthSeries = { slot: 3, name: 'Блог', points: [60, 66, 70, 88, 20] };

describe('GrowthChartComponent', () => {
    let fixture!: ComponentFixture<GrowthChartComponent>;
    const el = () => fixture.nativeElement as HTMLElement;
    const svg = () => el().querySelector('svg') as SVGSVGElement;
    const polylines = () => Array.from(el().querySelectorAll('polyline.gc-line'));
    const pointsOf = (line: Element) => (line.getAttribute('points') ?? '').trim().split(' ')
        .map(p => ({ x: Number(p.split(',')[0]), y: Number(p.split(',')[1]) }));
    const bodyRows = () => Array.from(el().querySelectorAll('tbody tr'));
    const cells = (row: Element) => Array.from(row.querySelectorAll('th, td')).map(c => c.textContent!.trim());

    function mount(series: readonly GrowthSeries[], open: boolean, extra: Record<string, unknown> = {}) {
        fixture?.destroy();
        fixture = TestBed.createComponent(GrowthChartComponent);
        fixture.componentRef.setInput('label', 'Просмотры по источникам');
        fixture.componentRef.setInput('series', series);
        fixture.componentRef.setInput('openTail', open);
        for (const [k, v] of Object.entries(extra)) fixture.componentRef.setInput(k, v);
        fixture.detectChanges();
    }

    beforeEach(() => {
        // The default mount is the honest one: five buckets, all of them closed.
        mount([TELEGRAM, BLOG], false);
    });

    describe('series mapping', () => {
        it('paints from the entity\'s slot, so a filter cannot repaint the survivors', () => {
            expect(fixture.componentInstance.lines().map(l => [l.slot, l.color]))
                .toEqual([[1, 'var(--series-1)'], [3, 'var(--series-3)']]);

            // Telegram deselected: Блог is still slot 3 and still the same ink.
            fixture.componentRef.setInput('series', [BLOG]);
            fixture.detectChanges();
            expect(fixture.componentInstance.lines().map(l => l.color)).toEqual(['var(--series-3)']);
        });

        it('hands the same mapping to the legend that it uses itself', () => {
            for (const slot of [1, 2, 3, 4, 5, 6] as const) expect(seriesColor(slot)).toBe(`var(--series-${slot})`);
        });

        it('reaches the DOM as the token, never as a painted literal (ADR-146)', () => {
            const inks = polylines().map(l => l.getAttribute('style') ?? '');
            expect(inks[0]).toContain('var(--series-1)');
            expect(inks[1]).toContain('var(--series-3)');
            for (const ink of [...inks, ...Array.from(el().querySelectorAll('circle, polygon')).map(n => n.getAttribute('style') ?? '')]) {
                expect(ink).not.toMatch(/#[0-9a-f]{3,8}\b/i);
                expect(ink).not.toMatch(/rgba?\(/);
            }
        });

        it('washes the series that asked for it, not whichever one is first', () => {
            expect(fixture.componentInstance.washes().map(w => w.slot)).toEqual([1]);

            fixture.componentRef.setInput('series', [BLOG, TELEGRAM]);
            fixture.detectChanges();
            expect(fixture.componentInstance.washes().map(w => w.slot)).toEqual([1]);
        });

        it('names every line it draws, because the palette cannot carry identity alone', () => {
            const labels = Array.from(el().querySelectorAll('text.gc-end')).map(t => t.textContent!.trim());
            expect(labels).toEqual(['Telegram', 'Блог']);
            // The dot beside the name is what wears the colour; the text wears a text token.
            const css = sheetFor('.gc-end');
            expect(css).toMatch(/\.gc-end[^{]*\{[^}]*fill:\s*var\(--t2\)/);
        });

        it('pushes two labels apart when two lines end together', () => {
            mount([{ slot: 1, name: 'A', points: [10, 100] }, { slot: 2, name: 'B', points: [10, 100] }], false);
            const [a, b] = fixture.componentInstance.endMarks();
            expect(a.y).toBe(b.y);
            expect(Math.abs(a.labelY - b.labelY)).toBeGreaterThanOrEqual(14);
        });
    });

    describe('the readout says what the series says', () => {
        it('reads its rows out of the data at that index', () => {
            fixture.componentRef.setInput('markerIndex', 3);
            fixture.detectChanges();

            const r = fixture.componentInstance.readout()!;
            expect(r.index).toBe(3);
            expect(r.rows).toEqual([
                { slot: 1, name: 'Telegram', value: 300 },
                { slot: 3, name: 'Блог', value: 88 },
            ]);
            expect(Array.from(el().querySelectorAll('text.gc-slip-value')).map(t => t.textContent!.trim()))
                .toEqual(['300', '88']);
        });

        // The prompt.md rule, enforced by construction: the slip's dots are placed by the same
        // projection as the line, so a row that disagreed with the curve would have to disagree
        // with itself. There is no input that could make them differ.
        it('puts its dots exactly on the curve it points at', () => {
            fixture.componentRef.setInput('markerIndex', 2);
            fixture.detectChanges();

            const onCurve = polylines().map(l => pointsOf(l)[2]);
            const dots = Array.from(el().querySelectorAll('circle.gc-dot'))
                .filter(c => Number(c.getAttribute('cx')) === onCurve[0].x)
                .map(c => Number(c.getAttribute('cy')));
            expect(dots).toEqual(onCurve.map(p => p.y));
        });

        it('emits the same rows it draws, and nothing while there is no active point', () => {
            const seen: (GrowthReadout | null)[] = [];
            fixture.componentInstance.readoutChange.subscribe(r => seen.push(r));

            el().dispatchEvent(new KeyboardEvent('keydown', { key: 'Home' }));
            fixture.detectChanges();
            expect(seen.at(-1)!.index).toBe(0);
            expect(seen.at(-1)!.rows.map(r => r.value)).toEqual([120, 60]);

            el().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
            fixture.detectChanges();
            expect(seen.at(-1)).toBeNull();
        });

        it('carries the same numbers into the live region and the table', () => {
            fixture.componentRef.setInput('markerIndex', 1);
            fixture.componentRef.setInput('xLabels', ['05.08', '06.08', '07.08', '08.08', '09.08']);
            fixture.detectChanges();

            expect(fixture.componentInstance.liveText()).toBe('06.08: Telegram 180, Блог 66');
            expect(cells(bodyRows()[1])).toEqual(['06.08', '180', '66']);
        });

        it('ignores a pinned index the data does not have', () => {
            fixture.componentRef.setInput('markerIndex', 99);
            fixture.detectChanges();
            expect(fixture.componentInstance.readout()).toBeNull();
            expect(el().querySelector('rect.gc-slip')).toBeNull();
        });
    });

    describe('an unfinished period is not plotted', () => {
        beforeEach(() => mount([TELEGRAM, BLOG], true));

        it('leaves the open bucket out of every drawn geometry', () => {
            expect(fixture.componentInstance.plotted()).toBe(4);
            for (const line of polylines()) expect(pointsOf(line).length).toBe(4);
            // 90 and 20 are the partial day; the axis is not scaled to them and no dot sits on them.
            expect(fixture.componentInstance.endMarks().map(m => m.slot)).toEqual([1, 3]);
            const lastOnCurve = pointsOf(polylines()[0])[3];
            expect(Array.from(el().querySelectorAll('circle.gc-dot')).map(c => Number(c.getAttribute('cx'))))
                .not.toContain(lastOnCurve.x + fixture.componentInstance.geom().step);
        });

        it('keeps the number in the table and says the bucket is open', () => {
            const rows = bodyRows();
            expect(rows.length).toBe(5);
            expect(cells(rows[4])).toEqual(['#5 (in progress)', '90', '20']);
        });

        it('does not let hover or the keyboard land on it', () => {
            el().dispatchEvent(new KeyboardEvent('keydown', { key: 'End' }));
            fixture.detectChanges();
            expect(fixture.componentInstance.readout()!.index).toBe(3);

            mount([TELEGRAM, BLOG], true, { markerIndex: 4 });
            expect(fixture.componentInstance.readout()).toBeNull();
        });

        it('scales the axis to the closed buckets only', () => {
            // The open bucket is a spike, so the two answers cannot be the same number: reaching
            // it would put the top at 4 000 and press every closed day onto the baseline.
            const SPIKE = [{ slot: 1 as const, name: 'Telegram', points: [120, 180, 240, 300, 4000] }];
            mount(SPIKE, true);
            expect(fixture.componentInstance.yGrid().at(-1)!.v).toBe(400);

            mount(SPIKE, false);
            expect(fixture.componentInstance.yGrid().at(-1)!.v).toBe(4000);
        });
    });

    describe('the ends of the range', () => {
        it('draws nothing and says so when there is no data', () => {
            mount([], false);
            expect(fixture.componentInstance.plotted()).toBe(0);
            expect(polylines().length).toBe(0);
            expect(fixture.componentInstance.endMarks().length).toBe(0);
            expect(fixture.componentInstance.readout()).toBeNull();
            expect(bodyRows()[0].textContent!.trim()).toBe('No data for this period');

            // A series that exists but carries no points is the same case, not a crash.
            mount([{ slot: 2, name: 'X', points: [] }], false);
            expect(fixture.componentInstance.plotted()).toBe(0);
            expect(svg()).toBeTruthy();
        });

        it('draws a single point as a point, since one point is not a line', () => {
            mount([{ slot: 2, name: 'X', points: [42] }], false);
            expect(polylines().length).toBe(0);
            expect(fixture.componentInstance.endMarks().map(m => m.text)).toEqual(['X']);
            expect(el().querySelectorAll('circle.gc-dot').length).toBe(1);
            expect(cells(bodyRows()[0])).toEqual(['#1', '42']);
        });

        it('a single point with an open tail is no point at all', () => {
            mount([{ slot: 2, name: 'X', points: [42] }], true);
            expect(fixture.componentInstance.plotted()).toBe(0);
            expect(el().querySelectorAll('circle.gc-dot').length).toBe(0);
            expect(bodyRows().length).toBe(1);
        });

        it('keeps a zero baseline and rounds the top to a readable step', () => {
            mount([{ slot: 1, name: 'A', points: [0, 931] }], false);
            const grid = fixture.componentInstance.yGrid();
            expect(grid[0].v).toBe(0);
            expect(grid[0].y).toBe(fixture.componentInstance.geom().y1);
            expect(grid.at(-1)!.v).toBe(1000);
            expect(grid.map(g => g.text)).toEqual(['0', '250', '500', '750', '1 000']);
        });

        it('drops an event tick the plotted range does not reach', () => {
            mount([TELEGRAM], true, { events: [{ index: 1, label: 'build' }, { index: 4, label: 'ghost' }] });
            expect(fixture.componentInstance.eventMarks().map(e => e.label)).toEqual(['build']);
        });
    });

    describe('it is data, not a picture of data', () => {
        it('keeps the whole series in the page and the picture out of the tree', () => {
            expect(svg().getAttribute('aria-hidden')).toBe('true');
            expect(el().querySelector('caption')!.textContent!.trim()).toBe('Просмотры по источникам');
            expect(Array.from(el().querySelectorAll('thead th')).map(t => t.textContent!.trim()))
                .toEqual(['Period', 'Telegram', 'Блог']);
            expect(bodyRows().map(r => cells(r)[1])).toEqual(['120', '180', '240', '300', '90']);
        });

        it('is one focus stop from which every point is reachable', () => {
            expect(el().getAttribute('tabindex')).toBe('0');
            expect(el().getAttribute('role')).toBe('group');
            expect(el().getAttribute('aria-label')).toBe('Просмотры по источникам');
            expect(el().querySelector(`#${el().getAttribute('aria-describedby')}`)).toBeTruthy();

            const visited: number[] = [];
            el().dispatchEvent(new KeyboardEvent('keydown', { key: 'Home' }));
            visited.push(fixture.componentInstance.readout()!.index);
            for (let i = 0; i < 6; i++) {
                el().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
                visited.push(fixture.componentInstance.readout()!.index);
            }
            expect(new Set(visited)).toEqual(new Set([0, 1, 2, 3, 4]));
        });
    });

    describe('the token contract, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { css = sheetFor('.gc-plot'); });

        it('takes the ruled ground by its token and never draws a rule of its own', () => {
            expect(css).toMatch(/background-image:\s*var\(--grid-graph\),\s*var\(--tex-paper\)/);
            expect(css).toMatch(/background-color:\s*var\(--sheet\)/);
            expect(css).not.toMatch(/repeating-linear-gradient/);
            expect(css).not.toMatch(/--grid-graph\s*:/);
        });

        it('rules the value grid solid, and spends dashing on the two things that mean something', () => {
            expect(css).toMatch(/\.gc-rule\s*\{[^}]*stroke:\s*var\(--rule-ink\)/);
            expect(css).not.toMatch(/\.gc-rule\s*\{[^}]*stroke-dasharray/);
            expect(css).not.toMatch(/\.gc-base\s*\{[^}]*stroke-dasharray/);
            expect(css).toMatch(/\.gc-dashed\s*\{[^}]*stroke-dasharray/);
            expect(css).toMatch(/\.gc-cross\s*\{[^}]*stroke-dasharray/);
        });

        it('sizes its numerals from the surface, so the density lint can score them', () => {
            expect(css).toMatch(/\[data-surface=chrome\][^,{]*\.gc-ax\b[\s\S]*?font-size:\s*var\(--text-chrome-sm\)/);
            expect(css).toMatch(/\[data-surface=chrome\][^,{]*\.gc-slip-title\s*\{[^}]*font-size:\s*var\(--text-chrome\)/);
            expect(css).toMatch(/font-family:\s*var\(--font-mono\)/);
            expect(el().getAttribute('data-surface')).toBe('chrome');
        });

        it('spends one box-shadow, and withholds it while focused so the global halo stands', () => {
            const shadowed = [...css.matchAll(/([^{}]+)\{([^{}]*box-shadow[^{}]*)\}/g)];
            expect(shadowed.length).toBe(1);
            expect(shadowed[0][1]).toContain(':not(:focus-visible)');
            expect(css).not.toMatch(/outline\s*:/);
        });

        it('paints no literal colour anywhere in its sheet', () => {
            expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
            expect(css).not.toMatch(/rgba?\(/);
            expect(css).not.toMatch(/--t3\b/);
        });
    });
});
