import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RulerBarComponent } from './ruler-bar.component';

@Component({
    imports: [RulerBarComponent],
    template: `
        <app-ruler-bar label="CEDAR QUEST"
                       [left]="[{ text: 'спринт S4 · 11/19' }]"
                       [right]="[{ text: 'слов 812' }, { text: '0.12.0', title: 'версия' }]">
            <button type="button" class="smuggled">Собрать</button>
        </app-ruler-bar>
    `,
})
class RulerHost {}

describe('RulerBarComponent', () => {
    function create(left: { text: string; title?: string }[] = [], right: { text: string; title?: string }[] = []) {
        const fixture = TestBed.createComponent(RulerBarComponent);
        fixture.componentRef.setInput('label', 'CEDAR QUEST');
        fixture.componentRef.setInput('left', left);
        fixture.componentRef.setInput('right', right);
        fixture.detectChanges();
        return fixture;
    }

    const textOf = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();

    it('is chrome — the rule is the bench\'s bottom edge, not a sheet', () => {
        expect(create().nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('reads the label, then the left readouts, then the right ones across the gap', () => {
        const el = create([{ text: 'спринт S4' }], [{ text: 'слов 812' }, { text: '0.12.0' }]).nativeElement as HTMLElement;
        expect(textOf(el.querySelector('.label')!)).toBe('CEDAR QUEST');
        const readouts = [...el.querySelectorAll('.readout')].map(textOf);
        expect(readouts).toEqual(['спринт S4', 'слов 812', '0.12.0']);

        const order = [...el.children].map(c => c.className || c.tagName.toLowerCase());
        expect(order.indexOf('gap')).toBeGreaterThan(order.indexOf('label'));
    });

    it('draws every number mono and leaves the words alone', () => {
        const el = create([{ text: 'спринт S4 · 11/19' }]).nativeElement as HTMLElement;
        const readout = el.querySelector('.readout')!;
        expect(textOf(readout)).toBe('спринт S4 · 11/19');
        expect([...readout.querySelectorAll('.num')].map(n => n.textContent)).toEqual(['4', '11/19']);
        // Nothing that is not a digit run ends up in a .num, and nothing that is stays out of one.
        for (const span of readout.querySelectorAll('span')) {
            expect(/^\d/.test(span.textContent!)).toBe(span.classList.contains('num'));
        }
    });

    it('keeps a version and a ratio as one numeral each', () => {
        const el = create([], [{ text: '0.12.0' }, { text: '11/19' }, { text: '09:51' }]).nativeElement as HTMLElement;
        expect([...el.querySelectorAll('.num')].map(n => n.textContent)).toEqual(['0.12.0', '11/19', '09:51']);
    });

    it('carries a long form only where a readout is too terse to read alone', () => {
        const el = create([], [{ text: '0.12.0', title: 'версия' }, { text: 'слов 812' }]).nativeElement as HTMLElement;
        const readouts = [...el.querySelectorAll('.readout')];
        expect(readouts[0].getAttribute('title')).toBe('версия');
        expect(readouts[1].getAttribute('title')).toBeNull();
    });

    it('exposes nothing interactive — a control in the ruler is the rule violation', () => {
        const el = create([{ text: 'спринт S4 · 11/19' }], [{ text: 'слов 812' }]).nativeElement as HTMLElement;
        expect(el.querySelectorAll('button, a, input, select, textarea, [tabindex], [contenteditable], [role]').length).toBe(0);
    });

    it('offers no slot for one either — projected content is dropped, not shown', () => {
        const fixture = TestBed.createComponent(RulerHost);
        fixture.detectChanges();
        const bar = fixture.nativeElement.querySelector('app-ruler-bar') as HTMLElement;
        // Readouts are strings for exactly this reason: an ng-content here would be an invitation
        // to hang a build button off the rule, and the invitation is the violation.
        expect(bar.querySelector('.smuggled')).toBeNull();
        expect(bar.querySelectorAll('button').length).toBe(0);
        expect(textOf(bar)).toContain('CEDAR QUEST');
    });
});
