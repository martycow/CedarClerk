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

test('the forms tab of the Posts Manager renders the preset', async ({ page, context }) => {
    await context.request.post('/api/form-presets', {
        data: { name: 'Visible preset', formJson: JSON.stringify(FORM) },
    });
    await page.goto('/posts?tab=forms');
    await expect(page.locator('.post-row', { hasText: 'Visible preset' })).toBeVisible();
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

    // Access granted: the same context now sees the body.
    await readerPage.goto(`${BLOG_ORIGIN}/${slug}`);
    await expect(readerPage.locator('body')).toContainText('only invited readers should see');
    await reader.close();

    const registrations = await (await context.request.get(`/api/drafts/${id}/registrations`)).json();
    expect(JSON.stringify(registrations)).toContain('Smoke Reader');
});
