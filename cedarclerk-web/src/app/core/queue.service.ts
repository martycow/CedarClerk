import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * Wave 2 item 10 — a weekly posting slot. Time is stored UTC (`timeUtcMinutes`, 0–1439) with
 * .NET's day convention (0 = Sunday); the UI converts to and from the browser zone. The accepted
 * DST caveat — a slot drifts an hour of local time after a switch — is stated in the UI, not
 * hidden here.
 */
export interface QueueSlot {
    id: string;
    targetId: string;
    targetName: string;
    network: string;
    name: string;
    category: string;
    dayOfWeek: number;
    timeUtcMinutes: number;
    isActive: boolean;
}

export interface QueueSlotInput {
    targetId: string;
    name: string;
    category: string;
    dayOfWeek: number;
    timeUtcMinutes: number;
    isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class QueueService {
    private http = inject(HttpClient);

    list() {
        return firstValueFrom(this.http.get<QueueSlot[]>('/api/queue/slots'));
    }

    /** The server answers with the id alone — callers re-list to pick up the full projection. */
    create(slot: QueueSlotInput) {
        return firstValueFrom(this.http.post<{ id: string }>('/api/queue/slots', slot));
    }

    update(id: string, slot: QueueSlotInput) {
        return firstValueFrom(this.http.put<{ id: string }>(`/api/queue/slots/${id}`, slot));
    }

    remove(id: string) {
        return firstValueFrom(this.http.delete<void>(`/api/queue/slots/${id}`));
    }
}
