import { pseudoProgress } from './pseudo-progress.util';

describe('pseudoProgress', () => {
    it('returns 0 at t=0', () => {
        expect(pseudoProgress(0)).toBe(0);
    });

    it('returns 0 for negative elapsed time', () => {
        expect(pseudoProgress(-5)).toBe(0);
    });

    it('grows monotonically over time', () => {
        const p1 = pseudoProgress(5);
        const p2 = pseudoProgress(20);
        const p3 = pseudoProgress(60);
        expect(p2).toBeGreaterThan(p1);
        expect(p3).toBeGreaterThan(p2);
    });

    it('never exceeds the 90% cap, even after a very long time', () => {
        expect(pseudoProgress(10_000)).toBeLessThanOrEqual(90);
    });

    it('is roughly two-thirds of the cap after one time constant (tau)', () => {
        // 90 * (1 - e^-1) ~= 56.9
        expect(pseudoProgress(20, 20)).toBe(57);
    });
});
