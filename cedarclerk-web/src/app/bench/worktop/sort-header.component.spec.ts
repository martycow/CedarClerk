import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { SortHeaderComponent } from './sort-header.component';

function styleText(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(style => style.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        sheet => Array.from(sheet.cssRules).map(rule => rule.cssText).join('\n'));
    return [...inline, ...adopted].filter(text => text.includes(marker)).join('\n');
}

describe('SortHeaderComponent', () => {
    function create(active = false, direction: 'asc' | 'desc' = 'asc') {
        const fixture = TestBed.createComponent(SortHeaderComponent);
        fixture.componentRef.setInput('label', 'Added');
        fixture.componentRef.setInput('active', active);
        fixture.componentRef.setInput('direction', direction);
        fixture.componentRef.setInput('sortByLabel', 'Sort by');
        fixture.componentRef.setInput('ascendingLabel', 'ascending');
        fixture.componentRef.setInput('descendingLabel', 'descending');
        fixture.detectChanges();
        return fixture;
    }

    it('keeps native button semantics and names the current direction', () => {
        const fixture = create(true, 'desc');
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

        expect(button.type).toBe('button');
        expect(button.getAttribute('role')).toBeNull();
        expect(button.getAttribute('aria-label')).toBe('Sort by: Added, descending');
        expect(button.querySelector('app-icon')).not.toBeNull();
    });

    it('makes the whole compact header pitch clickable with density tokens', () => {
        create();
        expect(styleText('min-height: var(--hit-surface, var(--hit-chrome))'))
            .toContain('min-height: var(--hit-surface, var(--hit-chrome))');
    });

    it('emits one sort request for a click', () => {
        const fixture = create();
        const sorted = vi.fn();
        fixture.componentInstance.sorted.subscribe(sorted);

        (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button')!.click();

        expect(sorted).toHaveBeenCalledOnce();
    });
});
