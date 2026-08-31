import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CONTENT_LANGUAGES, endonymOf } from '../core/languages';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from './icon.component';
import { PopoverComponent } from './popover.component';
import { PlanLockComponent } from './plan-lock.component';

export interface LanguageMenuItem { code: string; hasContent?: boolean; stale?: boolean; }

// T-350 — the one language menu that scales past nine: a searchable popover listing every content
// language with its endonym, a lock on the ones the plan does not reach (Free = en/ja) and a dot on
// the ones that already have content. Replaces the flat leaf-tag rows that ran off the edge as
// languages piled up. The active language and which ones "have content" are the caller's to say;
// the lock is read from the plan here so every call site gates alike.
@Component({
    selector: 'app-language-menu',
    imports: [FormsModule, IconComponent, PopoverComponent, PlanLockComponent],
    template: `
        <app-popover #pop>
            <button trigger type="button" class="lm-trigger" [title]="triggerTitle()">
                <app-icon name="translate" size="xs" />
                <span class="lm-current">{{ active().toUpperCase() }}</span>
                <span class="lm-endonym">{{ endonym(active()) }}</span>
                <app-icon name="caret-down" size="xs" />
            </button>
            <div panel class="lm-panel">
                <input class="lm-search" type="search" [(ngModel)]="query"
                       [placeholder]="t().languageMenu.search" [attr.aria-label]="t().languageMenu.search">
                <ul class="lm-list">
                    @for (item of filtered(); track item.code) {
                    <li>
                        <button type="button" class="lm-item" [class.on]="item.code === active()"
                                [disabled]="!auth.hasContentLanguage(item.code)"
                                (click)="pick(item.code, pop)">
                            <span class="lm-code">{{ item.code.toUpperCase() }}</span>
                            <span class="lm-name">{{ endonym(item.code) }}</span>
                            @if (item.hasContent) { <span class="lm-dot" [title]="t().languageMenu.hasContent"></span> }
                            @if (item.stale) { <span class="lm-stale" [title]="t().languageMenu.stale">•</span> }
                            @if (!auth.hasContentLanguage(item.code)) { <app-plan-lock tier="pro" /> }
                        </button>
                    </li>
                    } @empty {
                    <li class="lm-empty">{{ t().common.nothingHere }}</li>
                    }
                </ul>
            </div>
        </app-popover>
    `,
    styles: [`
        :host { display: inline-flex; }

        .lm-trigger {
            display: inline-flex; align-items: center; gap: var(--space-1);
            min-height: var(--hit-chrome); padding: 0 var(--space-2);
            border: 1px solid var(--border); border-radius: var(--radius-plaque);
            background: var(--sheet); color: var(--text);
            font-family: var(--font-sans); font-size: var(--text-chrome); cursor: pointer;
        }
        .lm-trigger:hover { background: var(--surface); }
        .lm-current { font-family: var(--font-mono); font-weight: 700; }
        .lm-endonym { color: var(--t2); }

        .lm-panel { display: flex; flex-direction: column; gap: var(--space-2); min-width: 220px; }
        .lm-search {
            box-sizing: border-box; width: 100%; padding: 5px 8px;
            border: 1px solid var(--border); border-radius: var(--radius-sm);
            background: var(--sheet); color: var(--text); font-size: var(--fs-ui);
        }
        .lm-list { list-style: none; margin: 0; padding: 0; max-height: 260px; overflow-y: auto; }

        .lm-item {
            display: flex; align-items: center; gap: var(--space-2); width: 100%;
            padding: var(--space-1) var(--space-2); border: none; background: none;
            color: var(--text); font-size: var(--fs-ui); text-align: left; cursor: pointer;
            border-radius: var(--radius-sm);
        }
        .lm-item:hover:not(:disabled) { background: var(--hover); }
        .lm-item.on { background: var(--asoft); color: var(--accent); }
        .lm-item:disabled { opacity: .6; cursor: default; }
        .lm-code { font-family: var(--font-mono); font-weight: 700; min-width: 26px; }
        .lm-name { flex: 1; color: var(--t2); }
        .lm-dot { width: 6px; height: 6px; border-radius: 50%; background: var(--ok); }
        .lm-stale { color: var(--warn); font-size: var(--fs-title); line-height: 0; }
        .lm-empty { padding: var(--space-2); color: var(--t2); font-size: var(--fs-ui); }
    `],
})
export class LanguageMenuComponent {
    protected readonly auth = inject(AuthService);
    protected readonly t = inject(LocaleService).t;

    active = input.required<string>();
    /** Per-language flags the caller owns: which have content, which are stale. */
    items = input<LanguageMenuItem[]>([]);
    /** When set, only these codes are offered (e.g. the editor's missing languages). */
    only = input<string[] | null>(null);
    picked = output<string>();

    protected readonly endonym = endonymOf;
    protected query = signal('');

    private readonly base = computed<LanguageMenuItem[]>(() => {
        const flags = new Map(this.items().map(i => [i.code, i]));
        const codes = this.only() ?? CONTENT_LANGUAGES;
        return codes.map(code => flags.get(code) ?? { code });
    });

    protected readonly filtered = computed(() => {
        const q = this.query().trim().toLowerCase();
        if (!q) return this.base();
        return this.base().filter(i =>
            i.code.includes(q) || endonymOf(i.code).toLowerCase().includes(q));
    });

    protected triggerTitle(): string {
        return this.t().languageMenu.pick;
    }

    protected pick(code: string, pop: PopoverComponent) {
        if (!this.auth.hasContentLanguage(code)) return;
        this.query.set('');
        pop.close();
        this.picked.emit(code);
    }
}
