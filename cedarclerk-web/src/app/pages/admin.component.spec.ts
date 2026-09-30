import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminComponent } from './admin.component';
import {
    AdminBilling, AdminCollectionQuery, AdminInviteCode, AdminLanding, AdminPost, AdminService, AdminUsage, AdminUser,
    AdminWaitlistEntry,
} from '../core/admin.service';
import { AuthService } from '../core/auth.service';
import { en } from '@localization/en';

async function settle(fixture: ComponentFixture<unknown>) {
    for (let link = 0; link < 5; link++) await fixture.whenStable();
}

function deferred<T>() {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>(done => { resolve = done; });
    return { promise, resolve };
}

function user(id: string, over: Partial<AdminUser> = {}): AdminUser {
    return {
        id, email: `${id}@example.test`, createdAt: '2026-08-01T09:00:00', isAdmin: false,
        inviteCodeId: null, planTier: 'Free', effectiveTier: 'Free', planExpiresAt: null,
        trialUsed: false, telegramUsername: null, isLocked: false, drafts: 0, published: 0,
        channels: 0, credits: 0, ...over,
    };
}

const USERS: AdminUser[] = [user('ada'), user('grace', { planTier: 'Pro', effectiveTier: 'Pro' })];
const INVITES: AdminInviteCode[] = [
    {
        id: 'invite-a', code: 'ALPHA', label: 'Founders', isActive: true, expiresAt: null,
        maxUses: null, uses: 3, createdAt: '2026-08-01T09:00:00', joined: 3, isUsable: true,
    },
    {
        id: 'invite-b', code: 'BETA', label: 'Press', isActive: false, expiresAt: '2026-09-01T09:00:00',
        maxUses: 1, uses: 1, createdAt: '2026-08-02T09:00:00', joined: 1, isUsable: false,
    },
];
const POSTS: AdminPost[] = [
    {
        id: 'post-a', title: 'Alpha log', ownerEmail: 'grace@example.test', updatedAt: '2026-08-01T09:00:00',
        isBlogPublished: true, isPrivate: false, isArchived: false, viewCount: 4, comments: 1,
        blogUrl: '/alpha', telegramUrl: null,
    },
    {
        id: 'post-b', title: 'Secret beta', ownerEmail: 'ada@example.test', updatedAt: '2026-08-02T09:00:00',
        isBlogPublished: false, isPrivate: true, isArchived: false, viewCount: 20, comments: 3,
        blogUrl: null, telegramUrl: null,
    },
];
const BILLING: AdminBilling = {
    payments: [
        {
            id: 'payment-a', provider: 'Stripe', plan: 'Pro', amount: 900, currency: 'USD', status: 'completed',
            createdAt: '2026-08-01T09:00:00', ownerEmail: 'grace@example.test',
        },
        {
            id: 'payment-b', provider: 'Stripe', plan: 'ProPlus', amount: 1900, currency: 'USD', status: 'pending',
            createdAt: '2026-08-02T09:00:00', ownerEmail: 'ada@example.test',
        },
    ],
    totalByCurrency: [{ currency: 'USD', total: 900 }],
    statuses: ['completed', 'pending'],
    total: 2,
    pageSize: 100,
};
const USAGE: AdminUsage[] = [
    { ownerId: 'ada', ownerEmail: 'ada@example.test', bytes: 0, files: 0, aiToday: 4 },
    { ownerId: 'grace', ownerEmail: 'grace@example.test', bytes: 2048, files: 2, aiToday: 0 },
];
const WAITLIST: AdminWaitlistEntry[] = [
    { id: 'wait-a', email: 'ada@example.test', language: 'en', createdAt: '2026-08-01T09:00:00' },
    { id: 'wait-b', email: 'boris@example.test', language: 'ru', createdAt: '2026-08-02T09:00:00' },
];
const LANDING: AdminLanding = {
    kickerEn: null, kickerRu: null, heroTitleEn: null, heroTitleRu: null, heroSubEn: null, heroSubRu: null,
    proofEn: null, proofRu: null, noteEn: null, noteRu: null, showcaseBlog: null,
    showShots: false, showFeatures: false, showPricing: false, showRoadmap: false, showStory: false,
    showDownload: false, shots: [], roadmap: [], story: [], configuredShowcaseBlog: null, files: [], waitlist: 0,
    defaults: {
        kicker: { en: null, ru: null },
        heroTitle: { en: null, ru: null },
        heroSub: { en: null, ru: null },
    },
};

class FakeAdmin {
    postQueries: AdminCollectionQuery[] = [];
    paymentQueries: AdminCollectionQuery[] = [];
    postTotal: number | null = null;
    paymentTotal: number | null = null;

    async listUsers() { return structuredClone(USERS); }
    async summary() {
        return { users: 2, paidUsers: 1, drafts: 0, published: 0, comments: 0, reactions: 0, channels: 0, storageBytes: 0 };
    }
    async audit() { return { entries: [], hasMore: false }; }
    async listInvites() { return structuredClone(INVITES); }
    async listPosts(query?: AdminCollectionQuery) {
        if (query) this.postQueries.push(structuredClone(query));
        let items = query?.filter === 'private' ? [POSTS[1]] : [...POSTS];
        if (query?.sort === 'activity' && query.direction === 'desc') items = [...items].reverse();
        return { items: structuredClone(items), total: this.postTotal ?? items.length, pageSize: 100 };
    }
    async billing(query?: AdminCollectionQuery) {
        if (query) this.paymentQueries.push(structuredClone(query));
        let payments = query?.filter === 'completed' ? [BILLING.payments[0]] : [...BILLING.payments];
        if (query?.sort === 'amount' && query.direction === 'desc') payments = [...payments].reverse();
        return {
            ...structuredClone(BILLING), payments: structuredClone(payments),
            total: this.paymentTotal ?? payments.length,
        };
    }
    landingData = structuredClone(LANDING);
    landingSave: Record<string, unknown> | null = null;
    async landing() { return structuredClone(this.landingData); }
    async waitlist() { return []; }
    async saveLanding(body: Record<string, unknown>) {
        this.landingSave = body;
        this.landingData = { ...this.landingData, ...body } as AdminLanding;
    }
    async usage() { return structuredClone(USAGE); }
}

describe('admin panel — the per-user modal (T-257)', () => {
    let fixture: ComponentFixture<AdminComponent>;
    const t = en.admin;

    const el = () => fixture.nativeElement as HTMLElement;
    const cards = () => [...el().querySelectorAll('.user-card')] as HTMLButtonElement[];
    const modals = () => [...el().querySelectorAll('app-modal')] as HTMLElement[];
    const modalTitles = () =>
        modals().map(m => m.querySelector('.modal-title')!.textContent!.trim());
    const buttonNamed = (root: ParentNode, label: string) =>
        [...root.querySelectorAll('app-button button')]
            .find(b => b.textContent!.trim() === label) as HTMLButtonElement | undefined;

    beforeEach(async () => {
        TestBed.configureTestingModule({
            providers: [provideRouter([]), { provide: AdminService, useClass: FakeAdmin }],
        });
        fixture = TestBed.createComponent(AdminComponent);
        TestBed.inject(AuthService).userEmail.set('ada@example.test');
        fixture.detectChanges();
        await settle(fixture);
        fixture.detectChanges();
    });

    afterEach(() => vi.useRealTimers());

    it('loads and saves bilingual landing copy without replacing screenshot settings', async () => {
        const api = TestBed.inject(AdminService) as unknown as FakeAdmin;
        api.landingData = {
            ...structuredClone(LANDING), showShots: true,
            shots: [{ file: 'uploaded.png', caption: { en: 'Product', ru: 'Продукт' } }],
            editorial: { closingTitle: { en: 'Saved heading', ru: null } },
            editorialFields: [{ key: 'closingTitle', label: { en: 'Closing', ru: 'Приглашение' },
                default: { en: 'Default heading', ru: 'Заголовок' } }],
        };
        const component = fixture.componentInstance;
        await component.loadLanding();
        component.tab.set('landing');
        fixture.detectChanges();
        expect(component.editorialFields[0].en).toBe('Saved heading');
        expect(el().querySelector('#landing-closingTitle-ru')?.getAttribute('placeholder')).toBe('Заголовок');
        component.editorialFields[0].ru = '  Новый заголовок  ';
        await component.saveLanding();
        expect(api.landingSave?.['editorial']).toEqual({ closingTitle: { en: 'Saved heading', ru: 'Новый заголовок' } });
        expect(api.landingSave?.['shots']).toEqual(api.landingData.shots);
        expect(api.landingSave?.['showShots']).toBe(true);
        expect(component.editorialFields[0].ru).toBe('Новый заголовок');
    });

    it('the card list carries no inline form, and a card opens the account modal', () => {
        expect(cards().length).toBe(2);
        expect(el().querySelector('.user-form')).toBeNull();
        expect(modals().length).toBe(0);

        cards()[1].click();
        fixture.detectChanges();

        expect(modalTitles()).toEqual(['grace@example.test']);
        // Seeded from the account, so saving without touching anything is a no-op.
        expect(fixture.componentInstance.planTier).toBe('Pro');
        expect(el().querySelector('.user-form')).not.toBeNull();
    });

    // ModalComponent binds document:keydown.escape on every mounted instance, so a stacked confirm
    // would be dismissed together with the modal it stands on — losing a destructive question.
    it('the delete confirm replaces the account modal rather than stacking on it', () => {
        cards()[1].click();
        fixture.detectChanges();

        buttonNamed(el(), t.actions.deleteAccount)!.click();
        fixture.detectChanges();

        expect(modals().length).toBe(1);
        expect(modalTitles()).toEqual([t.actions.deleteTitle]);
        expect(el().textContent).toContain(t.actions.deleteBody('grace@example.test'));
        expect(el().querySelector('.user-form')).toBeNull();
    });

    it('deleting is refused on your own account, so the confirm is unreachable there', () => {
        cards()[0].click();
        fixture.detectChanges();

        expect(buttonNamed(el(), t.actions.deleteAccount)!.disabled).toBe(true);
    });

    it('filters invite codes, exposes the active sort to assistive technology and clears a no-match query', () => {
        const component = fixture.componentInstance;
        component.tab.set('invites');
        fixture.detectChanges();

        const activeHeader = el().querySelector('.invite-row.head [aria-sort="ascending"]');
        expect(activeHeader?.textContent).toContain(t.invites.code);

        component.inviteQuery.set('missing');
        fixture.detectChanges();
        expect(el().textContent).toContain(en.admin.collections.noMatches);

        buttonNamed(el(), en.admin.collections.clearFilters)!.click();
        fixture.detectChanges();
        expect(component.inviteQuery()).toBe('');
        expect(component.visibleInvites().map(row => row.id)).toEqual(['invite-a', 'invite-b']);
    });

    it('uses server-filtered and server-sorted post/payment pages while local collections stay local', async () => {
        const component = fixture.componentInstance;

        component.setPostFilter('private');
        await settle(fixture);
        expect(component.visiblePosts().map(row => row.id)).toEqual(['post-b']);
        component.clearPostFilters();
        await settle(fixture);
        component.sortPosts('activity');
        await settle(fixture);
        expect(component.visiblePosts().map(row => row.id)).toEqual(['post-b', 'post-a']);

        component.waitlistEntries.set(structuredClone(WAITLIST));
        component.waitlistLanguage.set('ru');
        expect(component.visibleWaitlist().map(row => row.id)).toEqual(['wait-b']);
        component.clearWaitlistFilters();
        expect(component.waitlistLanguages()).toEqual(['en', 'ru']);

        component.setPaymentStatus('completed');
        await settle(fixture);
        expect(component.visiblePayments().map(row => row.id)).toEqual(['payment-a']);
        component.clearPaymentFilters();
        await settle(fixture);
        component.sortPayments('amount');
        await settle(fixture);
        expect(component.visiblePayments().map(row => row.id)).toEqual(['payment-b', 'payment-a']);

        component.usageFilter.set('storage');
        expect(component.visibleUsage().map(row => row.ownerEmail)).toEqual(['grace@example.test']);
        component.clearUsageFilters();
        component.sortUsage('ai');
        expect(component.visibleUsage().map(row => row.ownerEmail)).toEqual(['ada@example.test', 'grace@example.test']);
    });

    it('sends post and payment criteria to the server, debouncing free-text search', async () => {
        const component = fixture.componentInstance;
        const api = TestBed.inject(AdminService) as unknown as FakeAdmin;

        component.setPostFilter('private');
        await settle(fixture);
        expect(api.postQueries.at(-1)).toMatchObject({ filter: 'private', skip: 0 });

        component.sortPayments('amount');
        await settle(fixture);
        expect(api.paymentQueries.at(-1)).toMatchObject({ sort: 'amount', direction: 'desc' });

        vi.useFakeTimers();
        const calls = api.postQueries.length;
        component.postQuery.set('needle');
        component.postSkip.set(100);
        component.queuePostReload();
        expect(component.postSkip()).toBe(0);
        await vi.advanceTimersByTimeAsync(249);
        expect(api.postQueries).toHaveLength(calls);
        await vi.advanceTimersByTimeAsync(1);
        expect(api.postQueries.at(-1)?.search).toBe('needle');
    });

    it('does not let a general reload overwrite newer post and payment queries', async () => {
        const component = fixture.componentInstance;
        const api = TestBed.inject(AdminService) as unknown as FakeAdmin;
        const slowPosts = deferred<{ items: AdminPost[]; total: number; pageSize: number }>();
        const slowPayments = deferred<AdminBilling>();
        vi.spyOn(api, 'listPosts')
            .mockImplementationOnce(() => slowPosts.promise)
            .mockResolvedValueOnce({ items: [structuredClone(POSTS[1])], total: 1, pageSize: 100 });
        vi.spyOn(api, 'billing')
            .mockImplementationOnce(() => slowPayments.promise)
            .mockResolvedValueOnce({ ...structuredClone(BILLING), payments: [structuredClone(BILLING.payments[1])], total: 1 });

        const generalReload = (component as unknown as { reload(): Promise<void> }).reload();
        await Promise.resolve();
        await Promise.all([
            (component as unknown as { reloadPosts(): Promise<void> }).reloadPosts(),
            (component as unknown as { reloadPayments(): Promise<void> }).reloadPayments(),
        ]);
        expect(component.posts().map(row => row.id)).toEqual(['post-b']);
        expect(component.billing()?.payments.map(row => row.id)).toEqual(['payment-b']);

        slowPosts.resolve({ items: [structuredClone(POSTS[0])], total: 1, pageSize: 100 });
        slowPayments.resolve({ ...structuredClone(BILLING), payments: [structuredClone(BILLING.payments[0])], total: 1 });
        await generalReload;

        expect(component.posts().map(row => row.id)).toEqual(['post-b']);
        expect(component.billing()?.payments.map(row => row.id)).toEqual(['payment-b']);
    });

    it('pages posts and payments through server offsets and reports the full filtered range', async () => {
        const component = fixture.componentInstance;
        const api = TestBed.inject(AdminService) as unknown as FakeAdmin;
        api.postTotal = 202;
        api.paymentTotal = 202;
        component.postTotal.set(202);
        component.postPageSize.set(100);
        component.billing.set({ ...structuredClone(BILLING), total: 202 });

        await component.pagePostsForward();
        expect(api.postQueries.at(-1)?.skip).toBe(100);
        expect(component.postRangeLabel()).toBe('101–102 / 202');
        await component.pagePostsBack();
        expect(api.postQueries.at(-1)?.skip).toBe(0);

        await component.pagePaymentsForward();
        expect(api.paymentQueries.at(-1)?.skip).toBe(100);
        expect(component.paymentRangeLabel()).toBe('101–102 / 202');
        await component.pagePaymentsBack();
        expect(api.paymentQueries.at(-1)?.skip).toBe(0);
    });

    it('keeps table empty states in rows and gives simultaneous search landmarks unique names', () => {
        const component = fixture.componentInstance;
        const assertEmptyTable = (colspan: string) => {
            const row = el().querySelector('.table-empty-row[role="row"]') as HTMLElement;
            expect(row?.parentElement?.getAttribute('role')).toBe('table');
            expect(row?.firstElementChild?.getAttribute('role')).toBe('cell');
            expect(row?.firstElementChild?.getAttribute('aria-colspan')).toBe(colspan);
        };

        component.invites.set([]);
        component.tab.set('invites');
        fixture.detectChanges();
        assertEmptyTable('5');

        component.posts.set([]);
        component.postTotal.set(0);
        component.tab.set('posts');
        fixture.detectChanges();
        assertEmptyTable('5');

        component.landing.set(structuredClone(LANDING));
        component.waitlistEntries.set([]);
        component.tab.set('landing');
        fixture.detectChanges();
        assertEmptyTable('3');

        component.billing.set({ ...structuredClone(BILLING), payments: [], total: 0 });
        component.usage.set([]);
        component.tab.set('reports');
        fixture.detectChanges();
        const emptyCells = [...el().querySelectorAll('.table-empty-row > [role="cell"]')];
        expect(emptyCells.map(cell => cell.getAttribute('aria-colspan'))).toEqual(['5', '4']);
        const searchNames = [...el().querySelectorAll('[role="search"]')]
            .map(region => region.getAttribute('aria-label'));
        expect(searchNames).toEqual([t.reports.revenue, t.reports.usage]);
        expect(new Set(searchNames).size).toBe(searchNames.length);
    });
});
