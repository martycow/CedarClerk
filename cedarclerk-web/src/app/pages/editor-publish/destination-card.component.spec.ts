import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LocaleService } from '../../core/i18n/locale.service';
import { DestinationCardComponent, DestinationKind } from './destination-card.component';

@Component({
    imports: [DestinationCardComponent],
    template: `
        <app-destination-card id="telegram" name="Telegram" brand="telegram" meta="Dev Dairy Diary" state="ready"
                              [kind]="kind()" [includable]="includable()" [included]="included()" [active]="active()"
                              (include)="included.set($event); includes.push($event)" (select)="selects = selects + 1" />
    `,
})
class Host {
    kind = signal<DestinationKind>('publish');
    includable = signal(true);
    included = signal(false);
    active = signal(false);
    includes: boolean[] = [];
    selects = 0;
}

describe('DestinationCardComponent', () => {
    function mount() {
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(Host);
        fixture.detectChanges();
        return { fixture, host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
    }

    it('is two controls with two names: the checkbox includes, the button inspects', () => {
        const { el, host, fixture } = mount();
        const box = el.querySelector<HTMLInputElement>('.dest-card input[type=checkbox]')!;
        const button = el.querySelector<HTMLButtonElement>('.dest-card button.dest-select')!;
        expect(box.getAttribute('aria-label')).toBe('Include Telegram in this publish');
        expect(button.getAttribute('aria-label')).toBe('Telegram settings');
        expect(button.getAttribute('aria-pressed')).toBe('false');
        // The checkbox is beside the button, never inside it.
        expect(button.contains(box)).toBe(false);
        expect(box.closest('[role="button"], button')).toBeNull();

        button.click();
        expect(host.selects).toBe(1);
        expect(host.includes).toEqual([]);

        box.click();
        fixture.detectChanges();
        expect(host.includes).toEqual([true]);
        expect(host.selects).toBe(1);
        expect(el.querySelector('.dest-card')!.classList.contains('on')).toBe(true);
    });

    it('draws the state as an icon with a word, and the card as active when its settings are shown', () => {
        const { el, host, fixture } = mount();
        const tag = el.querySelector('app-state-tag')!;
        expect(tag.getAttribute('data-state')).toBe('ready');
        expect(tag.textContent!.trim()).toBe('Ready');
        expect(tag.querySelector('svg')).not.toBeNull();
        host.active.set(true);
        fixture.detectChanges();
        expect(el.querySelector('.dest-card')!.classList.contains('active')).toBe(true);
        expect(el.querySelector('.dest-select')!.getAttribute('aria-pressed')).toBe('true');
    });

    it('offers no checkbox at all for a copy target or an unsupported network', () => {
        const { el, host, fixture } = mount();
        for (const kind of ['copy', 'unsupported'] as DestinationKind[]) {
            host.kind.set(kind);
            host.includable.set(false);
            fixture.detectChanges();
            expect(el.querySelector('.dest-card input[type=checkbox]')).toBeNull();
            expect(el.querySelector('.dest-card')!.classList.contains('off')).toBe(true);
            // Selecting still works: the middle column shows what the destination is.
            el.querySelector<HTMLButtonElement>('.dest-select')!.click();
        }
        expect(host.selects).toBe(2);
        expect(host.includes).toEqual([]);
    });
});
