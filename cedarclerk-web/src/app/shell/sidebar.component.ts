import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Params, RouterLink } from '@angular/router';
import { indexTabBadgeLabel } from '../bench/chrome/index-tabs.component';
import { AccountMenuComponent } from '../shared/account-menu.component';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';
import { SidebarProject } from './project-switcher.component';

export type { SidebarProject } from './project-switcher.component';

export interface NavItem {
    id: string;
    label: string;
    icon: IconName;
    link: string | readonly unknown[];
    queryParams?: Params;
    count?: number;
    countTitle?: string;
    title?: string;
}

export interface NavGroup {
    id: 'write' | 'plan' | 'ship' | 'library';
    label: string;
    items: readonly NavItem[];
}

export interface SidebarUser {
    name: string;
    avatarUrl: string | null;
    initial: string;
}

// The navigation (ADR-239 clause 4): a paper column with one hairline edge. Every item is an anchor
// and the current one carries aria-current. The 92px rail is the same list drawn as icon over
// caption. Both modes keep the same project context and order. Workspace doors live in the
// project switcher, while the persistent width choice lives with Appearance.
@Component({
    selector: 'app-sidebar',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [NgTemplateOutlet, RouterLink, IconComponent, AccountMenuComponent],
    host: {
        'data-surface': 'paper',
        '[class.is-rail]': "mode() === 'rail'",
    },
    template: `
        <div class="side-head">
            @if (toggleLabel()) { <button type="button" class="side-toggle" [attr.aria-label]="toggleLabel()" [attr.aria-expanded]="mode() === 'full'" (click)="toggled.emit()"><app-icon [name]="mode() === 'full' ? 'caret-left' : 'caret-right'" size="sm" /></button> }
        </div>

        <nav class="side-nav" [attr.aria-label]="navLabel() || null">
            @for (group of groups(); track group.id) {
                @if (group.items.length) {
                    <div class="side-group">
                        @if (mode() === 'full') { <div class="label side-label">{{ group.label }}</div> }
                        @for (item of group.items; track item.id) {
                            <ng-container *ngTemplateOutlet="entry; context: { $implicit: item }" />
                        }
                    </div>
                }
            }
            <span class="side-spacer"></span>
        </nav>

        <div class="side-user">
            <app-account-menu [face]="mode() === 'full' ? 'row' : 'avatar'" />
            <a class="side-bell" routerLink="/posts" [attr.title]="alertsTitle() || null"
               [attr.aria-label]="alertsTitle() || null">
                <app-icon name="chat-teardrop" size="sm" />
                @if (alerts() > 0) { <span class="side-dot" aria-hidden="true"></span> }
            </a>
        </div>

        <ng-template #entry let-item>
            <a class="side-item" [class.is-on]="item.id === activeId()" [routerLink]="item.link"
               [queryParams]="item.queryParams || null"
               [attr.aria-current]="item.id === activeId() ? 'page' : null"
               [attr.title]="item.title || null" (click)="picked.emit(item.id)">
                <app-icon [name]="item.icon" size="sm" />
                <span class="side-text">{{ item.label }}</span>
                @if (mode() === 'full' && countOf(item); as count) {
                    <span class="side-count" [attr.title]="item.countTitle || null"
                          [attr.aria-label]="item.countTitle ? count + ' ' + item.countTitle : null">{{ count }}</span>
                }
            </a>
        </ng-template>
    `,
    styles: [`
        :host {
            display: flex;
            flex: none;
            flex-direction: column;
            box-sizing: border-box;
            width: var(--sidebar-w);
            min-height: 0;
            background: var(--surface);
            border-right: 1px solid var(--border);
            color: var(--text);
            font-family: var(--font-sans);
        }

        .side-group + .side-group { border-top: 1px solid var(--border); padding-top: var(--space-3); }

        .side-head {
            display: flex;
            align-items: center;
            flex: none;
            box-sizing: border-box;
            min-height: var(--hit-touch);
            justify-content: flex-end;
            padding: 0 var(--space-2) 0 var(--space-4);
        }

        .side-toggle { display: grid; place-items: center; min-width: var(--hit-chrome); min-height: var(--hit-chrome); border: 0; background: transparent; color: var(--text); cursor: pointer; }



        .side-nav {
            display: flex;
            flex: 1;
            flex-direction: column;
            min-height: 0;
            padding: 0 var(--space-3) var(--space-3);
            overflow: hidden auto;
            overscroll-behavior: contain;
        }

        .side-group {
            display: flex;
            flex-direction: column;
            gap: 2px;
        }

        .side-group + .side-group .side-label { padding-top: var(--space-5); }

        .side-label { padding: var(--space-2) var(--space-3) 6px; color: var(--t3); }

        .side-spacer { flex: 1; min-height: var(--space-4); }

        .side-item {
            display: flex;
            align-items: center;
            gap: 10px;
            box-sizing: border-box;
            min-height: var(--space-7);
            padding: 0 var(--space-3);
            border-radius: var(--radius-sm);
            color: var(--t2);
            font-size: var(--fs-14);
            font-weight: 600;
            text-decoration: none;
        }

        .side-item app-icon { color: var(--t3); }
        .side-item:hover { background: var(--hover); color: var(--text); }
        .side-item:hover app-icon { color: var(--text); }

        .side-item.is-on,
        .side-item.is-on:hover { background: var(--accent); color: var(--text-on-pine); }
        .side-item.is-on app-icon { color: var(--text-on-pine); }

        .side-text {
            flex: 1;
            min-width: 0;
            overflow: hidden;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        .side-count {
            font-size: var(--fs-12);
            font-weight: 600;
            font-variant-numeric: tabular-nums;
            opacity: .7;
        }

        .side-user {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            flex: none;
            box-sizing: border-box;
            min-height: 52px;
            padding: 0 var(--space-3) 0 var(--space-4);
            border-top: 1px solid var(--border);
        }

        .side-user app-account-menu { flex: 1; min-width: 0; }

        .side-bell {
            position: relative;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            flex: none;
            width: var(--hit-target);
            height: var(--hit-target);
            border-radius: var(--radius-sm);
            color: var(--t3);
        }

        .side-bell:hover { background: var(--hover); color: var(--text); }

        .side-dot {
            position: absolute;
            top: 7px;
            right: 7px;
            width: 7px;
            height: 7px;
            border-radius: 50%;
            background: var(--danger);
        }

        :host(.is-rail) { width: var(--sidebar-rail-w); }
        :host(.is-rail) .side-head { justify-content: center; gap: var(--space-1); padding: 0 var(--space-2); }
        :host(.is-rail) .side-nav { align-items: center; padding: var(--space-3) 0; }
        :host(.is-rail) .side-group { align-items: center; gap: var(--space-1); }
        :host(.is-rail) .side-group + .side-group { margin-top: var(--space-2); padding-top: var(--space-2); border-top: 1px solid var(--border); }

        :host(.is-rail) .side-item {
            flex-direction: column;
            justify-content: center;
            gap: 6px;
            width: 72px;
            height: 64px;
            padding: 0;
            font-size: var(--fs-12);
        }

        :host(.is-rail) .side-text { flex: none; max-width: 100%; text-align: center; }
        :host(.is-rail) .side-user { justify-content: center; padding: 0 var(--space-2); }
        :host(.is-rail) .side-user app-account-menu { flex: none; }

        @media (pointer: coarse) {
            :host([data-surface="paper"]) .side-bell { width: var(--hit-touch); height: var(--hit-touch); }
            :host([data-surface="paper"].is-rail) .side-user { gap: var(--space-1); padding-inline: 0; }
        }
    `],
})
export class SidebarComponent {
    readonly mode = input<'full' | 'rail'>('full');
    readonly groups = input<readonly NavGroup[]>([]);
    readonly activeId = input('');
    readonly project = input<SidebarProject | null>(null);
    readonly projects = input<readonly SidebarProject[]>([]);
    readonly projectHint = input('');
    readonly user = input<SidebarUser>();
    readonly alerts = input(0);
    readonly navLabel = input('');
    readonly brand = input('');
    readonly brandLabel = input('');
    readonly allProjectsLabel = input('');
    readonly alertsTitle = input('');
    readonly picked = output<string>();
    readonly toggleLabel = input('');
    readonly toggled = output<void>();

    countOf(item: NavItem): string {
        return indexTabBadgeLabel(item.count);
    }

}
