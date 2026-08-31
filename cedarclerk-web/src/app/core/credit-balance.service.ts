import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// T-351 — the one number the shell's chip shows. Refreshed by the shell on navigation rather
// than pushed: every metered action (an X post, an AI call, a purchase) ends in a navigation or
// a screen the user reads the result on, and a balance one page-turn stale is honest enough for
// a chip whose click opens the real wallet.
@Injectable({ providedIn: 'root' })
export class CreditBalanceService {
    private http = inject(HttpClient);

    readonly balance = signal<number | null>(null);

    async refresh() {
        try {
            const res = await firstValueFrom(this.http.get<{ balance: number }>('/api/billing/credits/balance'));
            this.balance.set(res.balance);
        } catch { /* the chip simply stays as it was */ }
    }
}
