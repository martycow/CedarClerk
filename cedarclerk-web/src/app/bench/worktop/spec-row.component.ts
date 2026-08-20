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
        '[style.--spec-label-w]': 'labelWidth() || null',
    },
    template: `
        <span class="label">{{ label() }}</span>
        <span class="value">
            <ng-content><span class="text">{{ value() }}</span></ng-content>
        </span>
    `,
    styles: [`
        :host {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
            padding: var(--space-1) calc(var(--space-1) / 2);
            border-bottom: 1px solid var(--rule-ink-soft);
        }

        .label {
            flex: none;
            /* No token owns an inspector column, and a px default would be one. A ch keeps the
               column proportional to the type it holds. */
            width: var(--spec-label-w, 13ch);
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

        /* The surface owns the sizes, so the lint can score them (ADR-138). */
        :host([data-surface="chrome"]) .label { font-size: var(--text-chrome); }
        :host([data-surface="chrome"]) .text { font-size: var(--text-chrome-sm); }

        :host(.warn) .text {
            color: var(--danger);
            font-weight: 700;
        }

        :host(.field) .text {
            flex: 1;
            box-sizing: border-box;
            padding: calc(var(--space-1) / 2) var(--space-1);
            border: var(--border-field);
            border-radius: var(--radius-field);
            background: var(--paper-bright);
            box-shadow: var(--shadow-field-inset);
        }

        /* A field already carries its own edge, so the warning is drawn on that edge — the weight
           the plain variant uses would fight the inset. */
        :host(.field.warn) .text {
            border: 1px dashed var(--danger);
            font-weight: 400;
        }
    `],
})
export class SpecRowComponent {
    readonly label = input.required<string>();
    /** Mono value. Ignored when the row projects content. */
    readonly value = input('');
    /** Draw the value as an editable paper field rather than as plain ink. */
    readonly field = input(false, { transform: booleanAttribute });
    /** Something is missing or wrong — rust ink, dashed field edge. */
    readonly warn = input(false, { transform: booleanAttribute });
    /** A CSS length for the label column; pass a token, not a measured pixel. */
    readonly labelWidth = input('');
    readonly scope = input<SpecScope>('document');
}
