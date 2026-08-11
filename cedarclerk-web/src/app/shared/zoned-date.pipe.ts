import { Pipe, PipeTransform } from '@angular/core';
import { formatInZone } from '../core/display-time';

/**
 * Drop-in replacement for `| date:'…'` that formats in the app's display zone instead of the
 * browser's (ADR-115), and reads an offset-less timestamp as UTC instead of as local time.
 *
 * `| date` was wrong on both counts: it showed whatever zone the machine was in, and the server's
 * offset-less timestamps made it read UTC as local — seven hours out.
 */
@Pipe({ name: 'zonedDate', standalone: true })
export class ZonedDatePipe implements PipeTransform {
    transform(value: string | number | Date | null | undefined, pattern = 'd MMM yyyy, HH:mm'): string {
        return formatInZone(value, pattern);
    }
}
