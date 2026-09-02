// ADR-244 — Pacific preserves existing accounts; /api/auth/me replaces it with the account's IANA
// timezone before authenticated screens render.
export const DEFAULT_DISPLAY_TIME_ZONE = 'America/Los_Angeles';
let currentTimeZone = DEFAULT_DISPLAY_TIME_ZONE;

export function displayTimeZone(): string { return currentTimeZone; }

export function setDisplayTimeZone(timeZoneId: string): void {
    try {
        new Intl.DateTimeFormat('en-US', { timeZone: timeZoneId }).format(0);
        currentTimeZone = timeZoneId;
    } catch {
        currentTimeZone = DEFAULT_DISPLAY_TIME_ZONE;
    }
}

// LocaleService writes the UI language onto <html lang>; reading it back here keeps every date the
// app prints — through the pipe or a direct call — in that language without threading a service
// through a pure formatter.
const MONTHS_SHORT: Record<string, readonly string[]> = {
    en: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'],
    ru: ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'],
};

function monthsShort(): readonly string[] {
    const lang = typeof document === 'undefined' ? '' : document.documentElement.lang;
    return MONTHS_SHORT[lang] ?? MONTHS_SHORT['en'];
}

// A named zone, not a fixed -8: Los Angeles is on PDT from March to November, and a fixed offset
// would be an hour wrong for most of the year.
// hourCycle rather than hour12: false — with hour12: false some engines print midnight as 24:00.
const PARTS = new Map<string, Intl.DateTimeFormat>();

function partsFormatter(timeZoneId: string): Intl.DateTimeFormat {
    let formatter = PARTS.get(timeZoneId);
    if (!formatter) {
        formatter = new Intl.DateTimeFormat('en-US-u-ca-gregory-nu-latn', {
            timeZone: timeZoneId,
            year: 'numeric', month: 'numeric', day: 'numeric',
            hour: 'numeric', minute: 'numeric', second: 'numeric', hourCycle: 'h23',
        });
        PARTS.set(timeZoneId, formatter);
    }
    return formatter;
}

export interface ZonedParts {
    year: number; month: number; day: number; hour: number; minute: number; second: number;
}

export function partsInZone(
    value: string | number | Date | null | undefined,
    timeZoneId = currentTimeZone,
): ZonedParts | null {
    const date = toInstant(value);
    if (!date) return null;
    const parts: Record<string, string> = {};
    for (const part of partsFormatter(timeZoneId).formatToParts(date)) {
        if (part.type !== 'literal') parts[part.type] = part.value;
    }
    return {
        year: Number(parts['year']), month: Number(parts['month']), day: Number(parts['day']),
        hour: Number(parts['hour']), minute: Number(parts['minute']), second: Number(parts['second']),
    };
}

const pad = (value: number | string) => String(value).padStart(2, '0');

export function dayInZone(value: string | number | Date, timeZoneId = currentTimeZone): string {
    const parts = partsInZone(value, timeZoneId);
    return parts ? `${parts.year}-${pad(parts.month)}-${pad(parts.day)}` : '';
}

export function timeInZone(value: string | number | Date, timeZoneId = currentTimeZone): string {
    const parts = partsInZone(value, timeZoneId);
    return parts ? `${pad(parts.hour)}:${pad(parts.minute)}` : '';
}

/** Account wall-clock input to a UTC instant. Invalid daylight-saving gaps return null. */
export function wallClockToInstant(day: string, time: string, timeZoneId = currentTimeZone): Date | null {
    const [year, month, date] = day.split('-').map(Number);
    const [hour, minute] = time.split(':').map(Number);
    if (![year, month, date, hour, minute].every(Number.isFinite)) return null;

    const wanted = Date.UTC(year, month - 1, date, hour, minute, 0);
    let guess = wanted;
    for (let i = 0; i < 4; i++) {
        const actual = partsInZone(guess, timeZoneId);
        if (!actual) return null;
        const actualWall = Date.UTC(actual.year, actual.month - 1, actual.day, actual.hour, actual.minute, 0);
        const delta = wanted - actualWall;
        guess += delta;
        if (delta === 0) break;
    }

    const matches = (instant: number) => {
        const p = partsInZone(instant, timeZoneId);
        return p?.year === year && p.month === month && p.day === date && p.hour === hour && p.minute === minute;
    };
    const candidates: number[] = [];
    for (let offset = -120; offset <= 120; offset += 30) {
        const candidate = guess + offset * 60_000;
        if (matches(candidate)) candidates.push(candidate);
    }
    return candidates.length ? new Date(Math.min(...candidates)) : null;
}

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
export function formatInZone(value: string | number | Date | null | undefined, pattern = 'MM/dd/yyyy, HH:mm'): string {
    const date = toInstant(value);
    if (!date) return '';

    const parts = partsInZone(date);
    if (!parts) return '';

    const { year, month, day, hour, minute } = parts;

    return pattern.replace(/yyyy|yy|y|MMM|MM|M|dd|d|HH|mm/g, token => {
        switch (token) {
            case 'yyyy': case 'y': return String(year);
            case 'yy': return pad(year % 100);
            case 'MMM': return monthsShort()[month - 1];
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
    const named = new Intl.DateTimeFormat('en-US', { timeZone: currentTimeZone, timeZoneName: 'short' })
        .formatToParts(date)
        .find(part => part.type === 'timeZoneName');
    return named?.value ?? '';
}
