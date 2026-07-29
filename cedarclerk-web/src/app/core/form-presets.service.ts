import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RegistrationQuestionType } from './drafts.service';

// Reusable registration-form definitions (N12). `formJson` holds the same blob shape as a
// draft's own form — applying a preset copies it onto the draft rather than linking to it, so
// editing a preset never rewrites a post that already uses it.
export interface FormPreset {
    id: string;
    name: string;
    formJson: string;
    // Kept for wire compatibility; since ADR-060 it mirrors the v2 blob's first language.
    language: string;
    createdAt: string;
}

// ADR-060 — the Forms tab edits the v2 multi-language blob natively: one skeleton of stable
// question/option ids, per-language text dictionaries on top. Everything else in the app keeps
// consuming the single-language projection (parseRegistrationForm in drafts.service).
export interface FormOptionEdit { id: string; label: Record<string, string>; }
export interface FormQuestionEdit {
    id: string;
    type: RegistrationQuestionType;
    required?: boolean;
    label: Record<string, string>;
    options: FormOptionEdit[];
}
export interface RegistrationFormEdit {
    v: 2;
    languages: string[];
    intro: Record<string, string>;
    requireName: boolean; requireNickname: boolean; requireEmail: boolean; requireSocial: boolean;
    questions: FormQuestionEdit[];
}

export function blankFormEdit(lang: string): RegistrationFormEdit {
    return { v: 2, languages: [lang], intro: {}, requireName: true, requireNickname: false, requireEmail: true, requireSocial: false, questions: [] };
}

// Loads either blob shape into the edit model, upgrading a v1 single-language preset to v2 with
// `fallbackLang` as its one language. Never throws — a corrupt blob opens as a blank form.
export function normalizeFormForEdit(json: string | null | undefined, fallbackLang: string): RegistrationFormEdit {
    if (!json) return blankFormEdit(fallbackLang);
    try {
        const raw = JSON.parse(json) as Record<string, unknown>;
        const isV2 = raw['v'] === 2;
        const languages = isV2 && Array.isArray(raw['languages'])
            ? (raw['languages'] as unknown[]).filter((l): l is string => typeof l === 'string' && !!l)
            : [];
        if (!languages.length) languages.push(fallbackLang);

        const textMap = (node: unknown): Record<string, string> => {
            if (typeof node === 'string') return node.trim() ? { [languages[0]]: node } : {};
            if (node && typeof node === 'object') {
                const out: Record<string, string> = {};
                for (const [k, v] of Object.entries(node as Record<string, unknown>)) {
                    if (typeof v === 'string' && v.trim()) out[k] = v;
                }
                return out;
            }
            return {};
        };

        const questions: FormQuestionEdit[] = [];
        for (const q of Array.isArray(raw['questions']) ? raw['questions'] as unknown[] : []) {
            if (!q || typeof q !== 'object') continue;
            const qo = q as Record<string, unknown>;
            const options: FormOptionEdit[] = [];
            for (const o of Array.isArray(qo['options']) ? qo['options'] as unknown[] : []) {
                if (typeof o === 'string') {
                    if (o.trim()) options.push({ id: o, label: { [languages[0]]: o } });
                } else if (o && typeof o === 'object') {
                    const oo = o as Record<string, unknown>;
                    const label = textMap(oo['label']);
                    options.push({ id: typeof oo['id'] === 'string' && oo['id'] ? oo['id'] as string : newOptionId(), label });
                }
            }
            const type = (qo['type'] === 'choice' || qo['type'] === 'multi' || qo['type'] === 'consent' ? qo['type'] : 'text') as RegistrationQuestionType;
            questions.push({
                id: typeof qo['id'] === 'string' && qo['id'] ? qo['id'] as string : newQuestionId(),
                type,
                required: type === 'consent' || !!qo['required'],
                label: textMap(qo['label']),
                options,
            });
        }

        return {
            v: 2,
            languages,
            intro: textMap(raw['intro']),
            requireName: !!raw['requireName'],
            requireNickname: !!raw['requireNickname'],
            requireEmail: !!raw['requireEmail'],
            requireSocial: !!raw['requireSocial'],
            questions,
        };
    } catch {
        return blankFormEdit(fallbackLang);
    }
}

// Date.now alone can collide when two ids are minted in the same millisecond (option rows are
// created programmatically, not one click apart like questions) — a random suffix settles it.
export function newQuestionId(): string {
    return `q${Date.now().toString(36)}${Math.floor(Math.random() * 1e6).toString(36)}`;
}
export function newOptionId(): string {
    return `o${Date.now().toString(36)}${Math.floor(Math.random() * 1e6).toString(36)}`;
}

@Injectable({ providedIn: 'root' })
export class FormPresetsService {
    private http = inject(HttpClient);

    list() {
        return firstValueFrom(this.http.get<FormPreset[]>('/api/form-presets'));
    }

    create(name: string, formJson: string, language: string) {
        return firstValueFrom(this.http.post<FormPreset>('/api/form-presets', { name, formJson, language }));
    }

    update(id: string, name: string, formJson: string, language: string) {
        return firstValueFrom(this.http.put<FormPreset>(`/api/form-presets/${id}`, { name, formJson, language }));
    }

    remove(id: string) {
        return firstValueFrom(this.http.delete(`/api/form-presets/${id}`));
    }

    // ADR-060 — fills `targetLanguage` by machine-translating the preset's first language
    // server-side; returns the updated preset. Pro Plus + daily AI quota, same as post translate.
    translate(id: string, targetLanguage: string) {
        return firstValueFrom(this.http.post<FormPreset>(`/api/form-presets/${id}/translate`, { targetLanguage }));
    }
}
