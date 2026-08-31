import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type FeedbackKind = 'bug' | 'idea' | 'other';

// T-191 — the client half of the feedback channel. The modal is hoisted in the shell (like the
// appearance panel), so `open` is the one signal that raises it from anywhere.
@Injectable({ providedIn: 'root' })
export class FeedbackFormService {
    private http = inject(HttpClient);

    readonly open = signal(false);

    submit(kind: FeedbackKind, message: string, path: string) {
        return firstValueFrom(this.http.post('/api/feedback', { kind, message, path }));
    }
}
