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
    await page.getByRole('tab', { name: 'Preferences', exact: true }).click();
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
    await page.goto('/settings?tab=preferences');
    const before = await page.locator('html').getAttribute('data-theme');
    try {
        const saved = page.waitForResponse(r => r.url().includes('/api/auth/appearance') && r.ok());
        await page.getByRole('button', { name: before === 'dark' ? 'Light' : 'Dark', exact: true }).click();
        await expect(page.locator('html')).not.toHaveAttribute('data-theme', before ?? '');
        await saved;

        const after = await page.locator('html').getAttribute('data-theme');
        await page.reload();
        await expect(page.locator('html')).toHaveAttribute('data-theme', after ?? '');
    } finally {
        const current = await page.locator('html').getAttribute('data-theme');
        if (before && current !== before) {
            const restored = page.waitForResponse(r => r.url().includes('/api/auth/appearance') && r.ok());
            await page.getByRole('button', { name: before === 'dark' ? 'Dark' : 'Light', exact: true }).click();
            await restored;
        }
    }
});

test('the glossary page opens', async ({ page }) => {
    await page.goto('/glossary');
    await expect(page.locator('app-sidebar')).toBeVisible();
});

// Asserted on the account's own email rather than on a tab label: the email is the one thing on
// this screen that reads the same in either UI language.
test('the account menu has one Settings door and opens Profile by default', async ({ page }) => {
    await page.goto('/drafts');
    await page.getByTitle('Account', { exact: true }).click();
    await page.getByRole('link', { name: 'Settings', exact: true }).click();
    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.locator('body')).toContainText(ADMIN.email);
});
