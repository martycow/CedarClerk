param([int]$Count = 35)

# .claude/skills/tasks/scripts -> repo root is four levels up
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
$backlog = Join-Path $root 'docs\tasks\BACKLOG.md'
if (-not (Test-Path $backlog)) { Write-Error "docs/tasks/BACKLOG.md не найден: $backlog"; exit 1 }

$section = ''
$rows = @()
foreach ($line in Get-Content $backlog -Encoding UTF8) {
    if ($line -match '^##\s+(.+)$') { $section = $Matches[1].Trim(); continue }
    if ($line -notmatch '^\|\s*T-(\d+)\s*\|') { continue }
    $num = [int]$Matches[1]

    # Trim outer pipes, then split; a row can carry stray pipes inside the
    # description (T-145 does today), so only the first four cells are trusted.
    $cells = ($line.Trim().Trim('|') -split '\|')
    if ($cells.Count -lt 4) { continue }
    $name = $cells[1].Trim()
    $prio = $cells[2].Trim()
    if ($name -match '~~' -or $prio -match 'Сделано|Снят|Закрыт|Моот') { continue }

    $rows += [pscustomobject]@{
        Num     = $num
        Id      = "T-$num"
        Name    = ($name -replace '\*\*', '')
        Prio    = ($prio -replace '\*\*', '')
        Tags    = $cells[3].Trim()
        Section = ($section -replace '\s*\(.*\)$', '')
    }
}

$open = $rows | Sort-Object Num -Descending
$show = $open | Select-Object -First $Count

'| ID | Имя | Приоритет | Теги | Раздел |'
'|---|---|---|---|---|'
foreach ($r in $show) { "| $($r.Id) | $($r.Name) | $($r.Prio) | $($r.Tags) | $($r.Section) |" }
''
"Открытых T-строк: $($open.Count), показано $($show.Count) — новые сверху, по номеру ID. Полные описания: docs/tasks/BACKLOG.md"
