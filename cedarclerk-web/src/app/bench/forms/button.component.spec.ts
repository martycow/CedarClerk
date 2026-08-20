import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ButtonComponent } from './button.component';

@Component({
    imports: [ButtonComponent],
    template: `<app-button (clicked)="hits = hits + 1">Publish</app-button>`,
})
class ProjectionHost {
    hits = 0;
}

describe('bench Button', () => {
    let fixture: ComponentFixture<ButtonComponent>;
    const button = () => fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    beforeEach(() => {
        fixture = TestBed.createComponent(ButtonComponent);
        fixture.detectChanges();
    });

    it('defaults to a pine md button that is not a submit', () => {
        expect(button().className).toContain('pine');
        expect(button().className).toContain('md');
        expect(button().type).toBe('button');
    });

    // The surface decides the hit target and the type size (ADR-138), and it is read off the
    // variant: rail is painted on wood, everything else sits on paper.
    for (const [variant, surface] of [['pine', 'paper'], ['paper', 'paper'], ['danger', 'paper'], ['rail', 'chrome']] as const) {
        it(`marks ${variant} as ${surface}`, () => {
            fixture.componentRef.setInput('variant', variant);
            fixture.detectChanges();
            expect(button().className).toContain(variant);
            expect(fixture.nativeElement.getAttribute('data-surface')).toBe(surface);
        });
    }

    for (const size of ['md', 'sm'] as const) {
        it(`carries the ${size} size class`, () => {
            fixture.componentRef.setInput('size', size);
            fixture.detectChanges();
            expect(button().className).toContain(size);
        });
    }

    // Both halves of the port's own rules, read off the compiled stylesheet. The length assertion
    // is the control: an empty styles array would satisfy every not.toContain below it.
    it('re-declares no focus ring and keeps no cb- name', () => {
        const css = (ButtonComponent as any).ɵcmp.styles.join('');
        expect(css.length).toBeGreaterThan(500);
        expect(css).not.toContain('outline:');
        expect(css).not.toContain('focus-halo');
        expect(css).not.toContain('brass-edge');
        expect(css).not.toContain('cb-');
        expect(button().className).not.toContain('cb-');
    });

    it('disables the native button and stops emitting', () => {
        let hits = 0;
        fixture.componentInstance.clicked.subscribe(() => hits++);
        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();
        expect(button().disabled).toBe(true);
        button().click();
        expect(hits).toBe(0);
    });

    it('emits clicked and projects its content', () => {
        const host = TestBed.createComponent(ProjectionHost);
        host.detectChanges();
        const inner = host.nativeElement.querySelector('button') as HTMLButtonElement;
        expect(inner.textContent?.trim()).toBe('Publish');
        inner.click();
        host.detectChanges();
        expect(host.componentInstance.hits).toBe(1);
    });

    it('takes a title and a submit type', () => {
        fixture.componentRef.setInput('title', 'Send it');
        fixture.componentRef.setInput('type', 'submit');
        fixture.detectChanges();
        expect(button().getAttribute('title')).toBe('Send it');
        expect(button().type).toBe('submit');
    });
});
