import { test, expect, Page } from '@playwright/test';
import fs from 'fs';
import path from 'path';
import { ADMIN, BLOG_ORIGIN, createDraft, expectSynced, paragraphs, pinEnglish, signIn } from './helpers';

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

test.beforeAll(() => {
    fs.mkdirSync(OUT, { recursive: true });
    for (const file of fs.readdirSync(OUT)) {
        if (/^\d{2}-.*\.png$/.test(file)) fs.unlinkSync(path.join(OUT, file));
    }
});

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

    const views = page.getByRole('tablist', { name: 'List view' });
    await views.getByRole('tab', { name: 'Grid' }).click();
    await shot(page, '12-drafts-grid');

    await views.getByRole('tab', { name: 'Table' }).click();
    // No shot of a folder menu: ADR-164 took the filter out of the popover and stood it on the
    // shelf beside the list, where every drafts capture above already shows it.

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

    await page.getByRole('tab', { name: 'Publish / Export' }).click();
    await shot(page, '21-publish-untouched');

    // Both destinations ticked — the state the Publish tab is actually used in.
    await page.locator('.dest-card input[type=checkbox]').nth(0).check();
    await page.locator('.dest-card input[type=checkbox]').nth(1).check();
    await shot(page, '22-publish-ticked');
    await page.getByRole('tab', { name: 'Write' }).click();

    await page.getByRole('button', { name: 'History', exact: true }).click();
    await shot(page, '23-version-history');
    await page.keyboard.press('Escape');

    await page.getByRole('button', { name: 'Details', exact: true }).click();
    await shot(page, '24-editor-details');

    const emoji = page.locator('[title*="моji" i], [title*="Emoji" i]').first();
    if (await emoji.count()) {
        await emoji.click();
        await shot(page, '25-emoji-panel');
        await page.keyboard.press('Escape');
    }

    await page.goto('/settings?tab=preferences');
    await expect(page.locator('#sec-appearance')).toBeVisible();
    await shot(page, '27-appearance');

    const marks = page.getByRole('checkbox', { name: 'Show paragraph marks' });
    const marksSaved = page.waitForResponse(r => r.url().includes('/api/auth/appearance') && r.ok());
    await marks.check();
    await marksSaved;
    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.sheet.show-invisibles')).toBeVisible();
    await shot(page, '26-paragraph-marks');

    await page.goto('/settings?tab=preferences');
    const marksCleared = page.waitForResponse(r => r.url().includes('/api/auth/appearance') && r.ok());
    await page.getByRole('checkbox', { name: 'Show paragraph marks' }).uncheck();
    await marksCleared;
});

test('@audit save-guard dialog', async ({ page, context }) => {
    const long = Array.from({ length: 4 }, (_, i) =>
        `Абзац ${i + 1}: достаточно текста, чтобы документ уверенно превышал порог в двести символов, ниже которого страж вообще не срабатывает.`);
    const id = await createDraft(context, 'Страж сохранения', long);
    await page.goto(`/editor?draft=${id}`);
    await expectSynced(page, 20_000);

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
    await page.locator('.post-card').first().click();
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
    await page.locator('.post-card').first().click();
    await shot(page, '43-forms-editor');

    await page.goto('/posts?tab=stats');
    await shot(page, '44-stats');
});

test('@audit feedback pass at ultrawide width', async ({ page, context }) => {
    await page.setViewportSize({ width: 3440, height: 1392 });
    const titles = [
        'Coyote vs ACME — production notes and the long road to launch',
        'Станция Кедр: документ дизайна интерфейса',
        'Multicultural Update',
        'Screenshot Saturday — lighting pass',
        'My plans for the next release',
        'Dedicated to Sasha',
        ...Array.from({ length: 14 }, (_, i) => `Production archive ${String(i + 1).padStart(2, '0')} — list density check`),
    ];
    const ids: string[] = [];
    for (const title of titles) ids.push(await createDraft(context, title, ['Текст поста для визуальной проверки списка.']));
    for (const id of ids.slice(0, 4)) {
        const published = await context.request.post(`/api/drafts/${id}/publish-blog`, { data: {} });
        expect(published.ok(), `publish failed for ${id}: ${published.status()}`).toBeTruthy();
    }
    const madePrivate = await context.request.post(`/api/drafts/${ids[2]}/private`, { data: { isPrivate: true } });
    expect(madePrivate.ok(), `private update failed for ${ids[2]}: ${madePrivate.status()}`).toBeTruthy();

    await page.goto('/posts');
    await expect(page.locator('.post-card').first()).toBeVisible();
    await shot(page, '45-posts-ultrawide');

    await page.goto('/drafts');
    await page.getByTitle('Account', { exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Account' })).toBeVisible();
    await shot(page, '46-account-menu-ultrawide');

    await page.keyboard.press('Escape');
    await page.locator('app-project-switcher .side-project').click();
    const projectSwitcher = page.getByRole('group', { name: 'Switch project' });
    await expect(projectSwitcher.getByRole('link', { name: 'All projects', exact: true })).toBeVisible();
    await expect(projectSwitcher.getByRole('link', { name: 'Manage teams', exact: true })).toBeVisible();
    await shot(page, '47-sidebar-switcher-ultrawide');

    await page.goto('/settings?tab=preferences');
    await expect(page.locator('#sec-appearance')).toBeVisible();
    const fullSaved = page.waitForResponse(r => r.url().includes('/api/auth/appearance') && r.ok());
    await page.getByRole('button', { name: 'Full', exact: true }).click();
    await fullSaved;
    await shot(page, '48-preferences-ultrawide');

    await page.goto(`/editor?draft=${ids[0]}`);
    await expect(page.locator('.tiptap')).toBeVisible();
    await shot(page, '49-editor-full-ultrawide');

    await page.goto('/settings?tab=preferences');
    const normalSaved = page.waitForResponse(r => r.url().includes('/api/auth/appearance') && r.ok());
    await page.getByRole('button', { name: 'Normal', exact: true }).click();
    await normalSaved;
});

test('@audit settings, glossary, admin', async ({ page, context }) => {
    await page.goto('/settings?tab=profile');
    await shot(page, '50-settings-profile');
    await page.getByRole('tab', { name: 'Preferences', exact: true }).click();
    await shot(page, '51-settings-preferences');

    await page.goto('/glossary');
    await shot(page, '52-glossary-empty');
    await context.request.post('/api/glossary', {
        data: { term: 'Кедр', description: 'Хвойное дерево, давшее имя проекту.', aliases: 'кедра,кедру', language: 'ru' },
    });
    await page.goto('/glossary');
    await shot(page, '53-glossary-term');

    await page.goto('/admin');
    await shot(page, '60-admin-users');
    const sections = page.getByRole('tablist', { name: 'Admin sections' });
    for (const [tab, name] of [['Invite codes', '61-admin-invites'], ['Posts', '62-admin-posts'], ['Reports', '63-admin-reports']] as [string, string][]) {
        await sections.getByRole('tab', { name: tab }).click();
        await shot(page, name);
    }
});

test('@audit blog surfaces', async ({ page, context }) => {
    await page.goto('/settings?tab=profile');
    await page.locator('#cc-social-github').fill('https://github.com/cedar-clerk');
    await page.locator('#cc-social-itch').fill('https://cedar-clerk.itch.io');
    const profileSaved = page.waitForResponse(r => r.url().includes('/api/auth/profile') && r.ok());
    await page.getByRole('button', { name: 'Save profile', exact: true }).first().click();
    await profileSaved;

    const pub = await createDraft(context, 'Публичный пост', [
        'Первый абзац публичного поста, который читатель видит целиком.',
        'Второй абзац, чтобы страница не выглядела пустой.',
    ]);
    const r1 = await context.request.post(`/api/drafts/${pub}/publish-blog`, { data: {} });
    const { slug } = await r1.json();

    await page.goto(BLOG_ORIGIN);
    await shot(page, '70-blog-index');
    await page.goto(`${BLOG_ORIGIN}/${slug}`);
    await page.locator('.author-links-btn').hover();
    await expect(page.locator('.author-links-menu')).toBeVisible();
    await expect(page.locator('.author-links-menu')).toContainText('GitHub');
    await expect(page.locator('.author-links-menu')).toContainText('itch.io');
    await shot(page, '71-blog-post-author-links');

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
        await page.goto('/settings?tab=preferences');
        await expect(page.locator('#sec-appearance')).toBeVisible();
        await shot(page, `92-settings-preferences-${device.name}`);

        const id = await createDraft(context, 'Заголовок поста для проверки топбара', ['Текст.']);
        await page.goto(`/editor?draft=${id}`);
        await expect(page.locator('app-sidebar')).toBeVisible();
        await shot(page, `93-editor-${device.name}`);
        await expect(page.locator('.tiptap')).toBeVisible();
        if (device.width <= 640) await expect(page.locator('app-sidebar')).toHaveClass(/is-rail/);
        for (const label of await page.locator('.frame-tab-label').all()) {
            expect(await label.evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
        }
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

// T-080 — the icon inventory. Captured for the same reason as the styleguide: the two "one meaning,
// two glyphs" tables are read, not asserted, and a screenshot is how they get read after a sweep.
test('@audit icon inventory', async ({ page }) => {
    await page.goto('/dev/icons');
    await expect(page.locator('h1')).toContainText('Icons');
    await shot(page, '74-icons-inventory');
});

// T-051 — the long-word pass. Every screen that carries a row of controls, rendered with strings
// ~30% longer and a German compound welded onto the longest word of each. What these shots are
// read for: a control that grew past its container, a tab strip that clipped, an ellipsis that
// swallowed the whole label.
test('@audit pseudo-locale long words', async ({ page, context }) => {
    await createDraft(context, 'Langwort', ['Текст.']);
    await page.goto('/drafts?pseudo=1');
    await shot(page, '75-pseudo-drafts');
    await page.goto('/posts');
    await shot(page, '76-pseudo-posts');
    await page.goto('/settings');
    await shot(page, '77-pseudo-settings');
    await page.goto('/editor');
    await page.waitForTimeout(800);
    await shot(page, '78-pseudo-editor');
    // Leave the flag off: it is stored per browser and would otherwise follow the next test.
    await page.goto('/drafts?pseudo=0');
});

test('@audit thread offer', async ({ page, context }) => {
    const long = Array.from({ length: 8 }, (_, i) => `Часть ${i + 1}. ` + 'я'.repeat(5000));
    const id = await createDraft(context, 'Длинный документ', long);
    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();
    await page.getByRole('tab', { name: 'Publish / Export' }).click();
    await page.locator('.dest-card input[type=checkbox]').nth(1).check();
    // The checkbox only ticks the destination — the settings button brings its panel forward.
    await page.locator('.dest-card .dest-select').nth(1).click();
    await page.locator('.thread-toggle input').check();
    await expect(page.locator('.thread-parts li').first()).toBeVisible();
    await shot(page, '79-thread-offer');
});

// T-009 — the screenshot the landing shows. Seeded with text worth reading rather than the smoke
// suite's filler: this one is looked at by strangers, and "Первый абзац" tells them nothing.
// The blog page is chosen over the editor on purpose — it carries no account email in its chrome.
test('@audit landing screenshot source', async ({ page, context }) => {
    const id = await createDraft(context, 'Станция Кедр — девлог', [
        'Первую неделю я потратил на то, чтобы станция не выглядела как коробка. Оказалось, что дело не в текстурах, а в том, куда падает свет: одна лампа под потолком превращает отсек в интерьер, а четыре — в чертёж.',
        'Дальше был звук. Гул вентиляции пишется одним слоем, но слышно его только тогда, когда он иногда замолкает — тишина в этой игре работает лучше любого эмбиента.',
        'На следующей неделе — двери. Они кажутся мелочью ровно до момента, когда через них проходит игрок.',
    ]);
    await context.request.post(`/api/drafts/${id}/publish-blog`, { data: {} });
    const meta = await (await context.request.get(`/api/drafts/${id}`)).json();

    const shots = await page.context().browser()!.newContext({ viewport: { width: 1120, height: 760 } });
    const p2 = await shots.newPage();
    await p2.goto(`${BLOG_ORIGIN}/${meta.blogSlug}`);
    await p2.waitForTimeout(400);
    await p2.screenshot({ path: path.join(OUT, '91-landing-blog.png') });
    await shots.close();
});

// T-009 — the page a stranger sees. Captured in both themes because it follows the visitor's
// system setting rather than the app's toggle: nobody is signed in to have a preference stored.
test('@audit landing', async ({ browser }) => {
    for (const scheme of ['light', 'dark'] as const) {
        const ctx = await browser.newContext({ colorScheme: scheme, viewport: { width: 1440, height: 900 } });
        const p = await ctx.newPage();
        await p.goto('http://localhost:8080/');
        await shot(p, `90-landing-${scheme}`);
        await ctx.close();
    }
});

test('@audit dark theme spot check', async ({ page, context }) => {
    await createDraft(context, 'Тёмная тема', ['Текст.']);
    await page.goto('/settings?tab=preferences');
    const before = await page.locator('html').getAttribute('data-theme');
    try {
        if (before !== 'dark') {
            const saved = page.waitForResponse(r => r.url().includes('/api/auth/appearance') && r.ok());
            await page.getByRole('button', { name: 'Dark', exact: true }).click();
            await saved;
        }
        await page.goto('/drafts');
        await shot(page, '80-dark-drafts');
        await page.goto('/posts');
        await shot(page, '81-dark-posts');
        await page.goto('/admin');
        await shot(page, '82-dark-admin');
    } finally {
        if (before && before !== 'dark') {
            await page.goto('/settings?tab=preferences');
            const restored = page.waitForResponse(r => r.url().includes('/api/auth/appearance') && r.ok());
            await page.getByRole('button', { name: before === 'dark' ? 'Dark' : 'Light', exact: true }).click();
            await restored;
        }
    }
});
