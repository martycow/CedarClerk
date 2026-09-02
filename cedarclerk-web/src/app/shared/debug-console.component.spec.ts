import { TestBed } from '@angular/core/testing';
import { DebugLogService } from '../core/debug-log.service';
import { LocaleService } from '../core/i18n/locale.service';
import { DebugConsoleComponent } from './debug-console.component';

describe('DebugConsoleComponent', () => {
    function mount() {
        const log = TestBed.inject(DebugLogService);
        log.clear();
        log.open.set(false);
        TestBed.inject(LocaleService).uiLang.set('en');

        const fixture = TestBed.createComponent(DebugConsoleComponent);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        return {
            fixture,
            log,
            el,
            overlay: () => el.querySelector('.console-overlay') as HTMLElement | null,
            rows: () => [...el.querySelectorAll('.debug-row')],
        };
    }

    // ADR-239 clause 11 — an overlay, and nothing at all while shut: no strip, no count, no
    // summary line on a working screen.
    it('renders nothing while shut, and a dialog over the page while open', () => {
        const h = mount();
        expect(h.el.children.length).toBe(0);

        h.log.open.set(true);
        h.fixture.detectChanges();
        const overlay = h.overlay()!;
        expect(overlay.getAttribute('role')).toBe('dialog');
        expect(overlay.getAttribute('data-surface')).toBe('paper');
        expect(h.el.querySelector('.summary')).toBeNull();
    });

    it('leaves open state with the service, and shuts on Escape or the close button', () => {
        const h = mount();
        h.log.open.set(true);
        h.fixture.detectChanges();
        expect(h.overlay()).toBeTruthy();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        h.fixture.detectChanges();
        expect(h.log.open()).toBe(false);
        expect(h.overlay()).toBeNull();

        h.log.open.set(true);
        h.fixture.detectChanges();
        (h.el.querySelector('.journal-head .mini[aria-label="Close"]') as HTMLButtonElement).click();
        h.fixture.detectChanges();
        expect(h.log.open()).toBe(false);
    });

    it('builds rows only while open', () => {
        const h = mount();
        h.log.finish(h.log.start('GET', '/api/drafts', undefined).id, 200, '[]', false);
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(0);

        h.log.open.set(true);
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(1);
    });

    it('clears the journal without shutting the overlay — the button acts on what is in view', () => {
        const h = mount();
        h.log.finish(h.log.start('GET', '/api/drafts', undefined).id, 200, '[]', false);
        h.log.open.set(true);
        h.fixture.detectChanges();

        (h.el.querySelector('.journal-head .mini[aria-label="Clear"]') as HTMLButtonElement).click();
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(0);
        expect(h.log.open()).toBe(true);
        expect(h.el.querySelector('.debug-empty')).toBeTruthy();
    });

    it('expands one row to its bodies', () => {
        const h = mount();
        h.log.finish(h.log.start('POST', '/api/posts', { a: 1 }).id, 500, 'boom', true);
        h.log.open.set(true);
        h.fixture.detectChanges();

        (h.rows()[0] as HTMLElement).click();
        h.fixture.detectChanges();
        const detail = h.el.querySelector('.debug-row-detail') as HTMLElement;
        expect(detail.textContent).toContain('"a": 1');
        expect(detail.textContent).toContain('boom');
    });
});
