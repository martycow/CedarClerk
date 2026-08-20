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
            drawer: () => el.querySelector('app-bench-drawer') as HTMLElement,
            pull: () => el.querySelector('.pull') as HTMLButtonElement,
            summary: () => el.querySelector('.summary')?.textContent?.trim() ?? '',
            rows: () => [...el.querySelectorAll('.debug-row')],
        };
    }

    it('is the drawer body and nothing else — no tab, no panel, no close button of its own', () => {
        const h = mount();
        expect(h.drawer()).toBeTruthy();
        // Everything the console renders lives inside the drawer. A node outside it would be the
        // floating tab coming back, which is the whole thing ADR-153 replaced.
        expect(h.el.firstElementChild).toBe(h.drawer());
        expect(h.el.children.length).toBe(1);
    });

    it('leaves open state with the service, and lets the lip be the only thing that moves it', () => {
        const h = mount();
        expect(h.drawer().classList.contains('is-open')).toBe(false);

        h.pull().click();
        h.fixture.detectChanges();
        expect(h.log.open()).toBe(true);
        expect(h.drawer().classList.contains('is-open')).toBe(true);

        h.log.open.set(false);
        h.fixture.detectChanges();
        expect(h.drawer().classList.contains('is-open')).toBe(false);
    });

    it('says on the lip what justifies leaving it shut: how much traffic, and whether any of it went wrong', () => {
        const h = mount();
        expect(h.summary()).toBe('0 requests');

        const pending = h.log.start('GET', '/api/drafts', undefined);
        h.fixture.detectChanges();
        expect(h.summary()).toBe('1 request · 1 in flight');

        h.log.finish(pending.id, 500, 'boom', true);
        h.fixture.detectChanges();
        expect(h.summary()).toBe('1 request · 1 error');
    });

    it('builds rows only while the drawer is open', () => {
        const h = mount();
        h.log.finish(h.log.start('GET', '/api/drafts', undefined).id, 200, '[]', false);
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(0);

        h.log.open.set(true);
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(1);
    });

    it('clears the journal without shutting the drawer — the button acts on what is in view', () => {
        const h = mount();
        h.log.finish(h.log.start('GET', '/api/drafts', undefined).id, 200, '[]', false);
        h.log.open.set(true);
        h.fixture.detectChanges();

        (h.el.querySelector('.journal-head .mini') as HTMLButtonElement).click();
        h.fixture.detectChanges();
        expect(h.rows().length).toBe(0);
        expect(h.log.open()).toBe(true);
        expect(h.el.querySelector('.debug-empty')).toBeTruthy();
    });
});
