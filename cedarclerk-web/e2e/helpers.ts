import { Page, BrowserContext, expect } from '@playwright/test';

// The account `Scripts/e2e.ps1` seeds and promotes to admin. Nothing here invents credentials —
// the launcher and this file must agree, so both read like the same fact.
export const ADMIN = { email: 'e2e-admin@local.test', password: 'E2e-passw0rd!' };
export const INVITE_CODE = 'e2e-invite';
export const BLOG_ORIGIN = 'http://blog.localhost:8080';

// Sign in through the API rather than the form, because the form is what several tests are *for*
// — driving it as a setup step in the other twelve would make an unrelated failure look like a
// failure of whatever the test was actually about. `context.request` shares the browser's cookie
// jar, so the page is signed in afterwards exactly as if it had been typed.
export async function signIn(context: BrowserContext, who = ADMIN) {
    const res = await context.request.post('/api/auth/login', { data: who });
    expect(res.ok(), `login failed for ${who.email}: ${res.status()}`).toBeTruthy();
}

// Pin the UI language before any script runs. Every text-based selector below reads the English
// dictionary, and the default follows navigator.language — which is a property of the machine
// running the tests, not of the app.
export async function pinEnglish(context: BrowserContext) {
    await context.addInitScript(() => localStorage.setItem('cedar-ui-lang', 'en'));
}

export async function registerAccount(context: BrowserContext, email: string, password = 'E2e-passw0rd!') {
    const res = await context.request.post('/api/auth/register', {
        data: { email, password, inviteCode: INVITE_CODE },
    });
    expect(res.ok(), `register failed for ${email}: ${res.status()}`).toBeTruthy();
}

// A draft created through the API, for tests whose subject is not the creation dialog.
export async function createDraft(context: BrowserContext, title: string, lines: string[] = ['Hello from the smoke suite.']): Promise<string> {
    const res = await context.request.post('/api/drafts', {
        data: { title, cedarJson: JSON.stringify(paragraphs(lines)) },
    });
    expect(res.ok(), `create draft failed: ${res.status()}`).toBeTruthy();
    const body = await res.json();
    return body.id;
}

// TipTap JSON for a document of N paragraphs — the shape the editor itself saves.
export function paragraphs(lines: string[]) {
    return {
        type: 'doc',
        content: lines.map(text => ({ type: 'paragraph', content: [{ type: 'text', text }] })),
    };
}

export async function openDraft(page: Page, id: string) {
    await page.goto(`/editor?id=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();
}

export async function typeInSheet(page: Page, text: string) {
    const sheet = page.locator('.tiptap');
    await sheet.click();
    await page.keyboard.type(text);
}

// The autosave debounce is 1.2s, and asserting the indicator alone is a race: right after a
// keystroke the status bar still reads "Saved" from the previous save, so the assertion can pass
// before this edit has been written at all. Waiting for the PUT itself is the only version of
// this that cannot pass early.
export async function withSave(page: Page, action: () => Promise<void>) {
    const written = page.waitForResponse(
        r => /\/api\/drafts\//.test(r.url()) && r.request().method() === 'PUT' && r.status() === 200,
        { timeout: 20_000 },
    );
    await action();
    await written;
    await expect(page.locator('.save-state .save-label')).toHaveText('Saved', { timeout: 10_000 });
}

// Node's DNS does not resolve *.localhost — only Chromium special-cases it — so anything going
// through `context.request` has to reach the blog branch by address and name the host in a header.
// Page navigations are unaffected and keep using BLOG_ORIGIN.
export const BLOG_API = 'http://127.0.0.1:8080';
export const BLOG_HEADERS = { Host: 'blog.localhost' };
