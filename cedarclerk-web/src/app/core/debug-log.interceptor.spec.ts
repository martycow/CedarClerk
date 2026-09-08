import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { debugLogInterceptor } from './debug-log.interceptor';
import { DebugLogService } from './debug-log.service';

describe('Authentication journal privacy', () => {
    beforeEach(() => TestBed.configureTestingModule({ providers: [
        provideHttpClient(withInterceptors([debugLogInterceptor])), provideHttpClientTesting(),
    ] }));

    it.each(['/api/auth/login', '/api/auth/reset-password', '/api/auth/external/telegram'])('does not retain %s credentials or response', path => {
        TestBed.inject(HttpClient).post(path, { password: 'test-password', token: 'test-token' }).subscribe();
        const http = TestBed.inject(HttpTestingController);
        http.expectOne(path).flush({ private: 'test-response' });
        expect(TestBed.inject(DebugLogService).entries()).toEqual([]);
        http.verify();
    });

    it('keeps ordinary requests available for diagnosis', () => {
        TestBed.inject(HttpClient).get('/api/health').subscribe();
        TestBed.inject(HttpTestingController).expectOne('/api/health').flush({ status: 'ok' });
        const entries = TestBed.inject(DebugLogService).entries();
        expect(entries).toHaveLength(1);
        expect(entries[0].status).toBe(200);
    });
});
