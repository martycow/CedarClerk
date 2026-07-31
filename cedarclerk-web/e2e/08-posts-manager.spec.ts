import { test, expect } from '@playwright/test';
import { createDraft, pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

test('the posts list opens and a post can be selected', async ({ page, context }) => {
    await createDraft(context, 'Managed post', ['Body.']);
    await page.goto('/posts');

    const row = page.locator('.post-row', { hasText: 'Managed post' });
    await expect(row).toBeVisible();
    await row.click();
    await expect(row).toHaveClass(/on/);
});

test('search narrows the post list', async ({ page, context }) => {
    await createDraft(context, 'Searchable in manager');
    await createDraft(context, 'Other manager post');
    await page.goto('/posts');

    await page.locator('.post-search').fill('Searchable');
    await expect(page.locator('.post-row', { hasText: 'Searchable in manager' })).toBeVisible();
    await expect(page.locator('.post-row', { hasText: 'Other manager post' })).toHaveCount(0);
});

// N7 folded /comments and /stats into this page; the old paths stay as redirects because they are
// what any existing bookmark points at.
test('the retired /stats and /comments routes still resolve', async ({ page }) => {
    await page.goto('/stats');
    await expect(page).toHaveURL(/\/posts/);
    await page.goto('/comments');
    await expect(page).toHaveURL(/\/posts/);
});
