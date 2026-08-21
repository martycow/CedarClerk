import { test, expect } from '@playwright/test';
import { pinEnglish, signIn } from './helpers';

// ADR-138 item 5. The touch floor a control is owed is the one its NEAREST declared surface sets,
// and the surfaces nest both ways round — a shelf panel stands on the worktop sheet, a ruler bar
// stands on the paper it measures. That is a media-query cascade over an inherited custom property,
// and the unit runner can measure neither half: its jsdom has no matchMedia at all, so
// @media (pointer: coarse) never applies, and getComputedStyle hands a custom property back only on
// the element that declared it, never on a descendant. So it is measured here, in an engine that
// resolves both — which is also where the defect was found.
//
// The nestings are built in the page rather than hunted for on a screen: the invariant belongs to
// styles.scss, not to whichever component happens to nest today. The last assertion holds the
// fixture to the shipped DOM so the two cannot drift apart.
test.use({ hasTouch: true });

const CHROME_HIT = '30px', PAPER_HIT = '44px';

// Surfaces from the outside in, and the floor owed at the bottom.
const NESTINGS: [string, string[], string][] = [
    ['page', ['paper'], PAPER_HIT],
    ['rail', ['chrome'], CHROME_HIT],
    ['sheet', ['chrome', 'paper'], PAPER_HIT],
    ['top', ['chrome', 'paper'], PAPER_HIT],
    ['ruler', ['paper', 'chrome'], CHROME_HIT],
    ['drawer', ['chrome', 'paper'], PAPER_HIT],
    // A shelf panel on the worktop sheet. Three deep is what rules out reading "nearest" as
    // "not under the opposite surface": that leaves this one matched by neither side and standing
    // at no floor at all.
    ['deep', ['chrome', 'paper', 'chrome'], CHROME_HIT],
];

test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

test('a touch control takes the floor of its nearest declared surface, either way round', async ({ page }) => {
    await page.goto('/drafts');
    await expect(page.locator('app-rail-header')).toBeVisible();

    const coarse = await page.evaluate(() => matchMedia('(pointer: coarse)').matches);
    expect(coarse, 'not a coarse pointer — nothing below would be measuring the touch floor').toBe(true);

    const measured = await page.evaluate((nestings: [string, string[]][]) => {
        const probe = document.createElement('div');
        // Off-screen rather than hidden: display:none has no layout and every height reads 0.
        probe.style.cssText = 'position:fixed;left:-9999px;top:0';
        for (const [id, surfaces] of nestings) {
            let node: HTMLElement = probe;
            for (const surface of surfaces) {
                const wrap = document.createElement('div');
                wrap.setAttribute('data-surface', surface);
                node.appendChild(wrap);
                node = wrap;
            }
            const button = document.createElement('button');
            button.id = `hit-${id}`;
            button.textContent = 'x';
            node.appendChild(button);
        }
        document.body.appendChild(probe);
        const out: Record<string, string> = {};
        for (const [id] of nestings) {
            const el = document.getElementById(`hit-${id}`)!;
            // The resolved property, not the box: a control taller than its floor by its own
            // content would read as passing while the floor under it was the wrong one.
            out[id] = getComputedStyle(el).minHeight;
        }
        probe.remove();
        return out;
    }, NESTINGS.map(([id, surfaces]) => [id, surfaces]) as [string, string[]][]);

    expect(measured).toEqual(Object.fromEntries(NESTINGS.map(([id, , hit]) => [id, hit])));
});

test('the shipped shell resolves the same two floors the fixture assumes', async ({ page }) => {
    await page.goto('/drafts');
    await expect(page.locator('app-rail-header')).toBeVisible();
    // The rail is chrome and paints while the page below it is still on `loading()`, which has no
    // control in it at all — so the paper half of this measurement read "no button on the sheet"
    // once in five runs. Waiting for a control that is actually on the sheet is what removes the
    // race; it cannot hide one going missing, because then this line is what goes red.
    await expect(page.getByRole('button', { name: 'New draft' })).toBeVisible();

    const floors = await page.evaluate(() => {
        const railed = document.querySelector('app-rail-header[data-surface="chrome"] button');
        const papered = document.querySelector('main[data-surface="paper"] button');
        return {
            rail: railed ? getComputedStyle(railed).minHeight : 'no button in the rail',
            body: papered ? getComputedStyle(papered).minHeight : 'no button on the sheet',
        };
    });

    expect(floors).toEqual({ rail: '30px', body: '44px' });
});
