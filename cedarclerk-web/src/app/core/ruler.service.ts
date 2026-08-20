import { Injectable, signal } from '@angular/core';
import { RulerReadout } from '../bench/chrome/ruler-bar.component';

// The rule is chrome and the numbers on it belong to whatever screen is open, so the two are
// joined by a signal holder rather than by an input: a page publishes what it measures and the
// bar renders it, without the shell knowing which screens exist.
//
// A page clears on destroy. The router destroys the outgoing component before it activates the
// next one, so a page that sets on init cannot have its readouts wiped by the page it replaced.
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
