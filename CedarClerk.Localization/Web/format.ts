// Interpolation for the handful of strings that need it: fmt(t().drafts.count, { n: 3 }).
export function fmt(template: string, params: Record<string, string | number>): string {
    return template.replace(/\{(\w+)\}/g, (whole, key) => String(params[key] ?? whole));
}
