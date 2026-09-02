import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { LocaleService } from '../../core/i18n/locale.service';
import { IconComponent } from '../../shared/icon.component';

export type PublishStep = 'version' | 'destinations' | 'settings' | 'review';

export interface PublishStepState {
    id: PublishStep;
    done: boolean;
    current: boolean;
}

export const PUBLISH_STEPS: readonly PublishStep[] = ['version', 'destinations', 'settings', 'review'];

/**
 * Done and current are read off the real ticks (ADR-242 clause 5): a version is always picked,
 * destinations are done once one is ticked, settings once every ticked one can run, and review
 * is where the pointer rests when nothing else is left.
 */
export function publishStepStates(facts: { languages: number; anyDestination: boolean; settingsComplete: boolean }): PublishStepState[] {
    const done: Record<PublishStep, boolean> = {
        version: facts.languages > 0,
        destinations: facts.anyDestination,
        settings: facts.anyDestination && facts.settingsComplete,
        review: false,
    };
    const current = PUBLISH_STEPS.find(step => !done[step]) ?? 'review';
    return PUBLISH_STEPS.map(id => ({ id, done: done[id], current: id === current }));
}

// The row of four at the head of the Publish workspace. Each step is a real control: pressing it
// moves focus to the region it names, so the row is navigation and never a drawing of one.
@Component({
    selector: 'app-publish-stepper',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: { 'data-surface': 'paper' },
    template: `
        <ol class="ps" [attr.aria-label]="t().editor.publishTab.stepsLabel">
            @for (step of steps(); track step.id; let i = $index; let last = $last) {
                <li class="ps-item">
                    <button type="button" class="ps-step" [class.is-done]="step.done" [class.is-current]="step.current"
                            [attr.aria-current]="step.current ? 'step' : null" (click)="pick.emit(step.id)">
                        <span class="ps-no" aria-hidden="true">
                            @if (step.done) { <app-icon name="check" size="xs" /> } @else { {{ i + 1 }} }
                        </span>
                        <span class="ps-name">{{ t().editor.publishTab.steps[step.id] }}</span>
                        @if (step.done) { <span class="visually-hidden">· {{ t().editor.publishTab.stepDone }}</span> }
                    </button>
                    @if (!last) { <span class="ps-arrow" aria-hidden="true"><app-icon name="caret-right" size="xs" /></span> }
                </li>
            }
        </ol>
    `,
    styles: [`
        :host { display: block; font-family: var(--font-sans); }

        .ps {
            display: flex;
            align-items: center;
            justify-content: center;
            gap: var(--space-2);
            margin: 0;
            padding: 0;
            list-style: none;
        }

        .ps-item { display: flex; align-items: center; gap: var(--space-2); }

        .ps-step {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            min-height: var(--hit-target);
            padding: 0 var(--space-3);
            border: 1px solid transparent;
            border-radius: var(--radius-sm);
            background: none;
            font-family: inherit;
            font-size: var(--fs-15);
            font-weight: 600;
            color: var(--t2);
            cursor: pointer;
        }

        .ps-step:hover { background: var(--hover); color: var(--text); }
        .ps-step.is-current { color: var(--text); }

        .ps-no {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 24px;
            height: 24px;
            border: 1px solid var(--border);
            border-radius: 50%;
            font-size: var(--fs-12);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            color: var(--t2);
        }

        .ps-step.is-done .ps-no { border-color: var(--ok); background: var(--ok-soft); color: var(--ok); }
        .ps-step.is-current .ps-no { border-color: var(--accent); background: var(--accent); color: var(--text-on-pine); }

        .ps-arrow { display: inline-flex; color: var(--t3); }

        @media (max-width: 759px) {
            .ps { flex-wrap: wrap; justify-content: flex-start; }
            .ps-arrow { display: none; }
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
export class PublishStepperComponent {
    readonly steps = input<readonly PublishStepState[]>([]);
    readonly pick = output<PublishStep>();

    protected readonly t = inject(LocaleService).t;
}
