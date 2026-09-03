import { test, expect } from '@playwright/test';
import { createDraft, openDraft, paragraphs, pinEnglish, signIn } from './helpers';

// ADR-067. Until 0.9.16 recovering a version meant a sqlite3 session on the Pi; this is the whole
// point of the feature being reachable at all.
test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

test('history lists versions and restores one', async ({ page, context }) => {
    const id = await createDraft(context, 'Version history', ['The original wording of this document.']);

    // A second revision, written the way the editor writes them.
    const save = await context.request.put(`/api/drafts/${id}`, {
        data: {
            title: 'Version history',
            cedarJson: JSON.stringify(paragraphs(['The replacement wording of this document.'])),
        },
    });
    expect(save.ok()).toBeTruthy();

    await openDraft(page, id);
    await expect(page.locator('.tiptap')).toContainText('replacement wording');

    await page.getByRole('button', { name: 'History', exact: true }).click();
    const modal = page.locator('app-modal', { hasText: 'history' });
    await expect(modal).toBeVisible();

    const rows = modal.locator('.revision-row, .revision-item, li, button').filter({ hasText: /edit|restored/i });
    await expect(rows.first()).toBeVisible();

    // The older of the two: the list is newest-first, so the second entry is the original.
    await rows.nth(1).click();
    await modal.getByRole('button', { name: 'Restore' }).click();

    await expect(page.locator('.tiptap')).toContainText('original wording', { timeout: 15_000 });
});

// A restore records what it replaced first, so it is itself undoable — the property that makes
// the feature safe to press.
test('a restore is itself recorded in the history', async ({ page, context }) => {
    const id = await createDraft(context, 'Undoable restore', ['first version text']);
    await context.request.put(`/api/drafts/${id}`, {
        data: { title: 'Undoable restore', cedarJson: JSON.stringify(paragraphs(['second version text'])) },
    });

    const list = await (await context.request.get(`/api/drafts/${id}/revisions/ru`)).json();
    expect(Array.isArray(list) ? list.length : list.revisions.length).toBeGreaterThanOrEqual(2);

    const revisions = Array.isArray(list) ? list : list.revisions;
    const oldest = revisions[revisions.length - 1];
    const restore = await context.request.post(`/api/drafts/${id}/revisions/${oldest.id}/restore`);
    expect(restore.ok()).toBeTruthy();

    const after = await (await context.request.get(`/api/drafts/${id}/revisions/ru`)).json();
    const afterRevisions = Array.isArray(after) ? after : after.revisions;
    expect(afterRevisions.length).toBeGreaterThan(revisions.length);

    await openDraft(page, id);
    await expect(page.locator('.tiptap')).toContainText('first version text');
});
