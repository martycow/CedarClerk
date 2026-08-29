import { TestBed } from '@angular/core/testing';
import { BrassNailComponent } from './brass-nail.component';

describe('BrassNailComponent', () => {
    async function render() {
        const fixture = TestBed.createComponent(BrassNailComponent);
        await fixture.whenStable();
        return fixture;
    }

    it('draws the domed head and one tapering shaft', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        expect(svg.querySelectorAll('path').length).toBe(1);
        expect(svg.querySelectorAll('circle').length).toBe(2);
        expect(svg.querySelector('defs linearGradient#brassNail')).not.toBeNull();
    });

    it('paints the metal through the brass tokens, plus exactly one specular glint', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        // Queried as `defs stop`, not `linearGradient stop`: jsdom matches camelCase SVG type
        // selectors case-sensitively after lowercasing, so the camelCase form finds nothing here.
        const stops = [...svg.querySelectorAll('defs stop')].map(s => s.getAttribute('stop-color'));
        expect(stops).toEqual(['var(--brass-hi)', 'var(--brass)']);
        const head = svg.querySelector('circle')!;
        expect(head.getAttribute('fill')).toBe('url(#brassNail)');
        expect(head.getAttribute('stroke')).toBe('var(--brass-edge)');
        // The glint is a spot of light on metal, deliberately not a themable ink — the one
        // literal colour the component is allowed.
        const literals = [...svg.querySelectorAll('*')]
            .flatMap(el => ['fill', 'stroke'].map(a => el.getAttribute(a)))
            .filter((v): v is string => v !== null && /#[0-9a-f]{3}|rgba?\(/i.test(v));
        expect(literals).toEqual(['#FFF3D6']);
    });

    it('is taller than it is wide, because it is driven in', async () => {
        const svg = (await render()).nativeElement.querySelector('svg') as SVGElement;
        expect(svg.getAttribute('width')).toBe('10');
        expect(svg.getAttribute('height')).toBe('14');
    });

    it('is always hidden from assistive tech', async () => {
        const host = (await render()).nativeElement as HTMLElement;
        expect(host.getAttribute('aria-hidden')).toBe('true');
        expect(host.getAttribute('role')).toBeNull();
    });
});
