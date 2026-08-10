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

    // ADR-108 — the host this installation checks credentials against, when it is not itself. Null
    // on the hosted server. Its presence is what tells the app that the account is shared with
    // another installation while the DATA here is not — which has to be said out loud, or one
    // identity across two data sets becomes a trap.
    readonly upstreamAuthHost = signal<string | null>(null);

    constructor() {
        firstValueFrom(this.http.get<{ version: string; openRegistration?: boolean; upstreamAuthHost?: string | null }>('/api/health'))
            .then(r => {
                this.version.set(r.version);
                this.openRegistration.set(r.openRegistration ?? false);
                this.upstreamAuthHost.set(r.upstreamAuthHost ?? null);
            })
            .catch(() => { /* chrome, not critical — silently absent if health is unreachable */ });
    }
}
