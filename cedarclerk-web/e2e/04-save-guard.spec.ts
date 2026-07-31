import { test, expect } from '@playwright/test';
import { createDraft, openDraft, paragraphs, pinEnglish, signIn, withSave } from './helpers';

// ADR-066. The 29.07 incident in one test: a save that would leave almost nothing must be refused
// and must offer the stored version back.
const LONG = [
    'This paragraph exists so the document is comfortably over the two hundred character floor the shrink guard requires before it will refuse anything at all.',
    'A second paragraph, so that deleting everything is unambiguous rather than a borderline case.',
    'And a third one, because a table being converted to paragraphs rewrites most of the JSON while keeping every word — which is why the guard measures extracted text.',
];

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

test('deleting nearly everything is refused, and the stored text comes back', async ({ page, context }) => {
    const id = await createDraft(context, 'Shrink guard', LONG);
    await openDraft(page, id);
    await expect(page.locator('.save-state .save-label')).toHaveText('Saved', { timeout: 20_000 });

    await page.locator('.tiptap').click();
    await page.keyboard.press('Control+a');
    await page.keyboard.type('x');

    const dialog = page.locator('app-modal', { hasText: 'Save stopped' });
    await expect(dialog).toBeVisible({ timeout: 20_000 });

    await dialog.getByRole('button', { name: 'Restore stored' }).click();
    await expect(page.locator('.tiptap')).toContainText('two hundred character floor');
    await expect(dialog).toHaveCount(0);
});

// The guard being lenient matters as much as it being strict: a false refusal on ordinary heavy
// editing is the failure mode that would make it hated, so it is asserted rather than assumed.
test('an ordinary heavy edit is not refused', async ({ page, context }) => {
    const id = await createDraft(context, 'Heavy but legitimate', LONG);
    await openDraft(page, id);
    await expect(page.locator('.save-state .save-label')).toHaveText('Saved', { timeout: 20_000 });

    // withSave waits for a 200 PUT, so this cannot pass by the save simply not having happened
    // yet — the point is that the server *accepted* a rewrite of this size.
    await withSave(page, async () => {
        await page.locator('.tiptap').click();
        await page.keyboard.press('Control+a');
        await page.keyboard.type(LONG[0] + ' Rewritten wholesale, but still a real document of real length.');
    });

    await expect(page.locator('app-modal', { hasText: 'Save stopped' })).toHaveCount(0);
});

// T-018.3, the API half — the editor sends expectedUpdatedAt, and a stale one must 409 rather
// than silently overwrite. Driven through the API because two concurrent editors is not a thing
// one browser can honestly stage.
test('a stale expectedUpdatedAt is refused by the server', async ({ context }) => {
    const id = await createDraft(context, 'Concurrency', LONG);
    const stale = new Date(Date.now() - 60_000).toISOString();

    const res = await context.request.put(`/api/drafts/${id}`, {
        data: {
            title: 'Concurrency',
            cedarJson: JSON.stringify(paragraphs([...LONG, 'An edit from a second tab.'])),
            expectedUpdatedAt: stale,
        },
    });
    expect(res.status()).toBe(409);
    expect((await res.json()).code).toBe('stale');
});
