// Every time this app prints is Pacific, whatever the machine showing it thinks (ADR-115).
// The backend keeps the same value in Consts.General.DisplayTimeZone — these two constants are the
// place a per-user timezone would replace.
export const DISPLAY_TIME_ZONE = 'America/Los_Angeles';

// A named zone, not a fixed -8: Los Angeles is on PDT from March to November, and a fixed offset
// would be an hour wrong for most of the year.
const MONTHS_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

// hourCycle rather than hour12: false — with hour12: false some engines print midnight as 24:00.
const PARTS = new Intl.DateTimeFormat('en-US', {
    timeZone: DISPLAY_TIME_ZONE,
    year: 'numeric', month: 'numeric', day: 'numeric',
    hour: 'numeric', minute: 'numeric', hourCycle: 'h23',
});

/**
 * The server sends UTC with a trailing Z since ADR-115. A value without one is still read as UTC
 * here rather than as local time: everything stored is UTC in fact, and it is the missing Z that
 * made the app show times seven hours out in the first place.
 */
export function toInstant(value: string | number | Date | null | undefined): Date | null {
    if (value === null || value === undefined || value === '') return null;
    if (value instanceof Date) return isNaN(value.getTime()) ? null : value;
    if (typeof value === 'number') return new Date(value);
    const iso = /Z|[+-]\d{2}:?\d{2}$/.test(value) ? value : value + 'Z';
    const date = new Date(iso);
    return isNaN(date.getTime()) ? null : date;
}

/**
 * Formats an instant in the display zone. Patterns are the subset of Angular's that this app
 * actually used before the switch — `d MMM yyyy, HH:mm` and friends — so templates read the same.
 */
export function formatInZone(value: string | number | Date | null | undefined, pattern = 'd MMM yyyy, HH:mm'): string {
    const date = toInstant(value);
    if (!date) return '';

    const parts: Record<string, string> = {};
    for (const part of PARTS.formatToParts(date)) {
        if (part.type !== 'literal') parts[part.type] = part.value;
    }

    const year = Number(parts['year']);
    const month = Number(parts['month']);
    const day = Number(parts['day']);
    const hour = parts['hour'] ?? '00';
    const minute = parts['minute'] ?? '00';
    const pad = (n: number | string) => String(n).padStart(2, '0');

    return pattern.replace(/yyyy|yy|y|MMM|MM|M|dd|d|HH|mm/g, token => {
        switch (token) {
            case 'yyyy': case 'y': return String(year);
            case 'yy': return pad(year % 100);
            case 'MMM': return MONTHS_SHORT[month - 1];
            case 'MM': return pad(month);
            case 'M': return String(month);
            case 'dd': return pad(day);
            case 'd': return String(day);
            case 'HH': return pad(hour);
            case 'mm': return pad(minute);
            default: return token;
        }
    });
}

/** "PDT" or "PST" for that instant — for the rare place that has to name the zone out loud. */
export function zoneAbbreviation(value: string | number | Date | null | undefined): string {
    const date = toInstant(value);
    if (!date) return '';
    const named = new Intl.DateTimeFormat('en-US', { timeZone: DISPLAY_TIME_ZONE, timeZoneName: 'short' })
        .formatToParts(date)
        .find(part => part.type === 'timeZoneName');
    return named?.value ?? '';
}
