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

    constructor() {
        firstValueFrom(this.http.get<{ version: string }>('/api/health'))
            .then(r => this.version.set(r.version))
            .catch(() => { /* chrome, not critical — silently absent if health is unreachable */ });
    }
}
