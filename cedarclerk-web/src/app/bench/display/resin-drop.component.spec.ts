import { TestBed } from '@angular/core/testing';
import { ResinDropComponent } from './resin-drop.component';

// The component's own stylesheet, as Angular actually shipped it to the document. Read rather
// than imported, so what is asserted is what the browser gets.
function ownStyles(marker: string): string {
    const sheets = [...document.querySelectorAll('style')].map(s => s.textContent ?? '');
    const mine = sheets.filter(s => s.includes(marker));
    expect(mine.length).toBeGreaterThan(0);
    return mine.join('\n');
}

describe('ResinDropComponent', () => {
    function create() {
        const fixture = TestBed.createComponent(ResinDropComponent);
        fixture.detectChanges();
        return fixture;
    }

    it('is chrome — it hangs on the rail and in the ruler', () => {
        expect(create().nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('starts forming, which is what unsaved looks like', () => {
        expect(create().nativeElement.getAttribute('data-state')).toBe('forming');
    });

    it('glints once when the resin sets, and never while it is still forming', () => {
        const fixture = create();
        expect(fixture.nativeElement.querySelector('.glint')).toBeNull();

        fixture.componentRef.setInput('state', 'set');
        fixture.detectChanges();
        expect(fixture.nativeElement.getAttribute('data-state')).toBe('set');
        expect(fixture.nativeElement.querySelector('.glint')).not.toBeNull();

        fixture.componentRef.setInput('state', 'forming');
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('.glint')).toBeNull();
    });

    it('shows a word only when it is given one', () => {
        const fixture = create();
        expect(fixture.nativeElement.querySelector('.label')).toBeNull();

        fixture.componentRef.setInput('label', 'сохранено 09:51');
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('.label').textContent.trim()).toBe('сохранено 09:51');
    });

    it('announces the save state, because the drop itself is a picture', () => {
        const fixture = create();
        const el = fixture.nativeElement as HTMLElement;
        expect(el.getAttribute('role')).toBe('status');
        expect(el.getAttribute('aria-label')).toBeNull();
        expect(el.querySelector('.drop')!.getAttribute('aria-hidden')).toBe('true');

        fixture.componentRef.setInput('title', 'Смола застыла — сохранено');
        fixture.detectChanges();
        expect(el.getAttribute('aria-label')).toBe('Смола застыла — сохранено');
        expect(el.getAttribute('title')).toBe('Смола застыла — сохранено');
    });

    it('pulses only while forming', () => {
        create();
        const css = ownStyles('bench-resin-form');
        expect(css).toMatch(/@keyframes\s+\S*bench-resin-form/);
        expect(css).toMatch(/\[data-state=["']?forming["']?\][^{]*\.drop[^{]*\{[^}]*animation:[^;}]*bench-resin-form/);
    });

    it('rides the motion tokens the global reduced-motion block collapses, and writes no second block', () => {
        create();
        const css = ownStyles('bench-resin-form');
        // Both animations must be measured in --motion-*; a literal duration would survive
        // styles.scss's @media (prefers-reduced-motion: reduce) untouched.
        const declared = [...css.matchAll(/animation:\s*\S*bench-resin-\w+[^;}]*/g)].map(m => m[0]);
        expect(declared.length).toBe(2);
        for (const decl of declared) expect(decl).toContain('var(--motion-slow)');
        expect(css).not.toContain('prefers-reduced-motion');
    });

    it('paints nothing with a literal colour', () => {
        create();
        const css = ownStyles('bench-resin-form');
        expect(css).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
        expect(css).not.toMatch(/\brgba?\(/);
    });
});
