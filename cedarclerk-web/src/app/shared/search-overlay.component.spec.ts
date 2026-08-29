import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocaleService } from '../core/i18n/locale.service';
import { DraftSearchHit, SearchService } from '../core/search.service';
import { SearchOverlayComponent } from './search-overlay.component';

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
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        const fixture: ComponentFixture<SearchOverlayComponent> = TestBed.createComponent(SearchOverlayComponent);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        return {
            fixture,
            component: fixture.componentInstance,
            drafts,
            navigate,
            el,
            panel: () => el.querySelector('.so-panel'),
            rows: () => [...el.querySelectorAll<HTMLButtonElement>('.so-row')],
            press: (key: string, init: KeyboardEventInit = {}) =>
                document.dispatchEvent(new KeyboardEvent('keydown', { key, ...init })),
        };
    }

    // The shortcut is the whole reason the shell mounts this once: no page wires anything.
    it('opens on Ctrl+K from anywhere and closes on Escape', () => {
        const h = mount();
        expect(h.panel()).toBeNull();

        h.press('k', { ctrlKey: true });
        h.fixture.detectChanges();
        expect(h.panel()).toBeTruthy();

        h.component.onInputKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
        h.fixture.detectChanges();
        expect(h.panel()).toBeNull();
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

        h.component.onInputKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));
        h.fixture.detectChanges();
        expect(h.navigate).toHaveBeenCalledWith(['/editor'], { queryParams: { draft: 'b2' } });
        expect(h.panel()).toBeNull();
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
