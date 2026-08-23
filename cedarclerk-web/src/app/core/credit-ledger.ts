import type { CreditLedgerEntry } from './billing.service';

export interface CreditLedgerGroup extends CreditLedgerEntry { count: number }

const PUBLISH_RUN_WINDOW_MS = 5 * 60_000;

/** Consecutive charges inside one short publishing run read as one action rather than one row per part. */
export function groupCreditLedger(entries: readonly CreditLedgerEntry[]): CreditLedgerGroup[] {
    const groups: CreditLedgerGroup[] = [];
    for (const entry of entries) {
        const previous = groups.at(-1);
        const near = previous
            && Math.abs(new Date(previous.createdAt).getTime() - new Date(entry.createdAt).getTime())
                <= PUBLISH_RUN_WINDOW_MS;
        if (previous && near && previous.reason === entry.reason
            && Math.sign(previous.delta) === Math.sign(entry.delta)) {
            previous.delta += entry.delta;
            previous.count++;
        } else {
            groups.push({ ...entry, count: 1 });
        }
    }
    return groups;
}
