// Phase 8 Step 8 (docs/ROADMAP.md) — neither AI provider streams a response today (see the ADR
// following ADR-035, docs/DECISIONS.md), so there's no real token-by-token progress to report.
// This asymptotic curve reads as much more "alive" than a flat elapsed-second counter: fast
// growth early, slowing down over time, capped at 90% so it never falsely claims completion —
// the caller jumps the displayed value to 100% only once the real response actually arrives.
const CAP_PERCENT = 90;
const TAU_SECONDS = 20;

export function pseudoProgress(elapsedSeconds: number, tau = TAU_SECONDS): number {
    if (elapsedSeconds <= 0) return 0;
    return Math.round(CAP_PERCENT * (1 - Math.exp(-elapsedSeconds / tau)));
}
