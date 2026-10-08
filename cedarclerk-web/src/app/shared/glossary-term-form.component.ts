import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '../core/i18n/locale.service';
import { LibraryAsset } from '../core/assets.service';
import {
    GLOSSARY_AI_COST, GlossaryEntry, GlossaryEntryInput, GlossaryService, parseSpellings,
} from '../core/glossary.service';
import { AuthService } from '../core/auth.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CONTENT_LANGUAGES, DEFAULT_PRIMARY_LANGUAGE, endonymOf } from '../core/languages';
import { IconComponent } from './icon.component';
import { MediaPickerComponent } from './media-picker.component';
import { PlanLockComponent } from './plan-lock.component';
import { ButtonComponent } from '../bench/forms/button.component';

interface LanguageFields {
    language: string;
    localizedName: string;
    spellings: string;
    localizedDescription: string;
}

/**
 * The glossary entry form, in one place: the /glossary page and the editor's create-from-selection
 * modal both use it, and two copies would have drifted the first time either grew a field.
 * ADR-320 — one form shows every language of an entry.
 *
 * The parent owns saving: the glossary page updates an existing entry, the editor creates one and
 * closes a modal, and neither is this component's business.
 */
@Component({
    selector: 'app-glossary-term-form',
    imports: [FormsModule, IconComponent, ButtonComponent, MediaPickerComponent, PlanLockComponent],
    templateUrl: './glossary-term-form.component.html',
    styleUrl: './glossary-term-form.component.css',
})
export class GlossaryTermFormComponent implements OnInit {
    private locale = inject(LocaleService);
    private glossary = inject(GlossaryService);
    auth = inject(AuthService);
    t = this.locale.t;
    readonly contentLanguages = CONTENT_LANGUAGES;
    readonly endonymOf = endonymOf;
    readonly cost = GLOSSARY_AI_COST;

    /** The entry being edited; null for a new one. */
    @Input() entry: GlossaryEntry | null = null;
    @Input() initialName = '';
    /** The language a new entry starts with: the one being listed, or the one being written. */
    @Input() initialLanguage = DEFAULT_PRIMARY_LANGUAGE;

    name = '';
    description = '';
    caseSensitive = signal(false);
    imageUrl = signal<string | null>(null);
    languages = signal<LanguageFields[]>([]);
    pickingImage = signal(false);

    ngOnInit() {
        const entry = this.entry;
        if (!entry) {
            this.name = this.initialName;
            this.languages.set([this.blank(this.initialLanguage)]);
            return;
        }
        this.name = entry.name;
        this.description = entry.description;
        this.caseSensitive.set(entry.isCaseSensitive);
        this.imageUrl.set(entry.imageUrl);
        this.languages.set(this.ordered(entry.languages.map(row => ({
            language: row.language,
            localizedName: row.localizedName,
            spellings: row.spellings.join(', '),
            localizedDescription: row.localizedDescription,
        }))));
    }

    private blank(language: string): LanguageFields {
        return { language, localizedName: '', spellings: '', localizedDescription: '' };
    }

    private ordered(rows: LanguageFields[]): LanguageFields[] {
        return [...rows].sort((a, b) =>
            this.contentLanguages.indexOf(a.language) - this.contentLanguages.indexOf(b.language));
    }

    /** What the parent should send. Trimming here so both callers cannot forget it differently. */
    value(): GlossaryEntryInput {
        return {
            name: this.name.trim(),
            description: this.description.trim(),
            imageUrl: this.imageUrl(),
            isCaseSensitive: this.caseSensitive(),
            projectId: this.entry?.projectId ?? null,
            languages: this.languages().map(row => ({
                language: row.language,
                localizedName: row.localizedName.trim(),
                spellings: parseSpellings(row.spellings),
                localizedDescription: row.localizedDescription.trim(),
            })),
        };
    }

    canSave(): boolean {
        return this.name.trim().length > 0 && this.description.trim().length > 0 && this.languages().length > 0;
    }

    // ─── Languages ────────────────────────────────────────────────────────────

    hasLanguage(language: string): boolean {
        return this.languages().some(row => row.language === language);
    }

    /** A locked language can be kept and edited where the entry already has it, never added. */
    languageLocked(language: string): boolean {
        return !this.hasLanguage(language) && !this.auth.hasContentLanguage(language);
    }

    toggleLanguage(language: string) {
        if (this.hasLanguage(language)) {
            this.languages.update(rows => rows.filter(row => row.language !== language));
            return;
        }
        if (this.languageLocked(language)) return;
        this.languages.update(rows => this.ordered([...rows, this.blank(language)]));
    }

    // ─── Word forms (Russian only) ────────────────────────────────────────────

    suggestingFor = signal<string | null>(null);
    suggestedNothingFor = signal<string | null>(null);

    /** Russian only: the other content languages either do not inflect this way or need real morphology. */
    canSuggest(row: LanguageFields): boolean {
        return row.language === 'ru' && this.nameIn(row).length > 2;
    }

    private nameIn(row: LanguageFields): string {
        return row.localizedName.trim() || this.name.trim();
    }

    async suggestForms(row: LanguageFields) {
        this.suggestingFor.set(row.language);
        this.suggestedNothingFor.set(null);
        try {
            const { forms } = await this.glossary.suggestForms(this.nameIn(row), row.language);
            if (forms.length === 0) { this.suggestedNothingFor.set(row.language); return; }
            row.spellings = this.merged(row.spellings, forms);
        } catch {
            this.suggestedNothingFor.set(row.language);
        } finally {
            this.suggestingFor.set(null);
        }
    }

    // Merged, not replaced: whatever the author already wrote is theirs.
    private merged(existing: string, forms: string[]): string {
        const kept = parseSpellings(existing);
        for (const form of forms) {
            if (!kept.some(a => a.toLowerCase() === form.toLowerCase())) kept.push(form);
        }
        return kept.join(', ');
    }

    // ─── AI ───────────────────────────────────────────────────────────────────

    aiError = signal('');
    translatingLang = signal<string | null>(null);
    describing = signal(false);

    /** A language the owner has written nothing for yet — the only kind AI may fill. */
    private untouched(row: LanguageFields): boolean {
        return !row.localizedName.trim() && !row.spellings.trim();
    }

    aiTranslateTargets(): LanguageFields[] {
        return this.languages().filter(row => this.untouched(row));
    }

    canAiTranslate(): boolean {
        return this.auth.hasAiPlan() && this.name.trim().length > 0 && !this.translatingLang()
            && this.aiTranslateTargets().length > 0;
    }

    // One call per language, in turn, so what was charged and what failed stay per language.
    async aiTranslate() {
        if (!this.canAiTranslate()) return;
        this.aiError.set('');
        for (const row of this.aiTranslateTargets()) {
            this.translatingLang.set(row.language);
            try {
                const proposal = await this.glossary.aiTranslate(this.name.trim(), this.description.trim(), row.language);
                row.localizedName = proposal.localizedName;
                row.spellings = proposal.spellings.join(', ');
                if (!row.localizedDescription.trim()) row.localizedDescription = proposal.localizedDescription;
            } catch (e) {
                this.aiError.set(httpErrorMessage(e, this.t().glossary.translateFailed));
                break;
            }
        }
        this.translatingLang.set(null);
    }

    canAiDescribe(): boolean {
        return this.auth.hasAiPlan() && !this.describing() && (this.name.trim().length > 0 || !!this.imageUrl());
    }

    describeCost(): number {
        return this.imageUrl() ? this.cost.image : this.cost.text;
    }

    async aiDescribe() {
        if (!this.canAiDescribe()) return;
        this.aiError.set('');
        this.describing.set(true);
        try {
            const language = this.languages()[0]?.language ?? this.initialLanguage;
            const { description } = await this.glossary.aiDescribe(this.name.trim(), language, this.imageUrl());
            this.description = description;
        } catch (e) {
            this.aiError.set(httpErrorMessage(e, this.t().glossary.aiDescribeFailed));
        } finally {
            this.describing.set(false);
        }
    }

    // ─── Image ────────────────────────────────────────────────────────────────

    // The asset library's own picker: it uploads as well as picks, on the ordinary whitelist and
    // quota, so the glossary has no second pipeline for a picture.
    pickedImage(asset: LibraryAsset) {
        this.imageUrl.set(`/media/${asset.localPath}`);
        this.pickingImage.set(false);
    }
}
