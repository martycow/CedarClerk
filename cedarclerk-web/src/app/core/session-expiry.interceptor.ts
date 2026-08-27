import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

// The auth cookie outlives the tab, but not forever, and until now nothing watched for the moment
// it stopped working: the guards only ask on navigation, so a session that ended while a screen
// sat open turned every action into a silent 401 — a Save that did nothing, a list that never
// loaded, and no way to tell that from a bug.
//
// Three endpoints are exempt because a 401 from them is an answer rather than an expiry: /me is
// how refresh() asks whether anyone is signed in, and login/register answer 401 for a wrong
// password. Everything else answering 401 means the session this app thought it had is gone.
const AnswerNot401Expiry = ['/api/auth/me', '/api/auth/login', '/api/auth/register'];

export const sessionExpiryInterceptor: HttpInterceptorFn = (req, next) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    return next(req).pipe(
        catchError((err: HttpErrorResponse) => {
            if (err.status === 401 && !AnswerNot401Expiry.some(path => req.url.includes(path)))
                auth.expireSession(router.url);
            return throwError(() => err);
        }),
    );
};
