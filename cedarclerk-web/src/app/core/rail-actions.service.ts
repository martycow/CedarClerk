import { Injectable, signal } from '@angular/core';
import { IconName } from '../shared/icon-data.generated';
import { ResinState } from '../bench/display/resin-drop.component';

/**
 * The screen's one primary action, as data. ADR-159 clause 2: a single object rather than a list,
 * because the placement table ADR-139 adopts says *the* screen's one primary action — a second
 * button on the rail is unrepresentable rather than merely discouraged.
 */
export interface RailAction {
    label: string;
    icon: IconName;
    hint?: string;
    disabled?: boolean;
    run: () => void;
}

export interface RailSave {
    state: ResinState;
    /** Usually empty: the drop is the indicator and the rule carries the word (ADR-159 clause 3). */
    label?: string;
    hint?: string;
}

// The rail is chrome and what it says on the right belongs to whatever screen is open, so the two
// are joined by a signal holder rather than by an input — the same shape and the same reason as
// RulerService (ADR-153 clause 5). It carries no TemplateRef: a portal here would let any page put
// any markup on the rail, which is the placement rule this exists to keep.
//
// A page clears on destroy, and the router destroys the outgoing component before activating the
// next one, so a page that publishes on init cannot be wiped by the page it replaced.
@Injectable({ providedIn: 'root' })
export class RailActionsService {
    readonly save = signal<RailSave | null>(null);
    readonly primary = signal<RailAction | null>(null);

    publish(contribution: { save?: RailSave | null; primary?: RailAction | null }) {
        this.save.set(contribution.save ?? null);
        this.primary.set(contribution.primary ?? null);
    }

    clear() {
        this.publish({});
    }
}
