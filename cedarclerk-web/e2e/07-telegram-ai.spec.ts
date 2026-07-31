import { test, expect } from '@playwright/test';
import { createDraft, pinEnglish, signIn } from './helpers';

// Marty's call (30.07.2026): the suite never publishes to Telegram for real. It runs with no bot
// token, so what is asserted is that the no-token path answers honestly instead of throwing —
// a real send to @testingandfun stays a manual live-verify item. Running with a token would start
// a second long-polling loop on the Pi's token and 409 it (.claude/rules/telegram-bot.md).
test.beforeEach(async ({ context }) => {
    await pinEnglish(context);
    await signIn(context);
});

// Ownership is checked before the bot is (`SubscriptionPlan.ResolveOwnedChannelAsync`), so an
// unconnected channel is refused with 403 and the no-token 503 is never reached from here. That
// ordering is correct — an account must not learn anything about a channel it doesn't own — but it
// does mean the 503 path itself cannot be smoke-tested without a working bot to connect a channel
// with, and so stays a manual item.
test('exporting to a channel the account does not own is refused', async ({ context }) => {
    const id = await createDraft(context, 'Telegram export', ['Body for an export that cannot happen.']);
    const res = await context.request.post('/api/posts/export', {
        data: { draftId: id, chatId: '@testingandfun', format: 'Html' },
    });
    expect(res.status()).toBe(403);
    expect(await res.text()).toMatch(/connected channels/i);
});

test('the known-channels list answers for an account with no linked Telegram', async ({ context }) => {
    const res = await context.request.get('/api/channels/known');
    expect(res.ok()).toBeTruthy();
    expect(Array.isArray(await res.json())).toBeTruthy();
});

// AI features are Pro Plus only (PlanLimitations.HasAiFeatures) and the seeded account is Free, so
// what a smoke run can prove is the gate — not a translation, which would need a provider key and
// would spend real money on every run.
test('auto-translate is refused on a Free account before any provider call', async ({ context }) => {
    const id = await createDraft(context, 'Translate gate', ['Text that will not be translated.']);
    const res = await context.request.post(`/api/drafts/${id}/translations/en/auto`);
    expect(res.status()).toBe(403);
});
