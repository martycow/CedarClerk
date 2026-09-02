import { Injectable, signal } from '@angular/core';

/** One readout: a count, a state word, a version. */
export interface RulerReadout {
    text: string;
    /** Long-form for a readout too terse to read on its own. */
    title?: string;
}

// Kept alive and rendered by nothing (ADR-239 clause 6): the pages still publish here until each
// draws the same numbers in its own app-page-header, and the service goes in the stage C close.
@Injectable({ providedIn: 'root' })
export class RulerService {
    readonly label = signal('');
    readonly left = signal<readonly RulerReadout[]>([]);
    readonly right = signal<readonly RulerReadout[]>([]);

    publish(readouts: { label?: string; left?: readonly RulerReadout[]; right?: readonly RulerReadout[] }) {
        this.label.set(readouts.label ?? '');
        this.left.set(readouts.left ?? []);
        this.right.set(readouts.right ?? []);
    }

    clear() {
        this.publish({});
    }
}
