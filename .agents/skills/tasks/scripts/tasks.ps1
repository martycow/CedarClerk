param([int]$Count = 35)

# .agents/skills/tasks/scripts -> repo root is four levels up
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
$backlog = Join-Path $root 'docs\tasks\BACKLOG.md'
if (-not (Test-Path $backlog)) { Write-Error "docs/tasks/BACKLOG.md not found: $backlog"; exit 1 }

# BACKLOG.md's canonical line: - [ ] T-xxx Name — description #tag1 #tag2 P1..P3
$section = ''
$rows = @()
foreach ($line in Get-Content $backlog -Encoding UTF8) {
    if ($line -match '^##\s+(.+)$') { $section = $Matches[1].Trim(); continue }
    if ($line -notmatch '^-\s*\[([ xX?>])\]\s*T-(\d+)\s+(.+)$') { continue }
    $done = $Matches[1] -in @('x', 'X')
    if ($done) { continue }
    $num = [int]$Matches[2]
    $rest = $Matches[3]

    $name = ($rest -split ' — ', 2)[0].Trim()
    $tags = ([regex]::Matches($rest, '#\S+') | ForEach-Object { $_.Value }) -join ' '
    $prioMatch = [regex]::Match($rest, '\bP[0-3]\b')
    $prio = if ($prioMatch.Success) { $prioMatch.Value } else { '' }

    $rows += [pscustomobject]@{
        Num     = $num
        Id      = "T-$num"
        Name    = ($name -replace '\*\*', '')
        Prio    = $prio
        Tags    = $tags
        Section = ($section -replace '\s*\(.*\)$', '')
    }
}

$open = $rows | Sort-Object Num -Descending
$show = $open | Select-Object -First $Count

'| ID | Name | Priority | Tags | Section |'
'|---|---|---|---|---|'
foreach ($r in $show) { "| $($r.Id) | $($r.Name) | $($r.Prio) | $($r.Tags) | $($r.Section) |" }
''
"Open T-rows: $($open.Count), showing $($show.Count) — newest first, by ID number. Full descriptions: docs/tasks/BACKLOG.md"
