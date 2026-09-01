import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminComponent } from './admin.component';
import { AdminService, AdminUser } from '../core/admin.service';
import { AuthService } from '../core/auth.service';
import { en } from '../core/i18n/en';

async function settle(fixture: ComponentFixture<unknown>) {
    for (let link = 0; link < 5; link++) await fixture.whenStable();
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

class FakeAdmin {
    async listUsers() { return structuredClone(USERS); }
    async summary() {
        return { users: 2, paidUsers: 1, drafts: 0, published: 0, comments: 0, reactions: 0, channels: 0, storageBytes: 0 };
    }
    async audit() { return { entries: [], hasMore: false }; }
    async listInvites() { return []; }
    async listPosts() { return []; }
    async billing() { return { payments: [], totalByCurrency: [] }; }
    async usage() { return []; }
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
});
