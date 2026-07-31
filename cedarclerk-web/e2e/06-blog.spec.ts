import { test, expect } from '@playwright/test';
import { BLOG_API, BLOG_HEADERS, BLOG_ORIGIN, createDraft, pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

async function publish(context: any, title: string, body: string[]) {
    const id = await createDraft(context, title, body);
    const res = await context.request.post(`/api/drafts/${id}/publish-blog`, { data: {} });
    expect(res.ok(), `publish failed: ${res.status()}`).toBeTruthy();
    const { slug } = await res.json();
    return { id, slug };
}

test('a published post renders on the blog host', async ({ page, context }) => {
    const { slug } = await publish(context, 'Smoke post', ['A paragraph that must appear on the blog page.']);

    await page.goto(`${BLOG_ORIGIN}/${slug}`);
    await expect(page.locator('body')).toContainText('A paragraph that must appear on the blog page.');
    await expect(page.locator('.annotation-controls .react-btn').first()).toBeVisible();
});

test('an anonymous reader can leave a reaction', async ({ page, context }) => {
    const { slug } = await publish(context, 'Reactable', ['Something worth reacting to.']);

    await page.goto(`${BLOG_ORIGIN}/${slug}`);
    const like = page.locator('.react-btn[data-kind="like"]').last();
    await like.click();
    await expect(like.locator('.count')).toHaveText('1', { timeout: 10_000 });
});

test('an anonymous reader can leave a comment', async ({ page, context }) => {
    const { slug } = await publish(context, 'Commentable', ['Something worth commenting on.']);

    await page.goto(`${BLOG_ORIGIN}/${slug}`);
    const form = page.locator('form.comment-form').last();
    await form.locator('textarea.comment-text').fill('A comment from the smoke suite.');
    await form.locator('input.comment-author').fill('Playwright');
    await form.getByRole('button').click();

    await expect(page.locator('.comment-list')).toContainText('A comment from the smoke suite.', { timeout: 10_000 });
});

test('the blog index lists the published post', async ({ page, context }) => {
    const { slug } = await publish(context, 'Indexed post', ['Body text.']);
    await page.goto(BLOG_ORIGIN);
    await expect(page.locator(`a[href*="${slug}"]`).first()).toBeVisible();
});

test('the RSS feed is valid XML and carries the post', async ({ context }) => {
    const { slug } = await publish(context, 'Feed post', ['Body text.']);
    const res = await context.request.get(`${BLOG_API}/rss.xml`, { headers: BLOG_HEADERS });
    expect(res.ok()).toBeTruthy();
    const xml = await res.text();
    expect(xml).toContain('<rss');
    expect(xml).toContain(slug);
});
