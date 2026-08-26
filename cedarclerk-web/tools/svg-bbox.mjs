// Measures the painted extent of an SVG path — the box the glyph actually covers, as opposed to
// the 256-unit canvas it is drawn on. generate-icons.mjs needs it to size icons optically; nothing
// else in the pipeline cares where the ink lands.
//
// Curves and arcs are sampled rather than solved: a 50-step walk is well under a unit of error on
// a 256 grid, and the result is rounded into a viewBox anyway.

const STEPS = 50;

export function pathBBox(d, box = { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity }) {
    const tokens = d.match(/[a-zA-Z]|-?\d*\.?\d+(?:e[-+]?\d+)?/g) ?? [];
    let i = 0, cmd = '', x = 0, y = 0, startX = 0, startY = 0, prevCtrlX = 0, prevCtrlY = 0;

    const hit = (px, py) => {
        if (px < box.minX) box.minX = px;
        if (px > box.maxX) box.maxX = px;
        if (py < box.minY) box.minY = py;
        if (py > box.maxY) box.maxY = py;
    };
    const num = () => parseFloat(tokens[i++]);
    const bezier = (points) => {
        for (let s = 0; s <= STEPS; s++) sampleBezier(points, s / STEPS, hit);
    };

    while (i < tokens.length) {
        if (/[a-zA-Z]/.test(tokens[i])) cmd = tokens[i++];
        const rel = cmd === cmd.toLowerCase();
        switch (cmd.toUpperCase()) {
            case 'M': {
                let mx = num(), my = num();
                if (rel) { mx += x; my += y; }
                x = startX = mx; y = startY = my;
                hit(x, y);
                // An implicit lineto follows a moveto, and it keeps the command's case.
                cmd = rel ? 'l' : 'L';
                break;
            }
            case 'L': {
                let lx = num(), ly = num();
                if (rel) { lx += x; ly += y; }
                x = lx; y = ly;
                hit(x, y);
                break;
            }
            case 'H': {
                let hx = num();
                if (rel) hx += x;
                x = hx;
                hit(x, y);
                break;
            }
            case 'V': {
                let vy = num();
                if (rel) vy += y;
                y = vy;
                hit(x, y);
                break;
            }
            case 'C': {
                let c1x = num(), c1y = num(), c2x = num(), c2y = num(), ex = num(), ey = num();
                if (rel) { c1x += x; c1y += y; c2x += x; c2y += y; ex += x; ey += y; }
                bezier([[x, y], [c1x, c1y], [c2x, c2y], [ex, ey]]);
                prevCtrlX = c2x; prevCtrlY = c2y; x = ex; y = ey;
                break;
            }
            case 'S': {
                let c2x = num(), c2y = num(), ex = num(), ey = num();
                if (rel) { c2x += x; c2y += y; ex += x; ey += y; }
                bezier([[x, y], [2 * x - prevCtrlX, 2 * y - prevCtrlY], [c2x, c2y], [ex, ey]]);
                prevCtrlX = c2x; prevCtrlY = c2y; x = ex; y = ey;
                break;
            }
            case 'Q': {
                let cx = num(), cy = num(), ex = num(), ey = num();
                if (rel) { cx += x; cy += y; ex += x; ey += y; }
                bezier([[x, y], [cx, cy], [ex, ey]]);
                prevCtrlX = cx; prevCtrlY = cy; x = ex; y = ey;
                break;
            }
            case 'T': {
                let ex = num(), ey = num();
                if (rel) { ex += x; ey += y; }
                const cx = 2 * x - prevCtrlX, cy = 2 * y - prevCtrlY;
                bezier([[x, y], [cx, cy], [ex, ey]]);
                prevCtrlX = cx; prevCtrlY = cy; x = ex; y = ey;
                break;
            }
            case 'A': {
                const rx = num(), ry = num(), rot = num(), large = num(), sweep = num();
                let ex = num(), ey = num();
                if (rel) { ex += x; ey += y; }
                sampleArc(x, y, rx, ry, rot, large, sweep, ex, ey, hit);
                x = ex; y = ey;
                break;
            }
            case 'Z':
                x = startX; y = startY;
                break;
            default:
                i++;
        }
    }
    return box;
}

/** Union of every path inside a fragment of SVG markup. */
export function markupBBox(markup) {
    const box = { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity };
    for (const match of markup.matchAll(/\sd="([^"]*)"/g)) pathBBox(match[1], box);
    return box;
}

function sampleBezier(points, t, hit) {
    const n = points.length - 1;
    let px = 0, py = 0, coeff = Math.pow(1 - t, n);
    for (let k = 0; k <= n; k++) {
        px += coeff * points[k][0];
        py += coeff * points[k][1];
        // Step the binomial term along rather than recomputing it: c(k+1) = c(k) * (n-k)/(k+1) * t/(1-t).
        if (k < n) coeff = coeff * ((n - k) / (k + 1)) * (t === 1 ? Infinity : t / (1 - t));
    }
    if (t === 1) { hit(points[n][0], points[n][1]); return; }
    hit(px, py);
}

// Endpoint parameterisation, straight out of the SVG spec's implementation notes (F.6.5).
function sampleArc(x1, y1, rx, ry, degrees, large, sweep, x2, y2, hit) {
    if (rx === 0 || ry === 0) { hit(x2, y2); return; }
    const phi = (degrees * Math.PI) / 180, cos = Math.cos(phi), sin = Math.sin(phi);
    const dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
    const x1p = cos * dx + sin * dy, y1p = -sin * dx + cos * dy;
    rx = Math.abs(rx); ry = Math.abs(ry);
    const oversize = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
    if (oversize > 1) { const s = Math.sqrt(oversize); rx *= s; ry *= s; }
    const numerator = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
    const denominator = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
    let factor = Math.sqrt(Math.max(0, numerator / denominator));
    if (large === sweep) factor = -factor;
    const cxp = (factor * rx * y1p) / ry, cyp = (-factor * ry * x1p) / rx;
    const cx = cos * cxp - sin * cyp + (x1 + x2) / 2;
    const cy = sin * cxp + cos * cyp + (y1 + y2) / 2;
    const angle = (ux, uy, vx, vy) => {
        const sign = Math.sign(ux * vy - uy * vx) || 1;
        const dot = (ux * vx + uy * vy) / (Math.hypot(ux, uy) * Math.hypot(vx, vy));
        return sign * Math.acos(Math.min(1, Math.max(-1, dot)));
    };
    const ux = (x1p - cxp) / rx, uy = (y1p - cyp) / ry;
    const vx = (-x1p - cxp) / rx, vy = (-y1p - cyp) / ry;
    const theta = angle(1, 0, ux, uy);
    let sweepAngle = angle(ux, uy, vx, vy);
    if (!sweep && sweepAngle > 0) sweepAngle -= 2 * Math.PI;
    else if (sweep && sweepAngle < 0) sweepAngle += 2 * Math.PI;
    for (let s = 0; s <= STEPS; s++) {
        const t = theta + (sweepAngle * s) / STEPS;
        hit(cos * rx * Math.cos(t) - sin * ry * Math.sin(t) + cx,
            sin * rx * Math.cos(t) + cos * ry * Math.sin(t) + cy);
    }
}
