import { TestBed } from '@angular/core/testing';
import { BrassHookComponent } from './brass-hook.component';

describe('BrassHookComponent', () => {
    async function render() {
        const fixture = TestBed.createComponent(BrassHookComponent);
        await fixture.whenStable();
        return fixture;
    }

    it('draws the shank, the hook curve and the screw head', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        expect(svg.querySelectorAll('rect').length).toBe(1);
        expect(svg.querySelectorAll('circle').length).toBe(2);
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

    it('is taller than it is wide, because it hangs', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        expect(svg.getAttribute('width')).toBe('14');
        expect(svg.getAttribute('height')).toBe('18');
    });

    it('is always hidden from assistive tech', async () => {
        const host = (await render()).nativeElement as HTMLElement;
        expect(host.getAttribute('aria-hidden')).toBe('true');
        expect(host.getAttribute('role')).toBeNull();
    });
});
