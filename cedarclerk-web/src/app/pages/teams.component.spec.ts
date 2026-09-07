import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TeamsComponent } from './teams.component';
import { JoinedTeam, Team, TeamMember, TeamsService } from '../core/teams.service';
import { LocaleService } from '../core/i18n/locale.service';
import { en } from '@localization/en';

const TEAM: Team = {
    id: 'team-1', name: 'Signal Pine', createdAt: '2026-09-01T00:00:00Z',
    memberCount: 3, activeCount: 2, projectCount: 1,
};

const JOINED: JoinedTeam = {
    id: 'joined-1', name: 'Borrowed Grove', ownerName: 'Rowan', role: 'viewer', status: 'active',
};

const MEMBER = (id: string, over: Partial<TeamMember> = {}): TeamMember => ({
    id, userId: id, email: `${id}@example.test`, name: id, role: 'editor', status: 'active',
    statusNote: null, pending: false, invitedAt: '2026-09-01T00:00:00Z', acceptedAt: '2026-09-01T00:00:00Z',
    isYou: false, ...over,
});

class TeamsStub {
    own: Team[] = [];
    memberships: JoinedTeam[] = [];
    people: TeamMember[] = [];

    list() { return Promise.resolve(this.own); }
    joined() { return Promise.resolve(this.memberships); }
    members() { return Promise.resolve(this.people); }
    create(name: string) { return Promise.resolve({ id: 'created', name }); }
}

describe('teams workspace states', () => {
    let fixture: ComponentFixture<TeamsComponent>;
    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;

    async function create(api: TeamsStub) {
        TestBed.configureTestingModule({
            providers: [
                { provide: TeamsService, useValue: api },
                { provide: LocaleService, useValue: { t: () => en } },
            ],
        });
        fixture = TestBed.createComponent(TeamsComponent);
        fixture.detectChanges();
        for (let i = 0; i < 5; i++) await Promise.resolve();
        await fixture.whenStable();
        fixture.detectChanges();
    }

    const buttonsNamed = (name: string) => [...el().querySelectorAll('app-button')]
        .filter(button => button.textContent?.trim() === name);

    it('replaces the zero-data workspace with one next action', async () => {
        await create(new TeamsStub());
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('form');
        expect(el().querySelectorAll('app-empty-state')).toHaveLength(1);
        expect(el().querySelector('.split-workspace')).toBeNull();
        expect(buttonsNamed(en.teams.newTeam)).toHaveLength(1);
    });

    it('replaces the zero state with only the creation form', async () => {
        await create(new TeamsStub());
        page().startCreate();
        fixture.detectChanges();

        expect(el().querySelector('.tm-create-card .tm-form')).not.toBeNull();
        expect(el().querySelector('app-empty-state')).toBeNull();
        expect(el().querySelector('.split-workspace')).toBeNull();
        expect(buttonsNamed(en.teams.newTeam)).toHaveLength(0);
    });

    it('uses the shared three-pane workspace and exposes its selected team', async () => {
        const api = new TeamsStub();
        api.own = [TEAM];
        await create(api);

        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        const workspace = el().querySelector('.tm-grid.split-workspace')!;
        expect(workspace.querySelectorAll(':scope > .split-pane')).toHaveLength(3);
        expect(workspace.querySelector('.tm-row.is-on')?.getAttribute('aria-current')).toBe('true');
        expect(buttonsNamed(en.teams.newTeam)).toHaveLength(1);

        page().startCreate();
        fixture.detectChanges();
        expect(buttonsNamed(en.teams.newTeam)).toHaveLength(0);
        expect(workspace.querySelector('.tm-sheet .tm-form')).not.toBeNull();
        expect(workspace.querySelector('.tm-sheet .tm-head')).toBeNull();
    });

    it('selects and describes a joined-only team without false empty states', async () => {
        const api = new TeamsStub();
        api.memberships = [JOINED];
        await create(api);

        expect(page().selectedJoined()?.id).toBe(JOINED.id);
        expect(el().querySelector('.tm-row.is-joined')?.getAttribute('aria-current')).toBe('true');
        expect(el().querySelector('.tm-sheet')?.textContent).toContain(JOINED.name);
        expect(el().querySelector('.tm-sheet')?.textContent).toContain(JOINED.ownerName);
        expect(el().querySelectorAll('app-empty-state')).toHaveLength(0);
    });

    it('leaves rename mode before creation starts', async () => {
        const api = new TeamsStub();
        api.own = [TEAM];
        await create(api);

        page().startRename();
        expect(page().renaming()).toBe(true);
        page().startCreate();
        fixture.detectChanges();

        expect(page().renaming()).toBe(false);
        expect(page().renameName()).toBe('');
        expect(el().querySelector('.tm-sheet .tm-form')).not.toBeNull();
    });

    it('filters and sorts the complete team list with a distinct no-match state', async () => {
        const api = new TeamsStub();
        api.own = [TEAM, { ...TEAM, id: 'team-2', name: 'Amber Crew', memberCount: 7, projectCount: 3 }];
        await create(api);

        page().setTeamScope('owned');
        page().teamSortKey.set('members');
        page().teamSortDirection.set('desc');
        expect(page().visibleTeams().map(team => team.name)).toEqual(['Amber Crew', 'Signal Pine']);

        page().teamQuery.set('missing');
        fixture.detectChanges();
        expect(page().visibleTeams()).toEqual([]);
        expect(el().querySelector('.tm-list app-empty-state')?.textContent).toContain('No teams match');
    });

    it('offers count-based team sorting only for the owned scope', async () => {
        const api = new TeamsStub();
        api.own = [TEAM];
        api.memberships = [JOINED];
        await create(api);

        const sort = () => el().querySelector('select[aria-label="Sort teams"]') as HTMLSelectElement;
        const head = () => el().querySelector('.tm-list-card .card-head') as HTMLElement;
        expect([...sort().options].map(option => option.value)).toEqual(['name']);
        expect(head().textContent).toContain(en.teams.crumb);
        expect(head().querySelector('.card-count')?.textContent?.trim()).toBe('2');

        page().setTeamScope('owned');
        fixture.detectChanges();
        expect([...sort().options].map(option => option.value)).toEqual(['name', 'members', 'projects']);
        expect(head().textContent).toContain(en.teams.yours);
        expect(head().querySelector('.card-count')?.textContent?.trim()).toBe('1');

        page().teamSortKey.set('members');
        page().setTeamScope('joined');
        fixture.detectChanges();
        expect(page().teamSortKey()).toBe('name');
        expect([...sort().options].map(option => option.value)).toEqual(['name']);
        expect(head().textContent).toContain(en.teams.joined);
        expect(head().querySelector('.card-count')?.textContent?.trim()).toBe('1');

        page().setTeamScope('owned');
        page().teamSortKey.set('projects');
        page().clearTeamFilters();
        fixture.detectChanges();
        expect(page().teamScope()).toBe('all');
        expect(page().teamSortKey()).toBe('name');
    });

    it('moves selection to a visible team when scope or search hides the current one', async () => {
        const api = new TeamsStub();
        api.own = [TEAM];
        api.memberships = [JOINED];
        await create(api);

        expect(page().selected()?.id).toBe(TEAM.id);
        page().setTeamScope('joined');
        fixture.detectChanges();
        expect(page().selected()).toBeNull();
        expect(page().selectedJoined()?.id).toBe(JOINED.id);
        expect(el().querySelector('.tm-sheet')?.textContent).toContain(JOINED.name);

        page().setTeamQuery('missing');
        fixture.detectChanges();
        expect(page().selectedId()).toBeNull();
        expect(el().querySelector('.tm-sheet')?.textContent).not.toContain(JOINED.name);
    });

    it('combines member search, role and status filters before sorting', async () => {
        const api = new TeamsStub();
        api.own = [TEAM];
        api.people = [
            MEMBER('zoe', { role: 'viewer', status: 'restricted' }),
            MEMBER('ada', { role: 'editor', status: 'active' }),
            MEMBER('pending', { role: 'editor', pending: true, acceptedAt: null }),
        ];
        await create(api);

        page().memberRole.set('editor');
        page().memberStatus.set('active');
        expect(page().visibleMembers().map(member => member.id)).toEqual(['ada']);

        page().memberStatus.set('pending');
        expect(page().visibleMembers().map(member => member.id)).toEqual(['pending']);
    });
});
