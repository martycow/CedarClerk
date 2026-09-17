import { TestBed } from '@angular/core/testing';
import { AppCommand, CommandsService, filterCommands } from './commands.service';

function command(id: string, over: Partial<AppCommand> = {}): AppCommand {
    return { id, group: 'file', label: id, run: () => undefined, ...over };
}

describe('command registry', () => {
    let service: CommandsService;

    beforeEach(() => {
        TestBed.configureTestingModule({});
        service = TestBed.inject(CommandsService);
    });

    it('lists what was registered and drops it again on release', () => {
        const release = service.register([command('file.new'), command('edit.find', { group: 'edit' })]);
        expect(service.all().map(c => c.id)).toEqual(['file.new', 'edit.find']);
        expect(service.group('edit').map(c => c.id)).toEqual(['edit.find']);

        release();
        expect(service.all()).toEqual([]);
    });

    it('releasing twice does not take a later registration of the same id down with it', () => {
        const release = service.register([command('file.new', { label: 'shell' })]);
        release();
        service.register([command('file.new', { label: 'page' })]);
        release();

        expect(service.all().map(c => c.label)).toEqual(['page']);
    });

    // A page registers after the shell, and its own Save is the one the menu must run.
    it('a later registration wins a colliding id', () => {
        service.register([command('file.save', { label: 'shell save' })]);
        service.register([command('file.save', { label: 'page save' })]);

        expect(service.all()).toHaveLength(1);
        expect(service.find('file.save')?.label).toBe('page save');
    });

    it('refuses to run a disabled command and reports that it did not', () => {
        let ran = 0;
        service.register([command('file.save', { enabled: () => false, run: () => { ran++; } })]);

        expect(service.run('file.save')).toBe(false);
        expect(service.run('file.missing')).toBe(false);
        expect(ran).toBe(0);
    });

    it('runs an enabled command', () => {
        let ran = 0;
        service.register([command('file.save', { run: () => { ran++; } })]);

        expect(service.run('file.save')).toBe(true);
        expect(ran).toBe(1);
    });
});

describe('command filtering', () => {
    const commands = [
        command('file.duplicate', { label: 'Duplicate document' }),
        command('view.theme', { label: 'Dark theme', group: 'view' }),
    ];

    it('matches the label and the id alike', () => {
        expect(filterCommands(commands, 'dupl').map(c => c.id)).toEqual(['file.duplicate']);
        expect(filterCommands(commands, 'view.').map(c => c.id)).toEqual(['view.theme']);
        expect(filterCommands(commands, 'THEME').map(c => c.id)).toEqual(['view.theme']);
    });

    it('an empty query is every command, not none', () => {
        expect(filterCommands(commands, '   ')).toHaveLength(2);
    });
});
