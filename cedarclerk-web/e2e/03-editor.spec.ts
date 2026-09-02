import { test, expect } from '@playwright/test';
import { createDraft, expectSynced, openDraft, pinEnglish, signIn, typeInSheet, withSave } from './helpers';

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
        // The toolbar button, not Ctrl+B. The shortcut applied the mark when this test ran alone
        // and silently did nothing in a full run — the selection was there, the document never
        // changed, so no save ever fired. Whatever that is, it belongs in a test of its own
        // (T-100); this test is about a mark surviving the round-trip, and the button is what a
        // person actually presses.
        await page.getByTitle(/^Bold/).first().click();
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

// The rule is the only place that says whether work is safe; a save indicator that lies is worse
// than none, and ADR-065 changed exactly this path (a no-op save must stay synced).
test('a no-op save does not flip the indicator to unsaved', async ({ page, context }) => {
    const id = await createDraft(context, 'No-op', ['untouched text']);
    await openDraft(page, id);
    await expectSynced(page, 20_000);

    // The first "Synced" is not yet quiet — the load can still write once more behind it, and that
    // write is not what this test is about. Wait for a run of them before opening the window.
    const settled = await page.evaluate(async () => {
        let run = 0;
        for (let i = 0; i < 400; i++) {
            run = synced() ? run + 1 : 0;
            if (run >= 30) return true;
            await new Promise(r => setTimeout(r, 50));
        }
        return false;

        function synced() {
            return (document.querySelector('app-document-frame .frame-footer')?.textContent ?? '').includes('Synced');
        }
    });
    expect(settled, 'the editor never held Synced long enough to watch').toBe(true);

    // Watched across the interaction rather than read after it. markDirty() flips the word to
    // "Syncing…" and the autosave puts it back well inside expect's retry window, so any assertion
    // made afterwards — retrying or not — reads "Synced" whether or not moving the caret dirtied
    // the document: the test passed with markDirty() wired to every transaction. The watcher opens
    // before the click and runs past the autosave debounce, so a flip has nowhere to hide.
    const words = page.evaluate(async () => {
        const seen = new Set<string>();
        for (let i = 0; i < 60; i++) {
            const text = (document.querySelector('app-document-frame .frame-footer')?.textContent ?? '').replace(/\s+/g, ' ');
            seen.add(text.includes('Synced') ? 'Synced' : text.trim().slice(0, 60));
            await new Promise(r => setTimeout(r, 50));
        }
        return [...seen];
    });

    await page.locator('.tiptap').click();
    await page.keyboard.press('End');
    expect(await words).toEqual(['Synced']);
});

// ADR-239 clause 9 — the Preview tab deep-links by ?tab= and draws the phone from the server's
// projection (CONTRACT §E8): a seeded draft of one paragraph is exactly one bubble, and the words
// in it are the document's own. Telegram is picked by its destination row; the channel need not
// be connected for the projection to answer.
test('the Preview tab renders a Telegram bubble for a seeded draft', async ({ page, context }) => {
    const id = await createDraft(context, 'Preview', ['One short paragraph for the phone.']);
    await page.goto(`/editor?draft=${id}&tab=preview`);
    await expect(page.getByRole('tab', { name: 'Preview' })).toHaveAttribute('aria-selected', 'true');

    await page.getByRole('tab', { name: /^Telegram/ }).click();
    const bubbles = page.locator('app-preview-phone .bubble');
    await expect(bubbles).toHaveCount(1);
    await expect(bubbles.first()).toContainText('One short paragraph for the phone.');
});

// ADR-242 — Publish / Export is the third selected state, not a door to a modal. Write stays
// mounted behind it (TipTap owns the sheet's node), a copy target or a coming-later network never
// carries a checkbox, inspecting a destination never includes it, and the three footers walk the
// document forward and back.
test('the Publish / Export tab deep-links, keeps Write mounted and walks the three steps', async ({ page, context }) => {
    const id = await createDraft(context, 'Publish tab', ['One paragraph for the rack.']);
    await page.goto(`/editor?draft=${id}&tab=publish`);
    await expect(page.getByRole('tab', { name: 'Publish / Export' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('app-publish-stepper .ps-step')).toHaveCount(4);
    await expect(page.locator('.main .tiptap')).toBeAttached();
    await expect(page.locator('.main')).toBeHidden();

    await expect(page.locator('.dest-card.copy-target input[type=checkbox]')).toHaveCount(0);
    await expect(page.locator('.dest-card.unsupported input[type=checkbox]')).toHaveCount(0);

    const blog = page.locator('.dest-card[data-destination="blog"]');
    await blog.locator('.dest-select').click();
    await expect(blog.locator('input[type=checkbox]')).not.toBeChecked();
    await expect(page.getByRole('button', { name: 'Publish', exact: true })).toBeDisabled();
    await blog.locator('input[type=checkbox]').check();
    await expect(page.getByRole('button', { name: 'Publish to 1 destination' })).toBeEnabled();
    // The review names the state in words as well as marks.
    await expect(page.locator('app-preview-checks .pc-group[data-tone="blocking"] .pc-clear')).toHaveText('All clear');

    await page.getByRole('button', { name: 'Back' }).click();
    await expect(page.getByRole('tab', { name: 'Preview' })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('button', { name: 'Continue to Publish' }).click();
    await expect(page).toHaveURL(/tab=publish/);
    await page.getByRole('tab', { name: 'Write' }).click();
    await expect(page).not.toHaveURL(/tab=/);
    await page.getByRole('button', { name: 'Continue to Preview' }).click();
    await expect(page).toHaveURL(/tab=preview/);
});
