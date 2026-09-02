import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleService } from '../../core/i18n/locale.service';
import { IconName } from '../../shared/icon-data.generated';
import { IconComponent } from '../../shared/icon.component';

export type CheckTone = 'ok' | 'warn' | 'blocking' | 'setup' | 'unavailable' | 'muted';

/** A real way out of a check: a route the app has, or an action the page owns (emitted by id). */
export interface CheckFix {
    label: string;
    route?: string;
    query?: Record<string, string>;
}

/** One row of the checks column. Only `blocking` stands between the author and the button. */
export interface PreviewCheck {
    id: string;
    label: string;
    detail: string;
    tone: CheckTone;
    fix?: CheckFix;
}

export type CheckGroupId = 'blocking' | 'warnings' | 'ready' | 'setup' | 'unavailable';

export interface CheckGroup {
    id: CheckGroupId;
    tone: CheckTone;
    checks: PreviewCheck[];
}

const GROUP_TONE: Record<CheckGroupId, CheckTone> = {
    blocking: 'blocking', warnings: 'warn', ready: 'ok', setup: 'setup', unavailable: 'unavailable',
};

const TONE_ICON: Record<CheckTone, IconName> = {
    ok: 'check', warn: 'warning', blocking: 'x', setup: 'gear', unavailable: 'clock', muted: 'info',
};

function groupOf(tone: CheckTone): CheckGroupId {
    switch (tone) {
        case 'blocking': return 'blocking';
        case 'warn': return 'warnings';
        case 'setup': return 'setup';
        case 'unavailable': return 'unavailable';
        default: return 'ready';
    }
}

/**
 * The four headings both tabs read checks under (ADR-242 clause 7). Blocking, Warnings and Ready
 * are always drawn — an empty Blocking group is the sentence "nothing stops this" — while Needs
 * setup and Unavailable appear only when they hold something.
 */
export function groupChecks(checks: readonly PreviewCheck[]): CheckGroup[] {
    const groups: CheckGroup[] = (['blocking', 'warnings', 'ready', 'setup', 'unavailable'] as CheckGroupId[])
        .map(id => ({ id, tone: GROUP_TONE[id], checks: [] }));
    for (const check of checks) groups.find(g => g.id === groupOf(check.tone))!.checks.push(check);
    return groups.filter(g => g.checks.length || g.id === 'blocking' || g.id === 'warnings' || g.id === 'ready');
}

@Component({
    selector: 'app-preview-checks',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent, RouterLink],
    host: { 'data-surface': 'paper', class: 'card' },
    template: `
        <div class="pc-head">
            <span class="label">{{ title() }}</span>
            @if (loading()) {
                <span class="pc-running" role="status"><app-icon name="circle-notch" size="sm" />{{ t().editor.previewTab.checks.running }}</span>
            }
        </div>
        <div class="pc-groups">
            @for (group of groups(); track group.id) {
                <section class="pc-group" [attr.data-tone]="group.tone">
                    <h4 class="pc-group-head">
                        <span class="pc-mark" aria-hidden="true"><app-icon [name]="iconOf(group.tone)" size="xs" /></span>
                        <span class="pc-group-name">{{ groupName(group.id) }}</span>
                        <span class="pc-group-count">{{ group.checks.length }}</span>
                    </h4>
                    @if (!group.checks.length) {
                        <p class="pc-clear">{{ t().editor.previewTab.checks.allClear }}</p>
                    } @else {
                        <ul class="pc-list" role="list">
                            @for (check of group.checks; track check.id) {
                                <li class="pc-row" [attr.data-tone]="check.tone" [attr.data-check]="check.id">
                                    <span class="pc-row-mark" aria-hidden="true"><app-icon [name]="iconOf(check.tone)" size="xs" /></span>
                                    <span class="pc-body">
                                        <span class="pc-label">{{ check.label }}</span>
                                        @if (check.detail) { <span class="pc-detail">{{ check.detail }}</span> }
                                    </span>
                                    <span class="visually-hidden">{{ toneWord(check.tone) }}</span>
                                    @if (check.fix; as remedy) {
                                        @if (remedy.route) {
                                            <a class="btn sm pc-fix" [routerLink]="remedy.route" [queryParams]="remedy.query ?? null"
                                               [attr.aria-label]="remedy.label + ' · ' + check.label">{{ remedy.label }}</a>
                                        } @else {
                                            <button type="button" class="btn sm pc-fix" (click)="fix.emit(check.id)"
                                                    [attr.aria-label]="remedy.label + ' · ' + check.label">{{ remedy.label }}</button>
                                        }
                                    }
                                </li>
                            }
                        </ul>
                    }
                </section>
            }
        </div>
        <span class="pc-spacer"></span>
        @if (showAll()) {
            <button type="button" class="btn pc-all" (click)="details.emit()">{{ t().editor.previewTab.checks.viewAll }}</button>
        }
    `,
    styles: [`
        :host {
            display: flex;
            flex-direction: column;
            width: 264px;
            flex: none;
            min-height: 0;
            box-sizing: border-box;
            padding: var(--space-4) var(--space-4) var(--space-3);
            font-family: var(--font-sans);
        }

        .pc-head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); }

        .pc-running { display: inline-flex; align-items: center; gap: var(--space-1); font-size: var(--fs-12); color: var(--t3); }

        .pc-groups { display: flex; flex-direction: column; min-height: 0; margin-top: var(--space-2); overflow: auto; }

        .pc-group { padding: var(--space-3) 0; border-bottom: 1px solid var(--paper-edge); }
        .pc-group:last-child { border-bottom: 0; }

        .pc-group-head { display: flex; align-items: center; gap: var(--space-2); margin: 0; font-size: var(--fs-15); font-weight: 700; }
        .pc-group-count { margin-left: auto; font-size: var(--fs-15); font-weight: 700; font-variant-numeric: tabular-nums; }

        .pc-mark {
            display: flex;
            align-items: center;
            justify-content: center;
            flex: none;
            width: 22px;
            height: 22px;
            border-radius: 50%;
            background: var(--ok-soft);
            color: var(--ok);
        }

        .pc-group[data-tone="warn"] .pc-mark, .pc-group[data-tone="warn"] .pc-group-count { background: var(--warn-soft); color: var(--warn); }
        .pc-group[data-tone="warn"] .pc-group-count { background: none; }
        .pc-group[data-tone="blocking"] .pc-mark { background: var(--danger-soft); color: var(--danger); }
        .pc-group[data-tone="blocking"] .pc-group-count { color: var(--danger); }
        .pc-group[data-tone="setup"] .pc-mark, .pc-group[data-tone="unavailable"] .pc-mark { background: var(--surface); color: var(--t2); }

        .pc-clear { margin: var(--space-1) 0 0 30px; font-size: var(--fs-13); color: var(--t2); }

        .pc-list { display: flex; flex-direction: column; margin: var(--space-1) 0 0; padding: 0; list-style: none; }

        .pc-row {
            display: flex;
            align-items: flex-start;
            gap: var(--space-2);
            padding: var(--space-2) 0 var(--space-2) 30px;
        }

        .pc-row-mark { display: flex; flex: none; margin-top: 2px; color: var(--t3); }
        .pc-row[data-tone="ok"] .pc-row-mark { color: var(--ok); }
        .pc-row[data-tone="warn"] .pc-row-mark { color: var(--warn); }
        .pc-row[data-tone="blocking"] .pc-row-mark { color: var(--danger); }

        .pc-body { display: flex; flex: 1; flex-direction: column; gap: 2px; min-width: 0; }
        .pc-label { font-size: var(--fs-14); font-weight: 600; }
        .pc-detail { font-size: var(--fs-13); color: var(--t2); overflow-wrap: anywhere; }

        .pc-fix { flex: none; }

        .pc-spacer { flex: 1; }

        .pc-all { margin-top: var(--space-3); }

        @media (max-width: 1180px) {
            :host { width: 100%; flex: none; }
            .pc-groups { overflow: visible; }
        }

        .visually-hidden {
            position: absolute;
            width: 1px;
            height: 1px;
            margin: -1px;
            padding: 0;
            border: 0;
            overflow: hidden;
            white-space: nowrap;
            clip-path: inset(50%);
        }
    `],
})
export class PreviewChecksComponent {
    readonly title = input('');
    readonly checks = input<readonly PreviewCheck[]>([]);
    readonly loading = input(false);
    /** The "View all details" button — the Preview tab's way into Publish; Publish itself hides it. */
    readonly showAll = input(true);
    readonly details = output<void>();
    /** A fix that is the page's own action rather than a route: the check's id. */
    readonly fix = output<string>();

    protected readonly t = inject(LocaleService).t;

    protected readonly groups = computed(() => groupChecks(this.checks()));

    iconOf(tone: CheckTone): IconName {
        return TONE_ICON[tone];
    }

    groupName(id: CheckGroupId): string {
        return this.t().editor.previewTab.checks.groups[id];
    }

    toneWord(tone: CheckTone): string {
        const words = this.t().editor.previewTab.checks;
        switch (tone) {
            case 'warn': return words.toneWarn;
            case 'ok': return words.toneOk;
            case 'blocking': return words.toneBlocking;
            case 'setup': return words.toneSetup;
            case 'unavailable': return this.t().editor.publishTab.state.unavailable;
            default: return words.toneInfo;
        }
    }
}
