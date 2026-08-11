import { describe, expect, it } from 'vitest';
import { formatInZone, toInstant, zoneAbbreviation } from './display-time';

// ADR-115. The two sides of a daylight-saving change are the point: a fixed -8 would be an hour
// wrong from March to November, which is most of the year.
describe('display-time', () => {
    it('shows a summer instant in PDT', () => {
        expect(formatInZone('2026-08-11T21:05:00Z', 'd MMM yyyy, HH:mm')).toBe('11 Aug 2026, 14:05');
        expect(zoneAbbreviation('2026-08-11T21:05:00Z')).toBe('PDT');
    });

    it('shows a winter instant in PST', () => {
        expect(formatInZone('2026-01-15T21:05:00Z', 'd MMM yyyy, HH:mm')).toBe('15 Jan 2026, 13:05');
        expect(zoneAbbreviation('2026-01-15T21:05:00Z')).toBe('PST');
    });

    it('reads a timestamp with no offset as UTC, not as local time', () => {
        // What the server used to send, and the reason times were seven hours out.
        expect(formatInZone('2026-08-11T21:05:00', 'd MMM yyyy, HH:mm')).toBe('11 Aug 2026, 14:05');
        expect(toInstant('2026-08-11T21:05:00')!.toISOString()).toBe('2026-08-11T21:05:00.000Z');
    });

    it('moves the date, not only the clock, when the zone crosses midnight', () => {
        expect(formatInZone('2026-08-12T03:00:00Z', 'd MMM yyyy')).toBe('11 Aug 2026');
    });

    it('prints midnight as 00:00 rather than 24:00', () => {
        expect(formatInZone('2026-08-11T07:00:00Z', 'HH:mm')).toBe('00:00');
    });

    it('supports the patterns the templates use', () => {
        const instant = '2026-08-11T21:05:00Z';
        expect(formatInZone(instant, 'd MMM')).toBe('11 Aug');
        expect(formatInZone(instant, 'd MMM, HH:mm')).toBe('11 Aug, 14:05');
        expect(formatInZone(instant, 'd MMM y, HH:mm')).toBe('11 Aug 2026, 14:05');
    });

    it('returns an empty string for nothing rather than "Invalid Date"', () => {
        expect(formatInZone(null)).toBe('');
        expect(formatInZone(undefined)).toBe('');
        expect(formatInZone('')).toBe('');
        expect(formatInZone('not a date')).toBe('');
    });
});
