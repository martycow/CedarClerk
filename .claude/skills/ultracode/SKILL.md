---
name: ultracode
description: Fleet dispatcher for Cedar Clerk — routes a task through the agent fleet (tech-lead contract → parallel tech lanes with disjoint file zones → tester → project-manager). Invoke as /ultracode [task].
disable-model-invocation: true
argument-hint: [task]
allowed-tools: Read, Grep, Glob, Agent, Edit, Write, Bash
---

# ultracode — fleet dispatcher

Orchestrate the task in `$ARGUMENTS` through the Cedar Clerk agent fleet. You
dispatch and integrate; the agents do the work. (Borrowed from Cowtext and adapted:
the fleet definitions in `.claude/agents/` are Cowtext-managed and untracked —
never commit or rewrite them from here.)

## Procedure

0. **Read the fleet state first**: `docs/fleet/README.md`, plus `ROSTER.md` /
   `ACTIVITY_LOG.md` in the same folder when they exist (they may not yet — do
   not create them just to have them).

1. **Pick the MINIMAL set of agents** for the task. Not every task needs the full
   fleet — a one-lane change needs one tech agent, tester, and project-manager.

2. **List the agents you are NOT launching in a single line**:
   `Idle by scope: <names>`. Idle by scope is not laziness and is never flagged.

3. **Order of battle**:
   - `tech-lead` first — writes the frozen contract, but only if the task spans
     more than one file or touches module boundaries.
   - `tech-general` ‖ `tech-ui` — in parallel, each with a file zone that
     overlaps no other lane. Natural Cedar Clerk cut: `tech-general` = C#
     (`CedarClerk.Core/Server/Localization/Cli` + tests), `tech-ui` =
     `cedarclerk-web/` and the blog's server-rendered CSS. Launch several
     `tech-general` instances if the core work itself splits into disjoint zones.
     (`tech-barn` is a Cowtext lane — it has no zone here; leave it idle.)
   - `tester` — runs `cedar test`; new UI additionally passes the guards
     (UiInventoryDrift, ErrorMessageLocalization, SchemaDriftGuard, DocsFlowGraph).
   - `project-manager` — always.

4. **Every agent's prompt must carry**: the goal, its FILE ZONE (exact paths it
   may touch — leaving the zone is forbidden), the acceptance criteria, and the
   binding project rules for that zone (`.claude/rules/*.md`, ADR-before-code,
   commit style: 3–4 words, no trailers).

5. **Conflict resolution**: architecture and module boundaries — `tech-lead`'s
   verdict wins; interface matters — `tech-ui`'s verdict wins.

6. **The final agent is always `project-manager`** — it records the session per
   Cedar Clerk convention: `docs/tasks/CHANGELOG.md` section, board rows
   closed/added in `docs/tasks/BACKLOG.md`, new terms into
   `docs/knowledge_base/TERMINOLOGY.md`.

## Rules

- Zones never overlap. If two lanes need the same file, re-cut the zones or
  serialize the lanes — never let both write it.
- Relay each agent's final report; confirmed defects go back to the owning lane,
  not fixed by the dispatcher.
- `product-analyst` is OUTSIDE this pipeline — never launched by ultracode.
- Deploy and anything on the droplet stay out of every lane — writing commands
  on production are handed to the maintainer (`.claude/rules/production-environment.md`).
