import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { avatarFill } from '../core/avatar-color.util';
import { IconComponent } from '../shared/icon.component';

export interface SidebarProject {
    id: string;
    name: string;
    kind: string;
    link: string | readonly unknown[];
}

// The one control that reads CurrentProjectService's memory (ADR-186, ADR-221): a menu button over
// a panel of anchors, drawn as the sidebar's card or as the editor top bar's inline name.
@Component({
    selector: 'app-project-switcher',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [NgTemplateOutlet, RouterLink, IconComponent],
    host: {
        'data-surface': 'paper',
        '[class.is-inline]': "variant() === 'inline'",
        '(document:click)': 'onDocumentClick($event)',
        '(document:keydown.escape)': 'onEscape()',
    },
    template: `
        @if (projects().length) {
            <button #trigger type="button" class="side-project" aria-haspopup="true"
                    [attr.aria-expanded]="open()" [attr.title]="hint() || null" (click)="toggle()">
                <ng-container *ngTemplateOutlet="face" />
                <app-icon name="caret-down" size="xs" />
            </button>
            <div #panel class="side-project-panel" role="group" [attr.aria-label]="hint() || null" [hidden]="!open()">
                @for (p of projects(); track p.id) {
                    <a class="side-project-item" [class.is-on]="p.id === project()?.id" [routerLink]="p.link"
                       [attr.aria-current]="p.id === project()?.id ? 'true' : null">{{ p.name }}</a>
                }
            </div>
        } @else {
            <a class="side-project" [routerLink]="fallbackLink()" [attr.title]="hint() || null">
                <ng-container *ngTemplateOutlet="face" />
            </a>
        }

        <ng-template #face>
            @if (variant() === 'card') {
                @if (project()?.id) {
                    <span class="side-project-tile" [style.background]="fill()">{{ initials() }}</span>
                } @else {
                    <span class="side-project-tile is-hub"><app-icon name="folder-open" size="sm" /></span>
                }
            }
            <span class="side-project-text">
                <span class="side-project-name">{{ name() }}</span>
                @if (variant() === 'card' && project()?.kind) {
                    <span class="side-project-kind">{{ project()!.kind }}</span>
                }
            </span>
        </ng-template>
    `,
    styles: [`
        :host { position: relative; display: block; }

        .side-project {
            display: flex;
            align-items: center;
            gap: 10px;
            box-sizing: border-box;
            width: 100%;
            min-height: var(--hit-touch);
            padding: 0 10px;
            border: 1px solid var(--border);
            border-radius: 6px;
            background: var(--sheet);
            color: var(--text);
            font-family: var(--font-sans);
            text-align: left;
            text-decoration: none;
            cursor: pointer;
        }

        .side-project:hover { background: var(--alt); }

        .side-project app-icon { color: var(--t3); }

        .side-project-tile {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            flex: none;
            width: 26px;
            height: 26px;
            border-radius: 5px;
            color: var(--avatar-ink);
            font-size: var(--fs-11);
            font-weight: 700;
        }

        .side-project-tile.is-hub { background: var(--surface); color: var(--t2); }

        .side-project-text {
            display: flex;
            flex: 1;
            flex-direction: column;
            min-width: 0;
        }

        .side-project-name {
            overflow: hidden;
            font-size: var(--fs-14);
            font-weight: 700;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        .side-project-kind {
            font-size: var(--fs-12);
            color: var(--t3);
        }

        :host(.is-inline) .side-project {
            width: auto;
            min-height: var(--hit-target);
            border: none;
            background: none;
        }

        :host(.is-inline) .side-project:hover { background: var(--hover); }
        :host(.is-inline) .side-project-name { font-size: var(--fs-15); }

        .side-project-panel {
            position: absolute;
            top: calc(100% + var(--space-1));
            left: 0;
            z-index: 30;
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
            min-width: 100%;
            padding: var(--space-2);
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            background: var(--sheet);
            box-shadow: var(--shadow-paper);
        }

        .side-project-panel[hidden] { display: none; }

        .side-project-item {
            display: flex;
            align-items: center;
            box-sizing: border-box;
            min-height: var(--hit-target);
            padding: 0 var(--space-3);
            border-radius: var(--radius-sm);
            color: var(--text);
            font-size: var(--fs-14);
            text-decoration: none;
            white-space: nowrap;
        }

        .side-project-item:hover { background: var(--hover); }
        .side-project-item.is-on { font-weight: 700; background: var(--hover); }
    `],
})
export class ProjectSwitcherComponent {
    readonly project = input<SidebarProject | null>(null);
    readonly projects = input<readonly SidebarProject[]>([]);
    readonly hint = input('');
    readonly variant = input<'card' | 'inline'>('card');
    readonly fallbackName = input('');
    readonly fallbackLink = input<string | readonly unknown[]>('/projects');

    private readonly el = inject(ElementRef<HTMLElement>);
    private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');

    protected readonly open = signal(false);

    protected readonly fill = computed(() => avatarFill(this.project()?.id ?? null));
    /** A project whose name the list has not answered yet says so rather than borrowing a word. */
    protected readonly name = computed(() => {
        const p = this.project();
        if (!p) return this.fallbackName();
        return p.name || (p.id ? '…' : this.fallbackName());
    });
    protected readonly initials = computed(() => {
        const name = this.project()?.name ?? '';
        const words = name.split(/\s+/).filter(Boolean);
        const letters = words.length > 1 ? words[0][0] + words[1][0] : name.slice(0, 2);
        return letters.toUpperCase();
    });

    toggle(): void { this.open.set(!this.open()); }

    onEscape(): void {
        if (!this.open()) return;
        const held = this.el.nativeElement.contains(document.activeElement);
        this.open.set(false);
        if (held) this.trigger()?.nativeElement.focus();
    }

    onDocumentClick(event: MouseEvent): void {
        if (!this.open()) return;
        const target = event.target instanceof Element ? event.target : null;
        if (target && this.trigger()?.nativeElement.contains(target)) return;
        this.open.set(false);
    }
}
