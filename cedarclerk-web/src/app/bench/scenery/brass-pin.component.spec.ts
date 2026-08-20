import { TestBed } from '@angular/core/testing';
import { BrassPinComponent } from './brass-pin.component';

// A pin drawn with an empty paint reads as a missing glyph, not as an error, so every assertion
// here is against rendered markup rather than against the class.
describe('BrassPinComponent', () => {
    async function render(label?: string) {
        const fixture = TestBed.createComponent(BrassPinComponent);
        if (label !== undefined) fixture.componentRef.setInput('label', label);
        await fixture.whenStable();
        return fixture;
    }

    it('draws the dome, its edge, the highlight and the cast shadow', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        expect(svg.querySelectorAll('circle').length).toBe(3);
        expect(svg.querySelectorAll('ellipse').length).toBe(1);
    });

    it('paints every fill and stroke through a token', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        const paints = [...svg.querySelectorAll('*')].flatMap(el =>
            ['fill', 'stroke'].map(a => el.getAttribute(a)).filter((v): v is string => v !== null));
        expect(paints.length).toBeGreaterThan(0);
        for (const paint of paints) {
            if (paint === 'none') continue;
            expect(paint).toMatch(/var\(--brass/);
        }
        expect(svg.outerHTML).not.toMatch(/#[0-9a-f]{3}|rgba?\(/i);
    });

    it('stays at its drawn size — brass is hardware, not a scalable icon', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        expect(svg.getAttribute('width')).toBe('14');
        expect(svg.getAttribute('height')).toBe('14');
    });

    it('is hidden from assistive tech while it is only decoration', async () => {
        const host = (await render()).nativeElement as HTMLElement;
        expect(host.getAttribute('aria-hidden')).toBe('true');
        expect(host.getAttribute('role')).toBeNull();
        expect(host.getAttribute('aria-label')).toBeNull();
    });

    it('announces itself once a label gives it meaning', async () => {
        const host = (await render('Pinned')).nativeElement as HTMLElement;
        expect(host.getAttribute('role')).toBe('img');
        expect(host.getAttribute('aria-label')).toBe('Pinned');
        expect(host.getAttribute('aria-hidden')).toBeNull();
    });
});
