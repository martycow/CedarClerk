import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { DesktopHandoffComponent } from './desktop-handoff.component';
import { AuthService } from '../core/auth.service';
import { ExternalAuthService } from '../core/external-auth.service';

describe('Desktop sign-in handoff', () => {
    const challenge = 'A'.repeat(43);

    async function setup(query: Record<string, string>, code: string | null = 'one-time') {
        const external = { desktopCode: vi.fn().mockResolvedValue(code), openDesktopApp: vi.fn() };
        TestBed.configureTestingModule({ providers: [
            { provide: ExternalAuthService, useValue: external },
            { provide: AuthService, useValue: { userEmail: signal('author@example.test') } },
            { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
        ] });
        const fixture = TestBed.createComponent(DesktopHandoffComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        const element = fixture.nativeElement as HTMLElement;
        return { fixture, element, external, button: () => element.querySelector<HTMLButtonElement>('.primary-btn button') };
    }

    it('names the account and mints nothing until the button is pressed', async () => {
        const { element, external } = await setup({ challenge });
        expect(element.textContent).toContain('author@example.test');
        expect(external.desktopCode).not.toHaveBeenCalled();
    });

    it('hands the minted code to the app', async () => {
        const { fixture, external, button, element } = await setup({ challenge });
        button()!.click();
        await fixture.whenStable();
        fixture.detectChanges();
        expect(external.desktopCode).toHaveBeenCalledExactlyOnceWith(challenge);
        expect(external.openDesktopApp).toHaveBeenCalledExactlyOnceWith('one-time');
        expect(element.querySelector('[role="status"]')).not.toBeNull();
    });

    it('offers nothing to press without a challenge from the shell', async () => {
        const { button, element } = await setup({ challenge: 'not-a-challenge' });
        expect(button()).toBeNull();
        expect(element.querySelector('[role="alert"]')).not.toBeNull();
    });

    it('says so when the server refuses, and opens nothing', async () => {
        const { fixture, external, button, element } = await setup({ challenge }, null);
        button()!.click();
        await fixture.whenStable();
        fixture.detectChanges();
        expect(external.openDesktopApp).not.toHaveBeenCalled();
        expect(element.querySelector('[role="alert"]')).not.toBeNull();
    });
});
