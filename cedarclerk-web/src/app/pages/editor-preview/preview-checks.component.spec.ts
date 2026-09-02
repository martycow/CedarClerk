import { TestBed } from '@angular/core/testing';
import { LocaleService } from '../../core/i18n/locale.service';
import { PreviewCheck, PreviewChecksComponent } from './preview-checks.component';

const CHECKS: PreviewCheck[] = [
    { id: 'title', label: 'Title', detail: 'Looks good', tone: 'ok' },
    { id: 'links', label: 'Link checks', detail: '1 warning found', tone: 'warn' },
    { id: 'slug', label: 'Slug', detail: 'Assigned at the first publish', tone: 'muted' },
];

describe('PreviewChecksComponent', () => {
    function create(checks: PreviewCheck[] = CHECKS, loading = false) {
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(PreviewChecksComponent);
        fixture.componentRef.setInput('title', 'Checks · Blog');
        fixture.componentRef.setInput('checks', checks);
        fixture.componentRef.setInput('loading', loading);
        fixture.detectChanges();
        return fixture;
    }

    it('lists every check with its label, detail and tone', () => {
        const el = create().nativeElement as HTMLElement;
        const rows = Array.from(el.querySelectorAll<HTMLElement>('.pc-row'));
        expect(rows.map(r => r.dataset['tone'])).toEqual(['ok', 'warn', 'muted']);
        expect(rows[1].querySelector('.pc-label')!.textContent!.trim()).toBe('Link checks');
        expect(rows[1].querySelector('.pc-detail')!.textContent!.trim()).toBe('1 warning found');
        // The tone is a word for a screen reader, not only a colour.
        expect(rows[1].textContent).toContain('warning');
    });

    it('shows a running line while the checks are being asked and no rows yet', () => {
        const el = create([], true).nativeElement as HTMLElement;
        expect(el.querySelector('.pc-running')).not.toBeNull();
        expect(el.querySelector('.pc-none')).toBeNull();
    });

    it('opens the full checks on "View all details"', () => {
        const fixture = create();
        let opened = false;
        fixture.componentInstance.details.subscribe(() => opened = true);
        (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.pc-all')!.click();
        expect(opened).toBe(true);
    });
});
