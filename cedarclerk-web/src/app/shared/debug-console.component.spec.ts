import { TestBed } from '@angular/core/testing';
import { DebugLogService } from '../core/debug-log.service';
import { LocaleService } from '../core/i18n/locale.service';
import { OverlayCoordinatorService } from '../core/overlay-coordinator.service';
import { DebugConsoleComponent } from './debug-console.component';

describe('DebugConsoleComponent', () => {
    function mount() {
        const log = TestBed.inject(DebugLogService);
        const overlays = TestBed.inject(OverlayCoordinatorService);
        log.clear();
        const active = overlays.active();
        if (active) overlays.close(active);
        TestBed.inject(LocaleService).uiLang.set('en');

        const fixture = TestBed.createComponent(DebugConsoleComponent);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        return {
            fixture,
            log,
            overlays,
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

        h.overlays.open('debug');
        h.fixture.detectChanges();
        const overlay = h.overlay()!;
        expect(overlay.getAttribute('role')).toBe('dialog');
        expect(overlay.getAttribute('data-surface')).toBe('paper');
        expect(h.el.querySelector('.summary')).toBeNull();
    });

    it('focuses inside, traps focus, consumes Escape, and restores the opener', async () => {
        const h = mount();
        const opener = document.createElement('button');
        document.body.append(opener);
        opener.focus();
        h.overlays.open('debug');
        h.fixture.detectChanges();
        await Promise.resolve();
        expect(h.overlay()).toBeTruthy();
        const clear = h.el.querySelector('.journal-head .mini[aria-label="Clear"]') as HTMLButtonElement;
        const close = h.el.querySelector('.journal-head .mini[aria-label="Close"]') as HTMLButtonElement;
        expect(document.activeElement).toBe(clear);
        expect(opener.inert).toBe(true);

        clear.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));
        expect(document.activeElement).toBe(close);
        close.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
        expect(document.activeElement).toBe(clear);

        const escapedPastDialog = vi.fn();
        document.addEventListener('keydown', escapedPastDialog);
        clear.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        h.fixture.detectChanges();
        await Promise.resolve();
        expect(h.overlays.active()).toBeNull();
        expect(h.overlay()).toBeNull();
        expect(escapedPastDialog).not.toHaveBeenCalled();
        expect(opener.inert).toBe(false);
        expect(document.activeElement).toBe(opener);
        document.removeEventListener('keydown', escapedPastDialog);
        opener.remove();
    });

    it('also shuts from the close button', () => {
        const h = mount();
        h.overlays.open('debug');
        h.fixture.detectChanges();
        (h.el.querySelector('.journal-head .mini[aria-label="Close"]') as HTMLButtonElement).click();
        h.fixture.detectChanges();
        expect(h.overlays.active()).toBeNull();
    });

    it('builds rows only while open', () => {
        const h = mount();
        h.log.finish(h.log.start('GET', '/api/drafts', undefined).id, 200, '[]', false);
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(0);

        h.overlays.open('debug');
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(1);
    });

    it('clears the journal without shutting the overlay — the button acts on what is in view', () => {
        const h = mount();
        h.log.finish(h.log.start('GET', '/api/drafts', undefined).id, 200, '[]', false);
        h.overlays.open('debug');
        h.fixture.detectChanges();

        (h.el.querySelector('.journal-head .mini[aria-label="Clear"]') as HTMLButtonElement).click();
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(0);
        expect(h.overlays.active()).toBe('debug');
        expect(h.el.querySelector('.debug-empty')).toBeTruthy();
    });

    it('uses a native keyboard button for each expandable row and exposes its state', () => {
        const h = mount();
        h.log.finish(h.log.start('POST', '/api/posts', { a: 1 }).id, 500, 'boom', true);
        h.overlays.open('debug');
        h.fixture.detectChanges();

        const toggle = h.rows()[0].querySelector('.debug-row-toggle') as HTMLButtonElement;
        expect(toggle.tagName).toBe('BUTTON');
        expect(toggle.getAttribute('aria-expanded')).toBe('false');
        toggle.click();
        h.fixture.detectChanges();
        const detail = h.el.querySelector('.debug-row-detail') as HTMLElement;
        expect(toggle.getAttribute('aria-expanded')).toBe('true');
        expect(detail.textContent).toContain('"a": 1');
        expect(detail.textContent).toContain('boom');
    });

    it('keeps the rows as the only vertical scroll owner when a body is expanded', () => {
        const h = mount();
        h.log.finish(h.log.start('POST', '/api/posts', { body: 'line\n'.repeat(200) }).id, 500, 'boom', true);
        h.overlays.open('debug');
        h.fixture.detectChanges();
        (h.el.querySelector('.debug-row-toggle') as HTMLButtonElement).click();
        h.fixture.detectChanges();

        const rows = h.el.querySelector('.rows') as HTMLElement;
        const body = h.el.querySelector('.debug-row-detail pre') as HTMLElement;
        expect(getComputedStyle(rows).overflowY).toBe('auto');
        expect(getComputedStyle(body).maxHeight).toBe('');
        expect(getComputedStyle(body).overflowX).toBe('auto');
        expect(getComputedStyle(body).overflowY).toBe('clip');
    });
});
