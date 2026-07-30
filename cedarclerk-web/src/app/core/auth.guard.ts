import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { AppearanceService } from './appearance.service';
import { ToolbarLayoutService } from './toolbar-layout.service';

export const authGuard: CanActivateFn = async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const appearance = inject(AppearanceService);
    const toolbarLayout = inject(ToolbarLayoutService);

    if (auth.userEmail()) {
        appearance.loadFromAuth();
        toolbarLayout.loadFromAuth();
        return true;
    }
    // 'unavailable' also lands on /login, but with serverUnreachable set so the page offers a
    // retry instead of pretending the session ended (T-062).
    if (await auth.refresh() !== 'ok') return router.parseUrl('/login');
    appearance.loadFromAuth();
    toolbarLayout.loadFromAuth();
    return true;
};