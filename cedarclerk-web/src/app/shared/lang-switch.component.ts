import { Component, inject } from '@angular/core';
import { LocaleService, UiLang } from '../core/i18n/locale.service';

// I1 — language picker for the login/register screens, which are the only place the UI language
// can't be changed otherwise: the Settings picker needs an account, and picking a language is
// exactly what someone who can't read the form wants to do first.
//
// Flags rather than language names (I17), because that is what a reader who doesn't speak the
// current language can actually recognise. LocaleService.set already writes localStorage, so the
// choice survives to the next screen; RegisterComponent additionally pushes it onto the new
// profile so Settings opens already holding it.
@Component({
    selector: 'app-lang-switch',
    template: `
        <div class="lang-switch">
            @for (o of options; track o.lang) {
            <button type="button" class="lang-flag" [class.on]="locale.uiLang() === o.lang"
                    (click)="locale.set(o.lang)" [title]="o.label" [attr.aria-label]="o.label"
                    [attr.aria-pressed]="locale.uiLang() === o.lang">{{ o.flag }}</button>
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

        .lang-flag {
            border: 1px solid transparent;
            background: none;
            border-radius: var(--radius-md);
            padding: 3px 8px;
            font-size: 18px;
            line-height: 1.2;
            cursor: pointer;
            /* The unselected flag is dimmed rather than hidden, so both stay findable. */
            opacity: .45;
            filter: grayscale(1);
            transition: opacity .12s, filter .12s;
        }

        .lang-flag:hover {
            opacity: .8;
            filter: grayscale(.3);
        }

        .lang-flag.on {
            opacity: 1;
            filter: none;
            border-color: var(--border);
            background: var(--alt);
        }
    `],
})
export class LangSwitchComponent {
    locale = inject(LocaleService);

    // Endonyms, not translations — a language name is only useful to someone who reads it.
    readonly options: { lang: UiLang; flag: string; label: string }[] = [
        { lang: 'ru', flag: '🇷🇺', label: 'Русский' },
        { lang: 'en', flag: '🇺🇸', label: 'English' },
    ];
}
