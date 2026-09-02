import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocaleService } from '../core/i18n/locale.service';
import { DraftSearchHit, SearchService } from '../core/search.service';
import { SearchOverlayComponent } from './search-overlay.component';
import { OverlayCoordinatorService } from '../core/overlay-coordinator.service';

describe('SearchOverlayComponent', () => {
    const hits: DraftSearchHit[] = [
        { id: 'a1', title: 'Alpha', snippet: 'first hit', updatedAt: '2026-08-29T00:00:00Z', documentType: 'post', isBlogPublished: true },
        { id: 'b2', title: 'Beta', snippet: 'second hit', updatedAt: '2026-08-29T00:00:00Z', documentType: 'note', isBlogPublished: false },
    ];

    afterEach(() => vi.useRealTimers());

    function mount(answer: DraftSearchHit[] = hits) {
        const drafts = vi.fn().mockResolvedValue(answer);
        TestBed.configureTestingModule({
            providers: [provideRouter([]), { provide: SearchService, useValue: { drafts } }],
        });
        TestBed.inject(LocaleService).uiLang.set('en');
        const overlays = TestBed.inject(OverlayCoordinatorService);
        const active = overlays.active();
        if (active) overlays.close(active);
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        const fixture: ComponentFixture<SearchOverlayComponent> = TestBed.createComponent(SearchOverlayComponent);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        return {
            fixture,
            component: fixture.componentInstance,
            drafts,
            navigate,
            overlays,
            el,
            panel: () => el.querySelector('.so-panel'),
            rows: () => [...el.querySelectorAll<HTMLButtonElement>('.so-row')],
        };
    }

    it('focuses inside, inerts the background, consumes Escape, and restores focus', async () => {
        const h = mount();
        const opener = document.createElement('button');
        document.body.append(opener);
        opener.focus();
        expect(h.panel()).toBeNull();

        h.component.openOverlay();
        h.fixture.detectChanges();
        await Promise.resolve();
        expect(h.panel()).toBeTruthy();
        expect(document.activeElement).toBe(h.el.querySelector('.so-input'));
        expect(opener.inert).toBe(true);

        const escapedPastDialog = vi.fn();
        document.addEventListener('keydown', escapedPastDialog);
        (document.activeElement as HTMLElement).dispatchEvent(
            new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        h.fixture.detectChanges();
        await Promise.resolve();
        expect(h.panel()).toBeNull();
        expect(escapedPastDialog).not.toHaveBeenCalled();
        expect(opener.inert).toBe(false);
        expect(document.activeElement).toBe(opener);
        document.removeEventListener('keydown', escapedPastDialog);
        opener.remove();
    });

    it('leaves the DOM when another shell overlay replaces it', () => {
        const h = mount();
        h.component.openOverlay();
        h.fixture.detectChanges();

        h.overlays.open('debug');
        h.fixture.detectChanges();

        expect(h.panel()).toBeNull();
        expect(h.overlays.active()).toBe('debug');
    });

    it('debounces the query and renders one row per hit', async () => {
        vi.useFakeTimers();
        const h = mount();
        h.component.openOverlay();
        h.fixture.detectChanges();

        h.component.onQueryChange('alp');
        h.component.onQueryChange('alpha');
        await vi.advanceTimersByTimeAsync(200);
        expect(h.drafts).not.toHaveBeenCalled();

        await vi.advanceTimersByTimeAsync(50);
        h.fixture.detectChanges();
        // Only the settled query is asked — retyping inside the window costs nothing.
        expect(h.drafts).toHaveBeenCalledOnce();
        expect(h.drafts).toHaveBeenCalledWith('alpha');
        expect(h.rows().length).toBe(2);
        expect(h.rows()[0].textContent).toContain('Alpha');
    });

    it('moves the highlight with the arrows and opens the lit row in the editor on Enter', async () => {
        vi.useFakeTimers();
        const h = mount();
        h.component.openOverlay();
        h.component.onQueryChange('alpha');
        await vi.advanceTimersByTimeAsync(251);
        h.fixture.detectChanges();

        h.component.onInputKeydown(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
        h.fixture.detectChanges();
        expect(h.rows()[1].classList.contains('active')).toBe(true);
        expect(h.el.querySelector('.so-input')?.getAttribute('aria-activedescendant'))
            .toBe('cedar-search-option-1');

        h.component.onInputKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));
        h.fixture.detectChanges();
        expect(h.navigate).toHaveBeenCalledWith(['/editor'], { queryParams: { draft: 'b2' } });
        expect(h.panel()).toBeNull();
    });

    it('cycles Tab and Shift+Tab within the search dialog', async () => {
        const h = mount();
        h.component.openOverlay();
        h.component.results.set(hits);
        h.fixture.detectChanges();
        await Promise.resolve();

        const input = h.el.querySelector('.so-input') as HTMLInputElement;
        const last = h.rows().at(-1)!;
        input.focus();
        input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));
        expect(document.activeElement).toBe(last);

        last.focus();
        last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
        expect(document.activeElement).toBe(input);
    });

    it('says "nothing matches" only once a query has actually answered empty', async () => {
        vi.useFakeTimers();
        const h = mount([]);
        h.component.openOverlay();
        h.fixture.detectChanges();
        // Before any query the note is the hint, not the empty state.
        expect(h.el.textContent).not.toContain('Nothing matches');

        h.component.onQueryChange('nope');
        await vi.advanceTimersByTimeAsync(251);
        h.fixture.detectChanges();
        expect(h.el.textContent).toContain('Nothing matches');
    });
});
