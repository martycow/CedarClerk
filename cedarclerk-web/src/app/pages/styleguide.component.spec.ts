import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { StyleguideComponent } from './styleguide.component';

// The page's job is to be the single copy of the kit, so the assertions that matter are the ones a
// reviewer cannot make by looking: that nothing here draws a control of its own, and that the two
// levers the page exists to expose are still wired to the mechanisms they claim.
describe('styleguide page', () => {
    let fixture: ComponentFixture<StyleguideComponent>;
    const el = () => fixture.nativeElement as HTMLElement;
    const byName = (name: RegExp) =>
        [...el().querySelectorAll('app-button button')].find(b => name.test(b.textContent ?? '')) as HTMLButtonElement;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideRouter([])] });
        fixture = TestBed.createComponent(StyleguideComponent);
        fixture.detectChanges();
    });

    // e2e/99-audit.spec.ts drives the page by these three accessible names and by the h1.
    it('keeps the names the audit run drives it by', () => {
        expect(el().querySelector('h1')?.textContent).toContain('Design System');
        expect(byName(/Theme:/)).toBeTruthy();
        expect(byName(/Density:/)).toBeTruthy();
        expect(byName(/Pseudo-locale:/)).toBeTruthy();
    });

    it('flips every toggled bay between the two surfaces', () => {
        const bays = () => [...el().querySelectorAll('.sg-bay')].map(b => b.getAttribute('data-surface'));

        expect(bays()).toContain('paper');
        expect(bays().filter(s => s === 'chrome').length).toBe(1); // the inspector, which never follows

        byName(/Surface:/).click();
        fixture.detectChanges();

        expect(bays()).not.toContain('paper');
        expect(bays().every(s => s === 'chrome')).toBe(true);
    });

    // The density toggle is the page's own root attribute, not a component's.
    it('sets data-density on the page root and nowhere else', () => {
        const rootEl = el().querySelector('.sg')!;
        expect(rootEl.getAttribute('data-density')).toBeNull();

        byName(/Density:/).click();
        fixture.detectChanges();

        expect(rootEl.getAttribute('data-density')).toBe('compact');
        expect(el().querySelectorAll('[data-density]').length).toBe(1);
    });

    it('shows every button variant in both sizes', () => {
        const faces = [...el().querySelectorAll('.sg-matrix app-button button')].map(b => b.className);
        for (const variant of ['pine', 'paper', 'rail', 'danger']) {
            for (const size of ['md', 'sm']) {
                expect(faces.some(c => c.includes(variant) && c.includes(size))).toBe(true);
            }
        }
    });

    it('renders each control state, disabled included', () => {
        const cells = el().querySelectorAll('.sg-matrix .sg-cell');
        expect(cells.length).toBe(32); // 4 variants x 2 sizes x 4 states
        expect([...el().querySelectorAll('.sg-matrix button')].filter(b => (b as HTMLButtonElement).disabled).length).toBe(8);
    });

    it('renders the whole kit, not a subset of it', () => {
        for (const tag of ['app-input', 'app-stamp-badge', 'app-leaf-tag',
            'app-paper-card', 'app-task-tag', 'app-spec-row', 'app-brass-pin', 'app-brass-hook',
            'app-worktop', 'app-module-tile', 'app-shelf-panel', 'app-index-tabs', 'app-log-line',
            'app-page-header', 'app-empty-state']) {
            expect(el().querySelector(tag), tag).toBeTruthy();
        }
    });

    // ADR-239 §B: every page class is on the page, in live markup, so a token change is judged
    // here rather than by walking the app.
    it('shows the page vocabulary', () => {
        for (const sel of ['.card', '.seg > button.is-on', '.row-list .row .t', '.tag.ok', '.tag.muted',
            '.tag.warn', '.tag.danger', '.tag.is-plain', '.empty-state', '.margin-note', '.kv',
            'a.btn.primary', 'a.btn.ghost', 'a.btn.sm', '.label']) {
            expect(el().querySelector(sel), sel).toBeTruthy();
        }
        expect(el().querySelector('app-page-header h1')?.textContent).toContain('Design System');
        expect(el().querySelector('.sg-wall app-page-header h2')?.textContent).toContain('Documents');
    });

    // ADR-140: a strip per surface the ring has to clear.
    it('proves the ring on both surfaces', () => {
        const strips = [...el().querySelectorAll('.sg-strip')];
        expect(strips.filter(s => s.getAttribute('data-surface') === 'paper').length).toBe(1);
        expect(strips.filter(s => s.getAttribute('data-surface') === 'chrome').length).toBe(1);
        for (const strip of strips) expect(strip.querySelector('app-button button')).toBeTruthy();
    });

    // The rule the whole rewrite exists for: one vocabulary. A control drawn by this page rather
    // than by a bench component would be a second one, and it would drift the day either moved.
    it('draws no control of its own', () => {
        const css = (StyleguideComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('');
        expect(css.length).toBeGreaterThan(2000);
        for (const dead of ['.btn-accent', '.btn-ghost', '.btn-danger', '.sg-input', '.sg-check',
            '.sg-badge', '.sg-count', 'focus-visible', 'cb-']) {
            expect(css, dead).not.toContain(dead);
        }
        // Every native control on the page belongs to a bench component or to the global `.seg`
        // (a §B class whose tiles are native buttons by contract); the page owns none.
        const bench = 'app-button, app-input, app-leaf-tag, app-spec-row, app-task-tag,'
            + ' app-module-tile, app-index-tabs, .seg';
        const loose = [...el().querySelectorAll('button, input, select, textarea')]
            .filter(node => !node.closest(bench))
            .map(node => node.outerHTML.slice(0, 80));
        expect(loose).toEqual([]);
    });

    // The bay is a piece of material, so its own labels take that material's type size. This is
    // the density contract applied to the page rather than described by it (ADR-138).
    it('sizes a bay label off the surface it stands on', () => {
        const css = (StyleguideComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('');
        expect(css).toContain('--text-chrome');
        expect(css).toContain('--fs-ui');
    });
});
