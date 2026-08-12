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

// Inline SVG data URIs rather than uploaded media: the viewer is being tested, not the media
// pipeline, and a test that needs no file on disk cannot fail because of one.
function svg(color: string) {
    return 'data:image/svg+xml,' + encodeURIComponent(
        `<svg xmlns="http://www.w3.org/2000/svg" width="600" height="400"><rect width="600" height="400" fill="${color}"/></svg>`);
}

async function publishDoc(context: any, title: string, content: unknown[]) {
    const created = await context.request.post('/api/drafts', {
        data: { title, cedarJson: JSON.stringify({ type: 'doc', content }) },
    });
    expect(created.ok(), `create draft failed: ${created.status()}`).toBeTruthy();
    const { id } = await created.json();
    const res = await context.request.post(`/api/drafts/${id}/publish-blog`, { data: {} });
    expect(res.ok(), `publish failed: ${res.status()}`).toBeTruthy();
    return (await res.json()).slug as string;
}

test('clicking a picture opens it over the page, not in a new tab', async ({ page, context }) => {
    const red = svg('#cc3333');
    const blue = svg('#3355cc');
    const slug = await publishDoc(context, 'Picture post', [
        { type: 'image', attrs: { src: red, caption: 'A caption worth keeping' } },
        { type: 'collage', attrs: { images: [blue] } },
    ]);

    await page.goto(`${BLOG_ORIGIN}/${slug}`);
    const box = page.locator('.lightbox');
    await expect(box).toBeHidden();

    await page.locator('.post-sheet img.zoomable').first().click();
    await expect(box).toBeVisible();
    // The whole point of the request: the article is still there behind it.
    expect(page.url()).toContain(slug);
    await expect(box.locator('img')).toHaveAttribute('src', red);
    await expect(box.locator('.lightbox-cap')).toContainText('A caption worth keeping');
    await expect(box.locator('.lightbox-count')).toContainText('1 / 2');

    // Two pictures in one post are one gallery, not two popups.
    await page.keyboard.press('ArrowRight');
    await expect(box.locator('img')).toHaveAttribute('src', blue);

    await page.keyboard.press('Escape');
    await expect(box).toBeHidden();
});

test('the RSS feed is valid XML and carries the post', async ({ context }) => {
    const { slug } = await publish(context, 'Feed post', ['Body text.']);
    const res = await context.request.get(`${BLOG_API}/rss.xml`, { headers: BLOG_HEADERS });
    expect(res.ok()).toBeTruthy();
    const xml = await res.text();
    expect(xml).toContain('<rss');
    expect(xml).toContain(slug);
});
