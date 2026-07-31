import { test, expect } from '@playwright/test';
import { ADMIN, pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

// ADR-044/ADR-050. The whole app is on t(); a screen that still holds an English literal is what
// this catches, in the cheapest possible form.
test('the UI language switches to Russian and survives a reload', async ({ page }) => {
    await page.goto('/settings');
    // I12 split Settings in two: the UI language lives under Account, with Profile as the default
    // tab, so the picker is one click away rather than on the page that opens.
    await page.getByRole('button', { name: 'Account', exact: true }).click();
    // Wait for the write, not just for the UI: the picker sets `lang` on <html> from the signal
    // immediately, while the profile POST is still in flight. Reloading between the two made this
    // test fail about one run in ten — /api/auth/me then answered with the old language and
    // adoptProfileLanguage put the UI back, which looks exactly like a persistence bug.
    const saved = page.waitForResponse(r => r.url().includes('/api/auth/ui-language') && r.ok());
    await page.locator('button', { hasText: 'RU · Русский' }).first().click();

    await expect(page.locator('html')).toHaveAttribute('lang', 'ru', { timeout: 10_000 });
    await saved;
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('lang', 'ru');
});

test('the theme toggle switches and persists', async ({ page }) => {
    await page.goto('/drafts');
    const before = await page.locator('html').getAttribute('data-theme');

    await page.locator('.theme-toggle').first().click();
    await expect(page.locator('html')).not.toHaveAttribute('data-theme', before ?? '');

    const after = await page.locator('html').getAttribute('data-theme');
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', after ?? '');
});

test('the glossary page opens', async ({ page }) => {
    await page.goto('/glossary');
    await expect(page.locator('app-page-header')).toBeVisible();
});

// Asserted on the account's own email rather than the word "Profile": the language test above
// leaves the UI in Russian (the choice is stored on the profile, so it outlives the browser), and
// a suite that only passes in the order it happens to run in is worse than no suite.
test('settings opens on the profile tab from the account menu deep link', async ({ page }) => {
    await page.goto('/settings?tab=profile');
    await expect(page.locator('body')).toContainText(ADMIN.email);
});
