import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { debugLogInterceptor } from './core/debug-log.interceptor';

// XHR backend, not withFetch() (28.07.2026) — this app has no SSR (docs/ROADMAP.md), so fetch's
// only advantage here didn't apply, and it cost a real one: the Fetch API has no upload-progress
// mechanism in browsers at all, so DraftsService.importMarkdown$'s reportProgress:true silently
// never fired a single UploadProgress event — the bar sat at 0% and the new stall timeout (which
// only resets on a progress tick) killed even a healthy multi-minute upload at 60s.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([debugLogInterceptor])),
  ]
};
