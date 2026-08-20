import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { InputComponent } from './input.component';

// Signals on both hosts because that is how the app binds it — tag-picker.component.html holds
// [(ngModel)] over a signal, and under zoneless change detection a plain property would never
// reach the field on its own.
@Component({
    imports: [InputComponent, FormsModule],
    template: `<app-input [(ngModel)]="model"></app-input>`,
})
class ModelHost {
    model = signal('first');
}

@Component({
    imports: [InputComponent, FormsModule],
    template: `<app-input type="number" [(ngModel)]="quota"></app-input>`,
})
class NumberHost {
    quota = signal<number | null>(3);
}

function type(field: HTMLInputElement, value: string) {
    field.value = value;
    field.dispatchEvent(new Event('input'));
}

describe('bench Input', () => {
    let fixture: ComponentFixture<InputComponent>;
    const field = () => fixture.nativeElement.querySelector('input') as HTMLInputElement;

    beforeEach(() => {
        fixture = TestBed.createComponent(InputComponent);
        fixture.detectChanges();
    });

    it('is a bare paper field until a label or a hint is given', () => {
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('paper');
        expect(fixture.nativeElement.querySelector('label')).toBeNull();
        expect(fixture.nativeElement.querySelector('.hint')).toBeNull();
        expect(field().getAttribute('type')).toBe('text');
    });

    it('binds the label to the field it labels', () => {
        fixture.componentRef.setInput('label', 'Email');
        fixture.componentRef.setInput('hint', 'we never post it anywhere');
        fixture.detectChanges();
        const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
        expect(label.textContent?.trim()).toBe('Email');
        expect(label.getAttribute('for')).toBe(field().id);
        expect(field().id).toBeTruthy();
        expect(fixture.nativeElement.querySelector('.hint').textContent.trim()).toBe('we never post it anywhere');
    });

    it('takes an explicit id when the consumer owns one', () => {
        fixture.componentRef.setInput('inputId', 'signup-email');
        fixture.detectChanges();
        expect(field().id).toBe('signup-email');
    });

    // ADR-138 — dense is the chrome density; a field on paper is the default.
    it('moves to the chrome surface when dense', () => {
        fixture.componentRef.setInput('dense', true);
        fixture.detectChanges();
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('switches to the reading serif for a long-form field', () => {
        fixture.componentRef.setInput('serif', true);
        fixture.detectChanges();
        expect(field().classList.contains('serif')).toBe(true);
    });

    it('passes the type and the placeholder through', () => {
        fixture.componentRef.setInput('type', 'password');
        fixture.componentRef.setInput('placeholder', 'at least ten characters');
        fixture.detectChanges();
        expect(field().getAttribute('type')).toBe('password');
        expect(field().getAttribute('placeholder')).toBe('at least ten characters');
    });

    it('disables the native field', () => {
        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();
        expect(field().disabled).toBe(true);
    });

    it('re-declares no focus ring and keeps no cb- name', () => {
        const css = (InputComponent as any).ɵcmp.styles.join('');
        expect(css.length).toBeGreaterThan(500);
        expect(css).not.toContain('outline:');
        expect(css).not.toContain('focus-halo');
        expect(css).not.toContain('brass-edge');
        expect(css).not.toContain('cb-');
    });

    describe('ControlValueAccessor', () => {
        it('writeValue fills the field, and null empties it', () => {
            fixture.componentInstance.writeValue('written');
            fixture.detectChanges();
            expect(field().value).toBe('written');
            fixture.componentInstance.writeValue(null);
            fixture.detectChanges();
            expect(field().value).toBe('');
        });

        it('reports every keystroke to the registered onChange', () => {
            const seen: unknown[] = [];
            fixture.componentInstance.registerOnChange(v => seen.push(v));
            type(field(), 'ab');
            type(field(), 'abc');
            expect(seen).toEqual(['ab', 'abc']);
        });

        it('reports the blur to the registered onTouched', () => {
            let touched = 0;
            fixture.componentInstance.registerOnTouched(() => touched++);
            expect(touched).toBe(0);
            field().dispatchEvent(new Event('blur'));
            expect(touched).toBe(1);
        });

        it('obeys setDisabledState without an input binding', () => {
            fixture.componentInstance.setDisabledState(true);
            fixture.detectChanges();
            expect(field().disabled).toBe(true);
            fixture.componentInstance.setDisabledState(false);
            fixture.detectChanges();
            expect(field().disabled).toBe(false);
        });
    });

    // The reason the component implements the interface at all: twenty templates bind ngModel,
    // and ngModel reaches a component through nothing else.
    describe('[(ngModel)]', () => {
        it('shows the model and writes back what is typed', async () => {
            const host = TestBed.createComponent(ModelHost);
            await host.whenStable();
            const bound = host.nativeElement.querySelector('input') as HTMLInputElement;
            expect(bound.value).toBe('first');

            type(bound, 'second');
            expect(host.componentInstance.model()).toBe('second');
        });

        it('follows a model the host changes after init', async () => {
            const host = TestBed.createComponent(ModelHost);
            await host.whenStable();
            host.componentInstance.model.set('third');
            await host.whenStable();
            expect((host.nativeElement.querySelector('input') as HTMLInputElement).value).toBe('third');
        });

        it('hands a numeric model a number, not a string', async () => {
            const host = TestBed.createComponent(NumberHost);
            await host.whenStable();
            const bound = host.nativeElement.querySelector('input') as HTMLInputElement;
            expect(bound.value).toBe('3');

            type(bound, '12');
            expect(host.componentInstance.quota()).toBe(12);

            type(bound, '');
            expect(host.componentInstance.quota()).toBeNull();
        });
    });
});
