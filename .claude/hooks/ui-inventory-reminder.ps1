# Fires once per session, on the first front-end edit, and says where UI elements are catalogued.
# Marty's complaint, 12.08.2026: X/Bluesky were built in the Export modal and rebuilt into
# Settings -> Integrations (ADR-095) because nobody looked at where Telegram already lived.
$ErrorActionPreference = 'Stop'

try { $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { exit 0 }

$path = $payload.tool_input.file_path
if (-not $path -or $path -notmatch 'cedarclerk-web') { exit 0 }

$marker = Join-Path $env:TEMP ("cedar-ui-reminder-" + $payload.session_id + ".flag")
if (Test-Path $marker) { exit 0 }
New-Item -ItemType File -Path $marker -Force | Out-Null

$text = @'
Front-end edit: docs/UI-INVENTORY.md lists every existing UI element and where it lives. Before adding
a new button/field/panel/indicator, search it for the feature's existing home (sec-integrations, the
Export modal's steps, the Posts Manager tabs) and put the control there. Update the inventory in the
same commit — UiInventoryDriftTests fails the build for a page or sec-* section it does not mention.
Full rule: .claude/rules/ui-changes.md
'@

@{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; additionalContext = $text } } | ConvertTo-Json -Compress
