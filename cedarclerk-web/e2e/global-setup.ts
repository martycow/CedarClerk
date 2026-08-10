import { chromium, FullConfig } from '@playwright/test';

/**
 * Pays the Angular dev server's first-request cost before any test runs (T-143, 10.08.2026).
 *
 * Playwright's `webServer` waits for the port to answer, and `ng serve` answers with index.html the
 * moment it is listening — but every route is a lazy chunk (T-092/ADR-076), and the *first* request
 * for one triggers a build. So the first test of a cold run was racing a compile that the config
 * had already declared finished.
 *
 * The evidence this is built on, rather than a guess: `01-auth.spec.ts`'s first test failed twice in
 * eight cold suite runs, always as the very first test — and passed **40/40** under
 * `--repeat-each=10` against a dev server the earlier tests had already warmed.
 *
 * Navigating once here, in a real browser, fetches those chunks the same way a test would. Failures
 * are swallowed on purpose: this is a warm-up, and if it cannot connect, the tests themselves are
 * the right place for that to be reported.
 */
export default async function globalSetup(config: FullConfig) {
    const baseURL = config.projects[0]?.use?.baseURL ?? 'http://localhost:4200';

    const browser = await chromium.launch();
    try {
        const page = await browser.newPage();
        // /login is where the first test starts, and it pulls the shared chrome with it.
        await page.goto(`${baseURL}/login`, { waitUntil: 'networkidle', timeout: 120_000 });
        await page.locator('#cc-login-email').waitFor({ state: 'visible', timeout: 120_000 });
    } catch {
        // Deliberately silent — see above.
    } finally {
        await browser.close();
    }
}
