import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { AppearanceService } from './appearance.service';

export const authGuard: CanActivateFn = async (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const appearance = inject(AppearanceService);

    if (auth.userEmail()) {
        appearance.loadFromAuth();
        return true;
    }
    // 'unavailable' also lands on /login, but with serverUnreachable set so the page offers a
    // retry instead of pretending the session ended (T-062).
    if (await auth.refresh() !== 'ok') {
        // The URL travels with the redirect: whoever signs in came here for a screen, not for the
        // hub, and the door is what remembers which one.
        return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }
    appearance.loadFromAuth();
    return true;
};