import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleService } from '../../core/i18n/locale.service';
import { IconName } from '../../shared/icon-data.generated';
import { IconComponent } from '../../shared/icon.component';

/** The one readiness vocabulary Preview and Publish share (ADR-242 clause 7). */
export type DestinationState = 'ready' | 'warn' | 'blocking' | 'setup' | 'unavailable';

const ICONS: Record<DestinationState, IconName> = {
    ready: 'check', warn: 'warning', blocking: 'x', setup: 'gear', unavailable: 'clock',
};

// A state is an icon with a word, never a colour alone.
@Component({
    selector: 'app-state-tag',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: { class: 'tag is-plain st', '[attr.data-state]': 'state()' },
    template: `<app-icon [name]="icon()" size="xs" />{{ label() }}`,
    styles: [`
        :host { gap: 4px; font-size: var(--fs-12); font-weight: 700; }
        :host([data-state="ready"]) { background: var(--ok-soft); color: var(--ok); }
        :host([data-state="warn"]) { background: var(--warn-soft); color: var(--warn); }
        :host([data-state="blocking"]) { background: var(--danger-soft); color: var(--danger); }
        :host([data-state="setup"]), :host([data-state="unavailable"]) { background: var(--surface); color: var(--t2); }
    `],
})
export class StateTagComponent {
    readonly state = input.required<DestinationState>();

    private readonly t = inject(LocaleService).t;

    protected readonly icon = computed(() => ICONS[this.state()]);
    protected readonly label = computed(() => {
        const words = this.t().editor.publishTab.state;
        switch (this.state()) {
            case 'ready': return words.ready;
            case 'warn': return words.warning;
            case 'blocking': return words.blocking;
            case 'setup': return words.setup;
            default: return words.unavailable;
        }
    });
}
