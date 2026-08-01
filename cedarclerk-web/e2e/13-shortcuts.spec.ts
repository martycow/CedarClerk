import { test, expect } from '@playwright/test';
import { createDraft, pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

// T-100 — "Ctrl+B doesn't always fire" was a keyboard layout, not a race: ProseMirror matches
// shortcuts by event.key, and on a Cyrillic layout Ctrl+B arrives as Ctrl+и and matches nothing.
// Playwright's keyboard sends `key` and `code` separately, which is exactly what is needed to
// reproduce a layout the CI machine does not have installed.
test('Ctrl+B works on a Cyrillic layout', async ({ page, context }) => {
    const id = await createDraft(context, 'Горячие клавиши', ['Текст для проверки.']);
    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();

    await page.locator('.tiptap p').first().click();
    await page.keyboard.press('Control+a');

    // What a Russian layout actually delivers: the physical B key, reported as 'и'.
    await page.locator('.tiptap').dispatchEvent('keydown', {
        key: 'и', code: 'KeyB', ctrlKey: true, bubbles: true, cancelable: true,
    });

    await expect(page.locator('.tiptap strong')).toHaveCount(1);
});

test('Ctrl+B still works on a Latin layout, and only once', async ({ page, context }) => {
    const id = await createDraft(context, 'Latin shortcuts', ['Text to check.']);
    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();

    await page.locator('.tiptap p').first().click();
    await page.keyboard.press('Control+a');
    await page.keyboard.press('Control+b');

    // The regression this guards: handling the physical key as well as the letter would toggle
    // bold twice and leave the text plain.
    await expect(page.locator('.tiptap strong')).toHaveCount(1);
});
