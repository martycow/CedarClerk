// ADR-138 — the surface split, enforced.
//
// Chrome and paper are two densities living on one screen, so nothing a page root declares can
// score them; this walks src/, finds what each surface actually declares, and holds each side to
// its own floors. Values are read out of the tree rather than from a copied list, the same way
// tools/check-contrast.mjs does, so the check cannot drift from what ships.
// Run: node tools/check-density.mjs   (from cedarclerk-web/)
//
// It reads CSS statically: it scores declared values, never a computed height. A chrome component
// that never got its data-surface attribute is invisible here unless its name is on CHROME_PARTS.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve, relative, sep } from 'node:path';

const SRC = resolve(import.meta.dirname, '../src');
const EXT = ['.css', '.scss', '.ts', '.html'];


const CHROME_FAMILY = /^--(bench-|hit-|text-chrome|text-readout)/;
const DENS_FAMILY = /^--dens-/;

// The chrome parts ADR-138 item 1 names. A file called one of these must spell the attribute.
const CHROME_PARTS = ['rail-header', 'hook-rail', 'shelf-panel', 'bench-drawer', 'ruler-bar', 'index-tabs'];

// PAPER_HIT is the box a paper control is drawn at; PAPER_TOUCH is the floor a finger is owed
// under a coarse pointer. One constant answering both is what let the drawing size drift six pixels
// off the mirror without a rule noticing (ADR-182).
const CHROME_HIT = 30, PAPER_HIT = 38, PAPER_TOUCH = 44;
const CHROME_FS_MIN = 11, CHROME_FS_MAX = 13, PAPER_FS_MIN = 14;
// ADR-200's third box. A block that draws at it says so in its selector — the tier is named, never
// inferred from the number, which is the same shape ADR-196's two exceptions take.
const TRIM_HIT = 24, TRIM_FS_MIN = 11, TRIM_FS_MAX = 12;
const TRIM_PIN = '[data-box="trim"]';

function walk(dir, out = []) {
    for (const name of readdirSync(dir)) {
        const p = join(dir, name);
        if (statSync(p).isDirectory()) walk(p, out);
        else if (EXT.some(e => name.endsWith(e))) out.push(p);
    }
    return out;
}

// Comments are blanked, not removed, so every index still maps to its original line. styles.scss
// documents nearly every token and its prose quotes the very sizes this check rejects.
function strip(s) {
    const blank = m => m.replace(/[^\n]/g, ' ');
    return s
        .replace(/\/\*[\s\S]*?\*\//g, blank)
        .replace(/(^|[^:])(\/\/[^\n]*)/g, (m, p, c) => p + blank(c));
}

const files = walk(SRC).map(path => {
    const raw = readFileSync(path, 'utf8');
    return { rel: relative(SRC, path).split(sep).join('/'), code: strip(raw) };
});

const lineOf = (code, i) => code.slice(0, i).split('\n').length;

function blocks(code, needle) {
    const out = [];
    let i = 0;
    while ((i = code.indexOf(needle, i)) !== -1) {
        const open = code.indexOf('{', i);
        if (open === -1) break;
        let depth = 0, j = open;
        for (; j < code.length; j++) {
            if (code[j] === '{') depth++;
            else if (code[j] === '}' && --depth === 0) break;
        }
        out.push({ at: i, open, body: code.slice(open + 1, j) });
        i = j + 1;
    }
    return out;
}

const decls = body => [...body.matchAll(/(--[\w-]+)\s*:\s*([^;{}]+)/g)];

const styles = files.find(f => f.rel === 'styles.scss');
const root = {};
for (const [, k, v] of decls(blocks(styles.code, ':root')[0].body)) root[k] = v.trim();
const compactBlock = blocks(styles.code, '[data-density="compact"]')[0];

function px(value, vars, seen = new Set()) {
    if (value == null) return null;
    const v = String(value).trim();
    // A carrier with a fallback is how a control names its drawn box without fighting the
    // coarse-pointer floor (ADR-196, ADR-200): --hit-surface exists only under that media query,
    // so off it the fallback is what ships, and the fallback is what this check scores.
    const withFallback = v.match(/^var\((--[\w-]+),\s*(.+)\)$/);
    if (withFallback) {
        const named = vars[withFallback[1]];
        return named !== undefined ? px(named, vars, seen) : px(withFallback[2], vars, seen);
    }
    const ref = v.match(/^var\((--[\w-]+)\)$/);
    if (ref) {
        if (seen.has(ref[1])) return null;
        seen.add(ref[1]);
        return px(vars[ref[1]], vars, seen);
    }
    const m = v.match(/^(-?[\d.]+)px$/);
    return m ? parseFloat(m[1]) : null;
}

const results = [];
// `sites` is what the rule actually looked at. A rule whose subject has not landed yet finds no
// sites and would otherwise print "ok 0" — a green line indistinguishable from a rule that
// searched and found nothing wrong. `nothing` is the reason printed in its place, so an unmeasured
// surface reads as unmeasured and turns into a real ok/FAIL by itself once the subject exists.
const rule = (name, note, nothing = null) => {
    const r = { name, note, fails: [], skipped: null, sites: 0, nothing };
    results.push(r);
    return r;
};
const fail = (r, rel, line, msg) => r.fails.push(`${rel}:${line}  ${msg}`);

// 1. Integers only (ADR-071 principle 7, restated by ADR-138 item 3). The mirror ships 63
//    half-pixel declarations; this is what stops them being copied in. Every custom property is a
//    candidate, not just the type families: ADR-138 item 3 counts the 1.5px ring inside
//    --shadow-pine-btn and the pegboard's dot radius among them, and neither is a --fs-.
const rInt = rule('integers only', 'no fractional px in a font-size or in any token value under src/');
for (const f of files) {
    for (const m of f.code.matchAll(/font-size\s*:\s*([^;{}]+)/g)) {
        const frac = m[1].match(/(-?\d*\.\d+)px/);
        if (frac) fail(rInt, f.rel, lineOf(f.code, m.index), `font-size: ${frac[0]} — half-pixel`);
    }
    for (const m of f.code.matchAll(/(--[\w-]+)\s*:\s*([^;{}]+)/g)) {
        const frac = m[2].match(/(-?\d*\.\d+)px/);
        if (frac) fail(rInt, f.rel, lineOf(f.code, m.index), `${m[1]}: ${frac[0]} — fractional px in a token value`);
    }
}

// 2. The two vocabularies never cross (ADR-138 item 4). One clause per direction: the compact block
//    ships and the surface blocks do not, so a single rule would let the half that measures nothing
//    hide behind the half that does.
const rCompactVocab = rule('compact declares no chrome token', 'the density knob may not reach into the chrome family',
    'no [data-density="compact"] block in styles.scss');
if (compactBlock) {
    rCompactVocab.sites++;
    for (const [, k] of decls(compactBlock.body)) {
        if (CHROME_FAMILY.test(k)) fail(rCompactVocab, 'styles.scss', lineOf(styles.code, compactBlock.at), `[data-density="compact"] declares ${k}`);
    }
}

const rSurfaceVocab = rule('[data-surface] declares no --dens-*', 'chrome and paper blocks may not reach into the density family',
    'no [data-surface] block in the tree yet — the shell has not landed');
for (const f of files) {
    for (const b of blocks(f.code, '[data-surface')) {
        rSurfaceVocab.sites++;
        for (const [, k] of decls(b.body)) {
            if (DENS_FAMILY.test(k)) fail(rSurfaceVocab, f.rel, lineOf(f.code, b.at), `[data-surface] block declares ${k}`);
        }
    }
}

// 3. Compact is already at paper's floor and must not be tightened past it (ADR-138 item 4).
const rCompact = rule('compact stays at the paper floor', `--dens-fs under compact resolves to >= ${PAPER_FS_MIN}px`,
    'no [data-density="compact"] block in styles.scss');
if (compactBlock) {
    rCompact.sites++;
    const vars = { ...root };
    for (const [, k, v] of decls(compactBlock.body)) vars[k] = v.trim();
    const v = px(vars['--dens-fs'], vars);
    if (v !== null && v < PAPER_FS_MIN) fail(rCompact, 'styles.scss', lineOf(styles.code, compactBlock.at), `--dens-fs resolves to ${v}px`);
}

// 4. The contract tokens carry the numbers ADR-138 item 2 states.
const rTokens = rule('contract token values', `--hit-chrome ${CHROME_HIT}px, --hit-target ${PAPER_HIT}px, --hit-touch ${PAPER_TOUCH}px, --hit-trim ${TRIM_HIT}px, chrome type ${CHROME_FS_MIN}-${CHROME_FS_MAX}px, --text-readout via the scale`,
    'none of the contract tokens are declared in styles.scss');
const tokenLine = k => {
    const m = styles.code.match(new RegExp(`${k}\\s*:`));
    return m ? lineOf(styles.code, m.index) : 1;
};
const declared = k => Object.prototype.hasOwnProperty.call(root, k);
for (const [k, want] of [['--hit-chrome', CHROME_HIT], ['--hit-target', PAPER_HIT], ['--hit-touch', PAPER_TOUCH], ['--hit-trim', TRIM_HIT]]) {
    if (!declared(k)) continue;
    rTokens.sites++;
    const v = px(root[k], root);
    if (v !== want) fail(rTokens, 'styles.scss', tokenLine(k), `${k} is ${root[k]}, the contract says ${want}px`);
}
for (const k of ['--text-chrome', '--text-chrome-sm']) {
    if (!declared(k)) continue;
    rTokens.sites++;
    const v = px(root[k], root);
    if (v === null || !Number.isInteger(v) || v < CHROME_FS_MIN || v > CHROME_FS_MAX)
        fail(rTokens, 'styles.scss', tokenLine(k), `${k} is ${root[k]}, chrome type is an integer ${CHROME_FS_MIN}-${CHROME_FS_MAX}px`);
}
for (const k of Object.keys(root)) {
    if (!/^--bench-|^--hit-/.test(k)) continue;
    rTokens.sites++;
    const v = px(root[k], root);
    if (v !== null && !Number.isInteger(v)) fail(rTokens, 'styles.scss', tokenLine(k), `${k} is ${root[k]} — not an integer`);
}
// A semantic role carrying a bare pixel number is the debt principle 7 exists to stop: the role
// aliases onto the scale, and the scale carries the number.
if (declared('--text-readout')) rTokens.sites++;
if (declared('--text-readout') && !/^var\(--fs-[\w-]+\)$/.test(root['--text-readout']))
    fail(rTokens, 'styles.scss', tokenLine('--text-readout'), `--text-readout is ${root['--text-readout']} — it must alias a --fs-* step`);

// 5. Each surface's floors hold inside anything scoped to it (ADR-138 item 7). ADR-196 names two
// exact desktop exceptions: the shared `sm` paper button and Stats' discrete range select use the
// chrome drawing size, while the global coarse-pointer rule still lifts both to 44px.
const rFloors = rule('surface floors', `chrome ${CHROME_HIT}px / ${CHROME_FS_MIN}-${CHROME_FS_MAX}px, paper ${PAPER_HIT}px / >= ${PAPER_FS_MIN}px, trim ${TRIM_HIT}px / ${TRIM_FS_MIN}-${TRIM_FS_MAX}px`,
    'no [data-surface="chrome"] or [data-surface="paper"] block in the tree yet — the shell has not landed');
for (const f of files) {
    for (const [surface, hit, fsMin, fsMax] of [['chrome', CHROME_HIT, CHROME_FS_MIN, CHROME_FS_MAX], ['paper', PAPER_HIT, PAPER_FS_MIN, Infinity]]) {
        for (const b of blocks(f.code, `[data-surface="${surface}"]`)) {
            rFloors.sites++;
            const base = b.open + 1;
            const selector = f.code.slice(b.at, b.open);
            const paperCompact = surface === 'paper'
                && (selector.includes('.btn.sm') || selector.includes('.range-picker select'));
            // ADR-200: the selector names the tier, so a trim block is scored against trim's own
            // numbers instead of the surface's. It still stands on a surface, because that is where
            // the coarse-pointer floor it spends is declared.
            const trim = selector.includes(TRIM_PIN);
            for (const m of b.body.matchAll(/font-size\s*:\s*([^;{}]+)/g)) {
                const v = px(m[1], root);
                if (v === null) continue;
                if (paperCompact && v >= CHROME_FS_MIN && v <= CHROME_FS_MAX) continue;
                if (trim) {
                    if (v < TRIM_FS_MIN || v > TRIM_FS_MAX)
                        fail(rFloors, f.rel, lineOf(f.code, base + m.index), `trim: font-size ${v}px, trim type is ${TRIM_FS_MIN}-${TRIM_FS_MAX}px`);
                    continue;
                }
                const want = fsMax === Infinity ? `>= ${fsMin}px` : `${fsMin}-${fsMax}px`;
                if (v < fsMin || v > fsMax) fail(rFloors, f.rel, lineOf(f.code, base + m.index), `${surface}: font-size ${v}px, ${surface} type is ${want}`);
            }
            // min-* only: it is the floor's own vocabulary, and a bare height/width is as often a
            // dock's dimension as a control's box.
            for (const m of b.body.matchAll(/min-(height|width)\s*:\s*([^;{}]+)/g)) {
                const v = px(m[2], root);
                if (paperCompact && v === CHROME_HIT) continue;
                const floor = trim ? TRIM_HIT : hit;
                if (v !== null && v < floor) fail(rFloors, f.rel, lineOf(f.code, base + m.index), `${trim ? 'trim' : surface}: min-${m[1]} ${v}px below the ${floor}px floor`);
            }
        }
    }
}

// 6. The coarse-pointer rules stay inside the surface vocabulary (ADR-138 item 5). Left global one
//    pushes the ruler from 30px to 44 and the chrome budget the shell exists to buy back is gone on
//    first touch.
const rCoarse = rule('coarse-pointer rules name a surface', 'every selector under @media (pointer: coarse) carries a data-surface qualifier',
    'no selector under a @media (pointer: coarse) block in the tree');
const surfaceLanded = files.some(f => /\[data-surface\s*=/.test(f.code));
if (!surfaceLanded) rCoarse.skipped = 'no [data-surface] in the tree yet — there is no chrome to exempt';
else for (const f of files) {
    for (const b of blocks(f.code, '@media (pointer: coarse)')) {
        const base = b.open + 1;
        for (const m of b.body.matchAll(/([^{}]+)\{/g)) {
            const sel = m[1].trim();
            if (!sel || sel.startsWith('@')) continue;
            // The selector, not the block, is the site: a coarse block holding none of them has
            // been read and has scored nothing, which is what `nothing` above exists to say.
            rCoarse.sites++;
            if (!sel.includes('data-surface')) fail(rCoarse, f.rel, lineOf(f.code, base + m.index), `selector "${sel.replace(/\s+/g, ' ')}" is unqualified`);
        }
    }
}

// 7. The floor is inherited, never selected (ADR-138 item 5). Rule 6 holds every selector here to
//    naming a surface, and the two blocks that shipped the nesting defect both did — which is why
//    it saw nothing. `[data-surface="paper"] button` and `[data-surface="chrome"] button` each
//    reach any depth, so a control lying under both was decided by which block came second, and
//    the same second block won for a sheet inside a panel and for a ruler on a sheet, though the
//    two want opposite answers. No selector can say "nearest": excluding descendants of the
//    opposite surface only moves the failure three levels down, where neither block matches and
//    the control stands at no floor at all. So the shape is the rule — a surface declares the
//    floor, controls spend it, and inheritance answers "nearest" at any depth in either direction.
const rNearest = rule('the touch floor is inherited, not selected',
    'under @media (pointer: coarse) no min-height/min-width comes from a selector pinning a surface value; each floor is var() of a property both surfaces declare',
    'no @media (pointer: coarse) block in the tree');

const PINS = /\[data-surface\s*=\s*"(paper|chrome)"\]/;

// Selector/body pairs one level inside a block body. `blocks` above finds a block by its needle;
// this walks a body it has already been handed.
function childRules(body) {
    const out = [];
    let i = 0, selStart = 0;
    while (i < body.length) {
        if (body[i] === '{') {
            let depth = 0, j = i;
            for (; j < body.length; j++) {
                if (body[j] === '{') depth++;
                else if (body[j] === '}' && --depth === 0) break;
            }
            out.push({ sel: body.slice(selStart, i).trim(), at: i, body: body.slice(i + 1, j) });
            i = selStart = j + 1;
        } else if (body[i] === '}') {
            i = selStart = i + 1;
        } else i++;
    }
    return out;
}

// The coarse-pointer carrier, so paper's entry is the touch floor and not the drawn box (ADR-182).
const FLOOR_HIT = { paper: PAPER_TOUCH, chrome: CHROME_HIT };

// The carriers, read once from the global block: styles.scss is where a surface declares its floor,
// and a component sheet may only spend what it inherits, never mint a carrier of its own.
const globalFloor = { paper: {}, chrome: {} };
for (const media of blocks(styles.code, '@media (pointer: coarse)')) {
    for (const r of childRules(media.body)) {
        const pinned = r.sel.match(PINS);
        if (!pinned || /min-(?:height|width)\s*:/.test(r.body)) continue;
        for (const [, k, v] of decls(r.body)) globalFloor[pinned[1]][k] = v.trim();
    }
}

for (const f of files) {
    for (const media of blocks(f.code, '@media (pointer: coarse)')) {
        rNearest.sites++;
        const base = media.open + 1;
        const spent = [];

        for (const r of childRules(media.body)) {
            if (!r.sel || r.sel.startsWith('@')) continue;
            const floors = [...r.body.matchAll(/min-(?:height|width)\s*:\s*([^;{}]+)/g)];
            const pinned = r.sel.match(PINS);
            if (pinned && floors.length) {
                fail(rNearest, f.rel, lineOf(f.code, base + r.at),
                    `"${r.sel.replace(/\s+/g, ' ')}" pins ${pinned[1]} and sets the floor by selector — a descendant selector reaches every depth, so the nesting is answered by source order`);
                continue;
            }
            for (const m of floors) spent.push({ value: m[1].trim(), at: base + r.at, sel: r.sel });
        }

        if (!spent.length) continue;

        const shared = Object.keys(globalFloor.paper).filter(k => k in globalFloor.chrome);
        if (!shared.length) {
            fail(rNearest, f.rel, lineOf(f.code, base),
                'a floor is spent here but no property is declared by both surfaces for it to be inherited from');
            continue;
        }
        for (const s of spent) {
            const ref = s.value.match(/^var\((--[\w-]+)\)$/);
            if (!ref || !shared.includes(ref[1]))
                fail(rNearest, f.rel, lineOf(f.code, s.at),
                    `"${s.sel.replace(/\s+/g, ' ')}" sets the floor to ${s.value} — it must be var(${shared.join(') or var(')}), the property each surface declares`);
        }
        for (const surface of ['paper', 'chrome']) {
            for (const k of shared) {
                const v = px(globalFloor[surface][k], root);
                if (v !== FLOOR_HIT[surface])
                    fail(rNearest, 'styles.scss', tokenLine(k),
                        `${surface} carries ${k} at ${globalFloor[surface][k]}, the contract says ${FLOOR_HIT[surface]}px`);
            }
        }
    }
}

// 8. A named chrome part that lost its attribute (ADR-138 consequence paragraph).
//
// A selector is not a declaration. Every one of these six files also carries
// `:host([data-surface="chrome"])` rules, so a search for the bare name is answered by the
// stylesheet whether or not the host still sets the attribute — the rule read green with the host
// binding deleted. Only the two forms that put the attribute on the element count: Angular host
// metadata, and a template attribute, which is the occurrence a `[` does not precede.
const SETS_CHROME = [/(['"])data-surface\1\s*:\s*(['"])chrome\2/, /(^|[^[\w-])data-surface\s*=\s*"chrome"/];
const rParts = rule('chrome parts carry the attribute', CHROME_PARTS.join(', '),
    'none of the named chrome components exist yet — the shell has not landed');
for (const part of CHROME_PARTS) {
    const own = files.filter(f => new RegExp(`(^|/)${part}\\.component\\.(ts|html|css|scss)$`).test(f.rel));
    if (!own.length) continue;
    rParts.sites++;
    if (!own.some(f => SETS_CHROME.some(re => re.test(f.code))))
        fail(rParts, own[0].rel, 1, `${part} declares no data-surface="chrome"`);
}

let failures = 0, unmeasured = 0;
for (const r of results) {
    if (!r.skipped && r.nothing && r.sites === 0) r.skipped = r.nothing;
    if (r.skipped) {
        unmeasured++;
        console.log(`skip        ${r.name} — ${r.skipped}`);
        continue;
    }
    failures += r.fails.length;
    console.log(`${r.fails.length ? 'FAIL' : 'ok  '}  ${String(r.fails.length).padStart(2)}  ${r.name} — ${r.note}`);
    for (const line of r.fails) console.log(`            ${line}`);
}
console.log(`\n${files.length} file(s) scanned, ${failures} failure(s), `
    + `${results.length - unmeasured}/${results.length} rule(s) measured something.`);
if (unmeasured) console.log(`${unmeasured} rule(s) had nothing to measure — printed above, never counted as passing.`);
process.exit(failures ? 1 : 0);
