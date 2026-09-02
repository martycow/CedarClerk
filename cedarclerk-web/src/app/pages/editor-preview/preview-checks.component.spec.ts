import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LocaleService } from '../../core/i18n/locale.service';
import { PreviewCheck, PreviewChecksComponent, groupChecks } from './preview-checks.component';

const CHECKS: PreviewCheck[] = [
    { id: 'title', label: 'Title', detail: 'Looks good', tone: 'ok' },
    { id: 'links', label: 'Link checks', detail: '1 warning found', tone: 'warn' },
    { id: 'slug', label: 'Slug', detail: 'Assigned at the first publish', tone: 'muted' },
    { id: 'type', label: 'Type', detail: 'Working material', tone: 'blocking', fix: { label: 'Fix' } },
    { id: 'account', label: 'Account', detail: 'Not connected', tone: 'setup', fix: { label: 'Fix', route: '/settings', query: { tab: 'integrations' } } },
];

describe('groupChecks', () => {
    it('sorts rows under the shared headings and always draws the three that answer the question', () => {
        const groups = groupChecks(CHECKS);
        expect(groups.map(g => g.id)).toEqual(['blocking', 'warnings', 'ready', 'setup']);
        expect(groups.map(g => g.checks.map(c => c.id))).toEqual([['type'], ['links'], ['title', 'slug'], ['account']]);
    });

    it('keeps Blocking, Warnings and Ready even when they are empty, and drops an empty Needs setup', () => {
        const groups = groupChecks([]);
        expect(groups.map(g => g.id)).toEqual(['blocking', 'warnings', 'ready']);
        expect(groups.every(g => g.checks.length === 0)).toBe(true);
    });
});

describe('PreviewChecksComponent', () => {
    function create(checks: PreviewCheck[] = CHECKS, loading = false, showAll = true) {
        TestBed.configureTestingModule({ providers: [provideRouter([])] });
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(PreviewChecksComponent);
        fixture.componentRef.setInput('title', 'Checks · Blog');
        fixture.componentRef.setInput('checks', checks);
        fixture.componentRef.setInput('loading', loading);
        fixture.componentRef.setInput('showAll', showAll);
        fixture.detectChanges();
        return fixture;
    }

    it('groups the checks under Blocking, Warnings, Ready and Needs setup with counts', () => {
        const el = create().nativeElement as HTMLElement;
        const groups = Array.from(el.querySelectorAll<HTMLElement>('.pc-group'));
        expect(groups.map(g => g.querySelector('.pc-group-name')!.textContent!.trim())).toEqual(['Blocking', 'Warnings', 'Ready', 'Needs setup']);
        expect(groups.map(g => g.querySelector('.pc-group-count')!.textContent!.trim())).toEqual(['1', '1', '2', '1']);
        const warning = groups[1].querySelector<HTMLElement>('.pc-row')!;
        expect(warning.dataset['tone']).toBe('warn');
        expect(warning.querySelector('.pc-label')!.textContent!.trim()).toBe('Link checks');
        expect(warning.querySelector('.pc-detail')!.textContent!.trim()).toBe('1 warning found');
        // The tone is a word for a screen reader, not only a colour.
        expect(warning.textContent).toContain('warning');
        expect(groups[0].textContent).toContain('blocking');
    });

    it('says all clear under an empty group instead of drawing nothing', () => {
        const el = create([{ id: 'title', label: 'Title', detail: 'ok', tone: 'ok' }]).nativeElement as HTMLElement;
        const groups = Array.from(el.querySelectorAll<HTMLElement>('.pc-group'));
        expect(groups.map(g => g.querySelector('.pc-group-name')!.textContent!.trim())).toEqual(['Blocking', 'Warnings', 'Ready']);
        expect(groups[0].querySelector('.pc-clear')!.textContent!.trim()).toBe('All clear');
        expect(groups[2].querySelector('.pc-clear')).toBeNull();
    });

    it('draws a Fix only where the row carries one: a link for a route, a button for an action', () => {
        const fixture = create();
        const el = fixture.nativeElement as HTMLElement;
        const fixed: string[] = [];
        fixture.componentInstance.fix.subscribe(id => fixed.push(id));
        const button = el.querySelector<HTMLButtonElement>('.pc-group[data-tone="blocking"] button.pc-fix')!;
        expect(button.getAttribute('aria-label')).toBe('Fix · Type');
        button.click();
        expect(fixed).toEqual(['type']);
        const link = el.querySelector<HTMLAnchorElement>('.pc-group[data-tone="setup"] a.pc-fix')!;
        expect(link.getAttribute('href')).toBe('/settings?tab=integrations');
        expect(el.querySelectorAll('.pc-fix').length).toBe(2);
    });

    it('shows a running line while the checks are being asked', () => {
        const el = create([], true).nativeElement as HTMLElement;
        expect(el.querySelector('.pc-running')).not.toBeNull();
    });

    it('opens the full checks on "View all details", and hides the button when told to', () => {
        const fixture = create();
        let opened = false;
        fixture.componentInstance.details.subscribe(() => opened = true);
        (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.pc-all')!.click();
        expect(opened).toBe(true);
        fixture.destroy();
        TestBed.resetTestingModule();
        const hidden = create(CHECKS, false, false).nativeElement as HTMLElement;
        expect(hidden.querySelector('.pc-all')).toBeNull();
    });
});
