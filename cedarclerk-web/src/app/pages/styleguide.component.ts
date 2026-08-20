import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ThemeService } from '../core/theme.service';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from '../shared/icon.component';
import { IconName, IconWeight } from '../shared/icon-data.generated';
import { ButtonComponent, ButtonSize, ButtonVariant } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { StampBadgeComponent, StampTone } from '../bench/display/stamp-badge.component';
import { ResinDropComponent } from '../bench/display/resin-drop.component';
import { LeafTagComponent, LeafState } from '../bench/display/leaf-tag.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';
import { TaskTagComponent } from '../bench/display/task-tag.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { BrassPinComponent } from '../bench/scenery/brass-pin.component';
import { BrassHookComponent } from '../bench/scenery/brass-hook.component';

export type SgSurface = 'paper' | 'chrome';

// T-078 / T-215 — the design-system reference page (ADR-071, ADR-136). One screen showing every
// token and every bench primitive, in both themes, both density modes and on both surfaces, so a
// divergence is visible without walking the app looking for it.
//
// One vocabulary only: every control here is a bench component. A styleguide that also drew its
// own .btn-accent/.sg-input/.sg-badge would be the ambiguity the port exists to end, and it would
// drift from the components it claims to document the moment either side moved.
//
// Deliberately NOT localized: this is a development surface, not a product screen, and putting
// ~120 more keys into en.ts/ru.ts to name swatches would be work with no reader.
@Component({
    selector: 'app-styleguide',
    imports: [
        IconComponent, RouterLink,
        ButtonComponent, InputComponent,
        StampBadgeComponent, ResinDropComponent, LeafTagComponent, PaperCardComponent, TaskTagComponent,
        SpecRowComponent, BrassPinComponent, BrassHookComponent,
    ],
    templateUrl: 'styleguide.component.html',
    styleUrls: ['styleguide.component.css'],
})
export class StyleguideComponent {
    private themes = inject(ThemeService);
    private locale = inject(LocaleService);

    theme = this.themes.theme;
    // T-051 — the pseudo-locale toggle lives here because this is the page that exists to make a
    // system-wide setting visible in one place. It is per-browser and never touches the profile.
    pseudo = this.locale.pseudo;
    // Local, not persisted: density is a property of a page (ADR-071), so this toggle stands in
    // for what /posts and /drafts will set on themselves once they migrate. Storing it would
    // imply it is a user preference, which it is not.
    compact = signal(false);
    // ADR-138's second axis, and the one thing about it that is invisible without a lever: the
    // same control painted on wood and on paper. It flips the demo bays, never the page root — a
    // surface is an element's material, not a screen's mode.
    surface = signal<SgSurface>('paper');
    motionKey = signal(0);

    readonly colors = [
        { name: '--bg', role: 'page background' },
        { name: '--canvas', role: 'recessed area behind sheets' },
        { name: '--surface', role: 'chrome: toolbars, popovers, cards' },
        { name: '--sheet', role: 'the writing/reading surface' },
        { name: '--alt', role: 'subtle fill: table head, inline code' },
        { name: '--border', role: 'hairlines and control borders' },
        { name: '--text', role: 'primary text' },
        { name: '--t2', role: 'secondary text' },
        { name: '--t3', role: 'tertiary / placeholder' },
        { name: '--accent', role: 'primary action, active state' },
        { name: '--asoft', role: 'accent wash (derived)' },
        { name: '--abord', role: 'accent border (derived)' },
        { name: '--danger', role: 'destructive action, error' },
        { name: '--warn', role: 'needs attention, pending' },
        { name: '--ok', role: 'success' },
        { name: '--hover', role: 'row / menu-item hover wash' },
        { name: '--scrim', role: 'backdrop behind a modal' },
    ];

    readonly series = ['--series-1', '--series-2', '--series-3', '--series-4', '--series-5', '--series-6'];

    readonly sizes = [
        { token: '--fs-9', px: '9' }, { token: '--fs-10', px: '10' }, { token: '--fs-11', px: '11' },
        { token: '--fs-12', px: '12' }, { token: '--fs-13', px: '13' }, { token: '--fs-14', px: '14' },
        { token: '--fs-15', px: '15' }, { token: '--fs-16', px: '16' }, { token: '--fs-17', px: '17' },
        { token: '--fs-18', px: '18' }, { token: '--fs-19', px: '19' }, { token: '--fs-20', px: '20' },
        { token: '--fs-22', px: '22' }, { token: '--fs-27', px: '27' },
    ];

    readonly roles = [
        { token: '--fs-caption', use: 'labels above a field, timestamps' },
        { token: '--fs-meta', use: 'secondary row data, counts' },
        { token: '--fs-ui', use: 'buttons, menu items, table cells' },
        { token: '--fs-body', use: 'default UI text' },
        { token: '--fs-title', use: 'page and section titles' },
    ];

    readonly spaces = ['--space-1', '--space-2', '--space-3', '--space-4', '--space-5', '--space-6'];
    readonly radii = ['--radius-sm', '--radius-md', '--radius-lg'];
    readonly elevations = ['--shadow', '--shadow-md', '--shadow-lg'];
    readonly icons = [
        { token: '--icon-xs', size: 'xs' as const },
        { token: '--icon-sm', size: 'sm' as const },
        { token: '--icon-md', size: 'md' as const },
        { token: '--icon-lg', size: 'lg' as const },
    ];

    readonly weights: IconWeight[] = ['regular', 'bold'];

    // A spread across the set rather than an exhaustive list: enough to judge stroke weight and
    // optical size against each other, which is what this row is for.
    readonly sampleIcons: IconName[] = [
        'trash', 'plus', 'check', 'x', 'warning', 'info', 'lock', 'eye', 'heart', 'archive',
        'gear', 'folder', 'newspaper', 'translate', 'sparkle', 'paper-plane-tilt', 'clock',
        'table', 'palette', 'arrow-clockwise',
    ];
    readonly motions = ['--motion-fast', '--motion-base', '--motion-slow'];

    // The 10 glyphs measured outside any icon set (ADR-072) — they render in the system emoji
    // font, which is why they look different on every OS. Shown here so the inconsistency is
    // visible rather than described; T-079 replaces them.
    readonly strayGlyphs = ['☾', '✦', '◷', '⤢', '¶', '⏰', '👍', '👎', '☰', '↑'];

    readonly buttonVariants: ButtonVariant[] = ['pine', 'paper', 'rail', 'danger'];
    readonly buttonSizes: ButtonSize[] = ['md', 'sm'];

    // Hover and focus are live cells, not pinned ones. No template can hold a pseudo-class open,
    // and the only way to fake one is to restate the component's own rules on the page — a second
    // copy of the thing this page exists to be the single copy of.
    readonly controlStates = [
        { key: 'default', label: 'default' },
        { key: 'hover', label: 'hover ↖' },
        { key: 'focus', label: 'focus ⇥' },
        { key: 'disabled', label: 'disabled' },
    ];

    readonly stampTones: StampTone[] = ['pine', 'brass', 'rust', 'ink'];
    readonly leafStates: LeafState[] = ['idle', 'active', 'dried'];

    // Bound through [(value)] so the field's own value channel is exercised on the page rather
    // than described: the ControlValueAccessor half is proved by the unit specs, this half by
    // typing into it.
    title = signal<string | number | null>('Devlog #12 — light and fog');
    slug = signal<string | number | null>('devlog-12-light-and-fog');
    parts = signal<string | number | null>(3);

    toggleTheme() {
        this.themes.toggle();
    }

    togglePseudo() {
        this.locale.setPseudo(!this.pseudo());
    }

    toggleSurface() {
        this.surface.update(s => (s === 'paper' ? 'chrome' : 'paper'));
    }

    replayMotion() {
        this.motionKey.update(k => k + 1);
    }
}
