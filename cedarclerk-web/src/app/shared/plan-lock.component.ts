import { Component, computed, inject, input } from '@angular/core';
import { IconComponent } from './icon.component';
import { LocaleService } from '../core/i18n/locale.service';

// T-349 — the plan lock. Every control gated behind a paid tier wears one of these when the
// caller's plan does not reach it: silver for Pro, gold for Pro+. The badge is the whole
// vocabulary — a gated button never fires and never explains itself in prose.
@Component({
    selector: 'app-plan-lock',
    imports: [IconComponent],
    host: { '[attr.data-tier]': 'tier()', '[attr.title]': 'title()' },
    template: `<app-icon name="lock" weight="bold" size="xs" [label]="title()" />`,
    styles: [`
        :host { display: inline-flex; align-items: center; }
        :host([data-tier="pro"]) { color: var(--lock-silver); }
        :host([data-tier="proplus"]) { color: var(--lock-gold); }
    `],
})
export class PlanLockComponent {
    tier = input.required<'pro' | 'proplus'>();
    private readonly t = inject(LocaleService).t;
    protected readonly title = computed(() =>
        this.tier() === 'pro' ? this.t().planLock.pro : this.t().planLock.proPlus);
}
