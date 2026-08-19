import { test, expect } from '@playwright/test';
import { pinEnglish, signIn } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

// ADR-127 — the media library page exists and answers honestly when there is nothing in it.
// The scratch database starts with no assets, so the empty state is the state this suite can
// actually assert without uploading anything.
test('the media library opens and shows the empty state', async ({ page }) => {
    await page.goto('/library');
    await expect(page.locator('app-page-header')).toBeVisible();
    await expect(page.locator('.media-empty')).toBeVisible();
    await expect(page.locator('.media-empty')).toContainText('No files yet');
});
