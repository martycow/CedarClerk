import { Component, computed, inject, signal } from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { InputComponent } from '../bench/forms/input.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';
import { ICONS, IconName, IconWeight } from '../shared/icon-data.generated';
import { ICON_USAGE } from '../shared/icon-usage.generated';
import { LocaleService } from '../core/i18n/locale.service';

interface Row {
    name: IconName;
    count: number;
    /** Accessible names this icon is used under, resolved into the current UI language. */
    labels: string[];
    files: string[];
}

// T-080 — the icon inventory. The styleguide (T-078) shows what the icon *tokens* do; this page
// shows what the icons *mean* in this app, which is the question that has an answer only by
// reading all 209 call sites at once.
//
// Two failure modes it exists to make visible, neither of which any single screen can show:
//   · one icon carrying several unrelated meanings (arrow-clockwise is a spinner AND refresh AND
//     archive — a reader has to learn it three times);
//   · one meaning drawn with two different icons, which is the same confusion in reverse.
//
// Not localized, same reasoning as the styleguide: a development surface, and naming its own
// controls would put keys into both dictionaries for a reader who does not exist.
@Component({
    selector: 'app-icons-page',
    imports: [IconComponent, InputComponent, LeafTagComponent],
    templateUrl: 'icons.component.html',
    styleUrls: ['icons.component.css'],
})
export class IconsComponent {
    private locale = inject(LocaleService);
    t = this.locale.t;

    query = signal('');
    weight = signal<IconWeight>('regular');
    size = signal<'xs' | 'sm' | 'md' | 'lg'>('md');
    readonly weights: IconWeight[] = ['regular', 'bold'];
    readonly sizes = ['xs', 'sm', 'md', 'lg'] as const;

    readonly setNames = Object.keys(ICONS.regular).sort() as IconName[];

    // Every icon in the set, carrying whatever the generator found about its use. An icon with
    // count 0 is in the bundle and drawn nowhere.
    rows = computed<Row[]>(() => {
        const used = new Map(ICON_USAGE.map(u => [u.icon, u]));
        return this.setNames.map(name => {
            const u = used.get(name);
            return {
                name,
                count: u?.count ?? 0,
                labels: (u?.labels ?? []).map(l => this.resolve(l)).sort(),
                files: u?.files ?? [],
            };
        }).sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
    });

    filtered = computed(() => {
        const q = this.query().trim().toLowerCase();
        if (!q) return this.rows();
        return this.rows().filter(r =>
            r.name.includes(q) || r.labels.some(l => l.toLowerCase().includes(q)));
    });

    unused = computed(() => this.rows().filter(r => r.count === 0));

    // Three distinct labels is where "this icon is overloaded" starts being a fair claim: two can
    // be the same idea worded twice (save / saving), three rarely is.
    overloaded = computed(() => this.rows().filter(r => r.labels.length >= 3));

    // The inverse reading: one meaning, drawn two ways. Only simple labels take part — a ternary
    // is two meanings in one expression and would produce false pairs.
    ambiguous = computed(() => {
        const byLabel = new Map<string, Set<IconName>>();
        for (const r of this.rows()) {
            for (const label of r.labels) {
                if (/[?(){}]/.test(label)) continue;
                if (!byLabel.has(label)) byLabel.set(label, new Set());
                byLabel.get(label)!.add(r.name);
            }
        }
        return [...byLabel.entries()]
            .filter(([, icons]) => icons.size > 1)
            .map(([label, icons]) => ({ label, icons: [...icons].sort() }))
            .sort((a, b) => a.label.localeCompare(b.label));
    });

    totals = computed(() => ({
        set: this.setNames.length,
        used: this.rows().filter(r => r.count > 0).length,
        sites: ICON_USAGE.reduce((n, u) => n + u.count, 0),
        dynamic: ICON_USAGE.find(u => u.icon === '(dynamic)')?.count ?? 0,
    }));

    // `t().common.delete` in a template becomes "Delete" here, so the inventory reads as meanings
    // rather than as key paths. Anything that is not a plain path is left as written.
    private resolve(label: string): string {
        return label.replace(/t\(\)((?:\.[A-Za-z0-9_]+)+)/g, (whole, path: string) => {
            let cur: unknown = this.t();
            for (const key of path.slice(1).split('.')) {
                cur = (cur as Record<string, unknown> | undefined)?.[key];
            }
            return typeof cur === 'string' ? cur : whole;
        });
    }
}
