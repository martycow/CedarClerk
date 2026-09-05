import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { ActivatedRouteSnapshot, PreloadAllModules, provideRouter, withPreloading, withViewTransitions } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { debugLogInterceptor } from './core/debug-log.interceptor';
import { sessionExpiryInterceptor } from './core/session-expiry.interceptor';
import { LocaleService } from './core/i18n/locale.service';

// XHR backend, not withFetch() (28.07.2026) — this app has no SSR (docs/tasks/ROADMAP.md), so fetch's
// only advantage here didn't apply, and it cost a real one: the Fetch API has no upload-progress
// mechanism in browsers at all, so DraftsService.importMarkdown$'s reportProgress:true silently
// never fired a single UploadProgress event — the bar sat at 0% and the new stall timeout (which
// only resets on a progress tick) killed even a healthy multi-minute upload at 60s.
// ADR-287 — a route can opt out of the cross-fade with `data: { viewTransition: false }`. Either
// end of the navigation counts: the editor is the one screen whose DOM is not Angular's to snapshot
// (TipTap owns it), and that holds whether it is being entered or left.
function optsOut(root: ActivatedRouteSnapshot): boolean {
  for (let node: ActivatedRouteSnapshot | null = root; node; node = node.firstChild)
    if (node.routeConfig?.data?.['viewTransition'] === false) return true;
  return false;
}

export function skipViewTransition(from: ActivatedRouteSnapshot, to: ActivatedRouteSnapshot, reducedMotion: boolean): boolean {
  return reducedMotion || optsOut(from) || optsOut(to);
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // ADR-263: only English is in the initial bundle. The first paint waits for the active
    // dictionary's chunk so no screen ever shows a fallback, and the others are fetched once the
    // browser is idle so a later switch is as instant as it was when every language shipped eagerly.
    provideAppInitializer(() => {
      const locale = inject(LocaleService);
      return locale.ready().then(() => {
        const idle = window.requestIdleCallback ?? ((cb: () => void) => setTimeout(cb, 1000));
        idle(() => void locale.preloadAll());
      });
    }),
    // T-092: every route is lazy, and PreloadAllModules fetches the rest in the background as soon
    // as the first one has rendered. Without it the split would trade a smaller first load for a
    // pause on every navigation, which on this app would be felt most opening the editor — the
    // heaviest chunk and the one people go to. With it, the chunk is usually already there.
    provideRouter(routes, withPreloading(PreloadAllModules), withViewTransitions({
      skipInitialTransition: true,
      onViewTransitionCreated: ({ transition, from, to }) => {
        const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (skipViewTransition(from, to, reduced)) transition.skipTransition();
      },
    })),
    provideHttpClient(withInterceptors([debugLogInterceptor, sessionExpiryInterceptor])),
  ]
};
