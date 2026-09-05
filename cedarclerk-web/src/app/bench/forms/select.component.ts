import { booleanAttribute, Component, computed, effect, forwardRef, input, output, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export type BenchSelectValue = string | number | null;

export interface BenchSelectOption<T extends BenchSelectValue = BenchSelectValue> {
    value: T;
    label: string;
    disabled?: boolean;
}

let nextId = 0;

// ADR-260 clause 3 — options are an array keyed by index, never projected <option> children: the
// value handed back is the one the consumer put in, so a numeric priority stays a number, and the
// selection does not depend on the order a parent's @for and this writeValue happen to run in.
@Component({
    selector: 'app-select',
    host: { '[attr.data-surface]': 'surface()' },
    providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SelectComponent), multi: true }],
    template: `
        @if (label()) { <label class="label" [attr.for]="fieldId()">{{ label() }}</label> }
        <select class="field" [id]="fieldId()" [disabled]="isDisabled()"
                [attr.aria-label]="ariaLabel() || null" [attr.title]="title() || null"
                (change)="onPick($event)" (blur)="onBlur()">
            @for (o of options(); track $index) {
                <option [value]="$index" [selected]="$index === selectedIndex()" [disabled]="o.disabled ?? false">{{ o.label }}</option>
            }
        </select>
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

        /* The same face app-input draws; the global select rule sits at (0,0,1) and this outranks
           it, so the component owns box, stock, radius, inset, type and density. */
        .field {
            width: 100%;
            box-sizing: border-box;
            border: var(--border-field);
            border-radius: var(--radius-field);
            background: var(--paper-bright);
            color: var(--text);
            font-family: var(--font-sans);
            cursor: pointer;
        }

        .field:not(:focus-visible) { box-shadow: var(--shadow-field-inset); }

        .field:disabled { opacity: .6; cursor: default; }

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
export class SelectComponent implements ControlValueAccessor {
    label = input('');
    hint = input('');
    title = input('');
    ariaLabel = input('');
    options = input<readonly BenchSelectOption[]>([]);
    dense = input(false, { transform: booleanAttribute });
    disabled = input(false, { transform: booleanAttribute });
    inputId = input('');
    value = input<BenchSelectValue | undefined>(undefined);
    valueChange = output<BenchSelectValue>();

    private generatedId = `bench-select-${nextId++}`;
    private cvaDisabled = signal(false);
    private onChange: (value: BenchSelectValue) => void = () => { };
    private onTouched: () => void = () => { };
    private emitted: unknown = Symbol('nothing emitted');

    current = signal<BenchSelectValue>(null);

    fieldId = computed(() => this.inputId() || this.generatedId);
    isDisabled = computed(() => this.disabled() || this.cvaDisabled());
    surface = computed(() => (this.dense() ? 'chrome' : 'paper'));
    selectedIndex = computed(() => this.options().findIndex(o => Object.is(o.value, this.current())));

    constructor() {
        effect(() => {
            const incoming = this.value();
            if (incoming === undefined || Object.is(incoming, this.emitted)) return;
            this.current.set(incoming);
        });
    }

    writeValue(value: BenchSelectValue | undefined): void {
        this.current.set(value === undefined ? null : value);
    }

    registerOnChange(fn: (value: BenchSelectValue) => void): void { this.onChange = fn; }

    registerOnTouched(fn: () => void): void { this.onTouched = fn; }

    setDisabledState(isDisabled: boolean): void { this.cvaDisabled.set(isDisabled); }

    onPick(event: Event) {
        const picked = this.options()[(event.target as HTMLSelectElement).selectedIndex];
        const out = picked ? picked.value : null;
        this.current.set(out);
        this.emitted = out;
        this.onChange(out);
        this.valueChange.emit(out);
    }

    onBlur() { this.onTouched(); }
}
