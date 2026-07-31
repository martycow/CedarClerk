import { test, expect, Page } from '@playwright/test';
import fs from 'fs';
import path from 'path';
import { ADMIN, BLOG_ORIGIN, createDraft, paragraphs, pinEnglish, signIn } from './helpers';

// Phase 10 Block D — the visual half of the audit. NOT part of the smoke suite: it asserts almost
// nothing, it captures each surface (including the ones nobody has ever opened) so the screenshots
// can be read afterwards. Opt in with AUDIT=1, so an ordinary `npm run e2e` stays 37 behavioural
// tests.
//
// Driven from Playwright rather than the browser extension because the extension turned out to be
// unreliable in this environment — screenshots timing out, zoom returning the wrong region,
// keystrokes not reaching the TipTap surface.
const OUT = path.resolve('../.e2e-audit');
const enabled = process.env.AUDIT === '1';

test.skip(!enabled, 'set AUDIT=1 to capture the audit screenshots');

test.beforeAll(() => fs.mkdirSync(OUT, { recursive: true }));

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

async function shot(page: Page, name: string) {
    await page.waitForTimeout(400);
    await page.screenshot({ path: path.join(OUT, `${name}.png`), fullPage: true });
}

test.use({ viewport: { width: 1440, height: 900 } });

test('@audit signed-out screens', async ({ page, context }) => {
    await context.clearCookies();
    await page.goto('/login');
    await shot(page, '01-login');
    await page.goto('/register');
    await shot(page, '02-register');
    await page.goto('/terms');
    await shot(page, '03-terms');
});

test('@audit drafts screen, both views', async ({ page, context }) => {
    await page.goto('/drafts');
    await shot(page, '10-drafts-empty');

    for (const t of ['Публичный пост о кедрах', 'Draft with a very long title that should not break the row layout on a narrow column', 'Третий']) {
        await createDraft(context, t, ['Текст поста.']);
    }
    await page.goto('/drafts');
    await shot(page, '11-drafts-table');

    await page.locator('.view-toggle button').nth(1).click();
    await shot(page, '12-drafts-grid');

    await page.locator('.view-toggle button').nth(0).click();
    await page.locator('.folder-filter-btn').click();
    await shot(page, '13-drafts-folder-menu');
    await page.keyboard.press('Escape');

    await page.locator('app-tag-picker button').first().click();
    await shot(page, '14-drafts-tag-manage');
});

test('@audit editor and its modals', async ({ page, context }) => {
    const id = await createDraft(context, 'Проверка редактора', [
        'Первый абзац для проверки экранов. Текста хватает, чтобы страница блога выглядела как настоящий пост.',
        'Второй абзац, чтобы в истории версий было что сравнивать.',
    ]);
    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();
    await shot(page, '20-editor');

    await page.locator('.export-trigger').click();
    await shot(page, '21-export-collapsed');

    // Both destinations ticked — the state the export window is actually used in.
    await page.locator('.dest-head input[type=checkbox]').nth(0).check();
    await page.locator('.dest-head input[type=checkbox]').nth(1).check();
    await shot(page, '22-export-expanded');
    await page.keyboard.press('Escape');

    await page.getByTitle(/history/i).click();
    await shot(page, '23-version-history');
    await page.keyboard.press('Escape');

    await page.locator('.retranslate-btn').first().click();
    await shot(page, '24-translate-all');
    await page.keyboard.press('Escape');

    // The emoji panel and the paragraph-mark toggle: both shipped 27.07 and never opened.
    const emoji = page.locator('[title*="моji" i], [title*="Emoji" i]').first();
    if (await emoji.count()) {
        await emoji.click();
        await shot(page, '25-emoji-panel');
        await page.keyboard.press('Escape');
    }

    await page.locator('.status-icon-btn').first().click();
    await page.locator('.tiptap').click();
    await shot(page, '26-paragraph-marks');

    // Appearance panel (ADR-057 made it a modal; ADR-069 replaced Apply with autosave).
    const palette = page.locator('header button').filter({ has: page.locator('svg') });
    for (let i = 0; i < await palette.count(); i++) {
        const title = await palette.nth(i).getAttribute('title');
        if (title && /appearance|оформ/i.test(title)) {
            await palette.nth(i).click();
            break;
        }
    }
    await shot(page, '27-appearance');
});

test('@audit save-guard dialog', async ({ page, context }) => {
    const long = Array.from({ length: 4 }, (_, i) =>
        `Абзац ${i + 1}: достаточно текста, чтобы документ уверенно превышал порог в двести символов, ниже которого страж вообще не срабатывает.`);
    const id = await createDraft(context, 'Страж сохранения', long);
    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.save-state .save-label')).toHaveText('Saved', { timeout: 20_000 });

    await page.locator('.tiptap').click();
    await page.keyboard.press('Control+a');
    await page.keyboard.type('x');
    await expect(page.locator('app-modal', { hasText: 'Save stopped' })).toBeVisible({ timeout: 20_000 });
    await shot(page, '30-save-guard');
});

test('@audit posts manager tabs', async ({ page, context }) => {
    const id = await createDraft(context, 'Пост для менеджера', ['Тело поста.']);
    await context.request.post(`/api/drafts/${id}/publish-blog`, { data: {} });

    await page.goto('/posts');
    await shot(page, '40-posts');
    await page.locator('.post-row').first().click();
    await shot(page, '41-posts-selected');

    await page.goto('/posts?tab=forms');
    await shot(page, '42-forms-empty');

    await context.request.post('/api/form-presets', {
        data: {
            name: 'Пресет для аудита',
            formJson: JSON.stringify({
                intro: 'Расскажите о себе.',
                requireName: true, requireNickname: false, requireEmail: true, requireSocial: false,
                questions: [
                    { id: 'q1', label: 'Откуда узнали?', type: 'text', options: [], required: false },
                    { id: 'q2', label: 'Развёрнутый ответ', type: 'longtext', options: [], required: false },
                    { id: 'q3', label: 'Просто текст для чтения', type: 'static', options: [], required: false },
                    { id: 'q4', label: 'Согласие на обработку', type: 'consent', options: [], required: true },
                ],
            }),
        },
    });
    await page.goto('/posts?tab=forms');
    await page.locator('.post-row').first().click();
    await shot(page, '43-forms-editor');

    await page.goto('/posts?tab=stats');
    await shot(page, '44-stats');
});

test('@audit settings, glossary, admin', async ({ page, context }) => {
    await page.goto('/settings?tab=profile');
    await shot(page, '50-settings-profile');
    await page.getByRole('button', { name: 'Account', exact: true }).click();
    await shot(page, '51-settings-account');

    await page.goto('/glossary');
    await shot(page, '52-glossary-empty');
    await context.request.post('/api/glossary', {
        data: { term: 'Кедр', description: 'Хвойное дерево, давшее имя проекту.', aliases: 'кедра,кедру', language: 'ru' },
    });
    await page.goto('/glossary');
    await shot(page, '53-glossary-term');

    await page.goto('/admin');
    await shot(page, '60-admin-users');
    for (const [i, name] of [[1, '61-admin-invites'], [2, '62-admin-posts'], [3, '63-admin-reports']] as [number, string][]) {
        await page.locator('.admin-tabs button').nth(i).click();
        await shot(page, name);
    }
});

test('@audit blog surfaces', async ({ page, context }) => {
    const pub = await createDraft(context, 'Публичный пост', [
        'Первый абзац публичного поста, который читатель видит целиком.',
        'Второй абзац, чтобы страница не выглядела пустой.',
    ]);
    const r1 = await context.request.post(`/api/drafts/${pub}/publish-blog`, { data: {} });
    const { slug } = await r1.json();

    await page.goto(BLOG_ORIGIN);
    await shot(page, '70-blog-index');
    await page.goto(`${BLOG_ORIGIN}/${slug}`);
    await shot(page, '71-blog-post');

    // A private post with a form: the gate, including the two field types added on 30.07.
    const priv = await createDraft(context, 'Приватный пост', ['Текст, доступный только по форме.']);
    await context.request.post(`/api/drafts/${priv}/registration-form`, {
        data: {
            formJson: JSON.stringify({
                intro: 'Расскажите о себе.',
                requireName: true, requireNickname: false, requireEmail: true, requireSocial: false,
                questions: [
                    { id: 'q1', label: 'Откуда узнали?', type: 'text', options: [], required: false },
                    { id: 'q2', label: 'Развёрнутый ответ', type: 'longtext', options: [], required: false },
                    { id: 'q3', label: 'Это статический блок, который читатель только читает.', type: 'static', options: [], required: false },
                    { id: 'q4', label: 'Согласие на обработку данных', type: 'consent', options: [], required: true },
                ],
            }),
        },
    });
    await context.request.post(`/api/drafts/${priv}/private`, { data: { isPrivate: true } });
    await context.request.post(`/api/drafts/${priv}/listed`, { data: { isListedWhilePrivate: true } });
    const r2 = await context.request.post(`/api/drafts/${priv}/publish-blog`, { data: {} });
    const privSlug = (await r2.json()).slug;

    const reader = await page.context().browser()!.newContext({ viewport: { width: 1440, height: 900 } });
    const rp = await reader.newPage();
    await rp.goto(`${BLOG_ORIGIN}/${privSlug}`);
    await rp.screenshot({ path: path.join(OUT, '72-blog-gate.png'), fullPage: true });
    await rp.goto(BLOG_ORIGIN);
    await rp.screenshot({ path: path.join(OUT, '73-blog-index-semi-public.png'), fullPage: true });
    await reader.close();
});

// Marty reads the drafts list on an iPad daily, and that is where the title column collapsed to
// nothing. These are his actual devices, not round numbers.
const DEVICES = [
    { name: 'ipad-landscape', width: 1180, height: 820 },
    { name: 'ipad-portrait', width: 820, height: 1180 },
    { name: 'iphone13', width: 390, height: 844 },
];

for (const device of DEVICES) {
    test(`@audit drafts on ${device.name}`, async ({ page, context }) => {
        for (const t of ['Публичный пост о кедрах', 'Draft with a very long title that should not break the row', 'Третий']) {
            await createDraft(context, t, ['Текст поста.']);
        }
        await page.setViewportSize({ width: device.width, height: device.height });
        await page.goto('/drafts');
        await expect(page.locator('.drafts-title').first()).toBeVisible();
        await shot(page, `90-drafts-${device.name}`);

        await page.goto('/posts');
        await shot(page, `91-posts-${device.name}`);
        await page.goto('/settings');
        await shot(page, `92-settings-${device.name}`);

        // The editor's topbar is the crowded one: title field, save state, Export and four nav
        // buttons in one row. This is where the labels wrapped onto two lines and the account
        // email ran off the right edge.
        const id = await createDraft(context, 'Заголовок поста для проверки топбара', ['Текст.']);
        await page.goto(`/editor?draft=${id}`);
        await expect(page.locator('.tiptap')).toBeVisible();
        await shot(page, `93-editor-${device.name}`);
    });
}

// T-078 — the styleguide in all four combinations it exists in. This is the one surface where the
// screenshots are the deliverable rather than evidence: it is how a token change gets judged
// without walking the app.
test('@audit styleguide, both themes and both densities', async ({ page }) => {
    await page.goto('/dev/styleguide');
    await expect(page.locator('h1')).toContainText('Design System');
    await shot(page, '70-styleguide-light-comfortable');

    const density = page.getByRole('button', { name: /Density:/ });
    await density.click();
    await shot(page, '71-styleguide-light-compact');

    // The theme button toggles the app-wide ThemeService, so this leaves the suite in dark — the
    // last two shots are taken in that order deliberately rather than toggling back and forth.
    await page.getByRole('button', { name: /Theme:/ }).click();
    await shot(page, '72-styleguide-dark-compact');
    await density.click();
    await shot(page, '73-styleguide-dark-comfortable');
});

test('@audit dark theme spot check', async ({ page, context }) => {
    await createDraft(context, 'Тёмная тема', ['Текст.']);
    await page.goto('/drafts');
    await page.locator('.theme-toggle').first().click();
    await shot(page, '80-dark-drafts');
    await page.goto('/posts');
    await shot(page, '81-dark-posts');
    await page.goto('/admin');
    await shot(page, '82-dark-admin');
});
