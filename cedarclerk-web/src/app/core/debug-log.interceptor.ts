import { HttpErrorResponse, HttpEventType, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, tap, throwError } from 'rxjs';
import { DebugLogService } from './debug-log.service';

// Records every HttpClient request/response into DebugLogService for the bottom debug console
// panel — the only way (short of SSH-ing into the Pi) to see the raw body of a failed request,
// e.g. a Telegram publish rejection with its full server-side error text.
export const debugLogInterceptor: HttpInterceptorFn = (req, next) => {
    const log = inject(DebugLogService);
    const requestBody = req.body instanceof FormData ? '[FormData]' : req.body;
    const entry = log.start(req.method, req.urlWithParams, requestBody);

    return next(req).pipe(
        tap(event => {
            if (event.type === HttpEventType.Response)
                log.finish(entry.id, event.status, event.body, false);
        }),
        catchError((err: HttpErrorResponse) => {
            log.finish(entry.id, err.status, err.error, true);
            return throwError(() => err);
        }),
    );
};
