import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { PasswordRecoveryComponent } from './password-recovery.component';

describe('Password recovery', () => {
    function setup(reset = false, fragment: string | null = null) {
        TestBed.configureTestingModule({
            providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
                { provide: ActivatedRoute, useValue: { snapshot: { data: { reset }, fragment } } }],
        });
        const historySpy = vi.spyOn(TestBed.inject(Location), 'replaceState');
        const fixture = TestBed.createComponent(PasswordRecoveryComponent);
        return { fixture, historySpy, component: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
    }

    it('does not submit an empty form through Enter', async () => {
        const { component, http } = setup();
        await component.submit();
        http.expectNone('/api/auth/forgot-password');
        expect(component.busy()).toBe(false);
    });

    it('submits one request and shows conditional confirmation', async () => {
        const { component, http } = setup();
        component.email = ' author@example.test ';
        const pending = component.submit();
        await component.submit();
        const request = http.expectOne('/api/auth/forgot-password');
        expect(request.request.body).toEqual({ email: 'author@example.test' });
        expect(component.busy()).toBe(true);
        request.flush(null, { status: 200, statusText: 'OK' });
        await pending;
        expect(component.done()).toBe(true);
        expect(component.busy()).toBe(false);
        http.verify();
    });

    it('keeps an outage distinct from an accepted request', async () => {
        const { component, http } = setup();
        component.email = 'author@example.test';
        const pending = component.submit();
        http.expectOne('/api/auth/forgot-password').flush(null, { status: 503, statusText: 'Unavailable' });
        await pending;
        expect(component.done()).toBe(false);
        expect(component.error()).toBe(component.t().passwordRecovery.unavailable);
        http.verify();
    });

    it('does not send mismatched passwords and strips the fragment from history', async () => {
        const { component, http, historySpy } = setup(true, 'userId=test-user&token=test-token');
        expect(historySpy).toHaveBeenCalledWith('/reset-password');
        component.password = 'Example-Password8';
        component.confirm = 'different';
        await component.submit();
        expect(component.error()).toBe(component.t().passwordRecovery.mismatch);
        http.expectNone('/api/auth/reset-password');
        historySpy.mockRestore();
    });

    it('sends the token only in the request body and clears it after success', async () => {
        const { component, http } = setup(true, 'userId=test-user&token=test-token');
        component.password = component.confirm = 'Example-Password8';
        const pending = component.submit();
        const request = http.expectOne('/api/auth/reset-password');
        expect(request.request.body).toEqual({ userId: 'test-user', token: 'test-token', password: 'Example-Password8' });
        request.flush(null);
        await pending;
        expect(component.done()).toBe(true);
        expect(component.password).toBe('');
        expect(component.hasToken).toBe(false);
        http.verify();
    });
});
