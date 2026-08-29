import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ButtonComponent } from './button.component';

@Component({
    imports: [ButtonComponent],
    template: `<app-button (clicked)="hits = hits + 1">Publish</app-button>`,
})
class ProjectionHost {
    hits = 0;
}

// The same content through the anchor form: projection has to survive the branch, not only the
// element it lands in by default.
@Component({
    imports: [ButtonComponent],
    template: `<app-button [link]="['/editor']" [queryParams]="{ draft: 'd-1' }">Continue</app-button>`,
})
class LinkHost {}

describe('bench Button', () => {
    let fixture: ComponentFixture<ButtonComponent>;
    const button = () => fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    const anchor = () => fixture.nativeElement.querySelector('a') as HTMLAnchorElement;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideRouter([])] });
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

    it('draws a small paper button at the compact desktop height', () => {
        const css = (ButtonComponent as any).ɵcmp.styles.join('');
        expect(css).toMatch(/data-surface=paper[^}]*\.btn\.sm[^}]*min-height:\s*var\(--hit-surface, var\(--hit-chrome\)\)/s);
    });

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

    // ADR-169. A button handed a route is an anchor, and the assertion is the href: middle click,
    // copy-link-address and the status-bar preview are all the browser's, and all of them need an
    // address to exist. A spy on the router would pass over a control that offers none of them.
    it('is an anchor carrying the route, and the query the editor reads', () => {
        fixture.componentRef.setInput('link', ['/editor']);
        fixture.componentRef.setInput('queryParams', { draft: 'd-1' });
        fixture.detectChanges();

        expect(anchor().tagName).toBe('A');
        expect(anchor().getAttribute('href')).toBe('/editor?draft=d-1');
        expect(fixture.nativeElement.querySelector('button')).toBeNull();
        expect(anchor().getAttribute('type')).toBeNull();
    });

    // The face is the variant's and the tag is the consumer's: the link wears the same classes and
    // the same surface, so nothing about a pine button changes when it gains an address.
    it('wears the pine face and the paper surface in link form', () => {
        fixture.componentRef.setInput('link', '/projects');
        fixture.detectChanges();

        expect([...anchor().classList].sort()).toEqual(['btn', 'md', 'pine']);
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('paper');

        const css = (ButtonComponent as any).ɵcmp.styles.join('');
        expect(css.length).toBeGreaterThan(500);
        // Every rule that paints a variant is on .btn, never on a tag, or the anchor would be
        // wearing a different button.
        expect(css).not.toMatch(/button\.btn|button\s*\{/);
        expect(css).toMatch(/\.btn[^{,.:]*\{[^}]*text-decoration:\s*none/);
    });

    it('projects its content through the link branch and stays silent there', () => {
        const host = TestBed.createComponent(LinkHost);
        host.detectChanges();
        const inner = host.nativeElement.querySelector('a') as HTMLAnchorElement;
        expect(inner.textContent?.trim()).toBe('Continue');
        expect(inner.getAttribute('href')).toBe('/editor?draft=d-1');
    });

    it('navigates through the router without also emitting clicked', () => {
        let hits = 0;
        fixture.componentInstance.clicked.subscribe(() => hits++);
        fixture.componentRef.setInput('link', ['/editor']);
        fixture.detectChanges();

        const go = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        anchor().dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
        expect(go).toHaveBeenCalledTimes(1);
        expect(hits).toBe(0);
    });

    // A disabled link keeps the element and loses the address — an <a> with no href is not
    // focusable, not activatable and offers no context menu, which is the whole of `disabled` on a
    // control whose only job is to go somewhere.
    it('drops the address rather than the element when a link is disabled', () => {
        fixture.componentRef.setInput('link', ['/editor']);
        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();

        expect(anchor().tagName).toBe('A');
        expect(anchor().getAttribute('href')).toBeNull();
        expect(anchor().getAttribute('aria-disabled')).toBe('true');

        const go = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        anchor().dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
        expect(go).not.toHaveBeenCalled();

        fixture.componentRef.setInput('disabled', false);
        fixture.detectChanges();
        expect(anchor().getAttribute('href')).toBe('/editor');
        expect(anchor().getAttribute('aria-disabled')).toBeNull();
    });

    // A destination the SPA router must not swallow — a server redirect, the landing — is a plain
    // anchor wearing the same face; the router is never asked, so a click is a full page load.
    it('renders a plain full-page anchor for href, with the same disabled rule', () => {
        fixture.componentRef.setInput('href', '/downloads/latest');
        fixture.detectChanges();

        expect(anchor().tagName).toBe('A');
        expect(anchor().getAttribute('href')).toBe('/downloads/latest');
        expect([...anchor().classList].sort()).toEqual(['btn', 'md', 'pine']);
        expect(fixture.nativeElement.querySelector('button')).toBeNull();

        fixture.componentRef.setInput('disabled', true);
        fixture.detectChanges();
        expect(anchor().getAttribute('href')).toBeNull();
        expect(anchor().getAttribute('aria-disabled')).toBe('true');
    });

    it('takes a title and a submit type', () => {
        fixture.componentRef.setInput('title', 'Send it');
        fixture.componentRef.setInput('type', 'submit');
        fixture.detectChanges();
        expect(button().getAttribute('title')).toBe('Send it');
        expect(button().type).toBe('submit');
    });
});
