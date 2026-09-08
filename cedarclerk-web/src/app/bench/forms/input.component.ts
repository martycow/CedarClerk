import { booleanAttribute, Component, computed, effect, forwardRef, inject, input, output, signal } from '@angular/core';
import { LocaleService } from '../../core/i18n/locale.service';
import { IconComponent } from '../../shared/icon.component';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export type BenchInputType = 'text' | 'email' | 'password' | 'search' | 'number' | 'date' | 'url' | 'tel';

let nextId = 0;

// ControlValueAccessor rather than a value/valueChange pair alone: the app binds [(ngModel)] on
// twenty templates (tag-picker, settings, login, the project pages), and ngModel talks to a
// component only through this interface.
//
// ADR-138 — `dense` is the chrome density (mono, 11px, 30px box) and belongs to inspector rows
// inside a ShelfPanel; a field on paper is the default and holds the 44px target on its own,
// without leaning on its label to reach the floor.
@Component({
    selector: 'app-input',
    imports: [IconComponent],
    host: { '[attr.data-surface]': 'surface()' },
    providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => InputComponent), multi: true }],
    template: `
        @if (label()) { <div class="label-row"><label class="label" [attr.for]="fieldId()">{{ label() }}</label><ng-content select="[labelAction]" /></div> }
        <div class="field-wrap" [class.has-toggle]="revealable() && type() === 'password'">
        <input class="field" [class.serif]="serif()" [id]="fieldId()" [attr.type]="revealable() && revealed() && type() === 'password' ? 'text' : type()"
               [attr.placeholder]="placeholder() || null" [attr.autocomplete]="autocomplete() || null"
               [attr.maxlength]="maxlength() || null" [attr.aria-label]="ariaLabel() || null"
               [disabled]="isDisabled()"
               [value]="text()" (input)="onInput($event)" (blur)="onBlur()">
        @if (revealable() && type() === 'password') {
            <button class="reveal" type="button" [disabled]="isDisabled()" [attr.aria-pressed]="revealed()"
                [attr.aria-label]="revealed() ? t().authLayout.hidePassword : t().authLayout.showPassword"
                (click)="revealed.set(!revealed())">
                <app-icon [name]="revealed() ? 'eye-slash' : 'eye'" size="sm" />
            </button>
        }
        </div>
        @if (hint()) { <p class="hint">{{ hint() }}</p> }
    `,
    styles: [`
        :host { display: block; }

        .label-row { display: flex; align-items: baseline; justify-content: space-between; gap: var(--space-2); }
        .field-wrap { position: relative; }
        .has-toggle .field { padding-right: var(--hit-touch) !important; }
        .reveal { position: absolute; right: 0; top: 0; height: 100%; width: var(--hit-touch); display: grid; place-items: center; border: 0; background: transparent; color: var(--t2); cursor: pointer; border-radius: var(--radius-field); }
        .reveal:hover { color: var(--accent); }
        .label {
            display: block;
            font-family: var(--font-sans);
            font-weight: 700;
            letter-spacing: var(--field-label-spacing, .07em);
            text-transform: var(--field-label-transform, uppercase);
            color: var(--field-label-ink, var(--surface-ink-soft, var(--t2)));
            margin-bottom: var(--space-1);
        }

        /* The class is the opt-out from the global field face in styles.scss: that rule sits at
           (0,6,1) and nothing here could out-specify it, so it excludes .field instead and this
           component owns the whole face — box, stock, radius, inset, type and density. */
        .field {
            width: 100%;
            box-sizing: border-box;
            border: var(--border-field);
            border-radius: var(--radius-field);
            background: var(--paper-bright);
            color: var(--text);
            font-family: var(--font-sans);
        }

        /* Withheld while focused: the ADR-140 halo is a box-shadow and this one would out-specify
           the global rule that draws it. The mirror's brass border-and-wash focus is not ported. */
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
export class InputComponent implements ControlValueAccessor {
    readonly t = inject(LocaleService).t;
    readonly revealable = input(false, { transform: booleanAttribute });
    readonly revealed = signal(false);
    label = input('');
    hint = input('');
    placeholder = input('');
    type = input<BenchInputType>('text');
    /**
     * The browser's fill hint. A credential field that omits it is offered to the password manager
     * on heuristics alone, and `current-password` vs `new-password` is a distinction no heuristic
     * makes: one asks to fill, the other to generate.
     */
    autocomplete = input('');
    maxlength = input<number | string | null>(null);
    /** The name a screen reader announces when no visible label is drawn — a search box named by its placeholder. */
    ariaLabel = input('');
    /** Long-form field (a draft title) — switches to the reading serif. */
    serif = input(false, { transform: booleanAttribute });
    /** Chrome density: mono, 11px, for an inspector row inside a ShelfPanel only. */
    dense = input(false, { transform: booleanAttribute });
    disabled = input(false, { transform: booleanAttribute });
    inputId = input('');
    value = input<string | number | null | undefined>(undefined);
    valueChange = output<string | number | null>();

    private generatedId = `bench-input-${nextId++}`;
    private cvaDisabled = signal(false);
    private onChange: (value: string | number | null) => void = () => { };
    private onTouched: () => void = () => { };
    // The last value this component itself put out. A consumer echoing it back through [(value)]
    // must not rewrite the field mid-edit: "1." would come home as 1 and eat the keystroke.
    private emitted: unknown = Symbol('nothing emitted');

    text = signal('');

    fieldId = computed(() => this.inputId() || this.generatedId);
    isDisabled = computed(() => this.disabled() || this.cvaDisabled());
    surface = computed(() => (this.dense() ? 'chrome' : 'paper'));

    constructor() {
        effect(() => {
            const incoming = this.value();
            if (incoming === undefined || Object.is(incoming, this.emitted)) return;
            this.text.set(incoming === null ? '' : String(incoming));
        });
    }

    writeValue(value: string | number | null): void {
        this.text.set(value === null || value === undefined ? '' : String(value));
    }

    registerOnChange(fn: (value: string | number | null) => void): void { this.onChange = fn; }

    registerOnTouched(fn: () => void): void { this.onTouched = fn; }

    setDisabledState(isDisabled: boolean): void { this.cvaDisabled.set(isDisabled); }

    onInput(event: Event) {
        const raw = (event.target as HTMLInputElement).value;
        this.text.set(raw);
        // Angular's own NumberValueAccessor hands a number to a numeric model; a ported field that
        // handed back a string would turn `quota: number` into a string on first keystroke.
        const out = this.type() === 'number' ? (raw === '' ? null : Number(raw)) : raw;
        this.emitted = out;
        this.onChange(out);
        this.valueChange.emit(out);
    }

    onBlur() { this.onTouched(); }
}
