import { INTERFACE_LANGUAGE_OPTIONS } from '@localization/dictionaries';
import { Component, inject, input } from '@angular/core';
import { LocaleService } from '../core/i18n/locale.service';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';

// I1 — language picker for the login/register screens, which are the only place the UI language
// can't be changed otherwise: the Settings picker needs an account, and picking a language is
// exactly what someone who can't read the form wants to do first.
//
// DB3.1 — this used flag emoji, on the reasoning that a flag is recognisable to someone who
// can't read the current language. That was wrong on the platform Marty actually uses: Windows
// ships no regional-indicator glyphs, so a flag renders as two letters anyway. Two-letter codes
// instead — the same treatment the editor's own content-language tabs already use, and they
// render identically everywhere. LocaleService.set writes localStorage, so the choice survives to
// the next screen; RegisterComponent additionally pushes it onto the new profile.
@Component({
    selector: 'app-lang-switch',
    imports: [LeafTagComponent],
    template: `
        <div class="lang-switch" [class.plain]="appearance() === 'plain'">
            @for (o of options; track o.lang) {
            @if (appearance() === 'plain') {
                <button type="button" [attr.aria-pressed]="locale.uiLang() === o.lang" [title]="o.label" (click)="locale.set(o.lang)">{{ o.code }}</button>
            } @else {
            <app-leaf-tag interactive [state]="locale.uiLang() === o.lang ? 'active' : 'idle'"
                          [hint]="o.label" (activated)="locale.set(o.lang)">{{ o.code }}</app-leaf-tag>
            }
            }
        </div>
    `,
    styles: [`
        .plain.lang-switch { margin: 0; gap: 0; }
        .plain button { min-width: var(--hit-touch); min-height: var(--hit-touch); background: transparent; border: 0; font: inherit; color: inherit; cursor: pointer; opacity: .7; }
        .plain button[aria-pressed="true"] { opacity: 1; font-weight: 700; text-decoration: underline; text-underline-offset: 5px; }
        .lang-switch {
            display: flex;
            justify-content: center;
            gap: 6px;
            margin-top: 18px;
        }
    `],
})
export class LangSwitchComponent {
    readonly appearance = input<'tags' | 'plain'>('tags');
    locale = inject(LocaleService);

    // Endonyms in the tooltip — a language name is only useful to someone who reads it.
    readonly options = INTERFACE_LANGUAGE_OPTIONS;
}
