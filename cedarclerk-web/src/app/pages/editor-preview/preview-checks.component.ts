import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { LocaleService } from '../../core/i18n/locale.service';
import { IconComponent } from '../../shared/icon.component';

export type CheckTone = 'ok' | 'warn' | 'muted';

/** One row of the checks column. None of them blocks anything (CONTRACT §D4). */
export interface PreviewCheck {
    id: string;
    label: string;
    detail: string;
    tone: CheckTone;
}

@Component({
    selector: 'app-preview-checks',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: { 'data-surface': 'paper', class: 'card' },
    template: `
        <div class="pc-head">
            <span class="label">{{ title() }}</span>
            @if (loading()) {
                <span class="pc-running" role="status"><app-icon name="circle-notch" size="sm" />{{ t().editor.previewTab.checks.running }}</span>
            }
        </div>
        <ul class="pc-list" role="list">
            @for (check of checks(); track check.id) {
                <li class="pc-row" [attr.data-tone]="check.tone">
                    <span class="pc-mark" aria-hidden="true">
                        <app-icon [name]="check.tone === 'warn' ? 'warning' : check.tone === 'ok' ? 'check' : 'info'" size="xs" />
                    </span>
                    <span class="pc-body">
                        <span class="pc-label">{{ check.label }}</span>
                        <span class="pc-detail">{{ check.detail }}</span>
                    </span>
                    <span class="visually-hidden">{{ toneWord(check.tone) }}</span>
                </li>
            } @empty {
                @if (!loading()) { <li class="pc-none">{{ t().editor.previewTab.checks.none }}</li> }
            }
        </ul>
        <span class="pc-spacer"></span>
        <button type="button" class="btn pc-all" (click)="details.emit()">{{ t().editor.previewTab.checks.viewAll }}</button>
    `,
    styles: [`
        :host {
            display: flex;
            flex-direction: column;
            width: 264px;
            flex: none;
            min-height: 0;
            box-sizing: border-box;
            padding: var(--space-4) var(--space-4) var(--space-3);
            font-family: var(--font-sans);
        }

        .pc-head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); }

        .pc-running { display: inline-flex; align-items: center; gap: var(--space-1); font-size: var(--fs-12); color: var(--t3); }

        .pc-list { display: flex; flex-direction: column; margin: var(--space-1) 0 0; padding: 0; overflow: auto; list-style: none; }

        .pc-row {
            display: flex;
            align-items: flex-start;
            gap: var(--space-3);
            padding: var(--space-3) 0;
            border-bottom: 1px solid var(--paper-edge);
        }

        .pc-mark {
            display: flex;
            align-items: center;
            justify-content: center;
            flex: none;
            width: 22px;
            height: 22px;
            border-radius: 50%;
            background: var(--ok);
            color: var(--text-on-pine);
        }

        .pc-row[data-tone="warn"] .pc-mark { background: var(--warn-soft); color: var(--warn); }
        .pc-row[data-tone="muted"] .pc-mark { background: var(--surface); color: var(--t2); }

        .pc-body { display: flex; flex: 1; flex-direction: column; gap: 2px; min-width: 0; }
        .pc-label { font-size: var(--fs-15); font-weight: 600; }
        .pc-detail { font-size: var(--fs-13); color: var(--t2); overflow-wrap: anywhere; }

        .pc-none { padding: var(--space-3) 0; font-size: var(--fs-13); color: var(--t3); }

        .pc-spacer { flex: 1; }

        .pc-all { margin-top: var(--space-3); }

        @media (max-width: 1180px) {
            :host { width: 100%; flex: none; }
            .pc-list { overflow: visible; }
            .pc-row { padding: var(--space-2) 0; }
        }

        .visually-hidden {
            position: absolute;
            width: 1px;
            height: 1px;
            margin: -1px;
            padding: 0;
            border: 0;
            overflow: hidden;
            white-space: nowrap;
            clip-path: inset(50%);
        }
    `],
})
export class PreviewChecksComponent {
    readonly title = input('');
    readonly checks = input<readonly PreviewCheck[]>([]);
    readonly loading = input(false);
    readonly details = output<void>();

    protected readonly t = inject(LocaleService).t;

    toneWord(tone: CheckTone): string {
        const words = this.t().editor.previewTab.checks;
        return tone === 'warn' ? words.toneWarn : tone === 'ok' ? words.toneOk : words.toneInfo;
    }
}
