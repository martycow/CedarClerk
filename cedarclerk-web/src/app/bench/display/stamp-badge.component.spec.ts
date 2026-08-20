import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { StampBadgeComponent } from './stamp-badge.component';

@Component({
    imports: [StampBadgeComponent],
    template: `<app-stamp-badge tone="rust" [rotate]="1.5">просрочено</app-stamp-badge>`,
})
class HostComponent {}

describe('StampBadgeComponent', () => {
    function create() {
        const fixture = TestBed.createComponent(StampBadgeComponent);
        fixture.detectChanges();
        return fixture;
    }

    it('is chrome, and says so where the density lint and the touch carve-out can read it', () => {
        expect(create().nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('defaults to the pine tone, tilted off square', () => {
        const el = create().nativeElement as HTMLElement;
        expect(el.getAttribute('data-tone')).toBe('pine');
        expect(el.style.transform).toBe('rotate(-2deg)');
    });

    it('reflects each of the four tones so the wash and the ink stay one pair', () => {
        const fixture = create();
        for (const tone of ['pine', 'brass', 'rust', 'ink'] as const) {
            fixture.componentRef.setInput('tone', tone);
            fixture.detectChanges();
            expect(fixture.nativeElement.getAttribute('data-tone')).toBe(tone);
        }
    });

    it('takes the rotation as a number of degrees', () => {
        const fixture = create();
        fixture.componentRef.setInput('rotate', 1.5);
        fixture.detectChanges();
        expect((fixture.nativeElement as HTMLElement).style.transform).toBe('rotate(1.5deg)');
    });

    it('renders its word and stays inert — a stamp is not a button', () => {
        const fixture = TestBed.createComponent(HostComponent);
        fixture.detectChanges();
        const badge = fixture.nativeElement.querySelector('app-stamp-badge') as HTMLElement;
        expect(badge.textContent?.trim()).toBe('просрочено');
        expect(badge.querySelector('button')).toBeNull();
        expect(badge.getAttribute('role')).toBeNull();
        expect(badge.tabIndex).toBe(-1);
    });
});
