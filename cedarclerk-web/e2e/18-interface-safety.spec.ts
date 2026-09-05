import { test, expect } from '@playwright/test';
import { createDraft, openDraft, pinEnglish, registerAccount, signIn, typeInSheet, withSave } from './helpers';

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

test('task deletion waits for confirmation and Escape preserves the editing dialog', async ({ context, page }) => {
    const projectResponse = await context.request.post('/api/projects', {
        data: { name: 'Interface safety', projectType: 'empty', language: 'en' },
    });
    expect(projectResponse.ok()).toBeTruthy();
    const project = await projectResponse.json();
    const taskResponse = await context.request.post(`/api/projects/${project.id}/tasks`, {
        data: { title: 'Only delete after confirmation' },
    });
    expect(taskResponse.ok()).toBeTruthy();
    const task = await taskResponse.json();
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/projects/${project.id}/tasks?task=${task.id}`);
    const editing = page.getByRole('dialog', { name: /Edit task/ });
    await editing.getByRole('button', { name: 'Delete', exact: true }).click();
    const confirmation = page.getByRole('dialog', { name: 'Confirm action', exact: true });
    await expect(confirmation.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
    expect((await context.request.get(`/api/tasks/${task.id}`)).ok()).toBeTruthy();
    await page.keyboard.press('Escape');
    await expect(confirmation).not.toBeVisible();
    await expect(editing).toBeVisible();
    await expect(editing.getByRole('button', { name: 'Delete', exact: true })).toBeFocused();
    expect((await context.request.get(`/api/tasks/${task.id}`)).ok()).toBeTruthy();
    await editing.getByRole('button', { name: 'Delete', exact: true }).click();
    const deleted = page.waitForResponse(response => response.request().method() === 'DELETE' && response.url().endsWith(`/api/tasks/${task.id}`));
    await confirmation.getByRole('button', { name: 'Delete', exact: true }).click();
    expect((await deleted).ok()).toBeTruthy();
    await expect(editing).not.toBeVisible();
    expect((await context.request.get(`/api/tasks/${task.id}`)).status()).toBe(404);
});

test('the mobile editor keeps the writing sheet in view while its outline follows the caret', async ({ context, page }) => {
    const id = await createDraft(context, 'Mobile writing stays visible');
    await page.setViewportSize({ width: 390, height: 844 });
    await openDraft(page, id);
    await page.getByRole('button', { name: 'Details', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Inspector', exact: true })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Document title', exact: true })).toBeInViewport();
    await page.locator('.tiptap').click();
    await page.keyboard.type(' Still writing.');
    await expect(page.getByRole('textbox', { name: 'Document title', exact: true })).toBeInViewport();
    await expect(page.getByRole('main')).not.toContainText('[disabled]=');
    await expect(page.getByRole('tab', { name: 'Write', exact: true })).toHaveAttribute('aria-selected', 'true');
});

test('a new account can save its first editor document without a false conflict', async ({ context, page }) => {
    await registerAccount(context, `first-writing-${Date.now()}@local.test`);
    await page.goto('/editor');
    await expect(page.locator('.tiptap')).toBeVisible();
    await withSave(page, () => typeInSheet(page, 'My first saved paragraph.'));
    await expect(page.getByRole('dialog')).not.toBeVisible();
    await page.reload();
    await expect(page.locator('.tiptap')).toContainText('My first saved paragraph.');
});
