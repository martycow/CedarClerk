import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { OverlayCoordinatorService } from './overlay-coordinator.service';

export type FeedbackKind = 'bug' | 'idea' | 'other';

// T-191 — the client half of the feedback channel. The modal is hoisted in the shell and raises
// through the shared overlay coordinator, so it cannot sit on top of another shell layer.
@Injectable({ providedIn: 'root' })
export class FeedbackFormService {
    private http = inject(HttpClient);
    private overlays = inject(OverlayCoordinatorService);

    readonly open = computed(() => this.overlays.active() === 'feedback');

    openForm(): void {
        this.overlays.open('feedback');
    }

    closeForm(): void {
        this.overlays.close('feedback');
    }

    submit(kind: FeedbackKind, message: string, path: string) {
        return firstValueFrom(this.http.post('/api/feedback', { kind, message, path }));
    }
}
