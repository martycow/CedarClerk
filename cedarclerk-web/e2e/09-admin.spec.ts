import { test, expect } from '@playwright/test';
import { ADMIN, pinEnglish, registerAccount, signIn } from './helpers';

test.beforeEach(async ({ context }) => pinEnglish(context));

test('the admin page lists users', async ({ page, context }) => {
    await signIn(context);
    await page.goto('/admin');
    await expect(page.locator('.admin-tabs')).toBeVisible();
    await expect(page.locator('body')).toContainText(ADMIN.email);
});

// The audit log was built alongside the actions it records, on the argument that a log starting
// halfway through is missing exactly what someone would go looking for. Paging came later.
test('the audit log answers with a paging shape', async ({ context }) => {
    await signIn(context);
    const res = await context.request.get('/api/admin/audit?skip=0');
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body).toHaveProperty('hasMore');
});

// TASKS.md carried this as "no automated test covers this" — the project has no HTTP-level
// integration tests, so the gate has only ever been checked by hand.
test('a signed-in non-admin gets 404 from the admin API and no admin page', async ({ page, context }) => {
    const plain = { email: `plain-${Date.now()}@local.test`, password: 'E2e-passw0rd!' };
    await registerAccount(context, plain.email, plain.password);
    await signIn(context, plain);

    const res = await context.request.get('/api/admin/users');
    expect(res.status()).toBe(404);

    await page.goto('/admin');
    await expect(page).not.toHaveURL(/\/admin/);
});
