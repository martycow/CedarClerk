import { TestBed } from '@angular/core/testing';
import { LocaleService } from '../../core/i18n/locale.service';
import { PublishStep, PublishStepperComponent, publishStepStates } from './publish-stepper.component';

describe('publishStepStates', () => {
    it('points at Destinations while nothing is ticked', () => {
        const steps = publishStepStates({ languages: 1, anyDestination: false, settingsComplete: false });
        expect(steps.map(s => `${s.id}:${s.done ? 'done' : ''}${s.current ? 'current' : ''}`))
            .toEqual(['version:done', 'destinations:current', 'settings:', 'review:']);
    });

    it('points at Settings while a ticked destination cannot run, and at Review once every one can', () => {
        const half = publishStepStates({ languages: 1, anyDestination: true, settingsComplete: false });
        expect(half.find(s => s.current)!.id).toBe('settings');
        const whole = publishStepStates({ languages: 2, anyDestination: true, settingsComplete: true });
        expect(whole.map(s => s.done)).toEqual([true, true, true, false]);
        expect(whole.find(s => s.current)!.id).toBe('review');
    });
});

describe('PublishStepperComponent', () => {
    it('is four real buttons, marks the current one and emits the step that was pressed', () => {
        TestBed.inject(LocaleService).uiLang.set('en');
        TestBed.inject(LocaleService).pseudo.set(false);
        const fixture = TestBed.createComponent(PublishStepperComponent);
        fixture.componentRef.setInput('steps', publishStepStates({ languages: 1, anyDestination: true, settingsComplete: false }));
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        const picked: PublishStep[] = [];
        fixture.componentInstance.pick.subscribe(step => picked.push(step));

        const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>('.ps-step'));
        expect(buttons.map(b => b.querySelector('.ps-name')!.textContent!.trim())).toEqual(['Version', 'Destinations', 'Settings', 'Review']);
        expect(buttons.map(b => b.getAttribute('aria-current'))).toEqual([null, null, 'step', null]);
        expect(buttons.map(b => b.classList.contains('is-done'))).toEqual([true, true, false, false]);
        // A done step says so in words, not only with a tick.
        expect(buttons[0].textContent).toContain('done');

        buttons[3].click();
        buttons[0].click();
        expect(picked).toEqual(['review', 'version']);
    });
});
