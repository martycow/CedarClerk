import { Component, inject, input } from '@angular/core';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from './icon.component';
import { PopoverComponent } from './popover.component';

// T-343 — explanatory prose folded behind an (i): the screens carried standing paragraphs of
// instructions, and the ruling was pictograms over text. The words are not gone — they open on
// demand, from a control that reads the same everywhere.
@Component({
    selector: 'app-hint-dot',
    imports: [IconComponent, PopoverComponent],
    template: `
        <app-popover>
            <button trigger type="button" class="dot" [attr.aria-label]="t().common.about" [title]="t().common.about">
                <app-icon name="info" size="xs" />
            </button>
            <p panel class="dot-text">{{ text() }}</p>
        </app-popover>
    `,
    styles: [`
        :host { display: inline-flex; vertical-align: middle; }

        .dot {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 22px;
            height: 22px;
            padding: 0;
            border: 1px solid var(--border);
            border-radius: 50%;
            background: var(--alt);
            color: var(--t2);
            cursor: pointer;
        }

        .dot:hover { background: var(--hover); color: var(--text); }

        .dot-text {
            margin: 0;
            max-width: 320px;
            font-size: var(--fs-ui);
            line-height: 1.45;
            color: var(--text);
        }
    `],
})
export class HintDotComponent {
    protected readonly t = inject(LocaleService).t;
    text = input.required<string>();
}
