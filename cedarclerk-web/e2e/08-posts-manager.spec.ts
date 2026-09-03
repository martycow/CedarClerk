import { test, expect } from '@playwright/test';
import { BLOG_API, BLOG_HEADERS, createDraft, pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

test('the posts list opens and a post can be selected', async ({ page, context }) => {
    await createDraft(context, 'Managed post', ['Body.']);
    await page.goto('/posts');

    const row = page.locator('.post-card', { hasText: 'Managed post' });
    await expect(row).toBeVisible();
    await row.click();
    await expect(row).toHaveClass(/on/);
});

test('search narrows the post list', async ({ page, context }) => {
    await createDraft(context, 'Searchable in manager');
    await createDraft(context, 'Other manager post');
    await page.goto('/posts');

    await page.getByRole('searchbox', { name: 'Search title or tag' }).fill('Searchable');
    await expect(page.locator('.post-card', { hasText: 'Searchable in manager' })).toBeVisible();
    await expect(page.locator('.post-card', { hasText: 'Other manager post' })).toHaveCount(0);
});

test('the active manager collection survives reload', async ({ page }) => {
    await page.goto('/posts');
    const tabs = page.getByRole('tablist', { name: 'Manager sections' });

    await tabs.getByRole('tab', { name: 'Stats' }).click();
    await expect(page).toHaveURL(/[?&]tab=stats(?:&|$)/);
    await page.reload();
    await expect(page.getByRole('tab', { name: 'Stats' })).toHaveAttribute('aria-selected', 'true');

    await page.getByRole('tab', { name: 'Forms' }).click();
    await expect(page).toHaveURL(/[?&]tab=forms(?:&|$)/);
    await page.reload();
    await expect(page.getByRole('tab', { name: 'Forms' })).toHaveAttribute('aria-selected', 'true');
});

// N7 folded /comments and /stats into this page; the old paths stay as redirects because they are
// what any existing bookmark points at.
test('the retired /stats and /comments routes still resolve', async ({ page }) => {
    await page.goto('/stats');
    await expect(page).toHaveURL(/\/posts/);
    await page.goto('/comments');
    await expect(page).toHaveURL(/\/posts/);
});

// ADR-097 — the audience split. Reads through the whole path on purpose: a blog page fetched with
// Cloudflare's country header has to become a row on the stats tab, which is the only thing that
// proves the header is being read at all (nothing else in the app touches CF-IPCountry).
test('a blog view with a country header shows up in the audience breakdown', async ({ page, context }) => {
    const id = await createDraft(context, 'Audience post', ['Read from somewhere.']);
    const published = await context.request.post(`/api/drafts/${id}/publish-blog`, { data: {} });
    expect(published.ok(), `publish failed: ${published.status()}`).toBeTruthy();
    const { slug } = await published.json();

    const view = await context.request.get(`${BLOG_API}/${slug}`, {
        headers: { ...BLOG_HEADERS, 'CF-IPCountry': 'DE', 'Accept-Language': 'de-DE,de;q=0.9' },
    });
    expect(view.ok(), `blog view failed: ${view.status()}`).toBeTruthy();

    await page.goto('/posts');
    await page.locator('.manager-tabs button', { hasText: 'Stats' }).click();

    // The breakdown lives in the audience card (T-222): two lists under two headings, country
    // first, so the row a language would also match is still read out of the right one.
    const shelf = page.locator('.audience-shelf');
    await expect(shelf.locator('.group-head').first()).toHaveText(/country/i);
    await expect(shelf.locator('.audience-list').nth(0)
        .locator('.audience-row', { hasText: 'Germany' })).toBeVisible({ timeout: 10_000 });
    await expect(shelf.locator('.audience-list').nth(1)
        .locator('.audience-row', { hasText: 'German' })).toBeVisible();
});
