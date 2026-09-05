import { booleanAttribute, Component, computed, effect, forwardRef, input, output, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let nextId = 0;

// ADR-260 clause 4 — the platform's own box, sized on the icon scale and inked by the global
// accent-color rule; the bench adds the row, the label, the hint and the same value surface the
// other fields have. The label is `label` or projected content, so a bold line over a small one
// is still one control.
@Component({
    selector: 'app-checkbox',
    host: { '[attr.data-surface]': 'surface()' },
    providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => CheckboxComponent), multi: true }],
    template: `
        <label class="row" [class.is-disabled]="isDisabled()">
            <input type="checkbox" class="box" [id]="fieldId()" [checked]="checked()" [disabled]="isDisabled()"
                   [attr.aria-label]="ariaLabel() || null"
                   (change)="onToggle($event)" (blur)="onBlur()">
            <span class="text">{{ label() }}<ng-content /></span>
        </label>
        @if (hint()) { <p class="hint">{{ hint() }}</p> }
    `,
    styles: [`
        :host { display: block; }

        .row {
            display: flex;
            align-items: flex-start;
            gap: var(--space-2);
            font-family: var(--font-sans);
            color: var(--text);
            cursor: pointer;
        }

        .row.is-disabled { opacity: .6; cursor: default; }

        .box {
            flex: none;
            width: var(--icon-sm);
            height: var(--icon-sm);
            margin: 0;
        }

        .text { min-width: 0; }

        .hint { margin: var(--space-1) 0 0; font-family: var(--font-sans); color: var(--t2); }

        :host([data-surface="paper"]) .row {
            font-size: var(--fs-ui);
            line-height: 1.4;
        }

        /* The box is centred on the first line of the label, whatever the line's height. */
        :host([data-surface="paper"]) .box { margin-top: calc((var(--fs-ui) * 1.4 - var(--icon-sm)) / 2); }

        :host([data-surface="paper"]) .hint { font-size: var(--fs-ui); }

        :host([data-surface="chrome"]) .row {
            font-family: var(--font-mono);
            font-size: var(--text-chrome-sm);
            line-height: 1.4;
        }

        :host([data-surface="chrome"]) .box { width: var(--icon-xs); height: var(--icon-xs); }

        :host([data-surface="chrome"]) .hint { font-size: var(--text-chrome-sm); }
    `],
})
export class CheckboxComponent implements ControlValueAccessor {
    label = input('');
    hint = input('');
    ariaLabel = input('');
    dense = input(false, { transform: booleanAttribute });
    disabled = input(false, { transform: booleanAttribute });
    inputId = input('');
    value = input<boolean | undefined>(undefined);
    valueChange = output<boolean>();

    private generatedId = `bench-checkbox-${nextId++}`;
    private cvaDisabled = signal(false);
    private onChange: (value: boolean) => void = () => { };
    private onTouched: () => void = () => { };
    private emitted: unknown = Symbol('nothing emitted');

    checked = signal(false);

    fieldId = computed(() => this.inputId() || this.generatedId);
    isDisabled = computed(() => this.disabled() || this.cvaDisabled());
    surface = computed(() => (this.dense() ? 'chrome' : 'paper'));

    constructor() {
        effect(() => {
            const incoming = this.value();
            if (incoming === undefined || Object.is(incoming, this.emitted)) return;
            this.checked.set(incoming);
        });
    }

    writeValue(value: boolean | null): void {
        this.checked.set(!!value);
    }

    registerOnChange(fn: (value: boolean) => void): void { this.onChange = fn; }

    registerOnTouched(fn: () => void): void { this.onTouched = fn; }

    setDisabledState(isDisabled: boolean): void { this.cvaDisabled.set(isDisabled); }

    onToggle(event: Event) {
        const on = (event.target as HTMLInputElement).checked;
        this.checked.set(on);
        this.emitted = on;
        this.onChange(on);
        this.valueChange.emit(on);
    }

    onBlur() { this.onTouched(); }
}
