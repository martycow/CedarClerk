import { test, expect } from '@playwright/test';
import { createDraft, openDraft, pinEnglish, signIn, typeInSheet, withSave } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

test('bold survives the save round-trip', async ({ page, context }) => {
    const id = await createDraft(context, 'Formatting', ['plain']);
    await openDraft(page, id);

    // Two saves, not one: the typing and the bold toggle fall either side of the 1.2s debounce
    // depending on how fast the machine is, and waiting once can return after the text was stored
    // but before the mark was.
    await withSave(page, () => typeInSheet(page, 'emphasised'));
    await withSave(page, async () => {
        await page.keyboard.press('Shift+Home');
        await page.keyboard.press('Control+b');
    });

    await page.reload();
    await expect(page.locator('.tiptap strong')).toContainText('emphasised');
});

test('a heading round-trips through the block dropdown', async ({ page, context }) => {
    const id = await createDraft(context, 'Headings', ['a line to promote']);
    await openDraft(page, id);

    await withSave(page, async () => {
        await page.locator('.tiptap p').first().click();
        await page.locator('.block-dropdown').click();
        await page.getByRole('button', { name: 'Heading 2', exact: true }).click();
    });

    await page.reload();
    await expect(page.locator('.tiptap h2')).toContainText('a line to promote');
});

// The status bar is the only place that says whether work is safe; a save indicator that lies is
// worse than none, and ADR-065 changed exactly this path (a no-op save must stay "Saved").
test('a no-op save does not flip the indicator to unsaved', async ({ page, context }) => {
    const id = await createDraft(context, 'No-op', ['untouched text']);
    await openDraft(page, id);
    await expect(page.locator('.save-state .save-label')).toHaveText('Saved', { timeout: 20_000 });

    await page.locator('.tiptap').click();
    await page.keyboard.press('End');
    await expect(page.locator('.save-state .save-label')).toHaveText('Saved');
});
