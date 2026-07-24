import { Injectable, computed, signal } from '@angular/core';

export interface DebugLogEntry {
    id: number;
    method: string;
    url: string;
    startedAt: number;
    requestBody?: unknown;
    status?: number;
    durationMs?: number;
    responseBody?: unknown;
    isError: boolean;
    inFlight: boolean;
}

const MAX_ENTRIES = 200;

// Backs the bottom debug console panel (shared/debug-console.component.ts) — a ring buffer of
// every HttpClient request/response this session has made, populated by debug-log.interceptor.ts.
// Session-only by design (no localStorage persistence): this is for diagnosing what just
// happened (e.g. why a Telegram publish failed), not a durable history.
@Injectable({ providedIn: 'root' })
export class DebugLogService {
    private seq = 0;
    entries = signal<DebugLogEntry[]>([]);
    inFlightCount = computed(() => this.entries().filter(e => e.inFlight).length);
    errorCount = computed(() => this.entries().filter(e => e.isError && !e.inFlight).length);

    start(method: string, url: string, requestBody: unknown): DebugLogEntry {
        const entry: DebugLogEntry = {
            id: ++this.seq,
            method,
            url,
            startedAt: performance.now(),
            requestBody,
            isError: false,
            inFlight: true,
        };
        this.entries.update(list => [entry, ...list].slice(0, MAX_ENTRIES));
        return entry;
    }

    finish(id: number, status: number | undefined, responseBody: unknown, isError: boolean) {
        this.entries.update(list => list.map(e => e.id === id
            ? { ...e, status, responseBody, isError, inFlight: false, durationMs: Math.round(performance.now() - e.startedAt) }
            : e));
    }

    clear() {
        this.entries.set([]);
    }
}
