import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// The mirror of authGuard: /login and /register are pages for someone who is *not* signed in.
// Without this they were the only routes that never asked the server anything, so landing on
// /login with a perfectly valid 30-day cookie showed a password form — while opening any other
// URL let the same browser straight in.
//
// 'unavailable' deliberately falls through to the page rather than redirecting: the session is
// unknown, not proven, and the login page has a retry for exactly that (T-062).
export const guestGuard: CanActivateFn = async (route) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    // Where the door was going to send them, if it carried a destination — a live session must not
    // lose the screen the redirect was for.
    const asked = route.queryParamMap.get('returnUrl');
    const back = asked && asked.startsWith('/') && !asked.startsWith('//') ? asked : '/projects';

    if (auth.userEmail()) return router.parseUrl(back);
    return await auth.refresh() === 'ok' ? router.parseUrl(back) : true;
};
