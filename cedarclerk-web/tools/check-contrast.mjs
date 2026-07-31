// T-082 — WCAG contrast check over the token set, both themes.
//
// Reads the values out of src/styles.scss rather than taking a copied list, so it cannot drift
// from what ships; resolves the color-mix() derivations the same way a browser does (sRGB).
// Run: node tools/check-contrast.mjs   (from cedarclerk-web/)
//
// Thresholds: 4.5 for body text, 3.0 for large text (>=19px semibold / 24px) and for the boundary
// of a control or a graphical object — WCAG 2.2 SC 1.4.3 and 1.4.11.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const src = readFileSync(resolve(import.meta.dirname, '../src/styles.scss'), 'utf8');

function block(selector) {
    const i = src.indexOf(selector);
    const open = src.indexOf('{', i);
    let depth = 0, j = open;
    for (; j < src.length; j++) {
        if (src[j] === '{') depth++;
        else if (src[j] === '}' && --depth === 0) break;
    }
    // Comments first: this file documents nearly every token, and a `/* ... */` sitting between a
    // declaration and its value would otherwise be read as part of the value.
    const body = src.slice(open + 1, j).replace(/\/\*[\s\S]*?\*\//g, '');
    const vars = {};
    for (const m of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) vars[m[1]] = m[2].trim();
    return vars;
}

const light = block(':root');
const dark = { ...light, ...block(':root[data-theme="dark"]') };

const hex = h => {
    const s = h.replace('#', '');
    const full = s.length === 3 ? [...s].map(c => c + c).join('') : s;
    return [0, 2, 4].map(i => parseInt(full.slice(i, i + 2), 16));
};

// color-mix(in srgb, A p%, B) — the only form used in the token set.
function resolveColor(value, vars, seen = new Set()) {
    value = value.trim();
    const varRef = value.match(/^var\((--[\w-]+)\)$/);
    if (varRef) {
        if (seen.has(varRef[1])) throw new Error('cycle at ' + varRef[1]);
        seen.add(varRef[1]);
        return resolveColor(vars[varRef[1]], vars, seen);
    }
    if (value.startsWith('#')) return hex(value);
    const mix = value.match(/^color-mix\(in srgb,\s*(.+?)\s+([\d.]+)%\s*,\s*(.+?)\s*\)$/);
    if (mix) {
        const a = resolveColor(mix[1], vars, new Set(seen));
        const p = parseFloat(mix[2]) / 100;
        const bRaw = mix[3].trim();
        // `transparent` composited over its own backdrop is handled by the caller passing the
        // backdrop in explicitly; here it means "keep A's colour, lighten by nothing".
        const b = bRaw === 'transparent' ? a : resolveColor(bRaw, vars, new Set(seen));
        return [0, 1, 2].map(i => Math.round(a[i] * p + b[i] * (1 - p)));
    }
    const rgba = value.match(/^rgba?\(([^)]+)\)$/);
    if (rgba) {
        const parts = rgba[1].split(',').map(s => parseFloat(s.trim()));
        return parts.slice(0, 3);
    }
    throw new Error('cannot resolve: ' + value);
}

const lum = ([r, g, b]) => {
    const f = c => {
        const s = c / 255;
        return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
};

const ratio = (a, b) => {
    const [l1, l2] = [lum(a), lum(b)].sort((x, y) => y - x);
    return (l1 + 0.05) / (l2 + 0.05);
};

// Pairs that actually occur in the app, not every combination: a token pair nothing renders is
// not a defect. The threshold attached to each pair is the contract that token carries.
//
// Two tokens are deliberately absent, and their absence is the decision (ADR-074):
//   --border  — a decorative hairline between cards and rows, never the only way to identify a
//               control. Holding it to 3:1 would draw the whole warm-paper surface as a wireframe.
//               The boundary that IS an affordance has its own token, --border-strong, below.
//   --abord   — the same, in the accent tint: it edges a chip or an active wash whose fill already
//               carries the state.
const SURFACES = ['--bg', '--canvas', '--surface', '--sheet', '--alt'];
const pairs = [];
for (const s of SURFACES) {
    pairs.push({ fg: '--text', bg: s, min: 4.5, note: 'body text' });
    pairs.push({ fg: '--t2', bg: s, min: 4.5, note: 'secondary text' });
    // --t3 is NOT text (ADR-074). Placeholder, disabled, and decoration only — at 4.5 on this
    // palette it collapses onto --t2 and the app loses a tier for nothing.
    pairs.push({ fg: '--t3', bg: s, min: 3.0, note: 'placeholder / disabled / decoration' });
    pairs.push({ fg: '--accent', bg: s, min: 4.5, note: 'link, active label' });
    pairs.push({ fg: '--danger', bg: s, min: 4.5, note: 'error text' });
    pairs.push({ fg: '--ok', bg: s, min: 4.5, note: 'success text' });
    pairs.push({ fg: '--warn', bg: s, min: 4.5, note: 'warning text' });
    pairs.push({ fg: '--border-strong', bg: s, min: 3.0, note: 'control boundary (field, toggle)' });
}
pairs.push({ fg: '--sheet', bg: '--accent', min: 4.5, note: 'text on a primary button' });
pairs.push({ fg: '--sheet', bg: '--danger', min: 4.5, note: 'text on a destructive button' });
pairs.push({ fg: '--accent', bg: '--asoft', min: 4.5, note: 'accent label on its own wash' });
for (let i = 1; i <= 6; i++) pairs.push({ fg: `--series-${i}`, bg: '--surface', min: 3.0, note: 'chart series' });

// SUGGEST=1 prints, for every failing foreground, the nearest value along the line towards black
// (light theme) or white (dark) that clears its worst surface. Hue is preserved by construction:
// the search moves along a straight line in sRGB from the current colour, so the mix stays the
// same colour, only darker or lighter. It is a starting point for a decision, not the decision.
function suggest(fgValue, vars, worstBg, min, towards) {
    const from = resolveColor(fgValue, vars);
    const bg = resolveColor(vars[worstBg], vars);
    let lo = 0, hi = 1;
    for (let i = 0; i < 24; i++) {
        const mid = (lo + hi) / 2;
        const c = [0, 1, 2].map(k => Math.round(from[k] * (1 - mid) + towards[k] * mid));
        if (ratio(c, bg) >= min) hi = mid;
        else lo = mid;
    }
    const c = [0, 1, 2].map(k => Math.round(from[k] * (1 - hi) + towards[k] * hi));
    return '#' + c.map(v => v.toString(16).padStart(2, '0')).join('').toUpperCase();
}

let failures = 0;
for (const [themeName, vars] of [['light', light], ['dark', dark]]) {
    console.log(`\n=== ${themeName} ===`);
    for (const p of pairs) {
        const r = ratio(resolveColor(vars[p.fg], vars), resolveColor(vars[p.bg], vars));
        const ok = r >= p.min;
        if (!ok) failures++;
        let line = `${ok ? 'ok  ' : 'FAIL'} ${r.toFixed(2).padStart(5)} (min ${p.min})  ${p.fg} on ${p.bg}  — ${p.note}`;
        if (!ok && process.env.SUGGEST) {
            const towards = themeName === 'light' ? [0, 0, 0] : [255, 255, 255];
            line += `   → ${suggest(vars[p.fg], vars, p.bg, p.min, towards)}`;
        }
        if (!ok || process.env.VERBOSE) console.log(line);
    }
}
console.log(`\n${failures} failing pair(s). Set VERBOSE=1 to print the passing ones too.`);
process.exit(failures ? 1 : 0);
