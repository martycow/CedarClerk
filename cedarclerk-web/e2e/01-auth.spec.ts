import { test, expect } from '@playwright/test';
import { ADMIN, pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => pinEnglish(context));

test('login form signs in and lands on /drafts', async ({ page }) => {
    await page.goto('/login');
    await page.locator('#cc-login-email').fill(ADMIN.email);
    await page.locator('#cc-login-pass').fill(ADMIN.password);
    await page.locator('.primary-btn').click();
    await expect(page).toHaveURL(/\/drafts/);
});

// v0.9.16/0.9.17: the cookie is 30 days and the ticket's ExpireTimeSpan was raised to match, so a
// reload must not ask again. This is the regression the two releases were about.
test('session survives a reload', async ({ page, context }) => {
    await signIn(context);
    await page.goto('/drafts');
    await expect(page).toHaveURL(/\/drafts/);
    await page.reload();
    await expect(page).toHaveURL(/\/drafts/);
    await expect(page.locator('.drafts-page')).toBeVisible();
});

// v0.9.17's guestGuard: /login and /register were the only routes that never asked whether a
// session already existed, which is what made a returning browser see a password prompt.
test('a signed-in browser is redirected away from /login', async ({ page, context }) => {
    await signIn(context);
    await page.goto('/login');
    await expect(page).toHaveURL(/\/drafts/);
});

test('an unauthenticated browser is sent to /login', async ({ page }) => {
    await page.goto('/drafts');
    await expect(page).toHaveURL(/\/login/);
});
