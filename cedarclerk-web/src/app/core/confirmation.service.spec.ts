import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ConfirmationService } from './confirmation.service';
import { ModalComponent } from '../shared/modal.component';
import { OverlayCoordinatorService } from './overlay-coordinator.service';

describe('destructive confirmation', () => {
    beforeEach(() => {
        localStorage.setItem('cedar-ui-lang', 'en');
        TestBed.configureTestingModule({ providers: [provideRouter([])] });
    });

    const buttons = () => [...document.querySelectorAll<HTMLButtonElement>('app-confirmation-dialog button')];
    const press = (label: string) => buttons().find(button => button.textContent?.trim() === label)!.click();

    it('focuses Cancel, ignores the backdrop, cancels on Escape and restores the opener', async () => {
        const opener = document.createElement('button');
        document.body.append(opener);
        opener.focus();
        const result = TestBed.inject(ConfirmationService).confirm('Delete the saved item?');
        await Promise.resolve();
        expect(document.activeElement?.textContent?.trim()).toBe('Cancel');
        expect(opener.inert).toBe(true);
        (document.querySelector('app-confirmation-dialog .modal-overlay') as HTMLElement).click();
        expect(document.querySelector('app-confirmation-dialog')).not.toBeNull();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        expect(await result).toBe(false);
        expect(opener.inert).toBe(false);
        expect(document.activeElement).toBe(opener);
        expect(document.querySelector('app-confirmation-dialog')).toBeNull();
        opener.remove();
    });

    it('does not replace a pending target, and requires an affirmative action', async () => {
        const service = TestBed.inject(ConfirmationService);
        const first = service.confirm({ message: 'Delete Alpha?', title: 'Delete Alpha', confirmLabel: 'Delete Alpha' });
        expect(await service.confirm('Delete Beta?')).toBe(false);
        expect(document.querySelector('app-confirmation-dialog')?.textContent).toContain('Delete Alpha?');
        press('Delete Alpha');
        expect(await first).toBe(true);
        const second = service.confirm('Delete Gamma?');
        press('Cancel');
        expect(await second).toBe(false);
    });

    it('cancels when navigation begins', async () => {
        const result = TestBed.inject(ConfirmationService).confirm('Delete the saved item?');
        await TestBed.inject(Router).navigateByUrl('/');
        expect(await result).toBe(false);
        expect(document.querySelector('app-confirmation-dialog')).toBeNull();
    });

    it('opens above an editing modal and only closes the top layer on Escape', async () => {
        const editing = TestBed.createComponent(ModalComponent);
        editing.detectChanges();
        const closed = vi.fn();
        editing.componentInstance.closed.subscribe(closed);
        const result = TestBed.inject(ConfirmationService).confirm('Delete a task?');
        await Promise.resolve();
        expect(document.querySelector('app-confirmation-dialog')?.closest('[inert]')).toBeNull();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        expect(await result).toBe(false);
        expect(closed).not.toHaveBeenCalled();
        expect(TestBed.inject(OverlayCoordinatorService).modalOpen()).toBe(true);
        editing.destroy();
    });

    it('dismisses every popover even when an earlier one supplies a focus target', () => {
        const overlays = TestBed.inject(OverlayCoordinatorService);
        const first = vi.fn(() => document.createElement('button'));
        const second = vi.fn(() => null);
        const removeFirst = overlays.registerDismissablePeer(first);
        const removeSecond = overlays.registerDismissablePeer(second);
        overlays.dismissPopovers();
        expect(first).toHaveBeenCalledOnce();
        expect(second).toHaveBeenCalledOnce();
        removeFirst();
        removeSecond();
    });
});
