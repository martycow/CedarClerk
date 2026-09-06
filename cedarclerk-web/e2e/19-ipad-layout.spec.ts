import { test, expect, Page } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { registerAccount } from './helpers';

const sizes = [
    { width: 1024, height: 768 },
    { width: 1180, height: 820 },
    { width: 1366, height: 1024 },
    { width: 820, height: 1180 },
];

async function contained(page: Page, selector: string) {
    for (const element of await page.locator(selector).all()) {
        await expect(element).toBeVisible();
        const overflow = await element.evaluate(node => node.scrollWidth - node.clientWidth);
        expect.soft(overflow, `Horizontal overflow in ${selector}`).toBeLessThanOrEqual(2);
    }
}

for (const viewport of sizes) {
    for (const language of ['en', 'ru']) {
        test.describe(`iPad ${viewport.width}x${viewport.height} ${language}`, () => {
            test.use({ viewport, hasTouch: true, reducedMotion: 'reduce' });

            test('project pages keep content and controls inside their work area', async ({ context, page }) => {
                await context.addInitScript(lang => {
                    localStorage.setItem('cedar-ui-lang', lang);
                    localStorage.setItem('cedar-theme', 'dark');
                }, language);
                await registerAccount(context, `tablet-${viewport.width}-${language}-${Date.now()}@local.test`);
                const locale = await context.request.post('/api/auth/ui-language', { data: { uiLanguage: language } });
                expect(locale.ok()).toBeTruthy();
                const response = await context.request.post('/api/projects', {
                    data: { name: 'Tablet layout', projectType: 'blog', language },
                });
                expect(response.ok()).toBeTruthy();
                const project = await response.json();
                const document = await context.request.post(`/api/projects/${project.id}/documents`, {
                    data: { documentType: 'changelog', title: language === 'ru'
                        ? 'Обновление проекта: новый уровень, исправления и планы на следующую неделю'
                        : 'Project update: a new level, fixes and plans for the next week' },
                });
                expect(document.ok()).toBeTruthy();
                const directory = path.resolve('../.e2e-audit/ipad-layout');
                await mkdir(directory, { recursive: true });
                const shot = (surface: string) => page.screenshot({ animations: 'disabled', path: path.join(directory, `${viewport.width}-${language}-${surface}.png`) });

                await page.goto(`/projects/${project.id}`);
                await expect(page.locator('.resume-t')).toContainText(language === 'ru' ? 'Обновление проекта' : 'Project update');
                await expect(page.getByRole('link', { name: language === 'ru' ? 'Задачи' : 'Tasks', exact: true })).toBeVisible();
                await contained(page, '.hub-grid, .resume, .docs-head, .hub-side .kv, .journal-list');
                const title = await page.locator('.resume-t').boundingBox();
                expect(title?.width).toBeGreaterThan(160);
                await shot('hub');

                await page.goto(`/projects/${project.id}/showcase`);
                await expect(page.locator('.block-row').first()).toBeVisible();
                await shot('showcase');
                await page.locator('.settings-card').last().locator('summary').click();
                await contained(page, '.builder, .block-rail, .inspector, .settings-card, .settings-body');
                for (const settings of await page.locator('.settings-card').all()) {
                    expect(await settings.evaluate(node => node.scrollHeight - node.clientHeight), 'Settings fields are not clipped vertically').toBeLessThanOrEqual(2);
                }
                await page.locator('.block-row').nth(1).click();
                await expect(page.locator('.block-row').nth(1)).toHaveClass(/is-selected/);
                await page.locator('.settings-card').last().scrollIntoViewIfNeeded();
                await shot('showcase-settings');

                await page.goto(`/projects/${project.id}/tasks`);
                await expect(page.locator('.column')).toHaveCount(4);
                await contained(page, '.toolbar, .column-head, .column-cards');
                if (viewport.width > viewport.height) await contained(page, '.board');
                const column = await page.locator('.column').first().boundingBox();
                expect(column?.height).toBeLessThan(400);
                await shot('tasks-empty');
                const task = await context.request.post(`/api/projects/${project.id}/tasks`, {
                    data: { title: language === 'ru' ? 'Проверить длинное название задачи в горизонтальном режиме' : 'Check a long task title in landscape orientation' },
                });
                expect(task.ok()).toBeTruthy();
                await page.reload();
                await expect(page.locator('.task-card')).toHaveCount(1);
                await shot('tasks-populated');
                await page.locator('.task-card').click();
                await expect(page.getByRole('dialog')).toBeVisible();
                await contained(page, '.modal-card');
                const overlay = await page.locator('.modal-overlay').boundingBox();
                expect(overlay?.x).toBe(0);
                expect(overlay?.width).toBe(viewport.width);
                await shot('task-dialog');
            });
        });
    }
}
