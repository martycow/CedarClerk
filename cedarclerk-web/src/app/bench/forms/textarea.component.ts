import { booleanAttribute, Component, computed, effect, forwardRef, input, numberAttribute, output, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let nextId = 0;

// The multi-line sibling of app-input (ADR-260 clause 2): same label, hint, id, density and
// ControlValueAccessor surface, plus the rows and the limit a long field carries.
@Component({
    selector: 'app-textarea',
    host: { '[attr.data-surface]': 'surface()' },
    providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => TextareaComponent), multi: true }],
    template: `
        @if (label()) { <label class="label" [attr.for]="fieldId()">{{ label() }}</label> }
        <textarea class="field" [class.serif]="serif()" [id]="fieldId()" [rows]="rows()"
                  [attr.placeholder]="placeholder() || null" [attr.maxlength]="maxlength() || null"
                  [attr.aria-label]="ariaLabel() || null" [attr.title]="title() || null"
                  [disabled]="isDisabled()"
                  [value]="text()" (input)="onInput($event)" (blur)="onBlur()"></textarea>
        @if (hint()) { <p class="hint">{{ hint() }}</p> }
    `,
    styles: [`
        :host { display: block; }

        .label {
            display: block;
            font-family: var(--font-sans);
            font-weight: 700;
            letter-spacing: .07em;
            text-transform: uppercase;
            color: var(--t2);
            margin-bottom: var(--space-1);
        }

        /* The same face app-input draws; the global textarea rule sits at (0,0,1) and this
           outranks it, so the component owns box, stock, radius, inset, type and density. */
        .field {
            display: block;
            width: 100%;
            box-sizing: border-box;
            border: var(--border-field);
            border-radius: var(--radius-field);
            background: var(--paper-bright);
            color: var(--text);
            font-family: var(--font-sans);
            line-height: 1.5;
            resize: vertical;
        }

        .field:not(:focus-visible) { box-shadow: var(--shadow-field-inset); }

        .field:disabled { opacity: .6; cursor: default; }
        .field.serif { font-family: var(--font-serif); }

        .hint { margin: var(--space-1) 0 0; font-family: var(--font-sans); color: var(--t2); }

        :host([data-surface="paper"]) .field {
            min-height: var(--hit-surface, var(--hit-target));
            padding: var(--space-2) var(--space-3);
            font-size: var(--fs-ui);
        }

        :host([data-surface="paper"]) .label,
        :host([data-surface="paper"]) .hint { font-size: var(--fs-ui); }

        :host([data-surface="chrome"]) .field {
            min-height: var(--hit-surface, var(--hit-chrome));
            padding: var(--space-1) var(--space-2);
            border-radius: var(--radius-stamp);
            font-family: var(--font-mono);
            font-size: var(--text-chrome-sm);
        }

        :host([data-surface="chrome"]) .label,
        :host([data-surface="chrome"]) .hint { font-size: var(--text-chrome-sm); }
    `],
})
export class TextareaComponent implements ControlValueAccessor {
    label = input('');
    hint = input('');
    placeholder = input('');
    title = input('');
    ariaLabel = input('');
    rows = input(3, { transform: numberAttribute });
    maxlength = input<number | string | null>(null);
    serif = input(false, { transform: booleanAttribute });
    dense = input(false, { transform: booleanAttribute });
    disabled = input(false, { transform: booleanAttribute });
    inputId = input('');
    value = input<string | null | undefined>(undefined);
    valueChange = output<string>();

    private generatedId = `bench-textarea-${nextId++}`;
    private cvaDisabled = signal(false);
    private onChange: (value: string) => void = () => { };
    private onTouched: () => void = () => { };
    private emitted: unknown = Symbol('nothing emitted');

    text = signal('');

    fieldId = computed(() => this.inputId() || this.generatedId);
    isDisabled = computed(() => this.disabled() || this.cvaDisabled());
    surface = computed(() => (this.dense() ? 'chrome' : 'paper'));

    constructor() {
        effect(() => {
            const incoming = this.value();
            if (incoming === undefined || Object.is(incoming, this.emitted)) return;
            this.text.set(incoming ?? '');
        });
    }

    writeValue(value: string | null): void {
        this.text.set(value ?? '');
    }

    registerOnChange(fn: (value: string) => void): void { this.onChange = fn; }

    registerOnTouched(fn: () => void): void { this.onTouched = fn; }

    setDisabledState(isDisabled: boolean): void { this.cvaDisabled.set(isDisabled); }

    onInput(event: Event) {
        const raw = (event.target as HTMLTextAreaElement).value;
        this.text.set(raw);
        this.emitted = raw;
        this.onChange(raw);
        this.valueChange.emit(raw);
    }

    onBlur() { this.onTouched(); }
}
