import { Component, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from './icon.component';

// T-345 — the one location control, wherever a location is asked for: an input with the profile's
// own location one press away. A document's location is usually the profile's (home) or a named
// trip; the chip covers the first, typing covers the second.
@Component({
    selector: 'app-location-input',
    imports: [FormsModule, IconComponent],
    template: `
        <span class="loc">
            <!--Committed on change (blur/Enter), not per keystroke: the editor row saves straight
            to the server and a location is typed once, not streamed.-->
            <input class="loc-input" type="text" maxlength="120"
                   [placeholder]="placeholder() || t().location.placeholder"
                   [ngModel]="value()" (change)="valueChange.emit($any($event.target).value ?? '')">
            @if (profileChip(); as home) {
                <button type="button" class="loc-chip" (click)="valueChange.emit(home)"
                        [title]="t().location.useProfile(home)">
                    <app-icon name="flag" size="xs" /> {{ home }}
                </button>
            }
        </span>
    `,
    styles: [`
        .loc { display: flex; flex-direction: column; gap: var(--space-1); min-width: 0; }

        .loc-input {
            box-sizing: border-box;
            width: 100%;
            padding: 5px 8px;
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            background: var(--sheet);
            color: var(--text);
            font-family: var(--font-sans);
            font-size: var(--fs-ui);
        }

        .loc-chip {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            align-self: flex-start;
            max-width: 100%;
            padding: 2px var(--space-2);
            border: 1px solid var(--border);
            border-radius: var(--radius-stamp);
            background: var(--alt);
            color: var(--t2);
            font-size: var(--fs-ui);
            cursor: pointer;
            overflow: hidden;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        .loc-chip:hover { background: var(--hover); color: var(--text); }
    `],
})
export class LocationInputComponent {
    private readonly auth = inject(AuthService);
    protected readonly t = inject(LocaleService).t;

    value = input('');
    placeholder = input('');
    valueChange = output<string>();

    /** The profile's location, offered only while the field says something else. */
    protected readonly profileChip = computed(() => {
        const home = this.auth.profileLocation()?.trim();
        return home && home !== this.value().trim() ? home : null;
    });
}
