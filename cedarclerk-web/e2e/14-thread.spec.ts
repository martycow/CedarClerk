import { test, expect } from '@playwright/test';
import { createDraft, pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

// T-106. A document too big for one Telegram message can go out as a thread — but only if the
// author asks for it and can see where the cuts land first. Both halves are asserted here: the
// offer appears when it is the answer, and it does not appear when it is not.
test('a post that does not fit offers a thread, with its parts listed', async ({ page, context }) => {
    // Comfortably past Telegram's 32,768: eight paragraphs of five thousand characters.
    const long = Array.from({ length: 8 }, (_, i) => `Часть ${i + 1}. ` + 'я'.repeat(5000));
    const id = await createDraft(context, 'Длинный документ', long);

    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();
    await page.locator('.export-trigger').click();
    // A destination's settings panel only exists once it is ticked (ADR-096), so the checks below
    // live behind Telegram being chosen — which is also when its limits start to matter.
    await page.locator('.dest-card input[type=checkbox]').nth(1).check();

    // The problem is stated before the remedy is offered.
    await expect(page.locator('.publish-issues li.blocking')).toHaveCount(1);

    const toggle = page.locator('.thread-toggle input');
    await expect(toggle).toBeVisible();
    // Off by default: turning one post into several messages is never automatic.
    await expect(toggle).not.toBeChecked();

    await toggle.check();

    // The preview is what makes the split checkable rather than a promise.
    const parts = page.locator('.thread-parts li');
    await expect(parts.first()).toBeVisible();
    expect(await parts.count()).toBeGreaterThan(1);
    // And the issue it answers is gone from the list.
    await expect(page.locator('.publish-issues li.blocking')).toHaveCount(0);
});

test('a post that fits is never offered a thread', async ({ page, context }) => {
    const id = await createDraft(context, 'Обычный пост', ['Короткий текст.']);

    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();
    await page.locator('.export-trigger').click();
    await page.locator('.dest-card input[type=checkbox]').nth(1).check();

    await expect(page.locator('.thread-toggle')).toHaveCount(0);
});

// ADR-096. A short-post network offers two publications, not one with an option: an announcement
// carrying a link, or the document itself as a reply chain. The panel must show only the fields the
// chosen one uses — the old checkbox sat next to a text field the thread mode never reads.
//
// The account is stubbed rather than connected: both connect flows leave the machine (Bluesky
// verifies the app password against bsky.social, X is an OAuth round trip), and neither belongs in
// a suite that must run offline and touch nothing real.
test('a connected short-post network offers link and thread as two modes', async ({ page, context }) => {
    await page.route('**/api/publish/networks', route => route.fulfill({
        json: [{
            network: 'bluesky',
            capabilities: { network: 'bluesky', maxCharacters: 300, derivesShortPost: true },
            accounts: [{
                id: '11111111-1111-1111-1111-111111111111',
                network: 'bluesky', displayName: 'cedar.bsky.social', remoteId: 'did:plc:test',
                lastPublishedAt: null, lastError: null,
            }],
        }],
    }));
    await page.route('**/api/publish/thread-preview*', route => route.fulfill({
        json: { parts: [{ index: 0 }, { index: 1 }, { index: 2 }] },
    }));

    const id = await createDraft(context, 'Короткий пост', ['Тело поста.']);
    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();
    await page.locator('.export-trigger').click();

    // Blog, Telegram, then the connected network — an unconnected one renders no checkbox at all.
    const bluesky = page.locator('.dest-card input[type=checkbox]').nth(2);
    await bluesky.check();

    const modes = page.locator('.mode-toggle button');
    await expect(modes).toHaveCount(2);
    // Link mode is the default, and it is the one with a text field.
    await expect(modes.nth(0)).toHaveClass(/on/);
    await expect(page.locator('.export-section textarea')).toBeVisible();

    await modes.nth(1).click();
    await expect(modes.nth(1)).toHaveClass(/on/);
    // The thread does not read the override, so the field it would edit is gone — and the count
    // that replaces it is what makes the choice checkable before anything is sent.
    await expect(page.locator('.export-section textarea')).toHaveCount(0);
    // The number, not the sentence around it: this suite does not pin the account's UI language,
    // and "3 posts on Bluesky" is "3 постов в Bluesky" half the time.
    await expect(page.locator('.export-section').filter({ hasText: 'Bluesky' })).toContainText('3');
});
