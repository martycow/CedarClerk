import { TestBed } from '@angular/core/testing';
import { BrassHookComponent } from './brass-hook.component';

describe('BrassHookComponent', () => {
    async function render() {
        const fixture = TestBed.createComponent(BrassHookComponent);
        await fixture.whenStable();
        return fixture;
    }

    it('draws the screw head and one continuous forged J', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        expect(svg.querySelectorAll('circle').length).toBe(1);
        expect(svg.querySelectorAll('path').length).toBe(2);
        expect(svg.querySelector('defs linearGradient#brassHook')).not.toBeNull();
    });

    it('paints the metal through the brass tokens, plus exactly one specular glint', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        // Queried as `defs stop`, not `linearGradient stop`: jsdom matches camelCase SVG type
        // selectors case-sensitively after lowercasing, so the camelCase form finds nothing here.
        const stops = [...svg.querySelectorAll('defs stop')].map(s => s.getAttribute('stop-color'));
        expect(stops).toEqual(['var(--brass-hi)', 'var(--brass)']);
        const screw = svg.querySelector('circle')!;
        expect(screw.getAttribute('fill')).toBe('url(#brassHook)');
        expect(screw.getAttribute('stroke')).toBe('var(--brass-edge)');
        // The highlight stroke is a glint of light on metal, deliberately not a themable ink —
        // the one literal colour the component is allowed.
        const literals = [...svg.querySelectorAll('*')]
            .flatMap(el => ['fill', 'stroke'].map(a => el.getAttribute(a)))
            .filter((v): v is string => v !== null && /#[0-9a-f]{3}|rgba?\(/i.test(v));
        expect(literals).toEqual(['#FFF3D6']);
    });

    it('is taller than it is wide, because it hangs', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        expect(svg.getAttribute('width')).toBe('14');
        expect(svg.getAttribute('height')).toBe('20');
    });

    it('is always hidden from assistive tech', async () => {
        const host = (await render()).nativeElement as HTMLElement;
        expect(host.getAttribute('aria-hidden')).toBe('true');
        expect(host.getAttribute('role')).toBeNull();
    });
});
