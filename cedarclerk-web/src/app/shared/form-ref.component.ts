import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideClipboardList as ClipboardList } from '@lucide/angular';
import { FormPreset } from '../core/form-presets.service';
import { RegistrationForm } from '../core/drafts.service';

// Design review (Claude Design, 28.07.2026) — the private-post registration form is defined on
// the Forms tab, assigned on the Posts tab, and re-picked in the Export modal: three real,
// distinct actions, but three near-identical bare <select>s made it read as three separate
// settings rather than one object referenced three times. This is the one shared shape for the
// "reference" half (Posts tab, Export modal) — Forms itself is the authoring surface and stays
// as-is. Every string is passed in by the caller rather than owned here, since Posts Manager and
// the Export modal each have their own already-translated wording for the same states.
@Component({
    selector: 'app-form-ref',
    imports: [RouterLink, ClipboardList],
    templateUrl: 'form-ref.component.html',
    styleUrls: ['form-ref.component.css'],
})
export class FormRefComponent {
    regForm = input<RegistrationForm | null>(null);
    presets = input<FormPreset[]>([]);
    languages = input<string[]>([]);
    primaryLanguage = input('ru');
    busy = input(false);

    onLabel = input.required<string>();
    offLabel = input.required<string>();
    changeLabel = input.required<string>();
    noFormLabel = input.required<string>();
    clearLabel = input.required<string>();
    languagesLabel = input.required<string>();
    noPresetsLabel = input.required<string>();
    // Optional: a different hint for "form already attached, preset library just empty" — only
    // the Posts tab distinguishes this from "nothing at all yet" (DB1). Falls back to
    // noPresetsLabel when absent (the Export modal's simpler two-state version).
    noPresetsSavedLabel = input<string | null>(null);
    createLabel = input.required<string>();
    manageLabel = input.required<string>();

    // Route-based "manage" link (the Export modal lives on a different page from /posts) — when
    // absent, the `manage` output fires instead (Posts Manager, where it's a same-page tab switch).
    manageRoute = input<any[] | null>(null);
    manageQueryParams = input<Record<string, string> | null>(null);

    pick = output<string>();
    manage = output<void>();

    emptyHint(): string {
        return (this.regForm() && this.noPresetsSavedLabel()) || this.noPresetsLabel();
    }
}
