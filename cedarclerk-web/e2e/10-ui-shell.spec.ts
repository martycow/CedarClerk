import { test, expect } from '@playwright/test';
import { ADMIN, pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

// The UI language is stored on the profile, not in the browser, so the test below outlives its own
// browser context and every later file in the run reads a Russian UI — which is how one broken test
// here made four unrelated ones fail two files away. afterEach rather than the end of that test, so
// a failure halfway through still hands the account back the way it was found.
test.afterEach(async ({ context }) => {
    await context.request.post('/api/auth/ui-language', { data: { uiLanguage: 'en' } });
});

// ADR-044/ADR-050. The whole app is on t(); a screen that still holds an English literal is what
// this catches, in the cheapest possible form.
test('the UI language switches to Russian and survives a reload', async ({ page }) => {
    await page.goto('/settings');
    // I12 split Settings in two: the UI language lives under Account, with Profile as the default
    // tab, so the picker is one click away rather than on the page that opens.
    await page.getByRole('tab', { name: 'Account', exact: true }).click();
    // Wait for the write, not just for the UI: the picker sets `lang` on <html> from the signal
    // immediately, while the profile POST is still in flight. Reloading between the two made this
    // test fail about one run in ten — /api/auth/me then answered with the old language and
    // adoptProfileLanguage put the UI back, which looks exactly like a persistence bug.
    const saved = page.waitForResponse(r => r.url().includes('/api/auth/ui-language') && r.ok());
    // The picker is a pair of interactive leaf tags now, and a leaf's pick target is a span
    // carrying role=button rather than a <button> element.
    await page.getByRole('button', { name: 'RU · Русский' }).click();

    await expect(page.locator('html')).toHaveAttribute('lang', 'ru', { timeout: 10_000 });
    await saved;
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('lang', 'ru');
});

test('the theme toggle switches and persists', async ({ page }) => {
    await page.goto('/drafts');
    const before = await page.locator('html').getAttribute('data-theme');

    // The theme toggle lives in the account menu (ADR-239 clause 4); the trigger's visible text is
    // the account's own name, so it is found by its title rather than by a word.
    await page.getByTitle('Account', { exact: true }).click();
    await page.getByRole('button', { name: 'Toggle theme' }).click();
    await expect(page.locator('html')).not.toHaveAttribute('data-theme', before ?? '');

    const after = await page.locator('html').getAttribute('data-theme');
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', after ?? '');
});

test('the glossary page opens', async ({ page }) => {
    await page.goto('/glossary');
    await expect(page.locator('app-sidebar')).toBeVisible();
});

// Asserted on the account's own email rather than on a tab label: the email is the one thing on
// this screen that reads the same in either UI language.
test('settings opens on the profile tab from the account menu deep link', async ({ page }) => {
    await page.goto('/settings?tab=profile');
    await expect(page.locator('body')).toContainText(ADMIN.email);
});
