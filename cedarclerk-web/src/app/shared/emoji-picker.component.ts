import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, inject, output, signal, viewChild } from '@angular/core';
import { LocaleService } from '../core/i18n/locale.service';

// ADR-314 — the full Unicode emoji set behind the editor's hand-picked grid.
//
// `emoji-picker-element` is a custom element with its own search, skin tones, "frequently used"
// and flag group. It is imported on first open, so none of its ~100 KB or the ~0.5 MB data file
// reaches anyone who never asks for "all emoji". Both are self-hosted (angular.json assets): the
// picker's default data source is a CDN, and a post-writing tool should not call out to one.
//
// The picker reports a glyph and nothing else (`picked`): inserting it is the editor's job, the
// same as for the hand-picked grid.

/** The data sets we ship (angular.json copies exactly these). Any other UI language falls back to en. */
export const EMOJI_DATA_LOCALES = ['en', 'ru'] as const;
export type EmojiDataLocale = (typeof EMOJI_DATA_LOCALES)[number];

export function emojiDataLocale(uiLang: string): EmojiDataLocale {
    return (EMOJI_DATA_LOCALES as readonly string[]).includes(uiLang) ? (uiLang as EmojiDataLocale) : 'en';
}

export const EMOJI_DATA_URL = (locale: EmojiDataLocale) => `assets/emoji/${locale}/data.json`;
export const FLAG_FONT_URL = 'assets/fonts/TwemojiCountryFlags.woff2';

@Component({
    selector: 'app-emoji-picker',
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (failed()) { <p class="field-hint-inline">{{ t().editor.emoji['loadFailed'] }}</p> }
        <div #host class="emoji-picker-host"></div>
    `,
    styles: [`
        :host { display: block; }
        .emoji-picker-host { display: block; }
        /* The picker's own CSS custom properties, mapped onto the app tokens so it follows the
           light/dark theme and the user's accent instead of carrying one-off colours (DESIGN.md). */
        .emoji-picker-host ::ng-deep emoji-picker,
        :host ::ng-deep emoji-picker {
            width: 100%;
            height: 340px;
            --background: var(--sheet);
            --border-color: var(--border);
            --input-border-color: var(--border-strong);
            --input-font-color: var(--ink);
            --button-hover-background: var(--alt);
            --button-active-background: var(--alt);
            --indicator-color: var(--pine);
            --category-font-color: var(--ink);
            --emoji-font-family: 'Twemoji Country Flags', 'Apple Color Emoji', 'Segoe UI Emoji', 'Noto Color Emoji', sans-serif;
        }
    `],
})
export class EmojiPickerComponent implements OnDestroy {
    private readonly locale = inject(LocaleService);
    t = this.locale.t;

    /** The glyph the user clicked. */
    readonly picked = output<string>();

    protected readonly failed = signal(false);
    private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
    private element: HTMLElement | null = null;
    private listener: ((e: Event) => void) | null = null;
    private mounted = false;

    constructor() {
        effect(() => {
            this.host();
            if (!this.mounted) { this.mounted = true; void this.mount(); }
        });
    }

    private async mount() {
        try {
            const lang = emojiDataLocale(this.locale.uiLang());
            const [{ Picker }, { polyfillCountryFlagEmojis }, i18n] = await Promise.all([
                import('emoji-picker-element'),
                import('country-flag-emoji-polyfill'),
                lang === 'ru' ? import('emoji-picker-element/i18n/ru_RU').then(m => m.default) : Promise.resolve(undefined),
            ]);
            // Windows Chrome/Edge draw no flags (they show two letters). The polyfill adds a flags-only
            // font — and only when the browser needs it. The app's own file, not its CDN default.
            polyfillCountryFlagEmojis('Twemoji Country Flags', FLAG_FONT_URL);

            const picker = new Picker({
                dataSource: EMOJI_DATA_URL(lang),
                locale: lang,
                ...(i18n ? { i18n } : {}),
            });
            this.listener = (e: Event) => {
                const unicode = (e as CustomEvent<{ unicode?: string }>).detail?.unicode;
                if (unicode) this.picked.emit(unicode);
            };
            picker.addEventListener('emoji-click', this.listener);
            this.host().nativeElement.appendChild(picker);
            this.element = picker;
        } catch {
            this.failed.set(true);
        }
    }

    ngOnDestroy() {
        if (this.element && this.listener) this.element.removeEventListener('emoji-click', this.listener);
        this.element?.remove();
    }
}
