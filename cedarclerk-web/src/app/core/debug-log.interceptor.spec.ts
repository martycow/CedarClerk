import { TestBed } from '@angular/core/testing';
import { HttpRequest, HttpResponse } from '@angular/common/http';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { debugLogInterceptor } from './debug-log.interceptor';
import { DebugLogService } from './debug-log.service';

describe('Debug log authentication privacy', () => {
    it('does not retain authentication credentials or reset tokens', async () => {
        TestBed.configureTestingModule({});
        for (const path of ['login', 'register', 'forgot-password', 'reset-password', 'external/telegram']) {
            const request = new HttpRequest('POST', `/api/auth/${path}`, { password: 'private', token: 'private' });
            await firstValueFrom(TestBed.runInInjectionContext(() => debugLogInterceptor(request,
                () => of(new HttpResponse({ status: 200 })))));
        }
        expect(TestBed.inject(DebugLogService).entries()).toEqual([]);
    });

    it('retains diagnostic responses for ordinary API calls', async () => {
        TestBed.configureTestingModule({});
        await firstValueFrom(TestBed.runInInjectionContext(() => debugLogInterceptor(new HttpRequest('GET', '/api/drafts'),
            () => of(new HttpResponse({ status: 200, body: [] })))));
        expect(TestBed.inject(DebugLogService).entries()[0].status).toBe(200);
    });
});
