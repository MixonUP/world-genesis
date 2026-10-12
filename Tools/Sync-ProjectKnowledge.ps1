[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vaultRoot = 'E:\Krea_tren_AI\Knowledge'
$cardPath = Join-Path $vaultRoot '01_Project\World_Genesis.md'
$journalPath = Join-Path $vaultRoot '07_Journal\2026-10-12.md'
$indexPath = Join-Path $vaultRoot '00_HOME.md'
$cardSource = Join-Path $projectRoot 'Docs\ObsidianPending\2026-10-12-project-card.md'
$journalSource = Join-Path $projectRoot 'Docs\ObsidianPending\2026-10-12-journal-entry.md'
$utf8 = [Text.UTF8Encoding]::new($false)
$marker = '2026-10-12 01:52 +03:00'

# This script is only the idempotent receipt sync for the dated setup operation.
# It never replaces history or synchronizes arbitrary folders.
foreach ($directory in @($vaultRoot, (Split-Path $cardPath), (Split-Path $journalPath))) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Required vault directory missing: $directory" }
}
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) { throw 'Knowledge index missing; do not create a replacement vault.' }
$cardText = [IO.File]::ReadAllText($cardSource)
$journalText = [IO.File]::ReadAllText($journalSource)

if (-not (Test-Path -LiteralPath $cardPath)) {
    [IO.File]::WriteAllText($cardPath, $cardText, $utf8)
} elseif (-not ([IO.File]::ReadAllText($cardPath).Contains($marker))) {
    $entryStart = $cardText.IndexOf('## 2026-10-12')
    if ($entryStart -lt 0) { throw 'Dated section absent from prepared project card.' }
    [IO.File]::AppendAllText($cardPath, "`n`n" + $cardText.Substring($entryStart), $utf8)
}
if (-not (Test-Path -LiteralPath $journalPath)) {
    [IO.File]::WriteAllText($journalPath, "# Журнал — 2026-10-12`n`n", $utf8)
}
if (-not ([IO.File]::ReadAllText($journalPath).Contains('World Genesis / организация Unity-проекта'))) {
    [IO.File]::AppendAllText($journalPath, "`n" + $journalText, $utf8)
}
if (-not ([IO.File]::ReadAllText($indexPath).Contains('[[01_Project/World_Genesis'))) {
    [IO.File]::AppendAllText($indexPath, "`n- 2026-10-12: [[01_Project/World_Genesis|World Genesis — организация отдельного Unity-проекта по референсу WorldBox]].`n", $utf8)
}
if (-not ([IO.File]::ReadAllText($cardPath).Contains($marker))) { throw 'Card readback failed.' }
if (-not ([IO.File]::ReadAllText($journalPath).Contains('World Genesis / организация Unity-проекта'))) { throw 'Journal readback failed.' }
if (-not ([IO.File]::ReadAllText($indexPath).Contains('[[01_Project/World_Genesis'))) { throw 'Index readback failed.' }

$receipt = [ordered]@{
    status = 'synced-and-readback-verified'
    timestamp = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(3)).ToString('o')
    card = $cardPath
    journal = $journalPath
    index = $indexPath
    cardSourceSha256 = (Get-FileHash -LiteralPath $cardSource -Algorithm SHA256).Hash
    journalSourceSha256 = (Get-FileHash -LiteralPath $journalSource -Algorithm SHA256).Hash
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'Evidence\obsidian-sync.json') -Encoding UTF8
Write-Output 'Obsidian card, journal and index appended and read back successfully.'
