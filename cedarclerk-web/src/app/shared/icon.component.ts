import { Component, Input, computed, inject, signal } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ICONS, IconName, IconWeight } from './icon-data.generated';

// T-079 / ADR-072 — the single icon component. Everything the app draws goes through here, so
// "make the icons bolder" or "a size step is wrong" is one change rather than a fourth sweep
// through 200 call sites.
//
// Size comes from the --icon-* tokens, never from a caller's pixel value; weight is a prop so a
// dense table and an empty-state illustration can differ without two icon sets existing.
const CACHE = new Map<string, SafeHtml>();

@Component({
    selector: 'app-icon',
    standalone: true,
    // fill="currentColor" is NOT decoration: the Phosphor assets carry it on their own <svg>
    // element, and the generator strips that wrapper to keep only the paths — so without it here
    // every icon in the app rendered in the SVG default, black, which is nearly invisible on the
    // dark theme (reported 01.08.2026). It was never inherited from anywhere; it was simply lost.
    template: `<svg viewBox="0 0 256 256" fill="currentColor" [style.width]="px()" [style.height]="px()"
                    [attr.aria-hidden]="label ? null : true" [attr.role]="label ? 'img' : null"
                    [attr.aria-label]="label || null"
                    [innerHTML]="body()"></svg>`,
    styles: [`
        :host { display: inline-flex; flex: none; }
        /* currentColor comes from the package's own markup, so an icon inherits the colour of
           whatever it sits in — the same behaviour the Lucide directive had. */
        svg { display: block; }
    `],
})
export class IconComponent {
    private sanitizer = inject(DomSanitizer);

    @Input({ required: true }) set name(value: IconName) { this._name.set(value); }
    @Input() set weight(value: IconWeight) { this._weight.set(value); }
    @Input() set size(value: 'xs' | 'sm' | 'md' | 'lg') { this._size.set(value); }
    /** Set only when the icon carries meaning no adjacent text repeats (T-080). */
    @Input() label = '';

    private _name = signal<IconName>('info');
    private _weight = signal<IconWeight>('regular');
    private _size = signal<'xs' | 'sm' | 'md' | 'lg'>('sm');

    px = computed(() => `var(--icon-${this._size()})`);

    // bypassSecurityTrustHtml is safe here and only here: the markup is a build-time constant
    // generated from an npm package, never anything a user typed. Angular would otherwise strip
    // the <path> elements entirely, since its HTML sanitizer does not keep SVG children.
    body = computed<SafeHtml>(() => {
        const key = `${this._weight()}/${this._name()}`;
        let html = CACHE.get(key);
        if (!html) {
            html = this.sanitizer.bypassSecurityTrustHtml(ICONS[this._weight()][this._name()] ?? '');
            CACHE.set(key, html);
        }
        return html;
    });
}
