import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormPreset } from '../core/form-presets.service';
import { RegistrationForm } from '../core/drafts.service';
import { IconComponent } from './icon.component';
import { ButtonComponent } from '../bench/forms/button.component';

// A private post's registration form is authored on the Forms page and referenced from the
// Publishing Manager and the editor's Publish tab. This is the one shape for that reference; every
// string is the caller's, since each screen has its own wording for the same states.
@Component({
    selector: 'app-form-ref',
    imports: [IconComponent, RouterLink, ButtonComponent],
    templateUrl: 'form-ref.component.html',
    styleUrls: ['form-ref.component.css'],
})
export class FormRefComponent {
    regForm = input<RegistrationForm | null>(null);
    presets = input<FormPreset[]>([]);
    languages = input<string[]>([]);
    primaryLanguage = input('ru');
    busy = input(false);
    /** The select's accessible name — its first option is a prompt, not a label. */
    fieldLabel = input.required<string>();

    onLabel = input.required<string>();
    offLabel = input.required<string>();
    changeLabel = input.required<string>();
    noFormLabel = input.required<string>();
    clearLabel = input.required<string>();
    languagesLabel = input.required<string>();
    noPresetsLabel = input.required<string>();
    // Optional: a different hint for "form already attached, preset library just empty". Falls
    // back to noPresetsLabel when absent.
    noPresetsSavedLabel = input<string | null>(null);
    createLabel = input.required<string>();
    manageLabel = input.required<string>();

    pick = output<string>();

    emptyHint(): string {
        return (this.regForm() && this.noPresetsSavedLabel()) || this.noPresetsLabel();
    }
}
