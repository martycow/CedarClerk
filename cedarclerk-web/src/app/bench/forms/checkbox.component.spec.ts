import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { CheckboxComponent } from './checkbox.component';

@Component({
    imports: [CheckboxComponent, FormsModule],
    template: `<app-checkbox [(ngModel)]="on"><b>Show me</b><small>in the commons</small></app-checkbox>`,
})
class ModelHost {
    on = signal(true);
}

@Component({
    imports: [CheckboxComponent],
    template: `<app-checkbox label="Public" [value]="on()" (valueChange)="on.set($event)"></app-checkbox>`,
})
class SignalHost {
    on = signal(false);
}

function flip(box: HTMLInputElement) {
    box.checked = !box.checked;
    box.dispatchEvent(new Event('change'));
}

describe('bench Checkbox', () => {
    let fixture: ComponentFixture<CheckboxComponent>;
    const box = () => fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;

    beforeEach(() => {
        fixture = TestBed.createComponent(CheckboxComponent);
        fixture.detectChanges();
    });

    it('is an unchecked paper row wrapped in its own label', () => {
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('paper');
        expect(box().checked).toBe(false);
        expect(box().closest('label')).not.toBeNull();
        expect(fixture.nativeElement.querySelector('.hint')).toBeNull();
    });

    it('draws the label text and the hint', () => {
        fixture.componentRef.setInput('label', 'Case-sensitive');
        fixture.componentRef.setInput('hint', 'matches only this spelling');
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('.text').textContent.trim()).toBe('Case-sensitive');
        expect(fixture.nativeElement.querySelector('.hint').textContent.trim()).toBe('matches only this spelling');
    });

    it('takes an explicit id, and a name for a box with no visible label', () => {
        expect(box().hasAttribute('aria-label')).toBe(false);
        fixture.componentRef.setInput('inputId', 'cc-discovery');
        fixture.componentRef.setInput('ariaLabel', 'Discoverable');
        fixture.detectChanges();
        expect(box().id).toBe('cc-discovery');
        expect(box().getAttribute('aria-label')).toBe('Discoverable');
    });

    it('moves to the chrome surface when dense', () => {
        fixture.componentRef.setInput('dense', true);
        fixture.detectChanges();
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('disables the native box', () => {
        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();
        expect(box().disabled).toBe(true);
    });

    // ADR-260 clause 4: the platform control is kept, so the sheet paints no box of its own.
    it('paints no stock on the box and re-declares no focus ring', () => {
        const css = (CheckboxComponent as any).ɵcmp.styles.join('');
        expect(css.length).toBeGreaterThan(300);
        expect(css).not.toContain('outline:');
        expect(css).not.toContain('appearance');
        expect(css).not.toMatch(/\.box[^}]*background/);
        expect(css).not.toContain('cb-');
    });

    describe('ControlValueAccessor', () => {
        it('writeValue checks and unchecks, and null reads as off', () => {
            fixture.componentInstance.writeValue(true);
            fixture.detectChanges();
            expect(box().checked).toBe(true);
            fixture.componentInstance.writeValue(null);
            fixture.detectChanges();
            expect(box().checked).toBe(false);
        });

        it('reports every flip to the registered onChange', () => {
            const seen: unknown[] = [];
            fixture.componentInstance.registerOnChange(v => seen.push(v));
            flip(box());
            flip(box());
            expect(seen).toEqual([true, false]);
        });

        it('reports the blur to the registered onTouched', () => {
            let touched = 0;
            fixture.componentInstance.registerOnTouched(() => touched++);
            box().dispatchEvent(new Event('blur'));
            expect(touched).toBe(1);
        });

        it('obeys setDisabledState without an input binding', () => {
            fixture.componentInstance.setDisabledState(true);
            fixture.detectChanges();
            expect(box().disabled).toBe(true);
        });
    });

    describe('[(ngModel)] with a projected label', () => {
        it('shows the model, projects the rich label, and writes back a flip', async () => {
            const host = TestBed.createComponent(ModelHost);
            await host.whenStable();
            const bound = host.nativeElement.querySelector('input') as HTMLInputElement;
            expect(bound.checked).toBe(true);
            expect(host.nativeElement.querySelector('.text b').textContent).toBe('Show me');

            flip(bound);
            expect(host.componentInstance.on()).toBe(false);
        });

        it('follows a model the host changes after init', async () => {
            const host = TestBed.createComponent(ModelHost);
            await host.whenStable();
            host.componentInstance.on.set(false);
            await host.whenStable();
            expect((host.nativeElement.querySelector('input') as HTMLInputElement).checked).toBe(false);
        });
    });

    describe('[value] / (valueChange)', () => {
        it('mirrors a signal host both ways', async () => {
            const host = TestBed.createComponent(SignalHost);
            await host.whenStable();
            const bound = host.nativeElement.querySelector('input') as HTMLInputElement;
            expect(bound.checked).toBe(false);

            flip(bound);
            expect(host.componentInstance.on()).toBe(true);
            await host.whenStable();

            host.componentInstance.on.set(false);
            await host.whenStable();
            expect(bound.checked).toBe(false);
        });
    });
});
