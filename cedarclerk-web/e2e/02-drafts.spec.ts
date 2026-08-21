import { test, expect } from '@playwright/test';
import { createDraft, openDraft, pinEnglish, signIn, typeInSheet, withSave } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

test('new draft dialog creates a draft and opens the editor', async ({ page }) => {
    await page.goto('/drafts');
    await page.getByRole('button', { name: 'New draft' }).click();

    const dialog = page.locator('app-modal');
    // The collapsed dialog holds exactly one field, so the role says which one without naming a
    // placeholder that is about to be re-worded.
    await dialog.getByRole('textbox').fill('Created by Playwright');
    // The confirm button repeats the dialog's own name rather than saying "Create" — scoped to the
    // modal so it can't match the toolbar button that opened it.
    await dialog.getByRole('button', { name: 'New draft' }).click();

    await expect(page).toHaveURL(/\/editor/);
    await expect(page.getByPlaceholder('Draft title')).toHaveValue('Created by Playwright');
});

// The round-trip that matters most: what the browser shows after a reload is what the server
// actually stored, not what the client still had in memory.
test('typed text survives a reload', async ({ page, context }) => {
    const id = await createDraft(context, 'Round trip', ['First line.']);
    await openDraft(page, id);
    await withSave(page, () => typeInSheet(page, ' Second sentence added by the suite.'));

    await page.reload();
    await expect(page.locator('.tiptap')).toContainText('Second sentence added by the suite.');
});

test('renaming a draft shows up in the list without a reload', async ({ page, context }) => {
    const id = await createDraft(context, 'Before rename');
    await openDraft(page, id);
    await withSave(page, () => page.getByPlaceholder('Draft title').fill('After rename'));

    await page.locator('app-hook-rail a[href="/drafts"]').click();
    await expect(page.locator('.drafts-title', { hasText: 'After rename' })).toBeVisible();
    await expect(page.locator('.drafts-title', { hasText: 'Before rename' })).toHaveCount(0);
});

test('search filters the drafts list', async ({ page, context }) => {
    await createDraft(context, 'Findable by search');
    await createDraft(context, 'Unrelated document');
    await page.goto('/drafts');
    await page.getByPlaceholder('Search title or tag').fill('Findable');
    await expect(page.locator('.drafts-title', { hasText: 'Findable by search' })).toBeVisible();
    await expect(page.locator('.drafts-title', { hasText: 'Unrelated document' })).toHaveCount(0);
});
