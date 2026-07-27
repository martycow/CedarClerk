import { Component, inject } from '@angular/core';
import { LocaleService, UiLang } from '../core/i18n/locale.service';

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
    template: `
        <div class="lang-switch">
            @for (o of options; track o.lang) {
            <button type="button" class="lang-code" [class.on]="locale.uiLang() === o.lang"
                    (click)="locale.set(o.lang)" [title]="o.label" [attr.aria-label]="o.label"
                    [attr.aria-pressed]="locale.uiLang() === o.lang">{{ o.code }}</button>
            }
        </div>
    `,
    styles: [`
        .lang-switch {
            display: flex;
            justify-content: center;
            gap: 6px;
            margin-top: 18px;
        }

        .lang-code {
            border: 1px solid transparent;
            background: none;
            border-radius: var(--radius-md);
            padding: 4px 10px;
            font-family: inherit;
            font-size: 12.5px;
            font-weight: 700;
            letter-spacing: .04em;
            color: var(--t3);
            cursor: pointer;
            transition: color .12s, background .12s;
        }

        .lang-code:hover {
            color: var(--text);
        }

        .lang-code.on {
            color: var(--accent);
            border-color: var(--abord);
            background: var(--asoft);
        }
    `],
})
export class LangSwitchComponent {
    locale = inject(LocaleService);

    // Endonyms in the tooltip — a language name is only useful to someone who reads it.
    readonly options: { lang: UiLang; code: string; label: string }[] = [
        { lang: 'ru', code: 'RU', label: 'Русский' },
        { lang: 'en', code: 'EN', label: 'English' },
    ];
}
