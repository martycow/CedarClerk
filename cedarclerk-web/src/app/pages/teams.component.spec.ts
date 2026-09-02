import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TeamsComponent } from './teams.component';
import { JoinedTeam, Team, TeamMember, TeamsService } from '../core/teams.service';
import { LocaleService } from '../core/i18n/locale.service';
import { en } from '../core/i18n/en';

const TEAM: Team = {
    id: 'team-1', name: 'Signal Pine', createdAt: '2026-09-01T00:00:00Z',
    memberCount: 3, activeCount: 2, projectCount: 1,
};

const JOINED: JoinedTeam = {
    id: 'joined-1', name: 'Borrowed Grove', ownerName: 'Rowan', role: 'viewer', status: 'active',
};

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
});
