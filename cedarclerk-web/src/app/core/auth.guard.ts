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
        return onboarded(auth, router, state.url);
    }
    // 'unavailable' also lands on /login, but with serverUnreachable set so the page offers a
    // retry instead of pretending the session ended (T-062).
    if (await auth.refresh() !== 'ok') {
        // The URL travels with the redirect: whoever signs in came here for a screen, not for the
        // hub, and the door is what remembers which one.
        return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }
    appearance.loadFromAuth();
    return onboarded(auth, router, state.url);
};

// T-328 — the display name doubles as the "went through onboarding" mark: no separate flag, and
// an account that predates the door goes through it once too.
const onboarded = (auth: AuthService, router: Router, url: string) =>
    auth.authorDisplayName()
        ? true
        : router.createUrlTree(['/onboarding'], { queryParams: { returnUrl: url } });

/** The onboarding door itself: needs a session, and an already-named account has no business here. */
export const onboardingGuard: CanActivateFn = async (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.userEmail() && await auth.refresh() !== 'ok') {
        return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }
    return auth.authorDisplayName() ? router.parseUrl('/') : true;
};