// T-082 — WCAG contrast check over the token set, both themes.
//
// Reads the values out of src/styles.scss rather than taking a copied list, so it cannot drift
// from what ships; resolves the color-mix() derivations the same way a browser does (sRGB),
// composites alpha over the backdrop the pair is measured against, and scores a gradient at every
// one of its colour stops.
// Run: node tools/check-contrast.mjs   (from cedarclerk-web/)
//
// Thresholds: 4.5 for body text, 3.0 for large text (>=19px semibold / 24px) and for the boundary
// of a control or a graphical object — WCAG 2.2 SC 1.4.3 and 1.4.11.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { CONTRACT } from './contract-tokens.mjs';

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
const darkOwn = block(':root[data-theme="dark"]');
const dark = { ...light, ...darkOwn };

// Colours carry alpha through resolution as [r, g, b, a]; only the composite drops it.
const hex = h => {
    const s = h.replace('#', '');
    const full = s.length <= 4 ? [...s].map(c => c + c).join('') : s;
    const n = [0, 2, 4, 6].map(i => parseInt(full.slice(i, i + 2), 16));
    return [n[0], n[1], n[2], full.length === 8 ? n[3] / 255 : 1];
};

// Splitting on a bare comma would cut rgba() and color-mix() in half, and every gradient argument
// list is full of them.
function splitTop(s, sep) {
    const out = [];
    let depth = 0, start = 0;
    for (let i = 0; i < s.length; i++) {
        if (s[i] === '(') depth++;
        else if (s[i] === ')') depth--;
        else if (depth === 0 && (sep === ',' ? s[i] === ',' : /\s/.test(s[i]))) {
            out.push(s.slice(start, i));
            start = i + 1;
        }
    }
    out.push(s.slice(start));
    return out.map(x => x.trim()).filter(Boolean);
}

const args = value => value.slice(value.indexOf('(') + 1, value.lastIndexOf(')'));

// A gradient's own arguments: the direction, the shape, the size and a bare colour hint are all
// positions in the ramp, not colours in it.
const GEOMETRY = /^(to|at|in|from|circle|ellipse|closest-side|closest-corner|farthest-side|farthest-corner)$|^-?[\d.]+[a-z%]*$/i;

function gradientCalls(value) {
    const out = [];
    const re = /(?:repeating-)?(?:linear|radial|conic)-gradient\(/gi;
    for (let m; (m = re.exec(value));) {
        let depth = 0, i = m.index + m[0].length - 1;
        const start = i + 1;
        for (; i < value.length; i++) {
            if (value[i] === '(') depth++;
            else if (value[i] === ')' && --depth === 0) break;
        }
        out.push(value.slice(start, i));
        re.lastIndex = i;
    }
    return out;
}

// color-mix(in srgb, A p%, B) — mixed premultiplied, which is what makes `transparent` behave:
// it is rgba(0, 0, 0, 0), so mixing p% of A into it yields A's own colour at p alpha rather than
// a colour dragged towards black. The composite against the real backdrop happens in `score`.
function mixColors(a, b, pa) {
    const alpha = a[3] * pa + b[3] * (1 - pa);
    const ch = i => alpha === 0 ? 0
        : Math.round((a[i] * a[3] * pa + b[i] * b[3] * (1 - pa)) / alpha);
    return [ch(0), ch(1), ch(2), alpha];
}

function resolveColor(value, vars, seen = new Set()) {
    value = value.trim();
    const varRef = value.match(/^var\((--[\w-]+)\)$/);
    if (varRef) {
        if (seen.has(varRef[1])) throw new Error('cycle at ' + varRef[1]);
        seen.add(varRef[1]);
        return resolveColor(vars[varRef[1]], vars, seen);
    }
    if (value === 'transparent') return [0, 0, 0, 0];
    if (value.startsWith('#')) return hex(value);
    if (/^color-mix\(/i.test(value)) {
        const parts = splitTop(args(value), ',');
        if (parts[0].replace(/\s+/g, ' ') !== 'in srgb') throw new Error('cannot resolve: ' + value);
        const operand = raw => {
            const t = splitTop(raw, ' ');
            const pct = t.length > 1 && t[t.length - 1].endsWith('%') ? parseFloat(t.pop()) : null;
            return { color: resolveColor(t.join(' '), vars, new Set(seen)), pct };
        };
        const a = operand(parts[1]), b = operand(parts[2]);
        const pa = a.pct ?? (b.pct === null ? 50 : 100 - b.pct);
        const pb = b.pct ?? 100 - pa;
        return mixColors(a.color, b.color, pa / (pa + pb));
    }
    const rgb = value.match(/^rgba?\(([^)]*)\)$/i);
    if (rgb) {
        const [main, slash] = rgb[1].split('/');
        const parts = main.split(/[,\s]+/).filter(Boolean);
        const alpha = slash ?? parts[3];
        const num = (p, scale) => p.endsWith('%') ? parseFloat(p) * scale : parseFloat(p);
        return [
            ...parts.slice(0, 3).map(p => Math.round(num(p, 2.55))),
            alpha === undefined ? 1 : num(alpha.trim(), 0.01),
        ];
    }
    throw new Error('cannot resolve: ' + value);
}

// One value, one or more colours: a flat colour is a single stop, a gradient is every stop it
// names. A pair is scored against the worst combination, because a boundary that clears its
// background at one end of a rail and vanishes at the other is not a boundary (ADR-140).
function resolveStops(value, vars, seen = new Set()) {
    value = value.trim();
    const varRef = value.match(/^var\((--[\w-]+)\)$/);
    if (varRef) {
        if (seen.has(varRef[1])) throw new Error('cycle at ' + varRef[1]);
        seen.add(varRef[1]);
        return resolveStops(vars[varRef[1]], vars, seen);
    }
    const calls = gradientCalls(value);
    if (!calls.length) return [resolveColor(value, vars, seen)];
    const stops = calls.flatMap(call => splitTop(call, ',').flatMap(arg => {
        const t = splitTop(arg, ' ');
        return GEOMETRY.test(t[0]) ? [] : [resolveColor(t[0], vars, new Set(seen))];
    }));
    if (!stops.length) throw new Error('cannot resolve: ' + value);
    return stops;
}

const over = (top, bottom) => top[3] >= 1
    ? top.slice(0, 3)
    : [0, 1, 2].map(i => Math.round(top[i] * top[3] + bottom[i] * (1 - top[3])));

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

const toHex = c => '#' + c.slice(0, 3).map(v => v.toString(16).padStart(2, '0')).join('').toUpperCase();

// A translucent surface is only as light as what it sits on, so the stack has to bottom out
// somewhere: the page ground, and under it the UA canvas, which is white.
const groundOf = vars => over(resolveStops(vars['--bg'], vars)[0], [255, 255, 255]);

function score(fgValue, bgValue, vars, ground) {
    const fgStops = resolveStops(fgValue, vars);
    const bgStops = resolveStops(bgValue, vars);
    let worst = null;
    for (const bgStop of bgStops) {
        const bg = over(bgStop, ground);
        for (const fgStop of fgStops) {
            const fg = over(fgStop, bg);
            const r = ratio(fg, bg);
            if (!worst || r < worst.r) worst = { r, fg, bg };
        }
    }
    return { ...worst, ramp: fgStops.length + bgStops.length > 2 };
}

// Pairs that actually occur in the app, not every combination: a token pair nothing renders is
// not a defect. The threshold attached to each pair is the contract that token carries.
//
// Two tokens are deliberately absent, and their absence is the decision (ADR-074):
//   --border  — a decorative hairline between cards and rows, never the only way to identify a
//               control. Holding it to 3:1 would draw the whole warm-paper surface as a wireframe.
//               The boundary that IS an affordance has its own token, --border-strong, below.
//   --abord   — the same, in the accent tint: it edges a chip or an active wash whose fill already
//               carries the state.
//
// The surfaces come in two families because the app carries two ink polarities (ADR-141). Paper
// is cream in both themes and its inks travel towards black; the wall is plaster by day and dark
// by night, and the one ink allowed on it — --wood-ink — travels the other way. Measuring a paper
// ink on the wall is therefore not a failing ratio but an unsatisfiable demand: a value at 4.5:1
// on night --canvas #251F13 is light, and a light value on night --surface #D9CEAE is not.
const PAPER = ['--surface', '--sheet', '--alt'];
const WALL = ['--bg', '--canvas'];
const pairs = [];
for (const s of WALL) {
    pairs.push({ fg: '--wood-ink', bg: s, min: 4.5, note: 'chrome text on the wall' });
}
for (const s of PAPER) {
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

// A gradient on a contract name is not a ratio that fails, it is a value that ships:
// generate-design-tokens.mjs copies these names verbatim into DesignTokens, which the blog and the
// landing page paint where a flat colour is expected (ADR-137). Returns the alias chain that leads
// to the gradient, or null when the token is flat.
function gradientTrail(value, vars, seen = new Set()) {
    const v = (value ?? '').trim();
    const varRef = v.match(/^var\((--[\w-]+)\)$/);
    if (varRef) {
        if (seen.has(varRef[1])) return null;
        seen.add(varRef[1]);
        const deeper = gradientTrail(vars[varRef[1]], vars, seen);
        return deeper && [varRef[1], ...deeper];
    }
    return gradientCalls(v).length ? [] : null;
}

// SUGGEST=1 prints, for every failing foreground, the nearest value along the line towards black
// or white — whichever the failing background is further from, since night holds a dark wall and
// a cream sheet in one theme (ADR-141) — that clears its worst surface. Hue is preserved:
// the search moves along a straight line in sRGB from the current colour, so the mix stays the
// same colour, only darker or lighter. It is a starting point for a decision, not the decision.
function suggest(from, bg, min, towards) {
    let lo = 0, hi = 1;
    for (let i = 0; i < 24; i++) {
        const mid = (lo + hi) / 2;
        const c = [0, 1, 2].map(k => Math.round(from[k] * (1 - mid) + towards[k] * mid));
        if (ratio(c, bg) >= min) hi = mid;
        else lo = mid;
    }
    return toHex([0, 1, 2].map(k => Math.round(from[k] * (1 - hi) + towards[k] * hi)));
}

let failures = 0, gradients = 0;
for (const [themeName, vars, own] of [['light', light, light], ['dark', dark, darkOwn]]) {
    console.log(`\n=== ${themeName} ===`);
    for (const name of CONTRACT) {
        if (own[`--${name}`] === undefined) continue;
        const trail = gradientTrail(own[`--${name}`], vars);
        if (!trail) continue;
        gradients++;
        const via = trail.length ? ` (via ${trail.join(' → ')})` : '';
        console.log(`FAIL  --${name} is a gradient${via} — a contract token ships verbatim into DesignTokens (ADR-137)`);
    }
    const ground = groundOf(vars);
    for (const p of pairs) {
        const { r, fg, bg, ramp } = score(vars[p.fg], vars[p.bg], vars, ground);
        const ok = r >= p.min;
        if (!ok) failures++;
        let line = `${ok ? 'ok  ' : 'FAIL'} ${r.toFixed(2).padStart(5)} (min ${p.min})  ${p.fg} on ${p.bg}  — ${p.note}`;
        if (ramp) line += `   worst stop ${toHex(fg)} on ${toHex(bg)}`;
        if (!ok && process.env.SUGGEST) {
            const towards = lum(bg) > 0.18 ? [0, 0, 0] : [255, 255, 255];
            line += `   → ${suggest(fg, bg, p.min, towards)}`;
        }
        if (!ok || process.env.VERBOSE) console.log(line);
    }
}
if (gradients) console.log(`\n${gradients} contract token(s) resolve to a gradient.`);
console.log(`\n${failures} failing pair(s). Set VERBOSE=1 to print the passing ones too.`);
process.exit(failures || gradients ? 1 : 0);
