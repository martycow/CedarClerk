import { booleanAttribute, Component, computed, input, output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Params, RouterLink } from '@angular/router';

export type ButtonVariant = 'pine' | 'paper' | 'rail' | 'danger';
export type ButtonSize = 'md' | 'sm';

// ADR-138 — the surface is read off the variant, not off the size: `rail` is painted on wood and
// gets chrome's 30px box and 13/11px type, everything else sits on paper and gets 44px and 14px.
// `size="sm"` therefore only tightens the padding on paper; it cannot take a paper control under
// the touch floor, which is what the mirror's 30px sm would have done on a pine button.
//
// A button handed a route is an anchor and looks exactly the same (ADR-169): every variant's face
// is painted on `.btn`, and the tag it is painted on is the consumer's business.
@Component({
    selector: 'app-button',
    imports: [NgTemplateOutlet, RouterLink],
    host: { '[attr.data-surface]': 'surface()' },
    template: `
        @if (link()) {
            <a [class]="classes()" [routerLink]="route()" [queryParams]="queryParams()"
               [attr.title]="title() || null" [attr.aria-disabled]="disabled() || null">
                <ng-container [ngTemplateOutlet]="face" />
            </a>
        } @else {
            <button [class]="classes()" [type]="type()" [disabled]="disabled()"
                    [attr.title]="title() || null" (click)="clicked.emit($event)">
                <ng-container [ngTemplateOutlet]="face" />
            </button>
        }

        <ng-template #face><ng-content /></ng-template>
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
            text-decoration: none;
            cursor: pointer;
            border-radius: var(--radius-plaque);
            transition: filter var(--motion-fast) var(--ease-settle),
                        transform var(--motion-fast) var(--ease-settle),
                        box-shadow var(--motion-fast) var(--ease-settle);
        }

        /* Two selectors for one state: :disabled matches no anchor, and in link form the route is
           dropped rather than the element, so aria-disabled is what is left to read (ADR-169). The
           hover and active rules below hang off :not(:disabled), which an anchor never satisfies —
           so the anchor's half also stops the pointer, or a dead link would still light up. */
        .btn:disabled { opacity: .6; cursor: default; }
        .btn[aria-disabled="true"] { opacity: .6; cursor: default; pointer-events: none; }

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
    /**
     * The screen this button opens; anything routerLink takes. Set, the control is an anchor and a
     * middle click opens it in a tab (ADR-163); unset, it is a native button speaking through
     * `clicked`.
     */
    link = input<string | readonly unknown[] | null>(null);
    /** The editor addresses a document by query, so a link to one is a route plus this. */
    queryParams = input<Params | null>(null);

    /** Silent in link form — there the anchor is the navigation. */
    clicked = output<MouseEvent>();

    surface = computed(() => (this.variant() === 'rail' ? 'chrome' : 'paper'));

    protected classes = computed(() => `btn ${this.variant()} ${this.size()}`);

    /** A disabled link is an anchor with no address, which is the platform's own way of not being
        a link at all — focus, activation and the context menu all go with the href (ADR-169). */
    protected route = computed(() => (this.disabled() ? null : this.link()));
}
