import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { RegisterComponent } from './register.component';
import { AuthService } from '../core/auth.service';
import { AssetsService } from '../core/assets.service';
import { VersionService } from '../core/version.service';
import { AnalyticsService } from '../core/analytics.service';
import { en } from '@localization/en';

describe('register', () => {
    let calls: unknown[][];

    function create() {
        calls = [];
        TestBed.resetTestingModule();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: AuthService, useValue: {
                    register: async (...args: unknown[]) => { calls.push(args); return { ok: false, error: 'no' }; },
                    checkUsername: async () => ({ available: true }),
                } },
                { provide: AssetsService, useValue: {} },
                { provide: VersionService, useValue: { openRegistration: signal(false) } },
                { provide: AnalyticsService, useValue: { capture() { } } },
            ],
        });
        const fixture = TestBed.createComponent(RegisterComponent);
        fixture.detectChanges();
        return fixture;
    }

    it('accepts a letter, a digit and a symbol within 8–32, and nothing less', () => {
        expect(RegisterComponent.passwordOk('abcdef1!')).toBe(true);
        expect(RegisterComponent.passwordOk('пароль1#')).toBe(true);
        expect(RegisterComponent.passwordOk('abcdefg1')).toBe(false);
        expect(RegisterComponent.passwordOk('abcdefg!')).toBe(false);
        expect(RegisterComponent.passwordOk('ab1!')).toBe(false);
        expect(RegisterComponent.passwordOk('a1!' + 'a'.repeat(30))).toBe(false);
    });

    it('keeps an account name to 16 characters', () => {
        const c = create().componentInstance;
        c.onUsernameInput('a'.repeat(20));
        expect(c.username()).toBe('a'.repeat(16));
    });

    it('does not send a form with a mismatched password, and says why', async () => {
        const fixture = create();
        const c = fixture.componentInstance;
        c.email.set('a@b.co');
        c.password.set('abcdef1!');
        c.confirm.set('abcdef1?');
        c.inviteCode.set('CODE123');
        c.onUsernameInput('writer');
        await c.submit();
        fixture.detectChanges();

        expect(calls).toEqual([]);
        expect((fixture.nativeElement as HTMLElement).textContent).toContain(en.register.mismatch);
    });

    it('sends a valid form', async () => {
        const c = create().componentInstance;
        c.email.set('a@b.co');
        c.password.set('abcdef1!');
        c.confirm.set('abcdef1!');
        c.inviteCode.set('CODE123');
        c.onUsernameInput('writer');
        await c.submit();

        expect(calls).toEqual([['a@b.co', 'abcdef1!', 'CODE123', 'writer']]);
    });
});
