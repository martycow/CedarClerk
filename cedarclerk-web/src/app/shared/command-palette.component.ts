import { Component, ElementRef, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppCommand, CommandGroup, CommandsService, filterCommands } from '../core/commands.service';
import { LocaleService } from '../core/i18n/locale.service';
import { OverlayCoordinatorService, OverlayLayerLease } from '../core/overlay-coordinator.service';
import { IconComponent } from './icon.component';

// ADR-301 clause 3 — Ctrl+Shift+P over the command registry, deliberately NOT the Ctrl+K document
// search: "which document" and "which action" are different questions, and one box answering both
// answers neither well. Mounted once by the shell, exclusive with every other transient layer.
@Component({
    selector: 'app-command-palette',
    imports: [FormsModule, IconComponent],
    templateUrl: 'command-palette.component.html',
    styleUrls: ['command-palette.component.css'],
    host: { '(document:keydown)': 'onDialogKeydown($event)' },
})
export class CommandPaletteComponent implements OnDestroy {
    private readonly commands = inject(CommandsService);
    private readonly overlays = inject(OverlayCoordinatorService);
    private readonly host = inject(ElementRef<HTMLElement>);
    t = inject(LocaleService).t;

    open = computed(() => this.overlays.active() === 'commands');
    query = signal('');
    index = signal(0);

    /** Menu order, so a command sits where the bar put it — the palette is a second view of one
        registry, not a second ranking, and a reader who learned File's order should not have to
        learn another. Registration order within a group is what the menu draws, so the sort is by
        group alone. Disabled commands stay listed: knowing a command exists is the point. */
    readonly results = computed<readonly AppCommand[]>(() => {
        const order: Record<CommandGroup, number> = { file: 0, edit: 1, view: 2, tools: 3, help: 4 };
        return filterCommands(this.commands.all(), this.query())
            .map((command, index) => ({ command, index }))
            .sort((a, b) => order[a.command.group] - order[b.command.group] || a.index - b.index)
            .map(entry => entry.command);
    });

    private readonly queryInput = viewChild<ElementRef<HTMLInputElement>>('queryInput');
    private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
    private layer?: OverlayLayerLease;
    private wasOpen = false;

    constructor() {
        effect(() => {
            const open = this.open();
            if (open && !this.wasOpen) this.startOpen();
            else if (this.wasOpen && !open) this.finishClose();
            this.wasOpen = open;
        });
    }

    openOverlay(): void {
        if (!this.overlays.open('commands')) return;
        this.query.set('');
        this.index.set(0);
    }

    toggleOverlay(): void {
        if (this.open()) this.close();
        else this.openOverlay();
    }

    close(): void {
        this.overlays.close('commands');
    }

    ngOnDestroy(): void {
        this.layer?.release();
    }

    groupLabel(group: CommandGroup): string {
        return this.t().shell.menus[group];
    }

    isEnabled(command: AppCommand): boolean {
        return this.commands.isEnabled(command);
    }

    onQueryChange(value: string): void {
        this.query.set(value);
        this.index.set(0);
    }

    onInputKeydown(event: KeyboardEvent): void {
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                this.move(1);
                break;
            case 'ArrowUp':
                event.preventDefault();
                this.move(-1);
                break;
            case 'Enter': {
                event.preventDefault();
                const command = this.results()[this.index()];
                if (command) this.pick(command);
                break;
            }
        }
    }

    onDialogKeydown(event: KeyboardEvent): void {
        if (!this.open() || !this.layer?.isTop()) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopImmediatePropagation();
            this.close();
        } else if (event.key === 'Tab') {
            const panel = this.panel()?.nativeElement;
            if (panel) this.overlays.trapTab(panel, event);
        }
    }

    pick(command: AppCommand): void {
        if (!this.isEnabled(command)) return;
        this.close();
        this.commands.run(command.id);
    }

    private move(step: number): void {
        const count = this.results().length;
        if (!count) return;
        this.index.set((this.index() + step + count) % count);
    }

    private startOpen(): void {
        this.layer = this.overlays.registerLayer(this.host.nativeElement, 'commands');
        queueMicrotask(() => {
            if (!this.open()) return;
            const panel = this.panel()?.nativeElement;
            if (panel) this.overlays.focusFirst(panel, this.queryInput()?.nativeElement);
        });
    }

    private finishClose(): void {
        this.layer?.release();
        this.layer = undefined;
    }
}
