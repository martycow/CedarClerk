import { test, expect } from '@playwright/test';
import { BLOG_API, BLOG_HEADERS, BLOG_ORIGIN, createDraft, pinEnglish, signIn } from './helpers';

// B3 / ADR-042 end to end: a private post shows its registration gate instead of a 404, a
// submission grants access, and the owner can read what was submitted.
test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

const FORM = {
    intro: 'Tell me who you are.',
    requireName: true,
    requireNickname: false,
    requireEmail: true,
    requireSocial: false,
    questions: [
        { id: 'q1', label: 'Where did you hear about this?', type: 'text', options: [], required: false },
    ],
};

test('a form preset can be created and listed', async ({ context }) => {
    const create = await context.request.post('/api/form-presets', {
        data: { name: 'Smoke preset', formJson: JSON.stringify(FORM) },
    });
    expect(create.ok(), `preset create failed: ${create.status()}`).toBeTruthy();

    const list = await (await context.request.get('/api/form-presets')).json();
    expect(list.some((p: any) => p.name === 'Smoke preset')).toBeTruthy();
});

test('the Forms page renders the preset', async ({ page, context }) => {
    await context.request.post('/api/form-presets', {
        data: { name: 'Visible preset', formJson: JSON.stringify(FORM) },
    });
    await page.goto('/forms');
    await expect(page.locator('.post-card', { hasText: 'Visible preset' })).toBeVisible();
});

test('a private post shows the gate, and a submission reaches the owner', async ({ page, context }) => {
    const id = await createDraft(context, 'Gated post', ['Text that only invited readers should see.']);

    const form = await context.request.post(`/api/drafts/${id}/registration-form`, {
        data: { formJson: JSON.stringify(FORM) },
    });
    expect(form.ok(), `form attach failed: ${form.status()}`).toBeTruthy();

    const priv = await context.request.post(`/api/drafts/${id}/private`, { data: { isPrivate: true } });
    expect(priv.ok(), `private toggle failed: ${priv.status()}`).toBeTruthy();

    const publish = await context.request.post(`/api/drafts/${id}/publish-blog`, { data: {} });
    expect(publish.ok(), `publish failed: ${publish.status()}`).toBeTruthy();
    const { slug } = await publish.json();

    // A reader with no access cookie: a fresh context, not the signed-in one.
    const reader = await page.context().browser()!.newContext();
    const readerPage = await reader.newPage();
    await readerPage.goto(`${BLOG_ORIGIN}/${slug}`);

    // The gate answers in the post's own language when the reader asks for none (FI4.1), so the
    // chrome here is Russian. Asserted on the author's own intro instead — it is never machine-
    // translated, by design, and so reads the same whichever language the gate is showing.
    await expect(readerPage.locator('body')).toContainText(FORM.intro);
    await expect(readerPage.locator('body')).not.toContainText('only invited readers should see');

    // Submitted through the form rather than the API on purpose. The access cookie is scoped to
    // the host that set it, and an API call has to reach the blog branch by address with a Host
    // header (Node resolves no *.localhost), which would leave the cookie on 127.0.0.1 where the
    // page navigation would never send it. Driving the form also covers the gate UI itself, which
    // is one of the things `TASKS.md` lists as never having been clicked.
    await readerPage.locator('.reg-input[data-field="name"]').fill('Smoke Reader');
    await readerPage.locator('.reg-input[data-field="email"]').fill('reader@local.test');
    await readerPage.locator('[data-question="q1"]').fill('From the smoke suite');

    // The submit is a fetch, and the access cookie arrives on its response — so the response is
    // what to wait for. Navigating straight after the click aborts the request in flight, which is
    // what made this look like a broken gate on the first run rather than a broken test.
    const submitted = readerPage.waitForResponse(
        r => r.url().includes(`/api/posts/${slug}/register`) && r.request().method() === 'POST',
    );
    await readerPage.locator('form.reg-form button').click();
    // Status only: the page script reloads on success, and reading the body of a response the
    // browser has already navigated away from is not possible.
    expect((await submitted).status()).toBe(201);

    // No navigation here: the page script reloads itself once the cookie is in, and a goto racing
    // that reload aborts it (ERR_ABORTED). Waiting for the body is also the more honest assertion —
    // it says the reader ended up on the post, not that a second request would have worked.
    await expect(readerPage.locator('body')).toContainText('only invited readers should see', { timeout: 15_000 });
    await reader.close();

    const registrations = await (await context.request.get(`/api/drafts/${id}/registrations`)).json();
    expect(JSON.stringify(registrations)).toContain('Smoke Reader');
});

// T-064 — the reported incident: a reader filled in the form in Telegram's in-app browser, opened
// the post in Chrome, and was asked to register again. The access now travels in the link, so a
// browser that has never seen this post can use it.
test('the access link from a submission works in another browser', async ({ page, context }) => {
    const id = await createDraft(context, 'Portable access', ['Text only registered readers see.']);
    await context.request.post(`/api/drafts/${id}/registration-form`, { data: { formJson: JSON.stringify(FORM) } });
    await context.request.post(`/api/drafts/${id}/private`, { data: { isPrivate: true } });
    const publish = await context.request.post(`/api/drafts/${id}/publish-blog`, { data: {} });
    const { slug } = await publish.json();

    // Browser one: fills in the form and is let in.
    const first = await page.context().browser()!.newContext();
    const firstPage = await first.newPage();
    await firstPage.goto(`${BLOG_ORIGIN}/${slug}`);
    await firstPage.locator('.reg-input[data-field="name"]').fill('First Browser');
    await firstPage.locator('.reg-input[data-field="email"]').fill('first@local.test');
    await firstPage.locator('[data-question="q1"]').fill('From the smoke suite');
    await firstPage.locator('.reg-submit').click();

    // Asserted on the address bar rather than on the response body: the submit navigates straight
    // to the access link, so by the time a test could read the body it is gone — and the address
    // is what the reader can actually copy, which is the whole point of T-064.
    await firstPage.waitForURL(/\?access=/);
    const accessUrl = firstPage.url();
    await expect(firstPage.locator('body')).toContainText('Text only registered readers see');

    // Browser two: never saw this post, has no cookies — and the link is all it needs.
    const second = await page.context().browser()!.newContext();
    const secondPage = await second.newPage();
    await secondPage.goto(accessUrl);

    await expect(secondPage.locator('body')).toContainText('Text only registered readers see');

    // And the gate still stands for a browser without the link.
    const third = await page.context().browser()!.newContext();
    const thirdPage = await third.newPage();
    await thirdPage.goto(`${BLOG_ORIGIN}/${slug}`);
    await expect(thirdPage.locator('body')).not.toContainText('Text only registered readers see');
});
