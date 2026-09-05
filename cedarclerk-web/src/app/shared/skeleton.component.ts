import {
    ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnDestroy, OnInit, PendingTasks, Signal, computed, effect,
    inject, input, signal,
} from '@angular/core';
import { LocaleService } from '../core/i18n/locale.service';

export type SkeletonVariant = 'text' | 'card' | 'avatar' | 'table-row';

// ADR-286 — a skeleton that shows for 80ms reads as a flash, not as loading. The hold lives next
// to the flag rather than in the component, because a component cannot outlive the `@if` that
// drew it. Registered as a pending task so `whenStable()` waits it out in tests.
export const SKELETON_MIN_MS = 300;

export function heldLoading(source: Signal<boolean>, minMs = SKELETON_MIN_MS): Signal<boolean> {
    const held = signal(source());
    const tasks = inject(PendingTasks);
    let shownAt = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let release: (() => void) | undefined;
    const clear = () => {
        if (timer !== undefined) clearTimeout(timer);
        timer = undefined;
        release?.();
        release = undefined;
    };
    inject(DestroyRef).onDestroy(clear);
    effect(() => {
        const on = source();
        clear();
        if (on) {
            shownAt = Date.now();
            held.set(true);
            return;
        }
        const left = shownAt === 0 ? 0 : minMs - (Date.now() - shownAt);
        if (left <= 0) {
            held.set(false);
            return;
        }
        release = tasks.add();
        timer = setTimeout(() => {
            held.set(false);
            clear();
        }, left);
    });
    return held.asReadonly();
}

@Component({
    selector: 'app-skeleton',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { role: 'status', 'aria-busy': 'true', '[attr.aria-label]': 't().common.loading', '[class]': '"v-" + variant()' },
    template: `
        @for (i of rows(); track i) {
            @switch (variant()) {
                @case ('avatar') { <span class="bone avatar"></span> }
                @case ('card') {
                    <div class="card-row">
                        <span class="bone avatar"></span>
                        <div class="card-lines">
                            <span class="bone line w-60"></span>
                            <span class="bone line w-90 thin"></span>
                        </div>
                    </div>
                }
                @case ('table-row') {
                    <div class="table-row">
                        <span class="bone line w-20"></span>
                        <span class="bone line w-35"></span>
                        <span class="bone line w-15"></span>
                        <span class="bone line w-30"></span>
                    </div>
                }
                @default { <span class="bone line" [class.w-60]="$last && rows().length > 1"></span> }
            }
        }
    `,
    styles: [`
        :host { display: flex; flex-direction: column; gap: var(--space-2); width: 100%; }
        :host(.v-avatar) { flex-direction: row; flex-wrap: wrap; width: auto; }

        .bone {
            position: relative;
            display: block;
            overflow: hidden;
            background: var(--alt);
            border-radius: var(--radius-sm);
        }

        .bone::after {
            content: '';
            position: absolute;
            inset: 0;
            transform: translateX(-100%);
            background: linear-gradient(90deg, transparent, var(--hover), transparent);
            animation: shimmer calc(var(--motion-slow) * 5) var(--ease) infinite;
        }

        @keyframes shimmer { to { transform: translateX(100%); } }

        /* The global clamp would leave a 1ms infinite loop running; still is what reduced motion means. */
        @media (prefers-reduced-motion: reduce) {
            .bone::after { animation: none; }
        }

        .line { height: .85em; margin-block: .3em; }
        .thin { height: .7em; }
        .w-15 { width: 15%; }
        .w-20 { width: 20%; }
        .w-30 { width: 30%; }
        .w-35 { width: 35%; }
        .w-60 { width: 60%; }
        .w-90 { width: 90%; }

        .avatar { flex: none; width: var(--avatar-size); height: var(--avatar-size); border-radius: 50%; }

        .card-row { display: flex; align-items: center; gap: var(--space-3); padding: var(--dens-row-y) 0; }
        .card-lines { flex: 1; min-width: 0; }

        .table-row { display: flex; align-items: center; gap: var(--space-4); padding: var(--dens-row-y) 0; }
        .table-row .line { margin-block: 0; }
    `],
})
export class SkeletonComponent implements OnInit, OnDestroy {
    protected readonly t = inject(LocaleService).t;
    variant = input<SkeletonVariant>('text');
    count = input(1);
    protected readonly rows = computed(() => Array.from({ length: Math.max(1, this.count()) }, (_, i) => i));

    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    private restoreRegion?: () => void;

    // The region the skeleton stands in is the thing that is busy, not the placeholder. ngOnInit,
    // not the constructor: inside an `@if` the host is inserted into its parent only after creation.
    ngOnInit() {
        const region = this.host.parentElement;
        if (!region) return;
        const previous = region.getAttribute('aria-busy');
        region.setAttribute('aria-busy', 'true');
        this.restoreRegion = () => {
            if (previous === null) region.removeAttribute('aria-busy');
            else region.setAttribute('aria-busy', previous);
        };
    }

    ngOnDestroy() {
        this.restoreRegion?.();
    }
}
