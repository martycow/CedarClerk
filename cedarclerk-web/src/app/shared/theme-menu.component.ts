import { Component, booleanAttribute, computed, inject, input } from '@angular/core';
import { AppearanceService } from '../core/appearance.service';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeMode, ThemeService } from '../core/theme.service';
import { IconComponent } from './icon.component';
import { IconName } from './icon-data.generated';
import { PopoverComponent } from './popover.component';

// ADR-321 — the one colour-mode control: a dropdown holding Light, Dark and System as toggle
// buttons. Signed in, the choice is part of the account's appearance; signed out, of the browser.
@Component({
    selector: 'app-theme-menu',
    imports: [IconComponent, PopoverComponent],
    template: `
        <app-popover #pop [placement]="placement()">
            <button trigger type="button" class="tm-trigger" [class.is-labelled]="labelled()"
                    aria-haspopup="true" [attr.aria-expanded]="pop.isOpen()"
                    [attr.aria-label]="labels().themeLabel" [attr.title]="labels().themeLabel">
                <app-icon [name]="icon()" size="sm" />
                @if (labelled()) { <span>{{ currentLabel() }}</span> }
            </button>
            <div panel class="tm-panel" role="group" [attr.aria-label]="labels().themeLabel">
                @for (option of options; track option.mode) {
                    <button type="button" class="tm-option" [class.is-on]="theme.mode() === option.mode"
                            [attr.aria-pressed]="theme.mode() === option.mode" (click)="choose(option.mode); pop.close()">
                        <app-icon [name]="option.icon" size="sm" /><span>{{ wordFor(option.mode) }}</span>
                    </button>
                }
            </div>
        </app-popover>
    `,
    styles: [`
        :host { display: inline-flex; }
        .tm-trigger { display: inline-flex; align-items: center; justify-content: center; gap: var(--space-1); min-width: var(--hit-touch); min-height: var(--hit-touch); padding: 0 var(--space-2); border: 0; border-radius: var(--radius-sm); background: transparent; color: inherit; font: inherit; font-size: var(--fs-ui); cursor: pointer; }
        .tm-trigger:hover { background: var(--hover); }
        .tm-trigger:focus-visible, .tm-option:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
        .tm-panel { display: flex; gap: var(--space-1); padding: var(--space-1); }
        .tm-option { display: inline-flex; flex-direction: column; align-items: center; gap: var(--space-1); min-width: 72px; min-height: var(--hit-touch); padding: var(--space-2); border: 1px solid transparent; border-radius: var(--radius-sm); background: transparent; color: var(--text); font: inherit; font-size: var(--fs-meta); cursor: pointer; }
        .tm-option:hover { background: var(--hover); }
        .tm-option.is-on { border-color: var(--accent); background: var(--asoft); color: var(--accent); font-weight: 600; }
    `],
})
export class ThemeMenuComponent {
    readonly placement = input<'vertical' | 'right-start' | 'left-start'>('vertical');
    /** Shows the current mode's word beside the icon, for Settings. */
    readonly labelled = input(false, { transform: booleanAttribute });

    protected readonly theme = inject(ThemeService);
    private readonly auth = inject(AuthService);
    private readonly appearance = inject(AppearanceService);
    private readonly t = inject(LocaleService).t;

    protected readonly options: readonly { mode: ThemeMode; icon: IconName }[] = [
        { mode: 'light', icon: 'sun' },
        { mode: 'dark', icon: 'moon' },
        { mode: 'system', icon: 'desktop' },
    ];

    protected readonly labels = computed(() => this.t().settings.appearance);
    protected readonly icon = computed<IconName>(() => this.options.find(o => o.mode === this.theme.mode())!.icon);
    protected readonly currentLabel = computed(() => this.wordFor(this.theme.mode()));

    protected wordFor(mode: ThemeMode): string {
        const l = this.labels();
        return mode === 'system' ? l.themeSystem : l[mode];
    }

    choose(mode: ThemeMode) {
        if (this.auth.userEmail()) {
            this.appearance.preview({ theme: mode });
            void this.appearance.commit();
        } else {
            this.theme.set(mode);
        }
    }
}
