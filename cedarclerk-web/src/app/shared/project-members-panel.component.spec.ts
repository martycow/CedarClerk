import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProjectMembersPanelComponent } from './project-members-panel.component';
import { MembersService, ProjectMember } from '../core/members.service';
import { en } from '../core/i18n/en';

function person(over: Partial<ProjectMember>): ProjectMember {
    return {
        id: 'm', userId: 'u', email: 'someone@example.test', role: 'editor', pending: false,
        invitedAt: null, acceptedAt: null, lastSeenAt: null, isYou: false, ...over,
    };
}

const OWNER = person({ id: null, userId: 'u-owner', email: 'owner@example.test', role: 'owner' });
const EDITOR = person({ id: 'm-ed', userId: 'u-ed', email: 'editor@example.test' });
const INVITED = person({ id: 'm-inv', userId: null, email: 'new@example.test', role: 'viewer', pending: true });

class FakeMembers {
    people: ProjectMember[] = [];
    invited: { projectId: string; email: string; role: string }[] = [];
    async list() { return structuredClone(this.people); }
    async invite(projectId: string, email: string, role: 'editor' | 'viewer') {
        this.invited.push({ projectId, email, role });
        return { member: person({ id: 'm-new', userId: null, email, role, pending: true }), inviteUrl: 'https://example.test/i/abc' };
    }
    async remove() { /* nothing to undo in a fake */ }
}

describe('ProjectMembersPanelComponent', () => {
    let fixture: ComponentFixture<ProjectMembersPanelComponent>;
    let members: FakeMembers;
    const t = en.projects.canvas;

    const el = () => fixture.nativeElement as HTMLElement;
    const rows = () => [...el().querySelectorAll('li.person')] as HTMLElement[];
    const names = () => rows().map(r => r.querySelector('.person-name')?.textContent?.replace(/\s+/g, ' ').trim());

    async function create(people: ProjectMember[]) {
        members = new FakeMembers();
        members.people = people;
        TestBed.configureTestingModule({ providers: [{ provide: MembersService, useValue: members }] });
        fixture = TestBed.createComponent(ProjectMembersPanelComponent);
        fixture.componentRef.setInput('projectId', 'p1');
        fixture.detectChanges();
        for (let i = 0; i < 6; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    it('lists everyone in the server\'s order and marks the caller', async () => {
        await create([OWNER, { ...EDITOR, isYou: true }, INVITED]);

        expect(names()).toEqual([
            'owner@example.test',
            `editor@example.test · ${t.you}`,
            'new@example.test',
        ]);
        expect(rows()[2].querySelector('.person-meta')?.textContent?.replace(/\s+/g, ' ').trim())
            .toBe(`${t.roleViewer} · ${t.invitePending}`);
        expect(el().querySelector('.side-count')?.textContent?.trim()).toBe('3');
    });

    it('shows a member the list and none of the owner\'s controls', async () => {
        await create([OWNER, { ...EDITOR, isYou: true }, INVITED]);

        expect(el().querySelector('.person-actions')).toBeNull();
        expect(el().querySelector('.invite')).toBeNull();
    });

    it('gives the owner controls on every row but their own', async () => {
        await create([{ ...OWNER, isYou: true }, EDITOR, INVITED]);

        expect(rows().map(r => !!r.querySelector('.person-actions'))).toEqual([false, true, true]);
        // Send again exists only on the pending row, beside the remove every controlled row has.
        expect(rows().map(r => r.querySelectorAll('.person-actions app-button').length)).toEqual([0, 1, 2]);
        expect(el().querySelector('.invite')).not.toBeNull();
    });

    it('invites, publishes the grown list and shows the link', async () => {
        await create([{ ...OWNER, isYou: true }]);
        let published: readonly ProjectMember[] = [];
        fixture.componentInstance.peopleChange.subscribe(p => published = p);

        fixture.componentInstance.inviteEmail.set('  New@Example.test ');
        fixture.componentInstance.inviteRole.set('viewer');
        await fixture.componentInstance.invite();
        fixture.detectChanges();

        expect(members.invited).toEqual([{ projectId: 'p1', email: 'new@example.test', role: 'viewer' }]);
        expect(published.map(p => p.email)).toEqual(['owner@example.test', 'new@example.test']);
        expect(el().querySelector('.invite-link')?.textContent?.trim()).toBe('https://example.test/i/abc');
        expect(fixture.componentInstance.inviteEmail()).toBe('');
    });

    it('removes through the confirm and publishes the shorter list', async () => {
        await create([{ ...OWNER, isYou: true }, EDITOR]);
        let published: readonly ProjectMember[] = [];
        fixture.componentInstance.peopleChange.subscribe(p => published = p);

        fixture.componentInstance.openRemove(EDITOR);
        fixture.detectChanges();
        expect(el().querySelector('app-modal')).not.toBeNull();

        await fixture.componentInstance.removeMember();
        fixture.detectChanges();
        expect(published.map(p => p.email)).toEqual(['owner@example.test']);
        expect(el().querySelector('app-modal')).toBeNull();
    });
});
