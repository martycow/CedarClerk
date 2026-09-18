import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MenuBarComponent } from './menu-bar.component';
import { AppCommand, CommandsService } from '../core/commands.service';
import { en } from '@localization/en';

describe('menu bar', () => {
    let fixture: ComponentFixture<MenuBarComponent>;
    let commands: CommandsService;
    const el = () => fixture.nativeElement as HTMLElement;
    const tops = () => [...el().querySelectorAll<HTMLButtonElement>('.top')];
    const rows = () => [...el().querySelectorAll<HTMLButtonElement>('.row')];
    const t = en.shell.menus;

    function register(...list: AppCommand[]) {
        commands.register(list);
        fixture.detectChanges();
    }

    beforeEach(() => {
        TestBed.configureTestingModule({});
        commands = TestBed.inject(CommandsService);
        fixture = TestBed.createComponent(MenuBarComponent);
        fixture.detectChanges();
    });

    it('draws the five groups in order, whether or not they hold anything', () => {
        expect(tops().map(button => button.textContent?.trim()))
            .toEqual([t.file, t.edit, t.view, t.tools, t.help]);
    });

    it('opens a menu on click and closes it on a second one', () => {
        tops()[0].click();
        fixture.detectChanges();
        expect(el().querySelector('.drop')).toBeTruthy();
        expect(tops()[0].getAttribute('aria-expanded')).toBe('true');

        tops()[0].click();
        fixture.detectChanges();
        expect(el().querySelector('.drop')).toBeNull();
    });

    it('says so rather than drawing an empty box for a group with no commands', () => {
        tops()[4].click();
        fixture.detectChanges();
        expect(el().querySelector('.drop-empty')?.textContent).toContain(t.empty);
    });

    // The bar holds no action of its own — every row is a registered command (ADR-301 clause 1).
    it('runs the registered command and closes the menu', () => {
        let ran = 0;
        register({ id: 'file.new', group: 'file', label: 'New document', run: () => { ran++; } });

        tops()[0].click();
        fixture.detectChanges();
        expect(rows()).toHaveLength(1);

        rows()[0].click();
        fixture.detectChanges();
        expect(ran).toBe(1);
        expect(el().querySelector('.drop')).toBeNull();
    });

    it('greys out a disabled command and refuses to run it', () => {
        let ran = 0;
        register({
            id: 'file.save', group: 'file', label: 'Save',
            enabled: () => false, run: () => { ran++; },
        });

        tops()[0].click();
        fixture.detectChanges();
        expect(rows()[0].disabled).toBe(true);

        rows()[0].click();
        expect(ran).toBe(0);
    });

    it('draws a tick for a checked toggle and its shortcut beside the label', () => {
        register({
            id: 'view.theme', group: 'view', label: 'Dark theme', shortcut: 'Ctrl+D',
            checked: () => true, run: () => undefined,
        });

        tops()[2].click();
        fixture.detectChanges();
        expect(rows()[0].getAttribute('aria-checked')).toBe('true');
        expect(rows()[0].querySelector('.tick app-icon')).toBeTruthy();
        expect(el().querySelector('.keys')?.textContent).toContain('Ctrl+D');
    });

    it('sliding along the bar moves the open menu, and Escape closes it', () => {
        tops()[0].click();
        fixture.detectChanges();

        tops()[2].dispatchEvent(new MouseEvent('mouseenter'));
        fixture.detectChanges();
        expect(tops()[2].getAttribute('aria-expanded')).toBe('true');
        expect(tops()[0].getAttribute('aria-expanded')).toBe('false');

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
        fixture.detectChanges();
        expect(el().querySelector('.drop')).toBeNull();
    });

    it('hovering a closed bar opens nothing', () => {
        tops()[2].dispatchEvent(new MouseEvent('mouseenter'));
        fixture.detectChanges();
        expect(el().querySelector('.drop')).toBeNull();
    });
});
