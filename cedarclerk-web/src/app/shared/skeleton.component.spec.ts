import { Component, Injector, runInInjectionContext, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LocaleService } from '../core/i18n/locale.service';
import { SKELETON_MIN_MS, SkeletonComponent, heldLoading } from './skeleton.component';

@Component({
    imports: [SkeletonComponent],
    template: `
        <section id="region">
            @if (show()) { <app-skeleton [variant]="variant" [count]="3" /> }
        </section>
    `,
})
class HostComponent {
    show = signal(true);
    variant: 'text' | 'card' | 'avatar' | 'table-row' = 'text';
}

describe('app-skeleton', () => {
    let fixture: ComponentFixture<HostComponent>;
    const el = () => fixture.nativeElement as HTMLElement;
    const region = () => el().querySelector('#region')!;

    beforeEach(async () => {
        TestBed.configureTestingModule({});
        TestBed.inject(LocaleService).uiLang.set('en');
        fixture = TestBed.createComponent(HostComponent);
        fixture.detectChanges();
        await fixture.whenStable();
    });

    it('marks the region it stands in busy, and clears it when it leaves', () => {
        expect(region().getAttribute('aria-busy')).toBe('true');
        const status = el().querySelector('app-skeleton')!;
        expect(status.getAttribute('role')).toBe('status');
        expect(status.getAttribute('aria-label')).toBe('Loading…');

        fixture.componentInstance.show.set(false);
        fixture.detectChanges();
        expect(region().hasAttribute('aria-busy')).toBe(false);
    });

    it('draws one bone per row for text, and a full row for a card and a table row', () => {
        expect(el().querySelectorAll('.bone.line').length).toBe(3);

        fixture.componentInstance.show.set(false);
        fixture.detectChanges();
        fixture.componentInstance.variant = 'card';
        fixture.componentInstance.show.set(true);
        fixture.detectChanges();
        expect(el().querySelectorAll('.card-row').length).toBe(3);
        expect(el().querySelectorAll('.card-row .avatar').length).toBe(3);

        fixture.componentInstance.show.set(false);
        fixture.detectChanges();
        fixture.componentInstance.variant = 'table-row';
        fixture.componentInstance.show.set(true);
        fixture.detectChanges();
        expect(el().querySelectorAll('.table-row').length).toBe(3);
    });
});

describe('heldLoading', () => {
    afterEach(() => vi.useRealTimers());

    function held(source: ReturnType<typeof signal<boolean>>) {
        TestBed.configureTestingModule({});
        const injector = TestBed.inject(Injector);
        const out = runInInjectionContext(injector, () => heldLoading(source));
        TestBed.tick();
        return out;
    }

    it('keeps the skeleton up for the minimum after a fast answer', () => {
        vi.useFakeTimers();
        const source = signal(true);
        const out = held(source);
        expect(out()).toBe(true);

        vi.advanceTimersByTime(40);
        source.set(false);
        TestBed.tick();
        expect(out()).toBe(true);

        vi.advanceTimersByTime(SKELETON_MIN_MS - 40 - 1);
        expect(out()).toBe(true);
        vi.advanceTimersByTime(2);
        expect(out()).toBe(false);
    });

    it('drops at once when the minimum has already passed', () => {
        vi.useFakeTimers();
        const source = signal(true);
        const out = held(source);
        vi.advanceTimersByTime(SKELETON_MIN_MS + 10);
        source.set(false);
        TestBed.tick();
        expect(out()).toBe(false);
    });

    it('never holds a flag that starts off', () => {
        const source = signal(false);
        const out = held(source);
        expect(out()).toBe(false);
    });
});
