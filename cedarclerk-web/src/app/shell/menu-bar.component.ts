import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal } from '@angular/core';
import { AppCommand, COMMAND_GROUPS, CommandGroup, CommandsService } from '../core/commands.service';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';

const GROUP_ICONS: Record<CommandGroup, IconName> = {
    file: 'file-text',
    edit: 'pencil-simple',
    view: 'eye',
    tools: 'flask',
    help: 'info',
};

// ADR-301 clause 1 — the workshop's menu row, drawn entirely from the command registry. It holds no
// action of its own on purpose: a menu that carried its own copy of Export would drift from the one
// the palette and the shortcut run. Navigation is not here — the sidebar keeps the project tree.
@Component({
    selector: 'app-menu-bar',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: {
        'data-surface': 'chrome',
        'data-box': 'trim',
        '(document:click)': 'onDocumentClick($event)',
        '(document:keydown)': 'onDocumentKeydown($event)',
    },
    template: `
        <div class="bar" role="menubar" [attr.aria-label]="labels().label">
            @for (group of groups(); track group.id) {
                <div class="menu" [class.is-open]="openGroup() === group.id">
                    <button type="button" class="top" role="menuitem"
                            [attr.aria-haspopup]="'menu'"
                            [attr.aria-expanded]="openGroup() === group.id"
                            [attr.id]="'menu-top-' + group.id"
                            (click)="toggle(group.id, $event)"
                            (mouseenter)="hover(group.id)">
                        <app-icon [name]="group.icon" size="xs" />
                        <span class="top-label">{{ group.label }}</span>
                    </button>

                    @if (openGroup() === group.id) {
                        <div class="drop" role="menu" [attr.aria-labelledby]="'menu-top-' + group.id">
                            @if (!group.commands.length) {
                                <p class="drop-empty">{{ labels().empty }}</p>
                            }
                            @for (command of group.commands; track command.id) {
                                @if (command.separatorBefore) { <hr class="rule" /> }
                                <button type="button" class="row" role="menuitem"
                                        [disabled]="!commands.isEnabled(command)"
                                        [attr.aria-checked]="command.checked ? command.checked() : null"
                                        (click)="pick(command)">
                                    <span class="tick">
                                        @if (command.checked && command.checked()) {
                                            <app-icon name="check" size="xs" weight="bold" />
                                        }
                                    </span>
                                    <span class="row-label">{{ command.label }}</span>
                                    @if (command.shortcut) {
                                        <kbd class="keys">{{ command.shortcut }}</kbd>
                                    }
                                </button>
                            }
                        </div>
                    }
                </div>
            }
        </div>
    `,
    styles: [`
        :host { display: block; }

        .bar {
            display: flex;
            align-items: stretch;
            gap: 2px;
            height: 28px;
            padding: 0 var(--space-1);
            background: var(--surface);
            color: var(--text);
            border-bottom: 1px solid var(--border);
        }

        .menu { position: relative; display: flex; }

        .top {
            display: inline-flex;
            align-items: center;
            gap: 5px;
            padding: 0 var(--space-2);
            border: 0;
            border-radius: var(--radius-sm);
            background: transparent;
            color: inherit;
            font: inherit;
            font-size: var(--text-chrome-sm, var(--fs-11));
            cursor: pointer;
        }

        .top:hover, .menu.is-open .top { background: var(--hover); }
        .top:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }

        .drop {
            position: absolute;
            top: 100%;
            left: 0;
            z-index: 60;
            min-width: 260px;
            padding: var(--space-1) 0;
            background: var(--surface);
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            box-shadow: var(--shadow-lg);
        }

        .row {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            width: 100%;
            min-height: 30px;
            padding: 0 var(--space-3) 0 var(--space-1);
            white-space: nowrap;
            border: 0;
            background: transparent;
            color: var(--text);
            font: inherit;
            font-size: var(--fs-13);
            text-align: left;
            cursor: pointer;
        }

        .row:hover:not(:disabled) { background: var(--hover); }
        .row:disabled { color: var(--t3); cursor: default; }
        .row:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }

        .tick {
            display: inline-flex;
            justify-content: center;
            width: 18px;
            color: var(--accent);
        }

        /* A label never wraps around its shortcut — the drop grows instead, which is what a menu
           does; two lines for one command was how the palette row read before this. */
        .row-label { flex: 1; min-width: 0; white-space: nowrap; }

        .keys {
            color: var(--t3);
            font-family: var(--font-mono);
            font-size: var(--fs-11);
        }

        .rule { margin: var(--space-1) 0; border: 0; border-top: 1px solid var(--border); }

        .drop-empty {
            margin: 0;
            padding: var(--space-2) var(--space-3);
            color: var(--t3);
            font-size: var(--fs-12);
        }

        /* The words go before the icons do: a phone still needs File to say File. */
        @media (max-width: 640px) {
            .top { padding: 0 var(--space-1); }
            .top-label { display: none; }
        }
    `],
})
export class MenuBarComponent {
    protected readonly commands = inject(CommandsService);
    protected readonly t = inject(LocaleService).t;
    private readonly host = inject(ElementRef<HTMLElement>);

    protected readonly labels = computed(() => this.t().shell.menus);

    protected readonly openGroup = signal<CommandGroup | null>(null);

    protected readonly groups = computed(() => {
        const labels = this.labels();
        const all = this.commands.all();
        return COMMAND_GROUPS.map(id => ({
            id,
            label: labels[id],
            icon: GROUP_ICONS[id],
            commands: all.filter(command => command.group === id),
        }));
    });

    toggle(group: CommandGroup, event: MouseEvent): void {
        event.stopPropagation();
        this.openGroup.update(current => current === group ? null : group);
    }

    /** Once one menu is open, sliding along the bar opens the next — the desktop idiom. */
    hover(group: CommandGroup): void {
        if (this.openGroup()) this.openGroup.set(group);
    }

    pick(command: AppCommand): void {
        this.openGroup.set(null);
        this.commands.run(command.id);
    }

    close(): void {
        this.openGroup.set(null);
    }

    onDocumentClick(event: MouseEvent): void {
        if (!this.openGroup()) return;
        const target = event.target;
        if (target instanceof Node && this.host.nativeElement.contains(target)) return;
        this.close();
    }

    onDocumentKeydown(event: KeyboardEvent): void {
        if (event.key === 'Escape' && this.openGroup()) {
            event.preventDefault();
            this.close();
        }
    }
}
