// T-080 — generates src/app/shared/icon-usage.generated.ts by reading every template in the app.
//
// Why generated rather than hand-kept: the question /dev/icons answers is "which icon means what
// here, and does any meaning have two icons" — an inventory maintained by hand answers it only
// until the next commit. This scans the real call sites, so a stale answer is impossible as long
// as the script is re-run; it is committed for the same reason icon-data.generated.ts is.
//
// Run: node tools/generate-icon-usage.mjs   (from cedarclerk-web/)
import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve, relative } from 'node:path';

const ROOT = resolve(import.meta.dirname, '..');
const SRC = resolve(ROOT, 'src/app');

const files = [];
(function walk(dir) {
    for (const entry of readdirSync(dir)) {
        const p = join(dir, entry);
        if (statSync(p).isDirectory()) walk(p);
        // The two dev surfaces are excluded: they draw every icon in the set by binding [name] to
        // a loop variable, so counting them would report the inventory's own rendering as product
        // usage and inflate "(dynamic)" with the page whose job is to explain it.
        else if (/\.(html|ts)$/.test(p)
            && !/icon-(data|usage)\.generated\.ts$/.test(p)
            && !/pages[\\/](icons|styleguide)\.component\./.test(p)) files.push(p);
    }
})(SRC);

// icon name -> { count, labels: Map<label, count>, files: Set }
const usage = new Map();
const record = (name, label, file) => {
    if (!usage.has(name)) usage.set(name, { count: 0, labels: new Map(), files: new Set() });
    const u = usage.get(name);
    u.count++;
    u.files.add(relative(SRC, file).replace(/\\/g, '/'));
    if (label) u.labels.set(label, (u.labels.get(label) ?? 0) + 1);
};

// A button's visible text, with the template's own control flow stripped out: `@if (busy()) {
// {{ t().common.save }} }` is the label "t().common.save", not a fragment of Angular syntax.
function visibleText(inner) {
    const text = inner
        .replace(/<app-icon[\s\S]*?(\/>|<\/app-icon>)/g, '')
        .replace(/<[^>]*>/g, '')
        // one level of nesting is enough for `@if (busy())`, which is the shape in this codebase
        .replace(/@(if|else if|else|for|switch|case|default|empty)\s*(\((?:[^()]|\([^()]*\))*\))?\s*\{?/g, ' ')
        .replace(/[{}]/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();
    // Anything still carrying a quote, an angle bracket or an assignment is a fragment of markup
    // this crude parser mis-sliced, not a label. Better to report nothing than to report noise.
    if (!text || /["<>=]/.test(text)) return null;
    return text;
}

const ICON_RE = /<app-icon\b([^>]*)>/g;
const NAME_RE = /(?:^|\s)name="([^"]+)"/;
const DYNAMIC_NAME_RE = /\[name\]="([^"]+)"/;

for (const file of files) {
    const src = readFileSync(file, 'utf8');

    // Controls first: an icon inside a labelled button/link takes that label as its meaning.
    const claimed = new Set();
    for (const tag of ['button', 'a']) {
        const re = new RegExp(`<${tag}\\b([^>]*)>([\\s\\S]*?)</${tag}>`, 'g');
        let m;
        while ((m = re.exec(src))) {
            const [whole, attrs, inner] = m;
            if (!inner.includes('<app-icon')) continue;
            const label =
                (attrs.match(/\[attr\.aria-label\]="([^"]+)"/) ?? attrs.match(/aria-label="([^"]+)"/) ??
                 attrs.match(/\[title\]="([^"]+)"/) ?? attrs.match(/(?<!\[)\btitle="([^"]+)"/))?.[1]
                // A visible text label counts too — it is the icon's meaning just as much.
                ?? visibleText(inner);
            let im;
            const iconRe = new RegExp(ICON_RE.source, 'g');
            // Offset of `inner` inside the file: the match minus its own closing tag and inner
            // length. indexOf(inner) would be wrong whenever the same text appears in the attrs.
            const innerStart = m.index + whole.length - `</${tag}>`.length - inner.length;
            while ((im = iconRe.exec(inner))) {
                claimed.add(innerStart + im.index);
                const name = im[1].match(NAME_RE)?.[1] ?? (im[1].match(DYNAMIC_NAME_RE) ? '(dynamic)' : null);
                if (name) record(name, label, file);
            }
        }
    }

    // Then every icon that is not inside a control: decorative, or standing alone in a row.
    let m;
    const loose = new RegExp(ICON_RE.source, 'g');
    while ((m = loose.exec(src))) {
        if (claimed.has(m.index)) continue;
        const attrs = m[1];
        const name = attrs.match(NAME_RE)?.[1] ?? (attrs.match(DYNAMIC_NAME_RE) ? '(dynamic)' : null);
        if (!name) continue;
        const own = attrs.match(/\[label\]="([^"]+)"/)?.[1] ?? attrs.match(/label="([^"]+)"/)?.[1] ?? null;
        record(name, own, file);
    }
}

const rows = [...usage.entries()]
    .sort((a, b) => b[1].count - a[1].count || a[0].localeCompare(b[0]))
    .map(([icon, u]) => ({
        icon,
        count: u.count,
        labels: [...u.labels.keys()].sort(),
        files: [...u.files].sort(),
    }));

const out = [
    '// GENERATED by tools/generate-icon-usage.mjs — do not edit by hand.',
    '// Every app-icon call site in src/app, with the label its control carries (T-080).',
    `// ${rows.length} distinct icons across ${rows.reduce((n, r) => n + r.count, 0)} call sites.`,
    '',
    'export interface IconUsageRow {',
    '    /** Icon name, or "(dynamic)" where the template binds [name] to an expression. */',
    '    icon: string;',
    '    count: number;',
    '    /** The accessible names this icon appears under — a raw t() path, or literal text. */',
    '    labels: string[];',
    '    files: string[];',
    '}',
    '',
    'export const ICON_USAGE: IconUsageRow[] = ' + JSON.stringify(rows, null, 4) + ';',
    '',
].join('\n');

writeFileSync(resolve(ROOT, 'src/app/shared/icon-usage.generated.ts'), out);
console.log(`wrote ${rows.length} icons, ${rows.reduce((n, r) => n + r.count, 0)} call sites`);
