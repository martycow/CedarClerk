import { Injectable, computed, signal } from '@angular/core';
import { IconName } from '../shared/icon-data.generated';

export type CommandGroup = 'file' | 'edit' | 'view' | 'tools' | 'help';

export const COMMAND_GROUPS: readonly CommandGroup[] = ['file', 'edit', 'view', 'tools', 'help'];

export interface AppCommand {
    id: string;
    group: CommandGroup;
    label: string;
    icon?: IconName;
    /** Written the way the user's keyboard reads it — 'Ctrl+K'. Display only; the shell binds keys. */
    shortcut?: string;
    /** Draws a rule above this row in the menu — the only grouping a menu gets. */
    separatorBefore?: boolean;
    /** False greys the row out everywhere at once and refuses `run`. */
    enabled?: () => boolean;
    /** A toggle's current state, drawn as a tick. */
    checked?: () => boolean;
    /** Spends credits and rewrites content — the inspector's AI panel lists exactly these. */
    ai?: boolean;
    run: () => void | Promise<void>;
}

/** What a registration hands back — the caller's only way to take its commands down again. */
export type CommandRelease = () => void;

interface Registration {
    token: number;
    commands: readonly AppCommand[];
}

// ADR-301 clause 2 — one definition per command, read by the menu bar, the palette and the
// keyboard. A page registers on mount and releases on destroy; a command whose owner is gone must
// not survive in a menu, because its `run` closes over that page's state.
@Injectable({ providedIn: 'root' })
export class CommandsService {
    private readonly registrations = signal<readonly Registration[]>([]);
    private nextToken = 0;

    /** Later registrations win a collision: a page's own Save replaces the shell's generic one. */
    readonly all = computed<readonly AppCommand[]>(() => {
        const byId = new Map<string, AppCommand>();
        for (const registration of this.registrations()) {
            for (const command of registration.commands) byId.set(command.id, command);
        }
        return [...byId.values()];
    });

    register(commands: readonly AppCommand[]): CommandRelease {
        const token = ++this.nextToken;
        this.registrations.update(list => [...list, { token, commands }]);
        let released = false;
        return () => {
            if (released) return;
            released = true;
            this.registrations.update(list => list.filter(entry => entry.token !== token));
        };
    }

    group(group: CommandGroup): readonly AppCommand[] {
        return this.all().filter(command => command.group === group);
    }

    find(id: string): AppCommand | undefined {
        return this.all().find(command => command.id === id);
    }

    isEnabled(command: AppCommand): boolean {
        return command.enabled ? command.enabled() : true;
    }

    /** True when the command existed and was allowed to run — the callers use it to eat the key. */
    run(id: string): boolean {
        const command = this.find(id);
        if (!command || !this.isEnabled(command)) return false;
        void command.run();
        return true;
    }
}

/** Matches on the label and the id, so `dupl` and `file.duplicate` both find Duplicate. */
export function filterCommands(commands: readonly AppCommand[], query: string): readonly AppCommand[] {
    const needle = query.trim().toLowerCase();
    if (!needle) return commands;
    return commands.filter(command =>
        command.label.toLowerCase().includes(needle) || command.id.toLowerCase().includes(needle));
}
