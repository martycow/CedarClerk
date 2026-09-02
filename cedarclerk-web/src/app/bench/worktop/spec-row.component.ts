import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';

/**
 * Whose property this row describes. The inspector is contextual — it shows the selected object,
 * and falls back to the document when nothing is selected — and it may never show both subjects
 * at once. The first engine draft did exactly that: the toolbar claimed a heading was selected
 * while the inspector described an image, and neither told the reader which one it meant. Every
 * row therefore carries its subject, so a mixed sheet is a thing that can be seen and asserted
 * rather than a convention nobody can check.
 */
export type SpecScope = 'selection' | 'document';

// One ruled line of a printed spec sheet: label in sans, value in mono ink. Stacked inside a
// ShelfPanel and grouped under a small uppercase heading.
@Component({
    selector: 'app-spec-row',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'chrome',
        '[attr.data-scope]': 'scope()',
        '[class.field]': 'field()',
        '[class.warn]': 'warn()',
        '[class.wrap]': 'wrap()',
        '[style.--spec-label-w]': 'labelWidth() || null',
    },
    template: `
        <span class="label">{{ label() }}</span>
        <span class="value">
            <ng-content><span class="text" [attr.title]="wrap() ? null : value() || null">{{ value() }}</span></ng-content>
        </span>
    `,
    styles: [`
        :host {
            display: flex;
            align-items: center;
            gap: 9px;
            min-width: 0;
            padding: 5px calc(var(--space-1) / 2);
            border-bottom: 1px solid var(--rule-ink-soft);
        }

        .label {
            flex: none;
            /* No token owns an inspector column, and a px default would be one. A ch keeps the
               column proportional to the type it holds. */
            width: var(--spec-label-w, 18ch);
            font-family: var(--font-sans);
            color: var(--t2);
        }

        .value {
            flex: 1;
            min-width: 0;
            display: flex;
            align-items: center;
            gap: var(--space-1);
        }

        .text {
            min-width: 0;
            font-family: var(--font-mono);
            color: var(--text);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        :host(.wrap) { align-items: flex-start; }

        :host(.wrap) .text {
            white-space: normal;
            overflow: visible;
            text-overflow: clip;
            overflow-wrap: anywhere;
        }

        /* The surface owns the sizes, so the lint can score them (ADR-138). */
        :host([data-surface="chrome"]) .label { font-size: var(--text-chrome); }
        :host([data-surface="chrome"]) .text { font-size: var(--text-chrome-sm); }

        :host(.warn) .text {
            color: var(--danger);
            font-weight: 700;
        }

        /* The box belongs to the value slot, not to the ink inside it: a projected input or select
           then *is* the field, rather than standing beside a sunken box that cannot be typed in.
           Drawn on the container, the marker is also only true where a control was projected. */
        :host(.field) .value {
            box-sizing: border-box;
            padding: 3px 7px;
            border: var(--border-field);
            border-radius: var(--radius-stamp);
            background: var(--paper-bright);
            box-shadow: var(--shadow-field-inset);
        }

        :host(.field) .text { flex: 1; }

        /* A field already carries its own edge, so the warning is drawn on that edge — the weight
           the plain variant uses would fight the inset. */
        :host(.field.warn) .value { border: 1px dashed var(--danger); }
        :host(.field.warn) .text { font-weight: 400; }
    `],
})
export class SpecRowComponent {
    readonly label = input.required<string>();
    /** Mono value. Ignored when the row projects content. */
    readonly value = input('');
    /** This row hosts a control. The box is a promise the row keeps — never decoration on ink. */
    readonly field = input(false, { transform: booleanAttribute });
    /** Something is missing or wrong — rust ink, dashed field edge. */
    readonly warn = input(false, { transform: booleanAttribute });
    /** Long inspector values may wrap when the consumer opts into a multi-line row. */
    readonly wrap = input(false, { transform: booleanAttribute });
    /** A CSS length for the label column; pass a token, not a measured pixel. */
    readonly labelWidth = input('');
    readonly scope = input<SpecScope>('document');
}
