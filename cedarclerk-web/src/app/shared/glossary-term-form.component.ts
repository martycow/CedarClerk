import { Component, Input, OnInit, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '../core/i18n/locale.service';
import { AssetsService } from '../core/assets.service';
import { GlossaryService, GlossaryTermInput } from '../core/glossary.service';
import { AuthService } from '../core/auth.service';
import { CONTENT_LANGUAGES, DEFAULT_PRIMARY_LANGUAGE } from '../core/languages';
import { IconComponent } from './icon.component';
import { ButtonComponent } from '../bench/forms/button.component';

/**
 * The glossary term form, in one place (Marty, 01.08.2026: "the menu must be exactly the one in
 * Glossary"). It is used by the /glossary page and by the editor's create-from-selection modal —
 * two copies of six fields would have drifted the first time either grew a seventh.
 *
 * The parent owns saving: the glossary page updates an existing row, the editor creates one and
 * closes a modal, and neither is this component's business.
 */
@Component({
    selector: 'app-glossary-term-form',
    imports: [FormsModule, IconComponent, ButtonComponent],
    template: `
        <div class="form-row-2">
            <label class="form-field">
                <span class="form-field-label">{{ t().glossary.term }}</span>
                <input class="chat-input" [(ngModel)]="term" maxlength="80"
                       [placeholder]="t().glossary.termPlaceholder">
            </label>
            <label class="form-field">
                <span class="form-field-label">{{ t().glossary.language }}</span>
                <!--T-350 — a locked language is visible and marked, never silently absent.-->
                <select class="chat-input" [value]="language()" (change)="language.set($any($event.target).value)">
                    @for (l of contentLanguages; track l) {
                    <option [value]="l" [disabled]="!auth.hasContentLanguage(l)">
                        {{ l.toUpperCase() }}{{ auth.hasContentLanguage(l) ? '' : ' · ' + t().planLock.pro }}
                    </option>
                    }
                </select>
            </label>
        </div>

        <label class="form-field">
            <span class="form-field-label">{{ t().glossary.description }}</span>
            <textarea class="chat-input" rows="3" [(ngModel)]="description" maxlength="1000"
                      [placeholder]="t().glossary.descriptionPlaceholder"></textarea>
        </label>

        <label class="form-field">
            <span class="form-field-label">{{ t().glossary.aliases }}</span>
            <input class="chat-input" [(ngModel)]="aliases" maxlength="400"
                   [placeholder]="t().glossary.aliasesPlaceholder">
            <p class="field-hint-inline">{{ t().glossary.aliasesHint }}</p>
        </label>

        <!--T-040 — proposes the Russian forms of the term. They land in the field above, where the
        author reads them and deletes what is wrong: a suffix rule may suggest, never decide.-->
        @if (canSuggest()) {
        <app-button class="suggest" variant="paper" size="sm" (clicked)="suggestForms()" [disabled]="suggesting()">
            @if (suggesting()) { <app-icon name="arrow-clockwise" size="sm" class="spin"></app-icon> }
            @else { <app-icon name="sparkle" size="sm"></app-icon> }
            {{ t().glossary.suggestForms }}
        </app-button>
        @if (suggestedNothing()) { <p class="field-hint-inline">{{ t().glossary.suggestNothing }}</p> }
        }

        <label class="term-case-toggle">
            <input type="checkbox" [checked]="caseSensitive()"
                   (change)="caseSensitive.set($any($event.target).checked)">
            <span>
                {{ t().glossary.caseSensitive }}
                <span class="field-hint-inline">{{ t().glossary.caseSensitiveHint }}</span>
            </span>
        </label>

        <div class="form-field">
            <span class="form-field-label">{{ t().glossary.image }}</span>
            @if (imageUrl(); as url) {
            <div class="term-image-row">
                <img [src]="url" alt="">
                <app-button variant="paper" size="sm" [title]="t().common.delete" (clicked)="imageUrl.set(null)">
                    <app-icon name="x" size="sm" [label]="t().common.delete"></app-icon>
                </app-button>
            </div>
            }
            <label class="image-pick">
                @if (uploading()) { <app-icon name="arrow-clockwise" size="sm" class="spin"></app-icon> }
                @else { <app-icon name="image" size="sm"></app-icon> }
                {{ imageUrl() ? t().glossary.replaceImage : t().glossary.addImage }}
                <input type="file" accept="image/*" hidden (change)="onImageChosen($event)">
            </label>
            @if (uploadError()) { <p class="channel-error">{{ uploadError() }}</p> }
        </div>
    `,
    styleUrl: './glossary-term-form.component.css',
})
export class GlossaryTermFormComponent implements OnInit {
    private locale = inject(LocaleService);
    private assets = inject(AssetsService);
    private glossary = inject(GlossaryService);
    auth = inject(AuthService);
    t = this.locale.t;
    readonly contentLanguages = CONTENT_LANGUAGES;

    @Input() initialTerm = '';
    @Input() initialDescription = '';
    @Input() initialAliases = '';
    @Input() initialImageUrl: string | null = null;
    @Input() initialLanguage = DEFAULT_PRIMARY_LANGUAGE;
    @Input() initialCaseSensitive = false;

    /** Emitted on every edit, so the parent's save button can enable and disable itself. */
    valueChange = output<GlossaryTermInput>();

    term = '';
    description = '';
    aliases = '';
    language = signal(DEFAULT_PRIMARY_LANGUAGE);
    caseSensitive = signal(false);
    imageUrl = signal<string | null>(null);
    uploading = signal(false);
    uploadError = signal('');

    ngOnInit() {
        this.term = this.initialTerm;
        this.description = this.initialDescription;
        this.aliases = this.initialAliases;
        this.language.set(this.initialLanguage);
        this.caseSensitive.set(this.initialCaseSensitive);
        this.imageUrl.set(this.initialImageUrl);
    }

    /** What the parent should send. Trimming here so both callers cannot forget it differently. */
    value(): GlossaryTermInput {
        return {
            term: this.term.trim(),
            description: this.description.trim(),
            aliases: this.aliases.trim(),
            imageUrl: this.imageUrl(),
            language: this.language(),
            isCaseSensitive: this.caseSensitive(),
        };
    }

    isComplete = computed(() => true);

    canSave(): boolean {
        return this.term.trim().length > 0 && this.description.trim().length > 0;
    }

    suggesting = signal(false);
    suggestedNothing = signal(false);

    /** Russian only: the other content languages either do not inflect this way or need real morphology. */
    canSuggest(): boolean {
        return this.language() === 'ru' && this.term.trim().length > 2;
    }

    async suggestForms() {
        this.suggesting.set(true);
        this.suggestedNothing.set(false);
        try {
            const { forms } = await this.glossary.suggestForms(this.term.trim(), this.language());
            if (forms.length === 0) { this.suggestedNothing.set(true); return; }

            // Merged, not replaced: whatever the author already wrote is theirs.
            const existing = this.aliases.split(',').map(a => a.trim()).filter(Boolean);
            const merged = [...existing];
            for (const form of forms) {
                if (!merged.some(a => a.toLowerCase() === form.toLowerCase())) merged.push(form);
            }
            this.aliases = merged.join(', ');
        } catch {
            this.suggestedNothing.set(true);
        } finally {
            this.suggesting.set(false);
        }
    }

    // The image goes through the ordinary asset upload, like the avatar (IF1): same whitelist,
    // same quota, same public /media serving, no second pipeline.
    async onImageChosen(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const file = input.files?.[0];
        if (!file) return;
        this.uploading.set(true);
        this.uploadError.set('');
        try {
            const { url } = await this.assets.upload(file);
            this.imageUrl.set(url);
        } catch {
            this.uploadError.set(this.t().glossary.imageFailed);
        } finally {
            this.uploading.set(false);
            input.value = '';
        }
    }
}
