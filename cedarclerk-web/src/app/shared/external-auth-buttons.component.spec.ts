import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ExternalAuthButtonsComponent } from './external-auth-buttons.component';
import { ExternalAuthService, TelegramWidgetUser } from '../core/external-auth.service';
import { TelegramLinkService } from '../core/telegram-link.service';
import { VersionService } from '../core/version.service';

describe('External sign-in buttons', () => {
    async function setup() {
        let callback: (user: TelegramWidgetUser | false) => void = () => {};
        const external = { startGoogle: vi.fn(), telegram: vi.fn().mockResolvedValue({ ok: true }) };
        const telegram = { prepare: vi.fn().mockResolvedValue(undefined), authorizeLogin: vi.fn((_id, _lang, cb) => { callback = cb; return true; }) };
        TestBed.configureTestingModule({ providers: [
            { provide: ExternalAuthService, useValue: external },
            { provide: TelegramLinkService, useValue: telegram },
            { provide: VersionService, useValue: { googleAuth: signal(true), discordAuth: signal(false), telegramBotId: signal(123) } },
        ] });
        const fixture = TestBed.createComponent(ExternalAuthButtonsComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
        const buttons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
        return { fixture, buttons, external, telegram, callback: (user: TelegramWidgetUser | false) => callback(user) };
    }

    it('preserves the Google return destination', async () => {
        const { fixture, buttons, external } = await setup();
        fixture.componentRef.setInput('returnUrl', '/projects');
        buttons[0].click();
        expect(external.startGoogle).toHaveBeenCalledWith('/projects');
    });

    it('posts a signed Telegram result once and reports successful sign in', async () => {
        const { fixture, buttons, external, callback } = await setup();
        const signedIn = vi.fn();
        fixture.componentInstance.signedIn.subscribe(signedIn);
        buttons[1].click();
        const user = { id: 42, auth_date: 100, hash: 'signed-payload' };
        callback(user);
        callback(user);
        await fixture.whenStable();
        expect(external.telegram).toHaveBeenCalledExactlyOnceWith(user);
        expect(signedIn).toHaveBeenCalledOnce();
    });

    it('ignores cancellation and results after navigation', async () => {
        const { fixture, buttons, external, callback } = await setup();
        buttons[1].click();
        callback(false);
        expect(external.telegram).not.toHaveBeenCalled();
        buttons[1].click();
        fixture.destroy();
        callback({ id: 42, auth_date: 100, hash: 'late' });
        expect(external.telegram).not.toHaveBeenCalled();
    });
});
