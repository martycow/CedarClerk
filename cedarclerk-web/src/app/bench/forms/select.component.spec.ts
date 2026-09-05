import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { BenchSelectOption, SelectComponent } from './select.component';

const PRIORITIES: BenchSelectOption[] = [
    { value: 1, label: 'P1 — now' },
    { value: 2, label: 'P2 — next' },
    { value: 3, label: 'P3 — later', disabled: true },
];

const SLOTS: BenchSelectOption[] = [
    { value: null, label: 'None' },
    { value: 'Url', label: 'URL' },
    { value: 'WordCount', label: 'Word count' },
];

@Component({
    imports: [SelectComponent, FormsModule],
    template: `<app-select [options]="options" [(ngModel)]="slot"></app-select>`,
})
class ModelHost {
    options = SLOTS;
    slot = signal<string | null>('Url');
}

@Component({
    imports: [SelectComponent],
    template: `<app-select [options]="options" [value]="priority()" (valueChange)="priority.set($any($event))"></app-select>`,
})
class SignalHost {
    options = PRIORITIES;
    priority = signal<number>(2);
}

function pick(field: HTMLSelectElement, index: number) {
    field.selectedIndex = index;
    field.dispatchEvent(new Event('change'));
}

describe('bench Select', () => {
    let fixture: ComponentFixture<SelectComponent>;
    const field = () => fixture.nativeElement.querySelector('select') as HTMLSelectElement;

    beforeEach(() => {
        fixture = TestBed.createComponent(SelectComponent);
        fixture.componentRef.setInput('options', PRIORITIES);
        fixture.detectChanges();
    });

    it('is a bare paper field until a label or a hint is given', () => {
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('paper');
        expect(fixture.nativeElement.querySelector('label')).toBeNull();
        expect(fixture.nativeElement.querySelector('.hint')).toBeNull();
        expect(field().options.length).toBe(3);
    });

    it('binds the label to the field it labels', () => {
        fixture.componentRef.setInput('label', 'Priority');
        fixture.componentRef.setInput('hint', 'one is the loudest');
        fixture.detectChanges();
        const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
        expect(label.textContent?.trim()).toBe('Priority');
        expect(label.getAttribute('for')).toBe(field().id);
        expect(field().id).toBeTruthy();
        expect(fixture.nativeElement.querySelector('.hint').textContent.trim()).toBe('one is the loudest');
    });

    it('takes an explicit id when the consumer owns one', () => {
        fixture.componentRef.setInput('inputId', 'task-priority');
        fixture.detectChanges();
        expect(field().id).toBe('task-priority');
    });

    it('names itself without a visible label, and explains through a title', () => {
        expect(field().hasAttribute('aria-label')).toBe(false);
        expect(field().hasAttribute('title')).toBe(false);
        fixture.componentRef.setInput('ariaLabel', 'Priority');
        fixture.componentRef.setInput('title', 'Pro only');
        fixture.detectChanges();
        expect(field().getAttribute('aria-label')).toBe('Priority');
        expect(field().getAttribute('title')).toBe('Pro only');
    });

    it('moves to the chrome surface when dense', () => {
        fixture.componentRef.setInput('dense', true);
        fixture.detectChanges();
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('renders every option label and disables the ones told to', () => {
        const options = Array.from(field().options);
        expect(options.map(o => o.textContent?.trim())).toEqual(['P1 — now', 'P2 — next', 'P3 — later']);
        expect(options.map(o => o.disabled)).toEqual([false, false, true]);
    });

    it('disables the native field', () => {
        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();
        expect(field().disabled).toBe(true);
    });

    it('re-declares no focus ring and keeps no cb- name', () => {
        const css = (SelectComponent as any).ɵcmp.styles.join('');
        expect(css.length).toBeGreaterThan(500);
        expect(css).not.toContain('outline:');
        expect(css).not.toContain('focus-halo');
        expect(css).not.toContain('cb-');
    });

    describe('ControlValueAccessor', () => {
        it('writeValue selects the option carrying that value, whatever its type', () => {
            fixture.componentInstance.writeValue(3);
            fixture.detectChanges();
            expect(field().selectedIndex).toBe(2);
            fixture.componentInstance.writeValue(1);
            fixture.detectChanges();
            expect(field().selectedIndex).toBe(0);
        });

        // ADR-260 clause 3: a native select under ngModel hands back '2' for 2. This one does not.
        it('hands the registered onChange the typed value the consumer put in', () => {
            const seen: unknown[] = [];
            fixture.componentInstance.registerOnChange(v => seen.push(v));
            pick(field(), 0);
            pick(field(), 1);
            expect(seen).toEqual([1, 2]);
        });

        it('reports the blur to the registered onTouched', () => {
            let touched = 0;
            fixture.componentInstance.registerOnTouched(() => touched++);
            field().dispatchEvent(new Event('blur'));
            expect(touched).toBe(1);
        });

        it('obeys setDisabledState without an input binding', () => {
            fixture.componentInstance.setDisabledState(true);
            fixture.detectChanges();
            expect(field().disabled).toBe(true);
        });
    });

    describe('[(ngModel)]', () => {
        it('shows the model, including a null option, and writes back what is picked', async () => {
            const host = TestBed.createComponent(ModelHost);
            await host.whenStable();
            const bound = host.nativeElement.querySelector('select') as HTMLSelectElement;
            expect(bound.selectedIndex).toBe(1);

            pick(bound, 0);
            expect(host.componentInstance.slot()).toBeNull();
            pick(bound, 2);
            expect(host.componentInstance.slot()).toBe('WordCount');
        });

        it('follows a model the host changes after init', async () => {
            const host = TestBed.createComponent(ModelHost);
            await host.whenStable();
            host.componentInstance.slot.set('WordCount');
            await host.whenStable();
            expect((host.nativeElement.querySelector('select') as HTMLSelectElement).selectedIndex).toBe(2);
        });
    });

    describe('[value] / (valueChange)', () => {
        it('mirrors a signal host both ways and keeps the number a number', async () => {
            const host = TestBed.createComponent(SignalHost);
            await host.whenStable();
            const bound = host.nativeElement.querySelector('select') as HTMLSelectElement;
            expect(bound.selectedIndex).toBe(1);

            pick(bound, 0);
            expect(host.componentInstance.priority()).toBe(1);

            host.componentInstance.priority.set(3);
            await host.whenStable();
            expect(bound.selectedIndex).toBe(2);
        });
    });
});
