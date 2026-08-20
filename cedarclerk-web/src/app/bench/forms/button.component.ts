import { booleanAttribute, Component, computed, input, output } from '@angular/core';

export type ButtonVariant = 'pine' | 'paper' | 'rail' | 'danger';
export type ButtonSize = 'md' | 'sm';

// ADR-138 — the surface is read off the variant, not off the size: `rail` is painted on wood and
// gets chrome's 30px box and 13/11px type, everything else sits on paper and gets 44px and 14px.
// `size="sm"` therefore only tightens the padding on paper; it cannot take a paper control under
// the touch floor, which is what the mirror's 30px sm would have done on a pine button.
@Component({
    selector: 'app-button',
    host: { '[attr.data-surface]': 'surface()' },
    template: `
        <button [class]="'btn ' + variant() + ' ' + size()" [type]="type()" [disabled]="disabled()"
                [attr.title]="title() || null" (click)="clicked.emit($event)">
            <ng-content />
        </button>
    `,
    styles: [`
        :host { display: inline-flex; }

        .btn {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: var(--space-2);
            width: 100%;
            font-family: var(--font-sans);
            font-weight: 700;
            line-height: 1.2;
            cursor: pointer;
            border-radius: var(--radius-plaque);
            transition: filter var(--motion-fast) var(--ease-settle),
                        transform var(--motion-fast) var(--ease-settle),
                        box-shadow var(--motion-fast) var(--ease-settle);
        }

        .btn:disabled { opacity: .6; cursor: default; }

        :host([data-surface="paper"]) .btn { min-height: var(--hit-target); font-size: var(--fs-ui); }
        :host([data-surface="paper"]) .btn.md { padding: var(--space-2) var(--space-4); }
        :host([data-surface="paper"]) .btn.sm { padding: var(--space-1) var(--space-3); }

        :host([data-surface="chrome"]) .btn { min-height: var(--hit-chrome); }
        :host([data-surface="chrome"]) .btn.md { padding: var(--space-1) var(--space-3); font-size: var(--text-chrome); }
        :host([data-surface="chrome"]) .btn.sm { padding: 0 var(--space-2); font-size: var(--text-chrome-sm); }

        /* Every box-shadow here is withheld while the control is focused. The ADR-140 ring spends
           its second layer on a box-shadow, and a component's own shadow out-specifies the global
           rule that draws it — so a pine button would keep its lift and lose its halo. */
        .btn.pine {
            border: 1px solid var(--pine-deep);
            background: var(--grad-pine);
            color: var(--text-on-pine);
            text-shadow: 0 1px 1px var(--pine-deep);
        }

        .btn.pine:not(:focus-visible) { box-shadow: var(--shadow-pine-btn); }
        .btn.pine:hover:not(:disabled) { filter: brightness(1.07); }
        .btn.pine:active:not(:disabled) { transform: translateY(2px); }
        .btn.pine:active:not(:disabled):not(:focus-visible) {
            box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--brass-hi) 30%, transparent), var(--shadow);
        }

        .btn.paper {
            border: var(--border-paper);
            background: var(--sheet);
            color: var(--t2);
            font-weight: 600;
        }

        .btn.paper:not(:focus-visible) { box-shadow: var(--shadow-paper-sm); }
        .btn.paper:hover:not(:disabled) { background: var(--surface); color: var(--text); }
        .btn.paper:active:not(:disabled) { transform: translateY(1px); }

        .btn.rail {
            border: var(--border-rail-btn);
            background: var(--rail-lo);
            color: var(--rail-ink);
            font-weight: 600;
        }

        .btn.rail:hover:not(:disabled) { background: var(--rail-edge); }
        .btn.rail:active:not(:disabled) { transform: translateY(1px); }

        .btn.danger {
            border: none;
            background: none;
            color: var(--danger);
            text-decoration: underline dashed;
            text-underline-offset: var(--space-1);
        }

        /* Qualified by the surface so it outranks the size rule above, which is where the
           horizontal padding of every other variant comes from. */
        :host([data-surface="paper"]) .btn.danger { padding-left: var(--space-1); padding-right: var(--space-1); }

        .btn.danger:hover:not(:disabled) { filter: brightness(.9); }
    `],
})
export class ButtonComponent {
    variant = input<ButtonVariant>('pine');
    size = input<ButtonSize>('md');
    disabled = input(false, { transform: booleanAttribute });
    type = input<'button' | 'submit' | 'reset'>('button');
    title = input('');
    clicked = output<MouseEvent>();

    surface = computed(() => (this.variant() === 'rail' ? 'chrome' : 'paper'));
}
