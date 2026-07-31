import { test, expect } from '@playwright/test';
import { execFileSync } from 'child_process';
import { createDraft, pinEnglish, signIn } from './helpers';

// T-082 / ADR-074. Both halves of the accessibility work are invariants, not one-off sweeps, so
// they are asserted here rather than written down somewhere and re-checked by memory — the same
// argument SchemaDriftGuardTests makes on the server side.

test('every colour token pair clears its contrast threshold', () => {
    // The checker owns the pair list and the thresholds; this test owns "it must pass". It exits
    // non-zero on a failure and execFileSync turns that into a thrown error carrying its stdout.
    const out = execFileSync('node', ['tools/check-contrast.mjs'], { encoding: 'utf8' });
    expect(out).toContain('0 failing pair(s)');
});

test.describe('accessible names', () => {
    test.beforeEach(async ({ context }) => {
        await pinEnglish(context);
        await signIn(context);
    });

    // An icon-only control with no name is a button that a screen reader announces as "button".
    // The sweep that fixed 58 of them (T-080) holds only if the next one is caught.
    for (const path of ['/drafts', '/posts', '/settings', '/editor']) {
        test(`no unnamed icon-only control on ${path}`, async ({ page, context }) => {
            if (path === '/editor') await createDraft(context, 'A11y', ['Текст.']);
            await page.goto(path);
            await page.waitForTimeout(600);

            const unnamed = await page.evaluate(() => {
                const bad: string[] = [];
                for (const el of Array.from(document.querySelectorAll('button, a'))) {
                    if (!el.querySelector('svg')) continue;
                    if ((el.textContent ?? '').trim().length > 0) continue;
                    const named = el.getAttribute('aria-label') || el.getAttribute('title')
                        || el.getAttribute('aria-labelledby');
                    if (!named) bad.push(el.outerHTML.slice(0, 120));
                }
                return bad;
            });

            expect(unnamed, `unnamed icon-only controls on ${path}`).toEqual([]);
        });
    }
});
