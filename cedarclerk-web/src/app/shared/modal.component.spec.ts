import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { OverlayCoordinatorService } from '../core/overlay-coordinator.service';
import { ModalComponent } from './modal.component';

describe('ModalComponent', () => {
    it('exposes modal semantics tied to its visible title', () => {
        const fixture = TestBed.createComponent(ModalComponent);
        fixture.detectChanges();

        const dialog = fixture.nativeElement.querySelector('.modal-card') as HTMLElement;
        const title = fixture.nativeElement.querySelector('.modal-title') as HTMLElement;
        expect(dialog.getAttribute('role')).toBe('dialog');
        expect(dialog.getAttribute('aria-modal')).toBe('true');
        expect(dialog.getAttribute('aria-labelledby')).toBe(title.id);
    });

    it('moves focus inside, makes the background inert, traps Tab, and restores the opener', async () => {
        const opener = document.createElement('button');
        document.body.append(opener);
        opener.focus();
        const fixture = TestBed.createComponent(ModalComponent);
        fixture.detectChanges();
        const card = fixture.nativeElement.querySelector('.modal-card') as HTMLElement;
        const bodyButton = document.createElement('button');
        const actionButton = document.createElement('button');
        card.querySelector('.modal-body')!.append(bodyButton);
        card.querySelector('.modal-actions')!.append(actionButton);
        await Promise.resolve();

        expect(document.activeElement).toBe(bodyButton);
        expect(opener.inert).toBe(true);

        actionButton.focus();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
        expect(document.activeElement).toBe(card.querySelector('.modal-close'));

        (card.querySelector('.modal-close') as HTMLButtonElement).focus();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));
        expect(document.activeElement).toBe(actionButton);

        opener.focus();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
        expect(document.activeElement).toBe(card.querySelector('.modal-close'));

        opener.focus();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));
        expect(document.activeElement).toBe(actionButton);

        fixture.destroy();
        await Promise.resolve();
        expect(opener.inert).toBe(false);
        expect(document.activeElement).toBe(opener);
        opener.remove();
    });

    it('lets only the top registered modal consume Escape', async () => {
        const first = TestBed.createComponent(ModalComponent);
        const second = TestBed.createComponent(ModalComponent);
        const firstClosed = vi.fn();
        const secondClosed = vi.fn();
        first.componentInstance.closed.subscribe(firstClosed);
        second.componentInstance.closed.subscribe(secondClosed);
        first.detectChanges();
        second.detectChanges();
        await Promise.resolve();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        expect(firstClosed).not.toHaveBeenCalled();
        expect(secondClosed).toHaveBeenCalledOnce();

        second.destroy();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        expect(firstClosed).toHaveBeenCalledOnce();
        first.destroy();
        expect(TestBed.inject(OverlayCoordinatorService).modalOpen()).toBe(false);
    });
});
