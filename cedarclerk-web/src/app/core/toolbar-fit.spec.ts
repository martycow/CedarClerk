import { ToolbarMeasurements, fitToolbar } from './toolbar-fit';

// Seven groups, more than the catalogue ships, so a fit that only works for two does not pass here.
const SEVEN = [100, 100, 100, 100, 100, 100, 100];

function measure(over: Partial<ToolbarMeasurements> = {}): ToolbarMeasurements {
    return {
        available: 1000,
        gap: 0,
        lead: 100,
        trail: 100,
        withCaptions: SEVEN,
        withoutCaptions: SEVEN,
        ...over,
    };
}

describe('fitToolbar', () => {
    it('keeps one row with its captions when everything fits', () => {
        // 100 + 7 x 100 + 100 = 900 in 1000.
        expect(fitToolbar(measure())).toEqual({ rows: 1, captions: true, firstRow: 7 });
    });

    it('counts the gap between every pair, so a fit is not one gap too optimistic', () => {
        // Nine items, eight gaps: 900 + 8 x 12 = 996 fits, 900 + 8 x 13 = 1004 does not.
        expect(fitToolbar(measure({ gap: 12 })).rows).toBe(1);
        expect(fitToolbar(measure({ gap: 13 })).rows).toBe(2);
    });

    it('gives up the captions before it wraps (ADR-150 clause 2)', () => {
        // Lead + groups + trail is 930 with the captions and 900 without them.
        const fit = fitToolbar(measure({
            available: 900,
            withCaptions: [100, 100, 100, 100, 100, 100, 130],
            withoutCaptions: SEVEN,
        }));
        expect(fit).toEqual({ rows: 1, captions: false, firstRow: 7 });
    });

    it('wraps rather than dropping a group, and keeps the captions when two rows hold them', () => {
        const fit = fitToolbar(measure({ available: 600 }));
        expect(fit.rows).toBe(2);
        expect(fit.captions).toBe(true);
        // Lead + four groups + trail is 600 exactly; a fifth would be 700.
        expect(fit.firstRow).toBe(4);
    });

    it('drops the captions on the second rung of two rows before it gives up', () => {
        // With captions the tail row is 3 x 160 = 480 and does not fit; without, 3 x 100 = 300.
        const fit = fitToolbar(measure({
            available: 460,
            withCaptions: [100, 100, 100, 100, 160, 160, 160],
            withoutCaptions: SEVEN,
        }));
        expect(fit).toEqual({ rows: 2, captions: false, firstRow: 2 });
    });

    it('returns the last rung rather than a fit it cannot make, so the strip scrolls', () => {
        // One group alone is wider than the strip: no arrangement holds, and the answer must still
        // be an arrangement.
        const fit = fitToolbar(measure({ available: 120 }));
        expect(fit).toEqual({ rows: 2, captions: false, firstRow: 0 });
    });

    it('never leaves the second row empty — that arrangement is the rung above it', () => {
        for (const available of [1000, 890, 600, 460, 300, 120]) {
            const fit = fitToolbar(measure({ available }));
            if (fit.rows === 2 && available > 120) expect(fit.firstRow).toBeLessThan(7);
        }
    });

    it('is decided by width alone — the same widths give the same answer whatever came before', () => {
        const a = fitToolbar(measure({ available: 600 }));
        const b = fitToolbar(measure({ available: 600 }));
        expect(a).toEqual(b);
    });

    it('holds the pinned head and tail on the first row, never counting them against the second', () => {
        // A trail wide enough to take the whole first row leaves every group on the second.
        const fit = fitToolbar(measure({ available: 300, lead: 100, trail: 200 }));
        expect(fit.firstRow).toBe(0);
        expect(fit.rows).toBe(2);
    });

    it('handles an empty catalogue without claiming a second row', () => {
        const fit = fitToolbar(measure({ withCaptions: [], withoutCaptions: [] }));
        expect(fit).toEqual({ rows: 1, captions: true, firstRow: 0 });
    });
});
