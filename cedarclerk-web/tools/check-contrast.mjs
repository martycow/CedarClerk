// T-082 — WCAG contrast check over the token set, both themes.
//
// Reads the values out of src/styles.scss rather than taking a copied list, so it cannot drift
// from what ships; resolves the color-mix() derivations the same way a browser does (sRGB),
// composites alpha over the backdrop the pair is measured against, and walks a gradient along its
// whole ramp rather than only at its stops.
// Run: node tools/check-contrast.mjs            (from cedarclerk-web/) — the pair table, a gate
//      node tools/check-contrast.mjs --census                          — the stylesheets, a report
//
// Two modes because they answer different questions. The table states the contracts the tokens
// carry and fails the build when one breaks. The census walks the stylesheets the app ships and
// lists every foreground-on-background they declare that no pair in the table covers, which is the
// only way to find out that the table is measuring something else.
//
// Thresholds: 4.5 for body text, 3.0 for large text (>=19px semibold / 24px) and for the boundary
// of a control or a graphical object — WCAG 2.2 SC 1.4.3 and 1.4.11.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, resolve, sep } from 'node:path';
import { CONTRACT, SERVED } from './contract-tokens.mjs';

const src = readFileSync(resolve(import.meta.dirname, '../src/styles.scss'), 'utf8');
const generatedTokens = readFileSync(
    resolve(import.meta.dirname, '../../CedarClerk.Core/DesignTokens.generated.cs'), 'utf8');

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

// Colours carry alpha through resolution as [r, g, b, a]; only a composite against an opaque
// backdrop drops it. Channels stay unrounded — a ramp is walked in small steps and rounding each
// one loses the crossing the walk exists to find.
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
const GRADIENT = /^(?:repeating-)?(?:linear|radial|conic)-gradient\(/i;
// A var() may carry a fallback — popover.component.css writes `var(--sheet, #fff)`. Reading only
// the name resolves a branch the browser never takes when the token is missing, and reading the
// whole text as a name fails the parse.
const VAR_CALL = /^var\(\s*(--[\w-]+)\s*(?:,([\s\S]*))?\)$/;
const varBranch = (m, vars) => vars[m[1]] !== undefined ? vars[m[1]] : ((m[2] ?? '').trim() || null);

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
        : (a[i] * a[3] * pa + b[i] * b[3] * (1 - pa)) / alpha;
    return [ch(0), ch(1), ch(2), alpha];
}

function resolveColor(value, vars, seen = new Set()) {
    value = value.trim();
    const varRef = value.match(VAR_CALL);
    if (varRef) {
        if (seen.has(varRef[1])) throw new Error('cycle at ' + varRef[1]);
        seen.add(varRef[1]);
        const branch = varBranch(varRef, vars);
        if (branch === null) throw new Error('cannot resolve: ' + value);
        return resolveColor(branch, vars, seen);
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
        const sum = pa + pb;
        if (sum <= 0) throw new Error('cannot resolve: ' + value);
        const mixed = mixColors(a.color, b.color, pa / sum);
        // Two percentages are normalised to 100 and the shortfall is spent on alpha: `A 25%, B 25%`
        // is a half-transparent 50/50 mix, and reading it as an opaque one errs towards passing.
        if (sum < 100) mixed[3] *= sum / 100;
        return mixed;
    }
    const rgb = value.match(/^rgba?\(([^)]*)\)$/i);
    if (rgb) {
        const [main, slash] = rgb[1].split('/');
        const parts = main.split(/[,\s]+/).filter(Boolean);
        const alpha = slash ?? parts[3];
        const num = (p, scale) => p.endsWith('%') ? parseFloat(p) * scale : parseFloat(p);
        return [
            ...parts.slice(0, 3).map(p => num(p, 2.55)),
            alpha === undefined ? 1 : num(alpha.trim(), 0.01),
        ];
    }
    throw new Error('cannot resolve: ' + value);
}

// How finely the span between two stops is walked. The span is walked at all because a foreground
// that crosses its background crosses it between the stops and never at one, and a pair scored
// only at its stops reads that crossing as the pair's best moment instead of its worst.
const RAMP_STEPS = 16;

const position = t => {
    const m = t.match(/^(-?[\d.]+)([a-z%]*)$/i);
    return m ? { n: parseFloat(m[1]), unit: m[2].toLowerCase() } : null;
};

// Two stops that meet at one position are a hard edge. Interpolating across it would invent
// colours the value never paints, and an invented colour can sit nearer the foreground than any
// real one does.
const meets = (a, b) => !!a && !!b && a.n === b.n && (a.unit === b.unit || a.n === 0);

function stopsOf(inner, vars, seen) {
    const out = [];
    for (const arg of splitTop(inner, ',')) {
        const t = splitTop(arg, ' ');
        if (GEOMETRY.test(t[0])) continue;
        const pos = t.slice(1).map(position).filter(Boolean);
        out.push({
            color: resolveColor(t[0], vars, new Set(seen)),
            start: pos[0] ?? null,
            end: pos[pos.length - 1] ?? null,
        });
    }
    if (!out.length) throw new Error('no colour stops in: ' + inner);
    return out;
}

const rampOf = stops => {
    const out = [stops[0].color];
    for (let i = 0; i + 1 < stops.length; i++) {
        if (!meets(stops[i].end, stops[i + 1].start)) {
            for (let k = 1; k < RAMP_STEPS; k++)
                out.push(mixColors(stops[i + 1].color, stops[i].color, k / RAMP_STEPS));
        }
        out.push(stops[i + 1].color);
    }
    return out;
};

// One value, one or more layers. A background is a comma-separated stack and the later a layer is
// written the further underneath it paints, so a transparent stop in the top layer shows the layer
// below — not the page ground. Each layer resolves to the ramp of colours it can paint; anything
// after the image inside a layer is position and size.
function layersOf(value, vars, seen = new Set()) {
    value = (value ?? '').trim();
    const varRef = value.match(VAR_CALL);
    if (varRef) {
        if (seen.has(varRef[1])) throw new Error('cycle at ' + varRef[1]);
        seen.add(varRef[1]);
        const branch = varBranch(varRef, vars);
        if (branch === null) throw new Error('cannot resolve: ' + value);
        return layersOf(branch, vars, seen);
    }
    const out = [];
    for (const layer of splitTop(value, ',')) {
        const head = splitTop(layer, ' ')[0];
        if (GRADIENT.test(head)) out.push(rampOf(stopsOf(args(head), vars, seen)));
        else if (VAR_CALL.test(head)) out.push(...layersOf(head, vars, new Set(seen)));
        else out.push([resolveColor(head, vars, new Set(seen))]);
    }
    return out;
}

const srcOver = (top, bottom) => {
    const a = top[3] + bottom[3] * (1 - top[3]);
    if (a === 0) return [0, 0, 0, 0];
    return [...[0, 1, 2].map(i => (top[i] * top[3] + bottom[i] * bottom[3] * (1 - top[3])) / a), a];
};

// Every continuous path a value can paint: the top layer runs along its ramp while the layers
// under it are held at each combination they can show through it.
function curvesOf(value, vars) {
    const layers = layersOf(value, vars);
    let unders = [[0, 0, 0, 0]];
    for (let i = layers.length - 1; i >= 1; i--)
        unders = unders.flatMap(u => layers[i].map(c => srcOver(c, u)));
    return unders.map(u => layers[0].map(c => srcOver(c, u)));
}

const key = c => [0, 1, 2].map(i => Math.round(c[i])).join(',');

// The opaque colours a value actually shows, given what it is painted on.
function painted(value, vars, backdrops) {
    const seenKeys = new Set(), out = [];
    for (const b of backdrops)
        for (const curve of curvesOf(value, vars))
            for (const c of curve) {
                const p = srcOver(c, b);
                const k = key(p);
                if (seenKeys.has(k)) continue;
                seenKeys.add(k);
                out.push(p);
            }
    return out;
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

const toHex = c => '#' + c.slice(0, 3).map(v => Math.round(v).toString(16).padStart(2, '0')).join('').toUpperCase();

// A translucent surface is only as light as what it sits on, so the stack has to bottom out
// somewhere: the page ground, and under it the UA canvas, which is white. Every colour the ground
// can show is a backdrop — taking the first one it names would measure a ramp by its head.
//
// The ground is read off the body rule rather than named here. body paints paper while --bg is the
// wall, and the two are opposite poles at night (ADR-141), so a name written down here is a premise
// that stays right only until the palette moves under it.
function groundValue(text) {
    const flat = text.replace(/\/\*[\s\S]*?\*\//g, '');
    let found = null;
    for (const m of flat.matchAll(/(?:^|[\n};])([^{}@;]*?)\{([^{}]*)\}/g)) {
        if (!/(^|,)\s*body\s*(,|$)/.test(m[1].trim())) continue;
        for (const d of m[2].matchAll(/(?:^|;)\s*background(?:-color)?\s*:\s*([^;]+)/g)) found = d[1].trim();
    }
    return found;
}

const GROUND = groundValue(src);
if (!GROUND) throw new Error('no body background in styles.scss — the page ground is unknown');
// A server-rendered page carries its own body rule, and it is not always the app's: the ground is
// therefore a parameter rather than a constant.
const groundsOf = (vars, ground = GROUND) => painted(ground, vars, [[255, 255, 255, 1]]);

const between = (a, b, t) => [...[0, 1, 2].map(i => a[i] * (1 - t) + b[i] * t), 1];

// Where a ramp passes through the luminance of its own background the ratio is exactly 1.00, and
// the colour it happens at is one no stop names.
function crossing(a, b, target) {
    let lo = 0, hi = 1;
    for (let i = 0; i < 24; i++) {
        const mid = (lo + hi) / 2;
        if ((lum(a) - target) * (lum(between(a, b, mid)) - target) > 0) lo = mid;
        else hi = mid;
    }
    return between(a, b, hi);
}

function score(fgValue, bgValue, vars, backdrops) {
    const bgs = painted(bgValue, vars, backdrops);
    const curves = curvesOf(fgValue, vars);
    let worst = null, fgCount = 0;
    for (const b of bgs) {
        const lb = lum(b);
        for (const curve of curves) {
            fgCount = Math.max(fgCount, curve.length);
            let prev = null;
            for (const c of curve) {
                const f = srcOver(c, b);
                const r = ratio(f, b);
                if (!worst || r < worst.r) worst = { r, fg: f, bg: b };
                if (prev && (lum(prev) - lb) * (lum(f) - lb) < 0)
                    worst = { r: 1, fg: crossing(prev, f, lb), bg: b };
                prev = f;
            }
        }
    }
    return { ...worst, ramp: bgs.length > 1 || fgCount > 1 };
}

// Pairs that actually occur in the app, not every combination: a token pair nothing renders is
// not a defect. The threshold attached to each pair is the contract that token carries.
//
// Beyond fg / bg / min a pair may carry:
//   under   — what a translucent background is painted on, when it does not lie on the page ground.
//   alt     — a second foreground that may carry the pair in place of the first. The focus ring is
//             two layers (ADR-140) and either boundary identifies it; demanding both would be a
//             stricter contract than the decision, failing surfaces the decision covers.
//   except  — a shortfall a decision accepts, and why, naming the decision. The pair is still
//             measured and still printed;
//             the only thing it stays out of is the failure count, and it is printed again if it
//             ever starts passing.
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
// --paper-bright is the fourth paper and not a special case: it is the stock a field is cut
// from — the bench Input, SpecRow's field variant, PaperCard's bright sheet — and body ink,
// a placeholder and a control boundary all land on it exactly as they do on the other three.
const PAPER = ['--surface', '--sheet', '--alt', '--paper-bright'];
const WALL = ['--bg', '--canvas'];
const pairs = [];
for (const s of WALL) {
    pairs.push({ fg: '--wood-ink', bg: s, min: 4.5, note: 'chrome text on the wall' });
    // The page meta line, a margin note and a ghost button's label all stand on the wall in the
    // soft ink; the ghost's hover wash is translucent, so the full ink is scored through it.
    pairs.push({ fg: '--wood-ink-soft', bg: s, min: 4.5, note: 'soft text on the wall' });
    pairs.push({ fg: '--wood-ink', bg: '--hover', under: s, min: 4.5, note: 'ghost label hovered on the wall' });
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
// Every accent fill in the product, and most of them are server-rendered: the app's primary button,
// the landing page's .btn-accent, and on the blog the channel avatar in the sticky header,
// .reg-submit and the comment form's button. Narrowing this pair back to the button would leave
// three public pages, served to readers who never log in, with nothing measuring them.
pairs.push({ fg: '--sheet', bg: '--accent', min: 4.5, note: 'ink on an accent fill' });
pairs.push({ fg: '--sheet', bg: '--danger', min: 4.5, note: 'text on a destructive button' });

// The washes. A state badge is its own ink on a tint of itself, which is a harder ratio than the
// same ink on paper and the one the app actually paints — .status-badge, .state-chip, .chip.warn,
// .prio.p1, .sg-badge-*, .pr-part.pr-done and the two banners. --t2 rides
// the accent wash the same way, on .hint-bubble, .notice and the blog's .sep-label.
pairs.push({ fg: '--accent', bg: '--asoft', min: 4.5, note: 'accent label on its own wash' });
pairs.push({ fg: '--t2', bg: '--asoft', min: 4.5, note: 'secondary text on the accent wash' });
pairs.push({ fg: '--text', bg: '--asoft', min: 4.5, note: 'body text on the accent wash' });
pairs.push({ fg: '--text', bg: '--code-bg', min: 4.5, note: 'inline code on its own wash' });
// Danger on the accent wash, which is not a colour scheme but a place two of them meet: the poll
// editor's card is washed in the accent and its remove glyph reddens on hover. The wash follows the
// user's accent, so this pair is the one place a state ink is scored against a tone it was not
// derived from, and the darkest preset is what sets --danger's value.
pairs.push({ fg: '--danger', bg: '--asoft', min: 4.5, note: 'destructive glyph on the accent wash' });
for (const [ink, wash] of [['--ok', '--ok-soft'], ['--warn', '--warn-soft'], ['--danger', '--danger-soft'],
    ['--brass-ink', '--brass-soft']]) {
    pairs.push({ fg: ink, bg: wash, min: 4.5, note: 'state label on its own wash' });
    pairs.push({ fg: '--text', bg: wash, min: 4.5, note: 'body text on a state wash' });
}

// The hover washes are translucent by design — a row hover has to work on whatever paper the row
// lies on — so each is measured on all three papers. --t2 carries icons here and not text: what is
// left on this wash is glyphs, the square icon buttons (.mini, .theme-toggle, .toolbar
// button) including the few whose face is a character rather than an SVG, and WCAG 1.4.11 is the
// floor a glyph owes. A LABEL on this wash is a defect in the rule, not a number to lower: it takes
// --text and lands on the pair above, as the toolbar's GIF button does.
for (const s of PAPER) {
    pairs.push({ fg: '--text', bg: '--hover', under: s, min: 4.5, note: 'body text in a hovered row' });
    pairs.push({ fg: '--t2', bg: '--hover', under: s, min: 4.5, note: 'secondary text in a hovered row' });
    pairs.push({ fg: '--text', bg: '--hover-strong', under: s, min: 4.5, note: 'label on a hovered control' });
    pairs.push({ fg: '--t2', bg: '--hover-strong', under: s, min: 3.0, note: 'icon glyph on a hovered control' });
    pairs.push({ fg: '--danger', bg: '--hover-danger', under: s, min: 4.5, note: 'remove icon on its hover wash' });
}
for (let i = 1; i <= 6; i++) pairs.push({ fg: `--series-${i}`, bg: '--surface', min: 3.0, note: 'chart series' });

// The hashed avatar fills. These carry an initial, so the floor is text and not the 3:1 a swatch
// would owe. They are measured here because the census structurally cannot reach them: the fill is
// bound with [style.background] in the editor's channel list and the admin user list, so it never
// appears in a stylesheet for the walker to pair with the ink. The count is read rather than fixed
// at six — a seventh fill added to the palette and left unmeasured is the failure this prevents.
const AVATAR_FILLS = Object.keys(light).filter(n => /^--avatar-\d+$/.test(n));
if (!AVATAR_FILLS.length) throw new Error('no --avatar-N fills in styles.scss — the avatar ink is unmeasured');
for (const fill of AVATAR_FILLS) {
    pairs.push({ fg: '--avatar-ink', bg: fill, min: 4.5, note: 'initial on a hashed avatar' });
}

// The primary button is a ramp, not a fill: --grad-pine runs from the lit face of the accent to the
// accent itself, so a label that clears 4.5:1 on --accent can still fail at the top of its own
// button. Both inks that land there are measured.
pairs.push({ fg: '--sheet', bg: '--grad-pine', min: 4.5, note: 'text on a primary button face' });
pairs.push({ fg: '--text-on-pine', bg: '--grad-pine', min: 4.5, note: 'bench ink on a primary button face' });

// The layered material. It is a stack, and the ink is scored against the whole stack: the pencil
// rules are drawn on paper.
pairs.push({ fg: '--text', bg: '--grid-graph', under: '--surface', min: 4.5, note: 'body text on ruled paper' });
pairs.push({ fg: '--t2', bg: '--grid-graph', under: '--surface', min: 4.5, note: 'secondary text on ruled paper' });
// The chart's graph paper is painted on the shelf sheet, so the ground that is scored is the one
// the ink actually lands on as well as the deeper cream ADR-158 names.
pairs.push({ fg: '--text', bg: '--grid-graph', under: '--sheet', min: 4.5, note: 'body text on the chart sheet' });
pairs.push({ fg: '--t2', bg: '--grid-graph', under: '--sheet', min: 4.5, note: 'secondary text on the chart sheet' });

// The bench stocks and plaques, none of which is paper and none of which takes a paper ink. A leaf
// is dyed card carrying its own green; a rail button and a wood plaque are chrome, so the cream
// that goes on wood is what is legible on them; and the priority-one chip is filled with resin,
// the one metal light enough to flip its ink back to dark. Each ramp is scored at both stops
// rather than as one token: the ink is flat and the ramp does not cross it, so the stops bound the
// ratio, and a failing stop is named instead of a gradient being named for it.
pairs.push({ fg: '--leaf-ink', bg: '--leaf-bg', min: 4.5, note: 'leaf label on its lit stop' });
pairs.push({ fg: '--leaf-ink', bg: '--leaf-bg-2', min: 4.5, note: 'leaf label on its shaded stop' });
// A dried leaf is a filter switched off, and its label stands at the floor a disabled control
// carries rather than at a live label's; the stock is translucent, so every paper is under it.
for (const s of PAPER) {
    pairs.push({ fg: '--leaf-dried-ink', bg: '--leaf-dried-bg', under: s, min: 3.0, note: 'dried leaf label' });
    pairs.push({ fg: '--leaf-ink', bg: '--leaf-dried-bg', under: s, min: 4.5, note: 'unpicked leaf label' });
}
pairs.push({ fg: '--rail-ink', bg: '--rail-lo', min: 4.5, note: 'rail button label' });
// The one painted button on the public header: the feed offer wears resin, so its label is read on
// resin and on the lit stop it takes when hovered.
pairs.push({ fg: '--rail-edge', bg: '--resin', min: 4.5, note: 'feed button label on resin' });
pairs.push({ fg: '--rail-edge', bg: '--resin-hi', min: 4.5, note: 'feed button label on lit resin' });
pairs.push({ fg: '--rail-ink', bg: '--rail-edge', min: 4.5, note: 'rail button label, pressed or hovered' });
// The rail button's resting and hovered faces are tints over whichever wood is behind them — the
// rail itself, or a shelf header's sign tile when the action sits in the header.
for (const wood of ['--surface-rail', '--grad-sign-tile']) {
    pairs.push({ fg: '--rail-ink', bg: '--rail-btn-face', under: wood, min: 4.5, note: 'rail button label on its resting face' });
    pairs.push({ fg: '--rail-ink', bg: '--rail-btn-face-hover', under: wood, min: 4.5, note: 'rail button label on its hovered face' });
}
pairs.push({ fg: '--rail-ink', bg: '--wood-lo', min: 4.5, note: 'priority chip on the lit stop of its plaque' });
pairs.push({ fg: '--rail-ink', bg: '--wood-edge', min: 4.5, note: 'priority chip on the shaded stop of its plaque' });
pairs.push({ fg: '--rail-edge', bg: '--resin-hi', min: 4.5, note: 'priority-one chip on the lit stop of its resin' });
pairs.push({ fg: '--rail-edge', bg: '--resin', min: 4.5, note: 'priority-one chip on the shaded stop of its resin' });

// The served chrome (ADR-215): the blog and the landing still cut their captions into the sign
// tile and stand their brand and crumbs on the rail gradient; both are ramps, so each is scored
// along its stops. The app's own shell is paper now (ADR-239) and paints none of these.
pairs.push({ fg: '--rail-ink', bg: '--grad-sign-tile', min: 4.5, note: 'caption carved into a sign tile' });
pairs.push({ fg: '--rail-ink-dim', bg: '--grad-sign-tile', min: 4.5, note: 'resting index tab' });
// The account plaque and the rule both write the rail's darkest wood on the brass ramp.
pairs.push({ fg: '--rail-edge', bg: '--grad-brass', min: 4.5, note: 'initial on the brass avatar plaque' });
pairs.push({ fg: '--rail-ink', bg: '--surface-rail', min: 4.5, note: 'brand and crumb on the rail' });
// The conifer beside the wordmark is a graphical object, so 3:1, and the wood under it is the
// whole gradient rather than one stop — it is small enough to sit on any of them.
pairs.push({ fg: '--pine-mark', bg: '--surface-rail', min: 3.0, note: 'the pine on the rail' });

// The soft cream, which is spent on two things and neither of them is a word: the crumb
// separator (`content: '/'`, never announced) and the resting face of a tool-strip button, whose
// face is a glyph and whose name is in its title. Both take the decoration floor for the reason
// --t3 does on paper (ADR-074) — held to 4.5 it collapses onto the cream it sits below. A label
// that reached for it takes --rail-ink, on the brand-and-crumb pair further up.
pairs.push({ fg: '--rail-ink-soft', bg: '--surface-rail', min: 3.0, note: 'crumb separator, resting tool glyph' });

// The editor's tool strip (ADR-150), which puts a row of controls on the rail for the first time:
// the app had chrome that carried captions and chrome that carried buttons, and never one band
// carrying both. The hover wash is the app's own, translucent by design, so what it lies on is
// part of its colour and the rail is named rather than guessed at.
pairs.push({ fg: '--rail-ink', bg: '--hover-strong', under: '--surface-rail', min: 4.5, note: 'tool label on a hovered control' });
pairs.push({ fg: '--brass-hi', bg: '--surface-rail', min: 4.5, note: 'brass caption on the rail' });
pairs.push({ fg: '--brass-hi', bg: '--rail-edge', min: 4.5, note: 'brass label on a recessed chip' });
// Severity on chrome is a stamp and never a tinted glyph: the rust ink is derived against paper
// and measures 1.13:1 laid straight on the rail. The wash is opaque, so it carries that ink onto
// any ground — what is measured here is the chip against the wood it is pinned to.
pairs.push({ fg: '--danger-soft', bg: '--surface-rail', min: 3.0, note: 'a stamped chip against the rail' });

// Graph paper (ADR-158). The chart draws on the ruled ground rather than on flat paper, and the
// two rules cross: at an intersection the ink is laid twice, which is the darkest the ground gets
// and the only stop worth scoring a line against. Every line in the categorical palette is
// measured there, not just the ones the reference screen happens to plot.
for (const ground of ['--surface', '--sheet']) {
    pairs.push({ fg: '--brass-ink', bg: '--grid-graph', under: ground, min: 4.5, note: 'event label pencilled on the axis' });
    for (let i = 1; i <= 6; i++) {
        pairs.push({ fg: `--series-${i}`, bg: '--grid-graph', under: ground, min: 3.0, note: 'chart series on ruled paper' });
    }
}

// The two bars the ported screens draw — an audience row on the stats shelf and a sprint's
// progress on the hub. A bar is read by its length, so the fill owes 3:1 against the track it
// runs in and not against the page. Both are accent-derived, so the preset re-run reaches them.
pairs.push({ fg: '--pine', bg: '--alt', min: 3.0, note: 'bar fill against its track' });
pairs.push({ fg: '--grad-pine', bg: '--surface', min: 3.0, note: 'progress fill against its track' });

// Cork. The shelf panel's second sheet tone makes --wood-hi a text background for the first time,
// which the wood block's own note says only --wood-ink may do.
pairs.push({ fg: '--wood-ink', bg: '--wood-hi', min: 4.5, note: 'note pinned to a cork board' });

// The focus ring (ADR-140), which nothing measured. Two layers, one global rule, and the surfaces
// below are every one a focusable control sits on — the gradients standing for their own stops.
// The boundary inside the band is measured per surface because the halo is translucent, and so is
// only as light as whatever it lies on.
const RING_SURFACES = [
    ['--sheet', null], ['--surface', null], ['--alt', null],
    ['--paper-bright', null], ['--paper-edge', null],
    ['--bg', null], ['--canvas', null],
    ['--leaf-bg', null], ['--leaf-bg-2', null],
    ['--surface-rail', null], ['--grad-sign-tile', null],
    ['--accent', null], ['--grad-pine', null],
    ['--shelf-frame', 'ADR-140 — neither ring layer clears the frame where it is light; the '
        + 'boundary inside the band carries it, as it does for cork and the ruler. The drawer pull '
        + 'is focusable and sits on the lip, which paints this ramp'],
    ['--wood-hi', 'ADR-140 — the light stop of --shelf-frame, read flat, where that shortfall '
        + 'is at its worst'],
    ['--grad-brass', 'ADR-140 — the halo vanishes into the ruler and the dark outline is the whole '
        + 'indicator, which the brass ramp holds at its light stop and not at its dark one'],
];
for (const [s, except] of RING_SURFACES) {
    pairs.push({
        fg: '--brass-edge', alt: '--focus-halo', bg: s, min: 3.0, except, exceptIn: 'light',
        note: 'focus ring against the surface',
    });
    pairs.push({
        fg: '--brass-edge', bg: '--focus-halo', under: s, min: 3.0,
        note: 'focus outline against its own halo',
    });
}

// A gradient on a contract name is not a ratio that fails, it is a value that ships:
// generate-design-tokens.mjs copies these names verbatim into DesignTokens, which the blog and the
// landing page paint where a flat colour is expected (ADR-137). Returns the alias chain that leads
// to the gradient, or null when the token is flat.
function gradientTrail(value, vars, seen = new Set()) {
    const v = (value ?? '').trim();
    const varRef = v.match(VAR_CALL);
    if (varRef) {
        if (seen.has(varRef[1])) return null;
        seen.add(varRef[1]);
        const deeper = gradientTrail(varBranch(varRef, vars), vars, seen);
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
        const c = [0, 1, 2].map(k => from[k] * (1 - mid) + towards[k] * mid);
        if (ratio(c, bg) >= min) hi = mid;
        else lo = mid;
    }
    return toHex([0, 1, 2].map(k => from[k] * (1 - hi) + towards[k] * hi));
}

let failures = 0, gradients = 0, excepted = 0, offContract = 0, stale = 0;

function measure(label, vars, list, themeName) {
    const grounds = groundsOf(vars);
    for (const p of list) {
        const backdrops = p.under ? painted(vars[p.under], vars, grounds) : grounds;
        let best = score(vars[p.fg], vars[p.bg], vars, backdrops);
        let carrier = p.fg;
        if (p.alt) {
            const other = score(vars[p.alt], vars[p.bg], vars, backdrops);
            if (other.r > best.r) { best = other; carrier = p.alt; }
        }
        const { r, fg, bg, ramp } = best;
        const ok = r >= p.min;
        // An exception is forgiven only in the theme it was derived in. A bare one bought silence
        // in the other theme too, where the same pair passes by a wide margin (ADR-172 clause 5).
        const forgiven = p.except && (!p.exceptIn || p.exceptIn === themeName);
        let mark = ok ? 'ok   ' : 'FAIL ';
        if (!ok && forgiven) { mark = 'xfail'; excepted++; }
        else if (!ok) failures++;
        const named = p.alt ? `${carrier} (of ${p.fg}/${p.alt})` : p.fg;
        let line = `${mark}${r.toFixed(2).padStart(5)} (min ${p.min})  ${label}${named} on ${p.bg}`
            + `${p.under ? ` over ${p.under}` : ''}  — ${p.note}`;
        if (ramp) line += `   worst ${toHex(fg)} on ${toHex(bg)}`;
        if (mark === 'xfail') line += `   accepted: ${p.except}`;
        if (ok && forgiven) line += `   — clears its floor in this theme`;
        if (!ok && process.env.SUGGEST) {
            const towards = lum(bg) > 0.18 ? [0, 0, 0] : [255, 255, 255];
            line += `   → ${suggest(fg, bg, p.min, towards)}`;
        }
        if (!ok || (ok && forgiven) || process.env.VERBOSE) console.log(line);
    }
}

// The accent is not only what this file sets. core/appearance.service.ts injects a preset onto
// :root for every logged-in user, and with the skin retired that rule wins on source order
// (ADR-141) — so a preset nobody measured is the accent most screens actually paint. The list is
// read out of the service for the same reason the palette is read out of styles.scss.
const service = readFileSync(resolve(import.meta.dirname, '../src/app/core/appearance.service.ts'), 'utf8');
const PRESETS = [...service.matchAll(
    /name:\s*'([^']+)',\s*hex:\s*'(#[0-9a-fA-F]{6})',\s*night:\s*'(#[0-9a-fA-F]{6})'/g,
)].map(m => ({ name: m[1], light: m[2], dark: m[3] }));

// Everything downstream of --accent: the wash and the button ramp derive from it by color-mix, so
// overriding the one name carries the preset through all of them.
const ACCENT_DEPENDENT = new Set(['--accent', '--asoft', '--abord', '--pine', '--pine-hi', '--pine-deep', '--grad-pine']);
const accentPairs = pairs.filter(p => [p.fg, p.alt, p.bg, p.under].some(n => ACCENT_DEPENDENT.has(n)));

const THEMES = [['light', light, light], ['dark', dark, darkOwn]];
const accentOf = (preset, themeName) => themeName === 'dark' ? preset.dark : preset.light;

function runContract() {
    for (const [themeName, vars, own] of THEMES) {
        console.log(`\n=== ${themeName} ===`);
        for (const name of CONTRACT) {
            if (own[`--${name}`] === undefined) continue;
            const trail = gradientTrail(own[`--${name}`], vars);
            if (!trail) continue;
            gradients++;
            const via = trail.length ? ` (via ${trail.join(' → ')})` : '';
            console.log(`FAIL  --${name} is a gradient${via} — a contract token ships verbatim into DesignTokens (ADR-137)`);
        }
        measure('', vars, pairs, themeName);
        for (const preset of PRESETS)
            measure(`[${preset.name}] `, { ...vars, '--accent': accentOf(preset, themeName) }, accentPairs, themeName);
    }
    if (!PRESETS.length) {
        failures++;
        console.log('\nFAIL  no accent presets read from core/appearance.service.ts — the injected accent is unmeasured (ADR-141)');
    }
    // What the server is actually served, which is not the same question as what the list names.
    // CONTRACT states which tokens are meant to reach DesignTokens; the generated file is what
    // does. They part company the moment a name joins the list and `npm run tokens:generate` is
    // not run, and nothing notices: DesignTokenDriftTests walks the generated dictionary, so a
    // name missing from it is a name it never visits.
    const served = new Set([...generatedTokens.matchAll(/\["([\w-]+)"\]/g)].map(m => m[1]));
    for (const name of SERVED) {
        if (light[`--${name}`] === undefined || served.has(name)) continue;
        stale++;
        console.log(`\nFAIL  --${name} is meant to reach the server and is declared in styles.scss, but DesignTokens does not carry it — run \`npm run tokens:generate\` (ADR-137, ADR-177)`);
    }
    // The other half of the same rule the gradient check enforces. A server-rendered page receives
    // the contract names and nothing else, so a var() outside the contract and without a fallback
    // resolves to nothing there and its declaration silently does not apply — no error, no wrong
    // colour, just a property that is not set. styles.scss states the hazard on --fs-read; the
    // landing page carried eight of them, which is what a rule nothing checks is worth.
    for (const f of serverSheets()) {
        for (const m of f.code.matchAll(/var\(\s*(--[\w-]+)\s*(,?)/g)) {
            if (m[2] === ',' || served.has(m[1].slice(2))) continue;
            offContract++;
            console.log(`\nFAIL  ${f.rel}:${lineOf(f.code, m.index)} paints ${m[1]}, which DesignTokens does not serve (ADR-137)`);
        }
    }
    if (gradients) console.log(`\n${gradients} contract token(s) resolve to a gradient.`);
    if (stale) console.log(`${stale} contract token(s) missing from DesignTokens.`);
    if (offContract) console.log(`${offContract} server-rendered reference(s) to a token outside the contract.`);
    if (excepted) console.log(`${excepted} accepted exception(s).`);
    console.log(`\n${failures} failing pair(s). Set VERBOSE=1 to print the passing ones too.`);
    process.exit(failures || gradients || offContract || stale ? 1 : 0);
}

// ════ CENSUS ═══════════════════════════════════════════════════════════════════════════════════
//
// The list above is written by hand, and a hand-written list measures what someone remembered. The
// census walks the stylesheets instead and reports every foreground-on-background the app declares
// that no pair covers.
//
// What it cannot do is decide a backdrop. A rule's real backdrop is a DOM ancestor, and there is no
// DOM here — so every combination carries how its backdrop was arrived at, and an assumed one is
// never printed as a measured one.

const SRC_DIR = resolve(import.meta.dirname, '../src');
const SHEET_EXT = ['.css', '.scss'];

// Comments are blanked rather than cut so every offset still maps to its own line.
const blankOut = s => s.replace(/[^\n]/g, ' ');
const stripComments = s => s
    .replace(/\/\*[\s\S]*?\*\//g, blankOut)
    .replace(/(^|[^:])(\/\/[^\n]*)/g, (m, p, c) => p + blankOut(c));

// Some components keep their CSS in a `styles` template literal, and a scanner that reads only
// stylesheet files cannot see a painted rule that happens to live in a .ts. Everything outside the
// literals is blanked, which leaves the CSS at its true offsets in the .ts file.
function styleLiterals(code) {
    const keep = [];
    for (const m of code.matchAll(/\bstyles\s*:\s*(\[|`)/g)) {
        let i = m.index + m[0].length - 1;
        const array = code[i] === '[';
        let depth = array ? 1 : 0;
        if (!array) i--;
        for (; i < code.length; i++) {
            const ch = code[i];
            if (array && ch === '[') depth++;
            else if (array && ch === ']' && --depth === 0) break;
            else if (ch === '`') {
                let j = i + 1;
                while (j < code.length && (code[j] !== '`' || code[j - 1] === '\\')) j++;
                keep.push([i + 1, j]);
                i = j;
                if (!array) break;
            }
        }
    }
    if (!keep.length) return null;
    let out = blankOut(code);
    for (const [a, b] of keep) out = out.slice(0, a) + code.slice(a, b) + out.slice(b);
    return out;
}

// Three pages are painted from C# rather than from a stylesheet — the public blog, the landing page
// and the draft preview — and until now nothing scanned them: DesignTokens is checked, and the CSS
// that uses it was not. They are also the only surface served to a reader who never logs in. The
// treatment is the one above: everything that is not CSS is blanked in place, so the offsets, and
// so the reported line numbers, stay those of the .cs file.
const SERVER_DIR = resolve(import.meta.dirname, '../../CedarClerk.Server');

// A run of three or more quotes opens a C# raw string, and the dollars in front of it say how many
// braces an interpolation hole takes — the only thing that separates a hole from the braces of a
// CSS rule, since a raw string has no backslash escaping to lean on.
function rawStrings(code) {
    const out = [];
    const open = /(\$*)("{3,})/g;
    for (let m; (m = open.exec(code));) {
        const from = m.index + m[0].length;
        const close = new RegExp(`"{${m[2].length},}`, 'g');
        close.lastIndex = from;
        const end = close.exec(code);
        out.push({ from, to: end ? end.index : code.length, holes: m[1].length });
        open.lastIndex = end ? end.index + end[0].length : code.length;
    }
    return out;
}

// A hole is opened by a run of at least `holes` braces and closed when the runs balance again, so
// `{{LIGHT_TOKENS}}` inside a `$"""` hole's own argument list closes nothing. With no dollars there
// are no holes at all, only the Replace-a-placeholder marks the shell templates carry; both stand
// where a declaration list will be, and the declarations are the theme this run already holds.
function blankHoles(text, holes) {
    if (!holes) return text.replace(/\{\{[A-Z_]+\}\}/g, blankOut);
    const run = (i, ch) => { let n = 0; while (text[i + n] === ch) n++; return n; };
    const out = [...text];
    for (let i = 0; i < text.length;) {
        const n = text[i] === '{' ? run(i, '{') : 0;
        if (n < holes) { i += n || 1; continue; }
        let depth = n, j = i + n;
        while (j < text.length && depth > 0) {
            if (text[j] === '{') { const k = run(j, '{'); depth += k; j += k; }
            else if (text[j] === '}') { const k = run(j, '}'); depth -= k; j += k; }
            else j++;
        }
        for (let p = i; p < j; p++) if (out[p] !== '\n') out[p] = ' ';
        i = j;
    }
    return out.join('');
}

const STYLE_EL = /<style>([\s\S]*?)<\/style>/g;

function csharpCss(code) {
    const literals = rawStrings(code);
    const keep = [], injected = new Set();
    for (const lit of literals) {
        const body = code.slice(lit.from, lit.to);
        for (const m of body.matchAll(STYLE_EL)) {
            const at = lit.from + m.index + '<style>'.length;
            keep.push({ from: at, to: at + m[1].length, holes: lit.holes });
            // `<style>{Css.Replace(...)}</style>` — the sheet is a second literal, and this is the
            // only place its name appears in the position of a stylesheet.
            for (const id of m[1].matchAll(/\{+\s*([A-Za-z_]\w*)/g)) injected.add(id[1]);
        }
    }
    for (const lit of literals) {
        const decl = code.slice(Math.max(0, lit.from - 240), lit.from)
            .match(/\bstring\s+([A-Za-z_]\w*)\s*=\s*\$*"{3,}$/);
        if (decl && injected.has(decl[1])) keep.push({ from: lit.from, to: lit.to, holes: lit.holes });
    }
    if (!keep.length) return null;
    let out = blankOut(code);
    for (const k of keep) out = out.slice(0, k.from) + blankHoles(code.slice(k.from, k.to), k.holes) + out.slice(k.to);
    return out;
}

function serverSheets() {
    const out = [];
    const walk = dir => {
        for (const name of readdirSync(dir)) {
            if (name === 'bin' || name === 'obj') continue;
            const p = join(dir, name);
            if (statSync(p).isDirectory()) { walk(p); continue; }
            if (!name.endsWith('.cs')) continue;
            const only = csharpCss(readFileSync(p, 'utf8'));
            if (!only) continue;
            const code = stripComments(only);
            out.push({
                rel: 'CedarClerk.Server/' + relative(SERVER_DIR, p).split(sep).join('/'),
                code, server: true, ground: groundValue(code) ?? GROUND,
            });
        }
    };
    walk(SERVER_DIR);
    return out;
}

// What a server-rendered page actually receives. DesignTokens carries the contract names and the
// bench materials (ADR-137, ADR-177) and nothing else, so a name the app resolves through
// styles.scss — --fs-ui, a private derivation, anything on neither list — is simply absent there,
// and resolving one here would score a colour the reader never gets.
const serverVars = vars => Object.fromEntries(
    SERVED.map(n => [`--${n}`, vars[`--${n}`]]).filter(([, v]) => v !== undefined));

function sheets() {
    const out = [];
    const walk = dir => {
        for (const name of readdirSync(dir)) {
            const p = join(dir, name);
            if (statSync(p).isDirectory()) { walk(p); continue; }
            const raw = () => readFileSync(p, 'utf8');
            const rel = relative(SRC_DIR, p).split(sep).join('/');
            if (SHEET_EXT.some(e => name.endsWith(e))) out.push({ rel, code: stripComments(raw()) });
            else if (name.endsWith('.ts')) {
                const only = styleLiterals(raw());
                if (only) out.push({ rel, code: stripComments(only), inline: true });
            }
        }
    };
    walk(SRC_DIR);
    out.push(...serverSheets());
    return out;
}

const lineOf = (code, i) => code.slice(0, i).split('\n').length;

// `&` is the parent, not a descendant of it — reading it as one turns `&:hover` into a selector
// that matches nothing and hides which element the rule is really about.
const nest = (chain, sel) => sel.includes('&')
    ? [...chain.slice(0, -1), sel.replace(/&/g, chain[chain.length - 1] ?? '')]
    : [...chain, sel];

// Rules, nested ones included: SCSS nesting is the only place a stylesheet states an enclosing
// surface, so the tree is what makes any backdrop better than a guess.
function rulesOf(code) {
    const out = [];
    const walk = (start, end, parent) => {
        let i = start, segStart = start;
        // Everything is located by the offset of its first real character, not by where its segment
        // began: the leading run of blanks a masked .ts file starts with carries no `;` to end it.
        const head = to => {
            const at = code.slice(segStart, to).search(/\S/);
            return at < 0 ? segStart : segStart + at;
        };
        const flush = to => {
            const text = code.slice(segStart, to);
            const d = text.match(/^\s*([-\w]+)\s*:\s*([\s\S]+)$/);
            if (d && parent) parent.decls.push({ prop: d[1].toLowerCase(), value: d[2].trim(), at: head(to) });
        };
        while (i < end) {
            const ch = code[i];
            if (ch === '{') {
                const sel = code.slice(segStart, i).trim();
                const selAt = head(i);
                let depth = 0, j = i;
                for (; j < end; j++) {
                    if (code[j] === '{') depth++;
                    else if (code[j] === '}' && --depth === 0) break;
                }
                const node = {
                    sel, at: selAt, decls: [],
                    parent: sel.startsWith('@') ? parent : (parent ?? null),
                    chain: sel.startsWith('@') ? (parent?.chain ?? []) : nest(parent?.chain ?? [], sel),
                };
                if (!sel.startsWith('@')) out.push(node);
                walk(i + 1, j, sel.startsWith('@') ? parent : node);
                i = segStart = j + 1;
                continue;
            }
            if (ch === ';') { flush(i); segStart = i + 1; }
            i++;
        }
        flush(end);
    };
    walk(0, code.length, null);
    return out;
}

const own = (rule, ...props) => {
    let found = null;
    for (const d of rule.decls) if (props.includes(d.prop)) found = d;
    return found;
};

const clean = v => v.replace(/!important/gi, '').trim();
const NON_PAINT = /^(none|inherit|initial|unset|revert|currentcolor|auto)$/i;

// Font sizes resolve through the same token graph the colours do.
function pxOf(value, vars, seen = new Set()) {
    const v = clean(value ?? '');
    const ref = v.match(VAR_CALL);
    if (ref) {
        if (seen.has(ref[1])) return null;
        seen.add(ref[1]);
        const branch = varBranch(ref, vars);
        return branch === null ? null : pxOf(branch, vars, seen);
    }
    const m = v.match(/^(-?[\d.]+)px$/);
    return m ? parseFloat(m[1]) : null;
}

// Every token a value reaches, so a combination that runs through the user's accent can be found
// without resolving it first.
function refsOf(value, vars, seen = new Set(), out = new Set()) {
    const v = clean(value ?? '');
    if (!v) return out;
    const ref = v.match(VAR_CALL);
    if (ref) {
        out.add(ref[1]);
        if (seen.has(ref[1])) return out;
        seen.add(ref[1]);
        const branch = varBranch(ref, vars);
        return branch === null ? out : refsOf(branch, vars, seen, out);
    }
    for (const m of v.matchAll(/var\(\s*(--[\w-]+)[^)]*\)/g)) refsOf(m[0], vars, new Set(seen), out);
    return out;
}

const opaque = (value, vars) => curvesOf(value, vars).every(c => c.every(x => x[3] >= 1));
const invisible = (value, vars) => curvesOf(value, vars).every(c => c.every(x => x[3] === 0));

// The attribute conditions a selector chain carries. A rule under [data-skin="forest"] paints in a
// palette that block declares, and scoring it in the base palette would report a colour nothing
// mixes; a rule under [data-theme="dark"] never paints in the light pass at all.
const condsOf = chain => new Set([...chain.join(' ').matchAll(/\[([\w-]+)\s*=\s*"([^"]*)"\]/g)].map(m => `${m[1]}=${m[2]}`));

// Custom properties declared on a conditional :root, from every sheet. They layer onto the theme
// for exactly those rules whose own chain states every condition the block was written under.
function rootScopes(files) {
    const out = [];
    for (const f of files) {
        for (const rule of rulesOf(f.code)) {
            if (!/^:root/.test(rule.sel) || rule.parent) continue;
            const vars = {};
            for (const d of rule.decls) if (d.prop.startsWith('--')) vars[d.prop] = clean(d.value);
            const conds = condsOf([rule.sel]);
            if (Object.keys(vars).length && conds.size) out.push({ conds, vars });
        }
    }
    return out;
}

// The nearest enclosing rule that paints something solid. It is an inference from the stylesheet,
// not a reading of the DOM: nesting means descendant, and a descendant can have anything between
// it and its ancestor. It is labelled as such wherever it is used.
function enclosing(rule, vars) {
    // An `&` rule is the same element as its parent, so the parent's own background is the state
    // this rule replaces — not something painted behind it.
    let self = rule;
    while (self.sel.includes('&') && self.parent) self = self.parent;
    for (let p = self.parent; p; p = p.parent) {
        const bg = own(p, 'background', 'background-color');
        if (!bg || NON_PAINT.test(clean(bg.value))) continue;
        try { if (opaque(bg.value, vars)) return { rule: p, value: clean(bg.value) }; } catch { /* unresolvable */ }
    }
    return null;
}

// A selector extends another when it adds to it at a boundary: `.mini:hover` extends `.mini`,
// `.mini-remove` does not, and reading the second as the first attributes an ink to an element that
// never carries it.
const SEL_BOUNDARY = /[\s:.#[>+~]/;
const COMBINATOR = /[\s>+~]/;
const extendsSel = (base, sel) =>
    sel.length > base.length && sel.startsWith(base) && SEL_BOUNDARY.test(sel[base.length]);

const partsOf = rule => splitTop(rule.chain.join(' ').replace(/\s+/g, ' '), ',');

// A rule that declares one half of a pair still paints both, and the other half is written
// somewhere: for a state rule, usually a few lines above it — `.mini:hover` sets the wash and
// `.mini` sets the ink. Reading it is the same kind of claim the enclosing-backdrop walk makes,
// so it is labelled the same way and never printed as something read off the rule itself. The
// nearest match wins, since a longer selector is the more specific rule and the closer element. A
// rule that lists several selectors resolves only when they all arrive at one value: two answers
// are not one fact.
function inheritHalf(entry, parsed, props) {
    const out = [];
    for (const target of entry.parts) {
        let best = null;
        for (const cand of parsed) {
            if (cand === entry) continue;
            const d = own(cand.rule, ...props);
            if (!d || NON_PAINT.test(clean(d.value))) continue;
            for (const part of cand.parts) {
                if (!extendsSel(part, target) || (best && part.length < best.len)) continue;
                best = {
                    len: part.length, from: part, value: clean(d.value),
                    same: !COMBINATOR.test(target.slice(part.length)),
                };
            }
        }
        if (!best) return { value: null };
        out.push(best);
    }
    if (new Set(out.map(o => o.value)).size > 1) return { value: null, conflict: true };
    return out[0];
}

function census() {
    const files = sheets();
    const sites = [];
    const skipped = new Map();
    const skip = (bucket, why, where) => {
        const k = `${bucket}|${why}`;
        if (!skipped.has(k)) skipped.set(k, { bucket, why, n: 0, first: where });
        skipped.get(k).n++;
    };

    const declared = { both: 0, inherited: 0, unresolved: 0, neither: 0 };
    const DECLARED = 'declared by the rule';

    for (const f of files) {
        const parsed = rulesOf(f.code).map(rule => ({ rule, parts: partsOf(rule) }));
        for (const entry of parsed) {
            const rule = entry.rule;
            const fg = own(rule, 'color'), bg = own(rule, 'background', 'background-color');
            const where = `${f.rel}:${lineOf(f.code, rule.at)}`;
            if (!fg && !bg) { declared.neither++; continue; }
            let fgV = fg && clean(fg.value), bgV = bg && clean(bg.value);
            let ink = DECLARED, surface = DECLARED;
            if (!fg) {
                const from = inheritHalf(entry, parsed, ['color']);
                if (!from.value) {
                    declared.unresolved++;
                    skip('half', from.conflict
                        ? 'the selectors listed in the rule inherit different inks'
                        : 'the ink is not declared on anything this selector extends', where);
                    continue;
                }
                fgV = from.value;
                ink = `inherited from "${from.from}" — taken from the sheet, not the DOM`;
            }
            if (!bg) {
                const from = inheritHalf(entry, parsed, ['background', 'background-color']);
                if (!from.value) {
                    declared.unresolved++;
                    skip('half', from.conflict
                        ? 'the selectors listed in the rule inherit different surfaces'
                        : 'the surface is not declared on anything this selector extends', where);
                    continue;
                }
                bgV = from.value;
                surface = from.same
                    ? `the same element without this state, from "${from.from}" — taken from the sheet, not the DOM`
                    : `the enclosing "${from.from}" — taken from the sheet, not the DOM`;
            }
            declared[ink === DECLARED && surface === DECLARED ? 'both' : 'inherited']++;
            if (NON_PAINT.test(bgV)) { skip('rule', `the rule declares no surface of its own — background: ${bgV}`, where); continue; }
            if (NON_PAINT.test(fgV)) { skip('rule', `the ink comes from somewhere else — color: ${fgV}`, where); continue; }
            if (/^transparent$/i.test(bgV)) { skip('rule', 'background is fully transparent — the rule paints no surface of its own', where); continue; }
            let fs = own(rule, 'font-size'), inheritedFrom = null;
            for (let p = rule.parent; p && !fs; p = p.parent) { fs = own(p, 'font-size'); if (fs) inheritedFrom = p.sel; }
            sites.push({
                file: f.rel, line: lineOf(f.code, rule.at),
                sel: rule.chain.join(' ').replace(/\s+/g, ' '),
                conds: condsOf(rule.chain),
                server: !!f.server, ground: f.ground ?? GROUND,
                ink, surface,
                fgV, bgV, rule, fsV: fs ? clean(fs.value) : null, fsFrom: inheritedFrom,
            });
        }
    }

    const scopes = rootScopes(files);
    const groups = new Map();
    let unresolved = 0, gated = 0;

    for (const [themeName, base] of THEMES) {
        const grounds = groundsOf(base);
        const covered = coverage(base, themeName);
        for (const s of sites) {
            const where = `${s.file}:${s.line}`;
            const wants = [...s.conds].filter(c => c.startsWith('data-theme='));
            if (wants.length && !wants.includes(`data-theme=${themeName}`)) { gated++; continue; }
            // A server-rendered page gets the contract tokens from DesignTokens and nothing else,
            // in the palette's own accent: appearance.service.ts injects a preset in the app, and
            // none of these pages runs it — the blog and the landing page are served logged out.
            let vars = s.server ? serverVars(base) : base;
            if (!s.server) for (const sc of scopes) if ([...sc.conds].every(c => s.conds.has(c))) vars = { ...vars, ...sc.vars };
            const accented = !s.server
                && [...refsOf(s.fgV, vars), ...refsOf(s.bgV, vars)].some(n => ACCENT_DEPENDENT.has(n));
            const skins = accented ? PRESETS.map(p => ({ name: p.name, vars: { ...vars, '--accent': accentOf(p, themeName) } }))
                : [{ name: null, vars }];
            let worst = null;
            try {
                if (invisible(s.fgV, vars)) { skip('scoring', 'foreground is fully transparent — the rule paints no ink', where); continue; }
                for (const skin of skins) {
                    const solid = opaque(s.bgV, skin.vars);
                    // The ground follows the rule's own palette: a scoped :root can move the body
                    // colour out from under everything written inside that scope.
                    const under = s.server ? groundsOf(skin.vars, s.ground)
                        : skin.vars === base ? grounds : groundsOf(skin.vars);
                    const near = solid ? null : enclosing(s.rule, skin.vars);
                    const backdrops = solid ? [[0, 0, 0, 0]]
                        : near ? painted(near.value, skin.vars, under) : under;
                    const on = solid ? 'the rule paints an opaque surface'
                        : near ? `nested under "${near.rule.sel.replace(/\s+/g, ' ')}" — taken from the sheet, not the DOM`
                            : 'assumed: the page ground';
                    const r = score(s.fgV, s.bgV, skin.vars, backdrops);
                    if (!worst || r.r < worst.r) worst = { ...r, on, solid, near: !!near, preset: skin.name };
                }
            } catch (e) {
                unresolved++;
                skip('scoring', `unresolvable value — ${e.message}`, where);
                continue;
            }
            const px = pxOf(s.fsV, vars);
            const min = px !== null && px >= 19 ? 3.0 : 4.5;
            if (covered.has(`${key(worst.fg)}|${key(worst.bg)}`)) continue;
            const k = [themeName, toHex(worst.fg), toHex(worst.bg), min, worst.on, s.ink, s.surface].join('|');
            if (!groups.has(k)) groups.set(k, {
                theme: themeName, ...worst, min, px, fsV: s.fsV, fsFrom: s.fsFrom,
                ink: s.ink, surface: s.surface, at: [],
            });
            groups.get(k).at.push(`${where}  ${s.sel}`);
        }
    }

    const list = [...groups.values()].sort((a, b) => a.r - b.r);
    console.log(`Census — every foreground/background the app declares that the pair table does not cover.`);
    console.log(`${files.length} stylesheet(s) scanned (${files.filter(f => f.inline).length} inline in a .ts, `
        + `${files.filter(f => f.server).length} in a C# raw string).`);
    console.log(`Of the rules in them: ${declared.both} state an ink and a surface, ${declared.inherited} state one `
        + `half and the other was read off a rule they extend, ${declared.unresolved} state one half that could not `
        + `be resolved, ${declared.neither} state neither and paint no combination at all.`);
    console.log(`Floor: 4.5 under 19px, 3.0 at 19px and above. A rule with no font-size of its own is`);
    console.log(`held to 4.5 — the size is inherited from a DOM this cannot see, and 3.0 would be a guess`);
    console.log(`in the direction of passing.\n`);
    for (const g of list) {
        const size = g.px !== null ? `${g.px}px${g.fsFrom ? ` (from "${g.fsFrom.replace(/\s+/g, ' ')}")` : ''}` : 'size inherited — unknown';
        const preset = g.preset ? `  worst accent preset ${g.preset}` : '';
        console.log(`${g.r < g.min ? 'FAIL ' : 'ok   '}${g.r.toFixed(2).padStart(5)} (min ${g.min})  [${g.theme}] `
            + `${toHex(g.fg)} on ${toHex(g.bg)}   ${size}${preset}`);
        console.log(`            backdrop: ${g.on}`);
        if (g.ink !== DECLARED) console.log(`            ink: ${g.ink}`);
        if (g.surface !== DECLARED) console.log(`            surface: ${g.surface}`);
        for (const at of g.at) console.log(`            ${at}`);
    }
    const bucket = name => [...skipped.values()].filter(s => s.bucket === name);
    const total = list => list.reduce((n, s) => n + s.n, 0);
    const byBackdrop = k => list.filter(g => g.on.startsWith(k)).length;
    console.log(`\n${list.length} uncovered combination(s), ${list.filter(g => g.r < g.min).length} of them below their floor.`);
    console.log(`Backdrops: ${byBackdrop('the rule')} measured off the rule itself, `
        + `${byBackdrop('nested')} taken from an enclosing rule in the same sheet, `
        + `${byBackdrop('assumed')} assumed to be the page ground.`);
    console.log(`\nWhat this run could not measure. A rule is skipped once; a scoring is one rule in one theme.`);
    console.log(`  ${total(bucket('rule'))} rule(s) never reached a score:`);
    for (const s of bucket('rule').sort((a, b) => b.n - a.n))
        console.log(`  ${String(s.n).padStart(4)}  ${s.why}   (first at ${s.first})`);
    console.log(`  ${total(bucket('half'))} rule(s) stated one half of a combination and the other stayed unknown:`);
    for (const s of bucket('half').sort((a, b) => b.n - a.n))
        console.log(`  ${String(s.n).padStart(4)}  ${s.why}   (first at ${s.first})`);
    console.log(`  ${total(bucket('scoring')) + gated} scoring(s) dropped, ${unresolved} of them because a value would not resolve:`);
    console.log(`  ${String(gated).padStart(4)}  the selector states the other theme`);
    for (const s of bucket('scoring').sort((a, b) => b.n - a.n))
        console.log(`  ${String(s.n).padStart(4)}  ${s.why}   (first at ${s.first})`);
}

// What the pair table measures, as resolved colours rather than as names: the census resolves a
// combination the app writes, and the two only meet as colour.
function coverage(vars, themeName) {
    const out = new Set();
    const add = (v, list) => {
        const grounds = groundsOf(v);
        for (const p of list) {
            const backdrops = p.under ? painted(v[p.under], v, grounds) : grounds;
            const bgs = painted(v[p.bg], v, backdrops);
            for (const name of [p.fg, p.alt].filter(Boolean)) {
                for (const b of bgs)
                    for (const curve of curvesOf(v[name], v))
                        for (const c of curve) out.add(`${key(srcOver(c, b))}|${key(b)}`);
            }
        }
    };
    add(vars, pairs);
    for (const preset of PRESETS) add({ ...vars, '--accent': accentOf(preset, themeName) }, accentPairs);
    return out;
}

if (process.argv.includes('--census')) census();
else runContract();
