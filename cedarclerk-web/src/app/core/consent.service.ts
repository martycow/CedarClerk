import { Injectable, signal } from '@angular/core';

export type Consent = 'granted' | 'denied' | 'unasked';

// One cookie, read by both surfaces (ADR-236). The landing is server-rendered and decides there
// whether to emit the provider script at all; this service is the SPA's half of the same answer,
// which is why the name and the values are Consts.General.ConsentCookie rather than something
// local to Angular — a second spelling would let one surface track while the other did not.
const COOKIE = 'cedar_consent';
const ONE_YEAR_SECONDS = 60 * 60 * 24 * 365;

@Injectable({ providedIn: 'root' })
export class ConsentService {
    readonly state = signal<Consent>(read());

    grant() { this.write('granted'); }
    deny() { this.write('denied'); }

    private write(value: Exclude<Consent, 'unasked'>) {
        // Lax rather than Strict: the landing links into the app on another host in production, and
        // Strict would drop the cookie on that crossing and re-ask on the other side. No Secure flag
        // on plain http, or a local run could never record an answer at all.
        const secure = location.protocol === 'https:' ? '; Secure' : '';
        document.cookie = `${COOKIE}=${value}; path=/; max-age=${ONE_YEAR_SECONDS}; SameSite=Lax${secure}`;
        this.state.set(value);
    }
}

function read(): Consent {
    const match = document.cookie.match(new RegExp(`(?:^|; )${COOKIE}=([^;]*)`));
    const value = match?.[1];
    return value === 'granted' || value === 'denied' ? value : 'unasked';
}
