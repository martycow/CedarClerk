import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Fetched once at app start from the same /api/health the deploy script itself health-checks
// against (Program.cs), so the frontend never carries its own copy of Consts.CurrentVersion to
// drift out of sync — surfaced in the chrome so "which version am I actually looking at" (Marty,
// 28.07.2026, mid-troubleshooting a deploy) has an answer without opening devtools.
@Injectable({ providedIn: 'root' })
export class VersionService {
    private http = inject(HttpClient);
    readonly version = signal<string | null>(null);

    // Whether this installation asks for an invite code at registration. False for the hosted
    // server; true in the desktop shell, which has no public to gate. Read from the same call, so
    // the register page — which runs before anyone is signed in — needs no endpoint of its own.
    readonly openRegistration = signal(false);

    // ADR-108's `upstreamAuthHost` is gone with ADR-117. It existed to warn that the account was
    // shared with another installation while the data here was not — a warning that has nothing left
    // to warn about now the desktop keeps no data of its own. One identity, one data set.
    // T-003 — which sign-in buttons the two doors may draw. Read from the same call for the same
    // reason as openRegistration: /login and /register run before there is a session to ask with.
    readonly googleAuth = signal(false);
    readonly telegramBotId = signal<number | null>(null);
    readonly telegramBot = signal<string | null>(null);

    constructor() {
        firstValueFrom(this.http.get<HealthResponse>('/api/health'))
            .then(r => {
                this.version.set(r.version);
                this.openRegistration.set(r.openRegistration ?? false);
                this.googleAuth.set(r.externalAuth?.google ?? false);
                this.telegramBotId.set(r.externalAuth?.telegramBotId ?? null);
                this.telegramBot.set(r.externalAuth?.telegramBot ?? null);
            })
            .catch(() => { /* chrome, not critical — silently absent if health is unreachable */ });
    }
}

interface HealthResponse {
    version: string;
    openRegistration?: boolean;
    externalAuth?: { google?: boolean; telegramBot?: string | null; telegramBotId?: number | null } | null;
}
