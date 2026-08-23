import { describe, expect, it } from 'vitest';
import { groupCreditLedger } from './credit-ledger';

describe('credit ledger grouping', () => {
    it('collapses consecutive parts of one publishing run', () => {
        const groups = groupCreditLedger([
            { delta: -1, reason: 'x-post', createdAt: '2026-08-23T12:04:00Z' },
            { delta: -1, reason: 'x-post', createdAt: '2026-08-23T12:02:00Z' },
            { delta: -1, reason: 'x-post', createdAt: '2026-08-23T12:00:00Z' },
        ]);

        expect(groups).toEqual([{
            delta: -3, reason: 'x-post', createdAt: '2026-08-23T12:04:00Z', count: 3,
        }]);
    });

    it('keeps separate activity outside the publishing window', () => {
        const groups = groupCreditLedger([
            { delta: -1, reason: 'x-post', createdAt: '2026-08-23T12:10:01Z' },
            { delta: -1, reason: 'x-post', createdAt: '2026-08-23T12:05:00Z' },
        ]);

        expect(groups).toHaveLength(2);
    });

    it('never merges opposite movements', () => {
        const groups = groupCreditLedger([
            { delta: 10, reason: 'adjustment', createdAt: '2026-08-23T12:01:00Z' },
            { delta: -2, reason: 'adjustment', createdAt: '2026-08-23T12:00:00Z' },
        ]);

        expect(groups).toHaveLength(2);
    });
});
