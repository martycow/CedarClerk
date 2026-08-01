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
    // The destination sections fold when unticked (N5), so the checks below live behind Telegram
    // being chosen — which is also when its limits start to matter.
    await page.locator('.dest-head input[type=checkbox]').nth(1).check();

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
    await page.locator('.dest-head input[type=checkbox]').nth(1).check();

    await expect(page.locator('.thread-toggle')).toHaveCount(0);
});
