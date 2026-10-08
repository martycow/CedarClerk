import { Page, BrowserContext, expect } from '@playwright/test';

// The account `Scripts/e2e.ps1` seeds and promotes to admin. Nothing here invents credentials —
// the launcher and this file must agree, so both read like the same fact.
export const ADMIN = { email: 'e2e-admin@local.test', password: 'E2e-passw0rd!' };
export const INVITE_CODE = 'e2e-invite';

// The two knobs an isolated stack turns: `E2E_BASE_URL` is the dev server the tests drive (the
// same value playwright.config.ts reads), `E2E_API_PORT` the Kestrel port behind it. Both are
// exported by `Scripts/e2e.ps1`; unset, they name the ordinary 4200/8080 pair. The blog host is
// the base URL's hostname under the seeded username, because `Cedar__TenantHost` is that hostname.
const BASE_URL = new URL(process.env['E2E_BASE_URL'] ?? 'http://localhost:4200');
export const API_PORT = process.env['E2E_API_PORT'] ?? '8080';
// Server-rendered pages (the landing, the blog) live on Kestrel's own port, not the dev server's.
export const MAIN_ORIGIN = `${BASE_URL.protocol}//${BASE_URL.hostname}:${API_PORT}`;
// Blogs are tenant-hosted since the multitenancy work: the seeded admin's lives on a subdomain
// named by its username, not on a shared blog host.
export const BLOG_HOST = `e2e-admin.${BASE_URL.hostname}`;
export const BLOG_ORIGIN = `${BASE_URL.protocol}//${BLOG_HOST}:${API_PORT}`;

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
    // The username becomes the blog's address: latin letters, digits and inner hyphens, at most
    // 16 (ADR-325). Test emails carry a timestamp and run longer, so the name is a hash of the email.
    let hash = 0;
    for (const ch of email) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
    const username = `e2e${hash.toString(36)}${email.length.toString(36)}`.slice(0, 16);
    const res = await context.request.post('/api/auth/register', {
        data: { email, password, inviteCode: INVITE_CODE, username },
    });
    expect(res.ok(), `register failed for ${email}: ${res.status()}`).toBeTruthy();
    // T-328 — a display name is what marks the account as onboarded; without it every guarded
    // route bounces to /onboarding and no text-based selector below finds its screen.
    const profile = await context.request.post('/api/auth/profile', {
        data: {
            authorDisplayName: 'Smoke Tester', profileUrl: '', profileLocation: '',
            headerSlot1Type: null, headerSlot2Type: null, headerSlot3Type: null,
        },
    });
    expect(profile.ok(), `onboarding profile save failed for ${email}: ${profile.status()}`).toBeTruthy();
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

// `.tiptap` is the one class ADR-171 clause 2 allows here: the writing surface has no role and no
// name, and the class is TipTap's own on the editable element rather than anything our stylesheets
// put there.
export async function openDraft(page: Page, id: string) {
    await page.goto(`/editor?draft=${id}`);
    await expect(page.locator('.tiptap')).toBeVisible();
}

export async function typeInSheet(page: Page, text: string) {
    const sheet = page.locator('.tiptap');
    await sheet.click();
    await page.keyboard.type(text);
}

// The autosave debounce is 1.2s, and asserting the indicator alone is a race: right after a
// keystroke the header still reads "Synced" from the previous save, so the assertion can pass
// before this edit has been written at all. Waiting for the PUT itself is the only version of
// this that cannot pass early.
export async function withSave(page: Page, action: () => Promise<void>) {
    // 30s, not the debounce's 1.2s plus a little: the window has to survive a machine busy with an
    // Angular rebuild or a second browser, and a suite that fails on load rather than on breakage
    // is a suite people stop reading.
    const written = page.waitForResponse(
        r => /\/api\/drafts\//.test(r.url()) && r.request().method() === 'PUT' && r.status() === 200,
        { timeout: 30_000 },
    );
    await action();
    await written;
    await expectSynced(page);
}

// ADR-249 gives sync state one home in the compact document header. "Syncing…" and "Sync failed"
// are the other two states, and neither contains this word.
export const SYNC_WORD_SELECTOR = 'app-document-frame .frame-save';

export async function expectSynced(page: Page, timeout = 10_000) {
    await expect(page.locator(SYNC_WORD_SELECTOR)).toContainText('Synced', { timeout });
}

// Node's DNS does not resolve *.localhost — only Chromium special-cases it — so anything going
// through `context.request` has to reach the blog branch by address and name the host in a header.
// Page navigations are unaffected and keep using BLOG_ORIGIN.
export const BLOG_API = `http://127.0.0.1:${API_PORT}`;
export const BLOG_HEADERS = { Host: BLOG_HOST };
