[CmdletBinding()]
param(
    [ValidateSet('Procgen', 'AgentQuality')]
    [string]$Bundle = 'Procgen'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vaultRoot = 'E:\Krea_tren_AI\Knowledge'
$sourcePath = Join-Path $vaultRoot '06_Sources\World_Genesis_Procgen_Research_2026-10-12.md'
$projectPath = Join-Path $vaultRoot '01_Project\World_Genesis.md'
$journalPath = Join-Path $vaultRoot '07_Journal\2026-10-12.md'
$indexPath = Join-Path $vaultRoot '00_HOME.md'
$sourceInput = Join-Path $projectRoot 'Docs\ObsidianPending\2026-10-12-procgen-source.md'
$entryInput = Join-Path $projectRoot 'Docs\ObsidianPending\2026-10-12-procgen-update.md'
$marker = '2026-10-12 02:15 +03:00 · World Genesis / исследование процедурной генерации'
$sourceLink = '[[06_Sources/World_Genesis_Procgen_Research_2026-10-12'
$indexEntry = '- 2026-10-12: [[06_Sources/World_Genesis_Procgen_Research_2026-10-12|World Genesis — формулы генерации, Unity skills и MCP; исследование и CPU-проверка]].'
$receiptRelative = 'Evidence\Research\2026-10-12\obsidian-sync.json'
if ($Bundle -eq 'AgentQuality') {
    $sourcePath = Join-Path $vaultRoot '06_Sources\World_Genesis_Agent_Quality_2026-10-12.md'
    $sourceInput = Join-Path $projectRoot 'Docs\ObsidianPending\2026-10-12-agent-quality-source.md'
    $entryInput = Join-Path $projectRoot 'Docs\ObsidianPending\2026-10-12-agent-quality-update.md'
    $marker = '2026-10-12 02:20 +03:00 · World Genesis / стандарт качества агента'
    $sourceLink = '[[06_Sources/World_Genesis_Agent_Quality_2026-10-12'
    $indexEntry = '- 2026-10-12: [[06_Sources/World_Genesis_Agent_Quality_2026-10-12|World Genesis — инструкции Anthropic/OpenAI и проверяемое сравнение качества агента]].'
    $receiptRelative = 'Evidence\Research\2026-10-12\agent-quality-obsidian-sync.json'
}
$utf8 = [Text.UTF8Encoding]::new($false)
$newline = [Environment]::NewLine

# One dated append-only bundle. No global sync, deletion, indexing service or monitor.
foreach ($path in @($projectPath, $journalPath, $indexPath, $sourceInput, $entryInput)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required file missing: $path" }
}
if (-not (Test-Path -LiteralPath (Split-Path $sourcePath) -PathType Container)) {
    throw 'Established source-card directory is missing.'
}
$sourceText = [IO.File]::ReadAllText($sourceInput)
$entryText = [IO.File]::ReadAllText($entryInput)
if (-not $entryText.Contains($marker)) { throw 'Prepared entry marker missing.' }

if (Test-Path -LiteralPath $sourcePath) {
    if ([IO.File]::ReadAllText($sourcePath) -ne $sourceText) {
        throw 'Existing source card differs. Preserve history and prepare a dated correction.'
    }
} else { [IO.File]::WriteAllText($sourcePath, $sourceText, $utf8) }

foreach ($path in @($projectPath, $journalPath)) {
    if (-not ([IO.File]::ReadAllText($path).Contains($marker))) {
        [IO.File]::AppendAllText($path, $newline + $newline + $entryText, $utf8)
    }
}
if (-not ([IO.File]::ReadAllText($indexPath).Contains($sourceLink))) {
    [IO.File]::AppendAllText($indexPath, $newline + $indexEntry + $newline, $utf8)
}
foreach ($path in @($projectPath, $journalPath)) {
    if (-not ([IO.File]::ReadAllText($path).Contains($entryText))) { throw "Entry readback failed: $path" }
}
if ([IO.File]::ReadAllText($sourcePath) -ne $sourceText) { throw 'Source readback failed.' }
if (-not ([IO.File]::ReadAllText($indexPath).Contains($sourceLink))) { throw 'Index readback failed.' }

$receipt = [ordered]@{
    status = 'synced-and-readback-verified'
    timestamp = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(3)).ToString('o')
    source = $sourcePath
    project = $projectPath
    journal = $journalPath
    index = $indexPath
    sourceSha256 = (Get-FileHash -LiteralPath $sourceInput -Algorithm SHA256).Hash
    entrySha256 = (Get-FileHash -LiteralPath $entryInput -Algorithm SHA256).Hash
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot $receiptRelative) -Encoding UTF8
Write-Output 'Research source card, project, journal and index written and readback verified.'
