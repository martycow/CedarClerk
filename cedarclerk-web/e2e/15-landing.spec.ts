import { test, expect } from '@playwright/test';
import { pinEnglish, signIn } from './helpers';

// T-009. The landing is server-rendered on the app's own host, so it is reached by address rather
// than through the dev server's baseURL.
const MAIN_ORIGIN = 'http://localhost:8080';

test('a stranger gets the landing, with prices that come from the code', async ({ page }) => {
    await page.goto(MAIN_ORIGIN + '/');

    await expect(page.locator('h1')).toContainText(/Write here|Пишите здесь/);
    // The numbers are read from PlanLimitations/Consts.Plans — a hand-written table would drift.
    await expect(page.locator('.plans')).toContainText('$3');
    await expect(page.locator('.plans')).toContainText('$6');
    await expect(page.locator('.plans')).toContainText('200 MB');
    // And the way in is on the page, not hidden behind a menu.
    await expect(page.locator('a[href="/register"]')).toBeVisible();
});

test('the landing answers in the language the browser asks for', async ({ browser }) => {
    const ru = await browser.newContext({ locale: 'ru-RU', extraHTTPHeaders: { 'Accept-Language': 'ru-RU,ru;q=0.9' } });
    const ruPage = await ru.newPage();
    await ruPage.goto(MAIN_ORIGIN + '/');
    await expect(ruPage.locator('h1')).toContainText('Пишите здесь');

    const en = await browser.newContext({ extraHTTPHeaders: { 'Accept-Language': 'en-US,en;q=0.9' } });
    const enPage = await en.newPage();
    await enPage.goto(MAIN_ORIGIN + '/');
    await expect(enPage.locator('h1')).toContainText('Write here');
});

// An author who types the address wants their drafts, not a sales page.
test('a signed-in visitor is not shown the landing', async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);

    const res = await context.request.get(MAIN_ORIGIN + '/');
    expect(await res.text()).not.toContain('plan-price');
});

// The regression this suite caught on its first run: the landing matched "/" on every host, so the
// blog's own index — its homepage — was replaced by a marketing page.
test('the blog keeps its own homepage', async ({ page }) => {
    await page.goto('http://blog.localhost:8080/');

    await expect(page.locator('body')).not.toContainText('What it costs');
    await expect(page.locator('body')).not.toContainText('Сколько стоит');
});
