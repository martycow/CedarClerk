import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { TextareaComponent } from './textarea.component';

@Component({
    imports: [TextareaComponent, FormsModule],
    template: `<app-textarea [(ngModel)]="model"></app-textarea>`,
})
class ModelHost {
    model = signal('first');
}

@Component({
    imports: [TextareaComponent],
    template: `<app-textarea [value]="text()" (valueChange)="text.set($event)"></app-textarea>`,
})
class SignalHost {
    text = signal('draft');
}

function type(field: HTMLTextAreaElement, value: string) {
    field.value = value;
    field.dispatchEvent(new Event('input'));
}

describe('bench Textarea', () => {
    let fixture: ComponentFixture<TextareaComponent>;
    const field = () => fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;

    beforeEach(() => {
        fixture = TestBed.createComponent(TextareaComponent);
        fixture.detectChanges();
    });

    it('is a bare paper field of three rows until told otherwise', () => {
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('paper');
        expect(fixture.nativeElement.querySelector('label')).toBeNull();
        expect(fixture.nativeElement.querySelector('.hint')).toBeNull();
        expect(field().rows).toBe(3);
    });

    it('binds the label to the field it labels', () => {
        fixture.componentRef.setInput('label', 'Description');
        fixture.componentRef.setInput('hint', 'plain text');
        fixture.detectChanges();
        const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
        expect(label.textContent?.trim()).toBe('Description');
        expect(label.getAttribute('for')).toBe(field().id);
        expect(field().id).toBeTruthy();
        expect(fixture.nativeElement.querySelector('.hint').textContent.trim()).toBe('plain text');
    });

    it('takes an explicit id when the consumer owns one', () => {
        fixture.componentRef.setInput('inputId', 'cc-sig');
        fixture.detectChanges();
        expect(field().id).toBe('cc-sig');
    });

    it('passes rows, placeholder, the limit, the name and the title through', () => {
        expect(field().hasAttribute('maxlength')).toBe(false);
        expect(field().hasAttribute('aria-label')).toBe(false);
        expect(field().hasAttribute('title')).toBe(false);
        fixture.componentRef.setInput('rows', '5');
        fixture.componentRef.setInput('placeholder', 'What needs doing');
        fixture.componentRef.setInput('maxlength', 1000);
        fixture.componentRef.setInput('ariaLabel', 'Description');
        fixture.componentRef.setInput('title', 'Pro only');
        fixture.detectChanges();
        expect(field().rows).toBe(5);
        expect(field().getAttribute('placeholder')).toBe('What needs doing');
        expect(field().getAttribute('maxlength')).toBe('1000');
        expect(field().getAttribute('aria-label')).toBe('Description');
        expect(field().getAttribute('title')).toBe('Pro only');
    });

    it('moves to the chrome surface when dense, and to the serif when long-form', () => {
        fixture.componentRef.setInput('dense', true);
        fixture.componentRef.setInput('serif', true);
        fixture.detectChanges();
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('chrome');
        expect(field().classList.contains('serif')).toBe(true);
    });

    it('disables the native field', () => {
        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();
        expect(field().disabled).toBe(true);
    });

    it('re-declares no focus ring and keeps no cb- name', () => {
        const css = (TextareaComponent as any).ɵcmp.styles.join('');
        expect(css.length).toBeGreaterThan(500);
        expect(css).not.toContain('outline:');
        expect(css).not.toContain('focus-halo');
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
        it('shows the model and writes back what is typed', async () => {
            const host = TestBed.createComponent(ModelHost);
            await host.whenStable();
            const bound = host.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
            expect(bound.value).toBe('first');
            type(bound, 'second');
            expect(host.componentInstance.model()).toBe('second');
        });

        it('follows a model the host changes after init', async () => {
            const host = TestBed.createComponent(ModelHost);
            await host.whenStable();
            host.componentInstance.model.set('third');
            await host.whenStable();
            expect((host.nativeElement.querySelector('textarea') as HTMLTextAreaElement).value).toBe('third');
        });
    });

    describe('[value] / (valueChange)', () => {
        it('mirrors a signal host both ways without eating a keystroke', async () => {
            const host = TestBed.createComponent(SignalHost);
            await host.whenStable();
            const bound = host.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
            expect(bound.value).toBe('draft');
            type(bound, 'draft two');
            await host.whenStable();
            expect(host.componentInstance.text()).toBe('draft two');
            expect(bound.value).toBe('draft two');
        });
    });
});
