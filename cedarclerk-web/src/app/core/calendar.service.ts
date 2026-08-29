import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Wave 2 item 9 — the content calendar's own write path. Reading stays on PostsService
// (listScheduled) and QueueService (slots); the one thing the calendar changes is when a
// pending post fires.
@Injectable({ providedIn: 'root' })
export class CalendarService {
    private http = inject(HttpClient);

    /**
     * Drag-to-day reschedule. Pending rows only — the server answers 409 for a Sent/Failed row,
     * which the calendar surfaces instead of moving the ticket.
     */
    reschedule(id: string, scheduledAtUtc: string) {
        return firstValueFrom(this.http.patch<{ id: string; scheduledAtUtc: string }>(
            `/api/posts/scheduled/${id}`, { scheduledAtUtc }));
    }
}
