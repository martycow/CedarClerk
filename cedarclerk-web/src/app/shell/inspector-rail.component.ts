import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AiOperationsService } from '../core/ai-operations.service';
import { AppearanceService, INSPECTOR_WIDTH_MAX, INSPECTOR_WIDTH_MIN, InspectorTab, clampInspectorWidth } from '../core/appearance.service';
import { AppCommand, CommandsService } from '../core/commands.service';
import { LocaleService } from '../core/i18n/locale.service';
import { WorkspaceContextService } from '../core/workspace-context.service';
import { ButtonComponent } from '../bench/forms/button.component';
import { IconComponent } from '../shared/icon.component';

// ADR-301 clause 4/5 — the session's inspector, beside the page rather than inside it. Properties
// says what the current object is; AI says what an operation would do to it, and states the scope
// before anything runs. The page-local inspectors stay: this one never knows about marks or tables.
@Component({
    selector: 'app-inspector-rail',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [FormsModule, RouterLink, IconComponent, ButtonComponent],
    host: {
        'data-surface': 'paper',
        '[style.--inspector-w]': 'widthPx()',
        '[class.is-open]': 'open()',
    },
    template: `
        <button type="button" class="handle" [attr.aria-expanded]="open()"
                aria-controls="cedar-inspector" [title]="t().shell.inspector.toggle"
                [attr.aria-label]="t().shell.inspector.toggle" (click)="toggle()">
            <app-icon [name]="open() ? 'caret-right' : 'caret-left'" size="sm" />
        </button>

        @if (open()) {
            <div class="grip" role="separator" aria-orientation="vertical"
                 [attr.aria-label]="t().shell.inspector.resize"
                 [attr.aria-valuenow]="width()" [attr.aria-valuemin]="min" [attr.aria-valuemax]="max"
                 tabindex="0" (pointerdown)="startDrag($event)" (keydown)="onGripKey($event)"></div>

            <aside id="cedar-inspector" class="rail" [attr.aria-label]="t().shell.inspector.title">
                <header class="head">
                    <div class="tabs" role="tablist" [attr.aria-label]="t().shell.inspector.title">
                        @for (tab of tabs; track tab.id) {
                            <button type="button" class="tab" role="tab"
                                    [class.on]="activeTab() === tab.id"
                                    [attr.aria-selected]="activeTab() === tab.id"
                                    [attr.aria-controls]="'cedar-inspector-' + tab.id"
                                    (click)="selectTab(tab.id)">
                                <app-icon [name]="tab.icon" size="xs" />
                                {{ tab.id === 'properties' ? t().shell.inspector.properties : t().shell.inspector.ai }}
                            </button>
                        }
                    </div>
                </header>

                @if (activeTab() === 'properties') {
                    <div id="cedar-inspector-properties" class="body" role="tabpanel">
                        @if (context.surface()) {
                            <p class="surface">{{ context.surface() }}</p>
                        }
                        @if (context.open(); as object) {
                            <div class="object">
                                @if (object.icon) { <app-icon [name]="object.icon" size="sm" /> }
                                <span class="object-main">
                                    <span class="object-title">{{ object.title }}</span>
                                    @if (object.detail) { <span class="object-detail">{{ object.detail }}</span> }
                                </span>
                            </div>
                        }
                        @if (context.selection().length > 1) {
                            <p class="note">{{ t().shell.inspector.selected(context.selection().length) }}</p>
                        }
                        @if (context.properties().length) {
                            <dl class="props">
                                @for (property of context.properties(); track property.label) {
                                    <div class="prop">
                                        <dt>
                                            {{ property.label }}
                                            @if (property.protected) {
                                                <app-icon name="lock" size="xs"
                                                          [label]="t().shell.inspector.protectedField" />
                                            }
                                        </dt>
                                        <dd>{{ property.value }}</dd>
                                    </div>
                                }
                            </dl>
                        } @else if (!context.open()) {
                            <p class="empty">{{ t().shell.inspector.noObject }}</p>
                        }
                    </div>
                } @else {
                    <div id="cedar-inspector-ai" class="body" role="tabpanel">
                        <section class="scope" [attr.aria-label]="t().shell.inspector.scopeTitle">
                            <p class="scope-row">
                                <span class="scope-key">{{ t().shell.inspector.scopeObjects }}</span>
                                <span class="scope-val">{{ scopeLabel() }}</span>
                            </p>
                            <p class="scope-row">
                                <span class="scope-key">{{ t().shell.inspector.scopeTask }}</span>
                                <span class="scope-val" [class.muted]="!task().trim()">
                                    {{ task().trim() || t().shell.inspector.scopeTaskEmpty }}
                                </span>
                            </p>
                            <p class="scope-row">
                                <span class="scope-key">{{ t().shell.inspector.scopeProtected }}</span>
                                <span class="scope-val" [class.muted]="!protectedLabel()">
                                    {{ protectedLabel() || t().shell.inspector.scopeProtectedNone }}
                                </span>
                            </p>
                        </section>

                        <label class="field">
                            <span class="field-label">{{ t().shell.inspector.taskLabel }}</span>
                            <textarea class="task" rows="3" [ngModel]="task()"
                                      (ngModelChange)="task.set($event)"
                                      [placeholder]="t().shell.inspector.taskPlaceholder"></textarea>
                        </label>

                        <h3 class="sub">{{ t().shell.inspector.actionsTitle }}</h3>
                        @if (aiCommands().length) {
                            <ul class="actions">
                                @for (command of aiCommands(); track command.id) {
                                    <li>
                                        <button type="button" class="action"
                                                [disabled]="!commands.isEnabled(command) || !scopeCount()"
                                                (click)="runAi(command)">
                                            <app-icon [name]="command.icon ?? 'sparkle'" size="sm" />
                                            <span>{{ command.label }}</span>
                                        </button>
                                    </li>
                                }
                            </ul>
                        } @else {
                            <p class="empty">{{ t().shell.inspector.noActions }}</p>
                        }

                        <p class="foot">
                            <a routerLink="/ai" class="link">
                                <app-icon name="clock" size="xs" />
                                {{ t().shell.inspector.openHistory(operations.operations().length) }}
                            </a>
                        </p>
                        <app-button variant="paper" size="sm" link="/ai">
                            {{ t().shell.inspector.reviewChanges }}
                        </app-button>
                    </div>
                }
            </aside>
        }
    `,
    styles: [`
        :host {
            display: flex;
            align-items: stretch;
            flex: none;
        }

        .handle {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 20px;
            padding: 0;
            border: 0;
            border-left: 1px solid var(--border);
            background: var(--surface);
            color: var(--t2);
            cursor: pointer;
        }

        .handle:hover { background: var(--hover); color: var(--text); }
        .handle:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }

        .grip {
            width: 4px;
            background: var(--border);
            cursor: col-resize;
            touch-action: none;
        }

        .grip:hover, .grip:focus-visible { background: var(--accent); outline: none; }

        .rail {
            display: flex;
            flex-direction: column;
            width: var(--inspector-w, 320px);
            min-height: 0;
            background: var(--surface);
            border-left: 1px solid var(--border);
            overflow: hidden;
        }

        .head { border-bottom: 1px solid var(--border); }

        .tabs { display: flex; }

        .tab {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: 5px;
            flex: 1;
            min-height: 34px;
            border: 0;
            border-bottom: 2px solid transparent;
            background: transparent;
            color: var(--t2);
            font: inherit;
            font-size: var(--fs-12);
            cursor: pointer;
        }

        .tab:hover { background: var(--hover); }
        .tab.on { border-bottom-color: var(--accent); color: var(--accent); }
        .tab:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }

        .body {
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
            padding: var(--space-3);
            overflow-y: auto;
        }

        .surface {
            margin: 0;
            color: var(--t3);
            font-size: var(--fs-caption);
            letter-spacing: .05em;
            text-transform: uppercase;
        }

        .object { display: flex; align-items: flex-start; gap: var(--space-2); }
        .object-main { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
        .object-title { font-size: var(--fs-ui); font-weight: 600; }
        .object-detail { color: var(--t2); font-size: var(--fs-meta); }

        .props { display: flex; flex-direction: column; gap: var(--space-2); margin: 0; }
        .prop { display: flex; flex-direction: column; gap: 2px; }
        .prop dt {
            display: flex;
            align-items: center;
            gap: 4px;
            color: var(--t2);
            font-size: var(--fs-meta);
        }
        .prop dd { margin: 0; font-size: var(--fs-ui); overflow-wrap: anywhere; }

        .scope {
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
            padding: var(--space-2);
            border: 1px solid var(--abord);
            border-radius: var(--radius-md);
            background: var(--asoft);
        }

        .scope-row { display: flex; flex-direction: column; gap: 1px; margin: 0; }
        .scope-key { color: var(--t2); font-size: var(--fs-meta); }
        .scope-val { font-size: var(--fs-ui); overflow-wrap: anywhere; }
        .scope-val.muted { color: var(--t3); }

        .field { display: flex; flex-direction: column; gap: var(--space-1); }
        .field-label { color: var(--t2); font-size: var(--fs-meta); }

        .task {
            width: 100%;
            padding: var(--space-2);
            border: 1px solid var(--border);
            border-radius: var(--radius-field);
            background: var(--sheet);
            color: var(--text);
            font: inherit;
            font-size: var(--fs-ui);
            resize: vertical;
        }

        .sub { margin: 0; color: var(--t2); font-size: var(--fs-meta); font-weight: 600; }

        .actions { display: flex; flex-direction: column; gap: var(--space-1); margin: 0; padding: 0; list-style: none; }

        .action {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            width: 100%;
            min-height: var(--hit-surface, 34px);
            padding: var(--space-1) var(--space-2);
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            background: var(--sheet);
            color: var(--text);
            font: inherit;
            font-size: var(--fs-ui);
            text-align: left;
            cursor: pointer;
        }

        .action:hover:not(:disabled) { background: var(--hover); }
        .action:disabled { color: var(--t3); cursor: default; }

        .empty, .note { margin: 0; color: var(--t2); font-size: var(--fs-ui); }

        .foot { margin: 0; }
        .link { display: inline-flex; align-items: center; gap: 4px; color: var(--t2); font-size: var(--fs-meta); }

        /* Below a tablet the rail would leave the page nothing, and a handle that toggles an
           invisible panel is worse than no handle — the whole thing withdraws. */
        @media (max-width: 1100px) {
            :host { display: none; }
        }
    `],
})
export class InspectorRailComponent {
    protected readonly context = inject(WorkspaceContextService);
    protected readonly commands = inject(CommandsService);
    protected readonly operations = inject(AiOperationsService);
    private readonly appearance = inject(AppearanceService);
    protected readonly t = inject(LocaleService).t;

    protected readonly min = INSPECTOR_WIDTH_MIN;
    protected readonly max = INSPECTOR_WIDTH_MAX;

    protected readonly tabs = [
        { id: 'properties' as const, icon: 'list' as const },
        { id: 'ai' as const, icon: 'sparkle' as const },
    ];

    protected readonly task = signal('');

    readonly open = computed(() => this.appearance.prefs().inspectorOpen);
    protected readonly width = computed(() => this.appearance.prefs().inspectorWidth);
    /** A custom property takes no unit suffix from a binding — the unit travels in the value. */
    protected readonly widthPx = computed(() => `${this.width()}px`);
    protected readonly activeTab = computed(() => this.appearance.prefs().inspectorTab);

    protected readonly scopeCount = computed(() => this.context.scope().length);

    protected readonly scopeLabel = computed(() => {
        const scope = this.context.scope();
        if (!scope.length) return this.t().shell.inspector.scopeEmpty;
        if (scope.length === 1) return scope[0].title;
        return this.t().shell.inspector.scopeCount(scope.length);
    });

    protected readonly protectedLabel = computed(() => this.context.protectedFields().join(', '));

    protected readonly aiCommands = computed<readonly AppCommand[]>(() =>
        this.commands.all().filter(command => command.ai));

    toggle(): void {
        this.appearance.preview({ inspectorOpen: !this.open() });
        void this.appearance.commit();
    }

    selectTab(tab: InspectorTab): void {
        this.appearance.preview({ inspectorTab: tab });
        void this.appearance.commit();
    }

    runAi(command: AppCommand): void {
        const scope = this.context.scope();
        if (!scope.length) return;
        this.operations.start({
            kind: 'edit',
            task: this.task().trim() || command.label,
            scope,
            protectedFields: this.context.protectedFields(),
        });
        this.commands.run(command.id);
    }

    startDrag(event: PointerEvent): void {
        event.preventDefault();
        const startX = event.clientX;
        const startWidth = this.width();
        const target = event.target as HTMLElement;
        target.setPointerCapture(event.pointerId);

        // The rail is on the right, so dragging left widens it.
        const move = (moveEvent: PointerEvent) =>
            this.appearance.preview({ inspectorWidth: clampInspectorWidth(startWidth + (startX - moveEvent.clientX)) });
        const up = () => {
            target.removeEventListener('pointermove', move);
            target.removeEventListener('pointerup', up);
            void this.appearance.commit();
        };
        target.addEventListener('pointermove', move);
        target.addEventListener('pointerup', up);
    }

    onGripKey(event: KeyboardEvent): void {
        const step = event.shiftKey ? 32 : 8;
        if (event.key === 'ArrowLeft') {
            event.preventDefault();
            this.appearance.preview({ inspectorWidth: clampInspectorWidth(this.width() + step) });
            void this.appearance.commit();
        } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            this.appearance.preview({ inspectorWidth: clampInspectorWidth(this.width() - step) });
            void this.appearance.commit();
        }
    }
}
