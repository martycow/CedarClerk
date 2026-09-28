#!/usr/bin/env bash
# Fires once per session, on the first front-end edit, and says where UI elements are catalogued.
# X/Bluesky were built in the Export modal and rebuilt into Settings -> Integrations (ADR-095)
# because nobody looked at where Telegram already lived.
payload=$(cat) || exit 0
path=$(jq -r '.tool_input.file_path // empty' <<<"$payload" 2>/dev/null) || exit 0
[[ "$path" == *cedarclerk-web* ]] || exit 0

session=$(jq -r '.session_id // "none"' <<<"$payload")
marker="${TMPDIR:-/tmp}/cedar-ui-reminder-$session.flag"
[[ -e "$marker" ]] && exit 0
touch "$marker"

text='Front-end edit: docs/design/UI-INVENTORY.md lists every existing UI element and where it lives. Before adding a new button/field/panel/indicator, search it for the feature'"'"'s existing home (sec-integrations, the Export modal'"'"'s steps, the Posts Manager tabs) and put the control there. Update the inventory in the same commit — UiInventoryDriftTests fails the build for a page or sec-* section it does not mention. Full rule: .claude/rules/ui-changes.md'

jq -nc --arg t "$text" '{hookSpecificOutput:{hookEventName:"PreToolUse",additionalContext:$t}}'
